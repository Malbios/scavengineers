using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Shop;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>The Station's vendor. A single always-available verb opens the shop panel instead of
/// cycling one Buy/Sell verb per item — the Buy tab lists every item in
/// <see cref="ItemCatalog.TradeableItemIds"/>, greyed out while unaffordable or with no room; the
/// Sell tab lists the same catalog, greyed out for anything not held. Buy is always pricier than
/// Sell (enforced by ItemsJsonTradePriceTests). Prices and the tradeable-item set both come from
/// Data/items.json via ItemCatalog, not a separate table here.</summary>
public partial class VendorVerbTarget : StaticBody3D, IVerbTarget
{
    private static readonly Verb ShopVerb = new("open_shop", "VERB_TRADE", DurationSeconds: 0f);

    public string? DisplayNameKey => "OBJECT_VENDOR";

    public float? CurrentVerbProgress => null;

    public IReadOnlyList<Verb> AvailableVerbs => [ShopVerb];

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

    /// <summary>Every purchasable item, Disabled (not hidden) when unaffordable or with no room to
    /// carry it, so the full catalog is always visible.</summary>
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

    public IReadOnlyList<ShopEntry> BuildSellEntries()
    {
        var player = GetPlayer();
        return ItemCatalog.TradeableItemIds
            .Select(itemId => new ShopEntry(itemId, $"ITEM_{itemId.ToUpperInvariant()}", ItemCatalog.SellPrice(itemId),
                Disabled: player is null || !player.Inventory.Has(itemId, 1)))
            .ToList();
    }

    /// <summary>Re-checked here, not just via the entry's Disabled flag — refuses the purchase
    /// cleanly (credits untouched) rather than spending credits for an item with nowhere to go.</summary>
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

    /// <summary>Grants credits for 1 unit at a time. An untradeable item is refused and not
    /// consumed, rather than accepted for 0 credits.</summary>
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
