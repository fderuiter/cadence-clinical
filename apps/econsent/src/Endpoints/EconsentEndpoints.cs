using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Apps.Econsent.Services;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Econsent.Endpoints;

public sealed record PublishTemplateRequest(
    [property: JsonPropertyName("study_id")] string? StudyId,
    [property: JsonPropertyName("protocol_version")] string? ProtocolVersion,
    [property: JsonPropertyName("change_summary")] string? ChangeSummary,
    [property: JsonPropertyName("change_reason")] string? ChangeReason,
    [property: JsonPropertyName("requires_reconsent")] bool? RequiresReconsent,
    [property: JsonPropertyName("channels")] string[]? Channels);

public sealed record PublishTemplateResponse(
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("is_published")] bool IsPublished,
    [property: JsonPropertyName("requires_reconsent")] bool RequiresReconsent,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);

public sealed record CaptureConsentRequest(
    [property: JsonPropertyName("subject_pseudonym")] string? SubjectPseudonym,
    [property: JsonPropertyName("study_id")] string? StudyId,
    [property: JsonPropertyName("site_id")] string? SiteId,
    [property: JsonPropertyName("signer_name")] string? SignerName,
    [property: JsonPropertyName("signer_email")] string? SignerEmail,
    [property: JsonPropertyName("signer_role")] string? SignerRole,
    [property: JsonPropertyName("meaning")] string? Meaning,
    [property: JsonPropertyName("signing_reason")] string? SigningReason,
    [property: JsonPropertyName("protocol_version")] string? ProtocolVersion,
    [property: JsonPropertyName("digest_sha256")] string? DigestSha256,
    [property: JsonPropertyName("signature_data")] string? SignatureData,
    [property: JsonPropertyName("liveness_token")] string? LivenessToken,
    [property: JsonPropertyName("device_timestamp")] DateTimeOffset? DeviceTimestamp);

public sealed record CaptureConsentResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("subject_pseudonym")] string SubjectPseudonym,
    [property: JsonPropertyName("signer_name")] string SignerName,
    [property: JsonPropertyName("signer_role")] string SignerRole,
    [property: JsonPropertyName("meaning")] string Meaning,
    [property: JsonPropertyName("server_timestamp")] DateTimeOffset ServerTimestamp,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sha256_hash")] string Sha256Hash);

public sealed record ApprovedConsentClauseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("clause_id")] string ClauseId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("version_index")] int VersionIndex);

public sealed record ApprovedConsentContentResponse(
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("template_name")] string TemplateName,
    [property: JsonPropertyName("study_id")] string StudyCode,
    [property: JsonPropertyName("site_id")] string? SiteCode,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("language_code")] string LanguageCode,
    [property: JsonPropertyName("requires_reconsent")] bool RequiresReconsent,
    [property: JsonPropertyName("digest_sha256")] string DigestSha256,
    [property: JsonPropertyName("clauses")] IReadOnlyList<ApprovedConsentClauseDto> Clauses,
    [property: JsonPropertyName("workflow_steps")] JsonElement WorkflowSteps);

public sealed record ComprehensionCheckResponse(
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("questions")] JsonElement Questions,
    [property: JsonPropertyName("threshold_policy")] JsonElement ThresholdPolicy);

public sealed record SubmitComprehensionAnswersRequest(
    [property: JsonPropertyName("subject_pseudonym")] string? SubjectPseudonym,
    [property: JsonPropertyName("submitted_answers")] Dictionary<string, string>? SubmittedAnswers,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange);

public sealed record SubmitComprehensionAnswersResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("score")] decimal Score,
    [property: JsonPropertyName("correct_count")] int CorrectCount,
    [property: JsonPropertyName("total_questions")] int TotalQuestions,
    [property: JsonPropertyName("next_step")] string NextStep,
    [property: JsonPropertyName("message")] string Message);

public sealed record PendingReconsentItemDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("requirement_id")] string RequirementId,
    [property: JsonPropertyName("study_id")] string StudyCode,
    [property: JsonPropertyName("site_id")] string? SiteCode,
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("prior_version_index")] int PriorVersionIndex,
    [property: JsonPropertyName("new_version_index")] int NewVersionIndex,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("subject_pseudonym")] string SubjectPseudonym,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("change_summary")] string ChangeSummary,
    [property: JsonPropertyName("deadline_at")] DateTimeOffset? DeadlineAt,
    [property: JsonPropertyName("clause_diffs")] JsonElement ClauseDiffs,
    [property: JsonPropertyName("comprehension_questions")] JsonElement ComprehensionQuestions);

public sealed record PendingReconsentListResponse(
    [property: JsonPropertyName("study_id")] string StudyCode,
    [property: JsonPropertyName("subject_pseudonym")] string SubjectPseudonym,
    [property: JsonPropertyName("pending_items")] IReadOnlyList<PendingReconsentItemDto> PendingItems);

public sealed record CompleteReconsentRequest(
    [property: JsonPropertyName("subject_pseudonym")] string? SubjectPseudonym,
    [property: JsonPropertyName("study_id")] string? StudyCode,
    [property: JsonPropertyName("site_id")] string? SiteCode,
    [property: JsonPropertyName("template_id")] string? TemplateId,
    [property: JsonPropertyName("version_index")] int? VersionIndex,
    [property: JsonPropertyName("protocol_version")] string? ProtocolVersion,
    [property: JsonPropertyName("signer_name")] string? SignerName,
    [property: JsonPropertyName("signer_email")] string? SignerEmail,
    [property: JsonPropertyName("signer_role")] string? SignerRole,
    [property: JsonPropertyName("meaning")] string? Meaning,
    [property: JsonPropertyName("signing_reason")] string? SigningReason,
    [property: JsonPropertyName("comprehension_answers")] Dictionary<string, string>? ComprehensionAnswers,
    [property: JsonPropertyName("digest_sha256")] string? DigestSha256,
    [property: JsonPropertyName("signature_data")] string? SignatureData,
    [property: JsonPropertyName("device_timestamp")] DateTimeOffset? DeviceTimestamp);

public sealed record CompleteReconsentResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("requirement_id")] string RequirementId,
    [property: JsonPropertyName("completed_consent_id")] string CompletedConsentId,
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("subject_pseudonym")] string SubjectPseudonym,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("server_timestamp")] DateTimeOffset ServerTimestamp,
    [property: JsonPropertyName("sha256_hash")] string Sha256Hash);

/// <summary>
/// Minimal API route definitions for eConsent template publishing, caller-bound participant review,
/// comprehension grading, initial signature capture, and atomic amendment re-consent completion.
/// </summary>
public static class EconsentEndpoints
{
    public const string CaptureConsentSemanticAction = "econsent.consent.capture";
    public const string ReconsentSignSemanticAction = "econsent.reconsent.sign";

    public static string CaptureConsentAction(string templateId, int versionIndex) =>
        $"/api/v1/econsent/templates/{templateId}/versions/{versionIndex}/capture-consent";

    public static string CaptureConsentLegacyAction(string templateId) =>
        $"/api/v1/econsent/templates/{templateId}/capture-consent";

    public static string ReconsentCompleteAction(string requirementId) =>
        $"/api/v1/econsent/reconsent/complete/{requirementId}";

    public static IEndpointRouteBuilder MapEconsentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/econsent/templates")
            .WithTags("eConsent");

        group.MapPost("/{template_id}/versions/{version_index}/publish", PublishTemplateVersionedAsync)
            .WithName("PublishTemplateVersioned")
            .RequirePermission("econsent_template:publish").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<PublishTemplateResponse>(StatusCodes.Status200OK);

        group.MapPost("/{template_id}/publish", PublishTemplateAsync)
            .WithName("PublishTemplate")
            .RequirePermission("econsent_template:publish").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<PublishTemplateResponse>(StatusCodes.Status200OK);

        group.MapGet("/{template_id}/approved-content", GetApprovedContentAsync)
            .WithName("GetApprovedConsentContent")
            .RequirePermission("econsent_self_consent:read").RequireCallerStudyScope()
            .Produces<ApprovedConsentContentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{template_id}/versions/{version_index}/comprehension-checks", GetComprehensionChecksAsync)
            .WithName("GetComprehensionChecks")
            .RequirePermission("econsent_self_consent:read").RequireCallerStudyScope()
            .Produces<ComprehensionCheckResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{template_id}/versions/{version_index}/submit-answers", SubmitComprehensionAnswersAsync)
            .WithName("SubmitComprehensionAnswers")
            .RequirePermission("econsent_self_consent:verify").RequireCallerStudyScope()
            .Produces<SubmitComprehensionAnswersResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{template_id}/versions/{version_index}/capture-consent", CaptureConsentAsync)
            .WithName("CaptureConsentVersioned")
            .RequirePermission("econsent_self_consent:sign").RequireCallerStudyScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<CaptureConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature(CaptureConsentSemanticAction, CaptureConsentActionFor);

        group.MapPost("/{template_id}/capture-consent", CaptureConsentLegacyAsync)
            .WithName("CaptureConsentLegacy")
            .RequirePermission("econsent_consent:capture").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<CaptureConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature(CaptureConsentSemanticAction, CaptureConsentLegacyActionFor);

        var reconsentGroup = app.MapGroup("/api/v1/econsent/reconsent")
            .WithTags("eConsent");

        reconsentGroup.MapGet("/pending/{study_id}", GetPendingReconsentsAsync)
            .WithName("GetPendingReconsents")
            .RequirePermission("econsent_self_reconsent:read").RequireStudyScope("study_id")
            .Produces<PendingReconsentListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        reconsentGroup.MapPost("/complete/{requirement_id}", CompleteReconsentAsync)
            .WithName("CompleteReconsentRequirement")
            .RequirePermission("econsent_self_reconsent:sign").RequireCallerStudyScope()
            .Produces<CompleteReconsentResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireElectronicSignature(ReconsentSignSemanticAction, ReconsentCompleteActionFor);

        return app;
    }

    private static string Caller(HttpContext httpContext) =>
        CallerScope.UserId(httpContext) ?? throw new InvalidOperationException("An admitted eConsent request has no signed caller.");

    private static IResult OutsideStudy() =>
        TypedResults.Problem(
            title: "Outside your studies",
            detail: "You are not assigned to this study.",
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult CrossParticipantDenied() =>
        TypedResults.Problem(
            title: "Cross-participant access denied",
            detail: "Participants may only access or sign their own consent records.",
            statusCode: StatusCodes.Status403Forbidden);

    private static bool IsStaffConsentCapturer(HttpContext httpContext) =>
        ClinicalScopeGuard.ResolvePrincipal(httpContext) is { } principal
        && PermissionMatrix.HasPermission(principal, "econsent_consent:capture");

    private static string CaptureConsentActionFor(HttpContext httpContext)
    {
        string templateId = httpContext.Request.RouteValues["template_id"]?.ToString() ?? string.Empty;
        int versionIndex = int.TryParse(httpContext.Request.RouteValues["version_index"]?.ToString(), out int v) ? v : 1;
        return CaptureConsentAction(templateId, versionIndex);
    }

    private static string CaptureConsentLegacyActionFor(HttpContext httpContext)
    {
        string templateId = httpContext.Request.RouteValues["template_id"]?.ToString() ?? string.Empty;
        return CaptureConsentLegacyAction(templateId);
    }

    private static string ReconsentCompleteActionFor(HttpContext httpContext)
    {
        string requirementId = httpContext.Request.RouteValues["requirement_id"]?.ToString() ?? string.Empty;
        return ReconsentCompleteAction(requirementId);
    }

    public static async Task<IResult> GetApprovedContentAsync(
        string template_id,
        [FromQuery(Name = "language_code")] string? language_code,
        EconsentDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (ClinicalScopeGuard.RequireAnyStudyScope(httpContext) is { } callerScopeDenied)
        {
            return callerScopeDenied;
        }

        string normalizedLanguage = string.IsNullOrWhiteSpace(language_code)
            ? "en"
            : language_code.Trim().ToLowerInvariant();

        var matchingById = await dbContext.ConsentTemplates
            .AsNoTracking()
            .Where(t => t.TemplateId == template_id)
            .OrderByDescending(t => t.VersionIndex)
            .ToListAsync(cancellationToken);

        ConsentTemplate? template = matchingById.FirstOrDefault(t => t.IsPublished);

        if (template is null && matchingById.Count > 0)
        {
            if (matchingById.Any(t => !ClinicalScopeGuard.CanAccessStudy(httpContext, t.StudyId)))
            {
                return OutsideStudy();
            }

            return TypedResults.NotFound(new { detail = $"No published consent template found for '{template_id}'." });
        }

        if (template is null && string.Equals(template_id, "template-icf", StringComparison.OrdinalIgnoreCase))
        {
            var candidatePublished = await dbContext.ConsentTemplates
                .AsNoTracking()
                .Where(t => t.IsPublished)
                .OrderByDescending(t => t.VersionIndex)
                .ToListAsync(cancellationToken);

            template = candidatePublished.FirstOrDefault(t => ClinicalScopeGuard.CanAccessStudy(httpContext, t.StudyId));
        }

        if (template is null)
        {
            return TypedResults.NotFound(new { detail = $"No published consent template found for '{template_id}'." });
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, template.StudyId))
        {
            return OutsideStudy();
        }

        var studyClauses = await dbContext.ConsentClauses
            .AsNoTracking()
            .Where(c => c.StudyId == template.StudyId)
            .OrderBy(c => c.VersionIndex)
            .ThenBy(c => c.ClauseId)
            .ToListAsync(cancellationToken);

        var approvedTranslations = !string.Equals(normalizedLanguage, "en", StringComparison.OrdinalIgnoreCase)
            ? await dbContext.ConsentTranslations
                .AsNoTracking()
                .Where(tr => tr.LanguageCode == normalizedLanguage && tr.Status == "APPROVED")
                .OrderByDescending(tr => tr.VersionIndex)
                .ToListAsync(cancellationToken)
            : [];

        var composedClauses = ComposeApprovedClauses(template, studyClauses, approvedTranslations);
        string templateName = template.TemplateName;
        var templateTranslation = approvedTranslations.FirstOrDefault(tr =>
            string.Equals(tr.SourceId, template.TemplateId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(tr.TranslatedTitle));
        if (templateTranslation is not null)
        {
            templateName = templateTranslation.TranslatedTitle;
        }

        string canonicalPayload = string.Join(
            "|",
            template.TemplateId,
            template.VersionIndex.ToString(CultureInfo.InvariantCulture),
            template.ProtocolVersion,
            normalizedLanguage,
            string.Join(";", composedClauses.Select(c => $"{c.ClauseId}:{c.Title}:{c.Text}")));
        string digestSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));

        Principal? principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        string? defaultSiteCode = principal?.AssignedSites.FirstOrDefault();

        var response = new ApprovedConsentContentResponse(
            TemplateId: template.TemplateId,
            TemplateName: templateName,
            StudyCode: template.StudyId,
            SiteCode: defaultSiteCode,
            ProtocolVersion: template.ProtocolVersion,
            VersionIndex: template.VersionIndex,
            LanguageCode: normalizedLanguage,
            RequiresReconsent: template.RequiresReconsent,
            DigestSha256: digestSha256,
            Clauses: composedClauses,
            WorkflowSteps: ParseJsonOrFallback(template.WorkflowStepsJson, "[]"));

        return TypedResults.Ok(response);
    }

    public static async Task<IResult> GetComprehensionChecksAsync(
        string template_id,
        int version_index,
        EconsentDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (ClinicalScopeGuard.RequireAnyStudyScope(httpContext) is { } callerScopeDenied)
        {
            return callerScopeDenied;
        }

        var template = await ResolveScopedTemplateAsync(template_id, version_index, dbContext, cancellationToken);
        if (template is null)
        {
            return TypedResults.NotFound(new { detail = $"Consent template '{template_id}' was not found." });
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, template.StudyId))
        {
            return OutsideStudy();
        }

        var check = await ResolveLatestComprehensionCheckAsync(template.TemplateId, version_index, dbContext, cancellationToken);

        if (check is null)
        {
            return TypedResults.Ok(new ComprehensionCheckResponse(
                TemplateId: template.TemplateId,
                VersionIndex: version_index,
                Questions: ParseJsonOrFallback("[]", "[]"),
                ThresholdPolicy: ParseJsonOrFallback("""{"required_score":100}""", "{}")));
        }

        return TypedResults.Ok(new ComprehensionCheckResponse(
            TemplateId: check.TemplateId,
            VersionIndex: check.VersionIndex,
            Questions: SanitizeQuestionsJson(check.QuestionsJson),
            ThresholdPolicy: ParseJsonOrFallback(check.ThresholdPolicyJson, "{}")));
    }

    public static async Task<IResult> SubmitComprehensionAnswersAsync(
        string template_id,
        int version_index,
        SubmitComprehensionAnswersRequest? request,
        EconsentDbContext dbContext,
        HttpContext httpContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (ClinicalScopeGuard.RequireAnyStudyScope(httpContext) is { } callerScopeDenied)
        {
            return callerScopeDenied;
        }

        if (request is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Request body is required."]
            });
        }

        string callerId = Caller(httpContext);
        if (!string.IsNullOrWhiteSpace(request.SubjectPseudonym)
            && !string.Equals(request.SubjectPseudonym.Trim(), callerId, StringComparison.Ordinal))
        {
            return CrossParticipantDenied();
        }

        string subjectPseudonym = callerId;
        var template = await ResolveScopedTemplateAsync(template_id, version_index, dbContext, cancellationToken);
        if (template is null)
        {
            return TypedResults.NotFound(new { detail = $"Consent template '{template_id}' was not found." });
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, template.StudyId))
        {
            return OutsideStudy();
        }

        var check = await ResolveLatestComprehensionCheckAsync(template.TemplateId, version_index, dbContext, cancellationToken);

        if (check is null)
        {
            return TypedResults.NotFound(new { detail = $"No comprehension check configured for '{template_id}' v{version_index}." });
        }

        var submitted = request.SubmittedAnswers ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var (passed, score, correctCount, totalQuestions) = GradeComprehensionAnswers(
            check.ExpectedAnswersJson,
            check.ThresholdPolicyJson,
            submitted);

        DateTimeOffset now = clock.UtcNow;
        string submittedJson = JsonSerializer.Serialize(submitted);
        string reason = !string.IsNullOrWhiteSpace(request.ReasonForChange)
            ? request.ReasonForChange.Trim()
            : "Participant comprehension check submission";

        var resultRecord = new ComprehensionResult
        {
            Id = Guid.NewGuid().ToString(),
            TemplateId = check.TemplateId,
            VersionIndex = version_index,
            SubjectPseudonym = subjectPseudonym,
            QuestionsJson = check.QuestionsJson,
            ExpectedAnswersJson = check.ExpectedAnswersJson,
            ThresholdPolicyJson = check.ThresholdPolicyJson,
            SubmittedAnswersJson = submittedJson,
            Passed = passed,
            Score = score,
            CreatedAt = now,
            CreatedBy = callerId,
            ReasonForChange = reason,
        };

        string resultDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{check.TemplateId}|{version_index}|{subjectPseudonym}|{submittedJson}|{passed}")));

        var auditLog = new ConsentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = callerId,
            ActorRole = "SUBJECT",
            Action = passed ? "COMPREHENSION_CHECK_PASSED" : "COMPREHENSION_CHECK_FAILED",
            DocumentId = resultRecord.Id,
            Details = $"Comprehension check graded ({correctCount}/{totalQuestions}, score {score:0.##}%) for {subjectPseudonym}",
            ReasonForChange = reason,
            TenantId = ClinicalScopeGuard.ResolvePrincipal(httpContext)?.TenantId ?? CallerScope.TenantId(httpContext) ?? string.Empty,
            Sha256Digest = resultDigest,
        };

        dbContext.ComprehensionResults.Add(resultRecord);
        dbContext.ConsentAuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);

        string message = passed
            ? "Comprehension check passed. You can now proceed to sign."
            : "One or more answers were incorrect. Please review the highlighted sections and try again.";

        return TypedResults.Ok(new SubmitComprehensionAnswersResponse(
            Id: resultRecord.Id,
            Passed: passed,
            Score: score,
            CorrectCount: correctCount,
            TotalQuestions: totalQuestions,
            NextStep: passed ? "sign_consent" : "retry_checks",
            Message: message));
    }

    public static async Task<IResult> GetPendingReconsentsAsync(
        string study_id,
        [FromQuery(Name = "subject_pseudonym")] string? subject_pseudonym,
        EconsentDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (ClinicalScopeGuard.RequireStudy(httpContext, study_id) is { } studyDenied)
        {
            return studyDenied;
        }

        string callerId = Caller(httpContext);
        if (!string.IsNullOrWhiteSpace(subject_pseudonym)
            && !string.Equals(subject_pseudonym.Trim(), callerId, StringComparison.Ordinal))
        {
            return CrossParticipantDenied();
        }

        var pendingRows = await dbContext.ReconsentRequirements
            .AsNoTracking()
            .Where(r => r.StudyId == study_id && r.SubjectPseudonym == callerId && r.Status == "PENDING")
            .ToListAsync(cancellationToken);
        var requirements = pendingRows
            .OrderByDescending(r => r.NewVersionIndex)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();

        var items = new List<PendingReconsentItemDto>(requirements.Count);
        foreach (var req in requirements)
        {
            var template = await dbContext.ConsentTemplates
                .AsNoTracking()
                .Where(t => t.TemplateId == req.TemplateId && t.VersionIndex == req.NewVersionIndex)
                .FirstOrDefaultAsync(cancellationToken);

            var check = await ResolveLatestComprehensionCheckAsync(req.TemplateId, req.NewVersionIndex, dbContext, cancellationToken);

            string protocolVersion = !string.IsNullOrWhiteSpace(template?.ProtocolVersion)
                ? template.ProtocolVersion
                : $"v{req.NewVersionIndex}.0";

            items.Add(new PendingReconsentItemDto(
                Id: req.Id,
                RequirementId: req.Id,
                StudyCode: req.StudyId,
                SiteCode: req.SiteId,
                TemplateId: req.TemplateId,
                PriorVersionIndex: req.PriorVersionIndex,
                NewVersionIndex: req.NewVersionIndex,
                VersionIndex: req.NewVersionIndex,
                ProtocolVersion: protocolVersion,
                SubjectPseudonym: req.SubjectPseudonym,
                Status: req.Status,
                ChangeSummary: req.ChangeSummary,
                DeadlineAt: req.DeadlineAt,
                ClauseDiffs: ParseJsonOrFallback(req.SubstantiveChangesJson, "[]"),
                ComprehensionQuestions: check is not null
                    ? SanitizeQuestionsJson(check.QuestionsJson)
                    : ParseJsonOrFallback("[]", "[]")));
        }

        return TypedResults.Ok(new PendingReconsentListResponse(
            StudyCode: study_id,
            SubjectPseudonym: callerId,
            PendingItems: items));
    }

    public static async Task<IResult> CompleteReconsentAsync(
        string requirement_id,
        CompleteReconsentRequest? request,
        EconsentDbContext dbContext,
        HttpContext httpContext,
        IClock clock,
        IEconsentExecutionWebhookPublisher? webhookPublisher = null,
        CancellationToken cancellationToken = default)
    {
        if (ClinicalScopeGuard.RequireAnyStudyScope(httpContext) is { } callerScopeDenied)
        {
            return callerScopeDenied;
        }

        string callerId = Caller(httpContext);
        if (!string.IsNullOrWhiteSpace(request?.SubjectPseudonym)
            && !string.Equals(request.SubjectPseudonym.Trim(), callerId, StringComparison.Ordinal))
        {
            return CrossParticipantDenied();
        }

        var requirement = await dbContext.ReconsentRequirements
            .FirstOrDefaultAsync(r => r.Id == requirement_id, cancellationToken);

        if (requirement is null)
        {
            return TypedResults.NotFound(new { detail = $"Re-consent requirement '{requirement_id}' was not found." });
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, requirement.StudyId))
        {
            return OutsideStudy();
        }

        if (!string.IsNullOrWhiteSpace(requirement.SiteId)
            && ClinicalScopeGuard.RequireSite(httpContext, requirement.SiteId) is { } reqSiteDenied)
        {
            return reqSiteDenied;
        }

        if (!string.Equals(requirement.SubjectPseudonym, callerId, StringComparison.Ordinal))
        {
            return CrossParticipantDenied();
        }

        if (string.Equals(requirement.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                title: "Re-consent already completed",
                detail: "This protocol amendment re-consent requirement has already been signed.",
                statusCode: StatusCodes.Status409Conflict);
        }

        Principal? principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        string? effectiveSiteId = ResolveFirstNonEmpty(
            requirement.SiteId,
            request?.SiteCode,
            principal?.AssignedSites.FirstOrDefault());

        if (!string.IsNullOrWhiteSpace(effectiveSiteId)
            && ClinicalScopeGuard.RequireSite(httpContext, effectiveSiteId) is { } effectiveSiteDenied)
        {
            return effectiveSiteDenied;
        }

        DateTimeOffset now = clock.UtcNow;
        var check = await ResolveLatestComprehensionCheckAsync(requirement.TemplateId, requirement.NewVersionIndex, dbContext, cancellationToken);

        if (check is not null)
        {
            if (request?.ComprehensionAnswers is { Count: > 0 } submittedAnswers)
            {
                var (passed, score, _, _) = GradeComprehensionAnswers(
                    check.ExpectedAnswersJson,
                    check.ThresholdPolicyJson,
                    submittedAnswers);

                dbContext.ComprehensionResults.Add(new ComprehensionResult
                {
                    Id = Guid.NewGuid().ToString(),
                    TemplateId = check.TemplateId,
                    VersionIndex = requirement.NewVersionIndex,
                    SubjectPseudonym = callerId,
                    QuestionsJson = check.QuestionsJson,
                    ExpectedAnswersJson = check.ExpectedAnswersJson,
                    ThresholdPolicyJson = check.ThresholdPolicyJson,
                    SubmittedAnswersJson = JsonSerializer.Serialize(submittedAnswers),
                    Passed = passed,
                    Score = score,
                    CreatedAt = now,
                    CreatedBy = callerId,
                    ReasonForChange = "Amendment re-consent comprehension check",
                });

                if (!passed)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["comprehension_answers"] = ["Comprehension answers did not meet the required threshold."]
                    });
                }
            }
            else
            {
                bool alreadyPassed = await dbContext.ComprehensionResults
                    .AsNoTracking()
                    .AnyAsync(
                        r => r.TemplateId == requirement.TemplateId
                            && r.VersionIndex == requirement.NewVersionIndex
                            && r.SubjectPseudonym == callerId
                            && r.Passed,
                        cancellationToken);

                if (!alreadyPassed)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["comprehension_answers"] = ["Comprehension check must be completed and passed before signing re-consent."]
                    });
                }
            }
        }

        var template = await dbContext.ConsentTemplates
            .AsNoTracking()
            .Where(t => t.TemplateId == requirement.TemplateId && t.VersionIndex == requirement.NewVersionIndex)
            .FirstOrDefaultAsync(cancellationToken);

        string protocolVersion = ResolveFirstNonEmpty(
            request?.ProtocolVersion,
            template?.ProtocolVersion) ?? $"v{requirement.NewVersionIndex}.0";

        string meaning = ResolveFirstNonEmpty(
            request?.Meaning,
            request?.SigningReason) ?? "I have read and agree to the amended consent terms";

        string signerName = !string.IsNullOrWhiteSpace(request?.SignerName) ? request.SignerName.Trim() : callerId;
        string signerRole = !string.IsNullOrWhiteSpace(request?.SignerRole) ? request.SignerRole.Trim() : "Subject";
        string digestHash = !string.IsNullOrWhiteSpace(request?.DigestSha256)
            ? request.DigestSha256.Trim().ToLowerInvariant()
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{requirement.TemplateId}|{requirement.NewVersionIndex}|{protocolVersion}|{requirement.SubstantiveChangesJson}")));
        string signatureData = !string.IsNullOrWhiteSpace(request?.SignatureData)
            ? request.SignatureData
            : $"reconsent-signed:{callerId}:{requirement.Id}";
        string bindingSha256 = ConsentSignatureBinding.Compute(digestHash, signatureData, null);

        string manifestJson = JsonSerializer.Serialize(new
        {
            signer_name = signerName,
            signer_role = signerRole,
            meaning,
            digest = digestHash,
            reconsent_requirement_id = requirement.Id,
        });

        var priorActiveConsents = await dbContext.SubjectConsents
            .Where(c => c.StudyId == requirement.StudyId
                        && c.SubjectPseudonym == callerId
                        && c.TemplateId == requirement.TemplateId
                        && c.VersionIndex < requirement.NewVersionIndex
                        && c.Status == "ACTIVE")
            .ToListAsync(cancellationToken);
        foreach (var prior in priorActiveConsents)
        {
            prior.Status = "SUPERSEDED";
            prior.ReasonForChange = $"Superseded by amendment v{requirement.NewVersionIndex}";
        }

        var consentRecord = new SubjectConsent
        {
            Id = Guid.NewGuid().ToString(),
            SubjectPseudonym = callerId,
            StudyId = requirement.StudyId,
            SiteId = effectiveSiteId ?? string.Empty,
            TemplateId = requirement.TemplateId,
            VersionIndex = requirement.NewVersionIndex,
            ProtocolVersion = protocolVersion,
            SourceContentIdentity = digestHash,
            Status = "ACTIVE",
            ServerTimestamp = now,
            DeviceTimestamp = request?.DeviceTimestamp ?? now,
            CreatedAt = now,
            CreatedBy = callerId,
            ReasonForChange = meaning,
            SignatureManifestJson = manifestJson,
        };

        var signatureRecord = new ConsentSignature
        {
            Id = Guid.NewGuid().ToString(),
            TemplateId = requirement.TemplateId,
            VersionIndex = requirement.NewVersionIndex,
            SubjectPseudonym = callerId,
            Role = signerRole.ToUpperInvariant(),
            SignerName = signerName,
            SignerEmail = request?.SignerEmail,
            Meaning = meaning,
            SignedAt = now,
            DigestSha256 = digestHash,
            SignatureBindingSha256 = bindingSha256,
            CreatedBy = callerId,
            ReasonForChange = meaning,
        };

        requirement.Status = "COMPLETED";
        requirement.CompletedConsentId = consentRecord.Id;
        requirement.ReasonForChange = meaning;

        string tenantId = principal?.TenantId ?? CallerScope.TenantId(httpContext) ?? string.Empty;
        var auditRecord = new ConsentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = callerId,
            ActorRole = signerRole,
            Action = "RECONSENT_SIGNED",
            DocumentId = consentRecord.Id,
            Details = $"Protocol amendment re-consent {requirement.Id} signed for {callerId} ({protocolVersion})",
            ReasonForChange = meaning,
            TenantId = tenantId,
            Sha256Digest = bindingSha256,
        };

        dbContext.SubjectConsents.Add(consentRecord);
        dbContext.ConsentSignatures.Add(signatureRecord);
        dbContext.ConsentAuditLogs.Add(auditRecord);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (webhookPublisher is not null)
        {
            await webhookPublisher.PublishConsentSignedAsync(
                new EconsentSignedSyncMessage(
                    SubjectPseudonym: callerId,
                    StudyCode: requirement.StudyId,
                    SiteCode: effectiveSiteId,
                    ProtocolVersion: protocolVersion,
                    VersionIndex: requirement.NewVersionIndex,
                    SignedAt: now,
                    ConsentId: consentRecord.Id,
                    TenantId: tenantId,
                    ReasonForChange: meaning),
                cancellationToken);
        }

        return TypedResults.Ok(new CompleteReconsentResponse(
            Id: requirement.Id,
            RequirementId: requirement.Id,
            CompletedConsentId: consentRecord.Id,
            TemplateId: requirement.TemplateId,
            VersionIndex: requirement.NewVersionIndex,
            ProtocolVersion: protocolVersion,
            SubjectPseudonym: callerId,
            Status: requirement.Status,
            ServerTimestamp: now,
            Sha256Hash: digestHash));
    }

    private static async Task<IResult> CaptureConsentAsync(
        string template_id,
        int version_index,
        CaptureConsentRequest request,
        EconsentDbContext dbContext,
        HttpContext httpContext,
        IClock clock,
        IEconsentExecutionWebhookPublisher webhookPublisher,
        CancellationToken cancellationToken)
    {
        return await ProcessCaptureConsentAsync(
            template_id,
            version_index,
            request,
            dbContext,
            httpContext,
            clock,
            cancellationToken,
            webhookPublisher);
    }

    private static async Task<IResult> CaptureConsentLegacyAsync(
        string template_id,
        CaptureConsentRequest request,
        EconsentDbContext dbContext,
        HttpContext httpContext,
        IClock clock,
        IEconsentExecutionWebhookPublisher webhookPublisher,
        CancellationToken cancellationToken)
    {
        return await ProcessCaptureConsentAsync(
            template_id,
            1,
            request,
            dbContext,
            httpContext,
            clock,
            cancellationToken,
            webhookPublisher);
    }

    public static async Task<IResult> ProcessCaptureConsentAsync(
        string templateId,
        int versionIndex,
        CaptureConsentRequest request,
        EconsentDbContext dbContext,
        HttpContext httpContext,
        IClock clock,
        CancellationToken cancellationToken,
        IEconsentExecutionWebhookPublisher? webhookPublisher = null)
    {
        if (request is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Request body is required."]
            });
        }

        bool isStaffCapture = IsStaffConsentCapturer(httpContext);
        if (isStaffCapture)
        {
            if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext) is { } staffScopeDenied)
            {
                return staffScopeDenied;
            }
        }
        else
        {
            if (ClinicalScopeGuard.RequireAnyStudyScope(httpContext) is { } selfScopeDenied)
            {
                return selfScopeDenied;
            }

            string callerUserId = Caller(httpContext);
            if (!string.IsNullOrWhiteSpace(request.SubjectPseudonym)
                && !string.Equals(request.SubjectPseudonym.Trim(), callerUserId, StringComparison.Ordinal))
            {
                return CrossParticipantDenied();
            }
        }

        if (!string.IsNullOrWhiteSpace(request.StudyId)
            && ClinicalScopeGuard.RequireStudy(httpContext, request.StudyId) is { } studyDenied)
        {
            return studyDenied;
        }

        Principal? principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        string? effectiveSiteId = null;
        if (!string.IsNullOrWhiteSpace(request.SiteId))
        {
            effectiveSiteId = request.SiteId;
        }
        else if (!isStaffCapture)
        {
            effectiveSiteId = principal?.AssignedSites.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(effectiveSiteId)
            && ClinicalScopeGuard.RequireSite(httpContext, effectiveSiteId) is { } siteDenied)
        {
            return siteDenied;
        }

        var existingTemplate = await dbContext.ConsentTemplates
            .AsNoTracking()
            .Where(t => t.TemplateId == templateId && t.VersionIndex == versionIndex)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await dbContext.ConsentTemplates
                .AsNoTracking()
                .Where(t => t.TemplateId == templateId)
                .OrderByDescending(t => t.VersionIndex)
                .FirstOrDefaultAsync(cancellationToken);

        if (existingTemplate is not null && !ClinicalScopeGuard.CanAccessStudy(httpContext, existingTemplate.StudyId))
        {
            return OutsideStudy();
        }

        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.SubjectPseudonym))
        {
            errors["subject_pseudonym"] = ["SubjectPseudonym is required."];
        }

        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            errors["study_id"] = ["StudyId is required."];
        }

        if (isStaffCapture && string.IsNullOrWhiteSpace(effectiveSiteId))
        {
            errors["site_id"] = ["SiteId is required."];
        }

        if (string.IsNullOrWhiteSpace(request.DigestSha256))
        {
            errors["digest_sha256"] = ["DigestSha256 is required."];
        }

        if (string.IsNullOrWhiteSpace(request.SignatureData))
        {
            errors["signature_data"] = ["SignatureData is required."];
        }

        DateTimeOffset now = clock.UtcNow;

        BiometricLivenessTokenData? livenessData = null;
        if (string.IsNullOrWhiteSpace(request.LivenessToken))
        {
            errors["liveness_token"] = ["Valid liveness token is required."];
        }
        else if (!BiometricLivenessTokenService.VerifyToken(request.LivenessToken, request.SubjectPseudonym, out livenessData, now: now))
        {
            errors["liveness_token"] = ["Liveness token is invalid or expired."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (isStaffCapture)
        {
            if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, effectiveSiteId) is { } denied)
            {
                return denied;
            }
        }
        else if (ClinicalScopeGuard.RequireStudy(httpContext, request.StudyId) is { } selfStudyDenied)
        {
            return selfStudyDenied;
        }

        string subjectPseudonym = request.SubjectPseudonym!.Trim();
        if (!isStaffCapture)
        {
            bool hasComprehensionCheck = await dbContext.ComprehensionChecks
                .AsNoTracking()
                .AnyAsync(c => c.TemplateId == templateId && c.VersionIndex == versionIndex, cancellationToken);

            if (hasComprehensionCheck)
            {
                bool passedComprehension = await dbContext.ComprehensionResults
                    .AsNoTracking()
                    .AnyAsync(
                        r => r.TemplateId == templateId
                            && r.VersionIndex == versionIndex
                            && r.SubjectPseudonym == subjectPseudonym
                            && r.Passed,
                        cancellationToken);

                if (!passedComprehension)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["comprehension"] = ["Participant must pass the comprehension check before signing consent."]
                    });
                }
            }
        }

        string capturedBy = Caller(httpContext);
        string studyId = request.StudyId!.Trim();
        string siteId = effectiveSiteId ?? string.Empty;
        string digestHash = request.DigestSha256!;
        string signatureData = request.SignatureData!;
        string livenessToken = request.LivenessToken!;
        string livenessTokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(livenessToken)));

        string signerName = !string.IsNullOrWhiteSpace(request.SignerName) ? request.SignerName : subjectPseudonym;
        string signerRole = !string.IsNullOrWhiteSpace(request.SignerRole) ? request.SignerRole : "Subject";

        string meaning;
        if (!string.IsNullOrWhiteSpace(request.Meaning))
        {
            meaning = request.Meaning;
        }
        else if (!string.IsNullOrWhiteSpace(request.SigningReason))
        {
            meaning = request.SigningReason;
        }
        else
        {
            meaning = "I agree to participate";
        }

        string protocolVersion = ResolveFirstNonEmpty(
            request.ProtocolVersion,
            existingTemplate?.ProtocolVersion) ?? "v1.0";

        var bindingSha256 = ConsentSignatureBinding.Compute(digestHash, signatureData, livenessTokenHash);

        var manifestData = new
        {
            signer_name = signerName,
            signer_role = signerRole,
            meaning,
            digest = digestHash
        };
        string signatureManifestJson = JsonSerializer.Serialize(manifestData);

        var consentRecord = new SubjectConsent
        {
            Id = Guid.NewGuid().ToString(),
            SubjectPseudonym = subjectPseudonym,
            StudyId = studyId,
            SiteId = siteId,
            TemplateId = templateId,
            VersionIndex = versionIndex,
            ProtocolVersion = protocolVersion,
            SourceContentIdentity = digestHash,
            Status = "ACTIVE",
            ServerTimestamp = now,
            DeviceTimestamp = request.DeviceTimestamp ?? now,
            CreatedAt = now,
            CreatedBy = capturedBy,
            ReasonForChange = meaning,
            SignatureManifestJson = signatureManifestJson
        };

        var signatureRecord = new ConsentSignature
        {
            Id = Guid.NewGuid().ToString(),
            TemplateId = templateId,
            VersionIndex = versionIndex,
            SubjectPseudonym = subjectPseudonym,
            Role = signerRole.ToUpperInvariant(),
            SignerName = signerName,
            SignerEmail = request.SignerEmail,
            Meaning = meaning,
            SignedAt = now,
            DigestSha256 = digestHash,
            SignatureBindingSha256 = bindingSha256,
            CreatedBy = capturedBy,
            ReasonForChange = meaning
        };

        var livenessRecord = new BiometricLivenessVerification
        {
            Id = Guid.NewGuid().ToString(),
            ConsentId = consentRecord.Id,
            SubjectPseudonym = subjectPseudonym,
            LivenessToken = livenessToken,
            LivenessTokenHash = livenessTokenHash,
            AntiSpoofScore = livenessData?.AntiSpoofScore ?? 0.98m,
            IsLive = livenessData?.IsLive ?? true,
            Status = "VERIFIED",
            VerifiedAt = livenessData is not null ? DateTimeOffset.FromUnixTimeSeconds(livenessData.IssuedAtUnix) : now,
            CreatedAt = now,
            CreatedBy = capturedBy,
            ReasonForChange = "Facial liveness verification completed"
        };

        string tenantId = principal?.TenantId ?? CallerScope.TenantId(httpContext) ?? string.Empty;
        var auditRecord = new ConsentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = capturedBy,
            ActorRole = signerRole,
            Action = "BIOMETRIC_LIVENESS_VERIFIED",
            DocumentId = consentRecord.Id,
            Details = $"Biometric liveness verified with anti-spoof score {livenessRecord.AntiSpoofScore} for subject {subjectPseudonym}",
            ReasonForChange = "Liveness verification for consent capture",
            TenantId = tenantId,
            Sha256Digest = livenessTokenHash
        };

        var matchingPendingReconsents = await dbContext.ReconsentRequirements
            .Where(r => r.StudyId == studyId
                        && r.SubjectPseudonym == subjectPseudonym
                        && r.TemplateId == templateId
                        && r.NewVersionIndex <= versionIndex
                        && r.Status == "PENDING")
            .ToListAsync(cancellationToken);
        foreach (var pending in matchingPendingReconsents)
        {
            pending.Status = "COMPLETED";
            pending.CompletedConsentId = consentRecord.Id;
            pending.ReasonForChange = meaning;
        }

        dbContext.SubjectConsents.Add(consentRecord);
        dbContext.ConsentSignatures.Add(signatureRecord);
        dbContext.BiometricLivenessVerifications.Add(livenessRecord);
        dbContext.ConsentAuditLogs.Add(auditRecord);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (webhookPublisher is not null)
        {
            await webhookPublisher.PublishConsentSignedAsync(
                new EconsentSignedSyncMessage(
                    SubjectPseudonym: subjectPseudonym,
                    StudyCode: studyId,
                    SiteCode: string.IsNullOrWhiteSpace(siteId) ? null : siteId,
                    ProtocolVersion: protocolVersion,
                    VersionIndex: versionIndex,
                    SignedAt: now,
                    ConsentId: consentRecord.Id,
                    TenantId: tenantId,
                    ReasonForChange: meaning),
                cancellationToken);
        }

        var response = new CaptureConsentResponse(
            Id: consentRecord.Id,
            TemplateId: templateId,
            VersionIndex: versionIndex,
            SubjectPseudonym: subjectPseudonym,
            SignerName: signerName,
            SignerRole: signerRole,
            Meaning: meaning,
            ServerTimestamp: now,
            Status: "ACTIVE",
            Sha256Hash: digestHash);

        return TypedResults.Ok(response);
    }

    private static async Task<IResult> PublishTemplateVersionedAsync(
        string template_id,
        int version_index,
        PublishTemplateRequest? request,
        EconsentDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        HttpContext httpContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        return await ProcessPublishTemplateAsync(template_id, version_index, request, dbContext, publishEndpoint, httpContext, clock, cancellationToken);
    }

    private static async Task<IResult> PublishTemplateAsync(
        string template_id,
        PublishTemplateRequest? request,
        EconsentDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        HttpContext httpContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        return await ProcessPublishTemplateAsync(template_id, 1, request, dbContext, publishEndpoint, httpContext, clock, cancellationToken);
    }

    public static async Task<IResult> ProcessPublishTemplateAsync(
        string templateId,
        int versionIndex,
        PublishTemplateRequest? request,
        EconsentDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        HttpContext httpContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        var template = await dbContext.ConsentTemplates
            .FirstOrDefaultAsync(t => t.TemplateId == templateId && t.VersionIndex == versionIndex, cancellationToken)
            ?? await dbContext.ConsentTemplates
                .FirstOrDefaultAsync(t => t.TemplateId == templateId, cancellationToken);

        string? targetStudy = !string.IsNullOrWhiteSpace(request?.StudyId) ? request.StudyId : template?.StudyId;
        if (string.IsNullOrWhiteSpace(targetStudy))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["study_id"] = ["StudyId is required to publish a new template."]
            });
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, targetStudy)
            || (template is not null && !ClinicalScopeGuard.CanAccessStudy(httpContext, template.StudyId)))
        {
            return OutsideStudy();
        }

        string actorId = Caller(httpContext);
        if (template is null)
        {
            template = new ConsentTemplate
            {
                Id = Guid.NewGuid().ToString(),
                TemplateId = templateId,
                StudyId = targetStudy,
                TemplateName = $"Consent Template {templateId}",
                ProtocolVersion = !string.IsNullOrWhiteSpace(request?.ProtocolVersion) ? request.ProtocolVersion : $"v{versionIndex}.0",
                IsPublished = true,
                RequiresReconsent = request?.RequiresReconsent ?? true,
                VersionIndex = versionIndex,
                CreatedAt = now,
                CreatedBy = actorId,
                ReasonForChange = request?.ChangeSummary ?? request?.ChangeReason ?? "Publishing template"
            };
            dbContext.ConsentTemplates.Add(template);
        }
        else
        {
            template.IsPublished = true;
            template.VersionIndex = versionIndex;
            if (request?.RequiresReconsent is bool requiresReconsent)
            {
                template.RequiresReconsent = requiresReconsent;
            }
            if (!string.IsNullOrWhiteSpace(request?.ProtocolVersion))
            {
                template.ProtocolVersion = request.ProtocolVersion;
            }
            if (!string.IsNullOrWhiteSpace(request?.StudyId))
            {
                template.StudyId = request.StudyId;
            }
            if (!string.IsNullOrWhiteSpace(request?.ChangeSummary) || !string.IsNullOrWhiteSpace(request?.ChangeReason))
            {
                template.ReasonForChange = request.ChangeSummary ?? request.ChangeReason!;
            }
        }

        if (template.RequiresReconsent)
        {
            string changeSummary;
            if (!string.IsNullOrWhiteSpace(request?.ChangeSummary))
            {
                changeSummary = request.ChangeSummary;
            }
            else if (!string.IsNullOrWhiteSpace(request?.ChangeReason))
            {
                changeSummary = request.ChangeReason;
            }
            else
            {
                changeSummary = "Updated informed consent form requires re-consent.";
            }

            var activePriorConsents = await dbContext.SubjectConsents
                .AsNoTracking()
                .Where(c => c.StudyId == template.StudyId
                            && c.TemplateId == template.TemplateId
                            && c.VersionIndex < template.VersionIndex
                            && c.Status == "ACTIVE")
                .ToListAsync(cancellationToken);

            foreach (var subjectGroup in activePriorConsents.GroupBy(c => c.SubjectPseudonym, StringComparer.Ordinal))
            {
                var latestPrior = subjectGroup.OrderByDescending(c => c.VersionIndex).First();
                bool alreadyPending = await dbContext.ReconsentRequirements
                    .AnyAsync(
                        r => r.StudyId == template.StudyId
                            && r.TemplateId == template.TemplateId
                            && r.NewVersionIndex == template.VersionIndex
                            && r.SubjectPseudonym == latestPrior.SubjectPseudonym
                            && r.Status == "PENDING",
                        cancellationToken);

                if (!alreadyPending)
                {
                    dbContext.ReconsentRequirements.Add(new ReconsentRequirement
                    {
                        Id = Guid.NewGuid().ToString(),
                        StudyId = template.StudyId,
                        SiteId = string.IsNullOrWhiteSpace(latestPrior.SiteId) ? null : latestPrior.SiteId,
                        TemplateId = template.TemplateId,
                        PriorVersionIndex = latestPrior.VersionIndex,
                        NewVersionIndex = template.VersionIndex,
                        SubjectPseudonym = latestPrior.SubjectPseudonym,
                        Status = "PENDING",
                        ChangeSummary = changeSummary,
                        SubstantiveChangesJson = "[]",
                        CreatedAt = now,
                        CreatedBy = actorId,
                        ReasonForChange = changeSummary,
                    });
                }
            }

            var reconsentEvent = new EconsentReconsentRequired(
                StudyId: template.StudyId,
                TemplateId: template.TemplateId,
                VersionIndex: template.VersionIndex,
                ChangeSummary: changeSummary,
                Channels: request?.Channels ?? ["SMS", "Mobile Push", "Email"],
                ProtocolVersion: template.ProtocolVersion,
                OccurredAt: now
            );

            await publishEndpoint.Publish(reconsentEvent, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new PublishTemplateResponse(
            TemplateId: template.TemplateId,
            VersionIndex: template.VersionIndex,
            IsPublished: template.IsPublished,
            RequiresReconsent: template.RequiresReconsent,
            PublishedAt: now);

        return TypedResults.Ok(response);
    }

    private static async Task<ComprehensionCheck?> ResolveLatestComprehensionCheckAsync(
        string templateId,
        int versionIndex,
        EconsentDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var checks = await dbContext.ComprehensionChecks
            .AsNoTracking()
            .Where(c => c.TemplateId == templateId && c.VersionIndex == versionIndex)
            .ToListAsync(cancellationToken);
        return checks
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();
    }

    private static async Task<ConsentTemplate?> ResolveScopedTemplateAsync(
        string templateId,
        int versionIndex,
        EconsentDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.ConsentTemplates
                .AsNoTracking()
                .Where(t => t.TemplateId == templateId && t.VersionIndex == versionIndex)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await dbContext.ConsentTemplates
                .AsNoTracking()
                .Where(t => t.TemplateId == templateId)
                .OrderByDescending(t => t.VersionIndex)
                .FirstOrDefaultAsync(cancellationToken);
    }

    private static List<ApprovedConsentClauseDto> ComposeApprovedClauses(
        ConsentTemplate template,
        IReadOnlyList<ConsentClause> studyClauses,
        IReadOnlyList<ConsentTranslation> approvedTranslations)
    {
        var result = new List<ApprovedConsentClauseDto>();
        var byClauseKey = new Dictionary<string, ConsentClause>(StringComparer.OrdinalIgnoreCase);
        foreach (var clause in studyClauses)
        {
            if (!string.IsNullOrWhiteSpace(clause.ClauseId))
            {
                byClauseKey[clause.ClauseId] = clause;
            }
            byClauseKey[clause.Id] = clause;
        }

        bool parsedFromTemplate = false;
        if (!string.IsNullOrWhiteSpace(template.ClausesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(template.ClausesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    parsedFromTemplate = true;
                    int index = 0;
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        index++;
                        if (elem.ValueKind == JsonValueKind.String)
                        {
                            string key = elem.GetString() ?? string.Empty;
                            if (byClauseKey.TryGetValue(key, out var matched))
                            {
                                result.Add(ApplyClauseTranslation(matched.Id, matched.ClauseId, matched.Title, matched.Text, matched.VersionIndex, approvedTranslations));
                            }
                        }
                        else if (elem.ValueKind == JsonValueKind.Object)
                        {
                            string clauseId = ReadStringProperty(elem, "clause_id")
                                ?? ReadStringProperty(elem, "id")
                                ?? $"clause-{index}";
                            string title = ReadStringProperty(elem, "title") ?? $"Section {index}";
                            string text = ReadStringProperty(elem, "text")
                                ?? ReadStringProperty(elem, "content")
                                ?? string.Empty;
                            int clauseVersion = elem.TryGetProperty("version_index", out var vProp) && vProp.TryGetInt32(out int vi)
                                ? vi
                                : template.VersionIndex;
                            if (byClauseKey.TryGetValue(clauseId, out var dbClause))
                            {
                                title = dbClause.Title;
                                text = dbClause.Text;
                                clauseVersion = dbClause.VersionIndex;
                            }
                            result.Add(ApplyClauseTranslation(clauseId, clauseId, title, text, clauseVersion, approvedTranslations));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                parsedFromTemplate = false;
            }
        }

        if (!parsedFromTemplate || result.Count == 0)
        {
            foreach (var clause in studyClauses)
            {
                result.Add(ApplyClauseTranslation(clause.Id, clause.ClauseId, clause.Title, clause.Text, clause.VersionIndex, approvedTranslations));
            }
        }

        return result;
    }

    private static ApprovedConsentClauseDto ApplyClauseTranslation(
        string id,
        string clauseId,
        string title,
        string text,
        int versionIndex,
        IReadOnlyList<ConsentTranslation> approvedTranslations)
    {
        var translation = approvedTranslations.FirstOrDefault(tr =>
            string.Equals(tr.SourceId, clauseId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tr.SourceId, id, StringComparison.OrdinalIgnoreCase));

        string resolvedTitle = !string.IsNullOrWhiteSpace(translation?.TranslatedTitle) ? translation.TranslatedTitle : title;
        string resolvedText = !string.IsNullOrWhiteSpace(translation?.TranslatedText) ? translation.TranslatedText : text;

        return new ApprovedConsentClauseDto(
            Id: id,
            ClauseId: string.IsNullOrWhiteSpace(clauseId) ? id : clauseId,
            Title: resolvedTitle,
            Text: resolvedText,
            Content: resolvedText,
            VersionIndex: versionIndex);
    }

    private static JsonElement SanitizeQuestionsJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return ParseJsonOrFallback("[]", "[]");
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return ParseJsonOrFallback("[]", "[]");
            }

            var sanitized = new List<Dictionary<string, object?>>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var clean = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var prop in item.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "expected_answer", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(prop.Name, "correct_answer", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(prop.Name, "correct_option", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    clean[prop.Name] = prop.Value.Clone();
                }
                sanitized.Add(clean);
            }

            using var outDoc = JsonDocument.Parse(JsonSerializer.Serialize(sanitized));
            return outDoc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ParseJsonOrFallback("[]", "[]");
        }
    }

    private static (bool Passed, decimal Score, int CorrectCount, int TotalQuestions) GradeComprehensionAnswers(
        string expectedAnswersJson,
        string thresholdPolicyJson,
        IReadOnlyDictionary<string, string> submittedAnswers)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(expectedAnswersJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(expectedAnswersJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        expected[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                            ? (prop.Value.GetString() ?? string.Empty)
                            : prop.Value.ToString();
                    }
                }
            }
            catch (JsonException)
            {
                // Keep empty dictionary if malformed
            }
        }

        int total = expected.Count;
        if (total == 0)
        {
            return (true, 100m, 0, 0);
        }

        int correct = 0;
        foreach (var (questionId, expectedAnswer) in expected)
        {
            if (submittedAnswers.TryGetValue(questionId, out string? actual)
                && string.Equals(actual?.Trim(), expectedAnswer.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                correct++;
            }
        }

        decimal score = Math.Round((decimal)correct * 100m / total, 2, MidpointRounding.AwayFromZero);
        int? minCorrect = null;
        decimal requiredScore = 100m;

        if (!string.IsNullOrWhiteSpace(thresholdPolicyJson))
        {
            try
            {
                using var policyDoc = JsonDocument.Parse(thresholdPolicyJson);
                if (policyDoc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (policyDoc.RootElement.TryGetProperty("min_correct", out var minCorrectProp)
                        && minCorrectProp.TryGetInt32(out int mc))
                    {
                        minCorrect = mc;
                    }
                    else if (policyDoc.RootElement.TryGetProperty("required_score", out var reqProp)
                        && reqProp.TryGetDecimal(out decimal rs))
                    {
                        requiredScore = rs;
                    }
                    else if (policyDoc.RootElement.TryGetProperty("passing_score", out var passProp)
                        && passProp.TryGetDecimal(out decimal ps))
                    {
                        requiredScore = ps <= 1m && ps > 0m ? ps * 100m : ps;
                    }
                    else if (policyDoc.RootElement.TryGetProperty("min_score_percent", out var pctProp)
                        && pctProp.TryGetDecimal(out decimal mp))
                    {
                        requiredScore = mp;
                    }
                }
            }
            catch (JsonException)
            {
                // Default to 100% required score
            }
        }

        bool passed = minCorrect.HasValue ? correct >= minCorrect.Value : score >= requiredScore;
        return (passed, score, correct, total);
    }

    private static JsonElement ParseJsonOrFallback(string? rawJson, string fallbackJson)
    {
        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Fall back to default literal below
            }
        }

        using var fallbackDoc = JsonDocument.Parse(fallbackJson);
        return fallbackDoc.RootElement.Clone();
    }

    private static string? ReadStringProperty(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static string? ResolveFirstNonEmpty(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }
        return null;
    }
}
