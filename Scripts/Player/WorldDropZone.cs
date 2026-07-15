using Godot;

namespace Scavengineers.Scripts.Player;

/// <summary>Full-screen catch-all drop target living behind every inventory panel/window under
/// HUD — receives a drag only when it's released somewhere none of those already claimed it (see
/// Scenes/Player.tscn child order: this node sits earlier than InventoryPanel/DrillWindow/
/// FlashlightWindow/BackpackWindow, so they get first refusal). Only visible while the inventory
/// panel itself is open (see Player.OpenInventory/CloseInventory), matching every other panel's
/// gating. Drill/flashlight battery slots are supported too (see Player.TryDropInWorld) — the
/// tool loses its battery exactly like ejecting onto a slot does, and the battery keeps its real
/// charge in the world, same as any other ejected battery.</summary>
public partial class WorldDropZone : Control
{
    public Player? PlayerRef { get; set; }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.AsGodotObject() is InventorySlotUI;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.AsGodotObject() is InventorySlotUI source)
        {
            PlayerRef?.TryDropInWorld(source, atPosition);
        }
    }
}
