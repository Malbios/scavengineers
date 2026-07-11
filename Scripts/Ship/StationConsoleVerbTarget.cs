using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// The Station's trade console. One Buy verb per item in <see cref="PlayerScript.HotbarItems"/>
/// (cycled via the same scroll-wheel verb selection every multi-verb target already uses, e.g.
/// DamagedConduitVerbTarget's Repair/Scrap), shown only while affordable and spending credits;
/// one Sell verb per item you actually hold, granting credits for 1 unit at a time rather than
/// dumping the whole inventory at once. Buy is always pricier than Sell so there's no trivial
/// buy-then-sell arbitrage loop.
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
    };

    private static readonly IReadOnlyList<Verb> BuyVerbs = PlayerScript.HotbarItems
        .Select(itemId => new Verb($"buy_{itemId}", $"VERB_BUY_{itemId.ToUpperInvariant()}", DurationSeconds: 0f)
        {
            DisplaySuffix = $"{Prices[itemId].Buy}cr",
        })
        .ToList();

    private static readonly IReadOnlyList<Verb> SellVerbs = PlayerScript.HotbarItems
        .Select(itemId => new Verb($"sell_{itemId}", $"VERB_SELL_{itemId.ToUpperInvariant()}", DurationSeconds: 0f)
        {
            DisplaySuffix = $"{Prices[itemId].Sell}cr",
        })
        .ToList();

    public string? DisplayNameKey => "OBJECT_TRADE_CONSOLE";

    public float? CurrentVerbProgress => null;

    public IReadOnlyList<Verb> AvailableVerbs
    {
        get
        {
            var player = GetPlayer();
            if (player is null)
            {
                return [];
            }

            var verbs = new List<Verb>();

            // Buy always shows, even unaffordable or with no room to carry it — Disabled
            // (rendered red by Player's HUD) signals "not possible right now" instead of hiding
            // the option entirely.
            verbs.AddRange(BuyVerbs.Select(v => v with
            {
                Disabled = player.Credits < Prices[ItemIdOf(v)].Buy || !player.Inventory.HasRoomFor(ItemIdOf(v), 1),
            }));
            verbs.AddRange(SellVerbs.Where(v => player.Inventory.Has(ItemIdOf(v), 1)));

            return verbs;
        }
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        var player = GetPlayer();
        if (player is null)
        {
            return;
        }

        if (SellVerbs.Any(v => v.Id == verb.Id))
        {
            var itemId = ItemIdOf(verb);
            if (inventory.TryRemove(itemId, 1))
            {
                player.AddCredits(Prices[itemId].Sell);
            }

            return;
        }

        if (BuyVerbs.Any(v => v.Id == verb.Id))
        {
            var itemId = ItemIdOf(verb);

            // Checked again here, not just via Disabled above — refuses the purchase cleanly
            // (credits untouched) rather than spending credits for an item that then has
            // nowhere to go.
            if (inventory.HasRoomFor(itemId, 1) && player.TrySpendCredits(Prices[itemId].Buy))
            {
                inventory.Add(itemId, 1);
            }
        }
    }

    public void CancelVerb()
    {
    }

    private static string ItemIdOf(Verb verb) => verb.Id[(verb.Id.IndexOf('_') + 1)..];

    // Resolved fresh on every access rather than cached in _Ready — Player's own _Ready (which
    // adds it to the "player" group) can run after this node's, depending on scene tree order,
    // so a one-time lookup here can permanently miss it.
    private PlayerScript? GetPlayer() => GetTree().GetFirstNodeInGroup("player") as PlayerScript;
}
