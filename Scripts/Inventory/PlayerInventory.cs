using System;
using System.Collections.Generic;
using System.Linq;

namespace Scavengineers.Scripts.Inventory;

/// <summary>
/// Composes the player's two hands with zero or more simultaneously-worn containers (each its
/// own separate <see cref="SlotContainer"/>), keyed by equip-slot name — a worn container is the
/// first non-fungible item type (it carries real per-instance state, its own contents), so each
/// is tracked as a distinct <see cref="EquippedContainer"/> rather than merged into the hand
/// slots. There is no unrealistic "pockets" storage beyond that: what you're not holding or
/// carrying in a worn container simply isn't on you.
/// </summary>
public sealed class PlayerInventory
{
    // Hands are real slots (not a separate reference like the old _heldItemId) — Has/CountOf/
    // Add/TryRemove/HasRoomFor all iterate them generically, and they're never auto-filled by a
    // passive Add (see Add's own doc) — only an explicit Equip (hotbar key or drag) puts
    // something new into an empty hand.
    public const int HandCount = 2;
    public const int LeftHandSlotIndex = 0;
    public const int RightHandSlotIndex = 1;

    // Placeholder/tunable — how many slots a worn backpack's own contents have.
    public const int BackpackSlotCount = 8;

    // Placeholder/tunable — the EVA suit torso's own 2 unlabeled pocket slots (its 4 specialized
    // tank/filter/battery sub-slots are tracked separately, see SpecializedSlot).
    public const int TorsoSlotCount = 2;

    // The fixed slot-name priority Add/TryRemove/Unequip use when more than one container is
    // worn at once — matches this codebase's original "backpack before hand" behavior exactly
    // when only "back" is worn, and extends predictably as more simultaneously-wearable
    // containers (e.g. the EVA suit's torso piece) are added. "head" is deliberately excluded —
    // a worn helmet carries a 0-slot container (see EquipItemDirectly), so it has nothing to
    // aggregate into anyway.
    private static readonly string[] ContainerPriority = ["back", "torso"];

    private readonly SlotContainer _hands = new(HandCount);

    /// <summary>The two hand slots — read by InventorySlotUI/Player.cs to wire up the panel's
    /// hand slots.</summary>
    public SlotContainer Hands => _hands;

    /// <summary>An item id plus its own separate <see cref="SlotContainer"/> — the shape a
    /// worn container takes while equipped (a backpack, the EVA suit's torso piece, or a
    /// container-less item like the helmet, which simply carries a 0-slot container). Which
    /// items are containers is hardcoded per equip slot (see EquipBackpackFromHand) rather than
    /// a speculative "which items are containers" lookup. Constructed fresh on each read (see
    /// <see cref="GetEquippedContainer"/>) — <see cref="Contents"/> is still the one real shared
    /// instance underneath, so mutating it is visible everywhere; only this wrapper record is
    /// recreated each time.</summary>
    public sealed record EquippedContainer(string ItemId, SlotContainer Contents);

    // Which item (if any) currently occupies each equip slot ("back", "torso", "head").
    private readonly Dictionary<string, string> _equippedItemIds = new();

    // Permanent per-item sub-inventory for container-carrying items ("backpack",
    // "debug_backpack", "eva_torso_suit") — created once, the first time that item is ever
    // equipped, and never destroyed by ordinary equip/unequip (only a genuine world-discard
    // removes an entry — see Player.TryDropInWorld). This is what lets a worn item's contents
    // survive being taken off into a hand, instead of being tied to "currently worn."
    private readonly Dictionary<string, SlotContainer> _persistentContents = new();

    public EquippedContainer? Backpack => GetEquippedContainer("back");

    public EquippedContainer? Torso => GetEquippedContainer("torso");

    public EquippedContainer? Head => GetEquippedContainer("head");

    /// <summary>Generic lookup by equip-slot name — used by Player.cs's generalized
    /// TryEquipItemFrom/TryUnequipItem so a new equip slot (beyond the named
    /// Backpack/Torso/Head convenience properties above) needs no new code here. Every
    /// container-carrying item (even a 0-slot one like the helmet) gets a persistent-contents
    /// entry once equipped — falls back to a fresh 0-slot container in the (should never happen)
    /// case one's somehow missing, so callers never see a null Contents.</summary>
    public EquippedContainer? GetEquippedContainer(string slotName)
    {
        if (!_equippedItemIds.TryGetValue(slotName, out var itemId))
        {
            return null;
        }

        var contents = _persistentContents.GetValueOrDefault(itemId) ?? new SlotContainer(0);
        return new EquippedContainer(itemId, contents);
    }

    /// <summary>The permanent sub-inventory for a container-carrying item, keyed by item id
    /// rather than equip slot — reachable whether the item is currently worn, just held in a
    /// hand, or (for anything that fits storage) sitting in a backpack slot. This is what makes
    /// "preview an item's contents while just carrying it" possible.</summary>
    public SlotContainer? GetPersistentContents(string itemId) => _persistentContents.GetValueOrDefault(itemId);

    /// <summary>Which worn container (if any) is in `slotName`'s equip slot — a small helper the
    /// aggregate methods below (Add/TryRemove/Counts/...) use instead of repeating the two-step
    /// "look up the item id, then its persistent contents" lookup themselves.</summary>
    private SlotContainer? GetWornContents(string slotName) =>
        _equippedItemIds.TryGetValue(slotName, out var itemId) ? _persistentContents.GetValueOrDefault(itemId) : null;

    /// <summary>Un-wears whatever's in `slotName` — the caller decides what happens to the bare
    /// item token (see Player.TryUnequipItem). Deliberately does NOT touch
    /// <see cref="_persistentContents"/> — that's the whole point of the persistent-contents
    /// model: taking something off doesn't destroy or relocate what's inside it.</summary>
    public void ClearEquippedContainer(string slotName) => _equippedItemIds.Remove(slotName);

    /// <summary>Fully forgets an item's persistent contents — the one place they're actually
    /// removed rather than just left dormant while unworn. Used by a genuine world-discard (see
    /// Player.TryDropInWorld) once a dropped world pickup takes ownership of the contents
    /// instead, and by test setup that needs "this item was never acquired" rather than just
    /// "isn't currently worn."</summary>
    public void DiscardPersistentContents(string itemId) => _persistentContents.Remove(itemId);

    /// <summary>A device's own swappable battery/tank — the shape a power drill's, flashlight's,
    /// or (later) an EVA suit tank's per-instance state takes. Deliberately not a generic
    /// slot-level extension to <see cref="SlotContainer"/> (that would touch every inventory
    /// consumer for one tool) — just enough state riding alongside the item that owns it,
    /// wherever it currently sits (hand or worn container), independent of location the same way
    /// a worn container's existence doesn't care which hand it was equipped from.</summary>
    public sealed class SpecializedSlot
    {
        public bool HasItem { get; set; }

        /// <summary>0-1; meaningless while <see cref="HasItem"/> is false.</summary>
        public float Charge { get; set; }
    }

    /// <summary>Which real inventory item id each specialized sub-slot accepts — the one place
    /// that grows when a new sub-slot is added (e.g. the EVA suit's O2/N2/filter/battery tanks),
    /// instead of a new bespoke class + a new branch in every dispatch site.</summary>
    private static readonly Dictionary<string, string> SpecializedSlotAcceptedItemIds = new()
    {
        ["drill_battery"] = "battery",
        ["flashlight_battery"] = "battery",
        ["suit_o2"] = "o2_tank",
        ["suit_n2"] = "n2_tank",
        ["suit_filter"] = "co2_filter",
        ["suit_battery"] = "battery",
    };

    public SpecializedSlot? Drill { get; private set; }

    public SpecializedSlot? Flashlight { get; private set; }

    /// <summary>The EVA suit torso's own tank/filter/battery sub-slots — non-null only while the
    /// torso is worn (attached/detached alongside it, see Player's equip/unequip flow).</summary>
    public SpecializedSlot? SuitO2 { get; private set; }

    public SpecializedSlot? SuitN2 { get; private set; }

    public SpecializedSlot? SuitFilter { get; private set; }

    public SpecializedSlot? SuitBattery { get; private set; }

    /// <summary>Looks up a specialized sub-slot by key — used by <see cref="InventorySlotUI"/>
    /// (via <see cref="InventorySlotUI.SpecializedSlotKey"/>) so a single generic code path can
    /// drive the drill/flashlight battery slots and the EVA suit's tank slots without a bespoke
    /// branch per device.</summary>
    public SpecializedSlot? GetSpecializedSlot(string key) => key switch
    {
        "drill_battery" => Drill,
        "flashlight_battery" => Flashlight,
        "suit_o2" => SuitO2,
        "suit_n2" => SuitN2,
        "suit_filter" => SuitFilter,
        "suit_battery" => SuitBattery,
        _ => null,
    };

    private void SetSpecializedSlot(string key, SpecializedSlot? value)
    {
        switch (key)
        {
            case "drill_battery":
                Drill = value;
                break;
            case "flashlight_battery":
                Flashlight = value;
                break;
            case "suit_o2":
                SuitO2 = value;
                break;
            case "suit_n2":
                SuitN2 = value;
                break;
            case "suit_filter":
                SuitFilter = value;
                break;
            case "suit_battery":
                SuitBattery = value;
                break;
        }
    }

    /// <summary>The accepted item id for a specialized sub-slot key — used by
    /// <see cref="InventorySlotUI"/> to reject a drag before calling
    /// <see cref="InsertIntoSpecializedSlot"/>, so dropping an unrelated item onto e.g. the
    /// drill's battery slot can't spuriously consume an unrelated battery from elsewhere in
    /// inventory.</summary>
    public static string? SpecializedSlotAcceptedItemId(string key) => SpecializedSlotAcceptedItemIds.GetValueOrDefault(key);

    /// <summary>The raw per-slot view of the two hands (see InventorySlotUI) — a worn
    /// backpack's own contents are addressed separately via <see cref="Backpack"/>.Contents,
    /// never merged in here.</summary>
    public IReadOnlyList<(string ItemId, int Count, float Charge)?> Slots => _hands.Slots;

    public IReadOnlyDictionary<string, int> Counts
    {
        get
        {
            var counts = new Dictionary<string, int>(_hands.Counts);
            foreach (var key in ContainerPriority)
            {
                if (GetWornContents(key) is not { } contents)
                {
                    continue;
                }

                foreach (var (itemId, count) in contents.Counts)
                {
                    counts[itemId] = counts.GetValueOrDefault(itemId) + count;
                }
            }

            return counts;
        }
    }

    public int CountOf(string itemId) =>
        _hands.CountOf(itemId) + ContainerPriority.Sum(key => GetWornContents(key)?.CountOf(itemId) ?? 0);

    public bool Has(string itemId, int count) => CountOf(itemId) >= count;

    /// <summary>Whether any item anywhere in inventory (hands or any worn container) matches
    /// `predicate` — e.g. Player's flashlight toggle uses this against
    /// ItemCatalog.IsToggleableLight to find a never-held toggleable light (like the debug
    /// flashlight) without hardcoding its item id.</summary>
    public bool HasAny(Func<string, bool> predicate) =>
        _hands.Slots.Any(slot => slot is { } s && predicate(s.ItemId))
        || ContainerPriority.Any(key => GetWornContents(key)?.Slots.Any(slot => slot is { } s && predicate(s.ItemId)) ?? false);

    /// <summary>Whether `count` more of this item would fit right now, across every worn
    /// container's contents and empty/matching hands, without actually adding anything.</summary>
    public bool HasRoomFor(string itemId, int count) =>
        ContainerPriority.Sum(key => GetWornContents(key)?.RoomFor(itemId) ?? 0) + _hands.RoomFor(itemId) >= count;

    /// <summary>Adds up to `count`: tops up/fills worn containers first (in <see cref="ContainerPriority"/>
    /// order — back before torso, matching this codebase's original "backpack before hand"
    /// behavior when only a backpack is worn), then falls back to a hand only if no worn
    /// container exists or has room — a passive pickup never bumps something you're deliberately
    /// holding, and never fills an empty hand as long as a worn container can take it. Skips every
    /// worn container entirely for an item that doesn't fit storage (see
    /// ItemCatalog.FitsInStorage) — it can only ever land in a hand. Returns how much actually
    /// fit (0..count). `charge` only matters for a freshly-created "battery" slot — every other
    /// item ignores it.</summary>
    public int Add(string itemId, int count = 1, float charge = 1f)
    {
        var added = 0;
        if (ItemCatalog.FitsInStorage(itemId))
        {
            foreach (var key in ContainerPriority)
            {
                if (added >= count)
                {
                    break;
                }

                if (GetWornContents(key) is { } contents)
                {
                    added += contents.Add(itemId, count - added, charge);
                }
            }
        }

        if (added >= count)
        {
            return added;
        }

        return added + _hands.Add(itemId, count - added, charge);
    }

    /// <summary>Removes up to `count`, preferring worn containers' bulk stock (in
    /// <see cref="ContainerPriority"/> order) over what's actively held in a hand — draining a
    /// hand last keeps a held item in hand as long as possible.</summary>
    public bool TryRemove(string itemId, int count = 1)
    {
        if (!Has(itemId, count))
        {
            return false;
        }

        var remaining = count;
        foreach (var key in ContainerPriority)
        {
            if (remaining <= 0)
            {
                break;
            }

            if (GetWornContents(key) is not { } contents)
            {
                continue;
            }

            var fromContainer = Math.Min(contents.CountOf(itemId), remaining);
            if (fromContainer > 0)
            {
                contents.TryRemove(itemId, fromContainer);
                remaining -= fromContainer;
            }
        }

        if (remaining > 0)
        {
            _hands.TryRemove(itemId, remaining);
        }

        return true;
    }

    /// <summary>Moves item `itemId` into `handIndex` from whichever worn container currently
    /// holds it (checked in <see cref="ContainerPriority"/> order) — equipping it (there's
    /// nowhere else it could be, since neither hand already holds it by the time this is called
    /// — see Player.ToggleHeldItem). <see cref="SlotContainer.MoveBetween"/> already swaps if
    /// `handIndex` is occupied by something else, which is what makes "replace whichever hand
    /// was filled most recently" trivial: call this again on that same hand, and its old
    /// contents land back in the container they came from. Returns false (no-op) if no worn
    /// container currently holds this item.</summary>
    public bool Equip(string itemId, int handIndex)
    {
        foreach (var key in ContainerPriority)
        {
            if (GetWornContents(key) is not { } contents)
            {
                continue;
            }

            for (var i = 0; i < contents.Slots.Count; i++)
            {
                if (contents.Slots[i]?.ItemId == itemId)
                {
                    SlotContainer.MoveBetween(contents, i, _hands, handIndex);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The explicit toggle-off case — pressing the hotbar key for whatever's already in
    /// a hand, with no swap target involved. Moves the hand's contents into worn containers (in
    /// <see cref="ContainerPriority"/> order), leaving any leftover that didn't fit right back in
    /// the hand (same "nothing vanishes" partial-fit spirit as <see cref="Add"/>) — no worn
    /// container with room means the hand keeps everything and this returns false.</summary>
    public bool Unequip(int handIndex)
    {
        if (_hands.Slots[handIndex] is not { } occupied)
        {
            return true;
        }

        _hands.SetSlot(handIndex, null);

        var remaining = occupied.Count;
        foreach (var key in ContainerPriority)
        {
            if (remaining <= 0)
            {
                break;
            }

            if (GetWornContents(key) is { } contents)
            {
                remaining -= contents.Add(occupied.ItemId, remaining, occupied.Charge);
            }
        }

        _hands.SetSlot(handIndex, remaining > 0 ? (occupied.ItemId, remaining, occupied.Charge) : null);
        return remaining == 0;
    }

    /// <summary>Removes up to `count` from that *specific* hand slot only — unlike the aggregate
    /// <see cref="TryRemove"/>, which could silently drain from the backpack instead and leave
    /// the hand's displayed count unchanged. Used by consuming a held item (see Player.UseHeldItem),
    /// where "what you see in your hand is what gets used" matters. Returns false (no-op) if the
    /// hand doesn't hold at least `count`.</summary>
    public bool TryRemoveFromHand(int handIndex, int count = 1)
    {
        if (_hands.Slots[handIndex] is not { } occupied || occupied.Count < count)
        {
            return false;
        }

        var remaining = occupied.Count - count;
        _hands.SetSlot(handIndex, remaining > 0 ? (occupied.ItemId, remaining, occupied.Charge) : null);
        return true;
    }

    /// <summary>Equips a worn backpack by consuming one "backpack" item from a hand (wherever it
    /// currently sits) and attaching a freshly-emptied <see cref="SlotContainer"/> to the "back"
    /// slot. Fails (no-op) if a backpack is already worn, or no hand currently holds one.</summary>
    public bool EquipBackpackFromHand(int slotCount = BackpackSlotCount)
    {
        if (Backpack is not null)
        {
            return false;
        }

        if (!_hands.TryRemove("backpack", 1))
        {
            return false;
        }

        EquipContainerDirectly("back", "backpack", new SlotContainer(slotCount));
        return true;
    }

    /// <summary>Detaches the worn backpack with no fungible fallback — the caller decides what
    /// happens to what was equipped (see Player.TryUnequipBackpack). Its persistent contents are
    /// untouched (see ClearEquippedContainer).</summary>
    public void ClearBackpack() => ClearEquippedContainer("back");

    /// <summary>Attaches an already-built container directly to `slotName`, bypassing the
    /// hand-consume step in <see cref="EquipBackpackFromHand"/> — used by save/load restoration
    /// and by picking a full worn container back up off the ground (see ContainerPickupItem).
    /// Fails (no-op, returns false) if that slot is already occupied. If `itemId` already has a
    /// persistent-contents entry (re-equipping something taken off earlier), that existing entry
    /// is reused and `contents` is discarded — first acquisition wins; only a genuine world-discard
    /// (see Player.TryDropInWorld) ever removes an entry, so by the time a load-restore path calls
    /// this after <see cref="Clear"/>, there's nothing to reuse and the restored `contents` become
    /// the new canonical entry.</summary>
    public bool EquipContainerDirectly(string slotName, string itemId, SlotContainer contents)
    {
        if (_equippedItemIds.ContainsKey(slotName))
        {
            return false;
        }

        _equippedItemIds[slotName] = itemId;
        if (!_persistentContents.ContainsKey(itemId))
        {
            _persistentContents[itemId] = contents;
        }

        return true;
    }

    /// <summary>Whether `slotName` currently has nothing worn in it — used by ContainerPickupItem
    /// to gate its Pick Up verb the same way the backpack's own equip flow already does.</summary>
    public bool IsContainerSlotFree(string slotName) => !_equippedItemIds.ContainsKey(slotName);

    /// <summary>Attaches a specialized sub-slot's state directly — used by the fresh-game
    /// stipend (fully charged) and by save/load restore, mirroring
    /// <see cref="EquipContainerDirectly"/>'s shape.</summary>
    public void AttachSpecializedSlot(string key, bool hasItem, float charge) =>
        SetSpecializedSlot(key, new SpecializedSlot { HasItem = hasItem, Charge = charge });

    /// <summary>Fully detaches a specialized sub-slot (back to null, not just emptied) — used
    /// when the device that owns it (e.g. the EVA suit's torso piece) is itself unequipped, since
    /// the sub-slot shouldn't exist at all while there's no suit to carry it.</summary>
    public void DetachSpecializedSlot(string key) => SetSpecializedSlot(key, null);

    /// <summary>Finds and removes one item matching `itemId` — preferring worn containers (in
    /// <see cref="ContainerPriority"/> order) over a held hand (same order <see cref="TryRemove"/>
    /// already uses) — and reports its real <c>Charge</c> instead of just discarding which
    /// specific instance it was, the way a plain <see cref="TryRemove"/>+hardcoded-1f would.
    /// Scans, then removes from that exact same slot (same read-then-remove-from-the-same-container
    /// shape as <see cref="Equip"/> above), so it can't drift onto a different instance than the
    /// one it reported. Null if no matching item exists anywhere in the inventory.</summary>
    private float? TryRemoveItem(string itemId)
    {
        foreach (var key in ContainerPriority)
        {
            if (GetWornContents(key) is not { } contents)
            {
                continue;
            }

            foreach (var slot in contents.Slots)
            {
                if (slot is { } found && found.ItemId == itemId)
                {
                    contents.TryRemove(itemId, 1);
                    return found.Charge;
                }
            }
        }

        foreach (var slot in _hands.Slots)
        {
            if (slot is { } found && found.ItemId == itemId)
            {
                _hands.TryRemove(itemId, 1);
                return found.Charge;
            }
        }

        return null;
    }

    /// <summary>Loads a spare item (whichever this sub-slot key accepts, wherever it currently
    /// sits) into the specialized sub-slot, honoring whichever specific instance came up first
    /// (see <see cref="TryRemoveItem"/>) — no forced-full-charge simplification, a used
    /// battery/tank installs used. No-op/false if the slot doesn't exist, it's already loaded, or
    /// no spare item is available.</summary>
    public bool InsertIntoSpecializedSlot(string key)
    {
        if (GetSpecializedSlot(key) is not { HasItem: false } slot
            || !SpecializedSlotAcceptedItemIds.TryGetValue(key, out var itemId)
            || TryRemoveItem(itemId) is not { } charge)
        {
            return false;
        }

        slot.HasItem = true;
        slot.Charge = charge;
        return true;
    }

    /// <summary>Ejects a specialized sub-slot's item back into inventory carrying its real
    /// remaining charge (see SlotContainer's Charge field) — no longer discarded on eject, so a
    /// used battery/tank genuinely stays used until replaced. No-op/false if the slot doesn't
    /// exist or it's already empty.</summary>
    public bool EjectSpecializedSlot(string key)
    {
        if (GetSpecializedSlot(key) is not { HasItem: true } slot
            || !SpecializedSlotAcceptedItemIds.TryGetValue(key, out var itemId))
        {
            return false;
        }

        var charge = slot.Charge;
        slot.HasItem = false;
        slot.Charge = 0f;
        Add(itemId, 1, charge);
        return true;
    }

    /// <summary>Same state mutation as <see cref="EjectSpecializedSlot"/>, but with no inventory
    /// destination at all — used when the player drags a specialized slot straight into the world
    /// (see Player.TryDropInWorld), which spawns a loose world pickup instead of placing it in a
    /// slot. Returns the ejected item's real charge, or null if the slot was already empty.</summary>
    public float? EjectSpecializedSlotForWorld(string key)
    {
        if (GetSpecializedSlot(key) is not { HasItem: true } slot)
        {
            return null;
        }

        var charge = slot.Charge;
        slot.HasItem = false;
        slot.Charge = 0f;
        return charge;
    }

    /// <summary>Same state mutation as <see cref="EjectSpecializedSlot"/>, but places the ejected
    /// item into a specific target slot instead of wherever <see cref="Add"/> finds room — used
    /// when the player drags a specialized slot's item onto a particular hand/container slot
    /// rather than dropping it in open space (see InventorySlotUI._DropData). No-op/false if the
    /// slot was already empty, or the target slot isn't actually empty.</summary>
    public bool EjectSpecializedSlotTo(string key, SlotContainer container, int slotIndex)
    {
        if (GetSpecializedSlot(key) is not { HasItem: true } slot
            || container.Slots[slotIndex] is not null
            || !SpecializedSlotAcceptedItemIds.TryGetValue(key, out var itemId))
        {
            return false;
        }

        var charge = slot.Charge;
        slot.HasItem = false;
        slot.Charge = 0f;
        container.SetSlot(slotIndex, (itemId, 1, charge));
        return true;
    }

    /// <summary>Drains a specialized sub-slot's charge by `amount` (clamped at 0) — used by the
    /// EVA suit's sustained-thrust locomotion (burning N2) and its per-tick O2/filter/battery
    /// draw while sealed. A no-op if the slot doesn't exist or isn't currently loaded.</summary>
    public void DrainSpecializedSlot(string key, float amount)
    {
        if (GetSpecializedSlot(key) is not { } slot)
        {
            return;
        }

        slot.Charge = Math.Max(0f, slot.Charge - amount);
    }

    public void Clear()
    {
        _hands.Clear();
        _equippedItemIds.Clear();
        _persistentContents.Clear();
        Drill = null;
        Flashlight = null;
        SuitO2 = null;
        SuitN2 = null;
        SuitFilter = null;
        SuitBattery = null;
    }
}
