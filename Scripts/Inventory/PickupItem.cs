using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;

namespace Scavengineers.Scripts.Inventory;

public partial class PickupItem : RigidBody3D, IVerbTarget, IPhysicsPresenceAware
{
    private static readonly Verb PickUpVerb = new("pick_up", "VERB_PICK_UP", DurationSeconds: 0f);

    // Placeholder/tunable — with zero gravity in a breached room (see ShipAtmosphereZone), gravity
    // no longer settles out any stray physics nudge (e.g. the floor's own collision finishing a
    // frame after this item spawns — see ShipBuildTarget.GenerateFloorCeilingPanels's deferred
    // build). Undamped, that nudge would drift at a constant velocity forever, and a room can sit
    // unobserved (both ships simulate simultaneously) for however long it takes the player to
    // arrive — long enough to drift clear out through an open hull breach. Damping bounds the
    // *total* distance a stray nudge can ever travel to a small, fixed amount regardless of how
    // much time passes. Kept low so an intentional player shove still carries and glides for a
    // couple of seconds rather than stopping dead — 2f killed that momentum almost instantly.
    private const float ZeroGSettleDamp = 0.4f;

    // Mirrors Player.cs's own DecompressionPullRange/DecompressionPullAcceleration exactly —
    // duplicated rather than shared, matching this codebase's existing duplication convention
    // between PickupItem and ContainerPickupItem (see ZeroGSettleDamp above).
    private const float DecompressionPullRange = 5f;
    private const float DecompressionPullAcceleration = 4f;

    // Once this close to an active breach, the item has effectively reached the hole — eject it
    // rather than let it keep accelerating into the hull geometry forever.
    private const float BreachEjectDistance = 0.3f;

    // How far past the breach, along the same pull direction, the ejected item comes to rest —
    // just enough to clear the hull opening. Not a real "outside the ship" simulation (EVA/
    // free-float movement is deferred — see docs/project-plan.md) — forward-compatible
    // groundwork only: a frozen, physically-present marker for whenever EVA recovery ships later.
    private const float BreachEjectOffset = 1f;

    // Mirrors Player.cs's own DurableToolIds exactly — duplicated rather than shared, matching
    // this codebase's existing duplication convention between PickupItem and Player (see
    // ZeroGSettleDamp/DecompressionPullRange above).
    private static readonly string[] DurableToolIds = ["crowbar", "power_drill", "wrench"];

    private bool _hasSettled;
    private bool _ejected;

    [Export]
    public string ItemId { get; set; } = "";

    [Export]
    public int Count { get; set; } = 1;

    /// <summary>0-1; meaningless except for ItemId == "battery" — same shape as
    /// PlayerInventory.SpecializedSlot.Charge and SlotContainer's own per-slot Charge, just
    /// carried on the loose world item instead.</summary>
    [Export]
    public float Charge { get; set; } = 1f;

    public override void _Ready()
    {
        var shapeKind = ItemCatalog.ShapeKind(ItemId);
        AddChild(ItemVisualBuilder.BuildVisual(ItemId, shapeKind));
        AddChild(new CollisionShape3D { Shape = ItemVisualBuilder.BuildCollisionShape(shapeKind) });

        LinearDamp = ZeroGSettleDamp;
        AngularDamp = ZeroGSettleDamp;

        // Starts frozen for exactly one physics tick — matching the old StaticBody3D behavior
        // (immovable, no physics response at all) just long enough to dodge a one-time startup
        // race: on the very first frame a ship's scene loads, a sibling's floor CollisionShape3D
        // may not exist yet, since ShipBuildTarget.GenerateFloorCeilingPanels builds it via
        // CallDeferred from its own _Ready(). By the time this item's own first _PhysicsProcess
        // fires, Godot has already fully flushed that frame's deferred-call queue (same guarantee
        // ShipSim.SeedVacuumFromInitialBreaches relies on), so it's safe to unfreeze there once
        // and never touch Freeze again — real physics (gravity, friction, player pushes) from
        // then on, regardless of room pressurization.
        Freeze = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_ejected)
        {
            return; // permanently inert — SetPhysicsProcess(false) already stops the engine from
                     // calling this on its own, but guard here too against a direct manual call.
        }

        if (!_hasSettled)
        {
            _hasSettled = true;
            Freeze = false;
            return; // one-time startup-race dodge only (see _Ready's own doc comment) — breach-
                    // pull logic begins the following tick, once real physics has taken over.
        }

        UpdateBreachPull(delta);
    }

    /// <summary>Extends Player.cs's own decompression-pull hazard (see its _PhysicsProcess) to
    /// loose items too — a breach doesn't just pull the player, it pulls anything unsecured
    /// nearby. Reuses the exact same FindZoneAt/ActiveBreachPositions building blocks, applied as
    /// LinearVelocity instead of the player's kinematic velocity. Not gated on anything
    /// item-agency-related, matching Player's own "isn't something you opt into" framing. Full
    /// DecompressionPullAcceleration applies at a constant rate any time inZeroG is true — NOT
    /// scaled by the room's current Pressure (see Player.cs's own matching comment): whole-
    /// component venting drives Pressure to near-zero within about a second and it never
    /// recovers, so a Pressure-scaled pull would decay to an imperceptible force almost
    /// immediately.</summary>
    private void UpdateBreachPull(double delta)
    {
        var zone = ShipAtmosphereZone.FindZoneAt(GetWorld3D(), GlobalPosition);
        if (zone?.BuildTargetRef is not { } buildTarget)
        {
            return; // no room found, or this ship has no floor/ceiling/wall breach tracking
                    // (only the Home Ship does today — see ShipAtmosphereZone.BuildTargetRef)
        }

        var tile = zone.TileAt(GlobalPosition);
        var cell = new CellCoord(tile.X, tile.Y);
        var roomVolume = zone.ShipSimRef?.VolumeAt(cell);
        var inZeroG = (roomVolume?.O2Fraction ?? 0.21) <= ShipAtmosphereZone.ZeroGO2Threshold;
        if (!inZeroG)
        {
            return;
        }

        // A ship with no life support (e.g. the Derelict) never regenerates air, so a room's
        // O2Fraction can stay at "reads as vacuum" forever even after its own breach is patched
        // and it's properly sealed off again (see Player.cs's own matching comment) — without
        // this check, an item in that now-sealed room would still get pulled toward some other,
        // unrelated breach elsewhere on the ship (even behind a closed, sealed door) just because
        // it's within raw range of ActiveBreachPositions(), which lists every breach on the ship.
        if (!(zone.ShipSimRef?.Atmosphere?.IsConnectedToOutside(cell) ?? false))
        {
            return;
        }

        foreach (var breachPosition in buildTarget.ActiveBreachPositions())
        {
            // Same reasoning as Player.cs's own matching check: atmosphere connectivity can span
            // multiple rooms through an open (unsealed) door, so a breach can be "connected" from
            // here while still being physically in a DIFFERENT room. Only pull/eject toward a
            // breach actually inside the same room/zone this item is currently in.
            if (!ReferenceEquals(ShipAtmosphereZone.FindZoneAt(GetWorld3D(), breachPosition), zone))
            {
                continue;
            }

            var toBreach = breachPosition - GlobalPosition;
            var distance = toBreach.Length();

            if (distance < BreachEjectDistance)
            {
                Eject(breachPosition, toBreach);
                return; // ejected — stop scanning any other breaches this tick
            }

            if (distance > DecompressionPullRange)
            {
                continue;
            }

            LinearVelocity += toBreach.Normalized() * DecompressionPullAcceleration * (float)delta;
        }
    }

    /// <summary>Comes to rest just past the breach along the same direction it was already being
    /// pulled — deliberately not full physics (no "outside the hull" space exists yet, EVA is
    /// deferred): a frozen, inert marker only, forward-compatible groundwork for future EVA
    /// recovery. Reusing the pull direction (rather than computing a hull normal) handles
    /// floor/ceiling/wall breaches uniformly for free.</summary>
    private void Eject(Vector3 breachPosition, Vector3 toBreach)
    {
        _ejected = true;
        var direction = toBreach.LengthSquared() > 0.0001f ? toBreach.Normalized() : Vector3.Up;
        GlobalPosition = breachPosition + direction * BreachEjectOffset;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Freeze = true;
        SetPhysicsProcess(false); // permanently inert from here on
    }

    /// <summary>Called by TravelConsoleVerbTarget.SetShipPresence when this item's ship stops or
    /// starts being physically present. Not present: freeze immediately and zero out any
    /// velocity — nothing exists underneath it anymore once the ship's collision is disabled, so
    /// a live body would fall forever. Present again: re-arm the exact same one-tick startup
    /// grace _Ready uses (freeze, then unfreeze on the next physics tick), since the ship's
    /// collision was just re-enabled and deserves the same one-frame settle this item already
    /// trusts at initial spawn. A no-op once ejected — permanently inert scenery at that point,
    /// though in practice the Home Ship (the only ship breaches/ejection apply to) never actually
    /// has its presence toggled, so this is cheap insurance rather than a live requirement.</summary>
    public void SetPhysicsPresence(bool present)
    {
        if (_ejected)
        {
            return;
        }

        Freeze = true;

        if (present)
        {
            _hasSettled = false; // re-arm the one-tick startup grace, same as on initial spawn
            SetPhysicsProcess(true);
        }
        else
        {
            LinearVelocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
            SetPhysicsProcess(false);
        }
    }

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [PickUpVerb];

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public string? DisplayNameKey => "ITEM_" + ItemId.ToUpperInvariant();

    /// <summary>Charge means durability for a durable tool sitting loose on the ground — same
    /// "same field, different meaning per item" pattern Charge already carries for a battery's
    /// charge fraction (see PlayerInventory.DamageToolInHand's own doc comment for the held-tool
    /// side of this). Null for anything else, matching IVerbTarget's default.</summary>
    public float? Condition => DurableToolIds.Contains(ItemId) ? Charge : null;

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != PickUpVerb.Id)
        {
            return;
        }

        // Only the amount that actually fits is picked up — the rest stays right here rather
        // than vanishing, no special handling needed since this object already IS the world
        // representation of "some of this item is sitting here" (unlike a refund/scrap yield,
        // which has no such object to fall back to — see InventoryOverflow).
        var added = inventory.Add(ItemId, Count, Charge);
        Count -= added;
        if (Count <= 0)
        {
            QueueFree();
        }
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }
}
