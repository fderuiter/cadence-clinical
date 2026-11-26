using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Concurrency;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Tickets.Application;

public interface ITicketAuthoringService
{
    Task<IResult> CreateTicketAsync(
        CreateTicketRequestDto request,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> UpdateTicketAsync(
        string ticketId,
        UpdateTicketRequestDto request,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> CreateTicketCommentAsync(
        string ticketId,
        CreateTicketCommentRequestDto request,
        HttpContext httpContext,
        CancellationToken ct);
}

public sealed class TicketAuthoringService(
    TicketsDbContext db,
    ITicketsUnitOfWork unitOfWork,
    IClock clock) : ITicketAuthoringService
{
    public async Task<IResult> CreateTicketAsync(
        CreateTicketRequestDto request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        Principal? principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (principal is null || string.IsNullOrWhiteSpace(principal.UserId))
        {
            return Results.Problem(
                title: "Unauthorized caller",
                detail: "Verified gateway caller identity is required to create a support ticket.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext) is { } callerScopeDenied)
        {
            return callerScopeDenied;
        }

        string changeReason = ChangeReasonHeader.Read(httpContext.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter must guarantee a non-empty X-Change-Reason.");

        string title = (request.Title ?? string.Empty).Trim();
        string description = (request.Description ?? string.Empty).Trim();
        if (title.Length == 0 || description.Length == 0)
        {
            return Results.Problem(
                title: "Invalid ticket payload",
                detail: "Both 'title' and 'description' are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (title.Length > 255)
        {
            return Results.Problem(
                title: "Invalid ticket title",
                detail: "Ticket 'title' must not exceed 255 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string category = string.IsNullOrWhiteSpace(request.Category)
            ? "SYSTEM_SUPPORT"
            : request.Category.Trim().ToUpperInvariant();
        if (!TicketRoutingAndSlaEngine.ValidCategories.Contains(category))
        {
            return Results.Problem(
                title: "Invalid ticket category",
                detail: $"Unsupported category '{category}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string severity = string.IsNullOrWhiteSpace(request.GxpSeverity)
            ? "MINOR"
            : request.GxpSeverity.Trim().ToUpperInvariant();
        if (!TicketRoutingAndSlaEngine.ValidSeverities.Contains(severity))
        {
            return Results.Problem(
                title: "Invalid GxP severity",
                detail: $"Unsupported gxp_severity '{severity}'. Expected MINOR, MAJOR, or CRITICAL.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string priority = string.IsNullOrWhiteSpace(request.Priority)
            ? "MEDIUM"
            : request.Priority.Trim().ToUpperInvariant();
        if (!TicketRoutingAndSlaEngine.ValidPriorities.Contains(priority))
        {
            return Results.Problem(
                title: "Invalid ticket priority",
                detail: $"Unsupported priority '{priority}'. Expected LOW, MEDIUM, HIGH, or CRITICAL.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? normalizedSubject = TicketScopeAndMappingHelpers.NormalizeOptional(request.SubjectCode);
        string? normalizedEntityType = TicketScopeAndMappingHelpers.NormalizeOptional(request.EntityType)?.ToUpperInvariant();
        string? normalizedEntityId = TicketScopeAndMappingHelpers.NormalizeOptional(request.EntityId);
        if (normalizedEntityType is null && normalizedSubject is not null)
        {
            normalizedEntityType = "SUBJECT";
            normalizedEntityId = normalizedSubject;
        }

        string? piiViolation = TicketPiiValidator.ValidateNoDirectParticipantPii(
            title,
            description,
            normalizedSubject,
            normalizedEntityType,
            normalizedEntityId,
            changeReason);
        if (piiViolation is not null)
        {
            return Results.Problem(
                title: "Direct participant PII rejected",
                detail: piiViolation,
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? requestedStudy = TicketScopeAndMappingHelpers.NormalizeOptional(request.StudyScope);
        string? effectiveStudy;
        if (requestedStudy is not null)
        {
            if (ClinicalScopeGuard.RequireStudy(httpContext, requestedStudy) is { } studyDenied)
            {
                return studyDenied;
            }

            effectiveStudy = requestedStudy;
        }
        else
        {
            effectiveStudy = TicketScopeAndMappingHelpers.ResolveDefaultStudyScope(principal);
        }

        string? requestedSite = TicketScopeAndMappingHelpers.NormalizeOptional(request.SiteScope);
        string? effectiveSite;
        if (requestedSite is not null)
        {
            if (ClinicalScopeGuard.RequireSite(httpContext, requestedSite) is { } siteDenied)
            {
                return siteDenied;
            }

            effectiveSite = requestedSite;
        }
        else
        {
            effectiveSite = principal.AssignedSites.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        }

        DateTimeOffset now = clock.UtcNow;
        string assignedQueue = TicketRoutingAndSlaEngine.ResolveQueue(category, request.SourceRoute, normalizedEntityType);
        var sla = TicketRoutingAndSlaEngine.ComputeSlaTargets(severity, priority, clock);

        string ticketId = Guid.NewGuid().ToString("D");
        string reference = TicketScopeAndMappingHelpers.GenerateTicketReference(now);

        var contextMetadata = new StoredTicketContext(
            AssignedQueue: assignedQueue,
            AcknowledgeTargetAt: sla.AcknowledgeTargetAt,
            SourceRoute: TicketScopeAndMappingHelpers.NormalizeOptional(request.SourceRoute),
            Persona: TicketScopeAndMappingHelpers.NormalizeOptional(request.Persona),
            SubjectCode: normalizedSubject);

        var ticket = new Ticket
        {
            Id = ticketId,
            Reference = reference,
            Title = title,
            Description = description,
            Category = category,
            Priority = priority,
            GxpSeverity = severity,
            Reporter = principal.UserId,
            AssigneeRole = assignedQueue,
            StudyId = effectiveStudy,
            SiteId = effectiveSite,
            RelatedEntityType = normalizedEntityType,
            RelatedEntityId = normalizedEntityId,
            ContextPayload = JsonSerializer.Serialize(contextMetadata),
            SlaTargetAt = sla.ResolutionTargetAt,
            DueDate = sla.ResolutionTargetAt,
            SlaBreached = false,
            SlaAmberWarned = false,
            EscalationCount = 0,
            IsDeleted = false,
            CreatedAt = now,
            CreatedBy = principal.UserId,
            ReasonForChange = changeReason,
            VersionIndex = 1,
        };

        db.Tickets.Add(ticket);
        db.TicketRevisions.Add(TicketRevision.FromTicket(ticket, now, principal.UserId));
        AuditActions.Record(db, new AuditAction(
            EntityName: nameof(Ticket),
            EntityId: ticket.Id,
            Action: "TICKET_CREATED",
            UserId: principal.UserId,
            ReasonForChange: changeReason,
            ChangesJson: JsonSerializer.Serialize(new
            {
                reference = ticket.Reference,
                category = ticket.Category,
                priority = ticket.Priority,
                gxp_severity = ticket.GxpSeverity,
                assigned_queue = assignedQueue,
                study_id = effectiveStudy,
                site_id = effectiveSite,
                acknowledge_target_at = sla.AcknowledgeTargetAt,
                sla_target_at = sla.ResolutionTargetAt,
            })));
        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        TicketResponseDto dto = TicketScopeAndMappingHelpers.ToResponseDto(ticket);
        return TypedResults.Created($"/api/v1/tickets/{ticket.Id}", dto);
    }

    public async Task<IResult> UpdateTicketAsync(
        string ticketId,
        UpdateTicketRequestDto request,
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

        if (ticket.SignatureTimestamp is not null)
        {
            return Results.Problem(
                title: "Ticket locked by 21 CFR Part 11 signature",
                detail: "This support ticket has been electronically signed and locked against further amendments.",
                statusCode: StatusCodes.Status409Conflict);
        }

        string nextTitle = request.Title is null ? ticket.Title : request.Title.Trim();
        string nextDescription = request.Description is null ? ticket.Description : request.Description.Trim();
        if (nextTitle.Length == 0 || nextDescription.Length == 0)
        {
            return Results.Problem(
                title: "Invalid ticket payload",
                detail: "Both 'title' and 'description' must remain non-empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (nextTitle.Length > 255)
        {
            return Results.Problem(
                title: "Invalid ticket title",
                detail: "Ticket 'title' must not exceed 255 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string nextPriority = string.IsNullOrWhiteSpace(request.Priority)
            ? ticket.Priority
            : request.Priority.Trim().ToUpperInvariant();
        if (!TicketRoutingAndSlaEngine.ValidPriorities.Contains(nextPriority))
        {
            return Results.Problem(
                title: "Invalid ticket priority",
                detail: $"Unsupported priority '{nextPriority}'. Expected LOW, MEDIUM, HIGH, or CRITICAL.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? piiViolation = TicketPiiValidator.ValidateNoDirectParticipantPii(
            nextTitle,
            nextDescription,
            null,
            ticket.RelatedEntityType,
            ticket.RelatedEntityId,
            changeReason);
        if (piiViolation is not null)
        {
            return Results.Problem(
                title: "Direct participant PII rejected",
                detail: piiViolation,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var transitionResult = ticket.TransitionTo(
                request.Status.Trim(),
                TicketScopeAndMappingHelpers.NormalizeOptional(request.ResolutionCode),
                TicketScopeAndMappingHelpers.NormalizeOptional(request.RootCauseSummary),
                TicketScopeAndMappingHelpers.NormalizeOptional(request.RootCauseCategory));
            if (transitionResult.IsFailure)
            {
                return Results.Problem(
                    title: "Invalid ticket transition",
                    detail: transitionResult.Error?.Description,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        StandaloneConcurrencyTokenInterceptor.EnforceExpectedVersion(
            db,
            ticket,
            request.ExpectedVersionIndex,
            httpContext);

        int baselineVersion = db.Entry(ticket).Property(x => x.VersionIndex).OriginalValue;
        DateTimeOffset now = clock.UtcNow;

        ticket.Title = nextTitle;
        ticket.Description = nextDescription;
        ticket.Priority = nextPriority;
        if (request.AssigneeUser is not null)
        {
            ticket.AssigneeUser = TicketScopeAndMappingHelpers.NormalizeOptional(request.AssigneeUser);
        }

        ticket.ReasonForChange = changeReason;
        ticket.VersionIndex = baselineVersion + 1;

        db.TicketRevisions.Add(TicketRevision.FromTicket(ticket, now, principal.UserId));
        AuditActions.Record(db, new AuditAction(
            EntityName: nameof(Ticket),
            EntityId: ticket.Id,
            Action: "TICKET_UPDATED",
            UserId: principal.UserId,
            ReasonForChange: changeReason,
            ChangesJson: JsonSerializer.Serialize(new
            {
                reference = ticket.Reference,
                status = ticket.Status.ToString(),
                priority = ticket.Priority,
                revision = ticket.VersionIndex,
            })));
        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(TicketScopeAndMappingHelpers.ToResponseDto(ticket));
    }

    public async Task<IResult> CreateTicketCommentAsync(
        string ticketId,
        CreateTicketCommentRequestDto request,
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
        if (!TicketScopeAndMappingHelpers.ValidCommentVisibilities.Contains(visibility))
        {
            return Results.Problem(
                title: "Invalid comment visibility",
                detail: $"Unsupported comment visibility '{visibility}'. Expected PUBLIC or INTERNAL_SPONSOR.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.Equals(visibility, "INTERNAL_SPONSOR", StringComparison.OrdinalIgnoreCase)
            && TicketScopeAndMappingHelpers.IsSiteRestrictedCommentCaller(principal))
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

        db.TicketComments.Add(comment);
        AuditActions.Record(db, new AuditAction(
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
        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/tickets/{ticket.Id}/comments/{comment.Id}",
            TicketScopeAndMappingHelpers.ToCommentDto(comment));
    }
}
