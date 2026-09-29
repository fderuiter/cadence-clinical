using System;

using Cadence.Kernel;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Surrogate primary key for the <c>ClinicalSubject</c> aggregate, mapping to canonical Kernel ParticipantId.
/// @req:PRD-SUB-001
/// </summary>
public readonly record struct SubjectId(Guid Value)
{
    public static SubjectId New() => new(Guid.NewGuid());

    public ParticipantId AsParticipantId() => new(Value);

    public override string ToString() => Value.ToString();
}
