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

    /// <summary>The same definitions as <see cref="Items"/>, in the order Data/items.json declares
    /// them — <see cref="StorageItemIds"/>/<see cref="TradeableItemIds"/> read this rather than the
    /// dictionary so their ordering is defined by the data file rather than by Dictionary
    /// enumeration order (which .NET explicitly doesn't guarantee). That ordering is player-visible:
    /// it's the order the shop panel lists items in.</summary>
    private static List<ItemDefinition>? _ordered;

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

    /// <summary>Which equip slot this item drags onto (e.g. "torso", "head") — null (the
    /// default) for anything that isn't equippable that way, same safe-fallback spirit as
    /// <see cref="MaxStackSize"/>. Used by Player.TryEquipItemFrom instead of a hardcoded
    /// item-id check, so the equip flow generalizes to any future equippable item.</summary>
    public static string? EquipSlot(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.EquipSlot : null;

    /// <summary>Whether this item can be placed into an ordinary storage slot (a backpack, or
    /// any future non-hand container) — true (the default, and the fallback for an unknown item
    /// id) for almost everything; the EVA suit's torso piece is the one exception (too bulky to
    /// pocket, but still holdable in a hand). Checked by InventorySlotUI's drag-and-drop and
    /// PlayerInventory.Add's passive-fill loop before letting an item land anywhere but Hands.</summary>
    public static bool FitsInStorage(string itemId) =>
        !Items.TryGetValue(itemId, out var item) || item.FitsInStorage;

    /// <summary>Which primitive-composition kind ItemVisualBuilder should use for this item's
    /// world-pickup visual/collision shape — null (the default, and the fallback for an unknown
    /// item id) falls back to ItemVisualBuilder's own plain-box default, same safe-fallback
    /// spirit as <see cref="MaxStackSize"/>.</summary>
    public static string? ShapeKind(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.ShapeKind : null;

    /// <summary>Inventory slots available when this item is installed as ship storage (a shelf/
    /// bin — see ShipBuildTarget.InstallStorage) — 0 (the default) for anything that isn't
    /// installable storage, same safe-fallback spirit as <see cref="MaxStackSize"/>.</summary>
    public static int StorageSlotCount(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.StorageSlotCount : 0;

    /// <summary>Every item id with a nonzero <see cref="StorageSlotCount"/> — ShipBuildTarget's
    /// storage install verbs are generated from this instead of a hardcoded list, so a new
    /// storage tier needs only an items.json entry, no code change.</summary>
    public static IReadOnlyCollection<string> StorageItemIds =>
        Ordered.Where(d => d.StorageSlotCount > 0).Select(d => d.Id).ToList();

    /// <summary>What the vendor charges to sell this item to the player — 0 (the default, and the
    /// fallback for an unknown item id) means "not for sale", which is what keeps this off tools,
    /// debug items and quest items without needing a second exclusion list. Buy is always higher
    /// than <see cref="SellPrice"/> so there's no trivial buy-then-sell arbitrage loop.</summary>
    public static int BuyPrice(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.BuyPrice : 0;

    /// <summary>What the vendor pays the player for this item — same 0-means-untradeable
    /// convention as <see cref="BuyPrice"/>.</summary>
    public static int SellPrice(string itemId) =>
        Items.TryGetValue(itemId, out var item) ? item.SellPrice : 0;

    /// <summary>Every item the vendor trades, in items.json order — VendorVerbTarget's Buy/Sell
    /// lists are generated from this instead of a hardcoded price table paired with a separate
    /// hardcoded item list, so a new tradeable item needs only an items.json entry. An item is
    /// tradeable iff it has a nonzero <see cref="BuyPrice"/>, same "the data decides" shape as
    /// <see cref="StorageItemIds"/>.</summary>
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

    /// <summary>Test-only seam: lets Scavengineers.Scripts.Tests seed the catalog directly,
    /// bypassing <see cref="Load"/>'s Godot.FileAccess call, which needs a running engine.
    /// Dictionary order is whatever the caller's own dictionary enumerates as — a seeding test
    /// that cares about <see cref="TradeableItemIds"/>/<see cref="StorageItemIds"/> ordering
    /// should use <see cref="SeedForTests(List{ItemDefinition})"/> instead.</summary>
    internal static void SeedForTests(Dictionary<string, ItemDefinition> items)
    {
        _ordered = items.Values.ToList();
        _items = items;
    }

    /// <summary>Ordered counterpart of <see cref="SeedForTests(Dictionary{string, ItemDefinition})"/>
    /// — mirrors the real <see cref="Load"/> path exactly (a list, keyed afterwards), so a test can
    /// assert on the data-file-order-dependent id lists. Also the seam Scavengineers.NodeTests uses:
    /// that project is its own Godot project with no res://Data/ of its own, so the real Load()
    /// always yields an empty catalog there (see its own project.godot).</summary>
    internal static void SeedForTests(List<ItemDefinition> items)
    {
        _ordered = items;
        _items = items.ToDictionary(d => d.Id);
    }

    /// <summary>Test-only seam: clears the seeded/cached catalog between tests so one test's
    /// seed data can't leak into the next.</summary>
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

        /// <summary>0 (the default) means untradeable — see <see cref="BuyPrice(string)"/>.</summary>
        [JsonPropertyName("buyPrice")]
        public int BuyPrice { get; set; }

        [JsonPropertyName("sellPrice")]
        public int SellPrice { get; set; }
    }
}
