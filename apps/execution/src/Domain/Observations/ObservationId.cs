using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Observations;

/// <summary>
/// Strongly-typed identifier for the <see cref="ClinicalObservation"/> aggregate root.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public readonly record struct ObservationId
{
    public Guid Value { get; }

    [JsonConstructor]
    public ObservationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Observation ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static ObservationId New() => new(Guid.NewGuid());

    public static ObservationId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(Guid.Parse(value));
    }

    public static bool TryParse(string? value, out ObservationId id)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = new ObservationId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
