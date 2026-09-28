using System;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Domain study code (legacy <c>study_id</c> field).
/// @req:PRD-SUB-001
/// </summary>
public readonly record struct StudyCode
{
    public string Value { get; }

    public StudyCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Study code cannot be empty or whitespace.", nameof(value));
        }

        if (value.Length > 255)
        {
            throw new ArgumentException("Study code cannot exceed 255 characters.", nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;
}
