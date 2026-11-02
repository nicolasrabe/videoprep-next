namespace Videoprep.Core.Models;

/// <summary>
/// RONDA 4 (spec E): reordering the queue by dragging. Only items that are still waiting move; the ones downloading or done stay
/// where they are and nothing can be dropped on top of them (or above them). Pure function: the caller saves the queue afterwards.
/// </summary>
public static class QueueOrder
{
    /// <summary>
    /// Moves the items with the given ids so they end up together, in their current relative order, just before the item that is at
    /// <paramref name="insertBefore"/> (an index into <paramref name="items"/> as it is now; <c>items.Count</c> = at the end).
    /// Locked items (<paramref name="locked"/>) are never moved and the drop is pushed below the last locked item.
    /// Returns true when the order changed.
    /// </summary>
    public static bool Move(IList<QueueItem> items, IReadOnlyCollection<int> ids, int insertBefore, Func<QueueItem, bool> locked)
    {
        var moving = items.Where(i => ids.Contains(i.Id) && !locked(i)).ToList();
        if (moving.Count == 0) return false;
        insertBefore = Math.Clamp(insertBefore, 0, items.Count);
        var lastLocked = -1;
        for (var i = 0; i < items.Count; i++) if (locked(items[i])) lastLocked = i;
        insertBefore = Math.Max(insertBefore, lastLocked + 1);

        // The item that stays right after the dropped block (the first one at or below the drop that is not moving), or the end.
        QueueItem? anchor = null;
        for (var i = insertBefore; i < items.Count; i++)
            if (!moving.Contains(items[i])) { anchor = items[i]; break; }

        var rest = items.Where(i => !moving.Contains(i)).ToList();
        var at = anchor is null ? rest.Count : rest.IndexOf(anchor);
        var result = new List<QueueItem>(rest);
        result.InsertRange(at, moving);

        var changed = false;
        for (var i = 0; i < result.Count; i++) if (!ReferenceEquals(items[i], result[i])) { changed = true; break; }
        if (!changed) return false;
        for (var i = 0; i < result.Count; i++) items[i] = result[i];
        return true;
    }
}
