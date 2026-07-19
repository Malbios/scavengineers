using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Ship;
using DraggableWindowScript = Scavengineers.Scripts.Player.DraggableWindow;
using InventorySlotUIScript = Scavengineers.Scripts.Player.InventorySlotUI;

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

    /// <summary>Regression coverage for a bug found right after shipping storage: StorageGrid's
    /// visible=false (copied from BackpackGrid's own template) never got flipped back to true —
    /// unlike Backpack's own "_backpackGrid!.Visible = backpackContents is not null;" line in
    /// UpdateInventoryHud, which storage's own block was missing entirely. The window itself
    /// opened fine; its grid — and therefore every slot in it — just stayed invisible.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public async Task OpeningAStorageUnit_MakesTheGridVisible_WithOneSlotUIPerContentsSlot()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);
        var storage = AutoFree(new StorageVerbTarget { ItemId = "shelf", Contents = new SlotContainer(6) });

        // Unlike Player.tscn (visible = false on both StorageGrid itself and its SlotTemplate),
        // the hand-built harness leaves both at Godot's own Control default (true) — pin both so
        // this test actually proves UpdateInventoryHud flips the grid visible and the count below
        // reflects only the 6 real duplicated slots, not the permanent template itself.
        var grid = player.GetNode<Control>("HUD/StorageWindow/Layout/StorageGrid");
        grid.Visible = false;
        player.GetNode<InventorySlotUIScript>("HUD/StorageWindow/Layout/StorageGrid/SlotTemplate").Visible = false;

        player.OpenStorageInventory(storage);

        // UpdateInventoryHud (where the rebuild/visibility logic lives) runs from
        // _PhysicsProcess, not synchronously on Open — let a couple of real ticks run, same
        // pattern PlayerToolDurabilityTest already uses.
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        AssertBool(grid.Visible).IsTrue();
        AssertInt(grid.GetChildren().OfType<InventorySlotUIScript>().Count(s => s.Visible)).IsEqual(6);
    }
}
