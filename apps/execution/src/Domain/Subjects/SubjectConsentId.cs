using System;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Surrogate primary key for the <c>SubjectConsent</c> aggregate.
/// @req:PRD-SUB-001
/// </summary>
public readonly record struct SubjectConsentId(Guid Value)
{
    public static SubjectConsentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
