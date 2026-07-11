using System;
using System.Collections.Generic;
using System.Linq;

namespace Scavengineers.Scripts.Inventory;

/// <summary>
/// A fixed-size slot inventory (docs/project-plan.md §4) — extracted from PlayerInventory
/// (inventory arc Stage 3) so a backpack's own contents can reuse the exact same mechanics
/// instead of a parallel implementation. PlayerInventory now composes one of these for the
/// body (pockets + hands) and, optionally, another for an equipped backpack.
/// </summary>
public sealed class SlotContainer
{
    private readonly (string ItemId, int Count)?[] _slots;

    public SlotContainer(int slotCount)
    {
        _slots = new (string, int)?[slotCount];
    }

    /// <summary>The raw per-slot view the inventory panel UI reads (see InventorySlotUI) — index
    /// identity matters here (which physical slot holds what), unlike <see cref="Counts"/>'s
    /// aggregated view for the plain-text HUD summary.</summary>
    public IReadOnlyList<(string ItemId, int Count)?> Slots => _slots;

    /// <summary>Direct slot assignment, bypassing the fill/merge logic in <see cref="Add"/> and
    /// <see cref="MoveSlot"/> — used by PlayerInventory's hand-unequip flow, which needs to clear
    /// a specific slot outright before topping up the body range from its old contents.</summary>
    public void SetSlot(int index, (string ItemId, int Count)? value) => _slots[index] = value;

    public IReadOnlyDictionary<string, int> Counts =>
        _slots.Where(s => s is not null)
            .GroupBy(s => s!.Value.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s!.Value.Count));

    public int CountOf(string itemId) => _slots.Where(s => s?.ItemId == itemId).Sum(s => s?.Count ?? 0);

    public bool Has(string itemId, int count) => CountOf(itemId) >= count;

    /// <summary>How much of `itemId` would still fit right now, without actually adding
    /// anything — for a caller that needs to know before committing to a cost (see
    /// StationConsoleVerbTarget's Buy verb), or that needs to combine room across more than one
    /// container (see PlayerInventory.HasRoomFor).</summary>
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

    /// <summary>Adds up to `count`, respecting both this item's own stack limit
    /// (<see cref="ItemCatalog.MaxStackSize"/>) and the number of free slots — returns how much
    /// actually fit (0..count). A caller whose item has nowhere else to fall back to (a refund,
    /// a purchase) must handle a partial result itself; see InventoryOverflow.</summary>
    public int Add(string itemId, int count = 1) => AddWithinRange(itemId, count, 0, _slots.Length);

    /// <summary>Adds within a sub-range of slots only — used by PlayerInventory to target just
    /// the body range (e.g. unequipping a hand without spilling into the *other* hand).</summary>
    public int AddWithinRange(string itemId, int count, int startInclusive, int endExclusive)
    {
        var remaining = count;
        var maxStack = ItemCatalog.MaxStackSize(itemId);

        // Top up existing stacks of the same item first.
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
            _slots[i] = (itemId, slot.Count + added);
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
            _slots[i] = (itemId, added);
            remaining -= added;
        }

        return count - remaining;
    }

    /// <summary>Moves slot `from` onto slot `to` — the inventory panel UI's drag-and-drop
    /// mutation (see InventorySlotUI). Different items swap; the same item tops `to` up from
    /// `from` up to that item's stack limit, leaving any remainder in `from` (same "partial fit"
    /// spirit as <see cref="Add"/>). A no-op for an out-of-range index, `from == to`, an empty
    /// `from`, or a same-item `to` that's already full.</summary>
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
        _slots[to] = (dest.ItemId, dest.Count + moved);
        var remaining = source.Count - moved;
        _slots[from] = remaining > 0 ? (source.ItemId, remaining) : null;
    }

    /// <summary>Same drag-and-drop mutation as <see cref="MoveSlot"/>, but for two different
    /// containers (e.g. a body slot and a worn backpack's own contents — see InventorySlotUI) —
    /// delegates to <see cref="MoveSlot"/> when `from` and `to` are actually the same container.</summary>
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
        to._slots[toIndex] = (dest.ItemId, dest.Count + moved);
        var remaining = source.Count - moved;
        from._slots[fromIndex] = remaining > 0 ? (source.ItemId, remaining) : null;
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
            _slots[i] = newCount > 0 ? (itemId, newCount) : null;
            remaining -= removed;
        }

        return true;
    }
}
