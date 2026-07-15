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
        var player = AutoFree(new PlayerScript());

        var head = new Node3D { Name = "Head" };
        var camera = new Camera3D { Name = "Camera3D", Current = true };
        camera.AddChild(new RayCast3D { Name = "InteractRay" });
        camera.AddChild(new SpotLight3D { Name = "FlashlightSpot" });
        head.AddChild(camera);
        player.AddChild(head);

        var hud = new CanvasLayer { Name = "HUD" };
        player.AddChild(hud);

        hud.AddChild(new Label { Name = "TargetNameLabel" });
        hud.AddChild(new Label { Name = "VerbLabel" });
        hud.AddChild(new ProgressBar { Name = "VerbProgressBar" });

        var resourcesPanel = new Control { Name = "ResourcesPanel" };
        hud.AddChild(resourcesPanel);
        resourcesPanel.AddChild(new ProgressBar { Name = "O2Bar" });
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
        hud.AddChild(new Label { Name = "LeftHandLabel" });
        hud.AddChild(new Label { Name = "RightHandLabel" });
        hud.AddChild(new Label { Name = "CreditsLabel" });

        var inventoryPanel = new Control { Name = "InventoryPanel" };
        hud.AddChild(inventoryPanel);
        var inventoryLayout = new Node { Name = "Layout" };
        inventoryPanel.AddChild(inventoryLayout);
        // Left empty — Player._Ready only iterates this node's children (GetChildren), it never
        // requires any to exist. Slot-UI-specific behavior isn't in scope for this harness.
        inventoryLayout.AddChild(new Node { Name = "EquipSlots" });

        var drillWindow = new Control { Name = "DrillWindow" };
        hud.AddChild(drillWindow);
        var drillLayout = new Node { Name = "Layout" };
        drillWindow.AddChild(drillLayout);
        drillLayout.AddChild(MakeSlot("DrillBatterySlot"));

        var flashlightWindow = new Control { Name = "FlashlightWindow" };
        hud.AddChild(flashlightWindow);
        var flashlightLayout = new Node { Name = "Layout" };
        flashlightWindow.AddChild(flashlightLayout);
        flashlightLayout.AddChild(MakeSlot("FlashlightBatterySlot"));

        var backpackWindow = new Control { Name = "BackpackWindow" };
        hud.AddChild(backpackWindow);
        var backpackLayout = new Node { Name = "Layout" };
        backpackWindow.AddChild(backpackLayout);
        var backpackGrid = new Control { Name = "BackpackGrid" };
        backpackLayout.AddChild(backpackGrid);
        backpackGrid.AddChild(MakeSlot("SlotTemplate"));

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
}
