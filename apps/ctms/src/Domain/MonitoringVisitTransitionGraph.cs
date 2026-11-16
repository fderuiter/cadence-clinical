using System.Collections.Generic;

using Cadence.Core.Domain;

namespace Cadence.Apps.Ctms.Domain;

/// <summary>
/// State machine transition graph for clinical trial monitoring visit aggregate roots.
/// Enforces deterministic state transitions and rejects unauthorized state jumps.
/// </summary>
public static class MonitoringVisitTransitionGraph
{
    private static readonly IReadOnlyDictionary<MonitoringVisitStatus, IReadOnlySet<MonitoringVisitStatus>> Allowed =
        new Dictionary<MonitoringVisitStatus, IReadOnlySet<MonitoringVisitStatus>>
        {
            [MonitoringVisitStatus.Scheduled] = new HashSet<MonitoringVisitStatus>
            {
                MonitoringVisitStatus.InProgress,
                MonitoringVisitStatus.Completed,
                MonitoringVisitStatus.Cancelled
            },
            [MonitoringVisitStatus.InProgress] = new HashSet<MonitoringVisitStatus>
            {
                MonitoringVisitStatus.Completed,
                MonitoringVisitStatus.Cancelled
            },
            [MonitoringVisitStatus.Completed] = new HashSet<MonitoringVisitStatus>
            {
                MonitoringVisitStatus.ReportDrafted,
                MonitoringVisitStatus.Approved,
                MonitoringVisitStatus.Signed_Off
            },
            [MonitoringVisitStatus.ReportDrafted] = new HashSet<MonitoringVisitStatus>
            {
                MonitoringVisitStatus.Approved,
                MonitoringVisitStatus.Signed_Off
            },
            [MonitoringVisitStatus.Approved] = new HashSet<MonitoringVisitStatus>(),
            [MonitoringVisitStatus.Signed_Off] = new HashSet<MonitoringVisitStatus>(),
            [MonitoringVisitStatus.Cancelled] = new HashSet<MonitoringVisitStatus>()
        };

    /// <summary>
    /// Checks whether the specified status is terminal (cannot transition to any other status).
    /// </summary>
    public static bool IsTerminal(MonitoringVisitStatus status) =>
        status is MonitoringVisitStatus.Approved or MonitoringVisitStatus.Signed_Off or MonitoringVisitStatus.Cancelled;

    /// <summary>
    /// Validates whether a transition from <paramref name="current"/> to <paramref name="target"/> is legal.
    /// Order of checks:
    /// 1. Same-state is an idempotent no-op.
    /// 2. If current is null, only Scheduled is allowed.
    /// 3. If current is Approved, approved monitoring visits are strictly immutable and terminal.
    /// 4. Allowed transitions lookup.
    /// </summary>
    public static Result<MonitoringVisitStatus> Guard(MonitoringVisitStatus? current, MonitoringVisitStatus target)
    {
        if (current == target)
        {
            return target;
        }

        if (current is null)
        {
            return target == MonitoringVisitStatus.Scheduled
                ? target
                : CtmsErrors.InvalidVisitStatusTransition("None", target.ToString());
        }

        if (current is MonitoringVisitStatus.Approved or MonitoringVisitStatus.Signed_Off or MonitoringVisitStatus.Cancelled)
        {
            return CtmsErrors.InvalidVisitStatusTransition(current.Value, target);
        }

        return Allowed[current.Value].Contains(target)
            ? target
            : CtmsErrors.InvalidVisitStatusTransition(current.Value, target);
    }
}
