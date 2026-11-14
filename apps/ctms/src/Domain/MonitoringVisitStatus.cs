using System.Collections.Generic;
using System.Linq;

namespace Cadence.Apps.Ctms.Domain;

/// <summary>
/// Explicit lifecycle states for a clinical trial monitoring visit aggregate root.
/// </summary>
public enum MonitoringVisitStatus
{
    Scheduled,
    InProgress,
    Completed,
    ReportDrafted,
    Approved,
    Cancelled
}

/// <summary>
/// Codec for converting between MonitoringVisitStatus enum and canonical UPPER_SNAKE_CASE strings.
/// </summary>
public static class MonitoringVisitStatusCodec
{
    private static readonly IReadOnlyDictionary<string, MonitoringVisitStatus> ByCode = new Dictionary<string, MonitoringVisitStatus>
    {
        ["SCHEDULED"] = MonitoringVisitStatus.Scheduled,
        ["IN_PROGRESS"] = MonitoringVisitStatus.InProgress,
        ["COMPLETED"] = MonitoringVisitStatus.Completed,
        ["REPORT_DRAFTED"] = MonitoringVisitStatus.ReportDrafted,
        ["SIGNED_OFF"] = MonitoringVisitStatus.Approved,
        ["APPROVED"] = MonitoringVisitStatus.Approved,
        ["CANCELLED"] = MonitoringVisitStatus.Cancelled,
    };

    public static string ToCode(MonitoringVisitStatus state) =>
        ByCode.First(kv => kv.Value == state).Key;

    public static bool TryParse(string? raw, out MonitoringVisitStatus state)
    {
        state = default;
        if (raw is null) return false;
        string normalized = raw.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
        return ByCode.TryGetValue(normalized, out state);
    }

    public static MonitoringVisitStatus Parse(string? raw) =>
        TryParse(raw, out var state) ? state : MonitoringVisitStatus.Scheduled;
}
