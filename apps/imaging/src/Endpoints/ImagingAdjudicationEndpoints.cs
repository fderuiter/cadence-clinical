using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Imaging.Endpoints;

public record ReaderAssessmentDto(
    string ReaderId,
    string ReaderName,
    double SumLongestDiametersMm,
    string TargetResponse,
    string OverallResponse,
    bool NewLesionsPresent,
    List<string>? Notes = null);

public record SubmitAdjudicationSessionRequest(
    string StudyId,
    string SiteId,
    string SubjectId,
    string TimepointName,
    ReaderAssessmentDto Reader1Assessment,
    ReaderAssessmentDto Reader2Assessment,
    string AssessmentCriteria = "RECIST_1_1");

public record ChairAdjudicateImagingRequest(
    string FinalConsensusResponse,
    double FinalConsensusSldMm,
    string BindingReader,
    string AdjudicationRationale,
    string? SignerName = null,
    string SignatureMeaning = "CEC Chair Adjudication Consensus Sign-Off");

public record AdjudicationSessionResponse(
    string SessionId,
    string StudyId,
    string SubjectId,
    string TimepointName,
    string WorkflowStatus,
    bool IsResponseDiscordant,
    double SldDiscordanceDelta,
    List<string> DiscordanceReasons,
    string? FinalConsensusResponse,
    double? FinalConsensusSldMm,
    string? BindingReader,
    string? AdjudicationRationale,
    string SessionDigest);

public static class ImagingAdjudicationEndpoints
{
    public const string ChairAdjudicateSemanticAction = "imaging.adjudication.chair_adjudicate";

    public static string ChairAdjudicateAction(string sessionId) =>
        $"/api/v1/imaging/adjudications/{sessionId}/chair-adjudicate";

    private static string ChairAdjudicateActionFor(HttpContext httpContext) =>
        ChairAdjudicateAction(httpContext.Request.RouteValues["sessionId"]?.ToString()?.Trim() ?? string.Empty);

    public static IEndpointRouteBuilder MapImagingAdjudicationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/imaging/adjudications")
            .WithTags("Imaging Adjudication");

        group.MapPost("/", SubmitOrEvaluateSessionAsync)
            .WithName("SubmitOrEvaluateAdjudicationSession")
            .RequirePermission("imaging_adjudication:submit").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<AdjudicationSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{sessionId}", GetSessionByIdAsync)
            .WithName("GetImagingAdjudicationSession")
            .RequirePermission("imaging_adjudication:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<ImagingAdjudicationSession>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{sessionId}/chair-adjudicate", ChairAdjudicateSessionAsync)
            .WithName("ChairAdjudicateImagingSession")
            .RequirePermission("imaging_adjudication:adjudicate").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(ChairAdjudicateSemanticAction, ChairAdjudicateActionFor)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<AdjudicationSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static string Caller(HttpContext httpContext) =>
        CallerScope.UserId(httpContext) ?? throw new InvalidOperationException("An admitted adjudication request has no signed caller.");

    private static IResult OutsideStudy() =>
        TypedResults.Problem(
            title: "Outside your studies",
            detail: "You are not assigned to this study.",
            statusCode: StatusCodes.Status403Forbidden);

    private static async Task<IResult> SubmitOrEvaluateSessionAsync(
        SubmitAdjudicationSessionRequest request,
        ImagingDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return TypedResults.BadRequest(new { code = "INVALID_REQUEST", message = "Request body is required." });
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, request.StudyId, request.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return OutsideStudy();
        }

        DateTimeOffset now = SystemClock.Instance.UtcNow;

        double r1Sld = request.Reader1Assessment.SumLongestDiametersMm;
        double r2Sld = request.Reader2Assessment.SumLongestDiametersMm;
        double maxSld = Math.Max(r1Sld, r2Sld);

        double sldDeltaPct = maxSld > 0.0 ? (Math.Abs(r1Sld - r2Sld) / maxSld) * 100.0 : 0.0;

        var discordanceReasons = new List<string>();

        if (sldDeltaPct > 20.0)
        {
            discordanceReasons.Add($"SLD variance between Reader 1 ({r1Sld:F1} mm) and Reader 2 ({r2Sld:F1} mm) exceeds 20.0% threshold ({sldDeltaPct:F1}%).");
        }

        if (!string.Equals(request.Reader1Assessment.OverallResponse, request.Reader2Assessment.OverallResponse, StringComparison.OrdinalIgnoreCase))
        {
            discordanceReasons.Add($"Overall response mismatch: Reader 1 assessed '{request.Reader1Assessment.OverallResponse}' while Reader 2 assessed '{request.Reader2Assessment.OverallResponse}'.");
        }

        if (request.Reader1Assessment.NewLesionsPresent != request.Reader2Assessment.NewLesionsPresent)
        {
            discordanceReasons.Add($"New lesion assessment mismatch: Reader 1 ({request.Reader1Assessment.NewLesionsPresent}) vs Reader 2 ({request.Reader2Assessment.NewLesionsPresent}).");
        }

        bool isDiscordant = discordanceReasons.Count > 0;
        string workflowStatus = isDiscordant ? "ESCALATED_TO_CHAIR" : "FINALIZED_CONCORDANT";

        string r1Json = JsonSerializer.Serialize(request.Reader1Assessment);
        string r2Json = JsonSerializer.Serialize(request.Reader2Assessment);

        string sessionDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.StudyId}|{request.SubjectId}|{request.TimepointName}|{r1Sld}|{r2Sld}|{isDiscordant}")));

        var session = new ImagingAdjudicationSession
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = request.StudyId,
            SiteId = request.SiteId,
            SubjectId = request.SubjectId,
            TimepointName = request.TimepointName,
            AssessmentCriteria = request.AssessmentCriteria,
            Reader1Id = request.Reader1Assessment.ReaderId,
            Reader1Name = request.Reader1Assessment.ReaderName,
            Reader1Status = "COMPLETED",
            Reader1AssessmentJson = r1Json,
            Reader2Id = request.Reader2Assessment.ReaderId,
            Reader2Name = request.Reader2Assessment.ReaderName,
            Reader2Status = "COMPLETED",
            Reader2AssessmentJson = r2Json,
            SldDiscordanceDelta = sldDeltaPct,
            IsResponseDiscordant = isDiscordant,
            DiscordanceReasonsJson = JsonSerializer.Serialize(discordanceReasons),
            WorkflowStatus = workflowStatus,
            FinalConsensusResponse = isDiscordant ? null : request.Reader1Assessment.OverallResponse,
            FinalConsensusSldMm = isDiscordant ? null : request.Reader1Assessment.SumLongestDiametersMm,
            BindingReader = isDiscordant ? null : "CONCORDANT_READERS",
            SessionDigest = sessionDigest,
            CreatedAt = now,
            CreatedBy = Caller(httpContext),
            UpdatedAt = now
        };

        dbContext.ImagingAdjudicationSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new AdjudicationSessionResponse(
            SessionId: session.Id,
            StudyId: session.StudyId,
            SubjectId: session.SubjectId,
            TimepointName: session.TimepointName,
            WorkflowStatus: session.WorkflowStatus,
            IsResponseDiscordant: session.IsResponseDiscordant,
            SldDiscordanceDelta: session.SldDiscordanceDelta,
            DiscordanceReasons: discordanceReasons,
            FinalConsensusResponse: session.FinalConsensusResponse,
            FinalConsensusSldMm: session.FinalConsensusSldMm,
            BindingReader: session.BindingReader,
            AdjudicationRationale: session.AdjudicationRationale,
            SessionDigest: session.SessionDigest));
    }

    private static async Task<IResult> GetSessionByIdAsync(
        string sessionId,
        ImagingDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.ImagingAdjudicationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session == null
            || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, session.StudyId, session.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { code = "SESSION_NOT_FOUND", message = $"Adjudication session '{sessionId}' not found." });
        }

        return TypedResults.Ok(session);
    }

    private static async Task<IResult> ChairAdjudicateSessionAsync(
        string sessionId,
        ChairAdjudicateImagingRequest request,
        ImagingDbContext dbContext,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.ImagingAdjudicationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return TypedResults.NotFound(new { code = "SESSION_NOT_FOUND", message = $"Adjudication session '{sessionId}' not found." });
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, session.StudyId, session.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return OutsideStudy();
        }

        // The chair who signs is derived from the verified X-Sig-Token claims (sub + username), never request.SignerName.
        var verifiedToken = httpContext.VerifiedSigToken();
        string signerId = verifiedToken?.Subject is { Length: > 0 } verifiedSub
            ? verifiedSub
            : Caller(httpContext);
        string signerName = verifiedToken?.Claims.FirstOrDefault(c => c.Type == SigTokenClaims.Username)?.Value is { Length: > 0 } verifiedUsername
            ? verifiedUsername
            : signerId;

        if (session.WorkflowStatus is "FINALIZED_BY_CHAIR" or "FINALIZED_CONCORDANT")
        {
            return TypedResults.Conflict(new { code = "SESSION_ALREADY_FINALIZED", message = $"Adjudication session '{sessionId}' is already finalized in status '{session.WorkflowStatus}'." });
        }

        DateTimeOffset now = clock.UtcNow;

        string payloadToHash = $"{session.Id}|{request.FinalConsensusResponse}|{request.FinalConsensusSldMm}|{request.BindingReader}|{request.AdjudicationRationale}";
        string payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadToHash)));

        var signatureManifest = new
        {
            SignerId = signerId,
            SignerName = signerName,
            SignerRole = "CEC_CHAIR",
            SigningReason = request.SignatureMeaning,
            SignedAt = now.ToString("o"),
            PayloadHash = payloadHash
        };

        session.AdjudicatorId = signerId;
        session.AdjudicatorName = signerName;
        session.WorkflowStatus = "FINALIZED_BY_CHAIR";
        session.FinalConsensusResponse = request.FinalConsensusResponse;
        session.FinalConsensusSldMm = request.FinalConsensusSldMm;
        session.BindingReader = request.BindingReader;
        session.AdjudicationRationale = request.AdjudicationRationale;
        session.ElectronicSignatureJson = JsonSerializer.Serialize(signatureManifest);
        session.LockedAt = now;
        session.LockedBy = signerId;
        session.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var discordanceReasons = JsonSerializer.Deserialize<List<string>>(session.DiscordanceReasonsJson) ?? [];

        return TypedResults.Ok(new AdjudicationSessionResponse(
            SessionId: session.Id,
            StudyId: session.StudyId,
            SubjectId: session.SubjectId,
            TimepointName: session.TimepointName,
            WorkflowStatus: session.WorkflowStatus,
            IsResponseDiscordant: session.IsResponseDiscordant,
            SldDiscordanceDelta: session.SldDiscordanceDelta,
            DiscordanceReasons: discordanceReasons,
            FinalConsensusResponse: session.FinalConsensusResponse,
            FinalConsensusSldMm: session.FinalConsensusSldMm,
            BindingReader: session.BindingReader,
            AdjudicationRationale: session.AdjudicationRationale,
            SessionDigest: session.SessionDigest));
    }
}
