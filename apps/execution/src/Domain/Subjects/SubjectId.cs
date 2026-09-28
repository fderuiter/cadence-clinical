using System;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Surrogate primary key for the <c>ClinicalSubject</c> aggregate.
/// @req:PRD-SUB-001
/// </summary>
public readonly record struct SubjectId(Guid Value)
{
    public static SubjectId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
