using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Travel;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;
using PlayerScript = Scavengineers.Scripts.Player.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the docking minigame's effect on TravelConsoleVerbTarget's
/// own arrival flow: the travel timer elapsing no longer resolves arrival directly (see
/// OnTravelComplete/CompleteDocking) — it opens the docking panel and waits for its own Dock
/// button to succeed. Mirrors TravelConsoleVerbTargetTest's own CreateConsole harness shape, plus
/// a real PlayerTestHarness player so OnTravelComplete's "player" group lookup actually finds
/// someone to open the panel on.</summary>
[TestSuite]
public class TravelConsoleDockingTest
{
    private static (PlayerScript Player, TravelConsoleVerbTarget Console, Node3D StationGroup, Node3D DerelictGroup) MakeHarness(SceneTree sceneTree)
    {
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var homeShip = AutoFree(new ShipSim { HasPowerGrid = true });
        sceneTree.Root.AddChild(homeShip);

        var stationShip = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(stationShip);

        var stationGroup = AutoFree(new Node3D { Name = "StationGroup" });
        sceneTree.Root.AddChild(stationGroup);

        var stationAirlock = AutoFree(new AirlockDoorVerbTarget { Name = "StationAirlock", ShipARef = homeShip, ShipBRef = stationShip });
        sceneTree.Root.AddChild(stationAirlock);

        var derelictGroup = AutoFree(new Node3D { Name = "DerelictGroup1" });
        sceneTree.Root.AddChild(derelictGroup);

        var derelictShip = new ShipSim { Name = "ShipSim" };
        derelictGroup.AddChild(derelictShip);

        var derelictAirlock = AutoFree(new AirlockDoorVerbTarget { Name = "DerelictAirlock", ShipARef = homeShip, ShipBRef = derelictShip });
        sceneTree.Root.AddChild(derelictAirlock);

        var console = AutoFree(new TravelConsoleVerbTarget
        {
            ShipSimRef = homeShip,
            StationAirlock = stationAirlock,
            DerelictAirlock = derelictAirlock,
            StationGroup = stationGroup,
            DerelictGroupPaths = new Godot.Collections.Array<NodePath> { new("../DerelictGroup1") },
            DerelictShipSimPaths = new Godot.Collections.Array<NodePath> { new("../DerelictGroup1/ShipSim") },
            DerelictMapPositions = new Godot.Collections.Array<Vector2> { new(10, 10) },
        });
        sceneTree.Root.AddChild(console);

        homeShip.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        homeShip.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        homeShip.SetThrusterCharge("t1", 1f);

        return (player, console, stationGroup, derelictGroup);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task TravelTimerElapsing_OpensDockingInsteadOfResolvingArrival()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, _, _) = MakeHarness(sceneTree);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(4.5), SceneTreeTimer.SignalName.Timeout);

        // Still Station — arrival hasn't resolved, even though the travel timer itself is long done.
        AssertInt(console.CurrentDestinationId).IsEqual(0);
        AssertBool(player.GetNode<DockingMinigamePanel>("HUD/DockingPanel").Visible).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task CompleteDocking_ResolvesArrival_SameAsTheOldOnTravelCompleteDid()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, console, stationGroup, derelictGroup) = MakeHarness(sceneTree);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(4.5), SceneTreeTimer.SignalName.Timeout);

        console.CompleteDocking();

        AssertInt(console.CurrentDestinationId).IsEqual(1);
        AssertBool(stationGroup.Visible).IsFalse();
        AssertBool(derelictGroup.Visible).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ThrusterN2_KeepsDraining_ThroughTheDockingPhase()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, console, _, _) = MakeHarness(sceneTree);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(4.5), SceneTreeTimer.SignalName.Timeout);

        // Travel itself is done (see the timer wait above) — this is measuring drain during the
        // _docking phase specifically, not the earlier _traveling one.
        var chargeAtDockingStart = console.ShipSimRef!.ThrusterChargeFraction("t1");
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(console.ShipSimRef!.ThrusterChargeFraction("t1")).IsLess(chargeAtDockingStart);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task PressingDockButton_WhileWithinTolerance_ResolvesArrivalAndClosesThePanel()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, stationGroup, derelictGroup) = MakeHarness(sceneTree);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(4.5), SceneTreeTimer.SignalName.Timeout);

        var dockingPanel = player.GetNode<DockingMinigamePanel>("HUD/DockingPanel");
        AssertBool(dockingPanel.Visible).IsTrue();

        // Force well within DockAlignmentTolerance/DockDistanceTolerance/DockMaxSafeSpeed —
        // proving the click wiring itself, not the flight simulation that gets a player there.
        dockingPanel.ResetAttempt(startingOffset: Vector2.Zero, startingVelocity: Vector2.Zero, startingDistance: 5f);
        AssertBool(dockingPanel.DockButton!.Disabled).IsFalse();

        // Exercises the real click-to-handler wiring, not Player.CompleteDocking() directly — an
        // unwired button is exactly the class of bug the PDA's second cartridge slot had earlier
        // this session.
        dockingPanel.DockButton!.EmitSignal(Button.SignalName.Pressed);

        AssertInt(console.CurrentDestinationId).IsEqual(1);
        AssertBool(stationGroup.Visible).IsFalse();
        AssertBool(derelictGroup.Visible).IsTrue();
        AssertBool(dockingPanel.Visible).IsFalse();
    }
}
