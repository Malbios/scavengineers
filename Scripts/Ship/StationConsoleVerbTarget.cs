using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Shop;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// The Station's trade console. A single always-available verb opens the shop panel (see
/// Player.OpenShop) instead of cycling one Buy/Sell verb per item at a time — the panel's Buy tab
/// lists every item in <see cref="PlayerScript.HotbarItems"/>, greyed out while unaffordable or
/// with no room to carry it; the Sell tab lists the same catalog, greyed out for anything you
/// don't currently hold. Buy is always pricier than Sell so there's no trivial buy-then-sell
/// arbitrage loop.
/// </summary>
public partial class StationConsoleVerbTarget : StaticBody3D, IVerbTarget
{
    private static readonly Dictionary<string, (int Buy, int Sell)> Prices = new()
    {
        ["scrap_metal"] = (5, 2),
        ["spare_parts"] = (15, 6),
        ["wall_panel"] = (10, 4),
        ["power_cell"] = (12, 5),
        ["battery"] = (40, 15),
        ["switch"] = (10, 4),
        ["recharge_station"] = (30, 12),
        ["backpack"] = (25, 10),
        ["ration_bar"] = (8, 3),
        ["water_bottle"] = (6, 2),
        ["wrench"] = (15, 6),
        ["pda"] = (20, 8),
        ["health_scan_cartridge"] = (10, 4),
    };

    private static readonly Verb ShopVerb = new("open_shop", "VERB_TRADE", DurationSeconds: 0f);

    public string? DisplayNameKey => "OBJECT_TRADE_CONSOLE";

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
    /// shown rather than hidden, so the full catalog is always visible.</summary>
    public IReadOnlyList<ShopEntry> BuildBuyEntries()
    {
        var player = GetPlayer();
        return PlayerScript.HotbarItems
            .Select(itemId => new ShopEntry(itemId, $"ITEM_{itemId.ToUpperInvariant()}", Prices[itemId].Buy,
                Disabled: player is null || player.Credits < Prices[itemId].Buy || !player.Inventory.HasRoomFor(itemId, 1)))
            .ToList();
    }

    /// <summary>Every sellable item, Disabled when you own none — same "always visible, greyed
    /// when not possible right now" symmetry Buy already has.</summary>
    public IReadOnlyList<ShopEntry> BuildSellEntries()
    {
        var player = GetPlayer();
        return PlayerScript.HotbarItems
            .Select(itemId => new ShopEntry(itemId, $"ITEM_{itemId.ToUpperInvariant()}", Prices[itemId].Sell,
                Disabled: player is null || !player.Inventory.Has(itemId, 1)))
            .ToList();
    }

    /// <summary>Called back from the shop panel's Buy button. Checked again here, not just via
    /// the entry's Disabled flag above — refuses the purchase cleanly (credits untouched) rather
    /// than spending credits for an item that then has nowhere to go.</summary>
    public bool TryBuy(string itemId)
    {
        var player = GetPlayer();
        if (player is null || !player.Inventory.HasRoomFor(itemId, 1) || !player.TrySpendCredits(Prices[itemId].Buy))
        {
            return false;
        }

        player.Inventory.Add(itemId, 1);
        return true;
    }

    /// <summary>Called back from the shop panel's Sell button — grants credits for 1 unit at a
    /// time rather than dumping the whole inventory at once.</summary>
    public bool TrySell(string itemId)
    {
        var player = GetPlayer();
        if (player is null || !player.Inventory.TryRemove(itemId, 1))
        {
            return false;
        }

        player.AddCredits(Prices[itemId].Sell);
        return true;
    }

    // Resolved fresh on every access rather than cached in _Ready — Player's own _Ready (which
    // adds it to the "player" group) can run after this node's, depending on scene tree order,
    // so a one-time lookup here can permanently miss it.
    private PlayerScript? GetPlayer() => GetTree().GetFirstNodeInGroup("player") as PlayerScript;
}
