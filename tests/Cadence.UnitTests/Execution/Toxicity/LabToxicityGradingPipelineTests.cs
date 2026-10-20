using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Toxicity;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Contracts;
using Cadence.Kernel.Time;

using FluentAssertions;

using MassTransit;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Toxicity;

public sealed class LabToxicityGradingPipelineTests
{
    private readonly IClock _clock = SystemClock.Instance;
    private readonly ICurrentAuditContextAccessor _auditContextAccessor = Substitute.For<ICurrentAuditContextAccessor>();
    private readonly IStagedAdverseEventReviewRepository _stagedReviewRepository = Substitute.For<IStagedAdverseEventReviewRepository>();
    private readonly IClinicalQueryRepository _queryRepository = Substitute.For<IClinicalQueryRepository>();
    private readonly IPublishEndpoint _publishEndpoint = Substitute.For<IPublishEndpoint>();

    public LabToxicityGradingPipelineTests()
    {
        _auditContextAccessor.UserId.Returns("investigator_user_01");
    }

    [Fact(DisplayName = "ExecuteActionAsync_Confirm_ResolvesQuery_AndPublishesOutboxEvent")]
    public async Task ExecuteActionAsync_Confirm_ResolvesQuery_AndPublishesOutboxEvent()
    {
        // Arrange
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
            queryId: query.Id);

        _stagedReviewRepository.GetByIdAsync(stagedReview.Id, Arg.Any<CancellationToken>())
            .Returns(stagedReview);

        _queryRepository.GetForUpdateAsync(query.Id, Arg.Any<CancellationToken>())
            .Returns(query);

        var governanceSvc = new LabToxicityGovernanceService(
            _stagedReviewRepository,
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
        query.Status.Should().Be(QueryStatus.Closed);

        await _publishEndpoint.Received(1).Publish(
            Arg.Is<Grade3ToxicityDetected>(e =>
                e.StudyId == "STUDY-001" &&
                e.SubjectId == "SUBJ-101" &&
                e.TestCode == "ALT" &&
                e.CtcaeGrade == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteActionAsync_Dismiss_CancelsQuery_WithoutOutboxPublish")]
    public async Task ExecuteActionAsync_Dismiss_CancelsQuery_WithoutOutboxPublish()
    {
        // Arrange
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
            queryId: query.Id);

        _stagedReviewRepository.GetByIdAsync(stagedReview.Id, Arg.Any<CancellationToken>())
            .Returns(stagedReview);

        _queryRepository.GetForUpdateAsync(query.Id, Arg.Any<CancellationToken>())
            .Returns(query);

        var governanceSvc = new LabToxicityGovernanceService(
            _stagedReviewRepository,
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
