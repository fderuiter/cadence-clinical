using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Observations;

/// <summary>
/// Value object representing a CDISC SDTM observation domain code (e.g. VS, LB, AE, PE).
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public readonly record struct DomainCode
{
    public string Value { get; }

    [JsonConstructor]
    public DomainCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Domain code cannot be empty or whitespace.", nameof(value));
        }

        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 50)
        {
            throw new ArgumentException("Domain code cannot exceed 50 characters.", nameof(value));
        }

        Value = normalized;
    }

    public override string ToString() => Value;
}
