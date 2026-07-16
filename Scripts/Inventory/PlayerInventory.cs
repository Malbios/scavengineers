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

    // The fixed slot-name priority Add/TryRemove/Unequip use when more than one container is
    // worn at once — matches this codebase's original "backpack before hand" behavior exactly
    // when only "back" is worn, and extends predictably as more simultaneously-wearable
    // containers (e.g. the EVA suit's torso piece) are added.
    private static readonly string[] ContainerPriority = ["back", "torso"];

    private readonly SlotContainer _hands = new(HandCount);

    /// <summary>The two hand slots — read by InventorySlotUI/Player.cs to wire up the panel's
    /// hand slots.</summary>
    public SlotContainer Hands => _hands;

    /// <summary>An item id plus its own separate <see cref="SlotContainer"/> — the shape a
    /// worn container takes while equipped (a backpack, or the EVA suit's torso piece). Which
    /// items are containers is hardcoded per equip slot (see EquipBackpackFromHand) rather than
    /// a speculative "which items are containers" lookup.</summary>
    public sealed record EquippedContainer(string ItemId, SlotContainer Contents);

    private readonly Dictionary<string, EquippedContainer> _equippedContainers = new();

    public EquippedContainer? Backpack => _equippedContainers.GetValueOrDefault("back");

    public EquippedContainer? Torso => _equippedContainers.GetValueOrDefault("torso");

    /// <summary>A power drill's own battery — the first item with real per-instance state that
    /// isn't a worn container (see <see cref="EquippedContainer"/>). Deliberately not a generic
    /// slot-level extension to <see cref="SlotContainer"/> (that would touch every inventory
    /// consumer for one tool) — just enough state riding alongside the "power_drill" item,
    /// wherever it currently sits (hand or backpack), independent of location the same way
    /// <see cref="Backpack"/>'s existence doesn't care which hand it was equipped from.</summary>
    public sealed class DrillState
    {
        public bool HasBattery { get; set; }

        /// <summary>0-1; meaningless while <see cref="HasBattery"/> is false.</summary>
        public float Charge { get; set; }
    }

    public DrillState? Drill { get; private set; }

    /// <summary>A flashlight's own battery — the same per-instance-state shape as
    /// <see cref="DrillState"/>, since removing the player's generic Power stat left the
    /// flashlight with nothing limiting continuous use.</summary>
    public sealed class FlashlightState
    {
        public bool HasBattery { get; set; }

        /// <summary>0-1; meaningless while <see cref="HasBattery"/> is false.</summary>
        public float Charge { get; set; }
    }

    public FlashlightState? Flashlight { get; private set; }

    /// <summary>The raw per-slot view of the two hands (see InventorySlotUI) — a worn
    /// backpack's own contents are addressed separately via <see cref="Backpack"/>.Contents,
    /// never merged in here.</summary>
    public IReadOnlyList<(string ItemId, int Count, float Charge)?> Slots => _hands.Slots;

    public IReadOnlyDictionary<string, int> Counts
    {
        get
        {
            var counts = new Dictionary<string, int>(_hands.Counts);
            foreach (var container in _equippedContainers.Values)
            {
                foreach (var (itemId, count) in container.Contents.Counts)
                {
                    counts[itemId] = counts.GetValueOrDefault(itemId) + count;
                }
            }

            return counts;
        }
    }

    public int CountOf(string itemId) =>
        _hands.CountOf(itemId) + _equippedContainers.Values.Sum(c => c.Contents.CountOf(itemId));

    public bool Has(string itemId, int count) => CountOf(itemId) >= count;

    /// <summary>Whether any item anywhere in inventory (hands or any worn container) matches
    /// `predicate` — e.g. Player's flashlight toggle uses this against
    /// ItemCatalog.IsToggleableLight to find a never-held toggleable light (like the debug
    /// flashlight) without hardcoding its item id.</summary>
    public bool HasAny(Func<string, bool> predicate) =>
        _hands.Slots.Any(slot => slot is { } s && predicate(s.ItemId))
        || _equippedContainers.Values.Any(c => c.Contents.Slots.Any(slot => slot is { } s && predicate(s.ItemId)));

    /// <summary>Whether `count` more of this item would fit right now, across every worn
    /// container's contents and empty/matching hands, without actually adding anything.</summary>
    public bool HasRoomFor(string itemId, int count) =>
        _equippedContainers.Values.Sum(c => c.Contents.RoomFor(itemId)) + _hands.RoomFor(itemId) >= count;

    /// <summary>Adds up to `count`: tops up/fills worn containers first (in <see cref="ContainerPriority"/>
    /// order — back before torso, matching this codebase's original "backpack before hand"
    /// behavior when only a backpack is worn), then falls back to a hand only if no worn
    /// container exists or has room — a passive pickup never bumps something you're deliberately
    /// holding, and never fills an empty hand as long as a worn container can take it. Returns
    /// how much actually fit (0..count). `charge` only matters for a freshly-created "battery"
    /// slot — every other item ignores it.</summary>
    public int Add(string itemId, int count = 1, float charge = 1f)
    {
        var added = 0;
        foreach (var key in ContainerPriority)
        {
            if (added >= count)
            {
                break;
            }

            if (_equippedContainers.TryGetValue(key, out var container))
            {
                added += container.Contents.Add(itemId, count - added, charge);
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

            if (!_equippedContainers.TryGetValue(key, out var container))
            {
                continue;
            }

            var fromContainer = Math.Min(container.Contents.CountOf(itemId), remaining);
            if (fromContainer > 0)
            {
                container.Contents.TryRemove(itemId, fromContainer);
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
            if (!_equippedContainers.TryGetValue(key, out var container))
            {
                continue;
            }

            for (var i = 0; i < container.Contents.Slots.Count; i++)
            {
                if (container.Contents.Slots[i]?.ItemId == itemId)
                {
                    SlotContainer.MoveBetween(container.Contents, i, _hands, handIndex);
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

            if (_equippedContainers.TryGetValue(key, out var container))
            {
                remaining -= container.Contents.Add(occupied.ItemId, remaining, occupied.Charge);
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

        _equippedContainers["back"] = new EquippedContainer("backpack", new SlotContainer(slotCount));
        return true;
    }

    /// <summary>Detaches the worn backpack with no fungible fallback — the caller decides what
    /// happens to what was equipped (see Player.TryUnequipBackpack).</summary>
    public void ClearBackpack() => _equippedContainers.Remove("back");

    /// <summary>Attaches an already-built container directly to `slotName`, bypassing the
    /// hand-consume step in <see cref="EquipBackpackFromHand"/> — used by save/load restoration
    /// and by picking a full worn container back up off the ground (see ContainerPickupItem).
    /// Fails (no-op, returns false) if that slot is already occupied.</summary>
    public bool EquipContainerDirectly(string slotName, string itemId, SlotContainer contents)
    {
        if (_equippedContainers.ContainsKey(slotName))
        {
            return false;
        }

        _equippedContainers[slotName] = new EquippedContainer(itemId, contents);
        return true;
    }

    /// <summary>Whether `slotName` currently has nothing worn in it — used by ContainerPickupItem
    /// to gate its Pick Up verb the same way the backpack's own equip flow already does.</summary>
    public bool IsContainerSlotFree(string slotName) => !_equippedContainers.ContainsKey(slotName);

    /// <summary>Attaches drill state directly — used by the fresh-game stipend (fully charged)
    /// and by save/load restore, mirroring <see cref="EquipContainerDirectly"/>'s shape.</summary>
    public void AttachDrill(bool hasBattery, float charge) => Drill = new DrillState { HasBattery = hasBattery, Charge = charge };

    /// <summary>Finds and removes one "battery" item — preferring the backpack over a held hand
    /// (same order <see cref="TryRemove"/> already uses) — and reports its real <c>Charge</c>
    /// instead of just discarding which specific instance it was, the way a plain
    /// <see cref="TryRemove"/>+hardcoded-1f would. Scans, then removes from that exact same slot
    /// (same read-then-remove-from-the-same-container shape as <see cref="Equip"/> above), so it
    /// can't drift onto a different battery than the one it reported. Null if no battery exists
    /// anywhere in the inventory.</summary>
    private float? TryRemoveBattery()
    {
        if (Backpack is not null)
        {
            foreach (var slot in Backpack.Contents.Slots)
            {
                if (slot is { ItemId: "battery" } found)
                {
                    Backpack.Contents.TryRemove("battery", 1);
                    return found.Charge;
                }
            }
        }

        foreach (var slot in _hands.Slots)
        {
            if (slot is { ItemId: "battery" } found)
            {
                _hands.TryRemove("battery", 1);
                return found.Charge;
            }
        }

        return null;
    }

    /// <summary>Loads a spare "battery" item (wherever it currently sits) into the drill, honoring
    /// whichever specific battery instance came up first (see <see cref="TryRemoveBattery"/>) —
    /// no forced-full-charge simplification anymore, a used battery installs used. No-op/false if
    /// there's no drill, it's already loaded, or no spare battery is available.</summary>
    public bool InsertDrillBattery()
    {
        if (Drill is not { HasBattery: false } || TryRemoveBattery() is not { } charge)
        {
            return false;
        }

        Drill.HasBattery = true;
        Drill.Charge = charge;
        return true;
    }

    /// <summary>Ejects the drill's battery back into inventory as a "battery" item carrying its
    /// real remaining charge (see SlotContainer's Charge field) — no longer discarded on eject,
    /// so a used battery genuinely stays used until replaced. No-op/false if there's no drill or
    /// it's already empty.</summary>
    public bool EjectDrillBattery()
    {
        if (Drill is not { HasBattery: true })
        {
            return false;
        }

        var charge = Drill.Charge;
        Drill.HasBattery = false;
        Drill.Charge = 0f;
        Add("battery", 1, charge);
        return true;
    }

    /// <summary>Same battery-state mutation as <see cref="EjectDrillBattery"/>, but with no
    /// inventory destination at all — used when the player drags the drill's battery slot straight
    /// into the world (see Player.TryDropInWorld), which spawns a loose world pickup instead of
    /// placing it in a slot. Returns the battery's real charge, or null if there's no installed
    /// battery to eject.</summary>
    public float? EjectDrillBatteryForWorld()
    {
        if (Drill is not { HasBattery: true })
        {
            return null;
        }

        var charge = Drill.Charge;
        Drill.HasBattery = false;
        Drill.Charge = 0f;
        return charge;
    }

    /// <summary>Same battery-state mutation as <see cref="EjectDrillBattery"/>, but places the
    /// ejected battery into a specific target slot instead of wherever <see cref="Add"/> finds
    /// room — used when the player drags the drill's battery onto a particular hand/backpack slot
    /// rather than dropping it in open space (see InventorySlotUI._DropData). No-op/false if
    /// there's no installed battery to eject, or the target slot isn't actually empty.</summary>
    public bool EjectDrillBatteryTo(SlotContainer container, int slotIndex)
    {
        if (Drill is not { HasBattery: true } || container.Slots[slotIndex] is not null)
        {
            return false;
        }

        var charge = Drill.Charge;
        Drill.HasBattery = false;
        Drill.Charge = 0f;
        container.SetSlot(slotIndex, ("battery", 1, charge));
        return true;
    }

    /// <summary>Attaches flashlight battery state directly — mirrors <see cref="AttachDrill"/>'s
    /// own fresh-game-stipend/save-load-restore usage.</summary>
    public void AttachFlashlight(bool hasBattery, float charge) => Flashlight = new FlashlightState { HasBattery = hasBattery, Charge = charge };

    /// <summary>Loads a spare "battery" item into the flashlight — mirrors
    /// <see cref="InsertDrillBattery"/> exactly (honors the specific battery's real charge).</summary>
    public bool InsertFlashlightBattery()
    {
        if (Flashlight is not { HasBattery: false } || TryRemoveBattery() is not { } charge)
        {
            return false;
        }

        Flashlight.HasBattery = true;
        Flashlight.Charge = charge;
        return true;
    }

    /// <summary>Ejects the flashlight's battery back into inventory — mirrors
    /// <see cref="EjectDrillBattery"/> exactly (real remaining charge preserved).</summary>
    public bool EjectFlashlightBattery()
    {
        if (Flashlight is not { HasBattery: true })
        {
            return false;
        }

        var charge = Flashlight.Charge;
        Flashlight.HasBattery = false;
        Flashlight.Charge = 0f;
        Add("battery", 1, charge);
        return true;
    }

    /// <summary>Same battery-state mutation as <see cref="EjectFlashlightBattery"/>, but with no
    /// inventory destination — mirrors <see cref="EjectDrillBatteryForWorld"/> exactly.</summary>
    public float? EjectFlashlightBatteryForWorld()
    {
        if (Flashlight is not { HasBattery: true })
        {
            return null;
        }

        var charge = Flashlight.Charge;
        Flashlight.HasBattery = false;
        Flashlight.Charge = 0f;
        return charge;
    }

    /// <summary>Same battery-state mutation as <see cref="EjectFlashlightBattery"/>, but places
    /// the ejected battery into a specific target slot — mirrors
    /// <see cref="EjectDrillBatteryTo"/> exactly.</summary>
    public bool EjectFlashlightBatteryTo(SlotContainer container, int slotIndex)
    {
        if (Flashlight is not { HasBattery: true } || container.Slots[slotIndex] is not null)
        {
            return false;
        }

        var charge = Flashlight.Charge;
        Flashlight.HasBattery = false;
        Flashlight.Charge = 0f;
        container.SetSlot(slotIndex, ("battery", 1, charge));
        return true;
    }

    public void Clear()
    {
        _hands.Clear();
        _equippedContainers.Clear();
        Drill = null;
        Flashlight = null;
    }
}
