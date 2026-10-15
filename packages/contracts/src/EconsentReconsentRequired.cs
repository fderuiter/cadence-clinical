using System;

namespace Cadence.Contracts;

/// <summary>
/// Domain event published when an informed consent form version or protocol amendment tagged for re-consent is published.
/// Written to the transactional outbox table for at-least-once downstream delivery.
/// @req:PRD-SYS-001
/// </summary>
/// <param name="StudyId">Study identifier.</param>
/// <param name="TemplateId">Consent template or amendment identifier.</param>
/// <param name="VersionIndex">Consent version index.</param>
/// <param name="ChangeSummary">Summary of changes requiring subject re-consent.</param>
/// <param name="Channels">Optional array of notification channels (e.g. SMS, Mobile Push, Email).</param>
/// <param name="ProtocolVersion">Optional protocol version string (e.g. v2.0).</param>
/// <param name="OccurredAt">Optional timestamp when the event occurred.</param>
public sealed record EconsentReconsentRequired(
    string StudyId,
    string TemplateId,
    int VersionIndex,
    string ChangeSummary,
    string[]? Channels = null,
    string? ProtocolVersion = null,
    DateTimeOffset? OccurredAt = null
);
