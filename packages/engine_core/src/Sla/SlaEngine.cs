using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cadence.Core.Domain.Sla;

/// <summary>
/// Computes SLA due dates, countdown clocks and escalations under one <see cref="SlaPolicy"/>. Port of
/// <c>packages/engine_core/sla/engine.py</c> (#5048); no I/O and no clock: the caller passes the instant a clock is
/// read at, from its <c>IClock</c> (K-TIME-3), as the other engines here do.
/// </summary>
public sealed class SlaEngine
{
    /// <summary>The hours allowed for a priority the policy does not list.</summary>
    public const double FallbackHours = 72.0;

    /// <summary>Creates an engine for <paramref name="policy"/>.</summary>
    /// <exception cref="ArgumentException">The policy's business-hours schedule can never run.</exception>
    public SlaEngine(SlaPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ValidateSchedule(policy.Schedule);
        Policy = policy;
    }

    /// <summary>The policy the engine applies.</summary>
    public SlaPolicy Policy { get; }

    /// <summary>Whether <paramref name="state"/> pauses the clock.</summary>
    public bool IsStatePaused(string state) => Policy.PausedStates.Contains(state, StringComparer.Ordinal);

    /// <summary>
    /// When an item started at <paramref name="startDate"/> is due: <paramref name="customHours"/> when positive,
    /// otherwise the policy's hours for <paramref name="priority"/>, times the <paramref name="category"/> multiplier,
    /// counted around the clock or only within the schedule's business hours.
    /// </summary>
    public DateTimeOffset ComputeSlaDueDate(DateTimeOffset startDate, string priority, string? category = null, double? customHours = null)
    {
        ArgumentNullException.ThrowIfNull(priority);
        DateTimeOffset start = startDate.ToUniversalTime();

        double hours = customHours is > 0
            ? customHours.Value
            : Policy.BaseHoursByPriority.GetValueOrDefault(priority.ToUpperInvariant(), FallbackHours);
        if (!string.IsNullOrEmpty(category))
        {
            hours *= Policy.CategoryMultipliers.GetValueOrDefault(category, 1.0);
        }

        return Policy.Schedule.Is24x7 ? start + TimeSpan.FromHours(hours) : AddBusinessHours(start, hours, Policy.Schedule);
    }

    /// <summary>
    /// The clock of an item started at <paramref name="startDate"/> and due at <paramref name="dueDate"/>, as of
    /// <paramref name="asOf"/>. Paused time moves the effective due date and does not count as elapsed. A due date at
    /// or before the start counts as one hour allowed.
    /// </summary>
    public CountdownClock ComputeCountdownClock(
        DateTimeOffset startDate,
        DateTimeOffset dueDate,
        DateTimeOffset asOf,
        double totalPausedSeconds = 0.0)
    {
        DateTimeOffset start = startDate.ToUniversalTime();
        DateTimeOffset due = dueDate.ToUniversalTime();
        DateTimeOffset now = asOf.ToUniversalTime();
        double paused = Math.Max(0.0, totalPausedSeconds);
        DateTimeOffset effectiveDue = due + TimeSpan.FromSeconds(paused);

        double allocatedSeconds = (due - start).TotalSeconds;
        if (allocatedSeconds <= 0) allocatedSeconds = 3600.0;

        double netElapsedSeconds = Math.Max(0.0, (now - start).TotalSeconds - paused);
        double elapsedPct = Round2(netElapsedSeconds / allocatedSeconds * 100.0);
        bool overdue = now > effectiveDue;

        SlaAlertLevel level;
        if (overdue || elapsedPct >= 100.0) level = SlaAlertLevel.Overdue;
        else if (elapsedPct >= Policy.CriticalThresholdPct) level = SlaAlertLevel.Critical;
        else if (elapsedPct >= Policy.WarningThresholdPct) level = SlaAlertLevel.Warning;
        else level = SlaAlertLevel.Normal;

        return new CountdownClock(
            start,
            due,
            effectiveDue,
            Round2(allocatedSeconds / 3600.0),
            totalPausedSeconds,
            Round2(netElapsedSeconds / 3600.0),
            elapsedPct,
            Math.Max(0.0, Round2((effectiveDue - now).TotalSeconds / 3600.0)),
            overdue,
            level);
    }

    /// <summary>
    /// The escalation tiers <paramref name="clock"/> has reached (lowest threshold first), every role they notify
    /// (sorted, once each), and the target priority of the last reached tier that sets one.
    /// </summary>
    public EscalationEvaluationResult EvaluateEscalations(CountdownClock clock, string? currentPriority = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var triggered = Policy.EscalationTiers
            .Where(t => clock.ElapsedPercentage >= t.BreachThresholdPct)
            .OrderBy(t => t.BreachThresholdPct)
            .ToList();
        string? recommended = triggered.LastOrDefault(t => !string.IsNullOrEmpty(t.Action.TargetPriority))?.Action.TargetPriority;
        var roles = triggered.SelectMany(t => t.Action.NotifyRoles).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        // Worded as the Python reference worded it, including its list and float formatting.
        string names = "[" + string.Join(", ", triggered.Select(t => PythonRepr(t.Name))) + "]";
        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"SLA status: {AlertLevelName(clock.AlertLevel)} ({PythonJson.Float(clock.ElapsedPercentage)}% elapsed). "
            + $"Triggered tiers: {names}. Recommended priority: {recommended ?? currentPriority ?? "None"}.");

        return new EscalationEvaluationResult(clock.AlertLevel, triggered, recommended, roles, summary);
    }

    /// <summary>The level as the Python reference named it (<c>NORMAL</c>, <c>WARNING</c>, <c>CRITICAL</c>, <c>OVERDUE</c>).</summary>
    public static string AlertLevelName(SlaAlertLevel level) => level.ToString().ToUpperInvariant();

    // Counts whole hours and minutes of each business day, as the Python did: the seconds of a start time are carried
    // but not counted against the day's remaining time.
    private static DateTimeOffset AddBusinessHours(DateTimeOffset start, double hours, BusinessHoursSchedule schedule)
    {
        var workDays = schedule.WorkDays.ToHashSet();
        var holidays = schedule.Holidays.ToHashSet();
        DateTimeOffset current = start;
        double left = hours;

        while (left > 0)
        {
            if (workDays.Contains(current.DayOfWeek) && !holidays.Contains(DateOnly.FromDateTime(current.UtcDateTime)))
            {
                if (current.Hour < schedule.WorkStartHour)
                {
                    current = AtHour(current, schedule.WorkStartHour);
                }

                if (current.Hour < schedule.WorkEndHour)
                {
                    double availableToday = schedule.WorkEndHour - current.Hour - current.Minute / 60.0;
                    if (left <= availableToday)
                    {
                        return current + TimeSpan.FromHours(left);
                    }

                    left -= availableToday;
                }
            }

            current = AtHour(current.AddDays(1), schedule.WorkStartHour);
        }

        return current;
    }

    private static DateTimeOffset AtHour(DateTimeOffset day, int hour) => new(day.Year, day.Month, day.Day, hour, 0, 0, TimeSpan.Zero);

    // Python's round(x, 2) rounds half to even; so does Math.Round's default.
    private static double Round2(double value) => Math.Round(value, 2);

    private static string PythonRepr(string value) =>
        value.Contains('\'', StringComparison.Ordinal) && !value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value}\""
            : $"'{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}'";

    // The Python loop never ended when no day could be a work day; the port refuses such a schedule instead.
    private static void ValidateSchedule(BusinessHoursSchedule schedule)
    {
        if (schedule.Is24x7) return;
        if (schedule.WorkDays.Count == 0)
        {
            throw new ArgumentException("A business-hours schedule needs at least one work day.", nameof(schedule));
        }

        if (schedule.WorkStartHour is < 0 or > 23 || schedule.WorkEndHour <= schedule.WorkStartHour || schedule.WorkEndHour > 24)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Business hours must run from an hour 0 to 23 to a later hour up to 24; got {schedule.WorkStartHour} to {schedule.WorkEndHour}."),
                nameof(schedule));
        }
    }
}
