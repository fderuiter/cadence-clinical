using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Domain.Forms;

/// <summary>
/// Strongly-typed identifier for the <see cref="FormSubmission"/> aggregate root.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-002
/// </summary>
public readonly record struct FormSubmissionId
{
    public Guid Value { get; }

    [JsonConstructor]
    public FormSubmissionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Form submission ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static FormSubmissionId New() => new(Guid.NewGuid());

    public static FormSubmissionId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(Guid.Parse(value));
    }

    public static bool TryParse(string? value, out FormSubmissionId id)
    {
        if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
        {
            id = new FormSubmissionId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
