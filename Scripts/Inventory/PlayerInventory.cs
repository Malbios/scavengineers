using System;
using System.Collections.Generic;

namespace Scavengineers.Scripts.Inventory;

/// <summary>
/// Composes the player's body slots (pockets + hands) with, optionally, an equipped
/// backpack's own separate <see cref="SlotContainer"/> (inventory arc Stage 3) — a backpack
/// is the first non-fungible item (it carries real per-instance state, its own contents),
/// so it's tracked as a distinct <see cref="EquippedContainer"/> rather than merged into the
/// body slots.
/// </summary>
public sealed class PlayerInventory
{
    // Placeholder/tunable — "pockets" capacity with nothing else equipped.
    private const int BaseSlotCount = 6;

    // Hands are real slots (not a separate reference like the old _heldItemId), reserved at the
    // end of the body array — Has/CountOf/Add/TryRemove/HasRoomFor all iterate the body
    // generically by index already, so they automatically include whatever's in a hand in every
    // capacity/count calculation with no special-casing.
    public const int HandCount = 2;
    public const int LeftHandSlotIndex = BaseSlotCount;
    public const int RightHandSlotIndex = BaseSlotCount + 1;

    // Placeholder/tunable — how many slots a worn backpack's own contents have.
    public const int BackpackSlotCount = 8;

    private readonly SlotContainer _body = new(BaseSlotCount + HandCount);

    /// <summary>The body's own slot container (pockets + hands) — read by InventorySlotUI/
    /// Player.cs to wire up the body/hand slots of the inventory panel.</summary>
    public SlotContainer Body => _body;

    /// <summary>An item id plus its own separate <see cref="SlotContainer"/> — the shape a
    /// worn backpack takes while equipped. The one container item type this stage supports is
    /// hardcoded as "backpack" (see EquipBackpackFromBody), matching Stage 1's "one flat
    /// catalog" precedent rather than a speculative "which items are containers" lookup.</summary>
    public sealed record EquippedContainer(string ItemId, SlotContainer Contents);

    public EquippedContainer? Backpack { get; private set; }

    /// <summary>The raw per-slot view the inventory panel UI reads for the body/hand slots
    /// (see InventorySlotUI) — a worn backpack's own contents are addressed separately via
    /// <see cref="Backpack"/>.Contents, never merged in here.</summary>
    public IReadOnlyList<(string ItemId, int Count)?> Slots => _body.Slots;

    public IReadOnlyDictionary<string, int> Counts
    {
        get
        {
            var counts = new Dictionary<string, int>(_body.Counts);
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

    public int CountOf(string itemId) => _body.CountOf(itemId) + (Backpack?.Contents.CountOf(itemId) ?? 0);

    public bool Has(string itemId, int count) => CountOf(itemId) >= count;

    /// <summary>Whether `count` more of this item would fit right now, across the body and (if
    /// worn) the backpack's contents, without actually adding anything.</summary>
    public bool HasRoomFor(string itemId, int count) =>
        _body.RoomFor(itemId) + (Backpack?.Contents.RoomFor(itemId) ?? 0) >= count;

    /// <summary>Adds up to `count`: tops up/fills the body first, then spills any remainder into
    /// the worn backpack's own contents (if any) — returns how much actually fit (0..count).</summary>
    public int Add(string itemId, int count = 1)
    {
        var addedToBody = _body.Add(itemId, count);
        var remaining = count - addedToBody;
        if (remaining <= 0 || Backpack is null)
        {
            return addedToBody;
        }

        return addedToBody + Backpack.Contents.Add(itemId, remaining);
    }

    public bool TryRemove(string itemId, int count = 1)
    {
        if (!Has(itemId, count))
        {
            return false;
        }

        var fromBody = Math.Min(_body.CountOf(itemId), count);
        if (fromBody > 0)
        {
            _body.TryRemove(itemId, fromBody);
        }

        var remaining = count - fromBody;
        if (remaining > 0)
        {
            Backpack?.Contents.TryRemove(itemId, remaining);
        }

        return true;
    }

    /// <summary>Moves item `itemId` from the first body slot (pockets only, never the *other*
    /// hand) that has it into `handIndex` — equipping it, falling back to the worn backpack's own
    /// contents if it isn't sitting in a pocket. <see cref="MoveSlot"/>/<see cref="SlotContainer.MoveBetween"/>
    /// already swap if `handIndex` is occupied by something else, which is what makes "replace
    /// whichever hand was filled most recently" trivial: call this again on that same hand, and
    /// its old contents land wherever the new item came from. Returns false (no-op) if neither
    /// the body nor the backpack currently holds this item.</summary>
    public bool EquipFromBody(string itemId, int handIndex)
    {
        for (var i = 0; i < BaseSlotCount; i++)
        {
            if (_body.Slots[i]?.ItemId == itemId)
            {
                MoveSlot(i, handIndex);
                return true;
            }
        }

        if (Backpack is not null)
        {
            for (var i = 0; i < Backpack.Contents.Slots.Count; i++)
            {
                if (Backpack.Contents.Slots[i]?.ItemId == itemId)
                {
                    SlotContainer.MoveBetween(Backpack.Contents, i, _body, handIndex);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The explicit toggle-off case — pressing the hotbar key for whatever's already in
    /// a hand, with no swap target involved. Moves the hand's contents back into the body
    /// (pockets only, never spilling into the *other* hand), leaving any leftover that didn't fit
    /// right back in the hand (same "nothing vanishes" partial-fit spirit as <see cref="Add"/>) —
    /// a completely full body means the hand keeps everything and this returns false.</summary>
    public bool UnequipToBody(int handIndex)
    {
        if (_body.Slots[handIndex] is not { } occupied)
        {
            return true;
        }

        _body.SetSlot(handIndex, null);
        var added = _body.AddWithinRange(occupied.ItemId, occupied.Count, 0, BaseSlotCount);
        var leftover = occupied.Count - added;
        _body.SetSlot(handIndex, leftover > 0 ? (occupied.ItemId, leftover) : null);
        return leftover == 0;
    }

    /// <summary>Moves slot `from` onto slot `to` within the body (pockets + hands) — the
    /// inventory panel UI's drag-and-drop mutation for ordinary slots (see InventorySlotUI).</summary>
    public void MoveSlot(int from, int to) => _body.MoveSlot(from, to);

    /// <summary>Equips a worn backpack by consuming one "backpack" item from the body (wherever
    /// it currently sits) and attaching a freshly-emptied <see cref="SlotContainer"/> as
    /// <see cref="Backpack"/>. Fails (no-op) if a backpack is already worn, or the body doesn't
    /// currently hold one.</summary>
    public bool EquipBackpackFromBody(int slotCount = BackpackSlotCount)
    {
        if (Backpack is not null)
        {
            return false;
        }

        if (!_body.TryRemove("backpack", 1))
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
    /// body-consume step in <see cref="EquipBackpackFromBody"/> — used by save/load restoration
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
        _body.Clear();
        Backpack = null;
    }
}
