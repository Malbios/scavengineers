using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ShipSim.ApplyGeneratedLayout/LootSpawns and
/// ShipBuildTarget.SpawnGeneratedLoot — the procedural-generation counterpart to Derelict.tscn's
/// hand-placed pickups, which can't track a data-driven layout's own footprint (see the
/// Derelict2/derelict_small orphaned-loot gap this feature is built to avoid repeating).</summary>
[TestSuite]
public class ShipBuildTargetLootTest
{
    [TestCase]
    [RequireGodotRuntime]
    public async Task SpawnsOnePickupItemPerLootSpawn_AtTheExpectedWorldPosition()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        var generated = new GeneratedShipLayout
        {
            Layout = new ShipLayoutCatalog.ShipLayoutDefinition { GridWidth = 12, RoomSplitColumns = [6] },
            Loot =
            [
                new LootSpawn("scrap_metal", 2, 3, 1),
                new LootSpawn("power_cell", 1, 8, 4),
            ],
        };
        shipSim.ApplyGeneratedLayout(generated);
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget
        {
            ShipSimRef = shipSim,
            ShipRoot = shipRoot,
            GenerateLoot = true,
        };
        shipRoot.AddChild(buildTarget);

        await FrameWait.UntilAsync(sceneTree, () => buildTarget.InitialGenerationComplete);

        var pickups = shipRoot.GetChildren().OfType<PickupItem>().ToList();

        AssertInt(pickups.Count).IsEqual(2);

        var scrap = pickups.Single(p => p.ItemId == "scrap_metal");
        AssertInt(scrap.Count).IsEqual(2);
        AssertFloat(scrap.Position.X).IsEqual(3 - 3 + 0.5f);
        AssertFloat(scrap.Position.Z).IsEqual(1 - 3 + 0.5f);

        var powerCell = pickups.Single(p => p.ItemId == "power_cell");
        AssertInt(powerCell.Count).IsEqual(1);

        shipRoot.QueueFree();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task SpawnsNothing_WhenGenerateLootIsOff()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var shipRoot = AutoFree(new Node3D());
        sceneTree.Root.AddChild(shipRoot);

        var shipSim = new ShipSim();
        shipSim.ApplyGeneratedLayout(new GeneratedShipLayout
        {
            Layout = new ShipLayoutCatalog.ShipLayoutDefinition { GridWidth = 12, RoomSplitColumns = [6] },
            Loot = [new LootSpawn("scrap_metal", 1, 1, 1)],
        });
        shipRoot.AddChild(shipSim);

        var buildTarget = new ShipBuildTarget
        {
            ShipSimRef = shipSim,
            ShipRoot = shipRoot,
            GenerateLoot = false,
        };
        shipRoot.AddChild(buildTarget);

        // Asserting an absence, so there's a real condition to wait for after all: generation
        // having *finished* is exactly what makes "nothing spawned" meaningful rather than "we
        // looked too early".
        await FrameWait.UntilAsync(sceneTree, () => buildTarget.InitialGenerationComplete);

        AssertBool(shipRoot.GetChildren().OfType<PickupItem>().Any()).IsFalse();

        shipRoot.QueueFree();
    }
}
