using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Godot;

namespace Scavengineers.Scripts.Ship;

/// <summary>Per-derelict grid shape and hazard placement, loaded from Data/Ships/layouts.json
/// (CLAUDE.md's data-driven non-negotiable). <see cref="ShipLayoutDefinition"/> is public (unlike
/// ItemCatalog.ItemDefinition) so Scavengineers.NodeTests — a separate assembly that can't reach
/// res://Data/ — can construct one directly and hand it to ShipSim.ApplyLayout.</summary>
public static class ShipLayoutCatalog
{
    private const string ResourcePath = "res://Data/Ships/layouts.json";

    private static Dictionary<string, ShipLayoutDefinition>? _layouts;

    private static Dictionary<string, ShipLayoutDefinition> Layouts => _layouts ??= Load();

    /// <summary>Null for an unknown/missing id — ShipSim.ApplyLayout treats null as a no-op.</summary>
    public static ShipLayoutDefinition? TryGet(string layoutId) =>
        Layouts.GetValueOrDefault(layoutId);

    /// <summary>Test-only seam bypassing <see cref="Load"/>'s Godot.FileAccess call.</summary>
    internal static void SeedForTests(Dictionary<string, ShipLayoutDefinition> layouts) => _layouts = layouts;

    internal static void ResetForTests() => _layouts = null;

    private static Dictionary<string, ShipLayoutDefinition> Load()
    {
        if (!Godot.FileAccess.FileExists(ResourcePath))
        {
            GD.PushWarning($"[ShipLayoutCatalog] {ResourcePath} not found — every LayoutId lookup will return null.");
            return new Dictionary<string, ShipLayoutDefinition>();
        }

        var json = Godot.FileAccess.GetFileAsString(ResourcePath);
        var definitions = JsonSerializer.Deserialize<List<ShipLayoutDefinition>>(json) ?? [];
        return definitions.ToDictionary(d => d.Id);
    }

    public sealed class ShipLayoutDefinition
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("gridWidth")]
        public int GridWidth { get; set; }

        [JsonPropertyName("roomSplitColumns")]
        public int[] RoomSplitColumns { get; set; } = [];

        [JsonPropertyName("eastCorridorLength")]
        public int EastCorridorLength { get; set; }

        [JsonPropertyName("hasHullBreaches")]
        public bool HasHullBreaches { get; set; }

        [JsonPropertyName("hasFireHazard")]
        public bool HasFireHazard { get; set; }

        [JsonPropertyName("initialBreaches")]
        public List<BreachEdge> InitialBreaches { get; set; } = [];

        [JsonPropertyName("fireGeneratorCell")]
        public CellPosition? FireGeneratorCell { get; set; }

        [JsonPropertyName("damagedConduitCell")]
        public CellPosition? DamagedConduitCell { get; set; }

        /// <summary>Null means single-deck (every layout before this field existed). Non-null
        /// describes a real second deck stacked above this one, reusing this same shape for its
        /// own independent hazard risk. Depth is capped at 1 — a SecondDeck's own SecondDeck is
        /// ignored.</summary>
        [JsonPropertyName("secondDeck")]
        public ShipLayoutDefinition? SecondDeck { get; set; }

        /// <summary>Shared X/Z tile where a ladder connects this deck to its SecondDeck. Required
        /// iff SecondDeck is set; must be a real cell in both decks' grids (an authoring
        /// constraint, not validated at runtime).</summary>
        [JsonPropertyName("ladderCell")]
        public CellPosition? LadderCell { get; set; }
    }

    public sealed class BreachEdge
    {
        [JsonPropertyName("cellX")]
        public int CellX { get; set; }

        [JsonPropertyName("cellY")]
        public int CellY { get; set; }

        [JsonPropertyName("outsideX")]
        public int OutsideX { get; set; }

        [JsonPropertyName("outsideY")]
        public int OutsideY { get; set; }
    }

    public sealed class CellPosition
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }
    }
}
