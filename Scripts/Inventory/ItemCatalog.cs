using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Godot;

namespace Scavengineers.Scripts.Inventory;

/// <summary>The game's item data, loaded from Data/items.json rather than scattered across
/// per-feature C# tables (CLAUDE.md's data-driven non-negotiable).</summary>
public static class ItemCatalog
{
    private const string ResourcePath = "res://Data/items.json";
    private const int DefaultMaxStackSize = 1;
    private static readonly Color DefaultColor = new(0.6f, 0.6f, 0.6f);

    private static Dictionary<string, ItemDefinition>? _items;

    /// <summary>Same definitions as <see cref="Items"/>, in Data/items.json's declared order —
    /// unlike Dictionary enumeration order, which .NET doesn't guarantee, and which would silently
    /// reshuffle the shop panel's item listing.</summary>
    private static List<ItemDefinition>? _ordered;

    /// <summary>Unknown/renamed item id falls back to 1 ("doesn't stack") rather than throwing —
    /// every other per-item lookup below falls back the same way, to its own type's zero value.</summary>
    public static int MaxStackSize(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.MaxStackSize : DefaultMaxStackSize;

    public static Color Color(string itemId) =>
        Items.TryGetValue(itemId, out var item) && item.Color is { } hex && !string.IsNullOrEmpty(hex)
            ? new Color(hex)
            : DefaultColor;

    /// <summary>Duplicates `template` before tinting it — Godot Resources are reference types, so
    /// recoloring the shared instance directly would recolor every other pickup using it too.</summary>
    public static Material TintedMaterial(string itemId, Material? template)
    {
        var material = template is StandardMaterial3D standard ? (StandardMaterial3D)standard.Duplicate() : new StandardMaterial3D();
        material.AlbedoColor = Color(itemId);
        return material;
    }

    public static float HungerRestore(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.HungerRestore : 0f;

    public static float ThirstRestore(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.ThirstRestore : 0f;

    public static bool IsToggleableLight(string itemId) =>
        Items.TryGetValue(itemId, out var item) && item.IsToggleableLight;

    public static string? EquipSlot(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.EquipSlot : null;

    /// <summary>The EVA suit's torso piece is the one item that isn't storage-eligible (too bulky
    /// to pocket, but still holdable in a hand) — everything else, including an unknown id,
    /// defaults to true.</summary>
    public static bool FitsInStorage(string itemId) =>
        !Items.TryGetValue(itemId, out var item) || item.FitsInStorage;

    public static string? ShapeKind(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.ShapeKind : null;

    public static int StorageSlotCount(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.StorageSlotCount : 0;

    public static IReadOnlyCollection<string> StorageItemIds =>
        Ordered.Where(d => d.StorageSlotCount > 0).Select(d => d.Id).ToList();

    /// <summary>0 means "not for sale" — keeps tools, debug items and quest items untradeable
    /// without a second exclusion list. Always higher than <see cref="SellPrice"/>, so there's no
    /// buy-then-sell arbitrage loop.</summary>
    public static int BuyPrice(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.BuyPrice : 0;

    public static int SellPrice(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.SellPrice : 0;

    /// <summary>Every item with a nonzero <see cref="BuyPrice"/>, in items.json order.</summary>
    public static IReadOnlyList<string> TradeableItemIds =>
        Ordered.Where(d => d.BuyPrice > 0).Select(d => d.Id).ToList();

    private static Dictionary<string, ItemDefinition> Items
    {
        get
        {
            EnsureLoaded();
            return _items!;
        }
    }

    private static List<ItemDefinition> Ordered
    {
        get
        {
            EnsureLoaded();
            return _ordered!;
        }
    }

    private static void EnsureLoaded()
    {
        if (_items is not null)
        {
            return;
        }

        _ordered = Load();
        _items = _ordered.ToDictionary(d => d.Id);
    }

    /// <summary>Test-only seam bypassing <see cref="Load"/>'s Godot.FileAccess call. Dictionary
    /// enumeration order is unspecified — a test asserting on <see cref="TradeableItemIds"/>/
    /// <see cref="StorageItemIds"/> ordering should use <see cref="SeedForTests(List{ItemDefinition})"/>
    /// instead.</summary>
    internal static void SeedForTests(Dictionary<string, ItemDefinition> items)
    {
        _ordered = items.Values.ToList();
        _items = items;
    }

    internal static void SeedForTests(List<ItemDefinition> items)
    {
        _ordered = items;
        _items = items.ToDictionary(d => d.Id);
    }

    internal static void ResetForTests()
    {
        _items = null;
        _ordered = null;
    }

    private static List<ItemDefinition> Load()
    {
        if (!Godot.FileAccess.FileExists(ResourcePath))
        {
            GD.PushWarning($"[ItemCatalog] {ResourcePath} not found — every item will fall back to a stack size of {DefaultMaxStackSize}.");
            return [];
        }

        var json = Godot.FileAccess.GetFileAsString(ResourcePath);
        return JsonSerializer.Deserialize<List<ItemDefinition>>(json) ?? [];
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

        [JsonPropertyName("equipSlot")]
        public string? EquipSlot { get; set; }

        [JsonPropertyName("fitsInStorage")]
        public bool FitsInStorage { get; set; } = true;

        [JsonPropertyName("shapeKind")]
        public string? ShapeKind { get; set; }

        [JsonPropertyName("storageSlotCount")]
        public int StorageSlotCount { get; set; }

        [JsonPropertyName("buyPrice")]
        public int BuyPrice { get; set; }

        [JsonPropertyName("sellPrice")]
        public int SellPrice { get; set; }
    }
}
