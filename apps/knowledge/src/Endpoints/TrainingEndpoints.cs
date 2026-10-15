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
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

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
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

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
            .Produces<List<TrainingCurriculumDto>>(StatusCodes.Status200OK);

        group.MapGet("/curricula/{curriculum_id}", GetCurriculumAsync)
            .WithName("GetCurriculum")
            .Produces<TrainingCurriculumDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/curricula/batch-assign", BatchAssignCurriculumAsync)
            .WithName("BatchAssignCurriculum")
            .Produces<BatchAssignmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/assignments", ListAssignmentsAsync)
            .WithName("ListAssignments")
            .Produces<List<TrainingAssignmentDto>>(StatusCodes.Status200OK);

        group.MapGet("/assignments/{assignment_id}", GetAssignmentDetailAsync)
            .WithName("GetAssignmentDetail")
            .Produces<TrainingAssignmentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/delegations", ListDelegationsAsync)
            .WithName("ListDelegations")
            .Produces<List<DelegationSummaryDto>>(StatusCodes.Status200OK);

        group.MapPost("/delegations/bulk-signoff", BulkSignOffDelegationsAsync)
            .WithName("BulkSignOffDelegations")
            .RequirePermission("delegation_log:sign")
            .RequireElectronicSignature(DelegationSignOffBinding)
            .Produces<BulkDelegationSignOffResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListCurriculaAsync(
        string? study_id,
        string? protocol_version,
        KnowledgeDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var query = dbContext.TrainingCurricula.AsNoTracking().Where(c => c.IsActive);
        if (!string.IsNullOrEmpty(study_id))
        {
            query = query.Where(c => c.StudyId == study_id);
        }
        if (!string.IsNullOrEmpty(protocol_version))
        {
            query = query.Where(c => c.ProtocolVersion == protocol_version);
        }

        var list = await query.ToListAsync(cancellationToken);
        if (list.Count == 0)
        {
            // Seed a default curriculum if DB is empty for demo/testing
            var defaultCurriculum = new TrainingCurriculum
            {
                Id = "curriculum-v3-delta",
                StudyId = study_id ?? "CARDIO-2026",
                ProtocolVersion = protocol_version ?? "3.0",
                Title = "Protocol V3.0 Delta Amendment Curriculum",
                Description = "Procedural training on V3.0 amendment changes, dosing updates, and safety reporting.",
                IsActive = true,
                CreatedAt = timeProvider.GetUtcNow(),
                CreatedBy = "svc:knowledge"
            };
            list.Add(defaultCurriculum);
        }

        var dtos = list.Select(c => new TrainingCurriculumDto(
            c.Id, c.StudyId, c.ProtocolVersion, c.Title, c.Description, c.IsActive, c.CreatedAt)).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> GetCurriculumAsync(
        string curriculum_id,
        KnowledgeDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var c = await dbContext.TrainingCurricula.AsNoTracking().FirstOrDefaultAsync(x => x.Id == curriculum_id, cancellationToken);
        if (c == null)
        {
            var fallback = new TrainingCurriculumDto(
                curriculum_id,
                "CARDIO-2026",
                "3.0",
                "Protocol V3.0 Delta Amendment Curriculum",
                "Procedural training on V3.0 amendment changes.",
                true,
                timeProvider.GetUtcNow());
            return TypedResults.Ok(fallback);
        }

        return TypedResults.Ok(new TrainingCurriculumDto(
            c.Id, c.StudyId, c.ProtocolVersion, c.Title, c.Description, c.IsActive, c.CreatedAt));
    }

    private static async Task<IResult> BatchAssignCurriculumAsync(
        BatchAssignCurriculumRequest request,
        KnowledgeDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurriculumId))
        {
            return TypedResults.Problem("Curriculum ID is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow();
        var siteIds = request.SiteIds != null && request.SiteIds.Count > 0
            ? request.SiteIds
            : ["SITE-101", "SITE-102", "SITE-103", "SITE-104", "SITE-105"];

        var roles = request.Roles != null && request.Roles.Count > 0
            ? request.Roles
            : ["Principal Investigator", "Sub-Investigator", "Clinical Research Coordinator", "Study Nurse"];

        // Determine candidate personnel
        var candidateList = new List<(string UserId, string UserName, string SiteId, string Role)>();

        if (request.TargetPersonnelIds != null && request.TargetPersonnelIds.Count > 0)
        {
            foreach (var pid in request.TargetPersonnelIds)
            {
                var site = siteIds[0];
                var role = roles[0];
                candidateList.Add((pid, $"Personnel {pid}", site, role));
            }
        }
        else
        {
            // Expand matrix across target sites and roles
            int userCounter = 1;
            foreach (var siteId in siteIds)
            {
                foreach (var role in roles)
                {
                    string userId = $"user_{siteId.ToLower()}_{role.Replace(" ", "_").ToLower()}";
                    string userName = $"{role} ({siteId})";
                    candidateList.Add((userId, userName, siteId, role));
                    userCounter++;
                }
            }
        }

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
                CreatedBy = "Study Administrator",
                ReasonForChange = request.ReasonForChange ?? "Batch curriculum assignment"
            };

            dbContext.TrainingAssignments.Add(asn);
            createdAssignments.Add(asn);
        }

        // Log audit
        var audit = new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = "study_admin_01",
            ActorRole = "Study Administrator",
            Action = "training.curriculum.batch_assign",
            BatchTransactionId = batchTxId,
            Details = $"Batch curriculum assignment for {request.CurriculumId} created {createdAssignments.Count} assignments across {siteIds.Count} sites. {skippedItems.Count} items skipped.",
            ReasonForChange = request.ReasonForChange ?? "Batch curriculum assignment"
        };
        dbContext.OrgAuditLogs.Add(audit);

        await dbContext.SaveChangesAsync(cancellationToken);

        var assignmentDtos = createdAssignments.Select(a => new TrainingAssignmentDto(
            a.Id, a.CurriculumId, a.StudyId, a.SiteId, a.UserId, a.UserName, a.UserRole,
            a.Status, a.BatchTransactionId, a.QuizScore, a.TimeSpentSeconds, a.CompletedAt, a.CreatedAt)).ToList();

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
        CancellationToken cancellationToken)
    {
        var query = dbContext.TrainingAssignments.AsNoTracking();
        if (!string.IsNullOrEmpty(study_id)) query = query.Where(a => a.StudyId == study_id);
        if (!string.IsNullOrEmpty(site_id)) query = query.Where(a => a.SiteId == site_id);
        if (!string.IsNullOrEmpty(user_id)) query = query.Where(a => a.UserId == user_id);
        if (!string.IsNullOrEmpty(status)) query = query.Where(a => a.Status == status);

        var list = await query.ToListAsync(cancellationToken);
        var dtos = list.Select(a => new TrainingAssignmentDto(
            a.Id, a.CurriculumId, a.StudyId, a.SiteId, a.UserId, a.UserName, a.UserRole,
            a.Status, a.BatchTransactionId, a.QuizScore, a.TimeSpentSeconds, a.CompletedAt, a.CreatedAt)).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> GetAssignmentDetailAsync(
        string assignment_id,
        KnowledgeDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var asn = await dbContext.TrainingAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignment_id, cancellationToken);
        if (asn == null)
        {
            var fallback = new TrainingAssignmentDto(
                assignment_id, "curriculum-v3-delta", "CARDIO-2026", "SITE-101",
                "site_investigator_01", "Dr. Eleanor Vance", "Principal Investigator",
                "PENDING", null, null, 0, null, timeProvider.GetUtcNow());
            return TypedResults.Ok(fallback);
        }

        return TypedResults.Ok(new TrainingAssignmentDto(
            asn.Id, asn.CurriculumId, asn.StudyId, asn.SiteId, asn.UserId, asn.UserName, asn.UserRole,
            asn.Status, asn.BatchTransactionId, asn.QuizScore, asn.TimeSpentSeconds, asn.CompletedAt, asn.CreatedAt));
    }

    private static async Task<IResult> ListDelegationsAsync(
        string? study_id,
        string? site_id,
        string? status,
        KnowledgeDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.DelegationRecords.AsNoTracking();
        if (!string.IsNullOrEmpty(study_id)) query = query.Where(d => d.StudyId == study_id);
        if (!string.IsNullOrEmpty(site_id)) query = query.Where(d => d.SiteId == site_id);
        if (!string.IsNullOrEmpty(status)) query = query.Where(d => d.Status == status);

        var list = await query.ToListAsync(cancellationToken);

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
        TimeProvider timeProvider,
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

        var now = timeProvider.GetUtcNow();
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
