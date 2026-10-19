using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Observations.Errors;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Toxicity;
using Cadence.Kernel;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Observations;

/// <summary>
/// Application service implementing discrete observation ingestion, UCUM normalization,
/// demographic reference range evaluation, CTCAE v5.0 toxicity grading, and SDV audit verification.
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
    IDemographicsNormalizationService? demographicsNormalizationService = null,
    IAutoQueryDispatchService? autoQueryDispatchService = null,
    IStagedAdverseEventReviewRepository? stagedReviewRepository = null,
    IFormSubmissionRepository? formSubmissionRepository = null,
    IClinicalQueryRepository? queryRepository = null) : IObservationApplicationService
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

        ClinicalSubject? subject = await subjectRepository.GetByIdAsync(request.SubjectId, cancellationToken).ConfigureAwait(false)
            ?? await subjectRepository.GetByCodeAsync(request.SubjectId, cancellationToken).ConfigureAwait(false);

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

        // Resolve subject demographics for lab range evaluation
        string? sex = null;
        int? ageYears = null;

        if (subject != null && demographicsNormalizationService != null)
        {
            DateOnly? obsDateOnly = request.ObservationDate.HasValue
                ? DateOnly.FromDateTime(request.ObservationDate.Value)
                : null;
            var safeDemo = demographicsNormalizationService.GetSafeDemographics(subject, observationDate: obsDateOnly);
            sex = safeDemo.Gender;
            if (safeDemo.Age is double decimalAge)
            {
                ageYears = (int)Math.Floor(decimalAge);
            }
        }

        // Lab range evaluation
        // Ranges are scoped to the observation's study and site, and loaded from the database on first use (#5435).
        await labRangeService.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var labResult = labRangeService.Evaluate(
            studyCode,
            testCode,
            normVal ?? request.Value,
            sex: sex,
            ageYears: ageYears,
            labSource: request.LabSource,
            siteId: siteCode?.Value);

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

        // CTCAE v5.0 Toxicity Grading & Staging
        await ProcessToxicityGradingAndStagingAsync(
            studyCode,
            subjectCode,
            siteCode,
            domainCode,
            testCode,
            request,
            observation,
            labResult,
            normVal,
            cancellationToken).ConfigureAwait(false);

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

    private async Task ProcessToxicityGradingAndStagingAsync(
        StudyCode studyCode,
        SubjectCode subjectCode,
        SiteCode? siteCode,
        DomainCode domainCode,
        TestCode testCode,
        ObservationCreateRequest request,
        ClinicalObservation observation,
        LabAssessmentResult labResult,
        double? normVal,
        CancellationToken cancellationToken)
    {
        // 1. Calculate baseline value from historical observations
        var history = await observationRepository.ListByCoordinatesAsync(
            studyCode, subjectCode, visitId: null, domain: null, testCode: testCode, cancellationToken: cancellationToken).ConfigureAwait(false);
        double? baselineValue = history.Count > 0
            ? history.OrderBy(h => h.ObservationDate).First().Value
            : null;

        double evalVal = normVal ?? request.Value ?? 0.0;
        var toxicityEngine = new LabToxicityGradingEngine();
        var toxEval = toxicityEngine.EvaluateObservation(
            testCode: testCode.Value,
            value: evalVal,
            unit: request.Unit ?? string.Empty,
            evaluatedAt: clock.UtcNow,
            refLow: labResult.RangeLow,
            refHigh: labResult.RangeHigh,
            baselineValue: baselineValue);

        // 2. Evaluate Hy's Law DILI criteria for hepatic panel
        bool isHysLawWarning = false;
        string codeUpper = testCode.Value.Trim().ToUpperInvariant();
        if (codeUpper is "ALT" or "AST" or "BILIRUBIN" or "TBIL" or "TOTAL_BILIRUBIN" or "BILI" or "ALKP" or "ALP" or "SGPT" or "SGOT" or "ALAT" or "ASAT")
        {
            isHysLawWarning = await CheckHysLawAsync(studyCode, subjectCode, testCode.Value, evalVal, labResult.RangeHigh, cancellationToken).ConfigureAwait(false);
        }

        bool isSevereTrigger = toxEval.IsCriticalAeTrigger || isHysLawWarning;

        if (isSevereTrigger)
        {
            // Draft AE FormSubmission
            FormSubmission? aeForm = null;
            if (formSubmissionRepository != null)
            {
                var payloadDict = new Dictionary<string, object?>
                {
                    ["test_code"] = toxEval.TestCode,
                    ["test_name"] = toxEval.TestName,
                    ["lab_value"] = toxEval.ObservedValue,
                    ["unit"] = toxEval.Unit,
                    ["ctcae_grade"] = (int)toxEval.Grade,
                    ["meddra_term"] = toxEval.AeDraft?.MeddraTerm ?? $"{toxEval.TestName} abnormal",
                    ["severity"] = toxEval.AeDraft?.Severity ?? "SEVERE",
                    ["is_serious"] = true,
                    ["hys_law_indicated"] = isHysLawWarning,
                    ["status"] = "DRAFT_AI"
                };

                string payloadJson = JsonSerializer.Serialize(payloadDict);
                var aeFormResult = FormSubmission.Initialize(
                    studyId: studyCode,
                    subjectId: subjectCode,
                    formId: new FormId("AE_CRF"),
                    siteId: siteCode,
                    visitId: request.VisitId,
                    protocolVersion: request.ProtocolVersionTag ?? "1.0.0",
                    initialPayload: payloadJson);

                if (aeFormResult.IsSuccess)
                {
                    aeForm = aeFormResult.Value!;
                    await formSubmissionRepository.AddAsync(aeForm, cancellationToken).ConfigureAwait(false);
                }
            }

            // Open ClinicalQuery for Investigator
            ClinicalQuery? query = null;
            if (queryRepository != null)
            {
                string queryMsg = $"Investigator review required for Grade {(int)toxEval.Grade} laboratory toxicity: {toxEval.TestName} ({toxEval.ObservedValue} {toxEval.Unit}).";
                if (isHysLawWarning)
                {
                    queryMsg += " [Hy's Law DILI Alert]";
                }

                var queryResult = ClinicalQuery.Raise(
                    studyId: studyCode,
                    subjectId: subjectCode,
                    testCode: testCode,
                    explanation: queryMsg,
                    initialStatus: QueryStatus.Open,
                    siteId: siteCode,
                    visitId: request.VisitId,
                    domain: domainCode,
                    observationId: observation.Id,
                    origin: "AUTO_TOXICITY_GRADING",
                    priority: "HIGH",
                    ruleId: "CTCAE_V5_GRADE3_4",
                    formId: "AE_CRF",
                    fieldId: testCode.Value,
                    queryType: "TOXICITY_REVIEW",
                    actionRequired: "INVESTIGATOR_CONFIRMATION");

                if (queryResult.IsSuccess)
                {
                    query = queryResult.Value!;
                    await queryRepository.AddAsync(query, cancellationToken).ConfigureAwait(false);
                }
            }

            // StagedAdverseEventReview record
            if (stagedReviewRepository != null)
            {
                var stagedReview = StagedAdverseEventReview.Create(
                    studyId: studyCode,
                    subjectId: subjectCode,
                    testCode: testCode,
                    testName: toxEval.TestName,
                    labValue: toxEval.ObservedValue,
                    unit: toxEval.Unit,
                    ctcaeGrade: (int)toxEval.Grade,
                    toxicityTerm: toxEval.AeDraft?.MeddraTerm ?? $"{toxEval.TestName} abnormal",
                    clinicalDescription: toxEval.Description,
                    isHysLawWarning: isHysLawWarning,
                    isDeltaCheckAlert: toxEval.IsDeltaCheckAlert,
                    deltaPercentage: toxEval.DeltaPercentage,
                    siteId: siteCode,
                    visitId: request.VisitId,
                    observationId: observation.Id,
                    formSubmissionId: aeForm?.Id,
                    queryId: query?.Id,
                    gradingScale: toxEval.Scale);

                await stagedReviewRepository.AddAsync(stagedReview, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> CheckHysLawAsync(
        StudyCode studyCode,
        SubjectCode subjectCode,
        string currentTestCode,
        double currentValue,
        double? currentRefHigh,
        CancellationToken cancellationToken)
    {
        var subjectObs = await observationRepository.ListBySubjectAsync(studyCode, subjectCode, cancellationToken).ConfigureAwait(false);

        double alt = 0.0, ast = 0.0, tbil = 0.0, alkp = 0.0;
        double altUln = 50.0, astUln = 50.0, tbilUln = 1.2, alkpUln = 120.0;

        string cUpper = currentTestCode.Trim().ToUpperInvariant();
        if (cUpper is "ALT" or "SGPT" or "ALAT") { alt = currentValue; if (currentRefHigh > 0) altUln = currentRefHigh.Value; }
        if (cUpper is "AST" or "SGOT" or "ASAT") { ast = currentValue; if (currentRefHigh > 0) astUln = currentRefHigh.Value; }
        if (cUpper is "BILIRUBIN" or "TBIL" or "TOTAL_BILIRUBIN" or "BILI") { tbil = currentValue; if (currentRefHigh > 0) tbilUln = currentRefHigh.Value; }
        if (cUpper is "ALKP" or "ALP" or "ALKALINE_PHOSPHATASE") { alkp = currentValue; if (currentRefHigh > 0) alkpUln = currentRefHigh.Value; }

        foreach (var obs in subjectObs)
        {
            string code = obs.TestCode.Value.Trim().ToUpperInvariant();
            if (code is "ALT" or "SGPT" or "ALAT")
            {
                if (alt < 0.0001 && obs.Value.HasValue) alt = obs.Value.Value;
                if (obs.ReferenceRangeHigh.HasValue && obs.ReferenceRangeHigh.Value > 0) altUln = obs.ReferenceRangeHigh.Value;
            }
            else if (code is "AST" or "SGOT" or "ASAT")
            {
                if (ast < 0.0001 && obs.Value.HasValue) ast = obs.Value.Value;
                if (obs.ReferenceRangeHigh.HasValue && obs.ReferenceRangeHigh.Value > 0) astUln = obs.ReferenceRangeHigh.Value;
            }
            else if (code is "BILIRUBIN" or "TBIL" or "TOTAL_BILIRUBIN" or "BILI")
            {
                if (tbil < 0.0001 && obs.Value.HasValue) tbil = obs.Value.Value;
                if (obs.ReferenceRangeHigh.HasValue && obs.ReferenceRangeHigh.Value > 0) tbilUln = obs.ReferenceRangeHigh.Value;
            }
            else if (code is "ALKP" or "ALP" or "ALKALINE_PHOSPHATASE")
            {
                if (alkp < 0.0001 && obs.Value.HasValue) alkp = obs.Value.Value;
                if (obs.ReferenceRangeHigh.HasValue && obs.ReferenceRangeHigh.Value > 0) alkpUln = obs.ReferenceRangeHigh.Value;
            }
        }

        if (alt > 0 && tbil > 0)
        {
            var hysEval = LabToxicityGradingEngine.EvaluateHysLaw(
                alt: alt, ast: ast, tbil: tbil, alkp: alkp,
                altUln: altUln, astUln: astUln, tbilUln: tbilUln, alkpUln: alkpUln,
                evaluatedAt: clock.UtcNow);

            return hysEval.IsHysLawMet;
        }

        return false;
    }

    public Task<Result<ObservationResponse>> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        GetByIdAsync(id, includeInactive: false, cancellationToken);

    public async Task<Result<ObservationResponse>> GetByIdAsync(
        string id,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        if (!ObservationId.TryParse(id, out var observationId) || observationId.IsEmpty)
        {
            return ObservationErrors.NotFound(id);
        }

        var observation = await observationRepository.GetByIdAsync(observationId, includeInactive, cancellationToken).ConfigureAwait(false);
        if (observation is null)
        {
            return ObservationErrors.NotFound(id);
        }

        return MapToResponse(observation);
    }

    public Task<Result<IReadOnlyList<ObservationResponse>>> ListBySubjectAsync(
        string studyId,
        string subjectId,
        CancellationToken cancellationToken = default) =>
        ListBySubjectAsync(studyId, subjectId, includeInactive: false, cancellationToken);

    public async Task<Result<IReadOnlyList<ObservationResponse>>> ListBySubjectAsync(
        string studyId,
        string subjectId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var studyCode = new StudyCode(studyId);
        var subjectCode = new SubjectCode(subjectId);

        var list = await observationRepository.ListBySubjectAsync(studyCode, subjectCode, includeInactive, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<ObservationResponse>>.Success(list.Select(MapToResponse).ToList());
    }

    public async Task<Result<ObservationResponse>> VerifySdvAsync(string id, AuthenticatedCaller? verifier, CancellationToken cancellationToken)
    {
        if (verifier?.FirstRoleMatching(CanVerifySourceData) is null)
        {
            return ObservationErrors.UnauthorizedSdvRole(verifier is null ? string.Empty : string.Join(",", verifier.Roles));
        }

        if (!ObservationId.TryParse(id, out var observationId) || observationId.IsEmpty)
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

        if (!ObservationId.TryParse(id, out var observationId) || observationId.IsEmpty)
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
            ProtocolVersionIndex: o.ProtocolVersionIndex,
            FormSubmissionId: o.FormSubmissionId?.ToString(),
            IsActive: o.IsActive
        );
}
