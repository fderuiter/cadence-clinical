using System;
using System.Collections.Generic;

using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Indicators;

/// <summary>The Study Pulse area an indicator reports under (Canvas §2.8, PX-9). The areas are fixed; the indicators in them are data.</summary>
public enum IndicatorArea
{
    /// <summary>Screening, enrollment and retention against plan.</summary>
    Enrollment,

    /// <summary>Data completeness and cleanliness.</summary>
    Data,

    /// <summary>Adverse events, SAEs and their follow-up.</summary>
    Safety,

    /// <summary>Site activation and performance.</summary>
    Sites,

    /// <summary>Validation findings, deviations, signals and risk indicators.</summary>
    Quality,

    /// <summary>Investigational product supply.</summary>
    Supply,

    /// <summary>Trial master file completeness.</summary>
    Tmf,

    /// <summary>Submission readiness.</summary>
    Submission,
}

/// <summary>What an indicator is for (spec §10.4).</summary>
public enum IndicatorKind
{
    /// <summary>A Study Pulse metric; with a threshold, it is also an attention rule.</summary>
    Pulse,

    /// <summary>A key risk indicator of a risk-based monitoring plan.</summary>
    Kri,

    /// <summary>A quality tolerance limit (ICH E6(R3)).</summary>
    Qtl,
}

/// <summary>How an indicator's value is computed from the records it counts.</summary>
public enum IndicatorMeasure
{
    /// <summary>The number of records counted.</summary>
    Count,

    /// <summary>The records counted as a percentage of the population, rounded to two decimals; none when the population is empty.</summary>
    Percent,
}

/// <summary>Which side of the threshold needs attention.</summary>
public enum ThresholdDirection
{
    /// <summary>A value above the limit needs attention.</summary>
    Above,

    /// <summary>A value below the limit needs attention.</summary>
    Below,
}

/// <summary>
/// When an indicator needs attention: its value is strictly above or below <see cref="Limit"/>. A breach is reported
/// with <see cref="Severity"/>; ranking breaches in Needs Attention is the application's (ADR-2488 §4.4).
/// </summary>
/// <param name="Direction">Which side of the limit needs attention.</param>
/// <param name="Limit">The limit, in the indicator's measure.</param>
/// <param name="Severity">How serious a breach is.</param>
public sealed record IndicatorThreshold(ThresholdDirection Direction, decimal Limit, ValidationSeverity Severity)
{
    /// <summary>True when <paramref name="value"/> is on the attention side of the limit; false when there is no value.</summary>
    public bool IsBreachedBy(decimal? value) => value is { } v && (Direction == ThresholdDirection.Above ? v > Limit : v < Limit);
}

/// <summary>
/// One versioned indicator (spec §10.4, K-QBD-1): a rule that counts the records of a population for which
/// <see cref="Counts"/> is true, a measure over that count, and an optional threshold. It is evaluated by the same
/// engine as every other rule (<see cref="Evaluation.IRuleEvaluator"/>), so the same indicator version over the same
/// records always gives the same value (K-RULE-4).
/// </summary>
/// <param name="Id">Stable across versions; letters, digits, <c>.</c>, <c>_</c> and <c>-</c>.</param>
/// <param name="Version">Raised whenever the indicator's meaning changes.</param>
/// <param name="Title">What the value is, as the Pulse shows it.</param>
/// <param name="Area">The Pulse area it reports under.</param>
/// <param name="Kind">Pulse metric, KRI or QTL.</param>
/// <param name="Population">The record type counted, as its source names it (for example <c>enrollments</c>).</param>
/// <param name="Counts">True for each record that is counted.</param>
/// <param name="Measure">Count, or percentage of the population.</param>
/// <param name="Threshold">When the value needs attention; null for a metric that is only shown.</param>
/// <param name="Explanation">What the indicator measures and why, in the author's words.</param>
/// <param name="Effective">The as-of dates it applies to.</param>
public sealed record Indicator(
    string Id,
    int Version,
    string Title,
    IndicatorArea Area,
    IndicatorKind Kind,
    string Population,
    RuleExpression Counts,
    IndicatorMeasure Measure,
    IndicatorThreshold? Threshold,
    string Explanation,
    EffectivePeriod Effective);

/// <summary>
/// A set of indicators released together: the content of a Metadata Registry library item of kind
/// <see cref="LibraryItemKind.IndicatorSet"/>, so its version and release Attestation are the library item's and a
/// study uses the versions its standards profile pins (ADR-2488).
/// </summary>
/// <param name="Authority">Whose indicators they are: the sponsor's, or one study's.</param>
/// <param name="Title">The set's name.</param>
/// <param name="Indicators">The indicators, in the order the Pulse lists them within an area.</param>
public sealed record IndicatorSet(RuleAuthority Authority, string Title, IReadOnlyList<Indicator> Indicators);

/// <summary>A released indicator set together with the library item version it was read from.</summary>
/// <param name="Id">The library item version.</param>
/// <param name="Key">The library item's key.</param>
/// <param name="Version">The library item's version.</param>
/// <param name="ContentHash">The released content's hash.</param>
/// <param name="Status">The library item's governance status.</param>
/// <param name="Set">The indicators.</param>
public sealed record LoadedIndicatorSet(LibraryItemId Id, string Key, int Version, string ContentHash, GovernanceStatus Status, IndicatorSet Set);

/// <summary>Where and when indicators are evaluated. The caller supplies <see cref="AsOf"/>; evaluation never reads the clock.</summary>
/// <param name="Scope">The object the records were read within: a study, study site or participant.</param>
/// <param name="AsOf">The date the records describe; indicators not effective on it are not evaluated.</param>
public sealed record IndicatorContext(NodeRef Scope, DateOnly AsOf);

/// <summary>One record of a population: the object it stands for, and its variables.</summary>
/// <param name="Node">The object, so a value drills down to what produced it (UX-7).</param>
/// <param name="Values">The variables the indicator rules read, by name.</param>
public sealed record IndicatorRecord(NodeRef Node, IReadOnlyDictionary<string, RuleValue> Values);

/// <summary>Which indicator version, in which released set, produced a result.</summary>
/// <param name="SetId">The set's library item version.</param>
/// <param name="SetKey">The set's key.</param>
/// <param name="SetVersion">The set's version.</param>
/// <param name="SetContentHash">The set's content hash.</param>
/// <param name="IndicatorId">The indicator's id.</param>
/// <param name="IndicatorVersion">The indicator's version.</param>
public sealed record IndicatorVersionRef(
    LibraryItemId SetId,
    string SetKey,
    int SetVersion,
    string SetContentHash,
    string IndicatorId,
    int IndicatorVersion)
{
    /// <summary>The explanation producer name, <c>quality.indicator/&lt;id&gt;@&lt;version&gt;</c> (K-EXP-3).</summary>
    public string Producer => $"quality.indicator/{IndicatorId}@{IndicatorVersion}";
}

/// <summary>
/// An indicator's value at one scope and as-of, with what produced it (ADR-2488). The value is reproducible from
/// <see cref="Indicator"/>, the records and the as-of (<see cref="InputHash"/>), and drills down to
/// <see cref="Contributors"/>.
/// </summary>
/// <param name="Indicator">The indicator version and the released set it came from.</param>
/// <param name="Title">The indicator's title.</param>
/// <param name="Area">Its Pulse area.</param>
/// <param name="Kind">Pulse metric, KRI or QTL.</param>
/// <param name="Scope">Where it was evaluated.</param>
/// <param name="AsOf">When.</param>
/// <param name="Population">How many records of the population were read.</param>
/// <param name="Counted">How many of them were counted.</param>
/// <param name="Measure">How <see cref="Value"/> was computed.</param>
/// <param name="Value">The value; null for a percentage of an empty population.</param>
/// <param name="Threshold">The indicator's threshold, if it has one.</param>
/// <param name="Breached">True when the value is on the attention side of the threshold.</param>
/// <param name="Contributors">The counted objects, sorted, up to <see cref="IndicatorEvaluator.MaxContributors"/>.</param>
/// <param name="InputHash">Hash of the indicator version, the set's content hash, the scope, the as-of and the records.</param>
/// <param name="ResultHash">Hash of this result without itself and its explanation.</param>
/// <param name="Explanation">Why the value is what it is, from the same evaluation (K-EXP-1).</param>
public sealed record IndicatorResult(
    IndicatorVersionRef Indicator,
    string Title,
    IndicatorArea Area,
    IndicatorKind Kind,
    NodeRef Scope,
    DateOnly AsOf,
    int Population,
    int Counted,
    IndicatorMeasure Measure,
    decimal? Value,
    IndicatorThreshold? Threshold,
    bool Breached,
    IReadOnlyList<NodeRef> Contributors,
    string InputHash,
    string ResultHash,
    Explanation Explanation);
