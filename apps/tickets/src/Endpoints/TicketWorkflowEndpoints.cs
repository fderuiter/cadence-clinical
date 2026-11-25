using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Application;
using Cadence.Apps.Tickets.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Tickets.Endpoints;

internal static class TicketWorkflowEndpoints
{
    public const string SignTicketSemanticAction = "tickets.ticket.sign";

    public static void MapTicketWorkflowRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/tickets")
            .WithTags("Tickets")
            .RequireCallerStudyAndSiteScope();

        group.MapGet("/{ticket_id}/comments", ListTicketCommentsAsync)
            .WithName("ListTicketComments")
            .Produces<IReadOnlyList<TicketCommentResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("tickets:read");

        group.MapPost("/{ticket_id}/comments", CreateTicketCommentAsync)
            .WithName("CreateTicketComment")
            .Produces<TicketCommentResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("tickets:comment")
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{ticket_id}/transitions", TransitionTicketAsync)
            .WithName("TransitionTicket")
            .Produces<TicketResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("tickets:transition")
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{ticket_id}/sign", SignTicketAsync)
            .WithName("SignTicket")
            .Produces<TicketResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("tickets:sign")
            .RequireElectronicSignature(SignTicketSemanticAction, SignTicketActionFor)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapGet("/{ticket_id}/audit-trail", GetTicketAuditTrailAsync)
            .WithName("GetTicketAuditTrail")
            .Produces<IReadOnlyList<TicketAuditEntryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("tickets:read");
    }

    public static string SignTicketAction(string ticketId) => $"/api/v1/tickets/{ticketId}/sign";

    private static string SignTicketActionFor(HttpContext httpContext) =>
        SignTicketAction(httpContext.Request.RouteValues["ticket_id"]?.ToString() ?? string.Empty);

    private static Task<IResult> ListTicketCommentsAsync(
        string ticket_id,
        HttpContext httpContext,
        TicketWorkflowApplicationService service,
        CancellationToken ct) =>
        service.ListTicketCommentsAsync(ticket_id, httpContext, ct);

    private static Task<IResult> CreateTicketCommentAsync(
        string ticket_id,
        CreateTicketCommentRequestDto request,
        HttpContext httpContext,
        TicketWorkflowApplicationService service,
        CancellationToken ct) =>
        service.CreateTicketCommentAsync(ticket_id, request, httpContext, ct);

    private static Task<IResult> TransitionTicketAsync(
        string ticket_id,
        TransitionTicketRequestDto request,
        HttpContext httpContext,
        TicketWorkflowApplicationService service,
        CancellationToken ct) =>
        service.TransitionTicketAsync(ticket_id, request, httpContext, ct);

    private static Task<IResult> SignTicketAsync(
        string ticket_id,
        SignTicketRequestDto request,
        HttpContext httpContext,
        TicketWorkflowApplicationService service,
        CancellationToken ct) =>
        service.SignTicketAsync(ticket_id, request, httpContext, ct);

    private static Task<IResult> GetTicketAuditTrailAsync(
        string ticket_id,
        HttpContext httpContext,
        TicketWorkflowApplicationService service,
        CancellationToken ct) =>
        service.GetTicketAuditTrailAsync(ticket_id, httpContext, ct);
}
