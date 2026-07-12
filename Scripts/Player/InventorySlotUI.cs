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

    /// <summary>True only for the one drill-battery slot — reads/writes the held power drill's
    /// own battery via <see cref="PlayerRef"/>, the same non-fungible-item-via-PlayerRef shape
    /// <see cref="IsBackSlot"/> already established. Shows a synthetic "battery" slot (count
    /// always 1) when the drill has one installed — the drill's actual charge is shown
    /// separately (HUD/ResourcesPanel's DrillBar), not on this slot.</summary>
    [Export]
    public bool IsDrillBatterySlot { get; set; }

    public SlotContainer? Container { get; set; }

    /// <summary>Wired on every slot (not just Back) — ordinary slots need it too, to react when
    /// the equipped backpack itself is dragged from the Back slot onto them (see _DropData).</summary>
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

        // The source slot itself, not just an index — hand and backpack-contents slots address
        // *different* SlotContainer instances, so a bare index alone can't say which array it
        // came from (see SlotContainer.MoveBetween).
        return this;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.AsGodotObject() is InventorySlotUI;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.AsGodotObject() is not InventorySlotUI source || PlayerRef is null || ReferenceEquals(source, this))
        {
            return;
        }

        if (IsBackSlot)
        {
            if (!source.IsBackSlot)
            {
                PlayerRef.TryEquipBackpackFromHand(source.SlotIndex);
            }

            return;
        }

        if (source.IsBackSlot)
        {
            PlayerRef.TryUnequipBackpack();
            return;
        }

        if (IsDrillBatterySlot)
        {
            if (!source.IsDrillBatterySlot && source.CurrentSlot()?.ItemId == "battery")
            {
                PlayerRef.Inventory.InsertDrillBattery();
            }

            return;
        }

        if (source.IsDrillBatterySlot)
        {
            PlayerRef.Inventory.EjectDrillBattery();
            return;
        }

        if (source.Container is null || Container is null)
        {
            return;
        }

        SlotContainer.MoveBetween(source.Container, source.SlotIndex, Container, SlotIndex);
    }

    private (string ItemId, int Count)? CurrentSlot()
    {
        if (IsBackSlot)
        {
            return PlayerRef?.Inventory.Backpack is { } backpack ? (backpack.ItemId, 1) : null;
        }

        if (IsDrillBatterySlot)
        {
            return PlayerRef?.Inventory.Drill is { HasBattery: true } ? ("battery", 1) : null;
        }

        return Container is { } c && SlotIndex >= 0 && SlotIndex < c.Slots.Count ? c.Slots[SlotIndex] : null;
    }
}
