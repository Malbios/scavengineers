using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Godot;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// Per-derelict grid shape and hazard placement, loaded from Data/Ships/layouts.json
/// (CLAUDE.md's data-driven non-negotiable) rather than the single hardcoded shape every
/// ShipSim instance used to share. Modeled directly on Scripts/Inventory/ItemCatalog.cs's own
/// pattern. <see cref="ShipLayoutDefinition"/> is deliberately public (not internal, unlike
/// ItemCatalog.ItemDefinition) so Scavengineers.NodeTests — a genuinely separate assembly that
/// can neither reach res://Data/ nor call this class's internal SeedForTests — can construct
/// one directly and hand it straight to ShipSim.ApplyLayout.
/// </summary>
public static class ShipLayoutCatalog
{
    private const string ResourcePath = "res://Data/Ships/layouts.json";

    private static Dictionary<string, ShipLayoutDefinition>? _layouts;

    private static Dictionary<string, ShipLayoutDefinition> Layouts => _layouts ??= Load();

    /// <summary>Null for an unknown/missing id — ShipSim.ApplyLayout treats null as a no-op,
    /// same safe-fallback spirit as ItemCatalog.MaxStackSize's missing-entry default. No warning
    /// here (unlike a missing Data/Ships/layouts.json in <see cref="Load"/>) since a per-key miss
    /// is a normal, silent no-op path exercised by every seeded test.</summary>
    public static ShipLayoutDefinition? TryGet(string layoutId) =>
        Layouts.GetValueOrDefault(layoutId);

    /// <summary>Test-only seam: lets Scavengineers.Scripts.Tests seed the catalog directly,
    /// bypassing <see cref="Load"/>'s Godot.FileAccess call, which needs a running engine.</summary>
    internal static void SeedForTests(Dictionary<string, ShipLayoutDefinition> layouts) => _layouts = layouts;

    /// <summary>Test-only seam: clears the seeded/cached catalog between tests so one test's
    /// seed data can't leak into the next.</summary>
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
