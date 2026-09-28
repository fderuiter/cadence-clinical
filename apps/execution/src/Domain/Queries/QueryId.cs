using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Queries;

/// <summary>
/// Strongly-typed identifier for the <see cref="ClinicalQuery"/> aggregate root.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public readonly record struct QueryId
{
    public Guid Value { get; }

    [JsonConstructor]
    public QueryId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Query ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static QueryId New() => new(Guid.NewGuid());

    public static QueryId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(Guid.Parse(value));
    }

    public static bool TryParse(string? value, out QueryId id)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = new QueryId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
