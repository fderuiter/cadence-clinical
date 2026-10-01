using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Tickets.Endpoints;

/// <summary>
/// Minimal API endpoints for ticket lifecycle management and batch query resolution operations.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// </summary>
public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tickets")
            .WithTags("Tickets");

        group.MapPost("/batch", BatchUpdateTicketsAsync)
            .WithName("BatchUpdateTickets")
            .Produces<BatchUpdateTicketsResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/batch-update", BatchUpdateTicketsAsync)
            .WithName("BatchUpdateTicketsAlias")
            .Produces<BatchUpdateTicketsResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListTicketsAsync)
            .WithName("ListTickets")
            .Produces<IReadOnlyList<Ticket>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", GetTicketByIdAsync)
            .WithName("GetTicketById")
            .Produces<Ticket>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateTicketAsync)
            .WithName("CreateTicket")
            .Produces<Ticket>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{id}", UpdateTicketAsync)
            .WithName("UpdateTicket")
            .Produces<Ticket>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public static async Task<IResult> BatchUpdateTicketsAsync(
        BatchUpdateTicketsCommand command,
        HttpContext httpContext,
        TicketsDbContext dbContext,
        IClock clock,
        CancellationToken ct)
    {
        var principal = httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
        string currentUser = principal?.UserId ?? httpContext.User.Identity?.Name ?? "system";

        bool hasCapability = PermissionMatrix.HasPermission(principal, "ticket.batch_resolve")
            || PermissionMatrix.HasPermission(principal, "ticket:batch_resolve")
            || (principal?.Roles.Any(r => r is "sysadmin" or "sponsor_dm" or "cra" or "monitor" or "admin" or "lead_monitor" or "data_manager" or "pi") ?? false)
            || httpContext.User.IsInRole("sysadmin")
            || httpContext.User.IsInRole("sponsor_dm")
            || httpContext.User.IsInRole("cra")
            || httpContext.User.IsInRole("admin");

        if (!hasCapability)
        {
            return Results.Problem(
                title: "Forbidden",
                detail: "Access denied. Action requires 'ticket.batch_resolve' capability.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var ids = command.TicketIds ?? command.TicketIdsAlt ?? [];
        if (ids.Count == 0)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "No ticket IDs provided in batch operation request.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (ids.Count > 100)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "Batch operation exceeds maximum allowed limit of 100 queries.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string rawAction = (command.Action ?? "RESOLVE").Trim().ToUpperInvariant();
        string rationale = command.Rationale ?? command.ResolutionRationale ?? $"Batch {rawAction}";

        int successCount = 0;
        int failureCount = 0;
        var results = new List<BatchTicketResultItem>();

        foreach (string id in ids)
        {
            var ticket = await dbContext.Tickets
                .FirstOrDefaultAsync(t => t.Id == id, ct)
                .ConfigureAwait(false);

            if (ticket is null)
            {
                failureCount++;
                results.Add(new BatchTicketResultItem(id, false, $"Ticket '{id}' was not found.", null, null));
                continue;
            }

            string currentStatus = (ticket.Status ?? string.Empty).ToUpperInvariant();
            string targetStatus;
            string? errorMessage = null;

            switch (rawAction)
            {
                case "RESOLVE":
                case "RESOLVED":
                    if (currentStatus == "CLOSED")
                    {
                        errorMessage = $"Ticket '{ticket.Reference}' is already CLOSED and cannot be resolved.";
                    }
                    targetStatus = "RESOLVED";
                    break;

                case "ESCALATE":
                case "ESCALATED":
                    if (currentStatus is "CLOSED" or "RESOLVED")
                    {
                        errorMessage = $"Ticket '{ticket.Reference}' is {currentStatus} and cannot be escalated.";
                    }
                    targetStatus = "ESCALATED";
                    break;

                case "PAUSE_SLA":
                case "PAUSED":
                    if (currentStatus is "CLOSED" or "RESOLVED")
                    {
                        errorMessage = $"Ticket '{ticket.Reference}' is {currentStatus} and SLA cannot be paused.";
                    }
                    targetStatus = "WAITING_ON_SITE";
                    break;

                case "CLOSE":
                case "CLOSED":
                    if (currentStatus == "CLOSED")
                    {
                        errorMessage = $"Ticket '{ticket.Reference}' is already CLOSED.";
                    }
                    targetStatus = "CLOSED";
                    break;

                case "REOPEN":
                case "OPEN":
                    if (currentStatus is not ("CLOSED" or "RESOLVED"))
                    {
                        errorMessage = $"Ticket '{ticket.Reference}' is not CLOSED or RESOLVED.";
                    }
                    targetStatus = "OPEN";
                    break;

                default:
                    errorMessage = $"Unsupported batch action '{command.Action}'.";
                    targetStatus = currentStatus;
                    break;
            }

            if (errorMessage is not null)
            {
                failureCount++;
                results.Add(new BatchTicketResultItem(ticket.Id, false, errorMessage, ticket.Status, ticket.Reference));
                continue;
            }

            ticket.Status = targetStatus;
            ticket.ReasonForChange = rationale;

            if (rawAction is "ESCALATE" or "ESCALATED")
            {
                ticket.LastEscalatedAt = clock.UtcNow;
                ticket.EscalationCount++;
                ticket.AssigneeRole = "PRINCIPAL_INVESTIGATOR";
            }
            else if (rawAction is "PAUSE_SLA" or "PAUSED")
            {
                ticket.SlaPausedAt = clock.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(command.RootCauseCategory))
            {
                ticket.RootCauseCategory = command.RootCauseCategory;
            }

            if (!string.IsNullOrWhiteSpace(command.RootCauseSummary))
            {
                ticket.RootCauseSummary = command.RootCauseSummary;
            }

            if (!string.IsNullOrWhiteSpace(command.ResolutionCode))
            {
                ticket.ResolutionCode = command.ResolutionCode;
            }

            string? sigToken = command.SignatureToken ?? command.SigToken;
            if (!string.IsNullOrWhiteSpace(sigToken) || rawAction is "RESOLVE" or "RESOLVED")
            {
                ticket.SignatureToken = sigToken ?? $"sig_{Guid.NewGuid():N}";
                ticket.SignatureMeaning = command.SignatureMeaning ?? "I authoritatively verify and close these clinical deviations/queries under 21 CFR Part 11.";
                ticket.SignatureAction = $"BATCH_{rawAction}";
                ticket.SignatureTimestamp = clock.UtcNow;
                ticket.SignatureUser = currentUser;
            }

            dbContext.TicketAuditLogs.Add(new TicketAuditLog
            {
                TicketId = ticket.Id,
                Action = $"BATCH_{rawAction}",
                Details = $"Batch operation '{rawAction}' executed. Rationale: {rationale}",
                CreatedBy = currentUser,
                ReasonForChange = rationale,
                CreatedAt = clock.UtcNow
            });

            successCount++;
            results.Add(new BatchTicketResultItem(ticket.Id, true, null, ticket.Status, ticket.Reference));
        }

        if (successCount > 0)
        {
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(new BatchUpdateTicketsResult(
            SuccessCount: successCount,
            FailureCount: failureCount,
            Results: results));
    }

    public static async Task<IResult> ListTicketsAsync(
        TicketsDbContext dbContext,
        CancellationToken ct)
    {
        var list = await dbContext.Tickets
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Results.Ok(list);
    }

    public static async Task<IResult> GetTicketByIdAsync(
        string id,
        TicketsDbContext dbContext,
        CancellationToken ct)
    {
        var ticket = await dbContext.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
            .ConfigureAwait(false);

        return ticket is not null ? Results.Ok(ticket) : Results.NotFound();
    }

    public static async Task<IResult> CreateTicketAsync(
        Ticket ticket,
        TicketsDbContext dbContext,
        IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string currentUser = httpContext.User.Identity?.Name ?? "system";
        if (string.IsNullOrWhiteSpace(ticket.Id)) ticket.Id = Guid.NewGuid().ToString();
        ticket.CreatedAt = clock.UtcNow.UtcDateTime;
        ticket.CreatedBy = currentUser;
        if (string.IsNullOrWhiteSpace(ticket.ReasonForChange)) ticket.ReasonForChange = "Initial ticket log";

        dbContext.Tickets.Add(ticket);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/v1/tickets/{ticket.Id}", ticket);
    }

    public static async Task<IResult> UpdateTicketAsync(
        string id,
        Ticket updated,
        TicketsDbContext dbContext,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var existing = await dbContext.Tickets
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
            .ConfigureAwait(false);

        if (existing is null) return Results.NotFound();

        existing.Title = updated.Title;
        existing.Description = updated.Description;
        existing.Status = updated.Status;
        existing.Priority = updated.Priority;
        existing.Category = updated.Category;
        existing.ReasonForChange = updated.ReasonForChange ?? "Updated ticket";

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(existing);
    }
}

public record BatchUpdateTicketsCommand(
    [property: JsonPropertyName("ticket_ids")] IReadOnlyList<string>? TicketIds,
    [property: JsonPropertyName("ticketIds")] IReadOnlyList<string>? TicketIdsAlt,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("rationale")] string? Rationale,
    [property: JsonPropertyName("resolution_rationale")] string? ResolutionRationale,
    [property: JsonPropertyName("root_cause_category")] string? RootCauseCategory,
    [property: JsonPropertyName("root_cause_summary")] string? RootCauseSummary,
    [property: JsonPropertyName("resolution_code")] string? ResolutionCode,
    [property: JsonPropertyName("signature_token")] string? SignatureToken,
    [property: JsonPropertyName("sig_token")] string? SigToken,
    [property: JsonPropertyName("signature_meaning")] string? SignatureMeaning);

public record BatchUpdateTicketsResult(
    [property: JsonPropertyName("success_count")] int SuccessCount,
    [property: JsonPropertyName("failure_count")] int FailureCount,
    [property: JsonPropertyName("results")] IReadOnlyList<BatchTicketResultItem> Results);

public record BatchTicketResultItem(
    [property: JsonPropertyName("ticket_id")] string TicketId,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error_message")] string? ErrorMessage,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("reference")] string? Reference);
