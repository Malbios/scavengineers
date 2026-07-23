using System.Collections.Generic;

using Godot;
using Scavengineers.Scripts.Inventory;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// The right-click-on-an-item sub-windows — drill battery, flashlight battery, backpack, EVA suit,
/// PDA — plus the slot grids inside the thruster and storage windows. Split out of Player.cs.
///
/// <para>These are distinct from <see cref="PanelController"/>'s modal panels: they don't suppress
/// gameplay input and aren't in <c>AnyPanelOpen</c>. What they are is a rendering of
/// <see cref="PlayerInventory"/>'s persistent-contents model — each window points at whichever
/// item's contents it was opened for, and that item may be worn, merely held, or tucked inside
/// another container. Because equipping/unequipping creates a *new* SlotContainer instance, the
/// windows are re-pointed every frame rather than on an equip event; that per-frame re-point is
/// this class's main job.</para>
///
/// <para>Constructed in code by <see cref="Player._Ready"/> and handed the HUD root, same as
/// <see cref="PlayerHudView"/> and <see cref="PanelController"/>.</para>
/// </summary>
public partial class InventoryWindowView : Node
{
    private DraggableWindow? _drillWindow;
    private DraggableWindow? _flashlightWindow;

    private DraggableWindow? _backpackWindow;
    private Control? _backpackGrid;
    private InventorySlotUI? _backpackSlotTemplate;
    private readonly List<InventorySlotUI> _backpackSlotUIs = new();

    /// <summary>Slot count the backpack grid was last built for — the grid's InventorySlotUI
    /// children are only rebuilt when this changes (equip/unequip/load), not every frame.</summary>
    private int _backpackSlotUICount = -1;

    private DraggableWindow? _suitWindow;
    private readonly InventorySlotUI?[] _suitPocketSlots = new InventorySlotUI?[2];

    private DraggableWindow? _pdaWindow;
    private InventorySlotUI? _pdaCartridgeSlot;
    private InventorySlotUI? _pdaCartridgeSlot2;

    private InventorySlotUI? _thrusterTankSlot;

    private Control? _storageGrid;
    private InventorySlotUI? _storageSlotTemplate;
    private readonly List<InventorySlotUI> _storageSlotUIs = new();
    private int _storageSlotUICount = -1;

    /// <summary>Which item's persistent contents each window is currently showing (see
    /// PlayerInventory.GetPersistentContents) — null when that window is closed. Deliberately an
    /// item id rather than a container reference: the container is replaced on every
    /// equip/unequip, the id survives it.</summary>
    private string? _openBackpackItemId;

    private string? _openSuitItemId;

    private string? _openPdaItemId;

    /// <summary>Every spawned slot addresses the player the same way the scene-authored ones do.
    /// Taken from Bind rather than read off the template node: the template's own PlayerRef is
    /// never set (it's a hidden prototype, not a live slot), so copying from it would leave every
    /// duplicate unwired.</summary>
    private Player? _player;

    public DraggableWindow? DrillWindow => _drillWindow;

    public DraggableWindow? FlashlightWindow => _flashlightWindow;

    public DraggableWindow? BackpackWindow => _backpackWindow;

    public DraggableWindow? SuitWindow => _suitWindow;

    public DraggableWindow? PdaWindow => _pdaWindow;

    public void Bind(Node hudRoot, Player player)
    {
        _player = player;
        _drillWindow = hudRoot.GetNode<DraggableWindow>("DrillWindow");
        _flashlightWindow = hudRoot.GetNode<DraggableWindow>("FlashlightWindow");
        hudRoot.GetNode<InventorySlotUI>("DrillWindow/Layout/DrillBatterySlot").PlayerRef = player;
        hudRoot.GetNode<InventorySlotUI>("FlashlightWindow/Layout/FlashlightBatterySlot").PlayerRef = player;

        _backpackWindow = hudRoot.GetNode<DraggableWindow>("BackpackWindow");
        _backpackGrid = hudRoot.GetNode<Control>("BackpackWindow/Layout/BackpackGrid");
        _backpackSlotTemplate = hudRoot.GetNode<InventorySlotUI>("BackpackWindow/Layout/BackpackGrid/SlotTemplate");

        _suitWindow = hudRoot.GetNode<DraggableWindow>("SuitWindow");
        foreach (var slotName in new[] { "O2Tank", "N2Tank", "Filter", "Battery" })
        {
            hudRoot.GetNode<InventorySlotUI>($"SuitWindow/Layout/SuitGrid/{slotName}").PlayerRef = player;
        }

        _suitPocketSlots[0] = hudRoot.GetNode<InventorySlotUI>("SuitWindow/Layout/SuitGrid/Pocket1");
        _suitPocketSlots[1] = hudRoot.GetNode<InventorySlotUI>("SuitWindow/Layout/SuitGrid/Pocket2");
        foreach (var pocketSlot in _suitPocketSlots)
        {
            pocketSlot!.PlayerRef = player;
        }

        _pdaWindow = hudRoot.GetNode<DraggableWindow>("PdaWindow");
        _pdaCartridgeSlot = hudRoot.GetNode<InventorySlotUI>("PdaWindow/Layout/PdaGrid/Cartridge1");
        _pdaCartridgeSlot.PlayerRef = player;
        _pdaCartridgeSlot2 = hudRoot.GetNode<InventorySlotUI>("PdaWindow/Layout/PdaGrid/Cartridge2");
        _pdaCartridgeSlot2.PlayerRef = player;

        _thrusterTankSlot = hudRoot.GetNode<InventorySlotUI>("ThrusterWindow/Layout/ThrusterGrid/Tank1");
        _thrusterTankSlot.PlayerRef = player;

        _storageGrid = hudRoot.GetNode<Control>("StorageWindow/Layout/StorageGrid");
        _storageSlotTemplate = hudRoot.GetNode<InventorySlotUI>("StorageWindow/Layout/StorageGrid/SlotTemplate");

        // Each window's own X button / right-click-on-background closes it exactly the way its
        // toggle-off path already does.
        _drillWindow.CloseRequested += () => _drillWindow!.Visible = false;
        _flashlightWindow.CloseRequested += () => _flashlightWindow!.Visible = false;
        _backpackWindow.CloseRequested += () =>
        {
            _backpackWindow!.Visible = false;
            _openBackpackItemId = null;
        };
        _suitWindow.CloseRequested += () =>
        {
            _suitWindow!.Visible = false;
            _openSuitItemId = null;
        };
        _pdaWindow.CloseRequested += () =>
        {
            _pdaWindow!.Visible = false;
            _openPdaItemId = null;
        };
    }

    /// <summary>Right-click-on-inventory-item entry point (see InventorySlotUI) — toggles whichever
    /// window represents that item's own inventory, or does nothing for an item that has none.
    /// Gated on <see cref="PlayerInventory.GetPersistentContents"/> rather than "currently worn",
    /// since a backpack/suit/PDA's contents are reachable whether it's worn, merely held, or (for
    /// the backpack) sitting in another backpack's slot.</summary>
    public void ToggleItemWindow(PlayerInventory inventory, string itemId)
    {
        switch (itemId)
        {
            case "power_drill":
                _drillWindow!.Visible = !_drillWindow.Visible;
                break;
            case "flashlight":
                _flashlightWindow!.Visible = !_flashlightWindow.Visible;
                break;
            case "backpack" or "debug_backpack" when inventory.GetPersistentContents(itemId) is not null:
                _backpackWindow!.Visible = !_backpackWindow.Visible;
                _openBackpackItemId = _backpackWindow.Visible ? itemId : null;
                break;
            case "eva_torso_suit" when inventory.GetPersistentContents(itemId) is not null:
                _suitWindow!.Visible = !_suitWindow.Visible;
                _openSuitItemId = _suitWindow.Visible ? itemId : null;
                break;
            case "pda" when inventory.GetPersistentContents(itemId) is not null:
                _pdaWindow!.Visible = !_pdaWindow.Visible;
                _openPdaItemId = _pdaWindow.Visible ? itemId : null;
                break;
        }
    }

    /// <summary>Closes every window here. Player wires this to PanelController.InventoryClosed —
    /// none of these are useful without the main inventory panel open to drag items to and from.</summary>
    public void CloseAll()
    {
        _drillWindow!.Visible = false;
        _flashlightWindow!.Visible = false;
        _backpackWindow!.Visible = false;
        _openBackpackItemId = null;
        _suitWindow!.Visible = false;
        _openSuitItemId = null;
        _pdaWindow!.Visible = false;
        _openPdaItemId = null;
    }

    /// <summary>Re-points every open window at its item's current contents, rebuilding the
    /// variable-size grids when their slot count changed, and closes any window whose item is gone.
    /// Thruster/storage contents are passed in rather than resolved here: they come from live world
    /// nodes that PanelController owns and that can be freed mid-frame, so Player does that
    /// validity check where the panel state lives.</summary>
    public void Refresh(PlayerInventory inventory, SlotContainer? thrusterContents, SlotContainer? storageContents)
    {
        var backpackContents = ContentsFor(inventory, ref _openBackpackItemId, _backpackWindow!);
        _backpackGrid!.Visible = backpackContents is not null;

        var backpackSlotCount = backpackContents?.Slots.Count ?? 0;
        if (backpackSlotCount != _backpackSlotUICount)
        {
            RebuildSlotUIs(_backpackSlotUIs, _backpackSlotTemplate!, _backpackGrid, backpackSlotCount, _player!);
            _backpackSlotUICount = backpackSlotCount;
        }

        foreach (var slot in _backpackSlotUIs)
        {
            slot.Container = backpackContents;
        }

        // The torso's own pocket slot *count* never varies (always 2), so no rebuild step — same
        // for the PDA's two cartridge pockets below. Both are static scene nodes.
        var suitContents = ContentsFor(inventory, ref _openSuitItemId, _suitWindow!);
        foreach (var pocketSlot in _suitPocketSlots)
        {
            pocketSlot!.Container = suitContents;
        }

        var pdaContents = ContentsFor(inventory, ref _openPdaItemId, _pdaWindow!);
        _pdaCartridgeSlot!.Container = pdaContents;
        _pdaCartridgeSlot2!.Container = pdaContents;

        _thrusterTankSlot!.Container = thrusterContents;

        _storageGrid!.Visible = storageContents is not null;
        var storageSlotCount = storageContents?.Slots.Count ?? 0;
        if (storageSlotCount != _storageSlotUICount)
        {
            RebuildSlotUIs(_storageSlotUIs, _storageSlotTemplate!, _storageGrid, storageSlotCount, _player!);
            _storageSlotUICount = storageSlotCount;
        }

        foreach (var slot in _storageSlotUIs)
        {
            slot.Container = storageContents;
        }
    }

    /// <summary>The contents a window should currently show, closing it (and forgetting its item)
    /// when that item has genuinely been discarded — otherwise an emptied window would keep
    /// floating there. The three windows all needed this identical guard.</summary>
    private static SlotContainer? ContentsFor(PlayerInventory inventory, ref string? openItemId, DraggableWindow window)
    {
        var contents = openItemId is { } itemId ? inventory.GetPersistentContents(itemId) : null;
        if (contents is null)
        {
            window.Visible = false;
            openItemId = null;
        }

        return contents;
    }

    /// <summary>Rebuilds a grid's InventorySlotUI children to match a container's actual slot count
    /// by duplicating its hidden template node — the scene only ever carries the one template, not
    /// a fixed slot count. Shared by the backpack (8 or 24 slots) and storage (per shelf/bin tier),
    /// which previously had a near-identical method each.</summary>
    private static void RebuildSlotUIs(List<InventorySlotUI> slots, InventorySlotUI template, Control grid, int slotCount, Player player)
    {
        foreach (var slot in slots)
        {
            slot.QueueFree();
        }

        slots.Clear();

        for (var i = 0; i < slotCount; i++)
        {
            var slot = (InventorySlotUI)template.Duplicate();
            slot.Visible = true;
            slot.SlotIndex = i;
            slot.PlayerRef = player;
            grid.AddChild(slot);
            slots.Add(slot);
        }
    }
}
