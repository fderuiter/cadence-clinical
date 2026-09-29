using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cadence.Core.Domain.Sla;

/// <summary>
/// Deterministic Event-Driven SLA Escalation Engine.
/// Calculates SLA target completion datetimes, live countdown clock status,
/// frozen/paused state intervals, and multi-tier operational escalation triggers.
/// @req:PRD-SYS-053
/// </summary>
public class SlaTrackingEngine
{
    private readonly SlaPolicy _policy;

    public SlaPolicy Policy => _policy;

    public SlaTrackingEngine(SlaPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public static DateTime ParseTimestamp(object value)
    {
        if (value is DateTime dt)
        {
            return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        }
        if (value is DateOnly d)
        {
            return new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
        }
        if (value is string s)
        {
            string cleaned = s.Trim();
            if (cleaned.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^1] + "+00:00";
            }
            if (DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
            if (DateOnly.TryParse(cleaned, CultureInfo.InvariantCulture, out var parsedDate))
            {
                return new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day, 0, 0, 0, DateTimeKind.Utc);
            }
            throw new ArgumentException($"Unable to parse timestamp string: '{s}'");
        }
        throw new ArgumentException($"Unsupported timestamp type: {value?.GetType()}");
    }

    public bool IsStatePaused(string state) =>
        !string.IsNullOrEmpty(state) && _policy.PausedStates.Contains(state);

    public DateTime ComputeSlaDueDate(
        object startDate,
        string priority,
        string? category = null,
        double? customHours = null)
    {
        DateTime startDt = ParseTimestamp(startDate);

        double allocatedHours;
        if (customHours.HasValue && customHours.Value > 0)
        {
            allocatedHours = customHours.Value;
        }
        else
        {
            string prioKey = (priority ?? "MEDIUM").ToUpperInvariant();
            if (!_policy.BaseHoursByPriority.TryGetValue(prioKey, out allocatedHours))
            {
                allocatedHours = 72.0;
            }
        }

        if (!string.IsNullOrEmpty(category) && _policy.CategoryMultipliers.TryGetValue(category, out double multiplier))
        {
            allocatedHours *= multiplier;
        }

        // 24x7 vs Business hours
        if (_policy.Schedule.Is24x7)
        {
            return startDt.AddHours(allocatedHours);
        }

        // Business hours schedule calculation
        DateTime curr = startDt;
        double hoursLeft = allocatedHours;
        var sched = _policy.Schedule;
        var workDaysSet = sched.WorkDays.ToHashSet();
        var holidaysSet = sched.Holidays.ToHashSet();

        while (hoursLeft > 0)
        {
            string dateStr = curr.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            int dayOfWeek = ((int)curr.DayOfWeek + 6) % 7; // Convert Sunday=0 to Monday=0..Sunday=6
            bool isWorkday = workDaysSet.Contains(dayOfWeek) && !holidaysSet.Contains(dateStr);

            if (isWorkday)
            {
                if (curr.Hour < sched.WorkStartHour)
                {
                    curr = new DateTime(curr.Year, curr.Month, curr.Day, sched.WorkStartHour, 0, 0, DateTimeKind.Utc);
                }

                if (curr.Hour < sched.WorkEndHour)
                {
                    double availableToday = (sched.WorkEndHour - curr.Hour) - (curr.Minute / 60.0);
                    if (hoursLeft <= availableToday)
                    {
                        curr = curr.AddHours(hoursLeft);
                        break;
                    }
                    hoursLeft -= availableToday;
                }
            }

            // Advance to start of next day
            curr = curr.Date.AddDays(1).AddHours(sched.WorkStartHour);
            curr = DateTime.SpecifyKind(curr, DateTimeKind.Utc);
        }

        return curr;
    }

    public CountdownClock ComputeCountdownClock(
        object startDate,
        object dueDate,
        double totalPausedSeconds = 0.0,
        DateTime? asOf = null)
    {
        DateTime startDt = ParseTimestamp(startDate);
        DateTime baseDueDt = ParseTimestamp(dueDate);
        DateTime nowDt = asOf.HasValue ? ParseTimestamp(asOf.Value) : DateTime.UtcNow;

        DateTime effectiveDueDt = baseDueDt.AddSeconds(Math.Max(0.0, totalPausedSeconds));

        double totalAllocatedSeconds = (baseDueDt - startDt).TotalSeconds;
        if (totalAllocatedSeconds <= 0)
        {
            totalAllocatedSeconds = 3600.0; // Fallback 1 hour
        }

        double totalAllocatedHours = Math.Round(totalAllocatedSeconds / 3600.0, 2);

        double rawElapsedSeconds = (nowDt - startDt).TotalSeconds;
        double netElapsedSeconds = Math.Max(0.0, rawElapsedSeconds - Math.Max(0.0, totalPausedSeconds));
        double elapsedHours = Math.Round(netElapsedSeconds / 3600.0, 2);

        double elapsedPct = Math.Round((netElapsedSeconds / totalAllocatedSeconds) * 100.0, 2);
        double remainingSeconds = (effectiveDueDt - nowDt).TotalSeconds;
        double remainingHours = Math.Max(0.0, Math.Round(remainingSeconds / 3600.0, 2));
        bool isOverdue = nowDt > effectiveDueDt;

        AlertLevel alertLevel;
        if (isOverdue || elapsedPct >= 100.0)
        {
            alertLevel = AlertLevel.OVERDUE;
        }
        else if (elapsedPct >= _policy.CriticalThresholdPct)
        {
            alertLevel = AlertLevel.CRITICAL;
        }
        else if (elapsedPct >= _policy.WarningThresholdPct)
        {
            alertLevel = AlertLevel.WARNING;
        }
        else
        {
            alertLevel = AlertLevel.NORMAL;
        }

        return new CountdownClock(
            ClockStartDate: startDt.ToString("o"),
            ClockDueDate: baseDueDt.ToString("o"),
            EffectiveDueDate: effectiveDueDt.ToString("o"),
            TotalAllocatedHours: totalAllocatedHours,
            TotalPausedSeconds: totalPausedSeconds,
            ElapsedHours: elapsedHours,
            ElapsedPercentage: elapsedPct,
            RemainingHours: remainingHours,
            IsOverdue: isOverdue,
            AlertLevel: alertLevel
        );
    }

    public EscalationEvaluationResult EvaluateEscalations(
        CountdownClock clock,
        string? currentPriority = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var triggered = new List<EscalationTier>();
        var recipients = new HashSet<string>();
        string? highestRecPriority = null;

        var sortedTiers = _policy.EscalationTiers
            .OrderBy(t => t.BreachThresholdPct)
            .ToList();

        foreach (var tier in sortedTiers)
        {
            if (clock.ElapsedPercentage >= tier.BreachThresholdPct)
            {
                triggered.Add(tier);
                foreach (var role in tier.Action.NotifyRoles)
                {
                    recipients.Add(role);
                }
                if (!string.IsNullOrEmpty(tier.Action.TargetPriority))
                {
                    highestRecPriority = tier.Action.TargetPriority;
                }
            }
        }

        string triggeredNames = string.Join(", ", triggered.Select(t => t.Name));
        string summary = $"SLA status: {clock.AlertLevel} ({clock.ElapsedPercentage}% elapsed). Triggered tiers: [{triggeredNames}]. Recommended priority: {highestRecPriority ?? currentPriority}.";

        return new EscalationEvaluationResult(
            CurrentAlertLevel: clock.AlertLevel,
            TriggeredTiers: triggered,
            RecommendedPriority: highestRecPriority,
            NotificationRoles: recipients.OrderBy(r => r).ToList(),
            AuditSummary: summary
        );
    }
}

/// <summary>
/// Alias class matching SlaEngine.
/// </summary>
public class SlaEngine : SlaTrackingEngine
{
    public SlaEngine(SlaPolicy policy) : base(policy) { }
}
