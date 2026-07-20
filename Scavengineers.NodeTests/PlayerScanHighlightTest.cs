using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for Player.UpdateScanHighlight throwing ObjectDisposedException
/// when a scan-highlighted target (e.g. a battery uninstalled mid-flight via
/// ShipBuildTarget.RemoveMachine's QueueFree) is destroyed while still referenced by the player's
/// own _highlightedVisuals cache — not battery- or travel-specific, any target destroyed while
/// scan-highlighted hits the same path. Uses Player.SetScanModeOnForTests since the real gate
/// (CanScan) needs a helmet ItemCatalog.EquipSlot can't resolve in this isolated test catalog (see
/// PlayerScanModeTest's own doc comment for the same limitation) — and since _scanModeOn is force-
/// reset to false every physics frame CanScan doesn't hold (see its own doc comment), the accessor
/// has to be re-asserted before every frame that needs it on, not just once at setup. Reuses
/// AirlockDoorVerbTarget as a plain IVerbTarget with a real MeshInstance3D child, same as
/// HighlightVisualDefaultTest and PlayerToolDurabilityTest's own raycast setup.</summary>
[TestSuite]
public class PlayerScanHighlightTest
{
    // Matches Player.ScanHighlightLayer's own private value — read here only via the public
    // VisualInstance3D.GetLayerMaskValue API on a mesh the test itself owns, not via reflection
    // into Player.
    private const int ScanHighlightLayer = 20;

    [TestCase]
    [RequireGodotRuntime]
    public async Task TargetDestroyedWhileScanHighlighted_DoesNotThrow_AndHighlightingStillWorksAfterward()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var shipA = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipA);
        var shipB = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipB);

        var firstTarget = new AirlockDoorVerbTarget { ShipARef = shipA, ShipBRef = shipB, Position = new Vector3(0, 0, -2) };
        firstTarget.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
        firstTarget.AddChild(new MeshInstance3D { Name = "FrameMesh" });
        sceneTree.Root.AddChild(firstTarget);

        var interactRay = player.GetNode<RayCast3D>("Head/Camera3D/InteractRay");
        interactRay.TargetPosition = new Vector3(0, 0, -10);

        // Let the raycast resolve and UpdateScanHighlight populate _highlightedVisuals with
        // firstTarget's mesh — re-asserting scan mode on before each frame since it auto-resets
        // off at the end of every physics frame CanScan doesn't hold (see this class's own doc
        // comment).
        player.SetScanModeOnForTests(true);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
        player.SetScanModeOnForTests(true);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        // Destroy the highlighted target while the player still references its mesh and is still
        // scanning — the exact shape of uninstalling a battery mid-flight while looking at it.
        player.SetScanModeOnForTests(true);
        firstTarget.QueueFree();

        // QueueFree only frees at end-of-frame idle time, not immediately — a couple of process
        // frames (plus the physics frames below) ensure the native object is actually gone before
        // UpdateScanHighlight's clear-loop runs against the stale reference.
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        // Point at a fresh target and confirm scan highlighting still works cleanly afterward —
        // proves the crash didn't just get swallowed mid-update leaving _highlightedVisuals in a
        // corrupt state.
        var secondTarget = AutoFree(new AirlockDoorVerbTarget { ShipARef = shipA, ShipBRef = shipB, Position = new Vector3(0, 0, -2) });
        secondTarget.AddChild(new CollisionShape3D { Shape = new BoxShape3D() });
        var secondMesh = new MeshInstance3D { Name = "FrameMesh" };
        secondTarget.AddChild(secondMesh);
        sceneTree.Root.AddChild(secondTarget);

        player.SetScanModeOnForTests(true);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
        player.SetScanModeOnForTests(true);
        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        AssertBool(secondMesh.GetLayerMaskValue(ScanHighlightLayer)).IsTrue();
    }
}
