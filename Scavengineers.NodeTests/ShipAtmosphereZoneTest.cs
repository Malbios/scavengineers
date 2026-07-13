using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ShipAtmosphereZone's real physics zero-g override (loose
/// pickup items drifting in a breached room) — a room reading as vacuum must flip the zone's own
/// Area3D gravity override on, and a normal breathable room must leave it off.</summary>
[TestSuite]
public class ShipAtmosphereZoneTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void EnablesGravityOverride_OnceRoomReadsAsVacuum()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);

        var cell = new CellCoord(0, 0);
        shipSim.Deck.BreachHull(cell);
        for (var i = 0; i < 50; i++)
        {
            shipSim.Atmosphere!.Tick(1);
        }

        var zone = AutoFree(new ShipAtmosphereZone { ShipSimRef = shipSim, Tile = new Vector2I(0, 0) });
        sceneTree.Root.AddChild(zone);

        zone._PhysicsProcess(0);

        AssertBool(zone.GravitySpaceOverride == Area3D.SpaceOverride.Replace).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void DisablesGravityOverride_InBreathableRoom()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipSim = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(shipSim);

        var zone = AutoFree(new ShipAtmosphereZone { ShipSimRef = shipSim, Tile = new Vector2I(0, 0) });
        sceneTree.Root.AddChild(zone);

        zone._PhysicsProcess(0);

        AssertBool(zone.GravitySpaceOverride == Area3D.SpaceOverride.Disabled).IsTrue();
    }
}
