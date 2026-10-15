using System;

namespace Cadence.Contracts;

/// <summary>
/// Domain event published when an ePRO questionnaire or diary submission is completed.
/// Written to the transactional outbox table for downstream gamification and adherence processing.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
/// <param name="SubjectId">Subject identifier.</param>
/// <param name="StudyId">Study identifier.</param>
/// <param name="SiteId">Optional site identifier.</param>
/// <param name="InstrumentId">Identifier of the completed instrument or diary form.</param>
/// <param name="SequenceNumber">Submission sequence number.</param>
/// <param name="DeviceTimestamp">Original device submission timestamp recorded client-side.</param>
/// <param name="SubmittedAt">Timestamp when the submission was processed by the service.</param>
/// <param name="IsOnTime">Indicates whether the questionnaire was submitted within its scheduled window.</param>
/// <param name="OccurredAt">Optional timestamp when the event occurred.</param>
public sealed record EProSubmissionCompleted(
    string SubjectId,
    string StudyId,
    string? SiteId,
    string InstrumentId,
    int SequenceNumber,
    DateTimeOffset DeviceTimestamp,
    DateTimeOffset SubmittedAt,
    bool IsOnTime,
    DateTimeOffset? OccurredAt = null
);
