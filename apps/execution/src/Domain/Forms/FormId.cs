using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Forms;

/// <summary>
/// Value object representing a protocol-defined eCRF form template identifier.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-002
/// </summary>
public readonly record struct FormId
{
    public string Value { get; }

    [JsonConstructor]
    public FormId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Form ID cannot be empty or whitespace.", nameof(value));
        }

        string normalized = value.Trim();
        if (normalized.Length > 255)
        {
            throw new ArgumentException("Form ID cannot exceed 255 characters.", nameof(value));
        }

        Value = normalized;
    }

    public override string ToString() => Value;
}
