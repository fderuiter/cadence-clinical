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
using Cadence.Database.Concurrency;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using static Cadence.Apps.Tickets.Application.TicketAccessAndMapping;

namespace Cadence.Apps.Tickets.Application;

/// <summary>
/// Application service owning support ticket creation, scoped listing, temporal lookup, and amendments.
/// </summary>
public sealed class TicketCrudApplicationService(TicketsUnitOfWork uow, IClock clock)
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

        string? normalizedSubject = NormalizeOptional(request.SubjectCode);
        string? normalizedEntityType = NormalizeOptional(request.EntityType)?.ToUpperInvariant();
        string? normalizedEntityId = NormalizeOptional(request.EntityId);
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

        string? requestedStudy = NormalizeOptional(request.StudyScope);
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
            effectiveStudy = ResolveDefaultStudyScope(principal);
        }

        string? requestedSite = NormalizeOptional(request.SiteScope);
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
        string reference = GenerateTicketReference(now);

        var contextMetadata = new StoredTicketContext(
            AssignedQueue: assignedQueue,
            AcknowledgeTargetAt: sla.AcknowledgeTargetAt,
            SourceRoute: NormalizeOptional(request.SourceRoute),
            Persona: NormalizeOptional(request.Persona),
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

        uow.Context.Tickets.Add(ticket);
        uow.Context.TicketRevisions.Add(TicketRevision.FromTicket(ticket, now, principal.UserId));
        AuditActions.Record(uow.Context, new AuditAction(
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
        await uow.CommitAsync(ct).ConfigureAwait(false);

        TicketResponseDto dto = ToResponseDto(ticket);
        return TypedResults.Created($"/api/v1/tickets/{ticket.Id}", dto);
    }

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

        string? normStudy = NormalizeOptional(requestedStudy);
        if (normStudy is not null && ClinicalScopeGuard.RequireStudy(httpContext, normStudy) is { } studyDenied)
        {
            return studyDenied;
        }

        string? normSite = NormalizeOptional(requestedSite);
        if (normSite is not null && ClinicalScopeGuard.RequireSite(httpContext, normSite) is { } siteDenied)
        {
            return siteDenied;
        }

        IQueryable<Ticket> query = uow.Context.Tickets
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
            .Where(t => IsTicketVisibleToCaller(principal, t))
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Reference, StringComparer.Ordinal)
            .Select(ToResponseDto)
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
        CancellationToken ct,
        int? revision = null,
        DateTimeOffset? asOf = null)
    {
        var (_, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticketId,
            httpContext,
            uow,
            asNoTracking: true,
            ct).ConfigureAwait(false);
        if (errorResult is not null || ticket is null)
        {
            return errorResult!;
        }

        if (revision.HasValue || asOf.HasValue)
        {
            IQueryable<TicketRevision> revQuery = uow.Context.TicketRevisions
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

            return TypedResults.Ok(ToResponseDto(Ticket.ReconstructFromRevision(ticket, matchedRevision)));
        }

        return TypedResults.Ok(ToResponseDto(ticket));
    }

    public async Task<IResult> UpdateTicketAsync(
        string ticketId,
        UpdateTicketRequestDto request,
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
                NormalizeOptional(request.ResolutionCode),
                NormalizeOptional(request.RootCauseSummary),
                NormalizeOptional(request.RootCauseCategory));
            if (transitionResult.IsFailure)
            {
                return Results.Problem(
                    title: "Invalid ticket transition",
                    detail: transitionResult.Error?.Description,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        StandaloneConcurrencyTokenInterceptor.EnforceExpectedVersion(
            uow.Context,
            ticket,
            request.ExpectedVersionIndex,
            httpContext);

        int baselineVersion = uow.Context.Entry(ticket).Property(x => x.VersionIndex).OriginalValue;
        DateTimeOffset now = clock.UtcNow;

        ticket.Title = nextTitle;
        ticket.Description = nextDescription;
        ticket.Priority = nextPriority;
        if (request.AssigneeUser is not null)
        {
            ticket.AssigneeUser = NormalizeOptional(request.AssigneeUser);
        }
        ticket.ReasonForChange = changeReason;
        ticket.VersionIndex = baselineVersion + 1;

        uow.Context.TicketRevisions.Add(TicketRevision.FromTicket(ticket, now, principal.UserId));
        AuditActions.Record(uow.Context, new AuditAction(
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
        await uow.CommitAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(ToResponseDto(ticket));
    }
}
