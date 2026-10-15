using System;

namespace Cadence.Contracts;

/// <summary>
/// Domain event published when an automated engagement nudge is triggered for a participant.
/// Written to the transactional outbox table for multi-channel nudge rendering and dispatch.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
/// <param name="SubjectId">Subject identifier.</param>
/// <param name="StudyId">Study identifier.</param>
/// <param name="NudgeType">Type of nudge (e.g., STREAK_CELEBRATION, ADHERENCE_NUDGE, MILESTONE_CONGRATS).</param>
/// <param name="Message">Personalized nudge message content.</param>
/// <param name="CurrentStreak">Current active adherence streak.</param>
/// <param name="TriggeredAt">Timestamp when the nudge was triggered.</param>
public sealed record EngagementNudgeTriggered(
    string SubjectId,
    string StudyId,
    string NudgeType,
    string Message,
    int CurrentStreak,
    DateTimeOffset TriggeredAt
);
