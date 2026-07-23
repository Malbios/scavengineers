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

    // Placeholder/tunable — without damping, a stray physics nudge in a zero-g breached room
    // (e.g. the floor's collision finishing a frame late) would drift at constant velocity
    // forever, potentially clear out through the breach by the time an unobserved room is next
    // seen. Kept low enough that an intentional player shove still glides for a couple of seconds.
    private const float ZeroGSettleDamp = 0.4f;

    // Mirrors Player.cs's own DecompressionPull constants — duplicated, not shared.
    private const float DecompressionPullRange = 5f;
    private const float DecompressionPullAcceleration = 4f;

    // This close to an active breach, the item has reached the hole — eject it rather than let it
    // keep accelerating into the hull geometry.
    private const float BreachEjectDistance = 0.3f;

    // How far past the breach the ejected item comes to rest. Not a real "outside the ship"
    // simulation (EVA is deferred) — a frozen, physically-present marker for future EVA recovery.
    private const float BreachEjectOffset = 1f;

    // Mirrors Player.cs's own DurableToolIds — duplicated, not shared.
    private static readonly string[] DurableToolIds = ["crowbar", "power_drill", "wrench"];

    private bool _hasSettled;
    private bool _ejected;

    [Export]
    public string ItemId { get; set; } = "";

    [Export]
    public int Count { get; set; } = 1;

    /// <summary>0-1; meaningless except for ItemId == "battery".</summary>
    [Export]
    public float Charge { get; set; } = 1f;

    /// <summary>Empty for every ordinary drop — set only by ShipBuildTarget.PlaceMissionItem, to
    /// the owning ship's SaveId, so SaveManager can recreate a still-outstanding contract item on
    /// load without re-deriving it from Contract state.</summary>
    [Export]
    public string MissionOwnerSaveId { get; set; } = "";

    public override void _Ready()
    {
        var shapeKind = ItemCatalog.ShapeKind(ItemId);
        AddChild(ItemVisualBuilder.BuildVisual(ItemId, shapeKind));
        AddChild(new CollisionShape3D { Shape = ItemVisualBuilder.BuildCollisionShape(shapeKind) });

        if (MissionOwnerSaveId != "")
        {
            AddToGroup("mission_item");
        }

        LinearDamp = ZeroGSettleDamp;
        AngularDamp = ZeroGSettleDamp;

        // Starts frozen for one physics tick to dodge a startup race: a sibling's floor
        // CollisionShape3D may not exist yet on the first frame a ship's scene loads, since
        // ShipBuildTarget.GenerateFloorCeilingPanels builds it via CallDeferred. By this item's
        // first _PhysicsProcess, that deferred queue has flushed, so it's safe to unfreeze once
        // and never touch Freeze again from here.
        Freeze = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_ejected)
        {
            return;
        }

        if (!_hasSettled)
        {
            _hasSettled = true;
            Freeze = false;
            return;
        }

        UpdateBreachPull(delta);
    }

    /// <summary>Extends Player.cs's decompression-pull hazard to loose items too. Full
    /// DecompressionPullAcceleration applies at a constant rate any time inZeroG is true — NOT
    /// scaled by the room's current Pressure, which whole-component venting drives to near-zero
    /// within about a second (a Pressure-scaled pull would decay to nothing almost immediately).</summary>
    private void UpdateBreachPull(double delta)
    {
        var zone = ShipAtmosphereZone.FindZoneAt(GetWorld3D(), GlobalPosition);
        if (zone?.BuildTargetRef is not { } buildTarget)
        {
            return;
        }

        var tile = zone.TileAt(GlobalPosition);
        var cell = new CellCoord(tile.X, tile.Y);
        var roomVolume = zone.ShipSimRef?.VolumeAt(cell);
        var inZeroG = (roomVolume?.O2Fraction ?? 0.21) <= ShipAtmosphereZone.ZeroGO2Threshold;
        if (!inZeroG)
        {
            return;
        }

        // A ship with no life support never regenerates air, so a room's O2Fraction can stay at
        // "reads as vacuum" even after its own breach is patched and resealed — without this
        // check, an item there would still get pulled toward an unrelated breach elsewhere on the
        // ship just for being in raw range of ActiveBreachPositions().
        if (!(zone.ShipSimRef?.Atmosphere?.IsConnectedToOutside(cell) ?? false))
        {
            return;
        }

        foreach (var breachPosition in buildTarget.ActiveBreachPositions())
        {
            // Atmosphere connectivity can span rooms through an open door, so a breach can read
            // as "connected" while being physically in a different room. Only pull/eject toward
            // one actually in the same zone as this item.
            if (!ReferenceEquals(ShipAtmosphereZone.FindZoneAt(GetWorld3D(), breachPosition), zone))
            {
                continue;
            }

            var toBreach = breachPosition - GlobalPosition;
            var distance = toBreach.Length();

            if (distance < BreachEjectDistance)
            {
                Eject(breachPosition, toBreach);
                return;
            }

            if (distance > DecompressionPullRange)
            {
                continue;
            }

            LinearVelocity += toBreach.Normalized() * DecompressionPullAcceleration * (float)delta;
        }
    }

    /// <summary>Reuses the pull direction (rather than computing a hull normal) so floor/ceiling/
    /// wall breaches are handled uniformly. Not full physics — EVA/"outside the hull" is deferred,
    /// this is just a frozen, physically-present marker for future EVA recovery.</summary>
    private void Eject(Vector3 breachPosition, Vector3 toBreach)
    {
        _ejected = true;
        var direction = toBreach.LengthSquared() > 0.0001f ? toBreach.Normalized() : Vector3.Up;
        GlobalPosition = breachPosition + direction * BreachEjectOffset;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Freeze = true;
        SetPhysicsProcess(false);
    }

    /// <summary>Not present: freeze and zero velocity, since nothing exists underneath once the
    /// ship's collision is disabled. Present again: re-arm the same one-tick startup grace
    /// <see cref="_Ready"/> uses. A no-op once ejected.</summary>
    public void SetPhysicsPresence(bool present)
    {
        if (_ejected)
        {
            return;
        }

        Freeze = true;

        if (present)
        {
            _hasSettled = false;
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

    /// <summary>Charge means durability for a durable tool sitting loose on the ground. Null for
    /// anything else.</summary>
    public float? Condition => DurableToolIds.Contains(ItemId) ? Charge : null;

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != PickUpVerb.Id)
        {
            return;
        }

        // Only the amount that actually fits is picked up — the rest stays right here.
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
