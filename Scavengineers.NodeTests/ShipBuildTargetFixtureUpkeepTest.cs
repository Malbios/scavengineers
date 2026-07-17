using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the fixture half of Stage 5's Maintain/Repair verbs — the
/// structural (floor/ceiling/wall) half already has its own coverage in
/// ShipBuildTargetUpkeepTest. Covers a placed conduit (owned directly by ShipBuildTarget) and
/// Switch/RechargeStation machines (delegated through ShipBuildTarget.MachineMaintainRepairVerbs/
/// ExecuteMachineMaintainRepair into their own VerbTarget scripts, the same pattern their existing
/// Uninstall/Scrap verbs already use — see MachineRemovalVerbs/ExecuteMachineRemoval). Battery is
/// deliberately excluded from all of this — its own Condition already means charge, not wear.
/// Uses ApplyBuildState (the save/load restore path) to place fixtures directly, since it bypasses
/// the Install verb's own tool/resource requirements entirely — exactly what a save load does.</summary>
[TestSuite]
public class ShipBuildTargetFixtureUpkeepTest
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
    public void AvailableVerbs_OffersMaintainConduit_ForAPlacedFloorConduitAboveHalfHealth()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget.ApplyBuildState(new BuildTargetSaveData { Conduits = [new TileCoord(0, 0)] });

        shipSim.Deck.Fixtures.Single(f => f.Id == "player_conduit_0_0_floor").Condition = 0.7f;

        buildTarget.SetAimPoint(new Vector3(-2.5f, 0f, -2.5f));

        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "maintain_conduit")).IsTrue();
        AssertBool(buildTarget.AvailableVerbs.Any(v => v.Id == "repair_conduit")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExecuteVerb_RepairConduit_RestoresItsFixtureConditionToFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (buildTarget, shipSim) = MakeHarness(sceneTree);
        buildTarget.ApplyBuildState(new BuildTargetSaveData { Conduits = [new TileCoord(0, 0)] });

        var conduitFixture = shipSim.Deck.Fixtures.Single(f => f.Id == "player_conduit_0_0_floor");
        conduitFixture.Condition = 0.3f;

        buildTarget.SetAimPoint(new Vector3(-2.5f, 0f, -2.5f));
        buildTarget.ExecuteVerb(new Verb("repair_conduit", "VERB_REPAIR_CONDUIT", DurationSeconds: 0.2f), inventory: null!);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        // Not exactly 1f: WearSystem keeps passively decaying every physics tick in the
        // background, including the ones this await let run.
        AssertFloat(conduitFixture.Condition).IsGreater(0.999f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ToggleLightVerbTarget_OffersMaintainVerb_AndReportsCondition_WhenItsFixtureIsDamaged()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines = [new MachineCoord("switch", 0, 0, 1, 0, null)],
        });

        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.SwitchFixtureId).Condition = 0.7f;

        var switchNode = buildTarget.GetChildren().OfType<ToggleLightVerbTarget>().Single();

        AssertBool(switchNode.AvailableVerbs.Any(v => v.Id == "maintain_switch")).IsTrue();
        AssertFloat(switchNode.Condition!.Value).IsEqual(0.7f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ToggleLightVerbTarget_ExecuteVerb_RepairSwitch_RestoresItsFixtureConditionToFull()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (buildTarget, shipSim) = MakeHarness(sceneTree);
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines = [new MachineCoord("switch", 0, 0, 1, 0, null)],
        });

        var switchFixture = shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.SwitchFixtureId);
        switchFixture.Condition = 0.3f;

        var switchNode = buildTarget.GetChildren().OfType<ToggleLightVerbTarget>().Single();
        switchNode.ExecuteVerb(new Verb("repair_switch", "VERB_REPAIR_SWITCH", DurationSeconds: 0.2f), inventory: null!);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(switchFixture.Condition).IsGreater(0.999f);
    }

    private static (ShipBuildTarget BuildTarget, ShipSim ShipSim) MakePoweredHarness(SceneTree sceneTree)
    {
        // Not MakeHarness — ShipSim only ever builds a PowerSystem at all when HasPowerGrid is
        // set (see ShipSim._Ready), and that has to be set before the node enters the tree, which
        // MakeHarness doesn't leave room for. None of this suite's other tests need real power.
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim { HasPowerGrid = true };
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot };
        shipRoot.AddChild(buildTarget);
        buildTarget.RoomLight = AutoFree(new OmniLight3D());

        return (buildTarget, shipSim);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task UninstallingTheSwitch_KeepsTheRoomLightOn_AsLongAsThereIsStillPower()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (buildTarget, _) = MakePoweredHarness(sceneTree);

        // A real charged battery + closed switch, both on the same edge — matches how the
        // existing battery/switch fixture tests each place a single machine there. Needed so the
        // light is legitimately on (driven by ToggleLightVerbTarget's own _PhysicsProcess) before
        // the switch is removed, rather than just asserting on a value nothing ever set.
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines =
            [
                new MachineCoord("battery", 0, 0, 1, 0, "1"),
                new MachineCoord("switch", 0, 0, 1, 0, null),
            ],
        });

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        AssertBool(buildTarget.RoomLight!.Visible).IsTrue();

        var switchNode = buildTarget.GetChildren().OfType<ToggleLightVerbTarget>().Single();
        switchNode.ExecuteVerb(new Verb("uninstall_switch", "VERB_UNINSTALL_SWITCH", DurationSeconds: 0.2f) { IsDestructive = true }, inventory: null!);

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        // Regression: uninstalling the switch removes the toggle, not the wire — with no switch
        // to gate it, the light should just keep following raw battery power (ShipBuildTarget's
        // own _PhysicsProcess takes over once no switch is placed), not go dark for no
        // electrical reason.
        AssertBool(buildTarget.RoomLight!.Visible).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task UninstallingTheSwitchThenTheBattery_TurnsOffTheRoomLight()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (buildTarget, shipSim) = MakePoweredHarness(sceneTree);

        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines =
            [
                new MachineCoord("battery", 0, 0, 1, 0, "1"),
                new MachineCoord("switch", 0, 0, 1, 0, null),
            ],
        });

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);

        var switchNode = buildTarget.GetChildren().OfType<ToggleLightVerbTarget>().Single();
        switchNode.ExecuteVerb(new Verb("uninstall_switch", "VERB_UNINSTALL_SWITCH", DurationSeconds: 0.2f) { IsDestructive = true }, inventory: null!);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        AssertBool(buildTarget.RoomLight!.Visible).IsTrue();

        var batteryNode = buildTarget.GetChildren().OfType<BatteryVerbTarget>().Single();
        batteryNode.ExecuteVerb(new Verb("uninstall_battery", "VERB_UNINSTALL_BATTERY", DurationSeconds: 0.2f) { IsDestructive = true }, inventory: null!);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        // The original bug report: remove the switch, then remove the battery too — the light
        // must actually go dark once there's genuinely no power left, not stay stuck either way.
        AssertBool(buildTarget.RoomLight!.Visible).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void RechargeStationVerbTarget_OffersRepairVerb_AndReportsCondition_WhenItsFixtureIsDamaged()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines = [new MachineCoord("recharge_station", 0, 0, 1, 0, null)],
        });

        shipSim.Deck.Fixtures.Single(f => f.Id == ShipSim.RechargeFixtureId).Condition = 0.4f;

        var stationNode = buildTarget.GetChildren().OfType<RechargeStationVerbTarget>().Single();

        AssertBool(stationNode.AvailableVerbs.Any(v => v.Id == "repair_recharge_station")).IsTrue();
        AssertFloat(stationNode.Condition!.Value).IsEqual(0.4f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BatteryVerbTarget_NeverOffersAnUpkeepVerb_EvenWhenItsConditionIsLow()
    {
        var (buildTarget, shipSim) = MakeHarness((SceneTree)Engine.GetMainLoop());
        buildTarget.ApplyBuildState(new BuildTargetSaveData
        {
            Machines = [new MachineCoord("battery", 0, 0, 1, 0, "0.2")],
        });

        // A battery's Condition means charge, not wear (see BatteryFixture's own doc comment) —
        // low charge must never surface a maintain/repair verb.
        var batteryNode = buildTarget.GetChildren().OfType<BatteryVerbTarget>().Single();
        AssertBool(batteryNode.AvailableVerbs.Any(v => v.Id.StartsWith("maintain_") || v.Id.StartsWith("repair_"))).IsFalse();
    }
}
