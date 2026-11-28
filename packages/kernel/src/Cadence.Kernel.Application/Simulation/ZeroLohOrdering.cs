using System;
using System.Collections.Generic;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>
/// SOH-bounded stable ordering helpers that stream items in <see cref="Guid"/> or ordinal key order without ever
/// allocating buffers on the Large Object Heap (<c>&gt;= 85,000</c> bytes).
/// </summary>
internal static class ZeroLohOrdering
{
    public const int MaxSohChunkLength = 1024;

    public static void ForEachOrderedByGuid<TItem, TState>(
        IReadOnlyList<TItem> items,
        Func<TItem, Guid> getId,
        TState state,
        Action<TState, TItem> visit)
    {
        var count = items.Count;
        if (count == 0)
        {
            return;
        }

        if (count == 1 || IsSortedByGuid(items, getId))
        {
            for (var i = 0; i < count; i++)
            {
                visit(state, items[i]);
            }

            return;
        }

        if (count <= MaxSohChunkLength)
        {
            var singleChunk = BuildGuidChunk(items, 0, count, getId);
            for (var i = 0; i < singleChunk.Length; i++)
            {
                visit(state, singleChunk[i].Value);
            }

            return;
        }

        var chunkCount = (count + MaxSohChunkLength - 1) / MaxSohChunkLength;
        var chunks = new IndexedGuidItem<TItem>[chunkCount][];
        for (var c = 0; c < chunkCount; c++)
        {
            var start = c * MaxSohChunkLength;
            var length = Math.Min(MaxSohChunkLength, count - start);
            chunks[c] = BuildGuidChunk(items, start, length, getId);
        }

        MergeGuidChunks(chunks, state, visit);
    }

    public static void ForEachOrderedByKey<TItem, TState>(
        IEnumerable<TItem> items,
        Func<TItem, string> getKey,
        TState state,
        Action<TState, TItem> visit)
    {
        var chunks = new List<IndexedKeyItem<TItem>[]>();
        var current = new IndexedKeyItem<TItem>[MaxSohChunkLength];
        var currentCount = 0;
        var order = 0;

        foreach (var item in items)
        {
            current[currentCount++] = new IndexedKeyItem<TItem>(getKey(item), order++, item);
            if (currentCount == MaxSohChunkLength)
            {
                Array.Sort(current, 0, currentCount, IndexedKeyItemComparer<TItem>.Instance);
                chunks.Add(current);
                current = new IndexedKeyItem<TItem>[MaxSohChunkLength];
                currentCount = 0;
            }
        }

        if (currentCount > 0)
        {
            var tail = new IndexedKeyItem<TItem>[currentCount];
            Array.Copy(current, tail, currentCount);
            Array.Sort(tail, 0, currentCount, IndexedKeyItemComparer<TItem>.Instance);
            chunks.Add(tail);
        }

        if (chunks.Count == 0)
        {
            return;
        }

        if (chunks.Count == 1)
        {
            var only = chunks[0];
            for (var i = 0; i < only.Length; i++)
            {
                visit(state, only[i].Value);
            }

            return;
        }

        MergeKeyChunks([.. chunks], state, visit);
    }

    private static bool IsSortedByGuid<TItem>(IReadOnlyList<TItem> items, Func<TItem, Guid> getId)
    {
        var previous = getId(items[0]);
        for (var i = 1; i < items.Count; i++)
        {
            var current = getId(items[i]);
            if (previous.CompareTo(current) > 0)
            {
                return false;
            }

            previous = current;
        }

        return true;
    }

    private static IndexedGuidItem<TItem>[] BuildGuidChunk<TItem>(
        IReadOnlyList<TItem> items,
        int start,
        int length,
        Func<TItem, Guid> getId)
    {
        var chunk = new IndexedGuidItem<TItem>[length];
        for (var i = 0; i < length; i++)
        {
            var index = start + i;
            var item = items[index];
            chunk[i] = new IndexedGuidItem<TItem>(getId(item), index, item);
        }

        Array.Sort(chunk, 0, length, IndexedGuidItemComparer<TItem>.Instance);
        return chunk;
    }

    private static void MergeGuidChunks<TItem, TState>(
        IndexedGuidItem<TItem>[][] chunks,
        TState state,
        Action<TState, TItem> visit)
    {
        var chunkCount = chunks.Length;
        var positions = new int[chunkCount];
        var heap = new int[chunkCount];
        var heapSize = 0;

        for (var c = 0; c < chunkCount; c++)
        {
            if (chunks[c].Length > 0)
            {
                heap[heapSize] = c;
                SiftUpGuid(heap, heapSize, chunks, positions);
                heapSize++;
            }
        }

        while (heapSize > 0)
        {
            var topChunk = heap[0];
            var topPos = positions[topChunk];
            visit(state, chunks[topChunk][topPos].Value);

            topPos++;
            positions[topChunk] = topPos;
            if (topPos < chunks[topChunk].Length)
            {
                SiftDownGuid(heap, heapSize, 0, chunks, positions);
            }
            else
            {
                heapSize--;
                if (heapSize > 0)
                {
                    heap[0] = heap[heapSize];
                    SiftDownGuid(heap, heapSize, 0, chunks, positions);
                }
            }
        }
    }

    private static void SiftUpGuid<TItem>(
        int[] heap,
        int index,
        IndexedGuidItem<TItem>[][] chunks,
        int[] positions)
    {
        while (index > 0)
        {
            var parent = (index - 1) >> 1;
            var childChunk = heap[index];
            var parentChunk = heap[parent];
            if (CompareGuidEntry(chunks[childChunk][positions[childChunk]], chunks[parentChunk][positions[parentChunk]]) >= 0)
            {
                break;
            }

            heap[index] = parentChunk;
            heap[parent] = childChunk;
            index = parent;
        }
    }

    private static void SiftDownGuid<TItem>(
        int[] heap,
        int heapSize,
        int index,
        IndexedGuidItem<TItem>[][] chunks,
        int[] positions)
    {
        while (true)
        {
            var left = (index << 1) + 1;
            if (left >= heapSize)
            {
                return;
            }

            var smallest = index;
            if (CompareGuidEntry(chunks[heap[left]][positions[heap[left]]], chunks[heap[smallest]][positions[heap[smallest]]]) < 0)
            {
                smallest = left;
            }

            var right = left + 1;
            if (right < heapSize
                && CompareGuidEntry(chunks[heap[right]][positions[heap[right]]], chunks[heap[smallest]][positions[heap[smallest]]]) < 0)
            {
                smallest = right;
            }

            if (smallest == index)
            {
                return;
            }

            (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
            index = smallest;
        }
    }

    private static int CompareGuidEntry<TItem>(in IndexedGuidItem<TItem> x, in IndexedGuidItem<TItem> y)
    {
        var cmp = x.Id.CompareTo(y.Id);
        return cmp != 0 ? cmp : x.Order.CompareTo(y.Order);
    }

    private static void MergeKeyChunks<TItem, TState>(
        IndexedKeyItem<TItem>[][] chunks,
        TState state,
        Action<TState, TItem> visit)
    {
        var chunkCount = chunks.Length;
        var positions = new int[chunkCount];
        var heap = new int[chunkCount];
        var heapSize = 0;

        for (var c = 0; c < chunkCount; c++)
        {
            if (chunks[c].Length > 0)
            {
                heap[heapSize] = c;
                SiftUpKey(heap, heapSize, chunks, positions);
                heapSize++;
            }
        }

        while (heapSize > 0)
        {
            var topChunk = heap[0];
            var topPos = positions[topChunk];
            visit(state, chunks[topChunk][topPos].Value);

            topPos++;
            positions[topChunk] = topPos;
            if (topPos < chunks[topChunk].Length)
            {
                SiftDownKey(heap, heapSize, 0, chunks, positions);
            }
            else
            {
                heapSize--;
                if (heapSize > 0)
                {
                    heap[0] = heap[heapSize];
                    SiftDownKey(heap, heapSize, 0, chunks, positions);
                }
            }
        }
    }

    private static void SiftUpKey<TItem>(
        int[] heap,
        int index,
        IndexedKeyItem<TItem>[][] chunks,
        int[] positions)
    {
        while (index > 0)
        {
            var parent = (index - 1) >> 1;
            if (CompareKeyEntry(chunks[heap[index]][positions[heap[index]]], chunks[heap[parent]][positions[heap[parent]]]) >= 0)
            {
                return;
            }

            (heap[index], heap[parent]) = (heap[parent], heap[index]);
            index = parent;
        }
    }

    private static void SiftDownKey<TItem>(
        int[] heap,
        int heapSize,
        int index,
        IndexedKeyItem<TItem>[][] chunks,
        int[] positions)
    {
        while (true)
        {
            var left = (index << 1) + 1;
            if (left >= heapSize)
            {
                return;
            }

            var smallest = index;
            if (CompareKeyEntry(chunks[heap[left]][positions[heap[left]]], chunks[heap[smallest]][positions[heap[smallest]]]) < 0)
            {
                smallest = left;
            }

            var right = left + 1;
            if (right < heapSize
                && CompareKeyEntry(chunks[heap[right]][positions[heap[right]]], chunks[heap[smallest]][positions[heap[smallest]]]) < 0)
            {
                smallest = right;
            }

            if (smallest == index)
            {
                return;
            }

            (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
            index = smallest;
        }
    }

    private static int CompareKeyEntry<TItem>(in IndexedKeyItem<TItem> x, in IndexedKeyItem<TItem> y)
    {
        var cmp = string.CompareOrdinal(x.Key, y.Key);
        return cmp != 0 ? cmp : x.Order.CompareTo(y.Order);
    }

    private readonly record struct IndexedGuidItem<TItem>(Guid Id, int Order, TItem Value);

    private readonly record struct IndexedKeyItem<TItem>(string Key, int Order, TItem Value);

    private sealed class IndexedGuidItemComparer<TItem> : IComparer<IndexedGuidItem<TItem>>
    {
        public static readonly IndexedGuidItemComparer<TItem> Instance = new();

        public int Compare(IndexedGuidItem<TItem> x, IndexedGuidItem<TItem> y) => CompareGuidEntry(x, y);
    }

    private sealed class IndexedKeyItemComparer<TItem> : IComparer<IndexedKeyItem<TItem>>
    {
        public static readonly IndexedKeyItemComparer<TItem> Instance = new();

        public int Compare(IndexedKeyItem<TItem> x, IndexedKeyItem<TItem> y) => CompareKeyEntry(x, y);
    }
}
