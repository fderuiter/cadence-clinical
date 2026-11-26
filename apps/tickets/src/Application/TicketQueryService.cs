using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Tickets.Application;

public interface ITicketQueryService
{
    Task<IResult> ListTicketsAsync(
        string? status,
        string? category,
        string? priority,
        string? gxpSeverity,
        string? assignedQueue,
        string? requestedStudy,
        string? requestedSite,
        string? search,
        int? limit,
        int? offset,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> GetTicketAsync(
        string ticketId,
        HttpContext httpContext,
        int? revision,
        DateTimeOffset? asOf,
        CancellationToken ct);

    Task<IResult> ListTicketCommentsAsync(
        string ticketId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> GetTicketAuditTrailAsync(
        string ticketId,
        HttpContext httpContext,
        CancellationToken ct);
}

public sealed class TicketQueryService(TicketsDbContext db) : ITicketQueryService
{
    public async Task<IResult> ListTicketsAsync(
        string? status,
        string? category,
        string? priority,
        string? gxpSeverity,
        string? assignedQueue,
        string? requestedStudy,
        string? requestedSite,
        string? search,
        int? limit,
        int? offset,
        HttpContext httpContext,
        CancellationToken ct)
    {
        Principal? principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (principal is null || string.IsNullOrWhiteSpace(principal.UserId))
        {
            return Results.Problem(
                title: "Unauthorized caller",
                detail: "Verified gateway caller identity is required.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext) is { } callerScopeDenied)
        {
            return callerScopeDenied;
        }

        string? normStudy = TicketScopeAndMappingHelpers.NormalizeOptional(requestedStudy);
        if (normStudy is not null && ClinicalScopeGuard.RequireStudy(httpContext, normStudy) is { } studyDenied)
        {
            return studyDenied;
        }

        string? normSite = TicketScopeAndMappingHelpers.NormalizeOptional(requestedSite);
        if (normSite is not null && ClinicalScopeGuard.RequireSite(httpContext, normSite) is { } siteDenied)
        {
            return siteDenied;
        }

        IQueryable<Ticket> query = db.Tickets
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<TicketStatus>(status.Trim(), ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(t => t.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            string cat = category.Trim().ToUpperInvariant();
            query = query.Where(t => t.Category == cat);
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            string prio = priority.Trim().ToUpperInvariant();
            query = query.Where(t => t.Priority == prio);
        }

        if (!string.IsNullOrWhiteSpace(gxpSeverity))
        {
            string sev = gxpSeverity.Trim().ToUpperInvariant();
            query = query.Where(t => t.GxpSeverity == sev);
        }

        if (!string.IsNullOrWhiteSpace(assignedQueue))
        {
            string queue = assignedQueue.Trim().ToUpperInvariant();
            query = query.Where(t => t.AssigneeRole == queue);
        }

        if (normStudy is not null)
        {
            query = query.Where(t => t.StudyId == normStudy);
        }

        if (normSite is not null)
        {
            query = query.Where(t => t.SiteId == normSite);
        }

        var candidates = await query
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string trimmedSearch = (search ?? string.Empty).Trim();
        int safeOffset = Math.Max(0, offset ?? 0);
        int safeLimit = Math.Clamp(limit ?? 100, 1, 500);

        var visible = candidates
            .Where(t => TicketScopeAndMappingHelpers.IsTicketVisibleToCaller(principal, t))
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Reference, StringComparer.Ordinal)
            .Select(TicketScopeAndMappingHelpers.ToResponseDto)
            .Where(dto => trimmedSearch.Length == 0
                          || dto.Title.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase)
                          || dto.Reference.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase)
                          || dto.Description.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase))
            .Skip(safeOffset)
            .Take(safeLimit)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<TicketResponseDto>>(visible);
    }

    public async Task<IResult> GetTicketAsync(
        string ticketId,
        HttpContext httpContext,
        int? revision,
        DateTimeOffset? asOf,
        CancellationToken ct)
    {
        var (_, ticket, errorResult) = await TicketScopeAndMappingHelpers.LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            db,
            asNoTracking: true,
            ct).ConfigureAwait(false);
        if (errorResult is not null || ticket is null)
        {
            return errorResult!;
        }

        if (revision.HasValue || asOf.HasValue)
        {
            IQueryable<TicketRevision> revQuery = db.TicketRevisions
                .AsNoTracking()
                .Where(r => r.TicketId == ticket.Id);

            TicketRevision? matchedRevision = revision.HasValue
                ? await revQuery.FirstOrDefaultAsync(r => r.RevisionNumber == revision.Value, ct).ConfigureAwait(false)
                : await revQuery
                    .Where(r => r.RecordedAt <= asOf!.Value)
                    .OrderByDescending(r => r.RevisionNumber)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

            if (matchedRevision is null)
            {
                return Results.Problem(
                    title: "Ticket revision not found",
                    detail: $"No historical revision matched for ticket '{ticketId.Trim()}'.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return TypedResults.Ok(TicketScopeAndMappingHelpers.ToResponseDto(
                Ticket.ReconstructFromRevision(ticket, matchedRevision)));
        }

        return TypedResults.Ok(TicketScopeAndMappingHelpers.ToResponseDto(ticket));
    }

    public async Task<IResult> ListTicketCommentsAsync(
        string ticketId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await TicketScopeAndMappingHelpers.LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            db,
            asNoTracking: true,
            ct).ConfigureAwait(false);
        if (errorResult is not null || principal is null || ticket is null)
        {
            return errorResult!;
        }

        bool restrictInternal = TicketScopeAndMappingHelpers.IsSiteRestrictedCommentCaller(principal);

        var rawComments = await db.TicketComments
            .AsNoTracking()
            .Where(c => c.TicketId == ticket.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var comments = rawComments
            .Where(c => !restrictInternal || string.Equals(c.Visibility, "PUBLIC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .Select(TicketScopeAndMappingHelpers.ToCommentDto)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<TicketCommentResponseDto>>(comments);
    }

    public async Task<IResult> GetTicketAuditTrailAsync(
        string ticketId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await TicketScopeAndMappingHelpers.LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            db,
            asNoTracking: true,
            ct).ConfigureAwait(false);
        if (errorResult is not null || principal is null || ticket is null)
        {
            return errorResult!;
        }

        bool restrictInternal = TicketScopeAndMappingHelpers.IsSiteRestrictedCommentCaller(principal);

        var comments = await db.TicketComments
            .AsNoTracking()
            .Where(c => c.TicketId == ticket.Id)
            .Select(c => new { c.Id, c.Visibility })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var visibleCommentIds = comments
            .Where(c => !restrictInternal || string.Equals(c.Visibility, "PUBLIC", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);

        var rawAuditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e =>
                (e.EntityName == nameof(Ticket) && e.EntityId == ticket.Id)
                || (e.EntityName == nameof(TicketComment) && visibleCommentIds.Contains(e.EntityId)))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var ordered = rawAuditEntries
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.VersionIndex)
            .Select(e => new TicketAuditEntryDto(
                Id: e.Id,
                EntityName: e.EntityName,
                EntityId: e.EntityId,
                Action: e.Action,
                Timestamp: e.Timestamp,
                UserId: e.UserId,
                ReasonForChange: e.ReasonForChange,
                VersionIndex: e.VersionIndex,
                ChangesJson: e.ChangesJson,
                PredecessorHash: e.PredecessorHash,
                BlockHash: e.BlockHash))
            .ToList();

        return TypedResults.Ok<IReadOnlyList<TicketAuditEntryDto>>(ordered);
    }
}
