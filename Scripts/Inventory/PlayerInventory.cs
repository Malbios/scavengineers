using System.Collections.Generic;

namespace Scavengineers.Scripts.Inventory;

/// <summary>
/// A basic salvage inventory (docs/project-plan.md §4) — one hardcoded item type for now,
/// no data-driven item definitions yet. Item id -> count, nothing more.
/// </summary>
public sealed class PlayerInventory
{
    private readonly Dictionary<string, int> _counts = new();

    public IReadOnlyDictionary<string, int> Counts => _counts;

    public int CountOf(string itemId) => _counts.GetValueOrDefault(itemId);

    public bool Has(string itemId, int count) => CountOf(itemId) >= count;

    public void Add(string itemId, int count = 1) => _counts[itemId] = CountOf(itemId) + count;

    public void Clear() => _counts.Clear();

    public bool TryRemove(string itemId, int count = 1)
    {
        if (!Has(itemId, count))
        {
            return false;
        }

        var remaining = _counts[itemId] - count;
        if (remaining <= 0)
        {
            _counts.Remove(itemId);
        }
        else
        {
            _counts[itemId] = remaining;
        }

        return true;
    }
}
