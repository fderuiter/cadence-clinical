using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Tickets.Application;

internal static partial class TicketAccessAndMapping
{
    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9_-]{1,99}$")]
    internal static partial Regex ResolutionCodeRegex();

    internal static readonly HashSet<string> ValidCommentVisibilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "PUBLIC",
        "INTERNAL_SPONSOR",
    };

    internal static readonly HashSet<string> ValidRootCauseCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROCESS_WORKFLOW_DEFICIT",
        "SITE_TRAINING_GAP",
        "DATA_ENTRY_ERROR",
        "SYSTEM_CONFIGURATION",
        "EQUIPMENT_CALIBRATION",
        "HUMAN_ERROR",
        "SYSTEM_DEFECT",
        "DATA_DISCREPANCY",
        "PROTOCOL_CLARIFICATION",
        "ACCESS_PROVISIONING",
        "INTEGRATION_LATENCY",
        "TRAINING_GAP",
        "NOT_APPLICABLE",
    };

    private static readonly HashSet<TicketStatus> SlaPausedStatuses =
    [
        TicketStatus.WAITING_ON_SITE,
        TicketStatus.WAITING_ON_SPONSOR,
        TicketStatus.PENDING_REGULATORY_REVIEW,
    ];

    private static readonly HashSet<string> SponsorCommentCapableRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNormalization.ROLE_SYSADMIN,
        RoleNormalization.ROLE_SPONSOR_DESIGNER,
        RoleNormalization.ROLE_SPONSOR_DM,
        RoleNormalization.ROLE_SPONSOR_MM,
        RoleNormalization.ROLE_SPONSOR_STATISTICIAN,
        RoleNormalization.ROLE_CRA_CANONICAL,
        "monitor",
        RoleNormalization.ROLE_EXTERNAL_MONITOR,
        RoleNormalization.ROLE_REVIEWER,
        RoleNormalization.ROLE_FINANCE_OFFICER,
        RoleNormalization.ROLE_AUDITOR_CANONICAL,
        "tmf_auditor",
        "tmf auditor",
        RoleNormalization.ROLE_UNBLINDED_STATISTICIAN,
        RoleNormalization.ROLE_IDMC,
        RoleNormalization.ROLE_EMERGENCY_UNBLINDER,
    };

    internal static async Task<(Principal? Principal, Ticket? Ticket, IResult? ErrorResult)> LoadAuthorizedTicketAsync(
        string rawTicketId,
        HttpContext httpContext,
        TicketsUnitOfWork uow,
        bool asNoTracking,
        CancellationToken ct)
    {
        Principal? principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (principal is null || string.IsNullOrWhiteSpace(principal.UserId))
        {
            return (null, null, Results.Problem(
                title: "Unauthorized caller",
                detail: "Verified gateway caller identity is required.",
                statusCode: StatusCodes.Status401Unauthorized));
        }

        if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext) is { } callerScopeDenied)
        {
            return (null, null, callerScopeDenied);
        }

        string key = (rawTicketId ?? string.Empty).Trim();
        IQueryable<Ticket> query = asNoTracking ? uow.Context.Tickets.AsNoTracking() : uow.Context.Tickets;
        Ticket? ticket = await query
            .FirstOrDefaultAsync(t => !t.IsDeleted && (t.Id == key || t.Reference == key), ct)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return (principal, null, Results.Problem(
                title: "Ticket not found",
                detail: $"No support ticket matched '{key}'.",
                statusCode: StatusCodes.Status404NotFound));
        }

        if (!IsTicketVisibleToCaller(principal, ticket))
        {
            return (principal, null, ClinicalScopeGuard.Forbidden(
                detail: "Caller is not authorized for this ticket's study or site scope.",
                title: "Ticket scope forbidden"));
        }

        return (principal, ticket, null);
    }

    internal static void ApplySlaPauseAndResume(
        Ticket ticket,
        TicketStatus previousStatus,
        TicketStatus nextStatus,
        DateTimeOffset now)
    {
        if (previousStatus == nextStatus)
        {
            return;
        }

        bool wasPaused = SlaPausedStatuses.Contains(previousStatus);
        bool willPause = SlaPausedStatuses.Contains(nextStatus);

        if (!wasPaused && willPause)
        {
            ticket.SlaPausedAt = now;
        }
        else if (wasPaused && !willPause)
        {
            if (ticket.SlaPausedAt is DateTimeOffset pausedAt)
            {
                int pausedSeconds = (int)Math.Max(0, (now - pausedAt).TotalSeconds);
                ticket.SlaTotalPausedSeconds += pausedSeconds;
                if (ticket.SlaTargetAt is DateTimeOffset slaTarget)
                {
                    ticket.SlaTargetAt = slaTarget.AddSeconds(pausedSeconds);
                }

                if (ticket.DueDate is DateTimeOffset dueDate)
                {
                    ticket.DueDate = dueDate.AddSeconds(pausedSeconds);
                }
            }

            ticket.SlaPausedAt = null;
        }

        if (ticket.SlaTargetAt is DateTimeOffset effectiveTarget && !SlaPausedStatuses.Contains(nextStatus))
        {
            ticket.SlaBreached = now > effectiveTarget && !TicketTransitionGraph.TerminalStates.Contains(nextStatus);
        }
    }

    internal static bool IsSiteRestrictedCommentCaller(Principal principal)
    {
        var normalized = RoleNormalization.GetNormalizedRoles(principal.Roles.Concat(principal.RawRoles));
        return !normalized.Any(SponsorCommentCapableRoles.Contains);
    }

    internal static TicketCommentResponseDto ToCommentDto(TicketComment comment) =>
        new(
            Id: comment.Id,
            TicketId: comment.TicketId,
            Body: comment.Body,
            Visibility: comment.Visibility,
            CreatedAt: comment.CreatedAt,
            CreatedBy: comment.CreatedBy,
            ReasonForChange: comment.ReasonForChange,
            VersionIndex: comment.VersionIndex);

    internal static bool IsTicketVisibleToCaller(Principal principal, Ticket ticket)
    {
        if (!string.IsNullOrWhiteSpace(ticket.StudyId))
        {
            if (!StudyAccessEvaluator.CanAccessStudy(principal, ticket.StudyId))
            {
                return false;
            }
        }
        else if (!HasAllStudies(principal)
                 && !string.Equals(ticket.Reporter, principal.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ticket.SiteId))
        {
            if (!SiteAccessEvaluator.CanAccessSite(principal, ticket.SiteId))
            {
                return false;
            }
        }
        else if (ClinicalScopeGuard.IsSiteBound(principal)
                 && !string.Equals(ticket.Reporter, principal.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    internal static bool HasAllStudies(Principal principal) =>
        principal.AssignedStudies.Any(s => string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal));

    internal static string? ResolveDefaultStudyScope(Principal principal)
    {
        var explicitStudies = principal.AssignedStudies
            .Where(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal))
            .ToList();
        return explicitStudies.Count == 1 ? explicitStudies[0] : null;
    }

    internal static string GenerateTicketReference(DateTimeOffset timestamp)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
        return $"TKT-{timestamp.Year}-{token}";
    }

    internal static TicketResponseDto ToResponseDto(Ticket ticket)
    {
        StoredTicketContext? ctx = null;
        if (!string.IsNullOrWhiteSpace(ticket.ContextPayload))
        {
            try
            {
                ctx = JsonSerializer.Deserialize<StoredTicketContext>(ticket.ContextPayload);
            }
            catch (JsonException)
            {
                // Non-structured context payloads remain gracefully ignored.
            }
        }

        string assignedQueue = ctx?.AssignedQueue
                               ?? ticket.AssigneeRole
                               ?? TicketRoutingAndSlaEngine.ResolveQueue(ticket.Category, ctx?.SourceRoute, ticket.RelatedEntityType);

        string? subjectCode = ctx?.SubjectCode
                              ?? (string.Equals(ticket.RelatedEntityType, "SUBJECT", StringComparison.OrdinalIgnoreCase)
                                  ? ticket.RelatedEntityId
                                  : null);

        return new TicketResponseDto(
            Id: ticket.Id,
            Reference: ticket.Reference,
            Title: ticket.Title,
            Description: ticket.Description,
            Category: ticket.Category,
            Priority: ticket.Priority,
            GxpSeverity: ticket.GxpSeverity,
            Status: ticket.Status.ToString(),
            AssignedQueue: assignedQueue,
            AssigneeRole: ticket.AssigneeRole,
            AssigneeUser: ticket.AssigneeUser,
            Reporter: ticket.Reporter,
            CreatedBy: ticket.CreatedBy,
            StudyScope: ticket.StudyId,
            SiteScope: ticket.SiteId,
            SubjectCode: subjectCode,
            RelatedEntityType: ticket.RelatedEntityType,
            RelatedEntityId: ticket.RelatedEntityId,
            SourceRoute: ctx?.SourceRoute,
            DueDate: ticket.DueDate,
            AcknowledgeTargetAt: ctx?.AcknowledgeTargetAt,
            SlaTargetAt: ticket.SlaTargetAt,
            SlaPausedAt: ticket.SlaPausedAt,
            SlaTotalPausedSeconds: ticket.SlaTotalPausedSeconds,
            SlaBreached: ticket.SlaBreached,
            SlaAmberWarned: ticket.SlaAmberWarned,
            EscalationCount: ticket.EscalationCount,
            ResolutionCode: ticket.ResolutionCode,
            RootCauseCategory: ticket.RootCauseCategory,
            RootCauseSummary: ticket.RootCauseSummary,
            SignatureAction: ticket.SignatureAction,
            SignatureToken: ticket.SignatureToken,
            SignatureMeaning: ticket.SignatureMeaning,
            SignatureTimestamp: ticket.SignatureTimestamp,
            SignatureUser: ticket.SignatureUser,
            ReasonForChange: ticket.ReasonForChange,
            CreatedAt: ticket.CreatedAt,
            UpdatedAt: ticket.SignatureTimestamp ?? ticket.CreatedAt,
            VersionIndex: ticket.VersionIndex);
    }

    internal static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
