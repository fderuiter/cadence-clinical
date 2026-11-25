using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Application;
using Cadence.Apps.Tickets.Endpoints.Filters;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Tickets.Endpoints;

internal static class TicketCrudEndpoints
{
    public static void MapTicketCrudRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/tickets")
            .WithTags("Tickets")
            .RequireCallerStudyAndSiteScope();

        group.MapPost("", CreateTicketAsync)
            .WithName("CreateTicket")
            .Produces<TicketResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("tickets:create")
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

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

        group.MapPatch("/{ticket_id}", UpdateTicketAsync)
            .WithName("UpdateTicket")
            .Produces<TicketResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("tickets:create")
            .AddEndpointFilter<ChangeReasonHeaderFilter>();
    }

    private static Task<IResult> CreateTicketAsync(
        CreateTicketRequestDto request,
        HttpContext httpContext,
        TicketCrudApplicationService service,
        CancellationToken ct) =>
        service.CreateTicketAsync(request, httpContext, ct);

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
        TicketCrudApplicationService service,
        CancellationToken ct) =>
        service.ListTicketsAsync(
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
        TicketCrudApplicationService service,
        CancellationToken ct,
        [FromQuery(Name = "revision")] int? revision = null,
        [FromQuery(Name = "as_of")] DateTimeOffset? asOf = null) =>
        service.GetTicketAsync(ticket_id, httpContext, ct, revision, asOf);

    private static Task<IResult> UpdateTicketAsync(
        string ticket_id,
        UpdateTicketRequestDto request,
        HttpContext httpContext,
        TicketCrudApplicationService service,
        CancellationToken ct) =>
        service.UpdateTicketAsync(ticket_id, request, httpContext, ct);
}
