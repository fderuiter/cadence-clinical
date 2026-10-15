using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Adaptive;
using Cadence.Kernel.Time;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution;

public sealed class AdaptiveDoseEscalationServiceTests
{
    private readonly RecordingAuditLogWriter _auditWriter;
    private readonly IClock _clock;
    private readonly AdaptiveDoseEscalationService _service;

    public AdaptiveDoseEscalationServiceTests()
    {
        _auditWriter = new RecordingAuditLogWriter();
        _clock = SystemClock.Instance;
        _service = new AdaptiveDoseEscalationService(_auditWriter, _clock);
    }

    [Fact]
    public async Task GetStateAsync_ReturnsValidBOINAndRARState()
    {
        // Act
        var state = await _service.GetStateAsync("STUDY-BOIN-001", TestContext.Current.CancellationToken);

        // Assert
        state.Should().NotBeNull();
        state.StudyId.Should().Be("STUDY-BOIN-001");
        state.DesignType.Should().Be("BOIN");
        state.TargetToxicityRate.Should().Be(0.25);
        state.LambdaE.Should().BeGreaterThan(0);
        state.LambdaD.Should().BeGreaterThan(state.LambdaE);
        state.DoseSummaries.Should().HaveCount(5);
        state.RarArms.Should().HaveCount(3);
    }

    [Fact]
    public async Task SubmitCohortTransitionAsync_ValidTransition_UpdatesStateAndWritesAuditLog()
    {
        // Arrange
        // Initial state for cohort at dose index 1 (20mg, 3 subjects, 0 DLTs) produces BoinEngine ESCALATE to dose index 2.
        var request = new AdaptiveCohortTransitionRequest(
            StudyId: "STUDY-BOIN-001",
            CohortNumber: 1,
            DecisionAction: "ESCALATE",
            TargetDoseLevelIndex: 2,
            OverrideReason: null,
            ElectronicSignature: "dr.smith@hospital.org"
        );
        var caller = new AuthenticatedCaller("USR-INVESTIGATOR-1", new[] { "Investigator" });

        // Act
        var result = await _service.SubmitCohortTransitionAsync(request, caller, sigToken: null, changeReasonHeader: null, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var response = result.Value!;
        response.StudyId.Should().Be("STUDY-BOIN-001");
        response.ActionExecuted.Should().Be("ESCALATE");
        response.NewDoseLevelIndex.Should().Be(2);
        response.IsOverride.Should().BeFalse();
        response.AuthorizedBy.Should().Be("USR-INVESTIGATOR-1");

        _auditWriter.Calls.Should().ContainSingle();
        var audit = _auditWriter.Calls[0];
        audit.EntityName.Should().Be("AdaptiveCohortTransition");
        audit.RecordId.Should().Contain("STUDY-BOIN-001");
        audit.Action.Should().Be("COHORT_TRANSITION_ESCALATE");
        audit.UserId.Should().Be("USR-INVESTIGATOR-1");
    }

    [Fact]
    public async Task SubmitCohortTransitionAsync_OverrideWithoutRationale_Returns422Error()
    {
        // Arrange
        // Recommendation is ESCALATE to dose index 2. Requesting STAY at dose index 1 without override rationale.
        var request = new AdaptiveCohortTransitionRequest(
            StudyId: "STUDY-BOIN-OVERRIDE",
            CohortNumber: 1,
            DecisionAction: "STAY",
            TargetDoseLevelIndex: 1,
            OverrideReason: null,
            ElectronicSignature: "dr.smith@hospital.org"
        );
        var caller = new AuthenticatedCaller("USR-INVESTIGATOR-1", new[] { "Investigator" });

        // Act
        var result = await _service.SubmitCohortTransitionAsync(request, caller, sigToken: null, changeReasonHeader: null, TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("OVERRIDE_RATIONALE_REQUIRED");
    }

    [Fact]
    public async Task SubmitCohortTransitionAsync_OverrideWithRationale_Succeeds()
    {
        // Arrange
        // Recommendation is ESCALATE to dose index 2. Requesting STAY at dose index 1 with rationale.
        var request = new AdaptiveCohortTransitionRequest(
            StudyId: "STUDY-BOIN-OVERRIDE-OK",
            CohortNumber: 1,
            DecisionAction: "STAY",
            TargetDoseLevelIndex: 1,
            OverrideReason: "Safety committee decided to expand current cohort for additional PK sampling.",
            ElectronicSignature: "dr.smith@hospital.org"
        );
        var caller = new AuthenticatedCaller("USR-INVESTIGATOR-1", new[] { "Investigator" });

        // Act
        var result = await _service.SubmitCohortTransitionAsync(request, caller, sigToken: null, changeReasonHeader: null, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.IsOverride.Should().BeTrue();
        result.Value.OverrideReason.Should().Be("Safety committee decided to expand current cohort for additional PK sampling.");
    }

    [Fact]
    public async Task SubmitCohortTransitionAsync_MissingElectronicSignature_ReturnsUnauthorizedError()
    {
        // Arrange
        var request = new AdaptiveCohortTransitionRequest(
            StudyId: "STUDY-BOIN-001",
            CohortNumber: 1,
            DecisionAction: "STAY",
            TargetDoseLevelIndex: 1,
            ElectronicSignature: null
        );
        var caller = new AuthenticatedCaller("USR-INVESTIGATOR-1", new[] { "Investigator" });

        // Act
        var result = await _service.SubmitCohortTransitionAsync(request, caller, sigToken: null, changeReasonHeader: null, TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ELECTRONIC_SIGNATURE_REQUIRED");
    }

    [Fact]
    public async Task SubmitCohortTransitionAsync_InvalidStudyId_ReturnsValidationError()
    {
        // Arrange
        var request = new AdaptiveCohortTransitionRequest(
            StudyId: "",
            CohortNumber: 1,
            DecisionAction: "STAY",
            TargetDoseLevelIndex: 1,
            ElectronicSignature: "sig"
        );
        var caller = new AuthenticatedCaller("USR-INVESTIGATOR-1", new[] { "Investigator" });

        // Act
        var result = await _service.SubmitCohortTransitionAsync(request, caller, sigToken: null, changeReasonHeader: null, TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_STUDY_ID");
    }
}
