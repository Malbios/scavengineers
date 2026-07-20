using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for installable storage (shelves/bins) — mirrors
/// ShipBuildTargetThrusterTest.cs's own harness/shape. Slot count is deliberately never asserted
/// against a real ItemCatalog value here: Scavengineers.NodeTests has no items.json of its own
/// (see PlayerEquipSlotTest's own doc comment for the same gap with ItemCatalog.EquipSlot), so
/// ItemCatalog.StorageSlotCount/StorageItemIds always return 0/empty in this project. That's
/// exactly why StorageVerbTarget.ApplySaveState derives slot count from the saved state string's
/// own segment count instead of consulting ItemCatalog — which is what makes the round-trip test
/// below possible at all in this environment.</summary>
[TestSuite]
public class ShipBuildTargetStorageTest
{
    private static (ShipBuildTarget BuildTarget, ShipSim ShipSim) MakeHarness(SceneTree sceneTree)
    {
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot };
        shipRoot.AddChild(buildTarget);

        return (buildTarget, shipSim);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void InstallStorage_NotOfferedAgain_WhenAimingFromEitherSideOfAnAlreadyInstalledUnit()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget.BatteryMesh = new BoxMesh(); // MachineVerbsFor's own "opted into this system" gate.

        var edgeA = new CellCoord(0, 0);
        var edgeB = new CellCoord(1, 0);
        shipSim.Deck.SealEdge(edgeA, edgeB); // A machine needs a real wall to mount on.

        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines = [new MachineCoord("storage:small_bin", 0, 0, 1, 0, "")],
        });

        // Aimed from inside cell (0,0) looking toward (1,0) — resolves _edgeA=(0,0), _edgeB=(1,0).
        buildTarget.SetAimPoint(new Vector3(-2.1f, 0f, -2.5f));
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "uninstall_storage")).IsTrue();

        // Aimed from inside cell (1,0) looking back toward (0,0) — the raw pair is reversed
        // (_edgeA=(1,0), _edgeB=(0,0)), exactly the case Deck.Normalize exists to dedupe.
        buildTarget.SetAimPoint(new Vector3(-1.9f, 0f, -2.5f));
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "uninstall_storage")).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void SaveRoundTrip_PreservesEveryUnitsOwnContents_IncludingSlotCount()
    {
        var (buildTarget, _) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines =
            [
                new MachineCoord("storage:small_bin", 0, 0, 1, 0, "scrap_metal,3,1;;n2_tank,1,0.5"),
                new MachineCoord("storage:shelf", 2, 0, 3, 0, ";;;;;"),
            ],
        });

        var captured = buildTarget.CaptureBuildState();
        var binRow = captured.Machines.Single(m => m.Type == "storage:small_bin");
        var shelfRow = captured.Machines.Single(m => m.Type == "storage:shelf");
        AssertBool(binRow.State == "scrap_metal,3,1;;n2_tank,1,0.5").IsTrue();
        AssertBool(shelfRow.State == ";;;;;").IsTrue();

        var (buildTarget2, _) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget2.ApplyBuildState(captured);

        var binNode = buildTarget2.GetChildren().OfType<StorageVerbTarget>().Single(n => n.ItemId == "small_bin");
        var shelfNode = buildTarget2.GetChildren().OfType<StorageVerbTarget>().Single(n => n.ItemId == "shelf");

        AssertInt(binNode.Contents.Slots.Count).IsEqual(3);
        AssertBool(binNode.Contents.Slots[0] is { ItemId: "scrap_metal", Count: 3 }).IsTrue();
        AssertBool(binNode.Contents.Slots[1] is null).IsTrue();
        AssertBool(binNode.Contents.Slots[2] is { ItemId: "n2_tank", Count: 1 }).IsTrue();
        AssertFloat(binNode.Contents.Slots[2]!.Value.Charge).IsEqual(0.5f);

        AssertInt(shelfNode.Contents.Slots.Count).IsEqual(6);
        AssertBool(shelfNode.Contents.Slots.All(s => s is null)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Uninstall_RefundsBothTheFixtureItemAndEverythingStoredInside()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (buildTarget, _) = MakeHarness(sceneTree);
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            // Count deliberately 1, not 3: PlayerInventory.Add's stacking depends on
            // ItemCatalog.MaxStackSize, which falls back to 1 for any item id in this project's
            // isolated NodeTests catalog (no items.json here — see this file's own class doc
            // comment) — a bare, backpack-less PlayerInventory can't actually hold 3 of a
            // stack-size-1 item in its 2 hand slots, which isn't what this test is about.
            Machines = [new MachineCoord("storage:small_bin", 0, 0, 1, 0, "scrap_metal,1,1")],
        });

        var storageNode = buildTarget.GetChildren().OfType<StorageVerbTarget>().Single();
        var inventory = new PlayerInventory();

        // ExecuteStorageRemoval is internal to ShipBuildTarget — go through the unit's own public
        // ExecuteVerb, same as ShipBuildTargetThrusterTest's own Uninstall test does.
        storageNode.ExecuteVerb(
            new Verb("uninstall_storage", "VERB_UNINSTALL_STORAGE", DurationSeconds: 0.2f) { IsDestructive = true },
            inventory);

        // Only starts the cycle timer — the actual removal (and refund) happens in
        // OnCycleComplete once the verb's own duration elapses.
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);

        AssertBool(inventory.Has("small_bin", 1)).IsTrue();
        AssertBool(inventory.Has("scrap_metal", 1)).IsTrue();
    }
}
