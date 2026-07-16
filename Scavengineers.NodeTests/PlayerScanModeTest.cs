using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the PDA's health-scan cartridge toggle (Player._Input's Key.V
/// branch, gated by Player.CanScan on the PDA worn + its cartridge pocket loaded + any helmet
/// worn). Only the PDA/cartridge half of the gate is exercisable here — the helmet half needs
/// ItemCatalog.EquipSlot("eva_helmet") to resolve to "head", which this project's isolated
/// NodeTests catalog can't do (always returns null regardless of item id, see
/// PlayerEquipSlotTest's own doc comment for the same limitation elsewhere); that half is covered
/// instead by ItemCatalogTests.EquipSlot_ReturnsTheDeclaredSlot (Scavengineers.Scripts.Tests,
/// where SeedForTests actually works) plus manual playtest. Toggles via a direct
/// _Input(InputEventKey) call — the first test in this project to drive input this way, since
/// nothing else needed to before.</summary>
[TestSuite]
public class PlayerScanModeTest
{
    private static void PressScanKey(Player player) =>
        player._Input(new InputEventKey { Keycode = Key.V, Pressed = true });

    [TestCase]
    [RequireGodotRuntime]
    public void PressingScanKey_StaysOff_WithNoPdaEquippedAtAll()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        PressScanKey(player);

        AssertBool(player.ScanModeOn).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void PressingScanKey_StaysOff_WhenPdaIsWornButItsCartridgePocketIsEmpty()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        player.Inventory.EquipContainerDirectly("pda", "pda", new SlotContainer(1));

        PressScanKey(player);

        AssertBool(player.ScanModeOn).IsFalse();
    }
}
