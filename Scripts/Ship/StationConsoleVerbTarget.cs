using System.Collections.Generic;
using System.Linq;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// A stub trade console — no economy yet, just item sinks/sources so the "return with loot"
/// beat has somewhere to go. One Buy verb per item in <see cref="PlayerScript.HotbarItems"/>
/// (cycled via the same scroll-wheel verb selection every multi-verb target already uses, e.g.
/// DamagedConduitVerbTarget's Repair/Scrap), each granting 1 of that item for free. Sell empties
/// the whole inventory. Neither costs/grants currency yet.
/// </summary>
public partial class StationConsoleVerbTarget : StaticBody3D, IVerbTarget
{
    private static readonly IReadOnlyList<Verb> BuyVerbs = PlayerScript.HotbarItems
        .Select(itemId => new Verb($"buy_{itemId}", $"VERB_BUY_{itemId.ToUpperInvariant()}", DurationSeconds: 0f))
        .ToList();

    private static readonly Verb SellVerb = new("sell", "VERB_SELL", DurationSeconds: 0f);

    public string? DisplayNameKey => "OBJECT_TRADE_CONSOLE";

    public float? CurrentVerbProgress => null;

    public IReadOnlyList<Verb> AvailableVerbs
    {
        get
        {
            var verbs = new List<Verb>(BuyVerbs);

            if (GetPlayer() is { Inventory.Counts.Count: > 0 })
            {
                verbs.Add(SellVerb);
            }

            return verbs;
        }
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == SellVerb.Id)
        {
            inventory.Clear();
            return;
        }

        if (BuyVerbs.FirstOrDefault(v => v.Id == verb.Id) is { } buyVerb)
        {
            inventory.Add(buyVerb.Id["buy_".Length..], 1);
        }
    }

    public void CancelVerb()
    {
    }

    // Resolved fresh on every access rather than cached in _Ready — Player's own _Ready (which
    // adds it to the "player" group) can run after this node's, depending on scene tree order,
    // so a one-time lookup here can permanently miss it.
    private PlayerScript? GetPlayer() => GetTree().GetFirstNodeInGroup("player") as PlayerScript;
}
