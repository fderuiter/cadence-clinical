using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Queries;

/// <summary>
/// Unit test coverage for automated query dispatch, dispatch policy tagging,
/// auto-closure upon form data correction, CDM manual queue routing, and Part 11 system audit trail entries.
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// @req:SRS-EDC-004
/// </summary>
public sealed class AutoQueryDispatchTests
{
    private readonly AstEditCheckEngine _editCheckEngine = new();
    private readonly InMemoryClinicalQueryRepository _queryRepo = new();
    private readonly FakeClock _clock = new();

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public void Requirement1_RuleEngine_TagsViolationsWithDispatchPolicyData()
    {
        // 1. Predictable out-of-range low violation -> IsAutoDispatchEnabled = true
        var lowObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("LB"),
            testCode: new TestCode("K"),
            testName: "Potassium",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 2.8,
            additionalProperties: new ObservationAdditionalProperties
            {
                ReferenceRangeLow = 3.5,
                ReferenceRangeHigh = 5.0
            }).Value!;

        var lowViolations = _editCheckEngine.EvaluateInlineConstraints(lowObs);
        lowViolations.Should().ContainSingle();
        lowViolations[0].RuleId.Should().Be("RULE_RANGE_LOW");
        lowViolations[0].IsAutoDispatchEnabled.Should().BeTrue();
        lowViolations[0].StandardTemplate.Should().NotBeNullOrWhiteSpace();

        // 2. Unit mismatch violation -> IsAutoDispatchEnabled = true
        var unitObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 120,
            unit: "INVALID_UNIT").Value!;

        var unitViolations = _editCheckEngine.EvaluateInlineConstraints(unitObs);
        unitViolations.Should().ContainSingle(v => v.RuleId == "RULE_UNIT_MISMATCH");
        unitViolations.First(v => v.RuleId == "RULE_UNIT_MISMATCH").IsAutoDispatchEnabled.Should().BeTrue();

        // 3. Complex clinical discrepancy (Systolic < Diastolic) -> IsAutoDispatchEnabled = false
        var sysObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 70.0,
            visitId: "VISIT-01").Value!;

        var diaObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("DIABP"),
            testName: "Diastolic Blood Pressure",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 90.0,
            visitId: "VISIT-01").Value!;

        var batchContext = new BatchEvaluationContext("STUDY-001", "SUBJ-001", [sysObs, diaObs], []);
        var batchViolations = _editCheckEngine.EvaluateBatchRules(batchContext);
        batchViolations.Should().ContainSingle(v => v.RuleId == "RULE_BP_PHYSIOLOGY");
        batchViolations.First(v => v.RuleId == "RULE_BP_PHYSIOLOGY").IsAutoDispatchEnabled.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task Requirement2_AutoDispatch_CreatesOpenQueryWithStandardTextAndSystemMetadata()
    {
        var dispatchService = new AutoQueryDispatchService(_queryRepo, _clock);

        var lowObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("LB"),
            testCode: new TestCode("K"),
            testName: "Potassium",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 2.8,
            additionalProperties: new ObservationAdditionalProperties
            {
                ReferenceRangeLow = 3.5,
                ReferenceRangeHigh = 5.0
            }).Value!;

        var violations = _editCheckEngine.EvaluateInlineConstraints(lowObs);

        var processed = await dispatchService.ProcessViolationsAndAutoResolutionAsync(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            formId: "FORM-LABS",
            observations: [lowObs],
            violations: violations,
            cancellationToken: CancellationToken.None);

        processed.Should().ContainSingle();
        var query = processed[0];

        query.Status.Should().Be(QueryStatus.Open);
        query.Origin.Should().Be("AUTO_DISPATCH");
        query.CreatedBy.Should().Be("svc:auto-query-engine");
        query.Explanation.Should().Contain("below reference range low");
        query.RuleId.Should().Be("RULE_RANGE_LOW");
        query.FieldId.Should().Be("K");

        // Verify stored query in repository
        var storedList = await _queryRepo.ListBySubjectAsync(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-001"), CancellationToken.None);
        storedList.Should().ContainSingle(q => q.Id == query.Id && q.Status == QueryStatus.Open);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task Requirement3_FormReEvaluation_AutoClosesQueryWhenDataCorrected()
    {
        var dispatchService = new AutoQueryDispatchService(_queryRepo, _clock);

        // 1. Initial Submission with out-of-range low value (K = 2.8) -> Triggers Auto-Query
        var lowObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("LB"),
            testCode: new TestCode("K"),
            testName: "Potassium",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 2.8,
            additionalProperties: new ObservationAdditionalProperties
            {
                ReferenceRangeLow = 3.5,
                ReferenceRangeHigh = 5.0
            }).Value!;

        var initialViolations = _editCheckEngine.EvaluateInlineConstraints(lowObs);

        await dispatchService.ProcessViolationsAndAutoResolutionAsync(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            formId: "FORM-LABS",
            observations: [lowObs],
            violations: initialViolations,
            cancellationToken: CancellationToken.None);

        var activeList = await _queryRepo.ListBySubjectAsync(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-001"), CancellationToken.None);
        activeList.Should().ContainSingle(q => q.Status == QueryStatus.Open);
        var openQuery = activeList.First(q => q.Status == QueryStatus.Open);

        // 2. Form Correction Submission: Site updates Potassium value to normal (K = 4.2)
        var correctedObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("LB"),
            testCode: new TestCode("K"),
            testName: "Potassium",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 4.2,
            additionalProperties: new ObservationAdditionalProperties
            {
                ReferenceRangeLow = 3.5,
                ReferenceRangeHigh = 5.0
            }).Value!;

        var correctedViolations = _editCheckEngine.EvaluateInlineConstraints(correctedObs);
        correctedViolations.Should().BeEmpty(); // Violation is cleared!

        // Process re-evaluation
        _ = await dispatchService.ProcessViolationsAndAutoResolutionAsync(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            formId: "FORM-LABS",
            observations: [correctedObs],
            violations: correctedViolations,
            cancellationToken: CancellationToken.None);

        // 3. Verify open query was automatically transitioned to Closed
        openQuery.Status.Should().Be(QueryStatus.Closed);
        openQuery.Resolver.Should().Be("svc:auto-query-engine");
        openQuery.ResolvedAt.Should().Be(_clock.UtcNow);
        openQuery.ReasonForChange.Should().Contain("auto-resolution upon site data correction");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task Requirement4_ComplexDiscrepancy_RoutesToCdmCandidateQueue()
    {
        var dispatchService = new AutoQueryDispatchService(_queryRepo, _clock);

        // Batch physiology check: Systolic 70 < Diastolic 90
        var sysObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 70.0,
            visitId: "VISIT-01").Value!;

        var diaObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("DIABP"),
            testName: "Diastolic Blood Pressure",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 90.0,
            visitId: "VISIT-01").Value!;

        var batchContext = new BatchEvaluationContext("STUDY-001", "SUBJ-001", [sysObs, diaObs], []);
        var violations = _editCheckEngine.EvaluateBatchRules(batchContext);

        var processed = await dispatchService.ProcessViolationsAndAutoResolutionAsync(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            formId: "FORM-VS",
            observations: [sysObs, diaObs],
            violations: violations,
            cancellationToken: CancellationToken.None);

        processed.Should().ContainSingle();
        var query = processed[0];

        // Should NOT be auto-dispatched to Open, but stay as Candidate in CDM manual queue
        query.Status.Should().Be(QueryStatus.Candidate);
        query.Origin.Should().Be("RULE_ENGINE");
        query.CreatedBy.Should().Be("svc:auto-query-engine");
        query.RuleId.Should().Be("RULE_BP_PHYSIOLOGY");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task Requirement5_AuditTrail_LogsSystemActorForCreationAndResolution()
    {
        var dispatchService = new AutoQueryDispatchService(_queryRepo, _clock);

        // 1. Initial violation creation
        var lowObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("LB"),
            testCode: new TestCode("K"),
            testName: "Potassium",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 2.8,
            additionalProperties: new ObservationAdditionalProperties { ReferenceRangeLow = 3.5, ReferenceRangeHigh = 5.0 }).Value!;

        var initialViolations = _editCheckEngine.EvaluateInlineConstraints(lowObs);

        var dispatchResult = await dispatchService.ProcessViolationsAndAutoResolutionAsync(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            formId: "FORM-LABS",
            observations: [lowObs],
            violations: initialViolations,
            cancellationToken: CancellationToken.None);

        var query = dispatchResult[0];

        // Audit metadata check for creation
        query.CreatedBy.Should().StartWith("svc:");
        query.CreatedBy.Should().Be(ServiceAccounts.AutoQueryEngine.UserId);
        query.ReasonForChange.Should().Be(ServiceAccounts.AutoQueryEngine.ChangeReason);

        // 2. Data Correction & Auto-Resolution
        var correctedObs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("LB"),
            testCode: new TestCode("K"),
            testName: "Potassium",
            observationDate: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 4.0,
            additionalProperties: new ObservationAdditionalProperties { ReferenceRangeLow = 3.5, ReferenceRangeHigh = 5.0 }).Value!;

        var correctedViolations = _editCheckEngine.EvaluateInlineConstraints(correctedObs);

        await dispatchService.ProcessViolationsAndAutoResolutionAsync(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            formId: "FORM-LABS",
            observations: [correctedObs],
            violations: correctedViolations,
            cancellationToken: CancellationToken.None);

        // Audit metadata check for resolution
        query.Status.Should().Be(QueryStatus.Closed);
        query.Resolver.Should().Be(ServiceAccounts.AutoQueryEngine.UserId);
        query.ReasonForChange.Should().Contain("auto-resolution upon site data correction");
    }
}
