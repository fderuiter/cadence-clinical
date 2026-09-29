using System;

namespace Cadence.Kernel.Domain.Bitemporal;

/// <summary>
/// Immutable bitemporal period capturing Valid Time (clinical truth in the trial) and Recorded Time (assertion knowledge in the system).
/// Conforms to ADR-2246 and PRD-SYS-053.
/// </summary>
public readonly record struct TemporalPeriod(
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    DateTimeOffset RecordedFrom,
    DateTimeOffset? RecordedTo)
{
    /// <summary>
    /// Creates an open-ended current observation recorded at the current instant.
    /// </summary>
    public static TemporalPeriod CreateCurrent(DateTimeOffset validFrom, DateTimeOffset recordedAt) =>
        new(validFrom, null, recordedAt, null);

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
}
