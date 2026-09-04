using System;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Domain subject code (legacy <c>subject_id</c> field) — distinct from the surrogate <see cref="SubjectId"/> PK.
/// @req:PRD-SUB-001
/// </summary>
public readonly record struct SubjectCode
{
    public string Value { get; }

    public SubjectCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Subject code cannot be empty or whitespace.", nameof(value));
        }

        if (value.Length > 255)
        {
            throw new ArgumentException("Subject code cannot exceed 255 characters.", nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;
}
