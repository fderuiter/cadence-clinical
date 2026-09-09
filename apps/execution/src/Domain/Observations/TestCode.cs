using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Observations;

/// <summary>
/// Value object representing a standardized CDISC SDTM short test code (e.g. SYSBP, DIABP, GLUC, ALT).
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public readonly record struct TestCode
{
    public string Value { get; }

    [JsonConstructor]
    public TestCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Test code cannot be empty or whitespace.", nameof(value));
        }

        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 100)
        {
            throw new ArgumentException("Test code cannot exceed 100 characters.", nameof(value));
        }

        Value = normalized;
    }

    public override string ToString() => Value;
}
