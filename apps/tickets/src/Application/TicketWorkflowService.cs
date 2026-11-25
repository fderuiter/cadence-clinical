using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Tickets.Application;

public interface ITicketWorkflowService
{
    Task<IResult> TransitionTicketAsync(
        string ticketId,
        TransitionTicketRequestDto request,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> SignTicketAsync(
        string ticketId,
        SignTicketRequestDto request,
        HttpContext httpContext,
        CancellationToken ct);
}

public sealed class TicketWorkflowService(
    TicketsDbContext db,
    ITicketsUnitOfWork unitOfWork,
    IClock clock) : ITicketWorkflowService
{
    public async Task<IResult> TransitionTicketAsync(
        string ticketId,
        TransitionTicketRequestDto request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await TicketScopeAndMappingHelpers.LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            db,
            asNoTracking: false,
            ct).ConfigureAwait(false);
        if (errorResult is not null || principal is null || ticket is null)
        {
            return errorResult!;
        }

        if (ticket.SignatureTimestamp is not null)
        {
            return Results.Problem(
                title: "Ticket locked by 21 CFR Part 11 signature",
                detail: "This support ticket has been electronically signed and locked against further status transitions.",
                statusCode: StatusCodes.Status409Conflict);
        }

        string changeReason = ChangeReasonHeader.Read(httpContext.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter must guarantee a non-empty X-Change-Reason.");

        string targetStatusRaw = (request.TargetStatus ?? string.Empty).Trim();
        if (targetStatusRaw.Length == 0)
        {
            return Results.Problem(
                title: "Invalid transition payload",
                detail: "'target_status' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? normalizedResolutionCode = TicketScopeAndMappingHelpers.NormalizeOptional(request.ResolutionCode)?.ToUpperInvariant();
        if (normalizedResolutionCode is not null
            && !TicketScopeAndMappingHelpers.ResolutionCodeRegex().IsMatch(normalizedResolutionCode))
        {
            return Results.Problem(
                title: "Invalid resolution code",
                detail: $"Unsupported resolution_code '{normalizedResolutionCode}'. Use an uppercase code identifier (2-100 characters).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? normalizedRootCauseCategory = TicketScopeAndMappingHelpers.NormalizeOptional(request.RootCauseCategory)?.ToUpperInvariant();
        if (normalizedRootCauseCategory is not null
            && !TicketScopeAndMappingHelpers.ValidRootCauseCategories.Contains(normalizedRootCauseCategory))
        {
            return Results.Problem(
                title: "Invalid root cause category",
                detail: $"Unsupported root_cause_category '{normalizedRootCauseCategory}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? normalizedRootCauseSummary = TicketScopeAndMappingHelpers.NormalizeOptional(request.RootCauseSummary);
        if (normalizedRootCauseSummary is { Length: > 1000 })
        {
            return Results.Problem(
                title: "Invalid root cause summary",
                detail: "'root_cause_summary' must not exceed 1000 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? piiViolation = TicketPiiValidator.ValidateTransitionNoDirectParticipantPii(
            normalizedRootCauseSummary,
            normalizedResolutionCode,
            changeReason);
        if (piiViolation is not null)
        {
            return Results.Problem(
                title: "Direct participant PII rejected",
                detail: piiViolation,
                statusCode: StatusCodes.Status400BadRequest);
        }

        TicketStatus previousStatus = ticket.Status;
        var transitionResult = ticket.TransitionTo(
            targetStatusRaw,
            normalizedResolutionCode,
            normalizedRootCauseSummary,
            normalizedRootCauseCategory);
        if (transitionResult.IsFailure)
        {
            var err = transitionResult.Error;
            int statusCode = err?.Type == Cadence.Core.Domain.ErrorType.Conflict
                             || string.Equals(err?.Code, "INVALID_STATUS_TRANSITION", StringComparison.Ordinal)
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            return Results.Problem(
                title: err?.Code ?? "INVALID_TRANSITION",
                detail: err?.Description ?? "Ticket status transition failed validation.",
                statusCode: statusCode);
        }

        string? normalizedAssigneeUser = TicketScopeAndMappingHelpers.NormalizeOptional(request.AssigneeUser);
        if (normalizedAssigneeUser is not null)
        {
            ticket.AssigneeUser = normalizedAssigneeUser;
        }

        int baselineVersion = db.Entry(ticket).Property(x => x.VersionIndex).OriginalValue;
        DateTimeOffset now = clock.UtcNow;
        TicketScopeAndMappingHelpers.ApplySlaPauseAndResume(ticket, previousStatus, ticket.Status, now);
        ticket.ReasonForChange = changeReason;
        ticket.VersionIndex = baselineVersion + 1;

        db.TicketRevisions.Add(TicketRevision.FromTicket(ticket, now, principal.UserId));

        AuditActions.Record(db, new AuditAction(
            EntityName: nameof(Ticket),
            EntityId: ticket.Id,
            Action: "TICKET_TRANSITIONED",
            UserId: principal.UserId,
            ReasonForChange: changeReason,
            ChangesJson: JsonSerializer.Serialize(new
            {
                reference = ticket.Reference,
                from_status = previousStatus.ToString(),
                to_status = ticket.Status.ToString(),
                resolution_code = ticket.ResolutionCode,
                root_cause_category = ticket.RootCauseCategory,
                sla_paused_at = ticket.SlaPausedAt,
                sla_total_paused_seconds = ticket.SlaTotalPausedSeconds,
                sla_target_at = ticket.SlaTargetAt,
                version_index = ticket.VersionIndex,
            })));
        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(TicketScopeAndMappingHelpers.ToResponseDto(ticket));
    }

    public async Task<IResult> SignTicketAsync(
        string ticketId,
        SignTicketRequestDto request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await TicketScopeAndMappingHelpers.LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            db,
            asNoTracking: false,
            ct).ConfigureAwait(false);
        if (errorResult is not null || principal is null || ticket is null)
        {
            return errorResult!;
        }

        string changeReason = ChangeReasonHeader.Read(httpContext.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter must guarantee a non-empty X-Change-Reason.");

        string meaning = (request.SignatureMeaning ?? string.Empty).Trim();
        if (meaning.Length == 0 || meaning.Length > 255)
        {
            return Results.Problem(
                title: "Invalid signature meaning",
                detail: "A non-empty 21 CFR Part 11 'signature_meaning' (up to 255 characters) is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string signatureAction = string.IsNullOrWhiteSpace(request.SignatureAction)
            ? "SIGN_CLOSURE"
            : request.SignatureAction.Trim().ToUpperInvariant();
        if (signatureAction.Length > 255)
        {
            return Results.Problem(
                title: "Invalid signature action",
                detail: "'signature_action' must not exceed 255 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? piiViolation = TicketPiiValidator.ValidateSignatureNoDirectParticipantPii(meaning, changeReason);
        if (piiViolation is not null)
        {
            return Results.Problem(
                title: "Direct participant PII rejected",
                detail: piiViolation,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (ticket.Status is not (TicketStatus.RESOLVED or TicketStatus.CLOSED))
        {
            return Results.Problem(
                title: "Ticket not eligible for closure signature",
                detail: "Only RESOLVED or CLOSED support tickets may be signed under 21 CFR Part 11.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (ticket.SignatureTimestamp is not null)
        {
            return Results.Problem(
                title: "Ticket already signed",
                detail: "This support ticket already carries an immutable 21 CFR Part 11 electronic signature.",
                statusCode: StatusCodes.Status409Conflict);
        }

        int baselineVersion = db.Entry(ticket).Property(x => x.VersionIndex).OriginalValue;
        DateTimeOffset now = clock.UtcNow;
        string? verifiedTokenId = httpContext.VerifiedSigToken()?.Id;
        ticket.SignatureAction = signatureAction;
        ticket.SignatureMeaning = meaning;
        ticket.SignatureToken = string.IsNullOrWhiteSpace(verifiedTokenId) ? "verified" : verifiedTokenId;
        ticket.SignatureTimestamp = now;
        ticket.SignatureUser = principal.UserId;
        ticket.ReasonForChange = changeReason;
        ticket.VersionIndex = baselineVersion + 1;

        db.TicketRevisions.Add(TicketRevision.FromTicket(ticket, now, principal.UserId));

        AuditActions.Record(db, new AuditAction(
            EntityName: nameof(Ticket),
            EntityId: ticket.Id,
            Action: "TICKET_SIGNED",
            UserId: principal.UserId,
            ReasonForChange: changeReason,
            ChangesJson: JsonSerializer.Serialize(new
            {
                reference = ticket.Reference,
                status = ticket.Status.ToString(),
                signature_action = ticket.SignatureAction,
                signature_meaning = ticket.SignatureMeaning,
                signature_token_jti = ticket.SignatureToken,
                signature_timestamp = ticket.SignatureTimestamp,
                signature_user = ticket.SignatureUser,
                version_index = ticket.VersionIndex,
            })));
        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(TicketScopeAndMappingHelpers.ToResponseDto(ticket));
    }
}
