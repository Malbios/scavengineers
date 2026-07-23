using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Godot;

namespace Scavengineers.Scripts.Travel;

/// <summary>
/// The strategic layer's location list, loaded from Data/destinations.json — which places exist,
/// what they're called, and where they sit on the travel map.
///
/// <para>This is the first half of the strategic/tactical split in
/// docs/architecture/space-and-travel.md: "a map/graph of locations — pure data, no physics". It
/// replaced <c>TravelConsoleVerbTarget</c>'s <c>StationMapPositions</c>/<c>DerelictMapPositions</c>
/// arrays and its hardcoded <c>OBJECT_STATION</c> / <c>$"OBJECT_DERELICT_{i + 1}"</c> label
/// construction, which meant a destination's *identity* was spread across two inspector arrays and
/// a string built by index.</para>
///
/// <para>The destination *ordering* here is load-bearing and must not be reshuffled: a destination
/// is addressed by its index across the whole list (stations first, then derelicts), and that index
/// is what save files and accepted contracts store. Appending is safe; reordering or removing
/// silently repoints an in-flight CargoDelivery at the wrong place. Same stable-id discipline as
/// docs/architecture/save-schema.md's content-ID rule, applied to positions in a list.</para>
///
/// <para>The *node* side — which scene subtree each destination is, and its airlock/build-target
/// references — is still scene-authored on the travel console (see its own doc comment). Moving
/// that here is what runtime instancing would need.</para>
/// </summary>
public static class DestinationCatalog
{
    private const string ResourcePath = "res://Data/destinations.json";

    private static List<DestinationDefinition>? _destinations;

    /// <summary>Every destination, stations first then derelicts, in the order their indices
    /// address them.</summary>
    public static IReadOnlyList<DestinationDefinition> All => Destinations;

    public static int StationCount => Destinations.Count(d => d.IsStation);

    public static int DerelictCount => Destinations.Count(d => !d.IsStation);

    /// <summary>Null for an index outside the list — a caller resolving an unknown destination
    /// degrades rather than throwing, same safe-fallback spirit as ItemCatalog/ShipLayoutCatalog.</summary>
    public static DestinationDefinition? At(int index) =>
        index >= 0 && index < Destinations.Count ? Destinations[index] : null;

    private static List<DestinationDefinition> Destinations => _destinations ??= Load();

    /// <summary>Test-only seam, matching ItemCatalog/ShipLayoutCatalog's own — Load needs a running
    /// Godot engine, and Scavengineers.NodeTests has no res://Data/ of its own.</summary>
    internal static void SeedForTests(List<DestinationDefinition> destinations) => _destinations = destinations;

    internal static void ResetForTests() => _destinations = null;

    private static List<DestinationDefinition> Load()
    {
        if (!Godot.FileAccess.FileExists(ResourcePath))
        {
            GD.PushWarning($"[DestinationCatalog] {ResourcePath} not found — the travel map will be empty.");
            return [];
        }

        var json = Godot.FileAccess.GetFileAsString(ResourcePath);
        var loaded = JsonSerializer.Deserialize<List<DestinationDefinition>>(json) ?? [];

        // Stations must precede derelicts: the whole index scheme (0..StationCount-1 = stations,
        // the rest derelicts) depends on it, and a data file that got this wrong would misroute
        // every arrival rather than failing visibly.
        var firstDerelict = loaded.FindIndex(d => !d.IsStation);
        if (firstDerelict >= 0 && loaded.Skip(firstDerelict).Any(d => d.IsStation))
        {
            GD.PushWarning("[DestinationCatalog] Stations must all come before derelicts in destinations.json — indices will be wrong.");
        }

        return loaded;
    }

    public sealed class DestinationDefinition
    {
        /// <summary>Stable, never-reused identifier. Not currently a save key (destinations are
        /// addressed by index today) but named per the save-schema rule so it can become one
        /// without a migration.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>"station" or "derelict" — the two behave differently on arrival (a station
        /// settles debt and has its own destination-side airlock door; a derelict is a salvage
        /// target), so it's a real distinction rather than a label.</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "derelict";

        public bool IsStation => Kind == "station";

        [JsonPropertyName("nameKey")]
        public string NameKey { get; set; } = "";

        [JsonPropertyName("mapX")]
        public float MapX { get; set; }

        [JsonPropertyName("mapY")]
        public float MapY { get; set; }

        public Vector2 MapPosition => new(MapX, MapY);
    }
}
