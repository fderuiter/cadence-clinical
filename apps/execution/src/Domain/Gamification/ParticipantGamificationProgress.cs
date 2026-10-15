using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Cadence.Apps.Execution.Domain.Gamification;

/// <summary>
/// Domain aggregate tracking a participant's adherence streak, completed trial milestones, and stipend bonus credits.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public sealed class ParticipantGamificationProgress
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string SubjectId { get; set; } = string.Empty;

    public string StudyId { get; set; } = string.Empty;

    public int CurrentStreak { get; set; }

    public int LongestStreak { get; set; }

    public int TotalSubmissions { get; set; }

    public int OnTimeSubmissions { get; set; }

    public DateTimeOffset? LastSubmissionDate { get; set; }

    public string MilestonesAchievedJson { get; set; } = "[]";

    public decimal TotalBonusEarned { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int VersionIndex { get; set; } = 1;

    public List<string> GetMilestonesList()
    {
        if (string.IsNullOrWhiteSpace(MilestonesAchievedJson)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(MilestonesAchievedJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public void SetMilestonesList(List<string> milestones)
    {
        MilestonesAchievedJson = JsonSerializer.Serialize(milestones);
    }
}
