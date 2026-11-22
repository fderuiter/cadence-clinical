using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Knowledge.Endpoints;

public sealed record BatchAssignCurriculumRequest(
    [property: JsonPropertyName("curriculum_id")] string CurriculumId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_ids")] List<string>? SiteIds,
    [property: JsonPropertyName("roles")] List<string>? Roles,
    [property: JsonPropertyName("target_personnel_ids")] List<string>? TargetPersonnelIds,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange);

public sealed record CreateTrainingModuleRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("module_type")] string? ModuleType,
    [property: JsonPropertyName("content_text")] string ContentText,
    [property: JsonPropertyName("sequence_order")] int? SequenceOrder,
    [property: JsonPropertyName("min_reading_time_seconds")] int? MinReadingTimeSeconds);

public sealed record CreateTrainingCurriculumRequest(
    [property: JsonPropertyName("study_id")] string StudyCode,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("is_active")] bool? IsActive,
    [property: JsonPropertyName("modules")] List<CreateTrainingModuleRequest>? Modules,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange);

public sealed record CompleteTrainingAssignmentRequest(
    [property: JsonPropertyName("quiz_score")] double? QuizScore,
    [property: JsonPropertyName("time_spent_seconds")] int? TimeSpentSeconds,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange);

public sealed record TrainingModuleDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("curriculum_id")] string CurriculumId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("module_type")] string ModuleType,
    [property: JsonPropertyName("content_text")] string ContentText,
    [property: JsonPropertyName("sequence_order")] int SequenceOrder,
    [property: JsonPropertyName("min_reading_time_seconds")] int MinReadingTimeSeconds);

public sealed record TrainingAssignmentDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("curriculum_id")] string CurriculumId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string UserName,
    [property: JsonPropertyName("user_role")] string UserRole,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("batch_transaction_id")] string? BatchTransactionId,
    [property: JsonPropertyName("quiz_score")] double? QuizScore,
    [property: JsonPropertyName("time_spent_seconds")] int TimeSpentSeconds,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("curriculum")] TrainingCurriculumDto? Curriculum = null);

public sealed record SkippedAssignmentItemDto(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("user_role")] string UserRole,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record BatchAssignmentResponse(
    [property: JsonPropertyName("batch_transaction_id")] string BatchTransactionId,
    [property: JsonPropertyName("curriculum_id")] string CurriculumId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("total_eligible")] int TotalEligible,
    [property: JsonPropertyName("total_assigned")] int TotalAssigned,
    [property: JsonPropertyName("total_skipped")] int TotalSkipped,
    [property: JsonPropertyName("assignments")] List<TrainingAssignmentDto> Assignments,
    [property: JsonPropertyName("skipped_items")] List<SkippedAssignmentItemDto> SkippedItems,
    [property: JsonPropertyName("executed_at")] DateTimeOffset ExecutedAt);

/// <summary>
/// The meaning and reason of a delegation sign-off. The signer is never taken from the body: it is the verified
/// caller, and the credential check is the step-up token in <c>X-Sig-Token</c> (#5311).
/// </summary>
public sealed record SignaturePayloadDto(
    [property: JsonPropertyName("meaning")] string Meaning,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange);

public sealed record BulkDelegationSignOffRequest(
    [property: JsonPropertyName("delegation_ids")] List<string> DelegationIds,
    [property: JsonPropertyName("study_id")] string? StudyId,
    [property: JsonPropertyName("site_id")] string? SiteId,
    [property: JsonPropertyName("signature_payload")] SignaturePayloadDto SignaturePayload)
{
    /// <summary>
    /// The step-up token's <c>batch_id</c> for this sign-off: the lower-case hex SHA-256 of the trimmed delegation
    /// ids, sorted ordinally and joined with commas. The web client computes the same value.
    /// </summary>
    public string BatchBindingId() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join(",", (DelegationIds ?? []).Select(id => id.Trim()).Order(StringComparer.Ordinal)))));
}

public sealed record DelegationSummaryDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("staff_user_id")] string StaffUserId,
    [property: JsonPropertyName("staff_name")] string StaffName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("task_code")] string TaskCode,
    [property: JsonPropertyName("duties")] string Duties,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("signed_off")] bool SignedOff,
    [property: JsonPropertyName("pi_signature_hash")] string? PiSignatureHash,
    [property: JsonPropertyName("pi_approved_at")] DateTimeOffset? PiApprovedAt,
    [property: JsonPropertyName("batch_transaction_id")] string? BatchTransactionId);

public sealed record FailedDelegationItemDto(
    [property: JsonPropertyName("delegation_id")] string DelegationId,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record BulkDelegationSignOffResponse(
    [property: JsonPropertyName("batch_transaction_id")] string BatchTransactionId,
    [property: JsonPropertyName("total_requested")] int TotalRequested,
    [property: JsonPropertyName("total_signed")] int TotalSigned,
    [property: JsonPropertyName("total_failed")] int TotalFailed,
    [property: JsonPropertyName("signed_delegations")] List<DelegationSummaryDto> SignedDelegations,
    [property: JsonPropertyName("failed_items")] List<FailedDelegationItemDto> FailedItems,
    [property: JsonPropertyName("sha256_seal")] string Sha256Seal,
    [property: JsonPropertyName("etmf_artifact_id")] string EtmfArtifactId,
    [property: JsonPropertyName("executed_at")] DateTimeOffset ExecutedAt);

public sealed record TrainingCurriculumDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("modules")] List<TrainingModuleDto>? Modules = null);

/// <summary>
/// Minimal API endpoints for Batch Curriculum Assignment and Bulk Delegation Sign-Off.
/// @req:PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
/// </summary>
public static class TrainingEndpoints
{
    public const int MaxBatchLimit = 200;

    /// <summary>The <c>action</c> a delegation sign-off token is bound to.</summary>
    public const string DelegationSignOffAction = "/api/v1/training/delegations/bulk-signoff";

    /// <summary>The <c>semantic_action</c> a delegation sign-off token is bound to.</summary>
    public const string DelegationSignOffSemanticAction = "knowledge.delegation.sign_off";

    public static IEndpointRouteBuilder MapTrainingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/training")
            .WithTags("Study Training & Gating");

        group.MapGet("/curricula", ListCurriculaAsync)
            .WithName("ListCurricula")
            .RequirePermission("training_log:read").RequireCallerStudyScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<List<TrainingCurriculumDto>>(StatusCodes.Status200OK);

        group.MapPost("/curricula", CreateCurriculumAsync)
            .WithName("CreateCurriculum")
            .RequirePermission("training_curriculum:create").RequireCallerStudyScope()
            .AddEndpointFilter(RequireChangeReasonHeaderFilter)
            .Produces<TrainingCurriculumDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/curricula/{curriculum_id}", GetCurriculumAsync)
            .WithName("GetCurriculum")
            .RequirePermission("training_log:read").RequireCallerStudyScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<TrainingCurriculumDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/curricula/batch-assign", BatchAssignCurriculumAsync)
            .WithName("BatchAssignCurriculum")
            .RequirePermission("training_curriculum:assign").RequireCallerStudyScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<BatchAssignmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/assignments", ListAssignmentsAsync)
            .WithName("ListAssignments")
            .RequirePermission("training_log:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<List<TrainingAssignmentDto>>(StatusCodes.Status200OK);

        group.MapGet("/assignments/{assignment_id}", GetAssignmentDetailAsync)
            .WithName("GetAssignmentDetail")
            .RequirePermission("training_log:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<TrainingAssignmentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/assignments/{assignment_id}/complete", CompleteAssignmentAsync)
            .WithName("CompleteTrainingAssignment")
            .RequirePermission("training_log:create").RequireCallerStudyAndSiteScope()
            .AddEndpointFilter(RequireChangeReasonHeaderFilter)
            .Produces<TrainingAssignmentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/delegations", ListDelegationsAsync)
            .WithName("ListDelegations")
            .RequirePermission("delegation_log:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<List<DelegationSummaryDto>>(StatusCodes.Status200OK);

        group.MapPost("/delegations/bulk-signoff", BulkSignOffDelegationsAsync)
            .WithName("BulkSignOffDelegations")
            .RequirePermission("delegation_log:sign").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(DelegationSignOffBinding)
            .Produces<BulkDelegationSignOffResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult OutsideStudy() =>
        TypedResults.Problem(
            title: "Outside your studies",
            detail: "You are not assigned to this study.",
            statusCode: StatusCodes.Status403Forbidden);

    private static async ValueTask<object?> RequireChangeReasonHeaderFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(GatewayHeaders.ChangeReason, out var raw)
            || string.IsNullOrWhiteSpace(raw.ToString())
            || !ChangeReasonHeader.TryDecode(raw.ToString(), out var decoded)
            || string.IsNullOrWhiteSpace(decoded))
        {
            return TypedResults.Problem(
                title: "Reason for change required",
                detail: "21 CFR Part 11 requires a non-empty X-Change-Reason header for training and delegation state mutations.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context).ConfigureAwait(false);
    }

    private static bool TryExtractReasonForChange(
        HttpContext httpContext,
        string? bodyReason,
        bool requireHeaderOrBody,
        out string reason,
        out IResult? problem)
    {
        reason = string.Empty;
        problem = null;

        if (httpContext.Request.Headers.TryGetValue(GatewayHeaders.ChangeReason, out var raw)
            && !string.IsNullOrWhiteSpace(raw.ToString()))
        {
            if (!ChangeReasonHeader.TryDecode(raw.ToString(), out var decoded) || string.IsNullOrWhiteSpace(decoded))
            {
                problem = TypedResults.Problem(
                    title: "Invalid change reason",
                    detail: "X-Change-Reason must be printable ASCII or RFC 8187 \"UTF-8''\" followed by percent-encoded UTF-8.",
                    statusCode: StatusCodes.Status400BadRequest);
                return false;
            }

            reason = decoded.Trim();
            return true;
        }

        if (!string.IsNullOrWhiteSpace(bodyReason))
        {
            reason = bodyReason.Trim();
            return true;
        }

        if (!requireHeaderOrBody)
        {
            return true;
        }

        problem = TypedResults.Problem(
            title: "Reason for change required",
            detail: "21 CFR Part 11 requires a non-empty X-Change-Reason header or reason_for_change.",
            statusCode: StatusCodes.Status400BadRequest);
        return false;
    }

    private static TrainingModuleDto ToModuleDto(TrainingCurriculumModule m) =>
        new(
            m.Id,
            m.CurriculumId,
            m.Title,
            m.ModuleType,
            m.ContentText,
            m.SequenceOrder,
            m.MinReadingTimeSeconds);

    private static TrainingCurriculumDto ToCurriculumDto(
        TrainingCurriculum c,
        IReadOnlyDictionary<string, List<TrainingModuleDto>> modulesByCurriculum)
    {
        var modules = modulesByCurriculum.TryGetValue(c.Id, out var list) ? list : [];
        return new TrainingCurriculumDto(
            c.Id,
            c.StudyId,
            c.ProtocolVersion,
            c.Title,
            c.Description,
            c.IsActive,
            c.CreatedAt,
            modules);
    }

    private static async Task<Dictionary<string, List<TrainingModuleDto>>> LoadModulesByCurriculumIdsAsync(
        KnowledgeDbContext dbContext,
        IReadOnlyCollection<string> curriculumIds,
        CancellationToken cancellationToken)
    {
        if (curriculumIds.Count == 0)
        {
            return new Dictionary<string, List<TrainingModuleDto>>(StringComparer.Ordinal);
        }

        var modules = await dbContext.TrainingCurriculumModules
            .AsNoTracking()
            .Where(m => curriculumIds.Contains(m.CurriculumId))
            .OrderBy(m => m.SequenceOrder)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        return modules
            .GroupBy(m => m.CurriculumId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(ToModuleDto).ToList(),
                StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, TrainingCurriculumDto>> LoadCurriculumDtosByIdsAsync(
        KnowledgeDbContext dbContext,
        IReadOnlyCollection<string> curriculumIds,
        CancellationToken cancellationToken)
    {
        if (curriculumIds.Count == 0)
        {
            return new Dictionary<string, TrainingCurriculumDto>(StringComparer.Ordinal);
        }

        var curricula = await dbContext.TrainingCurricula
            .AsNoTracking()
            .Where(c => curriculumIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        var modulesMap = await LoadModulesByCurriculumIdsAsync(dbContext, curriculumIds, cancellationToken);
        return curricula.ToDictionary(
            c => c.Id,
            c => ToCurriculumDto(c, modulesMap),
            StringComparer.Ordinal);
    }

    private static async Task<IResult> ListCurriculaAsync(
        string? study_id,
        string? protocol_version,
        KnowledgeDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var studies = CallerScope.StudyFilter(httpContext);
        var query = dbContext.TrainingCurricula.AsNoTracking().Where(c => c.IsActive);
        if (studies != null)
        {
            query = query.Where(c => studies.Contains(c.StudyId));
        }
        if (!string.IsNullOrEmpty(study_id))
        {
            query = query.Where(c => c.StudyId == study_id);
        }
        if (!string.IsNullOrEmpty(protocol_version))
        {
            query = query.Where(c => c.ProtocolVersion == protocol_version);
        }

        // An empty result is an empty list: nothing is invented when no curriculum exists (#5400).
        var list = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        var curriculumIds = list.Select(c => c.Id).Distinct(StringComparer.Ordinal).ToList();
        var modulesMap = await LoadModulesByCurriculumIdsAsync(dbContext, curriculumIds, cancellationToken);
        var dtos = list.Select(c => ToCurriculumDto(c, modulesMap)).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> CreateCurriculumAsync(
        CreateTrainingCurriculumRequest request,
        KnowledgeDbContext dbContext,
        HttpContext httpContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StudyCode))
        {
            return TypedResults.Problem("study_id is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.ProtocolVersion))
        {
            return TypedResults.Problem("protocol_version is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.Problem("title is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        string studyId = request.StudyCode.Trim();
        if (!CallerScope.CanAccessStudy(httpContext, studyId))
        {
            return OutsideStudy();
        }

        if (!TryExtractReasonForChange(httpContext, request.ReasonForChange, requireHeaderOrBody: true, out var reason, out var reasonProblem))
        {
            return reasonProblem!;
        }

        string actorId = CallerScope.UserId(httpContext)
            ?? throw new InvalidOperationException("An admitted training request has no signed caller.");
        string actorRole = CallerScope.Roles(httpContext);
        string protocolVersion = request.ProtocolVersion.Trim();
        string title = request.Title.Trim();

        bool duplicateExists = await dbContext.TrainingCurricula
            .AsNoTracking()
            .AnyAsync(
                c => c.StudyId == studyId
                     && c.ProtocolVersion == protocolVersion
                     && c.Title == title,
                cancellationToken);
        if (duplicateExists)
        {
            return TypedResults.Problem(
                $"A training curriculum titled '{title}' for protocol version '{protocolVersion}' already exists in study '{studyId}'.",
                statusCode: StatusCodes.Status409Conflict);
        }

        DateTimeOffset now = clock.UtcNow;
        var curriculum = new TrainingCurriculum
        {
            Id = "curr-" + Guid.NewGuid().ToString("N")[..12],
            StudyId = studyId,
            ProtocolVersion = protocolVersion,
            Title = title,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = request.IsActive ?? true,
            CreatedAt = now,
            CreatedBy = actorId,
            ReasonForChange = reason,
        };
        dbContext.TrainingCurricula.Add(curriculum);

        var createdModules = new List<TrainingCurriculumModule>();
        if (request.Modules is { Count: > 0 })
        {
            for (int idx = 0; idx < request.Modules.Count; idx++)
            {
                var modReq = request.Modules[idx];
                if (string.IsNullOrWhiteSpace(modReq.Title) || string.IsNullOrWhiteSpace(modReq.ContentText))
                {
                    return TypedResults.Problem(
                        "Each curriculum module requires a non-empty title and content_text.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var mod = new TrainingCurriculumModule
                {
                    Id = "mod-" + Guid.NewGuid().ToString("N")[..12],
                    CurriculumId = curriculum.Id,
                    Title = modReq.Title.Trim(),
                    ModuleType = string.IsNullOrWhiteSpace(modReq.ModuleType)
                        ? "PROTOCOL_DELTA"
                        : modReq.ModuleType.Trim().ToUpperInvariant(),
                    ContentText = modReq.ContentText.Trim(),
                    SequenceOrder = modReq.SequenceOrder ?? (idx + 1),
                    MinReadingTimeSeconds = Math.Max(10, modReq.MinReadingTimeSeconds ?? 45),
                    CreatedAt = now,
                    CreatedBy = actorId,
                    ReasonForChange = reason,
                    VersionIndex = 1,
                };
                dbContext.TrainingCurriculumModules.Add(mod);
                createdModules.Add(mod);
            }
        }

        dbContext.OrgAuditLogs.Add(new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = "training.curriculum.create",
            Details = $"Published protocol training curriculum '{curriculum.Title}' (v{curriculum.ProtocolVersion}) for study {curriculum.StudyId} with {createdModules.Count} module(s).",
            ReasonForChange = reason,
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var moduleDtos = createdModules
            .OrderBy(m => m.SequenceOrder)
            .Select(ToModuleDto)
            .ToList();
        var dto = new TrainingCurriculumDto(
            curriculum.Id,
            curriculum.StudyId,
            curriculum.ProtocolVersion,
            curriculum.Title,
            curriculum.Description,
            curriculum.IsActive,
            curriculum.CreatedAt,
            moduleDtos);

        return TypedResults.Created($"/api/v1/training/curricula/{curriculum.Id}", dto);
    }

    private static async Task<IResult> GetCurriculumAsync(
        string curriculum_id,
        KnowledgeDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // A missing curriculum and one in a study the caller is not assigned to get the same 404 (#5400).
        var c = await dbContext.TrainingCurricula.AsNoTracking().FirstOrDefaultAsync(x => x.Id == curriculum_id, cancellationToken);
        if (c == null || !CallerScope.CanAccessStudy(httpContext, c.StudyId))
        {
            return TypedResults.Problem($"Curriculum '{curriculum_id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        var modulesMap = await LoadModulesByCurriculumIdsAsync(dbContext, [c.Id], cancellationToken);
        return TypedResults.Ok(ToCurriculumDto(c, modulesMap));
    }

    private static async Task<IResult> BatchAssignCurriculumAsync(
        BatchAssignCurriculumRequest request,
        KnowledgeDbContext dbContext,
        HttpContext httpContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurriculumId))
        {
            return TypedResults.Problem("Curriculum ID is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!CallerScope.CanAccessStudy(httpContext, request.StudyId))
        {
            return OutsideStudy();
        }

        string actorId = CallerScope.UserId(httpContext)
            ?? throw new InvalidOperationException("An admitted training request has no signed caller.");
        string actorRole = CallerScope.Roles(httpContext);

        bool namesSites = request.SiteIds is { Count: > 0 };
        bool namesRoles = request.Roles is { Count: > 0 };
        bool namesPeople = request.TargetPersonnelIds is { Count: > 0 };
        if (!namesSites && !namesRoles && !namesPeople)
        {
            return TypedResults.Problem(
                "Name the site_ids, roles or target_personnel_ids to assign; there is no default audience.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryExtractReasonForChange(httpContext, request.ReasonForChange, requireHeaderOrBody: true, out var effectiveReason, out var reasonProblem))
        {
            return reasonProblem!;
        }

        // The curriculum must exist in the request's study; another study's curriculum gets the same 404 (#5400).
        var curriculum = await dbContext.TrainingCurricula.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CurriculumId, cancellationToken);
        if (curriculum == null || curriculum.StudyId != request.StudyId)
        {
            return TypedResults.Problem($"Curriculum '{request.CurriculumId}' not found in study '{request.StudyId}'.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!curriculum.IsActive)
        {
            return TypedResults.Problem($"Curriculum '{request.CurriculumId}' is not active.", statusCode: StatusCodes.Status409Conflict);
        }

        var principal = CallerScope.Principal(httpContext);
        if (namesSites && request.SiteIds!.Exists(site => !SiteAccessEvaluator.CanAccessSite(principal, site)))
        {
            return OutsideStudy();
        }

        // The audience is the study's site staff on the delegation log: the module's record of who works on the study,
        // at which site and in which role. Nobody is synthesized (#5400).
        var staff = await dbContext.DelegationRecords.AsNoTracking()
            .Where(d => d.StudyId == request.StudyId)
            .ToListAsync(cancellationToken);
        var studySites = staff.Select(d => d.SiteId).ToHashSet(StringComparer.Ordinal);

        var unknownSites = namesSites ? request.SiteIds!.Where(site => !studySites.Contains(site)).ToList() : [];
        if (unknownSites.Count > 0)
        {
            return TypedResults.Problem(
                $"Study '{request.StudyId}' has no staff at: {string.Join(", ", unknownSites)}. Nothing was assigned.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var unknownPeople = namesPeople
            ? request.TargetPersonnelIds!.Where(id => !staff.Exists(d => d.StaffUserId == id)).ToList()
            : [];
        if (unknownPeople.Count > 0)
        {
            return TypedResults.Problem(
                $"No delegation-log entry in study '{request.StudyId}' for: {string.Join(", ", unknownPeople)}. Nothing was assigned.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // One candidate per person and site, in a site the caller reaches, matching every filter the request names.
        var candidateList = staff
            .Where(d => !namesSites || request.SiteIds!.Contains(d.SiteId))
            .Where(d => !namesRoles || request.Roles!.Contains(d.Role, StringComparer.OrdinalIgnoreCase))
            .Where(d => !namesPeople || request.TargetPersonnelIds!.Contains(d.StaffUserId))
            .Where(d => SiteAccessEvaluator.CanAccessSite(principal, d.SiteId))
            .GroupBy(d => (d.StaffUserId, d.SiteId))
            .Select(g => g.OrderBy(d => d.Id, StringComparer.Ordinal).First())
            .OrderBy(d => d.SiteId, StringComparer.Ordinal).ThenBy(d => d.StaffUserId, StringComparer.Ordinal)
            .Select(d => (UserId: d.StaffUserId, UserName: d.StaffName, SiteId: d.SiteId, Role: d.Role))
            .ToList();

        if (candidateList.Count == 0)
        {
            return TypedResults.Problem(
                "No staff on the study's delegation log match these sites, roles and people. Nothing was assigned.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var now = clock.UtcNow;
        int siteCount = candidateList.Select(c => c.SiteId).Distinct(StringComparer.Ordinal).Count();

        // Request Size Guardrail check (200 limit)
        if (candidateList.Count > MaxBatchLimit)
        {
            return TypedResults.Problem(
                $"Synchronous batch API endpoints enforce a limit of {MaxBatchLimit} target personnel per request to prevent HTTP timeouts. Requested: {candidateList.Count}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string batchTxId = Guid.NewGuid().ToString();
        var createdAssignments = new List<TrainingAssignment>();
        var skippedItems = new List<SkippedAssignmentItemDto>();

        // Load existing assignments for duplicate check
        var existing = await dbContext.TrainingAssignments
            .Where(a => a.CurriculumId == request.CurriculumId && a.Status != "COMPLETED")
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidateList)
        {
            bool hasActive = existing.Any(a =>
                a.UserId == candidate.UserId &&
                a.SiteId == candidate.SiteId);

            if (hasActive)
            {
                skippedItems.Add(new SkippedAssignmentItemDto(
                    candidate.UserId,
                    candidate.SiteId,
                    candidate.Role,
                    "Duplicate active training assignment already exists for this curriculum"));
                continue;
            }

            var asn = new TrainingAssignment
            {
                Id = Guid.NewGuid().ToString(),
                CurriculumId = request.CurriculumId,
                StudyId = request.StudyId,
                SiteId = candidate.SiteId,
                UserId = candidate.UserId,
                UserName = candidate.UserName,
                UserRole = candidate.Role,
                Status = "PENDING",
                BatchTransactionId = batchTxId,
                TimeSpentSeconds = 0,
                CreatedAt = now,
                CreatedBy = actorId,
                ReasonForChange = effectiveReason
            };

            dbContext.TrainingAssignments.Add(asn);
            createdAssignments.Add(asn);
        }

        // Log audit
        var audit = new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = "training.curriculum.batch_assign",
            BatchTransactionId = batchTxId,
            Details = $"Batch curriculum assignment for {request.CurriculumId} created {createdAssignments.Count} assignments across {siteCount} sites. {skippedItems.Count} items skipped.",
            ReasonForChange = effectiveReason
        };
        dbContext.OrgAuditLogs.Add(audit);

        await dbContext.SaveChangesAsync(cancellationToken);

        var curriculumMap = await LoadCurriculumDtosByIdsAsync(dbContext, [request.CurriculumId], cancellationToken);
        curriculumMap.TryGetValue(request.CurriculumId, out var curriculumDto);

        var assignmentDtos = createdAssignments.Select(a => new TrainingAssignmentDto(
            a.Id, a.CurriculumId, a.StudyId, a.SiteId, a.UserId, a.UserName, a.UserRole,
            a.Status, a.BatchTransactionId, a.QuizScore, a.TimeSpentSeconds, a.CompletedAt, a.CreatedAt, curriculumDto)).ToList();

        var response = new BatchAssignmentResponse(
            batchTxId,
            request.CurriculumId,
            request.StudyId,
            candidateList.Count,
            createdAssignments.Count,
            skippedItems.Count,
            assignmentDtos,
            skippedItems,
            now);

        return TypedResults.Created($"/api/v1/training/assignments?batch_transaction_id={batchTxId}", response);
    }

    private static async Task<IResult> ListAssignmentsAsync(
        string? study_id,
        string? site_id,
        string? user_id,
        string? status,
        KnowledgeDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ClinicalScopeGuard.ResolveStudyAndSiteFilters(httpContext, study_id, site_id, out var studies, out _, out var denied))
        {
            return denied!;
        }

        var query = dbContext.TrainingAssignments.AsNoTracking();
        if (studies != null) query = query.Where(a => studies.Contains(a.StudyId));
        if (!string.IsNullOrEmpty(study_id)) query = query.Where(a => a.StudyId == study_id);
        if (!string.IsNullOrEmpty(site_id)) query = query.Where(a => a.SiteId == site_id);
        if (!string.IsNullOrEmpty(user_id)) query = query.Where(a => a.UserId == user_id);
        if (!string.IsNullOrEmpty(status)) query = query.Where(a => a.Status == status);

        var list = (await query.OrderByDescending(a => a.CreatedAt).ToListAsync(cancellationToken))
            .Where(a => ClinicalScopeGuard.CanAccessSite(httpContext, a.SiteId))
            .ToList();
        var curriculumIds = list.Select(a => a.CurriculumId).Distinct(StringComparer.Ordinal).ToList();
        var curriculumMap = await LoadCurriculumDtosByIdsAsync(dbContext, curriculumIds, cancellationToken);
        var dtos = list.Select(a =>
        {
            curriculumMap.TryGetValue(a.CurriculumId, out var curriculumDto);
            return new TrainingAssignmentDto(
                a.Id, a.CurriculumId, a.StudyId, a.SiteId, a.UserId, a.UserName, a.UserRole,
                a.Status, a.BatchTransactionId, a.QuizScore, a.TimeSpentSeconds, a.CompletedAt, a.CreatedAt, curriculumDto);
        }).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> GetAssignmentDetailAsync(
        string assignment_id,
        KnowledgeDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var asn = await dbContext.TrainingAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignment_id, cancellationToken);
        if (asn == null || !CallerScope.CanAccessStudy(httpContext, asn.StudyId) || !ClinicalScopeGuard.CanAccessSite(httpContext, asn.SiteId))
        {
            return TypedResults.Problem($"Assignment '{assignment_id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        var curriculumMap = await LoadCurriculumDtosByIdsAsync(dbContext, [asn.CurriculumId], cancellationToken);
        curriculumMap.TryGetValue(asn.CurriculumId, out var curriculumDto);

        return TypedResults.Ok(new TrainingAssignmentDto(
            asn.Id, asn.CurriculumId, asn.StudyId, asn.SiteId, asn.UserId, asn.UserName, asn.UserRole,
            asn.Status, asn.BatchTransactionId, asn.QuizScore, asn.TimeSpentSeconds, asn.CompletedAt, asn.CreatedAt, curriculumDto));
    }

    private static async Task<IResult> CompleteAssignmentAsync(
        string assignment_id,
        CompleteTrainingAssignmentRequest request,
        KnowledgeDbContext dbContext,
        HttpContext httpContext,
        IClock clock,
        IOptionsMonitor<GatewayAuthenticationOptions> gatewayAuthentication,
        CancellationToken cancellationToken)
    {
        var asn = await dbContext.TrainingAssignments.FirstOrDefaultAsync(a => a.Id == assignment_id, cancellationToken);
        if (asn == null || !CallerScope.CanAccessStudy(httpContext, asn.StudyId) || !ClinicalScopeGuard.CanAccessSite(httpContext, asn.SiteId))
        {
            return TypedResults.Problem($"Assignment '{assignment_id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!TryExtractReasonForChange(httpContext, request.ReasonForChange, requireHeaderOrBody: true, out var reason, out var reasonProblem))
        {
            return reasonProblem!;
        }

        double? normalizedScore = null;
        if (request.QuizScore.HasValue)
        {
            double raw = request.QuizScore.Value;
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < 0.0 || raw > 100.0)
            {
                return TypedResults.Problem("quiz_score must be between 0.0 and 1.0 (or 0 and 100).", statusCode: StatusCodes.Status400BadRequest);
            }

            normalizedScore = raw > 1.0 ? Math.Round(raw / 100.0, 4) : raw;
        }

        if (request.TimeSpentSeconds is < 0)
        {
            return TypedResults.Problem("time_spent_seconds cannot be negative.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.Equals(asn.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem($"Assignment '{assignment_id}' is already completed.", statusCode: StatusCodes.Status409Conflict);
        }

        string actorId = CallerScope.UserId(httpContext)
            ?? throw new InvalidOperationException("An admitted training request has no signed caller.");
        string actorRole = CallerScope.Roles(httpContext);
        DateTimeOffset now = clock.UtcNow;

        asn.Status = "COMPLETED";
        asn.CompletedAt = now;
        asn.QuizScore = normalizedScore ?? asn.QuizScore ?? 1.0;
        asn.TimeSpentSeconds = Math.Max(asn.TimeSpentSeconds, request.TimeSpentSeconds ?? 60);
        asn.ReasonForChange = reason;

        string? secret = gatewayAuthentication.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret;
        string certSeal = !string.IsNullOrWhiteSpace(secret)
            ? HmacSigner.ComputeSignature($"{asn.Id}|{asn.CurriculumId}|{asn.UserId}|{actorId}|{now:O}", secret)
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{asn.Id}|{asn.CurriculumId}|{asn.UserId}|{actorId}|{now:O}")));

        dbContext.TrainingCertificates.Add(new TrainingCertificate
        {
            Id = Guid.NewGuid().ToString(),
            AssignmentId = asn.Id,
            BatchTransactionId = asn.BatchTransactionId,
            SignerName = actorId,
            SignerRole = actorRole,
            Meaning = "Protocol Training Completion Recorded",
            Sha256Seal = certSeal,
            EtmfArtifactId = "05.02.04",
            CreatedAt = now,
        });

        dbContext.OrgAuditLogs.Add(new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = "training.assignment.complete",
            BatchTransactionId = asn.BatchTransactionId,
            Details = $"Completed protocol training assignment {asn.Id} for {asn.UserName} ({asn.UserId}) at site {asn.SiteId} in study {asn.StudyId}.",
            ReasonForChange = reason,
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var curriculumMap = await LoadCurriculumDtosByIdsAsync(dbContext, [asn.CurriculumId], cancellationToken);
        curriculumMap.TryGetValue(asn.CurriculumId, out var curriculumDto);

        return TypedResults.Ok(new TrainingAssignmentDto(
            asn.Id, asn.CurriculumId, asn.StudyId, asn.SiteId, asn.UserId, asn.UserName, asn.UserRole,
            asn.Status, asn.BatchTransactionId, asn.QuizScore, asn.TimeSpentSeconds, asn.CompletedAt, asn.CreatedAt, curriculumDto));
    }

    private static async Task<IResult> ListDelegationsAsync(
        string? study_id,
        string? site_id,
        string? status,
        KnowledgeDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ClinicalScopeGuard.ResolveStudyAndSiteFilters(httpContext, study_id, site_id, out var studies, out _, out var denied))
        {
            return denied!;
        }

        var query = dbContext.DelegationRecords.AsNoTracking();
        if (studies != null) query = query.Where(d => studies.Contains(d.StudyId));
        if (!string.IsNullOrEmpty(study_id)) query = query.Where(d => d.StudyId == study_id);
        if (!string.IsNullOrEmpty(site_id)) query = query.Where(d => d.SiteId == site_id);
        if (!string.IsNullOrEmpty(status)) query = query.Where(d => d.Status == status);

        var list = (await query.ToListAsync(cancellationToken))
            .Where(d => ClinicalScopeGuard.CanAccessSite(httpContext, d.SiteId))
            .ToList();

        var dtos = list.Select(d => new DelegationSummaryDto(
            d.Id, d.StudyId, d.SiteId, d.StaffUserId, d.StaffName, d.Role, d.TaskCode, d.Duties,
            d.Status, d.SignedOff, d.PiSignatureHash, d.PiApprovedAt, d.BatchTransactionId)).ToList();

        return TypedResults.Ok(dtos);
    }

    private static SigTokenBinding? DelegationSignOffBinding(EndpointFilterInvocationContext context) =>
        context.Arguments.OfType<BulkDelegationSignOffRequest>().FirstOrDefault() is { DelegationIds.Count: > 0 } request
            ? new SigTokenBinding(DelegationSignOffAction, DelegationSignOffSemanticAction, request.BatchBindingId())
            : null;

    /// <summary>
    /// The PI's Part 11 sign-off of existing delegation-log records (#5311). The route requires
    /// <c>delegation_log:sign</c> and a step-up token bound to exactly these ids, so the signer is the verified caller;
    /// an id that names no record fails the whole request instead of creating one. The seal is an HMAC keyed with the
    /// gateway secret.
    /// </summary>
    private static async Task<IResult> BulkSignOffDelegationsAsync(
        BulkDelegationSignOffRequest request,
        HttpContext httpContext,
        KnowledgeDbContext dbContext,
        IClock clock,
        IOptionsMonitor<GatewayAuthenticationOptions> gatewayAuthentication,
        CancellationToken cancellationToken)
    {
        if (request.DelegationIds == null || request.DelegationIds.Count == 0)
        {
            return TypedResults.Problem("At least one delegation record ID is required for bulk sign-off.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Limit Guardrail check (200 limit)
        if (request.DelegationIds.Count > MaxBatchLimit)
        {
            return TypedResults.Problem(
                $"Synchronous batch API endpoints enforce a limit of {MaxBatchLimit} target items per request. Requested: {request.DelegationIds.Count}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The filter has verified and consumed the token for this caller and these ids.
        if (httpContext.VerifiedSigToken() is null
            || httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] is not Principal signer)
        {
            return TypedResults.Problem(SigTokenVerifier.ReauthenticationRequired, statusCode: StatusCodes.Status401Unauthorized);
        }

        string secret = gatewayAuthentication.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret
            is { Length: > 0 } configured
            ? configured
            : throw new InvalidOperationException("GATEWAY_SECRET is not configured; delegation sign-offs cannot be sealed.");

        var ids = request.DelegationIds.Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var records = await dbContext.DelegationRecords
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(cancellationToken);
        var missing = ids.Except(records.Select(r => r.Id), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            return TypedResults.Problem(
                $"No delegation record exists for: {string.Join(", ", missing)}. Nothing was signed.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (records.Exists(r => !StudyAccessEvaluator.CanAccessStudy(signer, r.StudyId) || !SiteAccessEvaluator.CanAccessSite(signer, r.SiteId)))
        {
            return OutsideStudy();
        }

        var now = clock.UtcNow;
        string batchTxId = Guid.NewGuid().ToString();
        string signerRole = string.Join(",", signer.Roles);

        string sealInput = $"{batchTxId}|{string.Join(",", ids.Order(StringComparer.Ordinal))}|{signer.UserId}|{signerRole}|{request.SignaturePayload?.Meaning}|{now:O}";
        string seal = HmacSigner.ComputeSignature(sealInput, secret);

        var signedList = new List<DelegationSummaryDto>();
        foreach (var record in records.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            record.Status = "COMPLETED";
            record.SignedOff = true;
            record.PiSignatureHash = seal;
            record.PiApprovedAt = now;
            record.BatchTransactionId = batchTxId;

            signedList.Add(new DelegationSummaryDto(
                record.Id, record.StudyId, record.SiteId, record.StaffUserId, record.StaffName,
                record.Role, record.TaskCode, record.Duties, record.Status, record.SignedOff,
                record.PiSignatureHash, record.PiApprovedAt, record.BatchTransactionId));
        }

        dbContext.OrgAuditLogs.Add(new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = signer.UserId,
            ActorRole = signerRole,
            Action = "training.delegation.bulk_signoff",
            BatchTransactionId = batchTxId,
            Details = $"Executed Part 11 bulk delegation sign-off for {signedList.Count} delegation log records. Routed to eTMF Zone 5 (Artifact 05.02.04). Seal HMAC-SHA256: {seal}",
            ReasonForChange = request.SignaturePayload?.ReasonForChange ?? "21 CFR Part 11 Principal Investigator bulk delegation sign-off"
        });

        dbContext.TrainingCertificates.Add(new TrainingCertificate
        {
            Id = Guid.NewGuid().ToString(),
            AssignmentId = signedList[0].Id,
            BatchTransactionId = batchTxId,
            SignerName = signer.UserId,
            SignerRole = signerRole,
            Meaning = request.SignaturePayload?.Meaning ?? string.Empty,
            Sha256Seal = seal,
            EtmfArtifactId = "05.02.04",
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new BulkDelegationSignOffResponse(
            batchTxId,
            ids.Count,
            signedList.Count,
            0,
            signedList,
            [],
            seal,
            "05.02.04",
            now));
    }
}
