using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using DraggableWindowScript = Scavengineers.Scripts.Player.DraggableWindow;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for a pre-existing gap found while building the death screen:
/// the Tab hotkey's own guard listed _travelMapOpen/_shopOpen (and later _deathOpen) but never
/// _thrusterInventoryOpen, so Tab could open the full inventory panel on top of an already-open
/// thruster tank window — the one panel that bypassed AnyPanelOpen for this specific hotkey.
/// (The other half of that gap, CaptureMouse() ignoring AnyPanelOpen on window refocus, isn't
/// testable here — see CaptureMouse's own doc comment for why.)</summary>
[TestSuite]
public class PlayerPanelGatingTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void Tab_WhileTheThrusterWindowIsOpen_DoesNotAlsoOpenTheInventoryPanel()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        var thruster = AutoFree(new ThrusterVerbTarget());

        // Unlike Player.tscn (visible = false), the hand-built harness leaves this at Godot's
        // own default (true) — pin it to the real scene's starting state so this test actually
        // proves Tab left it alone, rather than just never having been true in the first place.
        player.GetNode<DraggableWindowScript>("HUD/InventoryPanel").Visible = false;

        player.OpenThrusterInventory(thruster);

        player._Input(new InputEventKey { Keycode = Key.Tab, Pressed = true });

        AssertBool(player.GetNode<DraggableWindowScript>("HUD/InventoryPanel").Visible).IsFalse();
    }
}
