using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.Core.Domain.Sla;

/// <summary>
/// SLA clock alert status level.
/// @req:PRD-SYS-053
/// </summary>
public enum AlertLevel
{
    NORMAL,
    WARNING,
    CRITICAL,
    OVERDUE
}

/// <summary>
/// Configuration for business hours and holiday calendar calculations.
/// @req:PRD-SYS-053
/// </summary>
public record BusinessHoursSchedule(
    bool Is24x7 = true,
    int WorkStartHour = 9,
    int WorkEndHour = 17,
    IReadOnlyList<int>? WorkDays = null,
    IReadOnlyList<string>? Holidays = null
)
{
    public IReadOnlyList<int> WorkDays { get; init; } = WorkDays ?? new List<int> { 0, 1, 2, 3, 4 }; // Monday-Friday
    public IReadOnlyList<string> Holidays { get; init; } = Holidays ?? Array.Empty<string>();
}

/// <summary>
/// Actions triggered upon reaching an escalation threshold tier.
/// @req:PRD-SYS-053
/// </summary>
public record EscalationAction(
    string? TargetPriority = null,
    IReadOnlyList<string>? NotifyRoles = null,
    string? WebhookChannel = null,
    string? MessageTemplate = null
)
{
    public IReadOnlyList<string> NotifyRoles { get; init; } = NotifyRoles ?? Array.Empty<string>();
}

/// <summary>
/// An escalation tier triggered at a specific elapsed percentage threshold.
/// @req:PRD-SYS-053
/// </summary>
public record EscalationTier(
    string TierId,
    string Name,
    EscalationAction Action,
    double BreachThresholdPct = 100.0,
    string? Description = null
);

/// <summary>
/// Live computed status of a statutory or service SLA countdown clock.
/// @req:PRD-SYS-053
/// </summary>
public record CountdownClock(
    string ClockStartDate,
    string ClockDueDate,
    string EffectiveDueDate,
    double TotalAllocatedHours,
    double TotalPausedSeconds,
    double ElapsedHours,
    double ElapsedPercentage,
    double RemainingHours,
    bool IsOverdue,
    AlertLevel AlertLevel
);

/// <summary>
/// Result of evaluating an active clock against policy escalation tiers.
/// @req:PRD-SYS-053
/// </summary>
public record EscalationEvaluationResult(
    AlertLevel CurrentAlertLevel,
    IReadOnlyList<EscalationTier> TriggeredTiers,
    string? RecommendedPriority,
    IReadOnlyList<string> NotificationRoles,
    string AuditSummary
);

/// <summary>
/// Declarative specification for domain-wide SLA commitments and escalations.
/// @req:PRD-SYS-053
/// </summary>
public record SlaPolicy
{
    public string PolicyId { get; init; }
    public string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, double> BaseHoursByPriority { get; init; }
    public IReadOnlyDictionary<string, double> CategoryMultipliers { get; init; }
    public IReadOnlyList<string> PausedStates { get; init; }
    public double WarningThresholdPct { get; init; }
    public double CriticalThresholdPct { get; init; }
    public BusinessHoursSchedule Schedule { get; init; }
    public IReadOnlyList<EscalationTier> EscalationTiers { get; init; }
    public int VersionIndex { get; init; }
    public string? Sha256Checksum { get; init; }
    public bool IsActive { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; }
    public string ReasonForChange { get; init; }

    public SlaPolicy(
        string policyId,
        string name,
        string? description = null,
        IReadOnlyDictionary<string, double>? baseHoursByPriority = null,
        IReadOnlyDictionary<string, double>? categoryMultipliers = null,
        IReadOnlyList<string>? pausedStates = null,
        double warningThresholdPct = 75.0,
        double criticalThresholdPct = 90.0,
        BusinessHoursSchedule? schedule = null,
        IReadOnlyList<EscalationTier>? escalationTiers = null,
        int versionIndex = 1,
        string? sha256Checksum = null,
        bool isActive = true,
        bool isDeleted = false,
        DateTime? createdAt = null,
        string createdBy = "system",
        string reasonForChange = "Initial SLA policy authoring")
    {
        PolicyId = policyId;
        Name = name;
        Description = description;
        BaseHoursByPriority = baseHoursByPriority ?? new Dictionary<string, double>
        {
            { "CRITICAL", 4.0 },
            { "HIGH", 24.0 },
            { "MEDIUM", 72.0 },
            { "LOW", 168.0 }
        };
        CategoryMultipliers = categoryMultipliers ?? new Dictionary<string, double>();
        PausedStates = pausedStates ?? Array.Empty<string>();
        WarningThresholdPct = warningThresholdPct;
        CriticalThresholdPct = criticalThresholdPct;
        Schedule = schedule ?? new BusinessHoursSchedule();
        EscalationTiers = escalationTiers ?? Array.Empty<EscalationTier>();
        VersionIndex = versionIndex;
        IsActive = isActive;
        IsDeleted = isDeleted;
        CreatedAt = createdAt ?? DateTime.UtcNow;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;

        if (!string.IsNullOrEmpty(sha256Checksum))
        {
            Sha256Checksum = sha256Checksum;
        }
        else
        {
            var payload = new
            {
                base_hours_by_priority = BaseHoursByPriority,
                category_multipliers = CategoryMultipliers,
                critical_threshold_pct = CriticalThresholdPct,
                name = Name,
                paused_states = PausedStates.OrderBy(s => s).ToList(),
                policy_id = PolicyId,
                version_index = VersionIndex,
                warning_threshold_pct = WarningThresholdPct
            };
            string canonical = JsonSerializer.Serialize(payload);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            Sha256Checksum = Convert.ToHexStringLower(hash);
        }
    }
}
