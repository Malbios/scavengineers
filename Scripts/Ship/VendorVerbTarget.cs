using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Shop;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// The Station's vendor — a person, not a machine (see World.tscn's ShopFigure, previously just
/// idle-animated set dressing with no interaction of its own). A single always-available verb
/// opens the shop panel (see Player.OpenShop) instead of cycling one Buy/Sell verb per item at a
/// time — the panel's Buy tab lists every item in <see cref="ItemCatalog.TradeableItemIds"/>,
/// greyed out while unaffordable or with no room to carry it; the Sell tab lists the same catalog,
/// greyed out for anything you don't currently hold. Buy is always pricier than Sell so there's no
/// trivial buy-then-sell arbitrage loop (enforced by ItemsJsonTradePriceTests).
///
/// Prices and the tradeable-item set both come from Data/items.json via ItemCatalog, not from a
/// table here. They used to be a hardcoded price dict indexed by a *separate* hardcoded list
/// (Player.HotbarItems) — two hand-maintained lists in two files, where adding a hotbar item
/// without a matching price threw KeyNotFoundException the moment the shop panel opened.
/// </summary>
public partial class VendorVerbTarget : StaticBody3D, IVerbTarget
{
    private static readonly Verb ShopVerb = new("open_shop", "VERB_TRADE", DurationSeconds: 0f);

    public string? DisplayNameKey => "OBJECT_VENDOR";

    public float? CurrentVerbProgress => null;

    public IReadOnlyList<Verb> AvailableVerbs => [ShopVerb];

    /// <summary>Opens the shop panel instead of executing a buy/sell directly — same "no bespoke
    /// per-object input path" shape TravelConsoleVerbTarget's ExecuteVerb already uses for its own
    /// map. Player still just calls ExecuteVerb generically.</summary>
    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != ShopVerb.Id)
        {
            return;
        }

        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
        {
            player.OpenShop(this);
        }
    }

    public void CancelVerb()
    {
    }

    /// <summary>Every purchasable item, Disabled when unaffordable or with no room to carry it —
    /// shown rather than hidden, so the full catalog is always visible. Driven by
    /// ItemCatalog.TradeableItemIds (i.e. by items.json), not by a separate hardcoded list: the
    /// item set and its prices are now one source, so an item can't be listed without a price.</summary>
    public IReadOnlyList<ShopEntry> BuildBuyEntries()
    {
        var player = GetPlayer();
        return ItemCatalog.TradeableItemIds
            .Select(itemId =>
            {
                var price = ItemCatalog.BuyPrice(itemId);
                return new ShopEntry(itemId, $"ITEM_{itemId.ToUpperInvariant()}", price,
                    Disabled: player is null || player.Credits < price || !player.Inventory.HasRoomFor(itemId, 1));
            })
            .ToList();
    }

    /// <summary>Every sellable item, Disabled when you own none — same "always visible, greyed
    /// when not possible right now" symmetry Buy already has.</summary>
    public IReadOnlyList<ShopEntry> BuildSellEntries()
    {
        var player = GetPlayer();
        return ItemCatalog.TradeableItemIds
            .Select(itemId => new ShopEntry(itemId, $"ITEM_{itemId.ToUpperInvariant()}", ItemCatalog.SellPrice(itemId),
                Disabled: player is null || !player.Inventory.Has(itemId, 1)))
            .ToList();
    }

    /// <summary>Called back from the shop panel's Buy button. Checked again here, not just via
    /// the entry's Disabled flag above — refuses the purchase cleanly (credits untouched) rather
    /// than spending credits for an item that then has nowhere to go. An untradeable item id
    /// (BuyPrice 0) is refused outright rather than sold for free.</summary>
    public bool TryBuy(string itemId)
    {
        var price = ItemCatalog.BuyPrice(itemId);
        if (price <= 0)
        {
            return false;
        }

        var player = GetPlayer();
        if (player is null || !player.Inventory.HasRoomFor(itemId, 1) || !player.TrySpendCredits(price))
        {
            return false;
        }

        player.Inventory.Add(itemId, 1);
        return true;
    }

    /// <summary>Called back from the shop panel's Sell button — grants credits for 1 unit at a
    /// time rather than dumping the whole inventory at once. An untradeable item is refused
    /// (and, importantly, not consumed) rather than accepted for 0 credits.</summary>
    public bool TrySell(string itemId)
    {
        var price = ItemCatalog.SellPrice(itemId);
        if (price <= 0)
        {
            return false;
        }

        var player = GetPlayer();
        if (player is null || !player.Inventory.TryRemove(itemId, 1))
        {
            return false;
        }

        player.AddCredits(price);
        return true;
    }

    // Resolved fresh on every access rather than cached in _Ready — Player's own _Ready (which
    // adds it to the "player" group) can run after this node's, depending on scene tree order,
    // so a one-time lookup here can permanently miss it.
    private PlayerScript? GetPlayer() => GetTree().GetFirstNodeInGroup("player") as PlayerScript;
}
