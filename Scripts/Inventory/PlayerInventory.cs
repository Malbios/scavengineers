using System;
using System.Collections.Generic;

namespace Scavengineers.Scripts.Inventory;

/// <summary>
/// Composes the player's two hands with, optionally, an equipped backpack's own separate
/// <see cref="SlotContainer"/> (inventory arc Stage 3) — a backpack is the first non-fungible
/// item (it carries real per-instance state, its own contents), so it's tracked as a distinct
/// <see cref="EquippedContainer"/> rather than merged into the hand slots. There is no
/// unrealistic "pockets" storage beyond that: what you're not holding or carrying in a bag
/// simply isn't on you.
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

    private readonly SlotContainer _hands = new(HandCount);

    /// <summary>The two hand slots — read by InventorySlotUI/Player.cs to wire up the panel's
    /// hand slots.</summary>
    public SlotContainer Hands => _hands;

    /// <summary>An item id plus its own separate <see cref="SlotContainer"/> — the shape a
    /// worn backpack takes while equipped. The one container item type this stage supports is
    /// hardcoded as "backpack" (see EquipBackpackFromBody), matching Stage 1's "one flat
    /// catalog" precedent rather than a speculative "which items are containers" lookup.</summary>
    public sealed record EquippedContainer(string ItemId, SlotContainer Contents);

    public EquippedContainer? Backpack { get; private set; }

    /// <summary>The raw per-slot view of the two hands (see InventorySlotUI) — a worn
    /// backpack's own contents are addressed separately via <see cref="Backpack"/>.Contents,
    /// never merged in here.</summary>
    public IReadOnlyList<(string ItemId, int Count)?> Slots => _hands.Slots;

    public IReadOnlyDictionary<string, int> Counts
    {
        get
        {
            var counts = new Dictionary<string, int>(_hands.Counts);
            if (Backpack is not null)
            {
                foreach (var (itemId, count) in Backpack.Contents.Counts)
                {
                    counts[itemId] = counts.GetValueOrDefault(itemId) + count;
                }
            }

            return counts;
        }
    }

    public int CountOf(string itemId) => _hands.CountOf(itemId) + (Backpack?.Contents.CountOf(itemId) ?? 0);

    public bool Has(string itemId, int count) => CountOf(itemId) >= count;

    /// <summary>Whether `count` more of this item would fit right now, across the worn
    /// backpack's contents (if any) and empty/matching hands, without actually adding
    /// anything.</summary>
    public bool HasRoomFor(string itemId, int count) =>
        (Backpack?.Contents.RoomFor(itemId) ?? 0) + _hands.RoomFor(itemId) >= count;

    /// <summary>Adds up to `count`: tops up/fills the worn backpack's own contents first, then
    /// falls back to a hand only if the backpack doesn't exist or doesn't have room — a passive
    /// pickup never bumps something you're deliberately holding, and never fills an empty hand
    /// as long as the bag can take it. Returns how much actually fit (0..count).</summary>
    public int Add(string itemId, int count = 1)
    {
        var addedToBackpack = Backpack?.Contents.Add(itemId, count) ?? 0;
        var remaining = count - addedToBackpack;
        if (remaining <= 0)
        {
            return addedToBackpack;
        }

        return addedToBackpack + _hands.Add(itemId, remaining);
    }

    /// <summary>Removes up to `count`, preferring the backpack's bulk stock over what's actively
    /// held in a hand — draining a hand last keeps a held item in hand as long as possible.</summary>
    public bool TryRemove(string itemId, int count = 1)
    {
        if (!Has(itemId, count))
        {
            return false;
        }

        var fromBackpack = Backpack is null ? 0 : Math.Min(Backpack.Contents.CountOf(itemId), count);
        if (fromBackpack > 0)
        {
            Backpack!.Contents.TryRemove(itemId, fromBackpack);
        }

        var remaining = count - fromBackpack;
        if (remaining > 0)
        {
            _hands.TryRemove(itemId, remaining);
        }

        return true;
    }

    /// <summary>Moves item `itemId` into `handIndex` from the worn backpack's contents —
    /// equipping it (there's nowhere else it could be, since neither hand already holds it by
    /// the time this is called — see Player.ToggleHeldItem). <see cref="SlotContainer.MoveBetween"/>
    /// already swaps if `handIndex` is occupied by something else, which is what makes "replace
    /// whichever hand was filled most recently" trivial: call this again on that same hand, and
    /// its old contents land back in the backpack. Returns false (no-op) if the backpack doesn't
    /// currently hold this item.</summary>
    public bool Equip(string itemId, int handIndex)
    {
        if (Backpack is null)
        {
            return false;
        }

        for (var i = 0; i < Backpack.Contents.Slots.Count; i++)
        {
            if (Backpack.Contents.Slots[i]?.ItemId == itemId)
            {
                SlotContainer.MoveBetween(Backpack.Contents, i, _hands, handIndex);
                return true;
            }
        }

        return false;
    }

    /// <summary>The explicit toggle-off case — pressing the hotbar key for whatever's already in
    /// a hand, with no swap target involved. Moves the hand's contents into the worn backpack
    /// (if any), leaving any leftover that didn't fit right back in the hand (same "nothing
    /// vanishes" partial-fit spirit as <see cref="Add"/>) — no backpack, or a full one, means the
    /// hand keeps everything and this returns false.</summary>
    public bool Unequip(int handIndex)
    {
        if (_hands.Slots[handIndex] is not { } occupied)
        {
            return true;
        }

        _hands.SetSlot(handIndex, null);
        var added = Backpack?.Contents.Add(occupied.ItemId, occupied.Count) ?? 0;
        var leftover = occupied.Count - added;
        _hands.SetSlot(handIndex, leftover > 0 ? (occupied.ItemId, leftover) : null);
        return leftover == 0;
    }

    /// <summary>Equips a worn backpack by consuming one "backpack" item from a hand (wherever it
    /// currently sits) and attaching a freshly-emptied <see cref="SlotContainer"/> as
    /// <see cref="Backpack"/>. Fails (no-op) if a backpack is already worn, or no hand currently
    /// holds one.</summary>
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

        Backpack = new EquippedContainer("backpack", new SlotContainer(slotCount));
        return true;
    }

    /// <summary>Detaches the worn backpack with no fungible fallback — the caller decides what
    /// happens to what was equipped (see Player.TryUnequipBackpack).</summary>
    public void ClearBackpack() => Backpack = null;

    /// <summary>Attaches an already-built container as the worn backpack directly, bypassing the
    /// hand-consume step in <see cref="EquipBackpackFromBody"/> — used by save/load restoration
    /// and by picking a full backpack back up off the ground (see ContainerPickupItem). Fails
    /// (no-op, returns false) if a backpack is already worn.</summary>
    public bool EquipContainerDirectly(string itemId, SlotContainer contents)
    {
        if (Backpack is not null)
        {
            return false;
        }

        Backpack = new EquippedContainer(itemId, contents);
        return true;
    }

    public void Clear()
    {
        _hands.Clear();
        Backpack = null;
    }
}
