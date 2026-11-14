using System.Collections.Generic;

using Cadence.Core.Domain;

namespace Cadence.Apps.Ctms.Domain;

/// <summary>
/// State machine transition graph for monitoring visit finding resolution statuses.
/// Enforces deterministic state transitions and rejects unauthorized state jumps.
/// </summary>
public static class FindingResolutionTransitionGraph
{
    private static readonly IReadOnlyDictionary<FindingResolutionStatus, IReadOnlySet<FindingResolutionStatus>> Allowed =
        new Dictionary<FindingResolutionStatus, IReadOnlySet<FindingResolutionStatus>>
        {
            [FindingResolutionStatus.Open] = new HashSet<FindingResolutionStatus>
            {
                FindingResolutionStatus.UnderReview,
                FindingResolutionStatus.Resolved,
                FindingResolutionStatus.Closed
            },
            [FindingResolutionStatus.UnderReview] = new HashSet<FindingResolutionStatus>
            {
                FindingResolutionStatus.Resolved,
                FindingResolutionStatus.Open,
                FindingResolutionStatus.Closed
            },
            [FindingResolutionStatus.Resolved] = new HashSet<FindingResolutionStatus>
            {
                FindingResolutionStatus.Closed,
                FindingResolutionStatus.UnderReview
            },
            [FindingResolutionStatus.Closed] = new HashSet<FindingResolutionStatus>()
        };

    /// <summary>
    /// Checks whether the specified status is terminal (cannot transition to any other status).
    /// </summary>
    public static bool IsTerminal(FindingResolutionStatus status) =>
        status is FindingResolutionStatus.Closed;

    /// <summary>
    /// Validates whether a transition from <paramref name="current"/> to <paramref name="target"/> is legal.
    /// </summary>
    public static Result<FindingResolutionStatus> Guard(FindingResolutionStatus? current, FindingResolutionStatus target)
    {
        if (current == target)
        {
            return target;
        }

        if (current is null)
        {
            return target == FindingResolutionStatus.Open
                ? target
                : CtmsErrors.InvalidFindingResolutionTransition(FindingResolutionStatus.Open, target);
        }

        return Allowed[current.Value].Contains(target)
            ? target
            : CtmsErrors.InvalidFindingResolutionTransition(current.Value, target);
    }
}
