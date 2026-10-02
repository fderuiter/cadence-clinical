using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Core.Domain.Sla;

/// <summary>How close an SLA clock is to its due date.</summary>
public enum SlaAlertLevel
{
    /// <summary>Below the warning threshold.</summary>
    Normal,

    /// <summary>At or past the warning threshold.</summary>
    Warning,

    /// <summary>At or past the critical threshold.</summary>
    Critical,

    /// <summary>Past the due date, or all of the allocated time is used.</summary>
    Overdue,
}

/// <summary>
/// When an SLA clock runs. Around the clock by default; otherwise from <see cref="WorkStartHour"/> to
/// <see cref="WorkEndHour"/> (UTC) on <see cref="WorkDays"/>, except <see cref="Holidays"/>.
/// </summary>
public sealed record BusinessHoursSchedule
{
    /// <summary>Monday to Friday.</summary>
    public static readonly IReadOnlyList<DayOfWeek> Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    /// <summary>Whether the clock runs around the clock, ignoring the rest of the schedule.</summary>
    public bool Is24x7 { get; init; } = true;

    /// <summary>The UTC hour work starts (0 to 23).</summary>
    public int WorkStartHour { get; init; } = 9;

    /// <summary>The UTC hour work ends (after <see cref="WorkStartHour"/>, at most 24).</summary>
    public int WorkEndHour { get; init; } = 17;

    /// <summary>The days the clock runs.</summary>
    public IReadOnlyList<DayOfWeek> WorkDays { get; init; } = Weekdays;

    /// <summary>Dates the clock does not run.</summary>
    public IReadOnlyList<DateOnly> Holidays { get; init; } = [];
}

/// <summary>What happens when an escalation tier is reached.</summary>
public sealed record EscalationAction(
    string? TargetPriority = null,
    IReadOnlyList<string>? NotifyRoles = null,
    string? WebhookChannel = null,
    string? MessageTemplate = null)
{
    /// <summary>The roles to notify (none when not set).</summary>
    public IReadOnlyList<string> NotifyRoles { get; init; } = NotifyRoles ?? [];
}

/// <summary>An escalation reached when a clock has used <paramref name="BreachThresholdPct"/> percent of its time.</summary>
public sealed record EscalationTier(
    string TierId,
    string Name,
    EscalationAction Action,
    double BreachThresholdPct = 100.0,
    string? Description = null);

/// <summary>An SLA clock's state at one instant.</summary>
public sealed record CountdownClock(
    DateTimeOffset ClockStartDate,
    DateTimeOffset ClockDueDate,
    DateTimeOffset EffectiveDueDate,
    double TotalAllocatedHours,
    double TotalPausedSeconds,
    double ElapsedHours,
    double ElapsedPercentage,
    double RemainingHours,
    bool IsOverdue,
    SlaAlertLevel AlertLevel);

/// <summary>The escalation tiers a clock has reached, who to notify, and the priority the last of them asks for.</summary>
public sealed record EscalationEvaluationResult(
    SlaAlertLevel CurrentAlertLevel,
    IReadOnlyList<EscalationTier> TriggeredTiers,
    string? RecommendedPriority,
    IReadOnlyList<string> NotificationRoles,
    string AuditSummary);

/// <summary>
/// An SLA commitment: hours allowed per priority, multipliers per category, the states that pause the clock, alert
/// thresholds, the schedule and the escalation tiers. The Python model's audit fields (<c>created_at</c>,
/// <c>created_by</c>, <c>reason_for_change</c>, <c>is_deleted</c>) belong to whatever stores a policy, through the
/// GxP audit interceptor, and are not part of this record.
/// </summary>
public sealed record SlaPolicy
{
    /// <summary>The hours each priority allows when a policy does not set its own.</summary>
    public static readonly IReadOnlyDictionary<string, double> DefaultBaseHours = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["CRITICAL"] = 4.0,
        ["HIGH"] = 24.0,
        ["MEDIUM"] = 72.0,
        ["LOW"] = 168.0,
    };

    /// <summary>The policy's identifier.</summary>
    public required string PolicyId { get; init; }

    /// <summary>The policy's name.</summary>
    public required string Name { get; init; }

    /// <summary>What the policy is for.</summary>
    public string? Description { get; init; }

    /// <summary>Hours allowed per priority (upper case); a priority not listed gets 72.</summary>
    public IReadOnlyDictionary<string, double> BaseHoursByPriority { get; init; } = DefaultBaseHours;

    /// <summary>Multipliers applied to the hours allowed, per category.</summary>
    public IReadOnlyDictionary<string, double> CategoryMultipliers { get; init; } = new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>The states in which the clock is paused.</summary>
    public IReadOnlyList<string> PausedStates { get; init; } = [];

    /// <summary>The elapsed percentage at which a clock turns <see cref="SlaAlertLevel.Warning"/>.</summary>
    public double WarningThresholdPct { get; init; } = 75.0;

    /// <summary>The elapsed percentage at which a clock turns <see cref="SlaAlertLevel.Critical"/>.</summary>
    public double CriticalThresholdPct { get; init; } = 90.0;

    /// <summary>When the clock runs.</summary>
    public BusinessHoursSchedule Schedule { get; init; } = new();

    /// <summary>The escalation tiers.</summary>
    public IReadOnlyList<EscalationTier> EscalationTiers { get; init; } = [];

    /// <summary>The policy's version, from 1.</summary>
    public int VersionIndex { get; init; } = 1;

    /// <summary>Whether the policy is in force.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// SHA-256 (lower-case hex) of the policy's identity, hours, multipliers, paused states, thresholds and version,
    /// written as Python's <c>json.dumps(payload, sort_keys=True)</c> writes it, so it equals the checksum the Python
    /// model sealed a policy with. It changes whenever one of those fields does; it is not a signature.
    /// </summary>
    public string ComputeChecksum()
    {
        var json = new StringBuilder("{");
        json.Append("\"base_hours_by_priority\": ").Append(PythonJson.Object(BaseHoursByPriority));
        json.Append(", \"category_multipliers\": ").Append(PythonJson.Object(CategoryMultipliers));
        json.Append(", \"critical_threshold_pct\": ").Append(PythonJson.Float(CriticalThresholdPct));
        json.Append(", \"name\": ").Append(PythonJson.String(Name));
        json.Append(", \"paused_states\": [")
            .AppendJoin(", ", PausedStates.Order(StringComparer.Ordinal).Select(PythonJson.String))
            .Append(']');
        json.Append(", \"policy_id\": ").Append(PythonJson.String(PolicyId));
        json.Append(", \"version_index\": ").Append(VersionIndex.ToString(CultureInfo.InvariantCulture));
        json.Append(", \"warning_threshold_pct\": ").Append(PythonJson.Float(WarningThresholdPct));
        json.Append('}');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json.ToString())));
    }
}
