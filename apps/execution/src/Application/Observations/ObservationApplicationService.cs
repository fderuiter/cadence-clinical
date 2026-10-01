using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Observations.Errors;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Observations;

/// <summary>
/// Application service implementing discrete observation ingestion, UCUM normalization,
/// demographic reference range evaluation, and SDV audit verification.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-042
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public sealed class ObservationApplicationService(
    IObservationRepository observationRepository,
    ISubjectRepository subjectRepository,
    IUcumNormalizationService ucumService,
    ILabRangeService labRangeService,
    IEditCheckEngine editCheckEngine,
    IClock clock,
    ICurrentAuditContextAccessor auditContextAccessor,
    IAutoQueryDispatchService? autoQueryDispatchService = null) : IObservationApplicationService
{
    public async Task<Result<ObservationResponse>> CreateAsync(
        ObservationCreateRequest request,
        CancellationToken cancellationToken)
    {
        SubjectCode subjectCode;
        try
        {
            subjectCode = new SubjectCode(request.SubjectId);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_SUBJECT_CODE", ex.Message);
        }

        ClinicalSubject? subject = await subjectRepository.GetByIdAsync(request.SubjectId, cancellationToken).ConfigureAwait(false);

        StudyCode studyCode;
        if (!string.IsNullOrWhiteSpace(request.StudyId))
        {
            try
            {
                studyCode = new StudyCode(request.StudyId);
            }
            catch (ArgumentException ex)
            {
                return DomainError.Validation("INVALID_STUDY_CODE", ex.Message);
            }
        }
        else if (subject != null)
        {
            studyCode = subject.StudyCode;
        }
        else
        {
            return DomainError.Validation("MISSING_STUDY_ID", "Subject not registered; cannot infer study_id.");
        }

        DomainCode domainCode;
        try
        {
            domainCode = new DomainCode(request.Domain);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_DOMAIN_CODE", ex.Message);
        }

        TestCode testCode;
        try
        {
            testCode = new TestCode(request.TestCode);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_TEST_CODE", ex.Message);
        }

        SiteCode? siteCode = null;
        if (!string.IsNullOrWhiteSpace(request.SiteId))
        {
            try
            {
                siteCode = new SiteCode(request.SiteId);
            }
            catch (ArgumentException ex)
            {
                return DomainError.Validation("INVALID_SITE_CODE", ex.Message);
            }
        }
        else if (subject?.SiteCode != null)
        {
            siteCode = subject.SiteCode;
        }

        // UCUM normalization
        var (normVal, normUnit) = ucumService.GetNormalizedRepresentation(request.Value, request.Unit);

        // Lab range evaluation
        var labResult = labRangeService.Evaluate(
            testCode,
            normVal ?? request.Value,
            sex: null,
            ageYears: null,
            labSource: request.LabSource);

        var additionalProps = new ObservationAdditionalProperties
        {
            LabSource = request.LabSource ?? "CENTRAL",
            LabSiteId = request.LabSiteId,
            LabIndicator = labResult.Indicator,
            LabOutOfRange = labResult.OutOfRange,
            MatchedNormalBounds = labResult.MatchedBoundsJson,
            RangeIndicator = labResult.Indicator,
            IsOutOfRange = labResult.OutOfRange,
            ReferenceRangeLow = labResult.RangeLow,
            ReferenceRangeHigh = labResult.RangeHigh
        };

        if (request.ObservationDate is not { } observationDate)
        {
            return ObservationErrors.ObservationDateRequired();
        }

        var creationResult = ClinicalObservation.Create(
            studyId: studyCode,
            subjectId: subjectCode,
            domain: domainCode,
            testCode: testCode,
            testName: request.TestName,
            value: request.Value,
            valueString: request.ValueString,
            unit: request.Unit,
            observationDate: observationDate,
            siteId: siteCode,
            visitId: request.VisitId,
            pageId: request.PageId,
            protocolVersionTag: request.ProtocolVersionTag ?? subject?.ActiveProtocolVersion ?? "1.0.0",
            protocolVersionIndex: request.ProtocolVersionIndex ?? 1,
            normalizedValue: normVal,
            normalizedUnit: normUnit,
            additionalProperties: additionalProps);

        if (creationResult.IsFailure)
        {
            return creationResult.Error!;
        }

        var observation = creationResult.Value!;

        // Synchronous inline constraints
        var inlineViolations = editCheckEngine.EvaluateInlineConstraints(observation);
        var fatalViolation = inlineViolations.FirstOrDefault(v => v.Severity == "FATAL");
        if (fatalViolation != null)
        {
            return DomainError.Validation(fatalViolation.RuleId, fatalViolation.Message);
        }

        observation.CreatedBy = auditContextAccessor.UserId;
        observation.ReasonForChange = auditContextAccessor.ChangeReason;

        await observationRepository.AddAsync(observation, cancellationToken).ConfigureAwait(false);
        await observationRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (autoQueryDispatchService != null)
        {
            await autoQueryDispatchService.ProcessViolationsAndAutoResolutionAsync(
                studyId: studyCode,
                subjectId: subjectCode,
                siteId: siteCode,
                visitId: request.VisitId,
                formId: request.PageId,
                observations: [observation],
                violations: inlineViolations,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return MapToResponse(observation);
    }

    public async Task<Result<ObservationResponse>> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (!ObservationId.TryParse(id, out var observationId))
        {
            return ObservationErrors.NotFound(id);
        }

        var observation = await observationRepository.GetByIdAsync(observationId, cancellationToken).ConfigureAwait(false);
        if (observation is null)
        {
            return ObservationErrors.NotFound(id);
        }

        return MapToResponse(observation);
    }

    public async Task<Result<IReadOnlyList<ObservationResponse>>> ListBySubjectAsync(
        string studyId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        var studyCode = new StudyCode(studyId);
        var subjectCode = new SubjectCode(subjectId);

        var list = await observationRepository.ListBySubjectAsync(studyCode, subjectCode, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<ObservationResponse>>.Success(list.Select(MapToResponse).ToList());
    }

    public async Task<Result<ObservationResponse>> VerifySdvAsync(string id, AuthenticatedCaller? verifier, CancellationToken cancellationToken)
    {
        if (verifier?.FirstRoleMatching(CanVerifySourceData) is null)
        {
            return ObservationErrors.UnauthorizedSdvRole(verifier is null ? string.Empty : string.Join(",", verifier.Roles));
        }

        if (!ObservationId.TryParse(id, out var observationId))
        {
            return ObservationErrors.NotFound(id);
        }

        var observation = await observationRepository.GetForUpdateAsync(observationId, cancellationToken).ConfigureAwait(false);
        if (observation is null)
        {
            return ObservationErrors.NotFound(id);
        }

        var verifyResult = observation.VerifySdv(verifier.UserId, clock.UtcNow);
        if (verifyResult.IsFailure)
        {
            return verifyResult.Error!;
        }

        observation.ReasonForChange = auditContextAccessor.ChangeReason;
        await observationRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapToResponse(observation);
    }

    public async Task<Result<ObservationResponse>> FlagSdvAsync(string id, string reason, AuthenticatedCaller? verifier, CancellationToken cancellationToken)
    {
        if (verifier?.FirstRoleMatching(CanVerifySourceData) is null)
        {
            return ObservationErrors.UnauthorizedSdvRole(verifier is null ? string.Empty : string.Join(",", verifier.Roles));
        }

        if (!ObservationId.TryParse(id, out var observationId))
        {
            return ObservationErrors.NotFound(id);
        }

        var observation = await observationRepository.GetForUpdateAsync(observationId, cancellationToken).ConfigureAwait(false);
        if (observation is null)
        {
            return ObservationErrors.NotFound(id);
        }

        var flagResult = observation.FlagSdv(reason);
        if (flagResult.IsFailure)
        {
            return flagResult.Error!;
        }

        observation.ReasonForChange = auditContextAccessor.ChangeReason;
        await observationRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapToResponse(observation);
    }

    /// <summary>
    /// Whether <paramref name="role"/> may verify source data or flag an SDV discrepancy: a CRA or monitor, or a
    /// data manager. Site roles never verify their own data (#4920). This is a role check until
    /// grants and qualifications from the Trust engine replace it (#4781).
    /// </summary>
    public static bool CanVerifySourceData(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var normalized = role.Trim().ToLowerInvariant();
        return normalized is "cra" or "clinical research associate" or "monitor" or "external_monitor"
            or "sponsor_dm" or "data_manager" or "data manager" or "data-manager" or "datamanager" or "dm";
    }

    private static ObservationResponse MapToResponse(ClinicalObservation o) =>
        new(
            Id: o.Id.ToString(),
            SubjectId: o.SubjectId.Value,
            StudyId: o.StudyId.Value,
            Domain: o.Domain.Value,
            TestCode: o.TestCode.Value,
            TestName: o.TestName,
            ObservationDate: o.ObservationDate,
            SiteId: o.SiteId?.Value,
            VisitId: o.VisitId,
            Value: o.Value,
            ValueString: o.ValueString,
            Unit: o.Unit,
            NormalizedValue: o.NormalizedValue,
            NormalizedUnit: o.NormalizedUnit,
            IsOutlier: o.IsOutlier,
            IsSdvVerified: o.IsSdvVerified,
            SdvVerifiedBy: o.SdvVerifiedBy,
            SdvVerifiedAt: o.SdvVerifiedAt?.UtcDateTime,
            LabSource: o.LabSource,
            LabSiteId: o.LabSiteId,
            LabIndicator: o.LabIndicator,
            LabOutOfRange: o.LabOutOfRange,
            MatchedNormalBounds: o.MatchedNormalBounds,
            RangeIndicator: o.RangeIndicator,
            IsOutOfRange: o.IsOutOfRange,
            ReferenceRangeLow: o.ReferenceRangeLow,
            ReferenceRangeHigh: o.ReferenceRangeHigh,
            ProtocolVersionTag: o.ProtocolVersionTag,
            ProtocolVersionIndex: o.ProtocolVersionIndex
        );
}
