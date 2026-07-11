using Godot;
using Scavengineers.Scripts.Inventory;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// One slot of the inventory panel (Scenes/Player.tscn's HUD/InventoryPanel) — shows whichever
/// item (if any) currently occupies <see cref="SlotIndex"/> in <see cref="Inventory"/>, and
/// participates in Godot's native Control drag-and-drop so slots can be reordered/merged by
/// mouse. <see cref="Inventory"/> is wired by Player.cs after it finds these slots in its own
/// _Ready(), not via [Export] — PlayerInventory is a plain C# class, not a Resource/Node, so
/// the editor can't reference it directly.
/// </summary>
public partial class InventorySlotUI : Control
{
    [Export]
    public int SlotIndex { get; set; }

    /// <summary>The panel's one shared tooltip label — every slot points at the same node.</summary>
    [Export]
    public Label? Tooltip { get; set; }

    public PlayerInventory? Inventory { get; set; }

    private ColorRect? _icon;
    private Label? _countLabel;

    public override void _Ready()
    {
        _icon = GetNode<ColorRect>("Icon");
        _countLabel = GetNode<Label>("Count");

        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        if (_icon is null || _countLabel is null)
        {
            return;
        }

        if (CurrentSlot() is not { } occupied)
        {
            _icon.Visible = false;
            _countLabel.Visible = false;
            return;
        }

        _icon.Visible = true;
        _icon.Color = ItemCatalog.Color(occupied.ItemId);
        // Skip the redundant "1" on a lone item — only worth showing once there's a real stack.
        _countLabel.Visible = occupied.Count > 1;
        _countLabel.Text = occupied.Count.ToString();
    }

    private void OnMouseEntered()
    {
        if (Tooltip is null || CurrentSlot() is not { } slot)
        {
            return;
        }

        Tooltip.Text = $"{Tr("ITEM_" + slot.ItemId.ToUpperInvariant())}: {slot.Count}";
        Tooltip.Visible = true;
        Tooltip.GlobalPosition = GetGlobalMousePosition() + new Vector2(16, 16);
    }

    private void OnMouseExited()
    {
        if (Tooltip is not null)
        {
            Tooltip.Visible = false;
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (CurrentSlot() is not { } slot)
        {
            return default;
        }

        var preview = new Control();
        preview.AddChild(new ColorRect { Color = ItemCatalog.Color(slot.ItemId), Size = Size });
        SetDragPreview(preview);

        return SlotIndex;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => data.VariantType == Variant.Type.Int;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Inventory is null || data.VariantType != Variant.Type.Int)
        {
            return;
        }

        Inventory.MoveSlot(data.AsInt32(), SlotIndex);
    }

    private (string ItemId, int Count)? CurrentSlot() =>
        Inventory is { } inv && SlotIndex >= 0 && SlotIndex < inv.Slots.Count ? inv.Slots[SlotIndex] : null;
}
