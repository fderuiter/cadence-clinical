namespace Cadence.Apps.Execution.Infrastructure.Consumers;

/// <summary>
/// MassTransit message contract for the legacy Redis pub/sub "consent completed" notification
/// (<c>apps/execution/workers/consent_subscriber.py</c>'s message payload). Consumed by
/// <see cref="EConsentCompletedConsumer"/> to auto-enroll a
/// <see cref="Domain.Subjects.ClinicalSubject"/> and independently backfill its consent-cache row
/// (SCN-009, <c>02_technical_design.md</c> §5.2). <see cref="SiteId"/> is the only optional field —
/// mirroring <see cref="Domain.Subjects.SiteCode"/>'s own nullable-by-design contract, a subject may
/// legally have no assigned site yet. <see cref="SubjectId"/>/<see cref="StudyId"/> empty or
/// whitespace-only marks the whole event malformed; the consumer drops it silently rather than
/// throwing or dead-lettering, matching the legacy subscriber's warning-log-only behavior exactly.
/// Replaces <c>apps/execution/workers/consent_subscriber.py</c>'s untyped Redis message payload with a
/// strongly-typed record. <see cref="SignedAt"/> is when the participant signed: the consent's valid time.
/// An event without it is rejected to the error queue rather than recorded with the time it was processed
/// (#4769).
/// @req:PRD-SUB-001
/// @req:PRD-SUB-007
/// </summary>
public sealed record EConsentCompletedEvent(
    string SubjectId,
    string StudyId,
    string? SiteId,
    string VersionTag,
    int VersionIndex,
    DateTimeOffset? SignedAt = null,
    string? TenantCode = null);
