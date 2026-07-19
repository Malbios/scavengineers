using GdUnit4;
using Godot;
using Scavengineers.Scripts.Shop;
using Scavengineers.Scripts.Travel;
using PlayerScript = Scavengineers.Scripts.Player.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Builds, in code, just the HUD/Head node tree Player._Ready() dereferences via
/// GetNode — Scavengineers.NodeTests is its own separate, scene-less Godot project (see its
/// project.godot), so res://Scenes/Player.tscn (the real scene) can't be loaded here; every other
/// NodeTest already constructs bare nodes rather than loading a scene (see ShipSimTest,
/// PickupItemTest), this does the same at Player's scale. Deliberately NOT a faithful
/// reproduction of Player.tscn's visuals (no backgrounds/tooltips/real icons) — only what
/// Player._Ready() itself needs to not throw. Don't reuse this for testing HUD *appearance*.</summary>
public static class PlayerTestHarness
{
    public static PlayerScript CreateAttached(SceneTree sceneTree)
    {
        // GdUnit4's own test runner is a real, non-headless window (Vulkan-rendered) — without
        // this, every test built on this harness would capture the developer's actual OS mouse
        // into that window for the run's duration (Player._Ready() calls CaptureMouse()
        // unconditionally). See Player.SuppressMouseCaptureForTests's own doc comment.
        PlayerScript.SuppressMouseCaptureForTests = true;

        var player = AutoFree(new PlayerScript());

        var head = new Node3D { Name = "Head" };
        var camera = new Camera3D { Name = "Camera3D", Current = true };
        camera.AddChild(new RayCast3D { Name = "InteractRay" });
        camera.AddChild(new SpotLight3D { Name = "FlashlightSpot" });
        head.AddChild(camera);
        player.AddChild(head);

        var scanHighlightViewport = new SubViewport { Name = "ScanHighlightViewport" };
        scanHighlightViewport.AddChild(new Camera3D { Name = "ScanHighlightCamera" });
        player.AddChild(scanHighlightViewport);

        var hud = new CanvasLayer { Name = "HUD" };
        player.AddChild(hud);

        hud.AddChild(new ColorRect { Name = "ScanHighlightOverlay" });
        hud.AddChild(new Label { Name = "TargetNameLabel" });
        hud.AddChild(new Label { Name = "VerbLabel" });
        hud.AddChild(new ProgressBar { Name = "VerbProgressBar" });
        hud.AddChild(new Label { Name = "PowerInfoLabel" });
        hud.AddChild(new Label { Name = "SavedLabel" });

        var resourcesPanel = new Control { Name = "ResourcesPanel" };
        hud.AddChild(resourcesPanel);
        resourcesPanel.AddChild(new ProgressBar { Name = "O2Bar" });
        resourcesPanel.AddChild(new Label { Name = "CO2Label" });
        resourcesPanel.AddChild(new ProgressBar { Name = "CO2Bar" });
        resourcesPanel.AddChild(new ProgressBar { Name = "HealthBar" });
        resourcesPanel.AddChild(new ProgressBar { Name = "HungerBar" });
        resourcesPanel.AddChild(new ProgressBar { Name = "ThirstBar" });
        resourcesPanel.AddChild(new ProgressBar { Name = "EnergyBar" });
        resourcesPanel.AddChild(new Label { Name = "RoomO2Label" });
        resourcesPanel.AddChild(new Label { Name = "DrillLabel" });
        resourcesPanel.AddChild(new ProgressBar { Name = "DrillBar" });
        resourcesPanel.AddChild(new Label { Name = "FlashlightLabel" });
        resourcesPanel.AddChild(new ProgressBar { Name = "FlashlightBar" });

        hud.AddChild(new ColorRect { Name = "SmokeOverlay" });
        hud.AddChild(new ColorRect { Name = "ColdOverlay" });
        hud.AddChild(new ColorRect { Name = "BurnOverlay" });
        hud.AddChild(new ColorRect { Name = "LowHealthOverlay" });
        hud.AddChild(new Label { Name = "LeftHandLabel" });
        hud.AddChild(new Label { Name = "RightHandLabel" });
        hud.AddChild(new Label { Name = "CreditsLabel" });

        var inventoryPanel = MakeWindow("InventoryPanel");
        hud.AddChild(inventoryPanel);
        var inventoryLayout = new Node { Name = "Layout" };
        inventoryPanel.AddChild(inventoryLayout);
        // Left empty — Player._Ready only iterates this node's children (GetChildren), it never
        // requires any to exist. Slot-UI-specific behavior isn't in scope for this harness.
        inventoryLayout.AddChild(new Node { Name = "EquipSlots" });

        var drillWindow = MakeWindow("DrillWindow");
        hud.AddChild(drillWindow);
        var drillLayout = new Node { Name = "Layout" };
        drillWindow.AddChild(drillLayout);
        drillLayout.AddChild(MakeSlot("DrillBatterySlot"));

        var flashlightWindow = MakeWindow("FlashlightWindow");
        hud.AddChild(flashlightWindow);
        var flashlightLayout = new Node { Name = "Layout" };
        flashlightWindow.AddChild(flashlightLayout);
        flashlightLayout.AddChild(MakeSlot("FlashlightBatterySlot"));

        var backpackWindow = MakeWindow("BackpackWindow");
        hud.AddChild(backpackWindow);
        var backpackLayout = new Node { Name = "Layout" };
        backpackWindow.AddChild(backpackLayout);
        var backpackGrid = new Control { Name = "BackpackGrid" };
        backpackLayout.AddChild(backpackGrid);
        backpackGrid.AddChild(MakeSlot("SlotTemplate"));

        var suitWindow = MakeWindow("SuitWindow");
        hud.AddChild(suitWindow);
        var suitLayout = new Node { Name = "Layout" };
        suitWindow.AddChild(suitLayout);
        var suitGrid = new Node { Name = "SuitGrid" };
        suitLayout.AddChild(suitGrid);
        suitGrid.AddChild(MakeSlot("Pocket1"));
        suitGrid.AddChild(MakeSlot("Pocket2"));
        suitGrid.AddChild(MakeSlot("O2Tank"));
        suitGrid.AddChild(MakeSlot("N2Tank"));
        suitGrid.AddChild(MakeSlot("Filter"));
        suitGrid.AddChild(MakeSlot("Battery"));

        var pdaWindow = MakeWindow("PdaWindow");
        hud.AddChild(pdaWindow);
        var pdaLayout = new Node { Name = "Layout" };
        pdaWindow.AddChild(pdaLayout);
        var pdaGrid = new Node { Name = "PdaGrid" };
        pdaLayout.AddChild(pdaGrid);
        pdaGrid.AddChild(MakeSlot("Cartridge1"));
        pdaGrid.AddChild(MakeSlot("Cartridge2"));

        var thrusterWindow = MakeWindow("ThrusterWindow");
        hud.AddChild(thrusterWindow);
        var thrusterLayout = new Node { Name = "Layout" };
        thrusterWindow.AddChild(thrusterLayout);
        var thrusterGrid = new Node { Name = "ThrusterGrid" };
        thrusterLayout.AddChild(thrusterGrid);
        thrusterGrid.AddChild(MakeSlot("Tank1"));

        var storageWindow = MakeWindow("StorageWindow");
        hud.AddChild(storageWindow);
        var storageLayout = new Node { Name = "Layout" };
        storageWindow.AddChild(storageLayout);
        var storageGrid = new Control { Name = "StorageGrid" };
        storageLayout.AddChild(storageGrid);
        storageGrid.AddChild(MakeSlot("SlotTemplate"));

        var travelMapPanel = new TravelMapPanel { Name = "TravelMapPanel" };
        travelMapPanel.TravelButton = new Button();
        travelMapPanel.CancelButton = new Button();
        travelMapPanel.AddChild(travelMapPanel.TravelButton);
        travelMapPanel.AddChild(travelMapPanel.CancelButton);
        hud.AddChild(travelMapPanel);

        var shopPanel = new ShopPanel { Name = "ShopPanel" };
        shopPanel.CloseButton = new Button();
        shopPanel.AddChild(shopPanel.CloseButton);
        hud.AddChild(shopPanel);

        var deathPanel = new Scavengineers.Scripts.Player.DeathPanel { Name = "DeathPanel" };
        deathPanel.ReloadButton = new Button();
        deathPanel.QuitButton = new Button();
        deathPanel.AddChild(deathPanel.ReloadButton);
        deathPanel.AddChild(deathPanel.QuitButton);
        hud.AddChild(deathPanel);

        hud.AddChild(new Scavengineers.Scripts.Player.WorldDropZone { Name = "WorldDropZone" });

        sceneTree.Root.AddChild(player);
        return player;
    }

    private static Scavengineers.Scripts.Player.InventorySlotUI MakeSlot(string name)
    {
        var slot = new Scavengineers.Scripts.Player.InventorySlotUI { Name = name };
        slot.AddChild(new ColorRect { Name = "Icon" });
        slot.AddChild(new Label { Name = "Count" });
        return slot;
    }

    /// <summary>DraggableWindow._Ready() dereferences TitleBar unconditionally (GuiInput +=) —
    /// unlike the real scene, where Godot's own node_paths export resolution wires it before
    /// _Ready() runs, a bare C# node here needs it assigned explicitly before this window ever
    /// enters the tree. CloseButton is deliberately left null (DraggableWindow already tolerates
    /// that) — no test built on this harness needs to press it.</summary>
    private static Scavengineers.Scripts.Player.DraggableWindow MakeWindow(string name)
    {
        var titleBar = new Control { Name = "TitleBar" };
        var window = new Scavengineers.Scripts.Player.DraggableWindow { Name = name, TitleBar = titleBar };
        window.AddChild(titleBar);
        return window;
    }
}
