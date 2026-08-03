namespace FunctionFoundry.Data;

/// <summary>
/// Options for <see cref="KWaySortedMerge"/>.
/// </summary>
/// <param name="DeduplicateAdjacent">When true, adjacent equal items across the merge are emitted once.</param>
public sealed record KWaySortedMergeOptions(bool DeduplicateAdjacent = false);

/// <summary>
/// Merges multiple already-sorted sequences into one sorted sequence using a heap.
/// </summary>
public static class KWaySortedMerge
{
    /// <summary>
    /// Merges <paramref name="sources"/> assuming each source is already ordered by <paramref name="comparer"/>.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="sources">Sorted input sequences.</param>
    /// <param name="comparer">Comparer defining order. When null, <see cref="Comparer{T}.Default"/> is used.</param>
    /// <param name="options">Optional merge options.</param>
    /// <returns>A lazily merged sequence.</returns>
    public static IEnumerable<T> Merge<T>(
        IReadOnlyList<IEnumerable<T>> sources,
        IComparer<T>? comparer = null,
        KWaySortedMergeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        comparer ??= Comparer<T>.Default;
        options ??= new KWaySortedMergeOptions();

        var enumerators = new List<IEnumerator<T>>(sources.Count);
        try
        {
            for (int i = 0; i < sources.Count; i++)
            {
                IEnumerable<T>? source = sources[i];
                ArgumentNullException.ThrowIfNull(source);
                enumerators.Add(source.GetEnumerator());
            }

            var heap = new PriorityQueue<(T Item, int SourceIndex), (T Item, int SourceIndex)>(
                Comparer<(T Item, int SourceIndex)>.Create((left, right) =>
                {
                    int cmp = comparer.Compare(left.Item, right.Item);
                    return cmp != 0 ? cmp : left.SourceIndex.CompareTo(right.SourceIndex);
                }));

            for (int i = 0; i < enumerators.Count; i++)
            {
                if (enumerators[i].MoveNext())
                {
                    T current = enumerators[i].Current;
                    heap.Enqueue((current, i), (current, i));
                }
            }

            bool hasPrevious = false;
            T previous = default!;
            while (heap.Count > 0)
            {
                (T item, int sourceIndex) = heap.Dequeue();
                if (!(options.DeduplicateAdjacent && hasPrevious && comparer.Compare(previous, item) == 0))
                {
                    yield return item;
                    previous = item;
                    hasPrevious = true;
                }

                if (enumerators[sourceIndex].MoveNext())
                {
                    T next = enumerators[sourceIndex].Current;
                    heap.Enqueue((next, sourceIndex), (next, sourceIndex));
                }
            }
        }
        finally
        {
            foreach (IEnumerator<T> enumerator in enumerators)
            {
                enumerator.Dispose();
            }
        }
    }
}
