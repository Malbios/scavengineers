using System.Collections.Generic;

using Godot;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Inventory;

public partial class PickupItem : RigidBody3D, IVerbTarget
{
    private static readonly Verb PickUpVerb = new("pick_up", "VERB_PICK_UP", DurationSeconds: 0f);

    // Placeholder/tunable — with zero gravity in a breached room (see ShipAtmosphereZone), gravity
    // no longer settles out any stray physics nudge (e.g. the floor's own collision finishing a
    // frame after this item spawns — see ShipBuildTarget.GenerateFloorCeilingPanels's deferred
    // build). Undamped, that nudge would drift at a constant velocity forever, and a room can sit
    // unobserved (both ships simulate simultaneously) for however long it takes the player to
    // arrive — long enough to drift clear out through an open hull breach. Damping bounds the
    // *total* distance a stray nudge can ever travel to a small, fixed amount regardless of how
    // much time passes, while still leaving a felt, gentle float rather than snapping to a stop.
    private const float ZeroGSettleDamp = 2f;

    [Export]
    public string ItemId { get; set; } = "";

    [Export]
    public int Count { get; set; } = 1;

    public override void _Ready()
    {
        LinearDamp = ZeroGSettleDamp;
        AngularDamp = ZeroGSettleDamp;
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
