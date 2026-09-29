using System;

namespace Cadence.Kernel.Domain.Bitemporal;

/// <summary>
/// Immutable bitemporal period: <see cref="Valid"/> time (when it was true in the trial) and <see cref="Recorded"/>
/// time (when Cadence asserted it, until superseded). Persisted as the <c>valid_period</c> and
/// <c>recorded_period</c> <c>tstzrange</c> columns (ADR-2246, spec §4.1, K-TIME-1/K-TIME-2).
/// @req:PRD-SYS-053
/// </summary>
public readonly record struct TemporalPeriod
{
    public TemporalPeriod(TimeRange valid, TimeRange recorded)
    {
        Valid = valid;
        Recorded = recorded;
    }

    public TemporalPeriod(
        DateTimeOffset validFrom,
        DateTimeOffset? validTo,
        DateTimeOffset recordedFrom,
        DateTimeOffset? recordedTo)
    {
        if (validTo.HasValue && validTo.Value < validFrom)
        {
            throw new ArgumentException("ValidTo cannot precede ValidFrom", nameof(validTo));
        }

        if (recordedTo.HasValue && recordedTo.Value < recordedFrom)
        {
            throw new ArgumentException("RecordedTo cannot precede RecordedFrom", nameof(recordedTo));
        }

        Valid = new TimeRange(validFrom, validTo);
        Recorded = new TimeRange(recordedFrom, recordedTo);
    }

    /// <summary>When the fact was true in the trial world (<c>valid_period</c>).</summary>
    public TimeRange Valid { get; init; }

    /// <summary>When Cadence asserted the fact, closed when it is superseded (<c>recorded_period</c>).</summary>
    public TimeRange Recorded { get; init; }

    public DateTimeOffset ValidFrom => Valid.Start;

    public DateTimeOffset? ValidTo => Valid.End;

    public DateTimeOffset RecordedFrom => Recorded.Start;

    public DateTimeOffset? RecordedTo => Recorded.End;

    /// <summary>
    /// Creates an open-ended current observation recorded at the current instant.
    /// </summary>
    public static TemporalPeriod CreateCurrent(DateTimeOffset validFrom, DateTimeOffset recordedAt) =>
        new(validFrom, null, recordedAt, null);

    /// <summary>
    /// Creates a bounded valid interval recorded at the specified transaction time.
    /// </summary>
    public static TemporalPeriod CreateBounded(DateTimeOffset validFrom, DateTimeOffset validTo, DateTimeOffset recordedAt) =>
        new(validFrom, validTo, recordedAt, null);

    /// <summary>
    /// Checks whether this interval was clinically valid and known by the system as of the specified dual coordinates.
    /// </summary>
    public bool IsEffectiveAsOf(DateTimeOffset validAt, DateTimeOffset knownAt) =>
        Valid.Contains(validAt) && Recorded.Contains(knownAt);

    /// <summary>
    /// Checks whether this record represents currently active and un-superseded clinical truth.
    /// </summary>
    public bool IsCurrent(DateTimeOffset now) =>
        Recorded.IsOpen && (Valid.IsOpen || Valid.End!.Value > now);

    /// <summary>
    /// Closes the recorded interval when this fact is superseded or corrected. This is the only change K-TIME-1
    /// allows on a persisted bitemporal row.
    /// </summary>
    public TemporalPeriod Supersede(DateTimeOffset supersededAt) =>
        this with { Recorded = new TimeRange(Recorded.Start, supersededAt) };

    /// <summary>
    /// Checks whether this period's valid time overlaps with another temporal period's valid time.
    /// </summary>
    public bool OverlapsValid(TemporalPeriod other) => Valid.Overlaps(other.Valid);

    /// <summary>
    /// Checks whether this period's recorded transaction time overlaps with another temporal period's recorded time.
    /// </summary>
    public bool OverlapsRecorded(TemporalPeriod other) => Recorded.Overlaps(other.Recorded);
}
