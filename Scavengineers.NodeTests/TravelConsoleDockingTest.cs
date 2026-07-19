using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Travel;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;
using PlayerScript = Scavengineers.Scripts.Player.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the docking minigame's effect on TravelConsoleVerbTarget's
/// own arrival flow: the travel timer elapsing no longer resolves arrival directly (see
/// OnTravelComplete/CompleteDocking) — it opens the docking panel and waits for its own Dock
/// button to succeed, but only automatically if the player is looking at the console when the
/// timer fires (see Player.IsLookingAt) — otherwise ResumeDockingVerb is how the player gets back
/// into it. Mirrors TravelConsoleVerbTargetTest's own CreateConsole harness shape, plus a real
/// PlayerTestHarness player so OnTravelComplete's "player" group lookup actually finds someone to
/// open the panel on. BaseTravelSeconds/MinTravelSeconds are dialed down from their real
/// (20s/8s) defaults on every harness console — these are public settable properties specifically
/// so tests don't have to really wait out a pacing-only duration (see TravelConsoleVerbTarget's
/// own doc comment).</summary>
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
            BaseTravelSeconds = 0.3f,
            MinTravelSeconds = 0.1f,
        });
        sceneTree.Root.AddChild(console);

        homeShip.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        homeShip.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        homeShip.SetThrusterCharge("t1", 1f);

        return (player, console, stationGroup, derelictGroup);
    }

    /// <summary>Gives the console a real collider and points the player's interact ray at it —
    /// same pattern PlayerToolDurabilityTest.cs already uses for "the player is looking at this
    /// object" setup (RayCast3D only resolves during the physics step, so two PhysicsFrame
    /// signals are awaited before IsLookingAt would see a hit).</summary>
    private static async Task PointPlayerAtConsoleAsync(SceneTree sceneTree, PlayerScript player, TravelConsoleVerbTarget console)
    {
        console.Position = new Vector3(0, 0, -2);
        console.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });

        var interactRay = player.GetNode<RayCast3D>("Head/Camera3D/InteractRay");
        interactRay.TargetPosition = new Vector3(0, 0, -10);

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task TravelTimerElapsing_WhilePlayerIsAtTheConsole_OpensDockingInsteadOfResolvingArrival()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, _, _) = MakeHarness(sceneTree);
        await PointPlayerAtConsoleAsync(sceneTree, player, console);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        // Still Station — arrival hasn't resolved, even though the travel timer itself is long done.
        AssertInt(console.CurrentDestinationId).IsEqual(0);
        AssertBool(player.GetNode<DockingMinigamePanel>("HUD/DockingPanel").Visible).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task TravelTimerElapsing_WhilePlayerIsElsewhere_DoesNotOpenDocking_ButOffersResumeDockingVerb()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, _, _) = MakeHarness(sceneTree);

        // No PointPlayerAtConsoleAsync call — the bare harness console has no CollisionShape3D at
        // all, so the interact ray naturally never hits it, matching a player who walked away
        // after confirming the destination (the travel map already closed by then). Explicitly
        // zeroed rather than left at the engine default: a shared sceneTree.Root across every
        // test in this suite means a not-yet-freed collider from an earlier test (see
        // PointPlayerAtConsoleAsync) could otherwise sit within a default ray's short reach.
        player.GetNode<RayCast3D>("Head/Camera3D/InteractRay").TargetPosition = Vector3.Zero;

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        AssertInt(console.CurrentDestinationId).IsEqual(0);
        AssertBool(player.GetNode<DockingMinigamePanel>("HUD/DockingPanel").Visible).IsFalse();
        AssertBool(console.AvailableVerbs.Any(v => v.Id == "resume_docking")).IsTrue();
        AssertBool(console.AvailableVerbs.Any(v => v.Id == "travel")).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExecuteVerb_ResumeDocking_OpensThePanel_AfterArrivalDidNotAutoOpenIt()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, _, _) = MakeHarness(sceneTree);

        // See the "elsewhere" test's own comment on why this is explicit rather than relying on
        // the engine default.
        player.GetNode<RayCast3D>("Head/Camera3D/InteractRay").TargetPosition = Vector3.Zero;

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        AssertBool(player.GetNode<DockingMinigamePanel>("HUD/DockingPanel").Visible).IsFalse();

        console.ExecuteVerb(new Verb("resume_docking", "VERB_RESUME_DOCKING", DurationSeconds: 0.2f), inventory: null!);

        AssertBool(player.GetNode<DockingMinigamePanel>("HUD/DockingPanel").Visible).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task CompleteDocking_ResolvesArrival_SameAsTheOldOnTravelCompleteDid()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, console, stationGroup, derelictGroup) = MakeHarness(sceneTree);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        console.CompleteDocking();

        AssertInt(console.CurrentDestinationId).IsEqual(1);
        AssertBool(stationGroup.Visible).IsFalse();
        AssertBool(derelictGroup.Visible).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ThrusterN2_StopsDraining_OnceTheDockingPhaseBegins()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (_, console, _, _) = MakeHarness(sceneTree);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        // Travel itself is done (see the timer wait above) — docking is open-ended (waits on
        // player skill), so drain is now bounded to the _traveling phase only; otherwise a slow
        // docking attempt could burn an unbounded amount of N2/battery regardless of how short
        // the timed trip itself was.
        var chargeAtDockingStart = console.ShipSimRef!.ThrusterChargeFraction("t1");
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        AssertFloat(console.ShipSimRef!.ThrusterChargeFraction("t1")).IsEqual(chargeAtDockingStart);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task PressingDockButton_WhileWithinTolerance_ResolvesArrivalAndClosesThePanel()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, stationGroup, derelictGroup) = MakeHarness(sceneTree);
        await PointPlayerAtConsoleAsync(sceneTree, player, console);

        console.BeginTravel(1);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

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
