using System;

namespace Cadence.Contracts;

/// <summary>
/// Domain event published when a Grade 3/4 laboratory toxicity event is detected or confirmed.
/// Written to the transactional outbox table for at-least-once downstream delivery.
/// @req:PRD-SYS-001
/// </summary>
public record Grade3ToxicityDetected(
    string ReviewId,
    string StudyId,
    string SubjectId,
    string? SiteId,
    string TestCode,
    string TestName,
    double LabValue,
    string Unit,
    int CtcaeGrade,
    string ToxicityTerm,
    bool IsHysLawWarning,
    string? QueryId,
    DateTimeOffset DetectedAt
);
