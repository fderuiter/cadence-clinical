using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Validation;

/// <summary>
/// Analyzes rule packs for mutually conflicting conditions across cross-form and field checks.
/// </summary>
public static class ConflictingConditionAnalyzer
{
    /// <summary>
    /// Analyzes the specified rule packs for mutually conflicting conditions.
    /// </summary>
    /// <param name="packs">The rule packs to analyze.</param>
    /// <returns>A list of conflicting rule pair descriptions.</returns>
    public static IReadOnlyList<string> Analyze(IEnumerable<RulePack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);

        var conflicts = new SortedSet<string>(StringComparer.Ordinal);
        var rules = packs.SelectMany(p => p.Rules).ToList();

        for (var i = 0; i < rules.Count; i++)
        {
            for (var j = i + 1; j < rules.Count; j++)
            {
                var r1 = rules[i];
                var r2 = rules[j];
                if (!ApplicabilityOverlaps(r1, r2))
                {
                    continue;
                }

                var sharedDataset = r1.Applicability.Dataset ?? r2.Applicability.Dataset;
                if (AreConflicting(r1.Condition, r2.Condition, sharedDataset))
                {
                    var (first, second) = CompareRuleOrder(r1, r2) <= 0 ? (r1, r2) : (r2, r1);
                    var datasetLabel = sharedDataset ?? "*";
                    conflicts.Add($"Mutually conflicting rule conditions detected on dataset '{datasetLabel}': {first.Id} (v{first.Version}) vs {second.Id} (v{second.Version}).");
                }
            }
        }

        return [.. conflicts];
    }

    private static int CompareRuleOrder(Rule left, Rule right)
    {
        var idComparison = StringComparer.Ordinal.Compare(left.Id, right.Id);
        return idComparison != 0 ? idComparison : left.Version.CompareTo(right.Version);
    }

    private static bool ApplicabilityOverlaps(Rule r1, Rule r2) =>
        r1.Applicability.Stage == r2.Applicability.Stage
        && MatchesOptional(r1.Applicability.Standard, r2.Applicability.Standard)
        && MatchesOptional(r1.Applicability.StandardVersion, r2.Applicability.StandardVersion)
        && MatchesOptional(r1.Applicability.Agency, r2.Applicability.Agency)
        && MatchesOptional(r1.Applicability.Dataset, r2.Applicability.Dataset)
        && EffectiveOverlaps(r1.Effective, r2.Effective);

    private static bool MatchesOptional(string? first, string? second) =>
        first is null || second is null || string.Equals(first, second, StringComparison.Ordinal);

    private static bool EffectiveOverlaps(EffectivePeriod first, EffectivePeriod second)
    {
        var start = MaxDate(first.From, second.From);
        var end = MinDate(first.Until, second.Until);
        return start is null || end is null || start.Value < end.Value;
    }

    private static DateOnly? MaxDate(DateOnly? left, DateOnly? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }

    private static DateOnly? MinDate(DateOnly? left, DateOnly? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left.Value <= right.Value ? left : right;
    }

    private static bool AreConflicting(RuleExpression expr1, RuleExpression expr2, string? sharedDataset)
    {
        var candidates = CollectCandidateVariables(expr1, sharedDataset)
            .Intersect(CollectCandidateVariables(expr2, sharedDataset), StringComparer.Ordinal);

        foreach (var variable in candidates)
        {
            var kind = InferDomainKind(expr1, expr2, variable, sharedDataset);
            if (!TryExtractAllowedDomain(expr1, variable, sharedDataset, kind, out var allowed1)
                || !TryExtractAllowedDomain(expr2, variable, sharedDataset, kind, out var allowed2))
            {
                continue;
            }

            if (DomainsConflict(allowed1, allowed2))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> CollectCandidateVariables(RuleExpression expr, string? sharedDataset) =>
        RuleExpressionJson.Variables(expr)
            .Select(v => NormalizeVariableName(v, sharedDataset))
            .ToHashSet(StringComparer.Ordinal);

    private static string NormalizeVariableName(string name, string? sharedDataset)
    {
        if (!string.IsNullOrWhiteSpace(sharedDataset))
        {
            var prefix = sharedDataset + ".";
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return name[prefix.Length..];
            }
        }

        return name;
    }

    private static DomainKind InferDomainKind(RuleExpression expr1, RuleExpression expr2, string variable, string? sharedDataset)
    {
        var literals = new List<RuleValue>();
        CollectComparisonLiterals(expr1, variable, sharedDataset, literals);
        CollectComparisonLiterals(expr2, variable, sharedDataset, literals);

        var hasNumber = literals.Any(l => l.TryGetNumber(out _));
        var hasNonNumericText = literals.Any(l => l.Kind == RuleValueKind.Text && !l.TryGetNumber(out _));
        var hasBoolean = literals.Any(l => l.Kind == RuleValueKind.Boolean);

        if (hasNumber && !hasNonNumericText && !hasBoolean)
        {
            return DomainKind.Number;
        }

        if (hasBoolean && !hasNumber && !hasNonNumericText)
        {
            return DomainKind.Boolean;
        }

        return DomainKind.Text;
    }

    private static void CollectComparisonLiterals(
        RuleExpression expr,
        string variable,
        string? sharedDataset,
        List<RuleValue> literals)
    {
        switch (expr)
        {
            case ComparisonExpression comp when TryNormalizeComparison(comp, variable, sharedDataset, out _, out var lit):
                literals.Add(lit);
                break;
            case NotExpression not:
                CollectComparisonLiterals(not.Operand, variable, sharedDataset, literals);
                break;
            case LogicalExpression logical:
                foreach (var op in logical.Operands)
                {
                    CollectComparisonLiterals(op, variable, sharedDataset, literals);
                }

                break;
            case FunctionExpression { Function: RuleFunction.In or RuleFunction.NotIn } fn
                when fn.Arguments.Count >= 2
                     && fn.Arguments[0] is VariableExpression v
                     && string.Equals(NormalizeVariableName(v.Name, sharedDataset), variable, StringComparison.Ordinal):
                foreach (var arg in fn.Arguments.Skip(1))
                {
                    if (arg is LiteralExpression litArg)
                    {
                        literals.Add(litArg.Value);
                    }
                }

                break;
        }
    }

    private static bool TryExtractAllowedDomain(
        RuleExpression expr,
        string variable,
        string? sharedDataset,
        DomainKind kind,
        out ValueDomain allowed)
    {
        var vars = CollectCandidateVariables(expr, sharedDataset);
        if (vars.Count == 1 && vars.Contains(variable))
        {
            if (TryEvaluateKleene(expr, variable, sharedDataset, kind, out var kleene))
            {
                // A rule fires only when its condition evaluates to True; False and Unknown are allowed.
                allowed = ValueDomain.Full(kind).Except(kleene.TrueSet);
                return true;
            }
        }
        else if (expr is LogicalExpression { Operator: LogicalOperator.Or } orExpr)
        {
            var combined = ValueDomain.Full(kind);
            var constrained = false;
            foreach (var branch in orExpr.Operands)
            {
                if (TryExtractAllowedDomain(branch, variable, sharedDataset, kind, out var branchAllowed))
                {
                    combined = combined.Intersect(branchAllowed);
                    constrained = true;
                }
            }

            if (constrained)
            {
                allowed = combined;
                return true;
            }
        }

        allowed = default;
        return false;
    }

    private static bool TryEvaluateKleene(
        RuleExpression expr,
        string variable,
        string? sharedDataset,
        DomainKind kind,
        out (ValueDomain TrueSet, ValueDomain FalseSet) result)
    {
        var universe = ValueDomain.Full(kind);
        switch (expr)
        {
            case ComparisonExpression comp when TryNormalizeComparison(comp, variable, sharedDataset, out var op, out var lit):
                var trueSet = EvaluateComparisonTrueSet(op, lit, kind);
                result = (trueSet, universe.Except(trueSet));
                return true;

            case FunctionExpression { Function: RuleFunction.IsEmpty } fn
                when fn.Arguments.Count == 1
                     && fn.Arguments[0] is VariableExpression ve1
                     && string.Equals(NormalizeVariableName(ve1.Name, sharedDataset), variable, StringComparison.Ordinal):
                var emptyTrue = ValueDomain.EmptyOrNull(kind);
                result = (emptyTrue, universe.Except(emptyTrue));
                return true;

            case FunctionExpression { Function: RuleFunction.IsNotEmpty } fn
                when fn.Arguments.Count == 1
                     && fn.Arguments[0] is VariableExpression ve2
                     && string.Equals(NormalizeVariableName(ve2.Name, sharedDataset), variable, StringComparison.Ordinal):
                var notEmptyTrue = universe.Except(ValueDomain.EmptyOrNull(kind));
                result = (notEmptyTrue, ValueDomain.EmptyOrNull(kind));
                return true;

            case FunctionExpression { Function: RuleFunction.In or RuleFunction.NotIn } fn
                when fn.Arguments.Count >= 2
                     && fn.Arguments[0] is VariableExpression ve3
                     && string.Equals(NormalizeVariableName(ve3.Name, sharedDataset), variable, StringComparison.Ordinal)
                     && fn.Arguments.Skip(1).All(a => a is LiteralExpression):
                var inTrue = ValueDomain.Empty(kind);
                foreach (var litArg in fn.Arguments.Skip(1).Cast<LiteralExpression>())
                {
                    inTrue = inTrue.Union(EvaluateComparisonTrueSet(ComparisonOperator.Equal, litArg.Value, kind));
                }

                result = fn.Function == RuleFunction.In
                    ? (inTrue, universe.Except(inTrue))
                    : (universe.Except(inTrue), inTrue);
                return true;

            case NotExpression notExpr when TryEvaluateKleene(notExpr.Operand, variable, sharedDataset, kind, out var inner):
                result = (inner.FalseSet, inner.TrueSet);
                return true;

            case LogicalExpression { Operator: LogicalOperator.And } andExpr:
                var andTrue = universe;
                var andFalse = ValueDomain.Empty(kind);
                foreach (var operand in andExpr.Operands)
                {
                    if (!TryEvaluateKleene(operand, variable, sharedDataset, kind, out var sub))
                    {
                        result = default;
                        return false;
                    }

                    andTrue = andTrue.Intersect(sub.TrueSet);
                    andFalse = andFalse.Union(sub.FalseSet);
                }

                result = (andTrue, andFalse);
                return true;

            case LogicalExpression { Operator: LogicalOperator.Or } orExpr:
                var orTrue = ValueDomain.Empty(kind);
                var orFalse = universe;
                foreach (var operand in orExpr.Operands)
                {
                    if (!TryEvaluateKleene(operand, variable, sharedDataset, kind, out var sub))
                    {
                        result = default;
                        return false;
                    }

                    orTrue = orTrue.Union(sub.TrueSet);
                    orFalse = orFalse.Intersect(sub.FalseSet);
                }

                result = (orTrue, orFalse);
                return true;

            default:
                result = default;
                return false;
        }
    }

    private static bool TryNormalizeComparison(
        ComparisonExpression comp,
        string variable,
        string? sharedDataset,
        out ComparisonOperator op,
        out RuleValue literal)
    {
        if (comp.Left is VariableExpression leftVar
            && string.Equals(NormalizeVariableName(leftVar.Name, sharedDataset), variable, StringComparison.Ordinal)
            && comp.Right is LiteralExpression rightLit)
        {
            op = comp.Operator;
            literal = rightLit.Value;
            return true;
        }

        if (comp.Left is LiteralExpression leftLit
            && comp.Right is VariableExpression rightVar
            && string.Equals(NormalizeVariableName(rightVar.Name, sharedDataset), variable, StringComparison.Ordinal))
        {
            op = FlipComparison(comp.Operator);
            literal = leftLit.Value;
            return true;
        }

        op = default;
        literal = default;
        return false;
    }

    private static ComparisonOperator FlipComparison(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Less => ComparisonOperator.Greater,
        ComparisonOperator.LessOrEqual => ComparisonOperator.GreaterOrEqual,
        ComparisonOperator.Greater => ComparisonOperator.Less,
        ComparisonOperator.GreaterOrEqual => ComparisonOperator.LessOrEqual,
        _ => op,
    };

    private static ValueDomain EvaluateComparisonTrueSet(ComparisonOperator op, RuleValue lit, DomainKind kind)
    {
        var universe = ValueDomain.Full(kind);
        if (lit.IsNull)
        {
            return op switch
            {
                ComparisonOperator.Equal => ValueDomain.NullOnly(kind),
                ComparisonOperator.NotEqual => universe.Except(ValueDomain.NullOnly(kind)),
                _ => ValueDomain.Empty(kind),
            };
        }

        return kind switch
        {
            DomainKind.Number when lit.TryGetNumber(out var number) => op switch
            {
                ComparisonOperator.Equal => ValueDomain.FromNumbers([NumberPoint(number)]),
                ComparisonOperator.NotEqual => new ValueDomain(
                    DomainKind.Number,
                    AllowsNull: true,
                    AllowsTrue: false,
                    AllowsFalse: false,
                    AllowsBlankText: false,
                    [NumberUpper(number, inclusive: false), NumberLower(number, inclusive: false)],
                    []),
                ComparisonOperator.Less => ValueDomain.FromNumbers([NumberUpper(number, inclusive: false)]),
                ComparisonOperator.LessOrEqual => ValueDomain.FromNumbers([NumberUpper(number, inclusive: true)]),
                ComparisonOperator.Greater => ValueDomain.FromNumbers([NumberLower(number, inclusive: false)]),
                _ => ValueDomain.FromNumbers([NumberLower(number, inclusive: true)]),
            },
            DomainKind.Boolean when lit.TryGetBoolean(out var boolean) => op switch
            {
                ComparisonOperator.Equal => new ValueDomain(DomainKind.Boolean, false, boolean, !boolean, false, [], []),
                ComparisonOperator.NotEqual => new ValueDomain(DomainKind.Boolean, true, !boolean, boolean, false, [], []),
                _ => ValueDomain.Empty(DomainKind.Boolean),
            },
            _ => EvaluateTextComparisonTrueSet(op, lit.ToString()),
        };
    }

    private static ValueDomain EvaluateTextComparisonTrueSet(ComparisonOperator op, string text)
    {
        var isBlank = string.IsNullOrWhiteSpace(text);
        return op switch
        {
            ComparisonOperator.Equal => new ValueDomain(
                DomainKind.Text,
                AllowsNull: false,
                AllowsTrue: false,
                AllowsFalse: false,
                AllowsBlankText: isBlank && text.Length == 0,
                [],
                [TextPoint(text)]),
            ComparisonOperator.NotEqual => ValueDomain.Full(DomainKind.Text)
                .Except(EvaluateTextComparisonTrueSet(ComparisonOperator.Equal, text)),
            ComparisonOperator.Less => ValueDomain.FromTexts(
                allowsBlankText: string.CompareOrdinal(string.Empty, text) < 0,
                [TextUpper(text, inclusive: false)]),
            ComparisonOperator.LessOrEqual => ValueDomain.FromTexts(
                allowsBlankText: string.CompareOrdinal(string.Empty, text) <= 0,
                [TextUpper(text, inclusive: true)]),
            ComparisonOperator.Greater => ValueDomain.FromTexts(
                allowsBlankText: false,
                [TextLower(text, inclusive: false)]),
            _ => ValueDomain.FromTexts(
                allowsBlankText: text.Length == 0,
                [TextLower(text, inclusive: true)]),
        };
    }

    private static OrderedInterval<decimal> NumberPoint(decimal value) =>
        new(IntervalBound<decimal>.Of(value), true, IntervalBound<decimal>.Of(value), true);

    private static OrderedInterval<decimal> NumberLower(decimal min, bool inclusive) =>
        new(IntervalBound<decimal>.Of(min), inclusive, IntervalBound<decimal>.Unbounded, false);

    private static OrderedInterval<decimal> NumberUpper(decimal max, bool inclusive) =>
        new(IntervalBound<decimal>.Unbounded, false, IntervalBound<decimal>.Of(max), inclusive);

    private static OrderedInterval<string> TextPoint(string value) =>
        new(IntervalBound<string>.Of(value), true, IntervalBound<string>.Of(value), true);

    private static OrderedInterval<string> TextLower(string min, bool inclusive) =>
        new(IntervalBound<string>.Of(min), inclusive, IntervalBound<string>.Unbounded, false);

    private static OrderedInterval<string> TextUpper(string max, bool inclusive) =>
        new(IntervalBound<string>.Unbounded, false, IntervalBound<string>.Of(max), inclusive);

    private static bool DomainsConflict(ValueDomain first, ValueDomain second)
    {
        var intersected = first.Intersect(second);
        if (first.HasPopulatedValue && second.HasPopulatedValue)
        {
            return !intersected.HasPopulatedValue;
        }

        return intersected.IsCompletelyEmpty;
    }

    private enum DomainKind
    {
        Number,
        Boolean,
        Text,
    }

    private readonly record struct IntervalBound<T>(bool HasValue, T Value)
    {
        public static IntervalBound<T> Unbounded => new(false, default!);

        public static IntervalBound<T> Of(T value) => new(true, value);
    }

    private readonly record struct OrderedInterval<T>(
        IntervalBound<T> Min,
        bool MinInclusive,
        IntervalBound<T> Max,
        bool MaxInclusive);

    private readonly record struct ValueDomain(
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
            DomainKind.Text => new(DomainKind.Text, true, false, false, true, [], [TextPoint(string.Empty)]),
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
                remainingTexts = IntervalOperations.Subtract(remainingTexts, [TextPoint(string.Empty)], StringComparer.Ordinal, IsDegenerateTextInterval);
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

    private static class IntervalOperations
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
}
