using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Rules.Validation;

internal enum DomainKind
{
    Number,
    Boolean,
    Text,
}

internal readonly record struct IntervalBound<T>(bool HasValue, T Value)
{
    public static IntervalBound<T> Unbounded => new(false, default!);

    public static IntervalBound<T> Of(T value) => new(true, value);
}

internal readonly record struct OrderedInterval<T>(
    IntervalBound<T> Min,
    bool MinInclusive,
    IntervalBound<T> Max,
    bool MaxInclusive);

internal readonly record struct ValueDomain(
    DomainKind Kind,
    bool AllowsNull,
    bool AllowsTrue,
    bool AllowsFalse,
    bool AllowsBlankText,
    IReadOnlyList<OrderedInterval<decimal>> Numbers,
    IReadOnlyList<OrderedInterval<string>> Texts)
{
    public bool HasPopulatedValue => Kind switch
    {
        DomainKind.Number => Numbers.Count > 0,
        DomainKind.Boolean => AllowsTrue || AllowsFalse,
        _ => AllowsBlankText || Texts.Count > 0,
    };

    public bool IsCompletelyEmpty => !AllowsNull && !HasPopulatedValue;

    public static ValueDomain Empty(DomainKind kind) =>
        new(kind, false, false, false, false, [], []);

    public static ValueDomain NullOnly(DomainKind kind) =>
        new(kind, true, false, false, false, [], []);

    public static ValueDomain EmptyOrNull(DomainKind kind) => kind switch
    {
        DomainKind.Text => new(DomainKind.Text, true, false, false, true, [], [ConditionDomainExtractor.TextPoint(string.Empty)]),
        _ => NullOnly(kind),
    };

    public static ValueDomain Full(DomainKind kind) => kind switch
    {
        DomainKind.Number => new(
            DomainKind.Number,
            true,
            false,
            false,
            false,
            [new OrderedInterval<decimal>(IntervalBound<decimal>.Unbounded, false, IntervalBound<decimal>.Unbounded, false)],
            []),
        DomainKind.Boolean => new(DomainKind.Boolean, true, true, true, false, [], []),
        _ => new(
            DomainKind.Text,
            true,
            false,
            false,
            true,
            [],
            [new OrderedInterval<string>(IntervalBound<string>.Unbounded, false, IntervalBound<string>.Unbounded, false)]),
    };

    public static ValueDomain FromNumbers(IReadOnlyList<OrderedInterval<decimal>> intervals) =>
        new(DomainKind.Number, false, false, false, false, IntervalOperations.Normalize(intervals, Comparer<decimal>.Default, null), []);

    public static ValueDomain FromTexts(bool allowsBlankText, IReadOnlyList<OrderedInterval<string>> intervals) =>
        new(DomainKind.Text, false, false, false, allowsBlankText, [], IntervalOperations.Normalize(intervals, StringComparer.Ordinal, IsDegenerateTextInterval));

    public ValueDomain Intersect(ValueDomain other) => new(
        Kind,
        AllowsNull && other.AllowsNull,
        AllowsTrue && other.AllowsTrue,
        AllowsFalse && other.AllowsFalse,
        AllowsBlankText && other.AllowsBlankText,
        Kind == DomainKind.Number ? IntervalOperations.Intersect(Numbers, other.Numbers, Comparer<decimal>.Default, null) : [],
        Kind == DomainKind.Text ? IntervalOperations.Intersect(Texts, other.Texts, StringComparer.Ordinal, IsDegenerateTextInterval) : []);

    public ValueDomain Union(ValueDomain other) => new(
        Kind,
        AllowsNull || other.AllowsNull,
        AllowsTrue || other.AllowsTrue,
        AllowsFalse || other.AllowsFalse,
        AllowsBlankText || other.AllowsBlankText,
        Kind == DomainKind.Number ? IntervalOperations.Normalize([.. Numbers, .. other.Numbers], Comparer<decimal>.Default, null) : [],
        Kind == DomainKind.Text ? IntervalOperations.Normalize([.. Texts, .. other.Texts], StringComparer.Ordinal, IsDegenerateTextInterval) : []);

    public ValueDomain Except(ValueDomain removed)
    {
        var remainingNumbers = Kind == DomainKind.Number
            ? IntervalOperations.Subtract(Numbers, removed.Numbers, Comparer<decimal>.Default, null)
            : [];
        var remainingTexts = Kind == DomainKind.Text
            ? IntervalOperations.Subtract(Texts, removed.Texts, StringComparer.Ordinal, IsDegenerateTextInterval)
            : [];

        if (Kind == DomainKind.Text && removed.AllowsBlankText && AllowsBlankText)
        {
            remainingTexts = IntervalOperations.Subtract(remainingTexts, [ConditionDomainExtractor.TextPoint(string.Empty)], StringComparer.Ordinal, IsDegenerateTextInterval);
        }

        var hasEmptyStringInterval = Kind == DomainKind.Text && ContainsEmptyString(remainingTexts);
        return new ValueDomain(
            Kind,
            AllowsNull && !removed.AllowsNull,
            AllowsTrue && !removed.AllowsTrue,
            AllowsFalse && !removed.AllowsFalse,
            AllowsBlankText && !removed.AllowsBlankText && (hasEmptyStringInterval || remainingTexts.Count > 0),
            remainingNumbers,
            remainingTexts);
    }

    private static bool ContainsEmptyString(IReadOnlyList<OrderedInterval<string>> intervals) =>
        intervals.Any(i =>
            (!i.Min.HasValue || (i.Min.Value.Length == 0 && i.MinInclusive))
            && (!i.Max.HasValue || i.Max.Value.Length > 0 || i.MaxInclusive));

    private static bool IsDegenerateTextInterval(OrderedInterval<string> interval)
    {
        if (interval.Max.HasValue && interval.Max.Value.Length == 0 && !interval.MaxInclusive)
        {
            return true;
        }

        return interval.Min.HasValue
            && interval.Max.HasValue
            && !interval.MinInclusive
            && !interval.MaxInclusive
            && string.Equals(interval.Max.Value, interval.Min.Value + "\0", StringComparison.Ordinal);
    }
}

internal static class IntervalOperations
{
    public static List<OrderedInterval<T>> Normalize<T>(
        IEnumerable<OrderedInterval<T>> source,
        IComparer<T> comparer,
        Func<OrderedInterval<T>, bool>? extraEmptyCheck)
    {
        var valid = source
            .Where(i => !IsEmpty(i, comparer, extraEmptyCheck))
            .OrderBy(i => i.Min.HasValue ? 1 : 0)
            .ThenBy(i => i.Min.Value, comparer)
            .ThenByDescending(i => i.MinInclusive)
            .ToList();
        if (valid.Count <= 1)
        {
            return valid;
        }

        var merged = new List<OrderedInterval<T>> { valid[0] };
        for (var i = 1; i < valid.Count; i++)
        {
            var current = valid[i];
            var last = merged[^1];
            if (OverlapOrTouch(last, current, comparer))
            {
                var (max, maxInc) = MaxUpperUnionBound(last.Max, last.MaxInclusive, current.Max, current.MaxInclusive, comparer);
                merged[^1] = new OrderedInterval<T>(last.Min, last.MinInclusive, max, maxInc);
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }

    public static List<OrderedInterval<T>> Intersect<T>(
        IReadOnlyList<OrderedInterval<T>> left,
        IReadOnlyList<OrderedInterval<T>> right,
        IComparer<T> comparer,
        Func<OrderedInterval<T>, bool>? extraEmptyCheck)
    {
        var result = new List<OrderedInterval<T>>();
        foreach (var a in left)
        {
            foreach (var b in right)
            {
                var (min, minInc) = MaxLowerBound(a.Min, a.MinInclusive, b.Min, b.MinInclusive, comparer);
                var (max, maxInc) = MinUpperBound(a.Max, a.MaxInclusive, b.Max, b.MaxInclusive, comparer);
                var candidate = new OrderedInterval<T>(min, minInc, max, maxInc);
                if (!IsEmpty(candidate, comparer, extraEmptyCheck))
                {
                    result.Add(candidate);
                }
            }
        }

        return Normalize(result, comparer, extraEmptyCheck);
    }

    public static List<OrderedInterval<T>> Subtract<T>(
        IReadOnlyList<OrderedInterval<T>> source,
        IReadOnlyList<OrderedInterval<T>> removed,
        IComparer<T> comparer,
        Func<OrderedInterval<T>, bool>? extraEmptyCheck)
    {
        var current = Normalize(source, comparer, extraEmptyCheck);
        foreach (var cut in removed)
        {
            if (IsEmpty(cut, comparer, extraEmptyCheck))
            {
                continue;
            }

            var next = new List<OrderedInterval<T>>();
            foreach (var seg in current)
            {
                if (cut.Min.HasValue)
                {
                    var (max, maxInc) = MinUpperBound(seg.Max, seg.MaxInclusive, cut.Min, !cut.MinInclusive, comparer);
                    var leftPiece = new OrderedInterval<T>(seg.Min, seg.MinInclusive, max, maxInc);
                    if (!IsEmpty(leftPiece, comparer, extraEmptyCheck))
                    {
                        next.Add(leftPiece);
                    }
                }

                if (cut.Max.HasValue)
                {
                    var (min, minInc) = MaxLowerBound(seg.Min, seg.MinInclusive, cut.Max, !cut.MaxInclusive, comparer);
                    var rightPiece = new OrderedInterval<T>(min, minInc, seg.Max, seg.MaxInclusive);
                    if (!IsEmpty(rightPiece, comparer, extraEmptyCheck))
                    {
                        next.Add(rightPiece);
                    }
                }
            }

            current = Normalize(next, comparer, extraEmptyCheck);
        }

        return current;
    }

    private static bool IsEmpty<T>(
        OrderedInterval<T> interval,
        IComparer<T> comparer,
        Func<OrderedInterval<T>, bool>? extraEmptyCheck)
    {
        if (extraEmptyCheck is not null && extraEmptyCheck(interval))
        {
            return true;
        }

        if (!interval.Min.HasValue || !interval.Max.HasValue)
        {
            return false;
        }

        var cmp = comparer.Compare(interval.Min.Value, interval.Max.Value);
        return cmp > 0 || (cmp == 0 && (!interval.MinInclusive || !interval.MaxInclusive));
    }

    private static bool OverlapOrTouch<T>(OrderedInterval<T> left, OrderedInterval<T> right, IComparer<T> comparer)
    {
        if (!left.Max.HasValue || !right.Min.HasValue)
        {
            return true;
        }

        var cmp = comparer.Compare(left.Max.Value, right.Min.Value);
        if (cmp > 0)
        {
            return true;
        }

        return cmp == 0 && (left.MaxInclusive || right.MinInclusive);
    }

    private static (IntervalBound<T> Bound, bool Inclusive) MaxLowerBound<T>(
        IntervalBound<T> a,
        bool aInc,
        IntervalBound<T> b,
        bool bInc,
        IComparer<T> comparer)
    {
        if (!a.HasValue)
        {
            return (b, bInc);
        }

        if (!b.HasValue)
        {
            return (a, aInc);
        }

        var cmp = comparer.Compare(a.Value, b.Value);
        if (cmp != 0)
        {
            return cmp > 0 ? (a, aInc) : (b, bInc);
        }

        return (a, aInc && bInc);
    }

    private static (IntervalBound<T> Bound, bool Inclusive) MinUpperBound<T>(
        IntervalBound<T> a,
        bool aInc,
        IntervalBound<T> b,
        bool bInc,
        IComparer<T> comparer)
    {
        if (!a.HasValue)
        {
            return (b, bInc);
        }

        if (!b.HasValue)
        {
            return (a, aInc);
        }

        var cmp = comparer.Compare(a.Value, b.Value);
        if (cmp != 0)
        {
            return cmp < 0 ? (a, aInc) : (b, bInc);
        }

        return (a, aInc && bInc);
    }

    private static (IntervalBound<T> Bound, bool Inclusive) MaxUpperUnionBound<T>(
        IntervalBound<T> a,
        bool aInc,
        IntervalBound<T> b,
        bool bInc,
        IComparer<T> comparer)
    {
        if (!a.HasValue || !b.HasValue)
        {
            return (IntervalBound<T>.Unbounded, false);
        }

        var cmp = comparer.Compare(a.Value, b.Value);
        if (cmp != 0)
        {
            return cmp > 0 ? (a, aInc) : (b, bInc);
        }

        return (a, aInc || bInc);
    }
}
