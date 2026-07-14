using System.Collections.Generic;

using Godot;
using Scavengineers.Scripts.Verbs;

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

    [Export]
    public string ItemId { get; set; } = "";

    [Export]
    public int Count { get; set; } = 1;

    public override void _Ready()
    {
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
        Freeze = false;
        SetPhysicsProcess(false); // one-time — nothing else to do once past the startup race
    }

    /// <summary>Called by TravelConsoleVerbTarget.SetShipPresence when this item's ship stops or
    /// starts being physically present. Not present: freeze immediately and zero out any
    /// velocity — nothing exists underneath it anymore once the ship's collision is disabled, so
    /// a live body would fall forever. Present again: re-arm the exact same one-tick startup
    /// grace _Ready uses (freeze, then unfreeze on the next physics tick), since the ship's
    /// collision was just re-enabled and deserves the same one-frame settle this item already
    /// trusts at initial spawn.</summary>
    public void SetPhysicsPresence(bool present)
    {
        Freeze = true;

        if (present)
        {
            SetPhysicsProcess(true);
        }
        else
        {
            LinearVelocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
            SetPhysicsProcess(false); // don't let the one-shot _PhysicsProcess undo this freeze
        }
    }

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [PickUpVerb];

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public string? DisplayNameKey => "ITEM_" + ItemId.ToUpperInvariant();

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
        var added = inventory.Add(ItemId, Count);
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
