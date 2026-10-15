using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Gamification;

/// <summary>
/// Gamification progress DTO returned to client applications and subject portal.
/// </summary>
public sealed record GamificationProgressResponse(
    string SubjectId,
    string StudyId,
    int CurrentStreak,
    int LongestStreak,
    int TotalSubmissions,
    int OnTimeSubmissions,
    DateTimeOffset? LastSubmissionDate,
    IReadOnlyList<string> MilestonesAchieved,
    decimal TotalBonusEarned,
    DateTimeOffset UpdatedAt
);

/// <summary>
/// Request payload for processing ePRO submission gamification evaluation.
/// </summary>
public sealed record ProcessGamificationSubmissionRequest(
    string SubjectId,
    string StudyId,
    string InstrumentId,
    int SequenceNumber,
    DateTimeOffset DeviceTimestamp,
    bool IsOnTime,
    string? SiteId = null
);

/// <summary>
/// Application service contract for calculating participant adherence streaks,
/// evaluating milestone criteria, and triggering transactional outbox engagement events.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public interface IGamificationService
{
    /// <summary>
    /// Retrieves current gamification streak and milestone progress for a participant.
    /// </summary>
    Task<GamificationProgressResponse> GetProgressAsync(string subjectId, string studyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates ePRO submission event using client device timestamp and updates compliance streak and milestones.
    /// </summary>
    Task<GamificationProgressResponse> ProcessSubmissionAsync(ProcessGamificationSubmissionRequest request, CancellationToken cancellationToken = default);
}
