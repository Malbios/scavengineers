using Godot;

namespace Scavengineers.Scripts.Inventory;

/// <summary>Spawns a generic dropped-item pickup at another node's own position — for a verb that
/// creates an item as a side effect (a refund, a scrap yield) with no existing world object to
/// fall back to.</summary>
public static class InventoryOverflow
{
    public static void DropAt(Node3D near, string itemId, int count, Vector3? position = null, float charge = 1f)
    {
        var pickup = new PickupItem { ItemId = itemId, Count = count, Charge = charge };

        // Parented at the world/ship root, not under `near` — `near` is the producing verb target
        // (a ShipBuildTarget, a damaged conduit), and a RigidBody3D meant to drift/settle
        // independently must not be parented under something whose transform could change or that
        // could later be freed.
        near.GetParent()?.AddChild(pickup);
        pickup.GlobalPosition = position ?? near.GlobalPosition;
    }
}
