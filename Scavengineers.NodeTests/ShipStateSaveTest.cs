using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>
/// Round-trips a ship's *live* sim state — the air in every cell and which cells are burning —
/// through a real SaveManager save and load.
///
/// <para>This is the gap docs/architecture/save-schema.md's "serialize live sim state, not just
/// static layout" rule described and the format didn't honour: everything structural round-tripped,
/// but a ship you'd vented came back at whatever its startup seeding produced, and its own named
/// example — "a fire mid-spread" — was simply lost.</para>
/// </summary>
[TestSuite]
public class ShipStateSaveTest
{
    private static (ShipSim Ship, SaveManager Manager, string Path) MakeHarness(SceneTree sceneTree, string saveId)
    {
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var path = Path.Combine(Path.GetTempPath(), $"scavengineers-shipstate-{Guid.NewGuid()}.json");
        var manager = AutoFree(new SaveManager { PlayerRef = player, SavePath = path });
        sceneTree.Root.AddChild(manager);

        var ship = AutoFree(new ShipSim { SaveId = saveId, GridWidth = 4 });
        ship.AddToGroup("saveable");
        sceneTree.Root.AddChild(ship);

        return (ship, manager, path);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task VentedRoomAndActiveFire_BothSurviveASaveLoadRoundTrip()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (ship, manager, path) = MakeHarness(sceneTree, "test_ship_state");

        try
        {
            await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

            var vented = new CellCoord(1, 1);
            var burning = new CellCoord(2, 2);
            ship.Atmosphere!.ApplyExternalVolume(vented, AtmosphereVolume.Vacuum);
            ship.Deck.IgniteFire(burning);

            manager.Save();

            // Put the ship back into a state the save must overwrite: air restored, fire out,
            // and a *different* cell burning — so a load that did nothing, or that only added
            // fires without clearing, would both fail here.
            ship.Atmosphere.ApplyExternalVolume(vented, AtmosphereVolume.Breathable);
            ship.Deck.ExtinguishFire(burning);
            ship.Deck.IgniteFire(new CellCoord(0, 0));

            AssertBool(manager.Load()).IsTrue();

            AssertFloat((float)ship.Atmosphere.VolumeAt(vented).O2Fraction).IsEqualApprox(0f, 0.0001f);
            AssertBool(ship.Deck.IsOnFire(burning)).IsTrue();
            AssertBool(ship.Deck.IsOnFire(new CellCoord(0, 0))).IsFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A ship with no SaveId is skipped rather than writing under the empty-string key —
    /// otherwise every unnamed ship would share one entry and all end up with the last one's
    /// atmosphere on load.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public async Task ShipWithNoSaveId_IsSkippedEntirely()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (ship, manager, path) = MakeHarness(sceneTree, saveId: "");

        try
        {
            await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);
            ship.Atmosphere!.ApplyExternalVolume(new CellCoord(1, 1), AtmosphereVolume.Vacuum);

            manager.Save();

            var json = File.ReadAllText(path);
            var data = System.Text.Json.JsonSerializer.Deserialize<SaveData>(json);

            AssertObject(data).IsNotNull();
            AssertBool(data!.Ships.ContainsKey("")).IsFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>An absent ship drops to a coarse tick rather than freezing — the sim-LOD seam in
    /// docs/architecture/multi-ship-fleet.md. Verified through the node, since IsPresent is what
    /// TravelConsoleVerbTarget.SetShipPresence actually sets.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public async Task AbsentShip_KeepsSimulating_RatherThanFreezing()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var ship = new ShipSim { GridWidth = 4, IsPresent = false };
        shipRoot.AddChild(ship);

        await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.PhysicsFrame);

        var cell = new CellCoord(0, 0);
        var startingHealth = ship.Deck.FloorHealth(cell);

        // Long enough to bank at least one full coarse lump (ShipSystems.CoarseTickSeconds).
        await sceneTree.ToSignal(sceneTree.CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);

        AssertBool(ship.Deck.FloorHealth(cell) < startingHealth).IsTrue();
    }
}
