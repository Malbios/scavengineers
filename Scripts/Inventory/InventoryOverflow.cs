using Godot;

namespace Scavengineers.Scripts.Inventory;

/// <summary>
/// Spawns a generic dropped-item pickup at another node's own position — the "nothing vanishes"
/// half of the inventory arc's Stage 1 capacity work (see PlayerInventory.Add's partial-add
/// return). Used wherever a verb creates an item as a side effect (a refund, a scrap yield) with
/// no existing world object to fall back to, unlike picking up something that's already sitting in
/// the world (see PickupItem's own partial-pickup handling).
/// </summary>
public static class InventoryOverflow
{
    public static void DropAt(Node3D near, string itemId, int count, Mesh mesh, Shape3D shape, Material? material, Vector3? position = null)
    {
        var pickup = new PickupItem { ItemId = itemId, Count = count };

        // Parented at the world/ship root (mirrors Player.SpawnDroppedContainer), not under
        // `near` itself — `near` is the producing verb target (a ShipBuildTarget, a damaged
        // conduit), and a RigidBody3D meant to drift/settle independently must not be parented
        // under something whose own transform could change or that could later be freed.
        near.GetParent()?.AddChild(pickup);
        // `position` lets a caller with an already-resolved world position (e.g. a raycast hit
        // point) use it instead of dropping at `near`'s own position.
        pickup.GlobalPosition = position ?? near.GlobalPosition;

        var meshInstance = new MeshInstance3D { Mesh = mesh };
        meshInstance.SetSurfaceOverrideMaterial(0, ItemCatalog.TintedMaterial(itemId, material));
        pickup.AddChild(meshInstance);

        pickup.AddChild(new CollisionShape3D { Shape = shape });
    }
}
