using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Gamification;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Execution.Application.Gamification;

/// <summary>
/// Gamification service evaluating participant compliance adherence streaks, milestone thresholds,
/// and IRB anti-coercion stipend bonus payouts via MassTransit transactional outbox.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public sealed class GamificationService(
    ExecutionDbContext dbContext,
    IClock clock,
    IPublishEndpoint publishEndpoint,
    ILogger<GamificationService>? logger = null) : IGamificationService
{
    public async Task<GamificationProgressResponse> GetProgressAsync(string subjectId, string studyId, CancellationToken cancellationToken = default)
    {
        var progress = await dbContext.GamificationProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SubjectId == subjectId && x.StudyId == studyId, cancellationToken);

        if (progress == null)
        {
            return new GamificationProgressResponse(
                subjectId,
                studyId,
                CurrentStreak: 0,
                LongestStreak: 0,
                TotalSubmissions: 0,
                OnTimeSubmissions: 0,
                LastSubmissionDate: null,
                MilestonesAchieved: Array.Empty<string>(),
                TotalBonusEarned: 0m,
                UpdatedAt: clock.UtcNow
            );
        }

        return MapToResponse(progress);
    }

    public async Task<GamificationProgressResponse> ProcessSubmissionAsync(ProcessGamificationSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        var progress = await dbContext.GamificationProgresses
            .FirstOrDefaultAsync(x => x.SubjectId == request.SubjectId && x.StudyId == request.StudyId, cancellationToken);

        if (progress == null)
        {
            progress = new ParticipantGamificationProgress
            {
                SubjectId = request.SubjectId,
                StudyId = request.StudyId,
                CurrentStreak = 0,
                LongestStreak = 0,
                TotalSubmissions = 0,
                OnTimeSubmissions = 0,
                LastSubmissionDate = null,
                MilestonesAchievedJson = "[]",
                TotalBonusEarned = 0m,
                UpdatedAt = clock.UtcNow,
                VersionIndex = 1
            };
            dbContext.GamificationProgresses.Add(progress);
        }

        // Calculate streak based on client original DeviceTimestamp (Guardrail 3)
        var submissionDate = request.DeviceTimestamp.Date;

        if (!progress.LastSubmissionDate.HasValue)
        {
            progress.CurrentStreak = 1;
        }
        else
        {
            var lastDate = progress.LastSubmissionDate.Value.Date;
            var diffDays = (submissionDate - lastDate).Days;

            if (diffDays == 1)
            {
                progress.CurrentStreak += 1;
            }
            else if (diffDays > 1)
            {
                progress.CurrentStreak = 1;
            }
            // If diffDays == 0 (same day submission), streak remains active without incrementing
        }

        progress.LongestStreak = Math.Max(progress.LongestStreak, progress.CurrentStreak);
        progress.TotalSubmissions += 1;
        if (request.IsOnTime)
        {
            progress.OnTimeSubmissions += 1;
        }

        if (!progress.LastSubmissionDate.HasValue || submissionDate > progress.LastSubmissionDate.Value.Date)
        {
            progress.LastSubmissionDate = request.DeviceTimestamp;
        }

        progress.UpdatedAt = clock.UtcNow;

        // Evaluate milestone completion and award non-coercive stipend bonuses ($25 per major milestone, Guardrail 1)
        var achievedList = progress.GetMilestonesList();
        var newlyAchieved = new List<(string MilestoneType, decimal Bonus, string Message)>();

        if (progress.CurrentStreak >= 3 && !achievedList.Contains("STREAK_3_DAY"))
        {
            achievedList.Add("STREAK_3_DAY");
            newlyAchieved.Add(("STREAK_3_DAY", 0m, "🔥 3-Day Compliance Streak Achieved! Keep up the great work."));
        }

        if (progress.CurrentStreak >= 7 && !achievedList.Contains("STREAK_7_DAY"))
        {
            achievedList.Add("STREAK_7_DAY");
            newlyAchieved.Add(("STREAK_7_DAY", 25.00m, "🏆 7-Day Compliance Streak Milestone! You've earned a $25 stipend bonus."));
        }

        if (progress.CurrentStreak >= 14 && !achievedList.Contains("STREAK_14_DAY"))
        {
            achievedList.Add("STREAK_14_DAY");
            newlyAchieved.Add(("STREAK_14_DAY", 25.00m, "⭐ 14-Day Compliance Streak Champion! You've earned a $25 stipend bonus."));
        }

        if (progress.CurrentStreak >= 30 && !achievedList.Contains("STREAK_30_DAY"))
        {
            achievedList.Add("STREAK_30_DAY");
            newlyAchieved.Add(("STREAK_30_DAY", 25.00m, "👑 30-Day Legend Streak! You've earned a $25 stipend bonus."));
        }

        if (progress.TotalSubmissions >= 28 && !achievedList.Contains("WEEK_4_COMPLETED"))
        {
            achievedList.Add("WEEK_4_COMPLETED");
            newlyAchieved.Add(("WEEK_4_COMPLETED", 25.00m, "🎉 Week 4 Trial Protocol Milestone Completed! You've earned a $25 stipend bonus."));
        }

        if (newlyAchieved.Count > 0)
        {
            progress.SetMilestonesList(achievedList);
            foreach (var milestone in newlyAchieved)
            {
                progress.TotalBonusEarned += milestone.Bonus;

                logger?.LogInformation(
                    "Subject {SubjectId} in Study {StudyId} achieved gamification milestone {MilestoneType} with bonus {BonusAmount}",
                    progress.SubjectId, progress.StudyId, milestone.MilestoneType, milestone.Bonus);

                // Publish milestone domain event to transactional outbox
                var milestoneEvent = new ParticipantStreakMilestoneAchieved(
                    progress.SubjectId,
                    progress.StudyId,
                    milestone.MilestoneType,
                    progress.CurrentStreak,
                    milestone.Bonus,
                    clock.UtcNow,
                    milestone.Message
                );
                await publishEndpoint.Publish(milestoneEvent, cancellationToken);

                // Publish engagement nudge event to transactional outbox
                var nudgeEvent = new EngagementNudgeTriggered(
                    progress.SubjectId,
                    progress.StudyId,
                    "MILESTONE_CELEBRATION",
                    milestone.Message,
                    progress.CurrentStreak,
                    clock.UtcNow
                );
                await publishEndpoint.Publish(nudgeEvent, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(progress);
    }

    private static GamificationProgressResponse MapToResponse(ParticipantGamificationProgress progress)
    {
        return new GamificationProgressResponse(
            progress.SubjectId,
            progress.StudyId,
            progress.CurrentStreak,
            progress.LongestStreak,
            progress.TotalSubmissions,
            progress.OnTimeSubmissions,
            progress.LastSubmissionDate,
            progress.GetMilestonesList(),
            progress.TotalBonusEarned,
            progress.UpdatedAt
        );
    }
}
