using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Application;
using Cadence.Apps.Tickets.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Tickets.Endpoints;

public static class TicketMutationEndpoints
{
    private const string SignTicketSemanticAction = "tickets.ticket.sign";

    public static IEndpointRouteBuilder MapTicketMutationEndpoints(this IEndpointRouteBuilder app)
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

        return app;
    }

    private static string SignTicketAction(string ticketId) => $"/api/v1/tickets/{ticketId}/sign";

    private static string SignTicketActionFor(HttpContext httpContext) =>
        SignTicketAction(httpContext.Request.RouteValues["ticket_id"]?.ToString() ?? string.Empty);

    private static Task<IResult> CreateTicketAsync(
        CreateTicketRequestDto request,
        HttpContext httpContext,
        [FromServices] ITicketAuthoringService authoringService,
        CancellationToken ct) =>
        authoringService.CreateTicketAsync(request, httpContext, ct);

    private static Task<IResult> UpdateTicketAsync(
        string ticket_id,
        UpdateTicketRequestDto request,
        HttpContext httpContext,
        [FromServices] ITicketAuthoringService authoringService,
        CancellationToken ct) =>
        authoringService.UpdateTicketAsync(ticket_id, request, httpContext, ct);

    private static Task<IResult> CreateTicketCommentAsync(
        string ticket_id,
        CreateTicketCommentRequestDto request,
        HttpContext httpContext,
        [FromServices] ITicketAuthoringService authoringService,
        CancellationToken ct) =>
        authoringService.CreateTicketCommentAsync(ticket_id, request, httpContext, ct);

    private static Task<IResult> TransitionTicketAsync(
        string ticket_id,
        TransitionTicketRequestDto request,
        HttpContext httpContext,
        [FromServices] ITicketWorkflowService workflowService,
        CancellationToken ct) =>
        workflowService.TransitionTicketAsync(ticket_id, request, httpContext, ct);

    private static Task<IResult> SignTicketAsync(
        string ticket_id,
        SignTicketRequestDto request,
        HttpContext httpContext,
        [FromServices] ITicketWorkflowService workflowService,
        CancellationToken ct) =>
        workflowService.SignTicketAsync(ticket_id, request, httpContext, ct);
}
