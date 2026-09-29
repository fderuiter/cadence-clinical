using System;

namespace Cadence.Kernel.Domain.Bitemporal;

/// <summary>
/// Immutable bitemporal period capturing Valid Time (clinical truth in the trial) and Recorded Time (assertion knowledge in the system).
/// Conforms to ADR-2246 and PRD-SYS-053.
/// </summary>
public readonly record struct TemporalPeriod
{
    public DateTimeOffset ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }
    public DateTimeOffset RecordedFrom { get; init; }
    public DateTimeOffset? RecordedTo { get; init; }

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

        ValidFrom = validFrom;
        ValidTo = validTo;
        RecordedFrom = recordedFrom;
        RecordedTo = recordedTo;
    }

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
    public bool IsEffectiveAsOf(DateTimeOffset validAt, DateTimeOffset knownAt)
    {
        bool validMatches = validAt >= ValidFrom && (!ValidTo.HasValue || validAt < ValidTo.Value);
        bool recordedMatches = knownAt >= RecordedFrom && (!RecordedTo.HasValue || knownAt < RecordedTo.Value);
        return validMatches && recordedMatches;
    }

    /// <summary>
    /// Checks whether this record represents currently active and un-superseded clinical truth.
    /// </summary>
    public bool IsCurrent(DateTimeOffset now) =>
        !RecordedTo.HasValue && (!ValidTo.HasValue || ValidTo.Value > now);

    /// <summary>
    /// Closes the recorded interval when this fact is superseded or corrected.
    /// </summary>
    public TemporalPeriod Supersede(DateTimeOffset supersededAt) =>
        this with { RecordedTo = supersededAt };

    /// <summary>
    /// Checks whether this period's valid time overlaps with another temporal period's valid time.
    /// </summary>
    public bool OverlapsValid(TemporalPeriod other)
    {
        DateTimeOffset startA = ValidFrom;
        DateTimeOffset endA = ValidTo ?? DateTimeOffset.MaxValue;
        DateTimeOffset startB = other.ValidFrom;
        DateTimeOffset endB = other.ValidTo ?? DateTimeOffset.MaxValue;

        return startA < endB && startB < endA;
    }

    /// <summary>
    /// Checks whether this period's recorded transaction time overlaps with another temporal period's recorded time.
    /// </summary>
    public bool OverlapsRecorded(TemporalPeriod other)
    {
        DateTimeOffset startA = RecordedFrom;
        DateTimeOffset endA = RecordedTo ?? DateTimeOffset.MaxValue;
        DateTimeOffset startB = other.RecordedFrom;
        DateTimeOffset endB = other.RecordedTo ?? DateTimeOffset.MaxValue;

        return startA < endB && startB < endA;
    }
}
