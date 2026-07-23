using System;
using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Contracts;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Shop;
using Scavengineers.Scripts.Travel;

namespace Scavengineers.Scripts.Player;

/// <summary>Every HUD panel that suppresses normal gameplay input while it's up.</summary>
public enum PanelId
{
    Inventory,
    TravelMap,
    Docking,
    Shop,
    ContractBoard,
    Thruster,
    Storage,
    Death,
}

/// <summary>Owns the player's modal HUD panels: which are open, the nodes themselves, the world
/// object each one was opened from, and the mouse-mode transition that goes with showing/hiding
/// them. One id-keyed mechanism, so adding a panel means adding an enum member and registering
/// its node. Panel *content* stays on Player's side — it owns the inventory and contract list the
/// shop and board are populated from — so this class exposes the typed panel nodes rather than
/// trying to reach that data itself.</summary>
public partial class PanelController : Node
{
    private readonly Dictionary<PanelId, Control> _panels = new();
    private readonly HashSet<PanelId> _open = [];

    private WorldDropZone? _worldDropZone;

    /// <summary>Supplied by Player, which owns the mouse-capture rules (including the
    /// suppress-for-tests flag) — this class decides *when* the mouse should go back to captured,
    /// never *whether* it's allowed to.</summary>
    private Action? _restoreMouse;

    public TravelMapPanel? TravelMap { get; private set; }

    public DockingMinigamePanel? Docking { get; private set; }

    public ShopPanel? Shop { get; private set; }

    public ContractBoardPanel? ContractBoard { get; private set; }

    public DeathPanel? Death { get; private set; }

    public DraggableWindow? InventoryWindow { get; private set; }

    public DraggableWindow? ThrusterWindow { get; private set; }

    public DraggableWindow? StorageWindow { get; private set; }

    /// <summary>The world object whichever panel was opened from, so the panel's own callbacks can
    /// be routed back to it. Each is set on open and cleared on close.</summary>
    public TravelConsoleVerbTarget? OpenTravelConsole { get; private set; }

    public TravelConsoleVerbTarget? OpenDockingConsole { get; private set; }

    public VendorVerbTarget? OpenVendor { get; private set; }

    public ContractGiverVerbTarget? OpenContractGiver { get; private set; }

    public ThrusterVerbTarget? OpenThruster { get; private set; }

    public StorageVerbTarget? OpenStorage { get; private set; }

    /// <summary>Raised when the inventory panel closes, so Player can drop the item sub-windows
    /// (drill/flashlight/backpack/suit/PDA) that are only useful while it's up. Those windows are
    /// inventory *view* state rather than modal panels — they don't suppress gameplay input and
    /// don't belong in <see cref="AnyOpen"/> — so they stay on Player's side of the line.</summary>
    public event Action? InventoryClosed;

    public bool AnyOpen => _open.Count > 0;

    public bool IsOpen(PanelId id) => _open.Contains(id);

    /// <summary>Whether anything *other than* <paramref name="id"/> is open — the Tab key's own
    /// gate, which has to let you toggle the inventory closed while refusing to open it over a
    /// shop/death screen/docking minigame.</summary>
    public bool AnyOpenExcept(PanelId id) => _open.Any(open => open != id);

    public void Bind(Node hudRoot, Player player, Action restoreMouse)
    {
        _restoreMouse = restoreMouse;

        InventoryWindow = hudRoot.GetNode<DraggableWindow>("InventoryPanel");
        ThrusterWindow = hudRoot.GetNode<DraggableWindow>("ThrusterWindow");
        StorageWindow = hudRoot.GetNode<DraggableWindow>("StorageWindow");

        TravelMap = hudRoot.GetNode<TravelMapPanel>("TravelMapPanel");
        Docking = hudRoot.GetNode<DockingMinigamePanel>("DockingPanel");
        Shop = hudRoot.GetNode<ShopPanel>("ShopPanel");
        ContractBoard = hudRoot.GetNode<ContractBoardPanel>("ContractBoardPanel");
        Death = hudRoot.GetNode<DeathPanel>("DeathPanel");
        _worldDropZone = hudRoot.GetNode<WorldDropZone>("WorldDropZone");

        TravelMap.PlayerRef = player;
        Docking.PlayerRef = player;
        Shop.PlayerRef = player;
        ContractBoard.PlayerRef = player;
        Death.PlayerRef = player;
        _worldDropZone.PlayerRef = player;

        _panels[PanelId.Inventory] = InventoryWindow;
        _panels[PanelId.TravelMap] = TravelMap;
        _panels[PanelId.Docking] = Docking;
        _panels[PanelId.Shop] = Shop;
        _panels[PanelId.ContractBoard] = ContractBoard;
        _panels[PanelId.Thruster] = ThrusterWindow;
        _panels[PanelId.Storage] = StorageWindow;
        _panels[PanelId.Death] = Death;
    }

    /// <summary>Shows a panel and frees the mouse. Callers that need to populate the panel first do
    /// so before calling this (see Player.OpenShop) — content is Player's concern, not this
    /// class's.</summary>
    public void Open(PanelId id)
    {
        _open.Add(id);
        _panels[id].Visible = true;

        if (id == PanelId.Inventory)
        {
            _worldDropZone!.Visible = true;
        }

        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Close(PanelId id)
    {
        _open.Remove(id);
        _panels[id].Visible = false;
        ClearOwnerOf(id);

        if (id == PanelId.Inventory)
        {
            _worldDropZone!.Visible = false;
            InventoryClosed?.Invoke();
        }

        _restoreMouse?.Invoke();
    }

    private void ClearOwnerOf(PanelId id)
    {
        switch (id)
        {
            case PanelId.TravelMap:
                OpenTravelConsole = null;
                break;
            case PanelId.Docking:
                OpenDockingConsole = null;
                break;
            case PanelId.Shop:
                OpenVendor = null;
                break;
            case PanelId.ContractBoard:
                OpenContractGiver = null;
                break;
            case PanelId.Thruster:
                OpenThruster = null;
                break;
            case PanelId.Storage:
                OpenStorage = null;
                break;
        }
    }

    // Typed open-with-owner entry points. Setting the owner and opening the panel are one step,
    // so the two can never disagree.

    public void OpenTravelMap(TravelConsoleVerbTarget console)
    {
        OpenTravelConsole = console;
        TravelMap!.Populate(console.BuildMapEntries(), console.CurrentDestinationId);
        Open(PanelId.TravelMap);
    }

    public void OpenDocking(TravelConsoleVerbTarget console)
    {
        OpenDockingConsole = console;
        Open(PanelId.Docking);
        Docking!.ResetAttempt();
    }

    public void OpenShop(VendorVerbTarget vendor)
    {
        OpenVendor = vendor;
        Open(PanelId.Shop);
    }

    public void OpenContractBoard(ContractGiverVerbTarget giver)
    {
        OpenContractGiver = giver;
        Open(PanelId.ContractBoard);
    }

    public void OpenThrusterInventory(ThrusterVerbTarget thruster)
    {
        OpenThruster = thruster;
        Open(PanelId.Thruster);
    }

    public void OpenStorageInventory(StorageVerbTarget storage)
    {
        OpenStorage = storage;
        Open(PanelId.Storage);
    }

    /// <summary>The Escape chain: closes the topmost closable panel and reports whether it closed
    /// anything, so Player's Escape handler can fall through to releasing the mouse when nothing
    /// was open. Docking and Death are deliberately absent — neither has a silent-abandon path;
    /// the only ways out are finishing or an in-panel choice.</summary>
    public bool CloseTopmostClosable()
    {
        foreach (var id in new[] { PanelId.TravelMap, PanelId.Shop, PanelId.ContractBoard, PanelId.Thruster, PanelId.Storage, PanelId.Inventory })
        {
            if (IsOpen(id))
            {
                Close(id);
                return true;
            }
        }

        return false;
    }
}
