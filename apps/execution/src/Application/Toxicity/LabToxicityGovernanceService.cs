using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Contracts;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Toxicity;
using Cadence.Kernel.Time;

using MassTransit;

namespace Cadence.Apps.Execution.Application.Toxicity;

public sealed class LabToxicityGovernanceService(
    IStagedAdverseEventReviewRepository stagedReviewRepository,
    IClinicalQueryRepository queryRepository,
    IPublishEndpoint publishEndpoint,
    IClock clock,
    ICurrentAuditContextAccessor auditContextAccessor) : ILabToxicityGovernanceService
{
    public async Task<Result<IReadOnlyList<StagedAdverseEventReviewResponse>>> ListQueueAsync(
        string? studyId = null,
        string? siteId = null,
        string? reviewStatus = "STAGED_FOR_INVESTIGATOR_REVIEW",
        CancellationToken cancellationToken = default)
    {
        var list = await stagedReviewRepository.ListQueueAsync(studyId, siteId, reviewStatus, cancellationToken).ConfigureAwait(false);
        var dtos = list.Select(MapToResponse).ToList();
        return Result<IReadOnlyList<StagedAdverseEventReviewResponse>>.Success(dtos);
    }

    public async Task<Result<StagedAdverseEventActionResponse>> ExecuteActionAsync(
        string reviewId,
        StagedAdverseEventActionRequest request,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ReasonForChange))
        {
            return DomainError.Validation("REASON_FOR_CHANGE_REQUIRED", "Mandatory GxP 21 CFR Part 11 justification reason is required.");
        }

        var review = await stagedReviewRepository.GetByIdAsync(reviewId, cancellationToken).ConfigureAwait(false);
        if (review is null)
        {
            return DomainError.NotFound("REVIEW_NOT_FOUND", $"Staged adverse event review '{reviewId}' not found.");
        }

        string reviewerId = caller?.UserId ?? auditContextAccessor.UserId ?? "system";
        string actionUpper = request.Action.Trim().ToUpperInvariant();
        DateTimeOffset now = clock.UtcNow;
        string msg;

        if (actionUpper == "CONFIRM")
        {
            review.Confirm(reviewerId, request.InvestigatorNotes, request.ReasonForChange, now);

            if (review.QueryId.HasValue)
            {
                var query = await queryRepository.GetForUpdateAsync(review.QueryId.Value, cancellationToken).ConfigureAwait(false);
                if (query != null)
                {
                    query.Close(reviewerId, now);
                }
            }

            // Transactional Outbox Event Publishing
            await publishEndpoint.Publish(new Grade3ToxicityDetected(
                ReviewId: review.Id,
                StudyId: review.StudyId.Value,
                SubjectId: review.SubjectId.Value,
                SiteId: review.SiteId?.Value,
                TestCode: review.TestCode.Value,
                TestName: review.TestName,
                LabValue: review.LabValue,
                Unit: review.Unit,
                CtcaeGrade: review.CtcaeGrade,
                ToxicityTerm: review.ToxicityTerm,
                IsHysLawWarning: review.IsHysLawWarning,
                QueryId: review.QueryId?.ToString(),
                DetectedAt: now
            ), cancellationToken).ConfigureAwait(false);

            msg = "Staged AE review confirmed successfully.";
        }
        else if (actionUpper == "DISMISS")
        {
            review.Dismiss(reviewerId, request.InvestigatorNotes, request.ReasonForChange, now);

            if (review.QueryId.HasValue)
            {
                var query = await queryRepository.GetForUpdateAsync(review.QueryId.Value, cancellationToken).ConfigureAwait(false);
                if (query != null)
                {
                    query.Cancel(request.ReasonForChange, reviewerId);
                }
            }

            msg = "Staged AE review dismissed successfully.";
        }
        else if (actionUpper == "ESCALATE_SAE")
        {
            review.Escalate(reviewerId, request.InvestigatorNotes, request.ReasonForChange, now);

            if (review.QueryId.HasValue)
            {
                var query = await queryRepository.GetForUpdateAsync(review.QueryId.Value, cancellationToken).ConfigureAwait(false);
                if (query != null)
                {
                    query.Close(reviewerId, now);
                }
            }

            await publishEndpoint.Publish(new Grade3ToxicityDetected(
                ReviewId: review.Id,
                StudyId: review.StudyId.Value,
                SubjectId: review.SubjectId.Value,
                SiteId: review.SiteId?.Value,
                TestCode: review.TestCode.Value,
                TestName: review.TestName,
                LabValue: review.LabValue,
                Unit: review.Unit,
                CtcaeGrade: review.CtcaeGrade,
                ToxicityTerm: review.ToxicityTerm,
                IsHysLawWarning: review.IsHysLawWarning,
                QueryId: review.QueryId?.ToString(),
                DetectedAt: now
            ), cancellationToken).ConfigureAwait(false);

            msg = "Staged AE escalated to SAE successfully.";
        }
        else
        {
            return DomainError.Validation("INVALID_ACTION", $"Unsupported review action '{request.Action}'. Must be CONFIRM, DISMISS, or ESCALATE_SAE.");
        }

        await stagedReviewRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new StagedAdverseEventActionResponse(
            Id: review.Id,
            ReviewStatus: review.ReviewStatus,
            ActionTaken: actionUpper,
            QueryId: review.QueryId?.ToString(),
            Message: msg,
            Timestamp: now
        );
    }

    public Task<Result<LabToxicityEvaluateResponse>> EvaluateAsync(
        LabToxicityEvaluateRequest request,
        CancellationToken cancellationToken = default)
    {
        var engine = new LabToxicityGradingEngine();
        DateTimeOffset evalTime = request.EvaluatedAt ?? clock.UtcNow;

        var result = engine.EvaluateObservation(
            testCode: request.TestCode,
            value: request.Value,
            unit: request.Unit,
            evaluatedAt: evalTime,
            refLow: request.RefLow,
            refHigh: request.RefHigh,
            baselineValue: request.BaselineValue
        );

        bool isHysLaw = result.IsHysLawWarning;
        string cUpper = request.TestCode.Trim().ToUpperInvariant();
        if (cUpper is "ALT" or "AST" or "BILIRUBIN" or "TBIL" or "TOTAL_BILIRUBIN" or "BILI" or "ALKP" or "ALP" or "SGPT" or "SGOT" or "ALAT" or "ASAT"
            && request.RefHigh is > 0)
        {
            double uln = request.RefHigh.Value;
            if (cUpper is "ALT" or "SGPT" or "ALAT")
            {
                var hys = LabToxicityGradingEngine.EvaluateHysLaw(
                    alt: request.Value, ast: 0, tbil: 0, alkp: 0,
                    altUln: uln, astUln: 50, tbilUln: 1.2, alkpUln: 120,
                    evaluatedAt: evalTime);
                if (hys.IsHysLawMet) isHysLaw = true;
            }
        }

        var response = new LabToxicityEvaluateResponse(
            TestCode: result.TestCode,
            TestName: result.TestName,
            ObservedValue: result.ObservedValue,
            Unit: result.Unit,
            CtcaeGrade: (int)result.Grade,
            GradingScale: result.Scale,
            ToxicityDescription: result.Description,
            IsCriticalAeTrigger: result.IsCriticalAeTrigger,
            IsHysLawWarning: isHysLaw,
            IsDeltaCheckAlert: result.IsDeltaCheckAlert,
            DeltaPercentage: result.DeltaPercentage
        );

        return Task.FromResult(Result<LabToxicityEvaluateResponse>.Success(response));
    }

    private static StagedAdverseEventReviewResponse MapToResponse(StagedAdverseEventReview r) =>
        new(
            Id: r.Id,
            StudyId: r.StudyId.Value,
            SiteId: r.SiteId?.Value,
            SubjectId: r.SubjectId.Value,
            VisitId: r.VisitId,
            ObservationId: r.ObservationId?.ToString(),
            QueryId: r.QueryId?.ToString(),
            TestCode: r.TestCode.Value,
            TestName: r.TestName,
            LabValue: r.LabValue,
            Unit: r.Unit,
            CtcaeGrade: r.CtcaeGrade,
            GradingScale: r.GradingScale,
            ToxicityTerm: r.ToxicityTerm,
            ClinicalDescription: r.ClinicalDescription,
            IsHysLawWarning: r.IsHysLawWarning,
            IsDeltaCheckAlert: r.IsDeltaCheckAlert,
            DeltaPercentage: r.DeltaPercentage,
            ReviewStatus: r.ReviewStatus,
            ReviewedBy: r.ReviewedBy,
            ReviewedAt: r.ReviewedAt,
            InvestigatorNotes: r.InvestigatorNotes,
            Version: r.Version,
            IsDeleted: r.IsDeleted,
            CreatedAt: r.CreatedAt
        );
}
