using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Scavengineers.Scripts.Inventory;

/// <summary>
/// The game's item data, loaded from Data/items.json (docs/project-plan.md's "data-driven
/// items" MVP bullet, CLAUDE.md's data-driven non-negotiable) rather than scattered across
/// per-feature C# tables. Deliberately minimal fields for now — Stage 2/3 of the inventory
/// arc (equip slots, containers) add fields here additively when they actually need them,
/// not speculatively ahead of time.
/// </summary>
public static class ItemCatalog
{
    private const string ResourcePath = "res://Data/items.json";
    private const int DefaultMaxStackSize = 1;

    private static Dictionary<string, ItemDefinition>? _items;

    /// <summary>Safe fallback (1) for an unknown item id — a missing/renamed catalog entry
    /// degrades to "doesn't stack" rather than crashing or throwing, same spirit as the
    /// save-schema doc's "missing-ID fallback is a placeholder + log, never a crash."</summary>
    public static int MaxStackSize(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.MaxStackSize : DefaultMaxStackSize;

    private static Dictionary<string, ItemDefinition> Items => _items ??= Load();

    private static Dictionary<string, ItemDefinition> Load()
    {
        if (!Godot.FileAccess.FileExists(ResourcePath))
        {
            GD.PushWarning($"[ItemCatalog] {ResourcePath} not found — every item will fall back to a stack size of {DefaultMaxStackSize}.");
            return new Dictionary<string, ItemDefinition>();
        }

        var json = Godot.FileAccess.GetFileAsString(ResourcePath);
        var definitions = JsonSerializer.Deserialize<List<ItemDefinition>>(json) ?? [];
        return definitions.ToDictionary(d => d.Id);
    }

    private sealed class ItemDefinition
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("maxStackSize")]
        public int MaxStackSize { get; set; } = DefaultMaxStackSize;
    }
}
