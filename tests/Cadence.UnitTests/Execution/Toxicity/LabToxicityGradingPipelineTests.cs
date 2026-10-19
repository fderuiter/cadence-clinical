using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Application.Toxicity;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Contracts;
using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Time;

using FluentAssertions;

using MassTransit;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Toxicity;

public sealed class LabToxicityGradingPipelineTests
{
    private readonly IObservationRepository _observationRepository = Substitute.For<IObservationRepository>();
    private readonly ISubjectRepository _subjectRepository = Substitute.For<ISubjectRepository>();
    private readonly IUcumNormalizationService _ucumService = Substitute.For<IUcumNormalizationService>();
    private readonly ILabRangeService _labRangeService = Substitute.For<ILabRangeService>();
    private readonly IEditCheckEngine _editCheckEngine = Substitute.For<IEditCheckEngine>();
    private readonly IClock _clock = SystemClock.Instance;
    private readonly ICurrentAuditContextAccessor _auditContextAccessor = Substitute.For<ICurrentAuditContextAccessor>();
    private readonly IStagedAdverseEventReviewRepository _stagedReviewRepository = Substitute.For<IStagedAdverseEventReviewRepository>();
    private readonly IFormSubmissionRepository _formSubmissionRepository = Substitute.For<IFormSubmissionRepository>();
    private readonly IClinicalQueryRepository _queryRepository = Substitute.For<IClinicalQueryRepository>();
    private readonly IPublishEndpoint _publishEndpoint = Substitute.For<IPublishEndpoint>();

    public LabToxicityGradingPipelineTests()
    {
        _auditContextAccessor.UserId.Returns("investigator_user_01");

        _ucumService.GetNormalizedRepresentation(Arg.Any<double?>(), Arg.Any<string?>())
            .Returns(call => ((double?)call[0], (string?)call[1]));

        _labRangeService.Evaluate(
                Arg.Any<StudyCode>(), Arg.Any<TestCode>(), Arg.Any<double?>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new LabAssessmentResult("NORMAL", false, null, 10.0, 40.0));
    }

    [Fact(DisplayName = "CreateAsync_WithGrade3Toxicity_StagesAE_CreatesQuery_AndSavesStagedReview")]
    public async Task CreateAsync_WithGrade3Toxicity_StagesAE_CreatesQuery_AndSavesStagedReview()
    {
        // Arrange
        var subject = ClinicalSubject.Create(
            subjectCode: new SubjectCode("SUBJ-101"),
            studyCode: new StudyCode("STUDY-001"),
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 1,
            encryptedDemographics: null,
            stratFactors: null);

        _subjectRepository.GetByCodeAsync("SUBJ-101", Arg.Any<CancellationToken>())
            .Returns(subject);

        _observationRepository.ListByCoordinatesAsync(
            new StudyCode("STUDY-001"), new SubjectCode("SUBJ-101"), null, null, Arg.Any<TestCode>(), Arg.Any<CancellationToken>())
            .Returns(new List<ClinicalObservation>());

        var obsService = new ObservationApplicationService(
            _observationRepository,
            _subjectRepository,
            _ucumService,
            _labRangeService,
            _editCheckEngine,
            _clock,
            _auditContextAccessor,
            stagedReviewRepository: _stagedReviewRepository,
            formSubmissionRepository: _formSubmissionRepository,
            queryRepository: _queryRepository);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            Domain: "LB",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            ObservationDate: _clock.UtcNow.UtcDateTime,
            Value: 250.0, // > 5.0x ULN (40 U/L) -> CTCAE Grade 3
            Unit: "U/L",
            SiteId: "SITE-01",
            VisitId: "VISIT-01");

        // Act
        var result = await obsService.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _observationRepository.Received(1).AddAsync(Arg.Any<ClinicalObservation>(), Arg.Any<CancellationToken>());
        await _formSubmissionRepository.Received(1).AddAsync(Arg.Is<FormSubmission>(f => f.FormId.Value == "AE_CRF"), Arg.Any<CancellationToken>());
        await _queryRepository.Received(1).AddAsync(Arg.Is<ClinicalQuery>(q => q.RuleId == "CTCAE_V5_GRADE3_4"), Arg.Any<CancellationToken>());
        await _stagedReviewRepository.Received(1).AddAsync(Arg.Is<StagedAdverseEventReview>(r => r.CtcaeGrade == 3 && r.ReviewStatus == "STAGED_FOR_INVESTIGATOR_REVIEW"), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "CreateAsync_WithNormalLabValue_DoesNotCreateStagedReview")]
    public async Task CreateAsync_WithNormalLabValue_DoesNotCreateStagedReview()
    {
        // Arrange
        var subject = ClinicalSubject.Create(
            subjectCode: new SubjectCode("SUBJ-101"),
            studyCode: new StudyCode("STUDY-001"),
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 1,
            encryptedDemographics: null,
            stratFactors: null);

        _subjectRepository.GetByCodeAsync("SUBJ-101", Arg.Any<CancellationToken>())
            .Returns(subject);

        _observationRepository.ListByCoordinatesAsync(
            new StudyCode("STUDY-001"), new SubjectCode("SUBJ-101"), null, null, Arg.Any<TestCode>(), Arg.Any<CancellationToken>())
            .Returns(new List<ClinicalObservation>());

        var obsService = new ObservationApplicationService(
            _observationRepository,
            _subjectRepository,
            _ucumService,
            _labRangeService,
            _editCheckEngine,
            _clock,
            _auditContextAccessor,
            stagedReviewRepository: _stagedReviewRepository,
            formSubmissionRepository: _formSubmissionRepository,
            queryRepository: _queryRepository);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            Domain: "LB",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            ObservationDate: _clock.UtcNow.UtcDateTime,
            Value: 25.0, // Normal value (< 40 ULN)
            Unit: "U/L",
            SiteId: "SITE-01",
            VisitId: "VISIT-01");

        // Act
        var result = await obsService.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _observationRepository.Received(1).AddAsync(Arg.Any<ClinicalObservation>(), Arg.Any<CancellationToken>());
        await _formSubmissionRepository.DidNotReceive().AddAsync(Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>());
        await _queryRepository.DidNotReceive().AddAsync(Arg.Any<ClinicalQuery>(), Arg.Any<CancellationToken>());
        await _stagedReviewRepository.DidNotReceive().AddAsync(Arg.Any<StagedAdverseEventReview>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteActionAsync_Confirm_CompletesForm_ResolvesQuery_AndPublishesOutboxEvent")]
    public async Task ExecuteActionAsync_Confirm_CompletesForm_ResolvesQuery_AndPublishesOutboxEvent()
    {
        // Arrange
        var formSubmission = FormSubmission.Initialize(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            formId: new FormId("AE_CRF")).Value!;

        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            testCode: new TestCode("ALT"),
            explanation: "Toxicity review required").Value!;

        var stagedReview = StagedAdverseEventReview.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            testCode: new TestCode("ALT"),
            testName: "Alanine Aminotransferase",
            labValue: 250.0,
            unit: "U/L",
            ctcaeGrade: 3,
            toxicityTerm: "ALT increased",
            clinicalDescription: "Grade 3 ALT elevation",
            isHysLawWarning: false,
            isDeltaCheckAlert: false,
            deltaPercentage: null,
            formSubmissionId: formSubmission.Id,
            queryId: query.Id);

        _stagedReviewRepository.GetByIdAsync(stagedReview.Id, Arg.Any<CancellationToken>())
            .Returns(stagedReview);

        _formSubmissionRepository.GetForUpdateAsync(formSubmission.Id, Arg.Any<CancellationToken>())
            .Returns(formSubmission);

        _queryRepository.GetForUpdateAsync(query.Id, Arg.Any<CancellationToken>())
            .Returns(query);

        var governanceSvc = new LabToxicityGovernanceService(
            _stagedReviewRepository,
            _formSubmissionRepository,
            _queryRepository,
            _publishEndpoint,
            _clock,
            _auditContextAccessor);

        var caller = new AuthenticatedCaller("investigator_user_01", ["INVESTIGATOR"]);
        var actionRequest = new StagedAdverseEventActionRequest(
            Action: "CONFIRM",
            ReasonForChange: "Confirmed Grade 3 AE by PI",
            InvestigatorNotes: "Patient monitored closely");

        // Act
        var result = await governanceSvc.ExecuteActionAsync(stagedReview.Id, actionRequest, caller, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        stagedReview.ReviewStatus.Should().Be("CONFIRMED");
        stagedReview.ReviewedBy.Should().Be("investigator_user_01");
        formSubmission.Status.Should().Be(FormSubmissionStatus.Completed);
        query.Status.Should().Be(QueryStatus.Closed);

        await _publishEndpoint.Received(1).Publish(
            Arg.Is<Grade3ToxicityDetected>(e =>
                e.StudyId == "STUDY-001" &&
                e.SubjectId == "SUBJ-101" &&
                e.TestCode == "ALT" &&
                e.CtcaeGrade == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteActionAsync_Dismiss_DeactivatesForm_CancelsQuery_WithoutOutboxPublish")]
    public async Task ExecuteActionAsync_Dismiss_DeactivatesForm_CancelsQuery_WithoutOutboxPublish()
    {
        // Arrange
        var formSubmission = FormSubmission.Initialize(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            formId: new FormId("AE_CRF")).Value!;

        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            testCode: new TestCode("ALT"),
            explanation: "Toxicity review required").Value!;

        var stagedReview = StagedAdverseEventReview.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            testCode: new TestCode("ALT"),
            testName: "Alanine Aminotransferase",
            labValue: 250.0,
            unit: "U/L",
            ctcaeGrade: 3,
            toxicityTerm: "ALT increased",
            clinicalDescription: "Grade 3 ALT elevation",
            isHysLawWarning: false,
            isDeltaCheckAlert: false,
            deltaPercentage: null,
            formSubmissionId: formSubmission.Id,
            queryId: query.Id);

        _stagedReviewRepository.GetByIdAsync(stagedReview.Id, Arg.Any<CancellationToken>())
            .Returns(stagedReview);

        _formSubmissionRepository.GetForUpdateAsync(formSubmission.Id, Arg.Any<CancellationToken>())
            .Returns(formSubmission);

        _queryRepository.GetForUpdateAsync(query.Id, Arg.Any<CancellationToken>())
            .Returns(query);

        var governanceSvc = new LabToxicityGovernanceService(
            _stagedReviewRepository,
            _formSubmissionRepository,
            _queryRepository,
            _publishEndpoint,
            _clock,
            _auditContextAccessor);

        var caller = new AuthenticatedCaller("investigator_user_01", ["INVESTIGATOR"]);
        var actionRequest = new StagedAdverseEventActionRequest(
            Action: "DISMISS",
            ReasonForChange: "Lab artifact, repeat normal",
            InvestigatorNotes: "Lab error confirmed");

        // Act
        var result = await governanceSvc.ExecuteActionAsync(stagedReview.Id, actionRequest, caller, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        stagedReview.ReviewStatus.Should().Be("DISMISSED");
        formSubmission.IsActive.Should().BeFalse();
        query.Status.Should().Be(QueryStatus.Cancelled);

        await _publishEndpoint.DidNotReceive().Publish(Arg.Any<Grade3ToxicityDetected>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteActionAsync_EscalateSae_PublishesOutboxWithCtcaeGrade4")]
    public async Task ExecuteActionAsync_EscalateSae_PublishesOutboxWithCtcaeGrade4()
    {
        // Arrange
        var stagedReview = StagedAdverseEventReview.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            testCode: new TestCode("ALT"),
            testName: "Alanine Aminotransferase",
            labValue: 850.0,
            unit: "U/L",
            ctcaeGrade: 4,
            toxicityTerm: "ALT increased",
            clinicalDescription: "Grade 4 ALT elevation",
            isHysLawWarning: true,
            isDeltaCheckAlert: false,
            deltaPercentage: null);

        _stagedReviewRepository.GetByIdAsync(stagedReview.Id, Arg.Any<CancellationToken>())
            .Returns(stagedReview);

        var governanceSvc = new LabToxicityGovernanceService(
            _stagedReviewRepository,
            _formSubmissionRepository,
            _queryRepository,
            _publishEndpoint,
            _clock,
            _auditContextAccessor);

        var caller = new AuthenticatedCaller("investigator_user_01", ["INVESTIGATOR"]);
        var actionRequest = new StagedAdverseEventActionRequest(
            Action: "ESCALATE_SAE",
            ReasonForChange: "Escalated to SAE per protocol safety criteria",
            InvestigatorNotes: "Patient hospitalized");

        // Act
        var result = await governanceSvc.ExecuteActionAsync(stagedReview.Id, actionRequest, caller, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        stagedReview.ReviewStatus.Should().Be("ESCALATED_SAE");

        await _publishEndpoint.Received(1).Publish(
            Arg.Is<Grade3ToxicityDetected>(e =>
                e.StudyId == "STUDY-001" &&
                e.SubjectId == "SUBJ-101" &&
                e.CtcaeGrade == 4),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "EvaluateAsync_ReturnsCTCAEEvaluationAndHysLawStatus")]
    public async Task EvaluateAsync_ReturnsCTCAEEvaluationAndHysLawStatus()
    {
        // Arrange
        var governanceSvc = new LabToxicityGovernanceService(
            _stagedReviewRepository,
            _formSubmissionRepository,
            _queryRepository,
            _publishEndpoint,
            _clock,
            _auditContextAccessor);

        var evalRequest = new LabToxicityEvaluateRequest(
            TestCode: "ALT",
            Value: 250.0,
            Unit: "U/L",
            RefHigh: 40.0,
            RefLow: 10.0);

        // Act
        var result = await governanceSvc.EvaluateAsync(evalRequest, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var val = result.Value!;
        val.CtcaeGrade.Should().Be(3);
        val.IsCriticalAeTrigger.Should().BeTrue();
        val.TestCode.Should().Be("ALT");
    }
}
