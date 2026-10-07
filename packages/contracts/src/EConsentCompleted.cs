using System;

namespace Cadence.Contracts;

/// <summary>
/// Domain event published when an informed consent form is completed or signed.
/// Written to the transactional outbox table for at-least-once downstream delivery.
/// @req:PRD-SYS-001
/// </summary>
/// <param name="SubjectId">Subject identifier.</param>
/// <param name="StudyId">Study identifier.</param>
/// <param name="SiteId">Optional site identifier.</param>
/// <param name="VersionTag">Consent form version tag.</param>
/// <param name="VersionIndex">Consent form version index.</param>
/// <param name="SignedAt">Optional timestamp when the participant signed the ICF.</param>
public sealed record EConsentCompleted(
    string SubjectId,
    string StudyId,
    string? SiteId,
    string VersionTag,
    int VersionIndex,
    DateTimeOffset? SignedAt = null
);
