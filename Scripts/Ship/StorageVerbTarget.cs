using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Godot;
using Scavengineers.Sim.Grid;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// A player-installable shelf/bin — same "many per ship, one per wall edge" shape as
/// <see cref="ThrusterVerbTarget"/>, minus its N2-transfer tick (storage has nothing to tick).
/// Unlike Thruster's single fixed-size tank slot, <see cref="Contents"/> is genuinely variable
/// per instance — sized by <c>ShipBuildTarget.InstallStorage</c> before this ever enters the
/// tree, from whichever catalog item (<see cref="ItemId"/>) was actually installed.
/// </summary>
public partial class StorageVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    private static readonly Verb OpenStorageVerb = new("open_storage", "VERB_OPEN_STORAGE", DurationSeconds: 0f);

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>Set by ShipBuildTarget when it spawns this instance — see ThrusterVerbTarget's
    /// own BuildTarget for why this is needed (Uninstall/Scrap reachable while aiming at the
    /// storage unit's own box, not just bare wall space next to it).</summary>
    [Export]
    public ShipBuildTarget? BuildTarget { get; set; }

    /// <summary>This specific unit's own Deck fixture id — derived by ShipBuildTarget from its
    /// normalized mounting edge, same shape as ThrusterVerbTarget.FixtureId.</summary>
    public string FixtureId { get; set; } = "";

    /// <summary>This unit's own mounting edge, set once at spawn — needed so ExecuteVerb can
    /// hand it back to ShipBuildTarget.ExecuteStorageRemoval, which looks placement up by edge
    /// rather than by fixture id.</summary>
    public CellCoord EdgeA { get; set; }

    public CellCoord EdgeB { get; set; }

    /// <summary>Which catalog item this specific instance is ("small_bin"/"shelf"/"large_shelf")
    /// — drives DisplayNameKey, the refund item on Uninstall, and (at fresh-install time only —
    /// see ShipBuildTarget.InstallStorage) the slot count.</summary>
    public string ItemId { get; set; } = "";

    /// <summary>Public setter, unlike Thruster's single fixed-size Contents — slot count is
    /// genuinely variable per instance.</summary>
    public SlotContainer Contents { get; set; } = new(0);

    [Export]
    public string SaveId { get; set; } = "";

    /// <summary>Reuses the item's own pickup-name key directly rather than a parallel OBJECT_*
    /// key — every existing machine's OBJECT_* key is textually identical to its ITEM_* key
    /// anyway (compare OBJECT_THRUSTER/ITEM_THRUSTER in strings.csv), so a second key per
    /// storage tier would be pure duplication.</summary>
    public string? DisplayNameKey => $"ITEM_{ItemId.ToUpperInvariant()}";

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public IReadOnlyList<Verb> AvailableVerbs =>
        new List<Verb> { OpenStorageVerb }
            .Concat(BuildTarget?.StorageRemovalVerbs ?? [])
            .ToList();

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == OpenStorageVerb.Id)
        {
            if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
            {
                player.OpenStorageInventory(this);
            }

            return;
        }

        BuildTarget?.ExecuteStorageRemoval(EdgeA, EdgeB, verb, inventory);
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }

    /// <summary>The whole Contents grid as one string: "itemId,count,charge;itemId,count,charge;
    /// ;..." (an empty segment means an empty slot) — segment count IS the slot count, so a
    /// reload (see ApplySaveState) never needs to re-derive it from ItemCatalog.</summary>
    public string GetSaveState() =>
        string.Join(';', Contents.Slots.Select(slot =>
            slot is { } s ? $"{s.ItemId},{s.Count},{s.Charge.ToString(CultureInfo.InvariantCulture)}" : ""));

    public void ApplySaveState(string state)
    {
        var segments = state.Split(';');
        Contents = new SlotContainer(segments.Length);
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length == 0)
            {
                continue;
            }

            var parts = segments[i].Split(',');
            Contents.SetSlot(i, (parts[0], int.Parse(parts[1]), float.Parse(parts[2], CultureInfo.InvariantCulture)));
        }
    }
}
