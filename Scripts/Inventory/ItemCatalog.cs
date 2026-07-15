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
    private static readonly Color DefaultColor = new(0.6f, 0.6f, 0.6f);

    private static Dictionary<string, ItemDefinition>? _items;

    /// <summary>Safe fallback (1) for an unknown item id — a missing/renamed catalog entry
    /// degrades to "doesn't stack" rather than crashing or throwing, same spirit as the
    /// save-schema doc's "missing-ID fallback is a placeholder + log, never a crash."</summary>
    public static int MaxStackSize(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.MaxStackSize : DefaultMaxStackSize;

    /// <summary>Used only by the inventory panel UI's slot icons (see InventorySlotUI) — a
    /// second, independent consumer of per-item color, deliberately not a retrofit of the
    /// existing hand-authored 3D pickup materials in World.tscn. Neutral gray fallback for an
    /// unknown/colorless id.</summary>
    public static Color Color(string itemId) =>
        Items.TryGetValue(itemId, out var item) && item.Color is { } hex && !string.IsNullOrEmpty(hex)
            ? new Color(hex)
            : DefaultColor;

    /// <summary>Duplicates `template` (so callers sharing one exported material resource don't all
    /// recolor together — Godot Resources are reference types) and tints the copy to this item's
    /// own <see cref="Color"/> — used by every code-spawned world pickup (InventoryOverflow.DropAt,
    /// Player.SpawnDroppedContainer) so a dropped item's world visual always matches its inventory
    /// slot color, regardless of which shared "generic pickup" material it was spawned with.</summary>
    public static Material TintedMaterial(string itemId, Material? template)
    {
        var material = template is StandardMaterial3D standard ? (StandardMaterial3D)standard.Duplicate() : new StandardMaterial3D();
        material.AlbedoColor = Color(itemId);
        return material;
    }

    /// <summary>How much Player.UseHeldItem's Eat restores when this item is consumed — 0 (the
    /// default) for a non-food item, same safe-fallback spirit as <see cref="MaxStackSize"/>.</summary>
    public static float HungerRestore(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.HungerRestore : 0f;

    /// <summary>How much Player.UseHeldItem's Drink restores when this item is consumed — 0 (the
    /// default) for a non-drink item.</summary>
    public static float ThirstRestore(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.ThirstRestore : 0f;

    /// <summary>Whether this item is a light source Player.UseHeldItem's F-key toggle should turn
    /// on/off — false (the default) for anything that isn't one, same safe-fallback spirit as
    /// <see cref="MaxStackSize"/>.</summary>
    public static bool IsToggleableLight(string itemId) =>
        Items.TryGetValue(itemId, out var item) && item.IsToggleableLight;

    private static Dictionary<string, ItemDefinition> Items => _items ??= Load();

    /// <summary>Test-only seam: lets Scavengineers.Scripts.Tests seed the catalog directly,
    /// bypassing <see cref="Load"/>'s Godot.FileAccess call, which needs a running engine.</summary>
    internal static void SeedForTests(Dictionary<string, ItemDefinition> items) => _items = items;

    /// <summary>Test-only seam: clears the seeded/cached catalog between tests so one test's
    /// seed data can't leak into the next.</summary>
    internal static void ResetForTests() => _items = null;

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

    internal sealed class ItemDefinition
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("maxStackSize")]
        public int MaxStackSize { get; set; } = DefaultMaxStackSize;

        [JsonPropertyName("color")]
        public string? Color { get; set; }

        [JsonPropertyName("hungerRestore")]
        public float HungerRestore { get; set; }

        [JsonPropertyName("thirstRestore")]
        public float ThirstRestore { get; set; }

        [JsonPropertyName("isToggleableLight")]
        public bool IsToggleableLight { get; set; }
    }
}
