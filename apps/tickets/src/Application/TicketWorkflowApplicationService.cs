using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using static Cadence.Apps.Tickets.Application.TicketAccessAndMapping;

namespace Cadence.Apps.Tickets.Application;

/// <summary>
/// Application service owning support ticket comments, SLA-aware workflow transitions,
/// 21 CFR Part 11 closure electronic signatures, and hash-chained audit trail queries.
/// </summary>
public sealed class TicketWorkflowApplicationService(TicketsUnitOfWork uow, IClock clock)
{
    public async Task<IResult> ListTicketCommentsAsync(
        string ticketId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            uow,
            asNoTracking: true,
            ct).ConfigureAwait(false);
        if (errorResult is not null || principal is null || ticket is null)
        {
            return errorResult!;
        }

        bool restrictInternal = IsSiteRestrictedCommentCaller(principal);

        var rawComments = await uow.Context.TicketComments
            .AsNoTracking()
            .Where(c => c.TicketId == ticket.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var comments = rawComments
            .Where(c => !restrictInternal || string.Equals(c.Visibility, "PUBLIC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .Select(ToCommentDto)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<TicketCommentResponseDto>>(comments);
    }

    public async Task<IResult> CreateTicketCommentAsync(
        string ticketId,
        CreateTicketCommentRequestDto request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            uow,
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
                detail: "This support ticket has been electronically signed and locked against further comments or amendments.",
                statusCode: StatusCodes.Status409Conflict);
        }

        string changeReason = ChangeReasonHeader.Read(httpContext.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter must guarantee a non-empty X-Change-Reason.");

        string body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            return Results.Problem(
                title: "Invalid comment payload",
                detail: "Comment 'body' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (body.Length > 4000)
        {
            return Results.Problem(
                title: "Invalid comment payload",
                detail: "Comment 'body' must not exceed 4000 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string visibility = string.IsNullOrWhiteSpace(request.Visibility)
            ? "PUBLIC"
            : request.Visibility.Trim().ToUpperInvariant();
        if (!ValidCommentVisibilities.Contains(visibility))
        {
            return Results.Problem(
                title: "Invalid comment visibility",
                detail: $"Unsupported comment visibility '{visibility}'. Expected PUBLIC or INTERNAL_SPONSOR.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.Equals(visibility, "INTERNAL_SPONSOR", StringComparison.OrdinalIgnoreCase)
            && IsSiteRestrictedCommentCaller(principal))
        {
            return ClinicalScopeGuard.Forbidden(
                detail: "Only callers holding sponsor or oversight roles may read or post INTERNAL_SPONSOR comments.",
                title: "Internal sponsor comment forbidden");
        }

        string? piiViolation = TicketPiiValidator.ValidateCommentNoDirectParticipantPii(body, changeReason);
        if (piiViolation is not null)
        {
            return Results.Problem(
                title: "Direct participant PII rejected",
                detail: piiViolation,
                statusCode: StatusCodes.Status400BadRequest);
        }

        DateTimeOffset now = clock.UtcNow;
        var comment = new TicketComment
        {
            Id = Guid.NewGuid().ToString("D"),
            TicketId = ticket.Id,
            Body = body,
            Visibility = visibility,
            CreatedAt = now,
            CreatedBy = principal.UserId,
            ReasonForChange = changeReason,
            VersionIndex = 1,
        };

        uow.Context.TicketComments.Add(comment);
        AuditActions.Record(uow.Context, new AuditAction(
            EntityName: nameof(TicketComment),
            EntityId: comment.Id,
            Action: "TICKET_COMMENT_ADDED",
            UserId: principal.UserId,
            ReasonForChange: changeReason,
            ChangesJson: JsonSerializer.Serialize(new
            {
                ticket_id = ticket.Id,
                reference = ticket.Reference,
                comment_id = comment.Id,
                visibility = comment.Visibility,
            })));
        await uow.CommitAsync(ct).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/tickets/{ticket.Id}/comments/{comment.Id}",
            ToCommentDto(comment));
    }

    public async Task<IResult> TransitionTicketAsync(
        string ticketId,
        TransitionTicketRequestDto request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            uow,
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

        string? normalizedResolutionCode = NormalizeOptional(request.ResolutionCode)?.ToUpperInvariant();
        if (normalizedResolutionCode is not null && !ResolutionCodeRegex().IsMatch(normalizedResolutionCode))
        {
            return Results.Problem(
                title: "Invalid resolution code",
                detail: $"Unsupported resolution_code '{normalizedResolutionCode}'. Use an uppercase code identifier (2-100 characters).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? normalizedRootCauseCategory = NormalizeOptional(request.RootCauseCategory)?.ToUpperInvariant();
        if (normalizedRootCauseCategory is not null && !ValidRootCauseCategories.Contains(normalizedRootCauseCategory))
        {
            return Results.Problem(
                title: "Invalid root cause category",
                detail: $"Unsupported root_cause_category '{normalizedRootCauseCategory}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? normalizedRootCauseSummary = NormalizeOptional(request.RootCauseSummary);
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

        string? normalizedAssigneeUser = NormalizeOptional(request.AssigneeUser);
        if (normalizedAssigneeUser is not null)
        {
            ticket.AssigneeUser = normalizedAssigneeUser;
        }

        int baselineVersion = uow.Context.Entry(ticket).Property(x => x.VersionIndex).OriginalValue;
        DateTimeOffset now = clock.UtcNow;
        ApplySlaPauseAndResume(ticket, previousStatus, ticket.Status, now);
        ticket.ReasonForChange = changeReason;
        ticket.VersionIndex = baselineVersion + 1;

        uow.Context.TicketRevisions.Add(TicketRevision.FromTicket(ticket, now, principal.UserId));

        AuditActions.Record(uow.Context, new AuditAction(
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
        await uow.CommitAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(ToResponseDto(ticket));
    }

    public async Task<IResult> SignTicketAsync(
        string ticketId,
        SignTicketRequestDto request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            uow,
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

        int baselineVersion = uow.Context.Entry(ticket).Property(x => x.VersionIndex).OriginalValue;
        DateTimeOffset now = clock.UtcNow;
        string? verifiedTokenId = httpContext.VerifiedSigToken()?.Id;
        ticket.SignatureAction = signatureAction;
        ticket.SignatureMeaning = meaning;
        ticket.SignatureToken = string.IsNullOrWhiteSpace(verifiedTokenId) ? "verified" : verifiedTokenId;
        ticket.SignatureTimestamp = now;
        ticket.SignatureUser = principal.UserId;
        ticket.ReasonForChange = changeReason;
        ticket.VersionIndex = baselineVersion + 1;

        uow.Context.TicketRevisions.Add(TicketRevision.FromTicket(ticket, now, principal.UserId));

        AuditActions.Record(uow.Context, new AuditAction(
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
        await uow.CommitAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(ToResponseDto(ticket));
    }

    public async Task<IResult> GetTicketAuditTrailAsync(
        string ticketId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            uow,
            asNoTracking: true,
            ct).ConfigureAwait(false);
        if (errorResult is not null || principal is null || ticket is null)
        {
            return errorResult!;
        }

        bool restrictInternal = IsSiteRestrictedCommentCaller(principal);

        var comments = await uow.Context.TicketComments
            .AsNoTracking()
            .Where(c => c.TicketId == ticket.Id)
            .Select(c => new { c.Id, c.Visibility })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var visibleCommentIds = comments
            .Where(c => !restrictInternal || string.Equals(c.Visibility, "PUBLIC", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);

        var rawAuditEntries = await uow.Context.AuditLogEntries
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
