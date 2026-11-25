using System;
using System.Collections.Generic;
using System.Linq;
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
using Cadence.Database.Concurrency;
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

public sealed record UpdateTicketRequestDto(
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("priority")] string? Priority = null,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("assignee_user")] string? AssigneeUser = null,
    [property: JsonPropertyName("resolution_code")] string? ResolutionCode = null,
    [property: JsonPropertyName("root_cause_summary")] string? RootCauseSummary = null,
    [property: JsonPropertyName("root_cause_category")] string? RootCauseCategory = null,
    [property: JsonPropertyName("expected_version_index")] int? ExpectedVersionIndex = null);

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
    [property: JsonPropertyName("due_date")] DateTimeOffset? DueDate,
    [property: JsonPropertyName("acknowledge_target_at")] DateTimeOffset? AcknowledgeTargetAt,
    [property: JsonPropertyName("sla_target_at")] DateTimeOffset? SlaTargetAt,
    [property: JsonPropertyName("sla_paused_at")] DateTimeOffset? SlaPausedAt,
    [property: JsonPropertyName("sla_total_paused_seconds")] int SlaTotalPausedSeconds,
    [property: JsonPropertyName("sla_breached")] bool SlaBreached,
    [property: JsonPropertyName("sla_amber_warned")] bool SlaAmberWarned,
    [property: JsonPropertyName("escalation_count")] int EscalationCount,
    [property: JsonPropertyName("resolution_code")] string? ResolutionCode,
    [property: JsonPropertyName("root_cause_category")] string? RootCauseCategory,
    [property: JsonPropertyName("root_cause_summary")] string? RootCauseSummary,
    [property: JsonPropertyName("signature_action")] string? SignatureAction,
    [property: JsonPropertyName("signature_token")] string? SignatureToken,
    [property: JsonPropertyName("signature_meaning")] string? SignatureMeaning,
    [property: JsonPropertyName("signature_timestamp")] DateTimeOffset? SignatureTimestamp,
    [property: JsonPropertyName("signature_user")] string? SignatureUser,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("version_index")] int VersionIndex);

public sealed record CreateTicketCommentRequestDto(
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("visibility")] string? Visibility = "PUBLIC");

public sealed record TicketCommentResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ticket_id")] string TicketId,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("version_index")] int VersionIndex);

public sealed record TransitionTicketRequestDto(
    [property: JsonPropertyName("target_status")] string TargetStatus,
    [property: JsonPropertyName("resolution_code")] string? ResolutionCode = null,
    [property: JsonPropertyName("root_cause_summary")] string? RootCauseSummary = null,
    [property: JsonPropertyName("root_cause_category")] string? RootCauseCategory = null,
    [property: JsonPropertyName("assignee_user")] string? AssigneeUser = null);

public sealed record SignTicketRequestDto(
    [property: JsonPropertyName("signature_meaning")] string SignatureMeaning,
    [property: JsonPropertyName("signature_action")] string? SignatureAction = "SIGN_CLOSURE");

public sealed record TicketAuditEntryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("entity_name")] string EntityName,
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("version_index")] long VersionIndex,
    [property: JsonPropertyName("changes_json")] string? ChangesJson,
    [property: JsonPropertyName("predecessor_hash")] string PredecessorHash,
    [property: JsonPropertyName("block_hash")] string BlockHash);

internal sealed record StoredTicketContext(
    [property: JsonPropertyName("assigned_queue")] string? AssignedQueue,
    [property: JsonPropertyName("acknowledge_target_at")] DateTimeOffset? AcknowledgeTargetAt,
    [property: JsonPropertyName("source_route")] string? SourceRoute,
    [property: JsonPropertyName("persona")] string? Persona,
    [property: JsonPropertyName("subject_code")] string? SubjectCode);

/// <summary>
/// Minimal API endpoints for audited support ticket creation, scoped listing, reference lookup,
/// comments, workflow transitions, 21 CFR Part 11 electronic signatures, and audit trails
/// (PRD-TCK-001, PRD-TCK-002, ADR-2610, SRS-SYS-005).
/// @req:PRD-TCK-001
/// @req:PRD-TCK-002
/// @req:PRD-SYS-192
/// @req:SRS-SYS-005
/// </summary>
public static partial class TicketEndpoints
{
    public const string SignTicketSemanticAction = "tickets.ticket.sign";

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Z0-9][A-Z0-9_-]{1,99}$")]
    private static partial System.Text.RegularExpressions.Regex ResolutionCodeRegex();

    private static readonly HashSet<string> ValidCommentVisibilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "PUBLIC",
        "INTERNAL_SPONSOR",
    };

    private static readonly HashSet<string> ValidRootCauseCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROCESS_WORKFLOW_DEFICIT",
        "SITE_TRAINING_GAP",
        "DATA_ENTRY_ERROR",
        "SYSTEM_CONFIGURATION",
        "EQUIPMENT_CALIBRATION",
        "HUMAN_ERROR",
        "SYSTEM_DEFECT",
        "DATA_DISCREPANCY",
        "PROTOCOL_CLARIFICATION",
        "ACCESS_PROVISIONING",
        "INTEGRATION_LATENCY",
        "TRAINING_GAP",
        "NOT_APPLICABLE",
    };

    private static readonly HashSet<TicketStatus> SlaPausedStatuses =
    [
        TicketStatus.WAITING_ON_SITE,
        TicketStatus.WAITING_ON_SPONSOR,
        TicketStatus.PENDING_REGULATORY_REVIEW,
    ];

    private static readonly HashSet<string> SponsorCommentCapableRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNormalization.ROLE_SYSADMIN,
        RoleNormalization.ROLE_SPONSOR_DESIGNER,
        RoleNormalization.ROLE_SPONSOR_DM,
        RoleNormalization.ROLE_SPONSOR_MM,
        RoleNormalization.ROLE_SPONSOR_STATISTICIAN,
        RoleNormalization.ROLE_CRA_CANONICAL,
        "monitor",
        RoleNormalization.ROLE_EXTERNAL_MONITOR,
        RoleNormalization.ROLE_REVIEWER,
        RoleNormalization.ROLE_FINANCE_OFFICER,
        RoleNormalization.ROLE_AUDITOR_CANONICAL,
        "tmf_auditor",
        "tmf auditor",
        RoleNormalization.ROLE_UNBLINDED_STATISTICIAN,
        RoleNormalization.ROLE_IDMC,
        RoleNormalization.ROLE_EMERGENCY_UNBLINDER,
    };

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/tickets")
            .WithTags("Tickets")
            .RequireCallerStudyAndSiteScope();

        group.MapPost("", CreateTicketAsync)
            .WithName("CreateTicket")
            .WithSummary("Creates a GxP support or operational issue ticket with SLA targets and hash-chained audit entry.")
            .Produces<TicketResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("tickets:create")
            .WithChangeReasonHeader(required: true)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapGet("", ListTicketsAsync)
            .WithName("ListTickets")
            .WithSummary("Lists operational tickets filtered by study, site, status, priority, or queue.")
            .Produces<IReadOnlyList<TicketResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("tickets:read");

        group.MapGet("/{ticket_id}", GetTicketAsync)
            .WithName("GetTicket")
            .WithSummary("Retrieves a single operational ticket by ID or reference code.")
            .Produces<TicketResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("tickets:read");

        group.MapPatch("/{ticket_id}", UpdateTicketAsync)
            .WithName("UpdateTicket")
            .WithSummary("Updates mutable ticket fields with optimistic concurrency and Part 11 change reason.")
            .Produces<TicketResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("tickets:create")
            .WithChangeReasonHeader(required: true)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapGet("/{ticket_id}/comments", ListTicketCommentsAsync)
            .WithName("ListTicketComments")
            .WithSummary("Lists role-filtered public and sponsor-internal comments on a ticket.")
            .Produces<IReadOnlyList<TicketCommentResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("tickets:read");

        group.MapPost("/{ticket_id}/comments", CreateTicketCommentAsync)
            .WithName("CreateTicketComment")
            .WithSummary("Appends a visibility-scoped comment to an operational ticket.")
            .Produces<TicketCommentResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("tickets:comment")
            .WithChangeReasonHeader(required: true)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{ticket_id}/transitions", TransitionTicketAsync)
            .WithName("TransitionTicket")
            .WithSummary("Transitions a ticket across workflow states and updates SLA pause timers.")
            .Produces<TicketResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("tickets:transition")
            .WithChangeReasonHeader(required: true)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{ticket_id}/sign", SignTicketAsync)
            .WithName("SignTicket")
            .WithSummary("Applies a 21 CFR Part 11 step-up electronic signature to close or attest a GxP ticket.")
            .Produces<TicketResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("tickets:sign")
            .RequireElectronicSignature(SignTicketSemanticAction, SignTicketActionFor)
            .WithChangeReasonHeader(required: true)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapGet("/{ticket_id}/audit-trail", GetTicketAuditTrailAsync)
            .WithName("GetTicketAuditTrail")
            .WithSummary("Retrieves the hash-chained GxP audit trail for an operational ticket.")
            .Produces<IReadOnlyList<TicketAuditEntryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("tickets:read");
        return app;
    }

    public static string SignTicketAction(string ticketId) => $"/api/v1/tickets/{ticketId}/sign";

    private static string SignTicketActionFor(HttpContext httpContext) =>
        SignTicketAction(httpContext.Request.RouteValues["ticket_id"]?.ToString() ?? string.Empty);

    private static async Task<IResult> CreateTicketAsync(
        CreateTicketRequestDto request,
        HttpContext httpContext,
        TicketsDbContext db,
        IClock clock,
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

        // Enforce and record caller study and site scope.
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

    private static async Task<IResult> GetTicketAsync(
        string ticket_id,
        HttpContext httpContext,
        TicketsDbContext db,
        CancellationToken ct,
        [FromQuery(Name = "revision")] int? revision = null,
        [FromQuery(Name = "as_of")] DateTimeOffset? asOf = null)
    {
        var (_, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticket_id,
            httpContext,
            db,
            asNoTracking: true,
            ct).ConfigureAwait(false);
        if (errorResult is not null || ticket is null)
        {
            return errorResult!;
        }

        if (revision.HasValue || asOf.HasValue)
        {
            IQueryable<TicketRevision> revQuery = db.TicketRevisions
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
                    detail: $"No historical revision matched for ticket '{ticket_id.Trim()}'.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return TypedResults.Ok(ToResponseDto(Ticket.ReconstructFromRevision(ticket, matchedRevision)));
        }

        return TypedResults.Ok(ToResponseDto(ticket));
    }

    private static async Task<IResult> UpdateTicketAsync(
        string ticket_id,
        UpdateTicketRequestDto request,
        HttpContext httpContext,
        TicketsDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticket_id,
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
            ticket.AssigneeUser = NormalizeOptional(request.AssigneeUser);
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(ToResponseDto(ticket));
    }

    private static async Task<IResult> ListTicketCommentsAsync(
        string ticket_id,
        HttpContext httpContext,
        TicketsDbContext db,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticket_id,
            httpContext,
            db,
            asNoTracking: true,
            ct).ConfigureAwait(false);
        if (errorResult is not null || principal is null || ticket is null)
        {
            return errorResult!;
        }

        bool restrictInternal = IsSiteRestrictedCommentCaller(principal);

        var rawComments = await db.TicketComments
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

    private static async Task<IResult> CreateTicketCommentAsync(
        string ticket_id,
        CreateTicketCommentRequestDto request,
        HttpContext httpContext,
        TicketsDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticket_id,
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/tickets/{ticket.Id}/comments/{comment.Id}",
            ToCommentDto(comment));
    }

    private static async Task<IResult> TransitionTicketAsync(
        string ticket_id,
        TransitionTicketRequestDto request,
        HttpContext httpContext,
        TicketsDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticket_id,
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

        int baselineVersion = db.Entry(ticket).Property(x => x.VersionIndex).OriginalValue;
        DateTimeOffset now = clock.UtcNow;
        ApplySlaPauseAndResume(ticket, previousStatus, ticket.Status, now);
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(ToResponseDto(ticket));
    }

    private static async Task<IResult> SignTicketAsync(
        string ticket_id,
        SignTicketRequestDto request,
        HttpContext httpContext,
        TicketsDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticket_id,
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(ToResponseDto(ticket));
    }

    private static async Task<IResult> GetTicketAuditTrailAsync(
        string ticket_id,
        HttpContext httpContext,
        TicketsDbContext db,
        CancellationToken ct)
    {
        var (principal, ticket, errorResult) = await LoadAuthorizedTicketAsync(
            ticket_id,
            httpContext,
            db,
            asNoTracking: true,
            ct).ConfigureAwait(false);
        if (errorResult is not null || principal is null || ticket is null)
        {
            return errorResult!;
        }

        bool restrictInternal = IsSiteRestrictedCommentCaller(principal);

        var comments = await db.TicketComments
            .AsNoTracking()
            .Where(c => c.TicketId == ticket.Id)
            .Select(c => new { c.Id, c.Visibility })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var visibleCommentIds = comments
            .Where(c => !restrictInternal || string.Equals(c.Visibility, "PUBLIC", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);

        var rawAuditEntries = await db.AuditLogEntries
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

    private static async Task<(Principal? Principal, Ticket? Ticket, IResult? ErrorResult)> LoadAuthorizedTicketAsync(
        string rawTicketId,
        HttpContext httpContext,
        TicketsDbContext db,
        bool asNoTracking,
        CancellationToken ct)
    {
        Principal? principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (principal is null || string.IsNullOrWhiteSpace(principal.UserId))
        {
            return (null, null, Results.Problem(
                title: "Unauthorized caller",
                detail: "Verified gateway caller identity is required.",
                statusCode: StatusCodes.Status401Unauthorized));
        }

        if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext) is { } callerScopeDenied)
        {
            return (null, null, callerScopeDenied);
        }

        string key = (rawTicketId ?? string.Empty).Trim();
        IQueryable<Ticket> query = asNoTracking ? db.Tickets.AsNoTracking() : db.Tickets;
        Ticket? ticket = await query
            .FirstOrDefaultAsync(t => !t.IsDeleted && (t.Id == key || t.Reference == key), ct)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return (principal, null, Results.Problem(
                title: "Ticket not found",
                detail: $"No support ticket matched '{key}'.",
                statusCode: StatusCodes.Status404NotFound));
        }

        if (!IsTicketVisibleToCaller(principal, ticket))
        {
            return (principal, null, ClinicalScopeGuard.Forbidden(
                detail: "Caller is not authorized for this ticket's study or site scope.",
                title: "Ticket scope forbidden"));
        }

        return (principal, ticket, null);
    }

    private static void ApplySlaPauseAndResume(
        Ticket ticket,
        TicketStatus previousStatus,
        TicketStatus nextStatus,
        DateTimeOffset now)
    {
        if (previousStatus == nextStatus)
        {
            return;
        }

        bool wasPaused = SlaPausedStatuses.Contains(previousStatus);
        bool willPause = SlaPausedStatuses.Contains(nextStatus);

        if (!wasPaused && willPause)
        {
            ticket.SlaPausedAt = now;
        }
        else if (wasPaused && !willPause)
        {
            if (ticket.SlaPausedAt is DateTimeOffset pausedAt)
            {
                int pausedSeconds = (int)Math.Max(0, (now - pausedAt).TotalSeconds);
                ticket.SlaTotalPausedSeconds += pausedSeconds;
                if (ticket.SlaTargetAt is DateTimeOffset slaTarget)
                {
                    ticket.SlaTargetAt = slaTarget.AddSeconds(pausedSeconds);
                }

                if (ticket.DueDate is DateTimeOffset dueDate)
                {
                    ticket.DueDate = dueDate.AddSeconds(pausedSeconds);
                }
            }

            ticket.SlaPausedAt = null;
        }

        if (ticket.SlaTargetAt is DateTimeOffset effectiveTarget && !SlaPausedStatuses.Contains(nextStatus))
        {
            ticket.SlaBreached = now > effectiveTarget && !TicketTransitionGraph.TerminalStates.Contains(nextStatus);
        }
    }

    private static bool IsSiteRestrictedCommentCaller(Principal principal)
    {
        var normalized = RoleNormalization.GetNormalizedRoles(principal.Roles.Concat(principal.RawRoles));
        return !normalized.Any(SponsorCommentCapableRoles.Contains);
    }

    private static TicketCommentResponseDto ToCommentDto(TicketComment comment) =>
        new(
            Id: comment.Id,
            TicketId: comment.TicketId,
            Body: comment.Body,
            Visibility: comment.Visibility,
            CreatedAt: comment.CreatedAt,
            CreatedBy: comment.CreatedBy,
            ReasonForChange: comment.ReasonForChange,
            VersionIndex: comment.VersionIndex);

    private static bool IsTicketVisibleToCaller(Principal principal, Ticket ticket)
    {
        if (!string.IsNullOrWhiteSpace(ticket.StudyId))
        {
            if (!StudyAccessEvaluator.CanAccessStudy(principal, ticket.StudyId))
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
        else if (ClinicalScopeGuard.IsSiteBound(principal)
                 && !string.Equals(ticket.Reporter, principal.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool HasAllStudies(Principal principal) =>
        principal.AssignedStudies.Any(s => string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal));

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
            DueDate: ticket.DueDate,
            AcknowledgeTargetAt: ctx?.AcknowledgeTargetAt,
            SlaTargetAt: ticket.SlaTargetAt,
            SlaPausedAt: ticket.SlaPausedAt,
            SlaTotalPausedSeconds: ticket.SlaTotalPausedSeconds,
            SlaBreached: ticket.SlaBreached,
            SlaAmberWarned: ticket.SlaAmberWarned,
            EscalationCount: ticket.EscalationCount,
            ResolutionCode: ticket.ResolutionCode,
            RootCauseCategory: ticket.RootCauseCategory,
            RootCauseSummary: ticket.RootCauseSummary,
            SignatureAction: ticket.SignatureAction,
            SignatureToken: ticket.SignatureToken,
            SignatureMeaning: ticket.SignatureMeaning,
            SignatureTimestamp: ticket.SignatureTimestamp,
            SignatureUser: ticket.SignatureUser,
            ReasonForChange: ticket.ReasonForChange,
            CreatedAt: ticket.CreatedAt,
            UpdatedAt: ticket.SignatureTimestamp ?? ticket.CreatedAt,
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
