using Godot;
using Scavengineers.Scripts.Inventory;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// One slot of the inventory panel (Scenes/Player.tscn's HUD/InventoryPanel) — shows whichever
/// item (if any) currently occupies <see cref="SlotIndex"/> in <see cref="Container"/>, and
/// participates in Godot's native Control drag-and-drop so slots can be reordered/merged by
/// mouse. Also doubles as the one special "Back" slot (<see cref="IsBackSlot"/>) representing
/// the equipped backpack itself — a non-fungible item with no ordinary (itemId, count) slot
/// representation, addressed through <see cref="PlayerRef"/> instead of <see cref="Container"/>.
/// Both are wired by Player.cs after it finds these slots in its own _Ready(), not via [Export]
/// — PlayerInventory/SlotContainer are plain C# classes, not Resources/Nodes, so the editor
/// can't reference them directly.
/// </summary>
public partial class InventorySlotUI : Control
{
    [Export]
    public int SlotIndex { get; set; }

    /// <summary>The panel's one shared tooltip label — every slot points at the same node.</summary>
    [Export]
    public Label? Tooltip { get; set; }

    /// <summary>True only for the one Back slot — reads/writes the equipped backpack itself via
    /// <see cref="PlayerRef"/> rather than addressing <see cref="Container"/> by index.</summary>
    [Export]
    public bool IsBackSlot { get; set; }

    public SlotContainer? Container { get; set; }

    /// <summary>Wired on every slot (not just Back) — ordinary slots need it too, to react when
    /// the equipped backpack itself is dropped onto them (see _DropData's -1 sentinel).</summary>
    public Player? PlayerRef { get; set; }

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

        // -1 is the sentinel for "dragging the equipped backpack itself" — the Back slot has no
        // ordinary Container index to report.
        return IsBackSlot ? -1 : SlotIndex;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => data.VariantType == Variant.Type.Int;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Int || PlayerRef is null)
        {
            return;
        }

        var from = data.AsInt32();

        if (IsBackSlot)
        {
            PlayerRef.TryEquipBackpackFromBody(from);
            return;
        }

        if (from == -1)
        {
            PlayerRef.TryUnequipBackpack();
            return;
        }

        Container?.MoveSlot(from, SlotIndex);
    }

    private (string ItemId, int Count)? CurrentSlot()
    {
        if (IsBackSlot)
        {
            return PlayerRef?.Inventory.Backpack is { } backpack ? (backpack.ItemId, 1) : null;
        }

        return Container is { } c && SlotIndex >= 0 && SlotIndex < c.Slots.Count ? c.Slots[SlotIndex] : null;
    }
}
