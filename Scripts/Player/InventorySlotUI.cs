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

    /// <summary>Non-empty only for a specialized sub-slot (e.g. "drill_battery",
    /// "flashlight_battery", or the EVA suit's tank slots) — reads/writes that device's own
    /// battery/tank via <see cref="PlayerRef"/>'s generic <c>PlayerInventory.SpecializedSlot</c>
    /// mechanism, the same non-fungible-item-via-PlayerRef shape <see cref="IsBackSlot"/> already
    /// established. Shows a synthetic single-item slot (count always 1) when loaded — the
    /// device's actual charge is shown separately (e.g. HUD/ResourcesPanel's DrillBar), not on
    /// this slot. Empty string = ordinary slot.</summary>
    [Export]
    public string SpecializedSlotKey { get; set; } = "";

    /// <summary>Non-empty only for the Torso/Head equip slots — reads/writes a whole worn item
    /// (with its own <see cref="PlayerInventory.EquippedContainer"/>, possibly 0-slot for a
    /// container-less item like the helmet) via <see cref="PlayerRef"/>, the same
    /// non-fungible-item-via-PlayerRef shape <see cref="IsBackSlot"/> already established —
    /// generalized (via <see cref="Player.TryEquipItemFromHand"/>'s tag-driven check) rather than
    /// hardcoded per item the way the Back slot still is. Empty string = ordinary slot.</summary>
    [Export]
    public string EquippedSlotName { get; set; } = "";

    /// <summary>How many inner slots a freshly-equipped item on <see cref="EquippedSlotName"/>
    /// gets (e.g. 2 for the EVA suit's torso pockets, 0 for the container-less helmet).</summary>
    [Export]
    public int EquippedContainerSlotCount { get; set; }

    /// <summary>True only for the Legs/LeftFoot/RightFoot slots — nothing targets them yet (see
    /// docs), so they never accept a drop at all, and show a "blocked" visual/tooltip whenever
    /// the EVA suit's torso piece is worn (it physically covers legs and feet).</summary>
    [Export]
    public bool IsUnusedBodySlot { get; set; }

    /// <summary>Localization key shown by <see cref="OnMouseEntered"/> when this slot has
    /// nothing in it — an empty slot otherwise gives no hover feedback about what it's for.</summary>
    [Export]
    public string EmptySlotNameKey { get; set; } = "";

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

    // Placeholder/tunable — how much an unused body slot dims while blocked by the worn torso.
    private static readonly Color BlockedModulate = new(1f, 1f, 1f, 0.35f);

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        if (IsUnusedBodySlot)
        {
            Modulate = PlayerRef?.Inventory.Torso is not null ? BlockedModulate : Colors.White;
        }

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
        if (Tooltip is null)
        {
            return;
        }

        if (IsUnusedBodySlot && PlayerRef?.Inventory.Torso is not null)
        {
            Tooltip.Text = Tr("HUD_SLOT_BLOCKED_BY_SUIT");
        }
        else if (BatteryCharge() is { } charge)
        {
            // A specialized slot's Count is always the synthetic "1" from CurrentSlot() —
            // showing charge instead is the actually useful number here (how much is left), not
            // how many. Named generically off CurrentSlot()'s own item id rather than assuming
            // "battery", since a future specialized slot (e.g. an EVA suit's O2 tank) won't be.
            var itemId = CurrentSlot()?.ItemId ?? "battery";
            Tooltip.Text = $"{Tr("ITEM_" + itemId.ToUpperInvariant())}: {Mathf.RoundToInt(charge * 100)}%";
        }
        else if (CurrentSlot() is { } slot)
        {
            Tooltip.Text = $"{Tr("ITEM_" + slot.ItemId.ToUpperInvariant())}: {slot.Count}";
        }
        else if (EmptySlotNameKey.Length > 0)
        {
            Tooltip.Text = Tr(EmptySlotNameKey);
        }
        else
        {
            return;
        }

        Tooltip.Visible = true;
        Tooltip.GlobalPosition = GetGlobalMousePosition() + new Vector2(16, 16);
    }

    /// <summary>Charge fraction (0-1) for the drill/flashlight battery slots specifically, or for
    /// an ordinary hand/backpack slot that happens to hold a loose "battery" item (see
    /// SlotContainer's own per-slot Charge) — null for every other slot, including a
    /// battery-less drill/flashlight (that case already falls through to CurrentSlot() returning
    /// null, i.e. "empty slot" tooltip handling).</summary>
    private float? BatteryCharge()
    {
        if (SpecializedSlotKey.Length > 0)
        {
            return PlayerRef?.Inventory.GetSpecializedSlot(SpecializedSlotKey) is { HasItem: true } slot ? slot.Charge : null;
        }

        return Container is { } c && SlotIndex >= 0 && SlotIndex < c.Slots.Count && c.Slots[SlotIndex] is { ItemId: "battery" } slot2
            ? slot2.Charge
            : null;
    }

    private void OnMouseExited()
    {
        if (Tooltip is not null)
        {
            Tooltip.Visible = false;
        }
    }

    /// <summary>Right-click opens whichever window (if any) represents this slot's occupying
    /// item's own inventory (drill/flashlight battery, worn backpack contents) — see
    /// Player.ToggleItemWindow. A separate event path from the left-click drag-and-drop below, so
    /// it doesn't interfere with it. A no-op for an empty slot or an item with no window.</summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && CurrentSlot() is { } occupied)
        {
            PlayerRef?.ToggleItemWindow(occupied.ItemId);
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
        !IsUnusedBodySlot && data.AsGodotObject() is InventorySlotUI;

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

        if (EquippedSlotName.Length > 0)
        {
            if (!source.IsBackSlot && source.SpecializedSlotKey.Length == 0 && source.EquippedSlotName.Length == 0)
            {
                PlayerRef.TryEquipItemFromHand(source.SlotIndex, EquippedSlotName, EquippedContainerSlotCount);
            }

            return;
        }

        if (source.EquippedSlotName.Length > 0)
        {
            PlayerRef.TryUnequipItem(source.EquippedSlotName);
            return;
        }

        if (SpecializedSlotKey.Length > 0)
        {
            if (source.SpecializedSlotKey != SpecializedSlotKey
                && source.CurrentSlot()?.ItemId == PlayerInventory.SpecializedSlotAcceptedItemId(SpecializedSlotKey))
            {
                PlayerRef.Inventory.InsertIntoSpecializedSlot(SpecializedSlotKey);
            }

            return;
        }

        if (source.SpecializedSlotKey.Length > 0)
        {
            // A real indexed Container slot (e.g. a hand or backpack slot) lands the item
            // exactly where it was dropped, failing outright if that exact slot is occupied
            // rather than silently placing it elsewhere. Only falls back to the generic
            // "wherever there's room" placement when the target isn't a real slot at all (e.g.
            // dropped onto another device's own specialized slot).
            if (Container is not null)
            {
                PlayerRef.Inventory.EjectSpecializedSlotTo(source.SpecializedSlotKey, Container, SlotIndex);
            }
            else
            {
                PlayerRef.Inventory.EjectSpecializedSlot(source.SpecializedSlotKey);
            }

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

        if (EquippedSlotName.Length > 0)
        {
            return PlayerRef?.Inventory.GetEquippedContainer(EquippedSlotName) is { } equipped ? (equipped.ItemId, 1) : null;
        }

        if (SpecializedSlotKey.Length > 0)
        {
            return PlayerRef?.Inventory.GetSpecializedSlot(SpecializedSlotKey) is { HasItem: true }
                && PlayerInventory.SpecializedSlotAcceptedItemId(SpecializedSlotKey) is { } specializedItemId
                ? (specializedItemId, 1)
                : null;
        }

        return Container is { } c && SlotIndex >= 0 && SlotIndex < c.Slots.Count && c.Slots[SlotIndex] is { } slot
            ? (slot.ItemId, slot.Count)
            : null;
    }
}
