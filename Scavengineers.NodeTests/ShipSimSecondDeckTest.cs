using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Sim.Grid;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for multi-deck derelicts: a DeckIndex>0 ShipSim resolves its own
/// shape from its PrimaryDeckRef's own SecondDeckLayout instead of its own LayoutId/
/// ProcedurallyGenerate, staying an empty, harmless grid when the primary deck's layout has no
/// SecondDeck at all (the "not every derelict is multi-deck" case) — see
/// ShipLayoutCatalog.ShipLayoutDefinition.SecondDeck's own doc comment.</summary>
[TestSuite]
public class ShipSimSecondDeckTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void SecondDeck_StaysEmpty_WhenThePrimaryDecksLayoutHasNoSecondDeck()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();

        // No LayoutId/ProcedurallyGenerate set at all — SecondDeckLayout stays null, matching any
        // single-deck derelict today (e.g. derelict_small).
        var primaryDeck = AutoFree(new ShipSim());
        sceneTree.Root.AddChild(primaryDeck);

        // GridWidth=0 mirrors Derelict.tscn's own Deck2 template default — inert until a real
        // SecondDeckLayout says otherwise.
        var secondDeck = AutoFree(new ShipSim { DeckIndex = 1, PrimaryDeckRef = primaryDeck, GridWidth = 0 });
        sceneTree.Root.AddChild(secondDeck);

        AssertInt(secondDeck.Deck.Cells.Count).IsEqual(0);
        AssertBool(secondDeck.LadderCell is null).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void SecondDeck_BuildsItsOwnRealGridAndIndependentHazard_WhenThePrimaryDecksLayoutHasOne()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();

        var secondDeckLayout = new ShipLayoutCatalog.ShipLayoutDefinition
        {
            Id = "test_deck2",
            GridWidth = 6,
            HasHullBreaches = true,
            InitialBreaches = [new() { CellX = 4, CellY = 5, OutsideX = 4, OutsideY = 6 }],
        };
        var primaryLayout = new ShipLayoutCatalog.ShipLayoutDefinition
        {
            Id = "test_primary",
            GridWidth = 18,
            RoomSplitColumns = [6, 12],
            LadderCell = new() { X = 2, Y = 2 },
            SecondDeck = secondDeckLayout,
        };

        // ApplyLayout called BEFORE AddChild, same established pattern ShipSimTest.cs's own
        // ApplyLayout_OverwritesGridShapeAndHazards_FromAGivenDefinition test already uses — the
        // fields it sets are what _Ready() actually builds Deck from.
        var primaryDeck = AutoFree(new ShipSim());
        primaryDeck.ApplyLayout(primaryLayout);
        sceneTree.Root.AddChild(primaryDeck);

        var secondDeck = AutoFree(new ShipSim { DeckIndex = 1, PrimaryDeckRef = primaryDeck, GridWidth = 0 });
        sceneTree.Root.AddChild(secondDeck);

        AssertInt(secondDeck.Deck.Cells.Count).IsGreater(0);
        AssertBool(secondDeck.HasHullBreaches).IsTrue();
        AssertBool(secondDeck.LadderCell == new CellCoord(2, 2)).IsTrue();

        // Own independent hazard — a real breach, on its own AtmosphereSystem instance, decoupled
        // entirely from the primary deck's.
        AssertBool(secondDeck.Atmosphere!.IsConnectedToOutside(new CellCoord(4, 5))).IsTrue();
    }
}
