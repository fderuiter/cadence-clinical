using System;

namespace Cadence.Contracts;

/// <summary>
/// Domain event published when a participant achieves a compliance streak milestone or study milestone.
/// Written to the transactional outbox table for downstream notification dispatch and wallet stipend credit.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
/// <param name="SubjectId">Subject identifier.</param>
/// <param name="StudyId">Study identifier.</param>
/// <param name="MilestoneType">Classification of milestone achieved (e.g., STREAK_3_DAY, STREAK_7_DAY, WEEK_4_COMPLETED).</param>
/// <param name="StreakCount">Current streak count achieved.</param>
/// <param name="BonusAmount">Stipend bonus amount awarded for milestone completion.</param>
/// <param name="AchievedAt">Timestamp when the milestone was achieved.</param>
/// <param name="Message">Optional custom milestone celebration message.</param>
public sealed record ParticipantStreakMilestoneAchieved(
    string SubjectId,
    string StudyId,
    string MilestoneType,
    int StreakCount,
    decimal BonusAmount,
    DateTimeOffset AchievedAt,
    string? Message = null
);
