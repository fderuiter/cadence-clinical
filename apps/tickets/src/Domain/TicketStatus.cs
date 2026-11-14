using System;
using System.Collections.Generic;

using Cadence.Core.Domain;

namespace Cadence.Apps.Tickets.Domain;

/// <summary>
/// Lifecycle statuses for operational and support tickets.
/// </summary>
public enum TicketStatus
{
    OPEN,
    IN_PROGRESS,
    WAITING_ON_SITE,
    WAITING_ON_SPONSOR,
    PENDING_REGULATORY_REVIEW,
    RESOLVED,
    CLOSED,
    REOPENED,
    CANCELLED
}

/// <summary>
/// Lifecycle transition graph and validation rules for support and operational tickets.
/// </summary>
public static class TicketTransitionGraph
{
    public static readonly IReadOnlySet<TicketStatus> TerminalStates = new HashSet<TicketStatus>
    {
        TicketStatus.CLOSED,
        TicketStatus.CANCELLED
    };

    private static readonly IReadOnlyDictionary<TicketStatus, IReadOnlySet<TicketStatus>> AllowedTransitions =
        new Dictionary<TicketStatus, IReadOnlySet<TicketStatus>>
        {
            [TicketStatus.OPEN] = new HashSet<TicketStatus>
            {
                TicketStatus.IN_PROGRESS,
                TicketStatus.WAITING_ON_SITE,
                TicketStatus.WAITING_ON_SPONSOR,
                TicketStatus.PENDING_REGULATORY_REVIEW,
                TicketStatus.RESOLVED,
                TicketStatus.CANCELLED,
                TicketStatus.CLOSED
            },
            [TicketStatus.IN_PROGRESS] = new HashSet<TicketStatus>
            {
                TicketStatus.RESOLVED,
                TicketStatus.CANCELLED,
                TicketStatus.OPEN,
                TicketStatus.WAITING_ON_SITE,
                TicketStatus.WAITING_ON_SPONSOR,
                TicketStatus.PENDING_REGULATORY_REVIEW
            },
            [TicketStatus.WAITING_ON_SITE] = new HashSet<TicketStatus>
            {
                TicketStatus.IN_PROGRESS,
                TicketStatus.OPEN,
                TicketStatus.WAITING_ON_SPONSOR,
                TicketStatus.RESOLVED,
                TicketStatus.CANCELLED
            },
            [TicketStatus.WAITING_ON_SPONSOR] = new HashSet<TicketStatus>
            {
                TicketStatus.IN_PROGRESS,
                TicketStatus.OPEN,
                TicketStatus.WAITING_ON_SITE,
                TicketStatus.RESOLVED,
                TicketStatus.CANCELLED
            },
            [TicketStatus.PENDING_REGULATORY_REVIEW] = new HashSet<TicketStatus>
            {
                TicketStatus.IN_PROGRESS,
                TicketStatus.RESOLVED,
                TicketStatus.CLOSED,
                TicketStatus.CANCELLED
            },
            [TicketStatus.RESOLVED] = new HashSet<TicketStatus>
            {
                TicketStatus.CLOSED,
                TicketStatus.REOPENED,
                TicketStatus.IN_PROGRESS
            },
            [TicketStatus.CLOSED] = new HashSet<TicketStatus>
            {
                TicketStatus.REOPENED
            },
            [TicketStatus.REOPENED] = new HashSet<TicketStatus>
            {
                TicketStatus.IN_PROGRESS,
                TicketStatus.RESOLVED,
                TicketStatus.CANCELLED,
                TicketStatus.WAITING_ON_SITE,
                TicketStatus.WAITING_ON_SPONSOR
            },
            [TicketStatus.CANCELLED] = new HashSet<TicketStatus>
            {
                TicketStatus.REOPENED
            }
        };

    public static bool CanTransition(TicketStatus current, TicketStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return AllowedTransitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    public static Result<TicketStatus> Guard(TicketStatus current, TicketStatus target)
    {
        if (current == target)
        {
            return Result<TicketStatus>.Success(target);
        }

        if (AllowedTransitions.TryGetValue(current, out var targets) && targets.Contains(target))
        {
            return Result<TicketStatus>.Success(target);
        }

        return DomainError.Validation(
            "INVALID_STATE_TRANSITION",
            $"Transition from {current} to {target} is forbidden.");
    }
}
