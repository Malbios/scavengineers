using System;
using System.Collections.Generic;
using System.Linq;

namespace Scavengineers.Scripts.Inventory;

/// <summary>A fixed-size slot inventory — shared by the player's body slots and an equipped
/// backpack's own contents, so both reuse the same fill/merge/stack mechanics.</summary>
public sealed class SlotContainer
{
    // Charge is 1f (full) and meaningless for every item except "battery" — carried on the slot
    // so a loose/dropped battery keeps its real remaining charge instead of it being discarded
    // the moment it leaves a device.
    private readonly (string ItemId, int Count, float Charge)?[] _slots;

    public SlotContainer(int slotCount)
    {
        _slots = new (string, int, float)?[slotCount];
    }

    /// <summary>Index identity matters here (which physical slot holds what), unlike
    /// <see cref="Counts"/>'s aggregated view.</summary>
    public IReadOnlyList<(string ItemId, int Count, float Charge)?> Slots => _slots;

    /// <summary>Direct slot assignment, bypassing the fill/merge logic in <see cref="Add"/> and
    /// <see cref="MoveSlot"/>.</summary>
    public void SetSlot(int index, (string ItemId, int Count, float Charge)? value) => _slots[index] = value;

    public IReadOnlyDictionary<string, int> Counts =>
        _slots.Where(s => s is not null)
            .GroupBy(s => s!.Value.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s!.Value.Count));

    public int CountOf(string itemId) => _slots.Where(s => s?.ItemId == itemId).Sum(s => s?.Count ?? 0);

    public bool Has(string itemId, int count) => CountOf(itemId) >= count;

    /// <summary>How much of `itemId` would still fit right now, without actually adding
    /// anything.</summary>
    public int RoomFor(string itemId)
    {
        var maxStack = ItemCatalog.MaxStackSize(itemId);
        var room = 0;

        foreach (var slot in _slots)
        {
            room += slot is null ? maxStack : slot.Value.ItemId == itemId ? Math.Max(0, maxStack - slot.Value.Count) : 0;
        }

        return room;
    }

    public bool HasRoomFor(string itemId, int count) => RoomFor(itemId) >= count;

    /// <summary>Respects both the item's stack limit and the number of free slots. Returns how
    /// much actually fit (0..count) — a caller with nowhere else to fall back to (a refund, a
    /// purchase) must handle a partial result itself.</summary>
    public int Add(string itemId, int count = 1, float charge = 1f) => AddWithinRange(itemId, count, 0, _slots.Length, charge);

    /// <summary>Adds within a sub-range of slots only — e.g. unequipping a hand without spilling
    /// into the *other* hand.</summary>
    public int AddWithinRange(string itemId, int count, int startInclusive, int endExclusive, float charge = 1f)
    {
        var remaining = count;
        var maxStack = ItemCatalog.MaxStackSize(itemId);

        // Top up existing stacks first.
        for (var i = startInclusive; i < endExclusive && remaining > 0; i++)
        {
            if (_slots[i] is not { } slot || slot.ItemId != itemId)
            {
                continue;
            }

            var room = maxStack - slot.Count;
            if (room <= 0)
            {
                continue;
            }

            var added = Math.Min(room, remaining);
            _slots[i] = (itemId, slot.Count + added, slot.Charge);
            remaining -= added;
        }

        // Then spill into empty slots.
        for (var i = startInclusive; i < endExclusive && remaining > 0; i++)
        {
            if (_slots[i] is not null)
            {
                continue;
            }

            var added = Math.Min(maxStack, remaining);
            _slots[i] = (itemId, added, charge);
            remaining -= added;
        }

        return count - remaining;
    }

    /// <summary>Different items swap; the same item tops `to` up from `from` up to its stack
    /// limit, leaving any remainder in `from`. A no-op for an out-of-range index, `from == to`,
    /// an empty `from`, or a same-item `to` that's already full.</summary>
    public void MoveSlot(int from, int to)
    {
        if (from == to || from < 0 || from >= _slots.Length || to < 0 || to >= _slots.Length)
        {
            return;
        }

        if (_slots[from] is not { } source)
        {
            return;
        }

        if (_slots[to] is not { } dest)
        {
            _slots[to] = source;
            _slots[from] = null;
            return;
        }

        if (dest.ItemId != source.ItemId)
        {
            (_slots[from], _slots[to]) = (_slots[to], _slots[from]);
            return;
        }

        var room = ItemCatalog.MaxStackSize(source.ItemId) - dest.Count;
        if (room <= 0)
        {
            return;
        }

        var moved = Math.Min(room, source.Count);
        _slots[to] = (dest.ItemId, dest.Count + moved, dest.Charge);
        var remaining = source.Count - moved;
        _slots[from] = remaining > 0 ? (source.ItemId, remaining, source.Charge) : null;
    }

    /// <summary>Same mutation as <see cref="MoveSlot"/>, but for two different containers —
    /// delegates to it when `from` and `to` are actually the same container.</summary>
    public static void MoveBetween(SlotContainer from, int fromIndex, SlotContainer to, int toIndex)
    {
        if (ReferenceEquals(from, to))
        {
            from.MoveSlot(fromIndex, toIndex);
            return;
        }

        if (fromIndex < 0 || fromIndex >= from._slots.Length || toIndex < 0 || toIndex >= to._slots.Length)
        {
            return;
        }

        if (from._slots[fromIndex] is not { } source)
        {
            return;
        }

        if (to._slots[toIndex] is not { } dest)
        {
            to._slots[toIndex] = source;
            from._slots[fromIndex] = null;
            return;
        }

        if (dest.ItemId != source.ItemId)
        {
            to._slots[toIndex] = source;
            from._slots[fromIndex] = dest;
            return;
        }

        var room = ItemCatalog.MaxStackSize(source.ItemId) - dest.Count;
        if (room <= 0)
        {
            return;
        }

        var moved = Math.Min(room, source.Count);
        to._slots[toIndex] = (dest.ItemId, dest.Count + moved, dest.Charge);
        var remaining = source.Count - moved;
        from._slots[fromIndex] = remaining > 0 ? (source.ItemId, remaining, source.Charge) : null;
    }

    public void Clear()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i] = null;
        }
    }

    public bool TryRemove(string itemId, int count = 1)
    {
        if (!Has(itemId, count))
        {
            return false;
        }

        var remaining = count;
        for (var i = 0; i < _slots.Length && remaining > 0; i++)
        {
            if (_slots[i] is not { } slot || slot.ItemId != itemId)
            {
                continue;
            }

            var removed = Math.Min(slot.Count, remaining);
            var newCount = slot.Count - removed;
            _slots[i] = newCount > 0 ? (itemId, newCount, slot.Charge) : null;
            remaining -= removed;
        }

        return true;
    }
}
