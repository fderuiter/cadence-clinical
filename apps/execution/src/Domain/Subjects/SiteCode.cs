using System;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Domain site code (legacy <c>site_id</c> field). Nullable-by-design at call sites — unlike
/// <see cref="SubjectCode"/>/<see cref="StudyCode"/>, absence is a legal domain state (a subject
/// may have no site yet), so this is validated only when a non-null instance is constructed. An
/// explicitly-empty string is rejected rather than silently treated as "absent".
/// @req:PRD-SUB-001
/// </summary>
public readonly record struct SiteCode
{
    public string Value { get; }

    public SiteCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Site code cannot be empty or whitespace when provided.", nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;
}
