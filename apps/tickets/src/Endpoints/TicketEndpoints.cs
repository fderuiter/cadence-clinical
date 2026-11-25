using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Application;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Tickets.Endpoints;

/// <summary>
/// Minimal API endpoints for audited support ticket creation, scoped listing, reference lookup,
/// comments, workflow transitions, 21 CFR Part 11 electronic signatures, and audit trails
/// (PRD-TCK-001, PRD-TCK-002, ADR-2610, SRS-SYS-005).
/// @req:PRD-TCK-001
/// @req:PRD-TCK-002
/// @req:PRD-SYS-192
/// @req:SRS-SYS-005
/// </summary>
public static class TicketEndpoints
{
    public const string SignTicketSemanticAction = "tickets.ticket.sign";

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/tickets")
            .WithTags("Tickets")
            .RequireCallerStudyAndSiteScope();

        group.MapGet("", ListTicketsAsync)
            .WithName("ListTickets")
            .Produces<IReadOnlyList<TicketResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("tickets:read");

        group.MapGet("/{ticket_id}", GetTicketAsync)
            .WithName("GetTicket")
            .Produces<TicketResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("tickets:read");

        group.MapGet("/{ticket_id}/comments", ListTicketCommentsAsync)
            .WithName("ListTicketComments")
            .Produces<IReadOnlyList<TicketCommentResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("tickets:read");

        group.MapGet("/{ticket_id}/audit-trail", GetTicketAuditTrailAsync)
            .WithName("GetTicketAuditTrail")
            .Produces<IReadOnlyList<TicketAuditEntryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("tickets:read");

        app.MapTicketMutationEndpoints();
        return app;
    }

    public static string SignTicketAction(string ticketId) => $"/api/v1/tickets/{ticketId}/sign";

    private static Task<IResult> ListTicketsAsync(
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "category")] string? category,
        [FromQuery(Name = "priority")] string? priority,
        [FromQuery(Name = "gxp_severity")] string? gxpSeverity,
        [FromQuery(Name = "assigned_queue")] string? assignedQueue,
        [FromQuery(Name = "study_id")] string? requestedStudy,
        [FromQuery(Name = "site_id")] string? requestedSite,
        [FromQuery(Name = "search")] string? search,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "offset")] int? offset,
        HttpContext httpContext,
        [FromServices] ITicketQueryService queryService,
        CancellationToken ct) =>
        queryService.ListTicketsAsync(
            status,
            category,
            priority,
            gxpSeverity,
            assignedQueue,
            requestedStudy,
            requestedSite,
            search,
            limit,
            offset,
            httpContext,
            ct);

    private static Task<IResult> GetTicketAsync(
        string ticket_id,
        HttpContext httpContext,
        [FromServices] ITicketQueryService queryService,
        CancellationToken ct,
        [FromQuery(Name = "revision")] int? revision = null,
        [FromQuery(Name = "as_of")] DateTimeOffset? asOf = null) =>
        queryService.GetTicketAsync(ticket_id, httpContext, revision, asOf, ct);

    private static Task<IResult> ListTicketCommentsAsync(
        string ticket_id,
        HttpContext httpContext,
        [FromServices] ITicketQueryService queryService,
        CancellationToken ct) =>
        queryService.ListTicketCommentsAsync(ticket_id, httpContext, ct);

    private static Task<IResult> GetTicketAuditTrailAsync(
        string ticket_id,
        HttpContext httpContext,
        [FromServices] ITicketQueryService queryService,
        CancellationToken ct) =>
        queryService.GetTicketAuditTrailAsync(ticket_id, httpContext, ct);
}
