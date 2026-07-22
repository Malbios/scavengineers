using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the multi-deck ladder gap: exactly one of a cell's two panels
/// is skipped (never both, and never via Deck.BreachHull — see ShipBuildTarget.GeneratePanelsForCell's
/// own doc comment for why that would incorrectly vent the deck to vacuum) — the primary deck
/// (DeckIndex 0) opens its ceiling at ShipSimRef.LadderCell; a second deck (DeckIndex > 0) opens
/// its floor there instead, each keeping its OTHER panel completely normal.</summary>
[TestSuite]
public class ShipBuildTargetLadderGapTest
{
    private static readonly Vector3 FloorPos = new(-0.5f, -0.025f, -0.5f);
    private static readonly Vector3 CeilingPos = new(-0.5f, 2.025f, -0.5f);

    [TestCase]
    [RequireGodotRuntime]
    public async Task PrimaryDeck_SkipsOnlyTheCeilingPanel_AtTheLadderCell()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipSim.ApplyLayout(new ShipLayoutCatalog.ShipLayoutDefinition { Id = "t", GridWidth = 6, LadderCell = new() { X = 2, Y = 2 } });
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget { ShipSimRef = shipSim, ShipRoot = shipRoot, PanelMesh = new BoxMesh(), PanelCollisionShape = new BoxShape3D() };
        shipRoot.AddChild(buildTarget);

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);

        var colliders = buildTarget.GetChildren().OfType<CollisionShape3D>().ToList();
        AssertBool(colliders.Any(c => c.Position.IsEqualApprox(FloorPos))).IsTrue();
        AssertBool(colliders.Any(c => c.Position.IsEqualApprox(CeilingPos))).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task SecondDeck_SkipsOnlyTheFloorPanel_AtTheLadderCell()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var primaryDeck = new ShipSim();
        primaryDeck.ApplyLayout(new ShipLayoutCatalog.ShipLayoutDefinition { Id = "t", GridWidth = 6, LadderCell = new() { X = 2, Y = 2 } });
        shipRoot.AddChild(primaryDeck);

        var secondDeck = new ShipSim { DeckIndex = 1, PrimaryDeckRef = primaryDeck, GridWidth = 6 };
        shipRoot.AddChild(secondDeck);

        var buildTarget = new ShipBuildTarget { ShipSimRef = secondDeck, ShipRoot = shipRoot, PanelMesh = new BoxMesh(), PanelCollisionShape = new BoxShape3D() };
        shipRoot.AddChild(buildTarget);

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);

        var colliders = buildTarget.GetChildren().OfType<CollisionShape3D>().ToList();
        AssertBool(colliders.Any(c => c.Position.IsEqualApprox(FloorPos))).IsFalse();
        AssertBool(colliders.Any(c => c.Position.IsEqualApprox(CeilingPos))).IsTrue();
    }
}
