using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Application;
using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Endpoints.Filters;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Tickets.Endpoints;

/// <summary>
/// Request payload for <c>POST /api/v1/tickets</c>.
/// Note: C# record property names avoid raw <c>*StudyId</c>/<c>*SiteId</c>/<c>*ParticipantId</c> suffixes per K-XS-1
/// while preserving snake_case JSON field names on the wire. Caller identity is never accepted from the body.
/// </summary>
public sealed record CreateTicketRequestDto(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string? Category = "SYSTEM_SUPPORT",
    [property: JsonPropertyName("gxp_severity")] string? GxpSeverity = "MINOR",
    [property: JsonPropertyName("priority")] string? Priority = "MEDIUM",
    [property: JsonPropertyName("study_id")] string? StudyScope = null,
    [property: JsonPropertyName("site_id")] string? SiteScope = null,
    [property: JsonPropertyName("subject_id")] string? SubjectCode = null,
    [property: JsonPropertyName("entity_type")] string? EntityType = null,
    [property: JsonPropertyName("entity_id")] string? EntityId = null,
    [property: JsonPropertyName("source_route")] string? SourceRoute = null,
    [property: JsonPropertyName("persona")] string? Persona = null);

public sealed record TicketResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("gxp_severity")] string GxpSeverity,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("assigned_queue")] string AssignedQueue,
    [property: JsonPropertyName("assignee_role")] string? AssigneeRole,
    [property: JsonPropertyName("assignee_user")] string? AssigneeUser,
    [property: JsonPropertyName("reporter")] string Reporter,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("study_id")] string? StudyScope,
    [property: JsonPropertyName("site_id")] string? SiteScope,
    [property: JsonPropertyName("subject_id")] string? SubjectCode,
    [property: JsonPropertyName("related_entity_type")] string? RelatedEntityType,
    [property: JsonPropertyName("related_entity_id")] string? RelatedEntityId,
    [property: JsonPropertyName("source_route")] string? SourceRoute,
    [property: JsonPropertyName("acknowledge_target_at")] DateTimeOffset? AcknowledgeTargetAt,
    [property: JsonPropertyName("sla_target_at")] DateTimeOffset? SlaTargetAt,
    [property: JsonPropertyName("sla_breached")] bool SlaBreached,
    [property: JsonPropertyName("sla_amber_warned")] bool SlaAmberWarned,
    [property: JsonPropertyName("escalation_count")] int EscalationCount,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("version_index")] int VersionIndex);

internal sealed record StoredTicketContext(
    [property: JsonPropertyName("assigned_queue")] string? AssignedQueue,
    [property: JsonPropertyName("acknowledge_target_at")] DateTimeOffset? AcknowledgeTargetAt,
    [property: JsonPropertyName("source_route")] string? SourceRoute,
    [property: JsonPropertyName("persona")] string? Persona,
    [property: JsonPropertyName("subject_code")] string? SubjectCode);

/// <summary>
/// Minimal API endpoints for audited support ticket creation, scoped listing, and reference lookup
/// (PRD-TCK-001, PRD-TCK-002, ADR-2610).
/// @req:PRD-TCK-001
/// @req:PRD-TCK-002
/// @req:PRD-SYS-192
/// </summary>
public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/tickets")
            .WithTags("Tickets");

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

        return app;
    }

    private static async Task<IResult> CreateTicketAsync(
        CreateTicketRequestDto request,
        HttpContext httpContext,
        TicketsDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        Principal? principal = ResolvePrincipal(httpContext);
        if (principal is null || string.IsNullOrWhiteSpace(principal.UserId))
        {
            return Results.Problem(
                title: "Unauthorized caller",
                detail: "Verified gateway caller identity is required to create a support ticket.",
                statusCode: StatusCodes.Status401Unauthorized);
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

        // Enforce and record caller study and site scope.
        string? requestedStudy = NormalizeOptional(request.StudyScope);
        string? effectiveStudy;
        if (requestedStudy is not null)
        {
            if (!CanAccessStudy(httpContext, principal, requestedStudy))
            {
                return Results.Problem(
                    title: "Study scope forbidden",
                    detail: $"Caller is not authorized for study '{requestedStudy}'.",
                    statusCode: StatusCodes.Status403Forbidden);
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
            if (!SiteAccessEvaluator.CanAccessSite(principal, requestedSite))
            {
                return Results.Problem(
                    title: "Site scope forbidden",
                    detail: $"Caller is not authorized for site '{requestedSite}'.",
                    statusCode: StatusCodes.Status403Forbidden);
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

        db.Tickets.Add(ticket);
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        TicketResponseDto dto = ToResponseDto(ticket);
        return TypedResults.Created($"/api/v1/tickets/{ticket.Id}", dto);
    }

    private static async Task<IResult> ListTicketsAsync(
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
        TicketsDbContext db,
        CancellationToken ct)
    {
        Principal? principal = ResolvePrincipal(httpContext);
        if (principal is null || string.IsNullOrWhiteSpace(principal.UserId))
        {
            return Results.Problem(
                title: "Unauthorized caller",
                detail: "Verified gateway caller identity is required.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        string? normStudy = NormalizeOptional(requestedStudy);
        if (normStudy is not null && !CanAccessStudy(httpContext, principal, normStudy))
        {
            return Results.Problem(
                title: "Study scope forbidden",
                detail: $"Caller is not authorized for study '{normStudy}'.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        string? normSite = NormalizeOptional(requestedSite);
        if (normSite is not null && !SiteAccessEvaluator.CanAccessSite(principal, normSite))
        {
            return Results.Problem(
                title: "Site scope forbidden",
                detail: $"Caller is not authorized for site '{normSite}'.",
                statusCode: StatusCodes.Status403Forbidden);
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
            .Where(t => IsTicketVisibleToCaller(httpContext, principal, t))
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

    private static async Task<IResult> GetTicketAsync(
        string ticket_id,
        HttpContext httpContext,
        TicketsDbContext db,
        CancellationToken ct)
    {
        Principal? principal = ResolvePrincipal(httpContext);
        if (principal is null || string.IsNullOrWhiteSpace(principal.UserId))
        {
            return Results.Problem(
                title: "Unauthorized caller",
                detail: "Verified gateway caller identity is required.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        string key = ticket_id.Trim();
        Ticket? ticket = await db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && (t.Id == key || t.Reference == key), ct)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return Results.Problem(
                title: "Ticket not found",
                detail: $"No support ticket matched '{key}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!IsTicketVisibleToCaller(httpContext, principal, ticket))
        {
            return Results.Problem(
                title: "Ticket scope forbidden",
                detail: "Caller is not authorized for this ticket's study or site scope.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(ToResponseDto(ticket));
    }

    private static bool IsTicketVisibleToCaller(HttpContext httpContext, Principal principal, Ticket ticket)
    {
        if (!string.IsNullOrWhiteSpace(ticket.StudyId))
        {
            if (!CanAccessStudy(httpContext, principal, ticket.StudyId))
            {
                return false;
            }
        }
        else if (!HasAllStudies(principal)
                 && !string.Equals(ticket.Reporter, principal.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ticket.SiteId))
        {
            if (!SiteAccessEvaluator.CanAccessSite(principal, ticket.SiteId))
            {
                return false;
            }
        }
        else if (IsSiteBound(principal)
                 && !string.Equals(ticket.Reporter, principal.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static Principal? ResolvePrincipal(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (CallerScope.Principal(httpContext) is { } principal)
        {
            return principal;
        }

        ClaimsPrincipal user = httpContext.User;
        string? userId = user.FindFirst(CadenceClaimTypes.Subject)?.Value;
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var rawRoles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return new Principal
        {
            UserId = userId,
            Roles = RoleNormalization.GetNormalizedRoles(rawRoles),
            RawRoles = rawRoles,
            AssignedStudies = user.FindAll(CadenceClaimTypes.StudyIds).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList(),
            AssignedSites = user.FindAll(CadenceClaimTypes.SiteId).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList(),
        };
    }

    private static bool CanAccessStudy(HttpContext httpContext, Principal principal, string studyCode) =>
        CallerScope.Principal(httpContext) is not null
            ? CallerScope.CanAccessStudy(httpContext, studyCode)
            : StudyAccessEvaluator.CanAccessStudy(principal, studyCode);

    private static bool HasAllStudies(Principal principal) =>
        principal.AssignedStudies.Any(s => string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal));

    private static bool IsSiteBound(Principal principal) =>
        principal.Roles.Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains)
        || principal.AssignedSites.Count > 0;

    private static string? ResolveDefaultStudyScope(Principal principal)
    {
        var explicitStudies = principal.AssignedStudies
            .Where(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal))
            .ToList();
        return explicitStudies.Count == 1 ? explicitStudies[0] : null;
    }

    private static string GenerateTicketReference(DateTimeOffset timestamp)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
        return $"TKT-{timestamp.Year}-{token}";
    }

    private static TicketResponseDto ToResponseDto(Ticket ticket)
    {
        StoredTicketContext? ctx = null;
        if (!string.IsNullOrWhiteSpace(ticket.ContextPayload))
        {
            try
            {
                ctx = JsonSerializer.Deserialize<StoredTicketContext>(ticket.ContextPayload);
            }
            catch (JsonException)
            {
                // Non-structured context payloads remain gracefully ignored.
            }
        }

        string assignedQueue = ctx?.AssignedQueue
                               ?? ticket.AssigneeRole
                               ?? TicketRoutingAndSlaEngine.ResolveQueue(ticket.Category, ctx?.SourceRoute, ticket.RelatedEntityType);

        string? subjectCode = ctx?.SubjectCode
                              ?? (string.Equals(ticket.RelatedEntityType, "SUBJECT", StringComparison.OrdinalIgnoreCase)
                                  ? ticket.RelatedEntityId
                                  : null);

        return new TicketResponseDto(
            Id: ticket.Id,
            Reference: ticket.Reference,
            Title: ticket.Title,
            Description: ticket.Description,
            Category: ticket.Category,
            Priority: ticket.Priority,
            GxpSeverity: ticket.GxpSeverity,
            Status: ticket.Status.ToString(),
            AssignedQueue: assignedQueue,
            AssigneeRole: ticket.AssigneeRole,
            AssigneeUser: ticket.AssigneeUser,
            Reporter: ticket.Reporter,
            CreatedBy: ticket.CreatedBy,
            StudyScope: ticket.StudyId,
            SiteScope: ticket.SiteId,
            SubjectCode: subjectCode,
            RelatedEntityType: ticket.RelatedEntityType,
            RelatedEntityId: ticket.RelatedEntityId,
            SourceRoute: ctx?.SourceRoute,
            AcknowledgeTargetAt: ctx?.AcknowledgeTargetAt,
            SlaTargetAt: ticket.SlaTargetAt,
            SlaBreached: ticket.SlaBreached,
            SlaAmberWarned: ticket.SlaAmberWarned,
            EscalationCount: ticket.EscalationCount,
            ReasonForChange: ticket.ReasonForChange,
            CreatedAt: ticket.CreatedAt,
            VersionIndex: ticket.VersionIndex);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
