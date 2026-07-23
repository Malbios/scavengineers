using System;
using System.Collections.Generic;
using System.Linq;

namespace Scavengineers.Scripts.Inventory;

/// <summary>Composes the player's two hands with zero or more simultaneously-worn containers
/// (each its own <see cref="SlotContainer"/>), keyed by equip-slot name. There is no "pockets"
/// storage beyond that — what isn't held or worn simply isn't on the player.</summary>
public sealed class PlayerInventory
{
    // Hands are real slots, iterated generically by Has/CountOf/Add/TryRemove/HasRoomFor — never
    // auto-filled by a passive Add, only by an explicit Equip (hotbar key or drag).
    public const int HandCount = 2;
    public const int LeftHandSlotIndex = 0;
    public const int RightHandSlotIndex = 1;

    // Placeholder/tunable — how many slots a worn backpack's own contents have.
    public const int BackpackSlotCount = 8;

    // Placeholder/tunable — the EVA suit torso's own 2 unlabeled pocket slots (its 4 specialized
    // tank/filter/battery sub-slots are tracked separately, see SpecializedSlot).
    public const int TorsoSlotCount = 2;

    // The PDA's one cartridge pocket — room to grow later without a redesign.
    public const int PdaSlotCount = 2;

    // Priority order Add/TryRemove/Unequip use when more than one container is worn — back before
    // torso. "head" is deliberately excluded: a worn helmet's container has 0 slots (see
    // EquipItemDirectly), so there's nothing to aggregate into.
    private static readonly string[] ContainerPriority = ["back", "torso"];

    private readonly SlotContainer _hands = new(HandCount);

    public SlotContainer Hands => _hands;

    /// <summary>Wears down whichever hand currently holds `itemId` by `amount` of its held-slot
    /// Charge (clamped at 0) — tool durability (crowbar/power_drill/wrench), a different meaning
    /// from Charge's usual "battery level". No-op if the item isn't in a hand right now.</summary>
    public void DamageToolInHand(string itemId, float amount)
    {
        for (var i = 0; i < HandCount; i++)
        {
            if (_hands.Slots[i] is { } slot && slot.ItemId == itemId)
            {
                _hands.SetSlot(i, (slot.ItemId, slot.Count, Math.Max(0f, slot.Charge - amount)));
                return;
            }
        }
    }

    /// <summary>An item id plus its own <see cref="SlotContainer"/> — the shape a worn container
    /// takes while equipped. Rebuilt fresh on each <see cref="GetEquippedContainer"/> call, but
    /// <see cref="Contents"/> is the one real shared instance underneath, so mutating it is
    /// visible everywhere.</summary>
    public sealed record EquippedContainer(string ItemId, SlotContainer Contents);

    // Which item (if any) currently occupies each equip slot ("back", "torso", "head").
    private readonly Dictionary<string, string> _equippedItemIds = new();

    // Permanent per-item sub-inventory for container-carrying items, created once on first equip
    // and never destroyed by ordinary equip/unequip (only a genuine world-discard removes an
    // entry — see Player.TryDropInWorld). Lets a worn item's contents survive being taken off.
    private readonly Dictionary<string, SlotContainer> _persistentContents = new();

    public EquippedContainer? Backpack => GetEquippedContainer("back");

    public EquippedContainer? Torso => GetEquippedContainer("torso");

    public EquippedContainer? Head => GetEquippedContainer("head");

    /// <summary>Every container-carrying item, even a 0-slot one like the helmet, gets a
    /// persistent-contents entry once equipped — falls back to a fresh 0-slot container in the
    /// (should never happen) case one's missing, so callers never see a null Contents.</summary>
    public EquippedContainer? GetEquippedContainer(string slotName)
    {
        if (!_equippedItemIds.TryGetValue(slotName, out var itemId))
        {
            return null;
        }

        var contents = _persistentContents.GetValueOrDefault(itemId) ?? new SlotContainer(0);
        return new EquippedContainer(itemId, contents);
    }

    /// <summary>Reachable whether the item is worn, held, or sitting in a backpack slot — this is
    /// what makes "preview an item's contents while just carrying it" possible.</summary>
    public SlotContainer? GetPersistentContents(string itemId) => _persistentContents.GetValueOrDefault(itemId);

    private SlotContainer? GetWornContents(string slotName) =>
        _equippedItemIds.TryGetValue(slotName, out var itemId) ? _persistentContents.GetValueOrDefault(itemId) : null;

    /// <summary>Deliberately does NOT touch <see cref="_persistentContents"/> — taking something
    /// off doesn't destroy or relocate what's inside it.</summary>
    public void ClearEquippedContainer(string slotName) => _equippedItemIds.Remove(slotName);

    /// <summary>The one place persistent contents are actually removed rather than left dormant
    /// while unworn (a genuine world-discard, or test setup for "never acquired").</summary>
    public void DiscardPersistentContents(string itemId) => _persistentContents.Remove(itemId);

    /// <summary>Seeds an item's persistent contents at load time. No-op if an entry already
    /// exists — same "first acquisition wins" contract as <see cref="EquipContainerDirectly"/>.</summary>
    public void RestorePersistentContents(string itemId, SlotContainer contents)
    {
        if (!_persistentContents.ContainsKey(itemId))
        {
            _persistentContents[itemId] = contents;
        }
    }

    /// <summary>A device's own swappable battery/tank (power drill, flashlight, EVA suit tank) —
    /// deliberately not a generic slot-level extension to <see cref="SlotContainer"/>, since that
    /// would touch every inventory consumer for one tool.</summary>
    public sealed class SpecializedSlot
    {
        public bool HasItem { get; set; }

        /// <summary>0-1; meaningless while <see cref="HasItem"/> is false.</summary>
        public float Charge { get; set; }
    }

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

    /// <summary>Non-null only while the torso is worn — attached/detached alongside it.</summary>
    public SpecializedSlot? SuitO2 { get; private set; }

    public SpecializedSlot? SuitN2 { get; private set; }

    public SpecializedSlot? SuitFilter { get; private set; }

    public SpecializedSlot? SuitBattery { get; private set; }

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

    public static string? SpecializedSlotAcceptedItemId(string key) => SpecializedSlotAcceptedItemIds.GetValueOrDefault(key);

    /// <summary>Every item id that ever carries a meaningful per-slot Charge — a tank/filter/
    /// battery keeps its real charge whether docked in its own specialized slot or sitting loose
    /// in an ordinary hand/backpack slot.</summary>
    public static readonly IReadOnlySet<string> ChargeableItemIds = SpecializedSlotAcceptedItemIds.Values.ToHashSet();

    /// <summary>The raw per-slot view of the two hands — a worn backpack's own contents are
    /// addressed separately via <see cref="Backpack"/>.Contents, never merged in here.</summary>
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

    public bool HasAny(Func<string, bool> predicate) =>
        _hands.Slots.Any(slot => slot is { } s && predicate(s.ItemId))
        || ContainerPriority.Any(key => GetWornContents(key)?.Slots.Any(slot => slot is { } s && predicate(s.ItemId)) ?? false);

    public bool HasRoomFor(string itemId, int count) =>
        ContainerPriority.Sum(key => GetWornContents(key)?.RoomFor(itemId) ?? 0) + _hands.RoomFor(itemId) >= count;

    /// <summary>Tops up worn containers first (<see cref="ContainerPriority"/> order), then falls
    /// back to a hand only if no worn container exists or has room — a passive pickup never
    /// bumps something deliberately held. Skips worn containers entirely for an item that doesn't
    /// fit storage (see <see cref="ItemCatalog.FitsInStorage"/>). Returns how much actually fit.</summary>
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

    /// <summary>Prefers worn containers' bulk stock over what's actively held in a hand — draining
    /// a hand last keeps a held item in hand as long as possible.</summary>
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

    /// <summary>Moves `itemId` into `handIndex` from whichever worn container holds it.
    /// <see cref="SlotContainer.MoveBetween"/> swaps if `handIndex` is occupied, which is what
    /// makes "replace whichever hand was filled most recently" trivial: calling this again on
    /// that hand sends its old contents back where they came from. Returns false if no worn
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

    /// <summary>Moves the hand's contents into worn containers, leaving any leftover that didn't
    /// fit back in the hand. Returns false if no worn container had room, so the hand keeps
    /// everything.</summary>
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

    /// <summary>Removes from that specific hand slot only, unlike <see cref="TryRemove"/>, which
    /// could silently drain the backpack instead and leave the hand's displayed count unchanged.</summary>
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

    public void ClearBackpack() => ClearEquippedContainer("back");

    /// <summary>Attaches an already-built container directly to `slotName`, bypassing the
    /// hand-consume step in <see cref="EquipBackpackFromHand"/>. Fails if that slot is already
    /// occupied. If `itemId` already has a persistent-contents entry, that existing entry is
    /// reused and `contents` is discarded — first acquisition wins.</summary>
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

    public bool IsContainerSlotFree(string slotName) => !_equippedItemIds.ContainsKey(slotName);

    public void AttachSpecializedSlot(string key, bool hasItem, float charge) =>
        SetSpecializedSlot(key, new SpecializedSlot { HasItem = hasItem, Charge = charge });

    /// <summary>Fully detaches (back to null, not just emptied) — used when the device that owns
    /// it is itself unequipped, since the sub-slot shouldn't exist without it.</summary>
    public void DetachSpecializedSlot(string key) => SetSpecializedSlot(key, null);

    /// <summary>Finds and removes one item matching `itemId`, preferring worn containers over a
    /// held hand, and reports its real Charge instead of discarding which instance it was.</summary>
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

    /// <summary>Loads a spare item into the specialized sub-slot, honoring whichever instance
    /// came up first — no forced-full-charge, a used battery/tank installs used.</summary>
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
    /// remaining charge — a used battery/tank stays used until replaced.</summary>
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

    /// <summary>Same as <see cref="EjectSpecializedSlot"/> but with no inventory destination —
    /// used when dragging a specialized slot straight into the world.</summary>
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

    /// <summary>Same as <see cref="EjectSpecializedSlot"/> but places the item into a specific
    /// target slot instead of wherever <see cref="Add"/> finds room.</summary>
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
