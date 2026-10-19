using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Verifies multi-layer validation of reason for change during observation correction across domain aggregate,
/// command handler, and HTTP API endpoint layers.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ObservationReasonForChangeValidationTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly EnrollmentId Enrollment = EnrollmentId.New();
    private static readonly ParticipantId Participant = ParticipantId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly ConceptRef Concept = new(ConceptId.New(), 1);
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;

    #region Domain Aggregate Tests

    [Theory(DisplayName = "Domain_ObservationCorrect_WithNullOrWhitespaceReason_ReturnsReasonForChangeRequired")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DomainObservationCorrectWithNullOrWhitespaceReasonReturnsReasonForChangeRequired(string? invalidReason)
    {
        var obsId = ObservationId.New();
        var recordResult = Observation.Record(
            obsId, Tenant, Enrollment, Participant, Study, Concept,
            value: "100", observedAt: T0, reasonForChange: string.Empty, recordedBy: "user", recordedAt: T0);

        recordResult.IsSuccess.Should().BeTrue();
        var observation = recordResult.Value!;

        var error = observation.Correct("120", invalidReason!, "user", T0.AddMinutes(5));

        error.Should().Be(ObservationErrors.ReasonForChangeRequired);
        observation.Value.Should().Be("100", "invalid correction must not update observation value");
        observation.Revision.Should().Be(1, "invalid correction must not increment revision number");
        observation.Revisions.Should().HaveCount(1);
    }

    [Fact(DisplayName = "Domain_ObservationCorrect_WithValidReason_Succeeds")]
    public void DomainObservationCorrectWithValidReasonSucceeds()
    {
        var obsId = ObservationId.New();
        var recordResult = Observation.Record(
            obsId, Tenant, Enrollment, Participant, Study, Concept,
            value: "100", observedAt: T0, reasonForChange: string.Empty, recordedBy: "user", recordedAt: T0);

        var observation = recordResult.Value!;
        var error = observation.Correct("120", "Transcription error", "user", T0.AddMinutes(5));

        error.Should().BeNull();
        observation.Value.Should().Be("120");
        observation.Revision.Should().Be(2);
        observation.Revisions.Should().HaveCount(2);
        observation.Revisions[1].ReasonForChange.Should().Be("Transcription error");
    }

    [Fact(DisplayName = "Domain_ObservationRecord_WithoutReason_SucceedsForBackwardCompatibility")]
    public void DomainObservationRecordWithoutReasonSucceedsForBackwardCompatibility()
    {
        var obsId = ObservationId.New();
        var recordResult = Observation.Record(
            obsId, Tenant, Enrollment, Participant, Study, Concept,
            value: "100", observedAt: T0, reasonForChange: string.Empty, recordedBy: "user", recordedAt: T0);

        recordResult.IsSuccess.Should().BeTrue();
        recordResult.Value!.Value.Should().Be("100");
    }

    #endregion

    #region Application Command Handler Tests

    [Theory(DisplayName = "Handler_CorrectObservationHandler_WithWhitespaceReason_RejectsBeforeStateReads")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandlerCorrectObservationHandlerWithWhitespaceReasonRejectsBeforeStateReads(string? invalidReason)
    {
        var handler = new CorrectObservationHandler();
        var command = new CorrectObservation(
            Tenant, ObservationId.New(), Enrollment, Value: "120", ExpectedVersion: 1, IdempotencyKey: "k1", ReasonForChange: invalidReason!);

        var context = Substitute.For<ICommandContext>();
        var work = Substitute.For<IKernelUnitOfWork>();
        context.Work.Returns(work);

        var result = await handler.HandleAsync(command, context, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(ObservationErrors.ReasonForChangeRequired);

        // Verify no state reads or entity loads occurred before rejection
        await work.DidNotReceive().LockAsync<ObservationLock>(Arg.Any<ObservationLockId>(), Arg.Any<CancellationToken>());
        await work.DidNotReceive().ReadAsync<ObservationLock>(Arg.Any<ObservationLockId>(), Arg.Any<CancellationToken>());
        await work.DidNotReceive().FindAsync<Observation>(Arg.Any<ObservationId>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region HTTP API Endpoint Tests

    [Theory(DisplayName = "Endpoint_CorrectAsync_WithWhitespaceReason_ReturnsHttp422Problem")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EndpointCorrectAsyncWithWhitespaceReasonReturnsHttp422Problem(string? invalidReason)
    {
        var obsId = ObservationId.New();
        var obsRef = NodeRef.For(obsId).ToString();
        var enrollmentRef = NodeRef.For(Enrollment).ToString();
        var request = new KernelCorrectObservationRequest(enrollmentRef, Value: "120", ExpectedVersion: 1);

        var httpContext = new DefaultHttpContext();
        KernelRequestTenant.Set(httpContext, Tenant);
        var sender = Substitute.For<IKernelCommandSender>();
        var changeReason = Substitute.For<IKernelChangeReason>();
        changeReason.Read(httpContext).Returns(invalidReason);

        var response = await KernelObservationEndpoints.CorrectAsync(
            obsRef, request, httpContext, sender, changeReason, CancellationToken.None);

        response.Result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)response.Result;
        problem.StatusCode.Should().Be(422);
        problem.ProblemDetails.Title.Should().Be("K.Observation.ReasonForChangeRequired");

        // Verify command sender was NOT called
        await sender.DidNotReceive().SendAsync<ObservationState>(
            Arg.Any<ICommand<ObservationState>>(), Arg.Any<CommandMetadata>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Endpoint_CorrectAsync_WithValidReason_DispatchesCommandToSender")]
    public async Task EndpointCorrectAsyncWithValidReasonDispatchesCommandToSender()
    {
        var obsId = ObservationId.New();
        var obsRef = NodeRef.For(obsId).ToString();
        var enrollmentRef = NodeRef.For(Enrollment).ToString();
        var request = new KernelCorrectObservationRequest(enrollmentRef, Value: "120", ExpectedVersion: 1);

        var httpContext = new DefaultHttpContext();
        KernelRequestTenant.Set(httpContext, Tenant);
        var sender = Substitute.For<IKernelCommandSender>();
        var changeReason = Substitute.For<IKernelChangeReason>();
        changeReason.Read(httpContext).Returns("Valid reason for correction");

        var expectedState = new ObservationState(obsId, Enrollment, Participant, Study, Concept, "120", Version: 2, Revision: 2);
        sender.SendAsync(Arg.Any<CorrectObservation>(), Arg.Any<CommandMetadata>(), Arg.Any<CancellationToken>())
            .Returns(CommandResult<ObservationState>.Success(expectedState));

        var response = await KernelObservationEndpoints.CorrectAsync(
            obsRef, request, httpContext, sender, changeReason, CancellationToken.None);

        response.Result.Should().BeOfType<Ok<KernelObservationStateResponse>>();
        var ok = (Ok<KernelObservationStateResponse>)response.Result;
        ok.Value!.Value.Should().Be("120");

        await sender.Received(1).SendAsync(
            Arg.Is<CorrectObservation>(c => c.ReasonForChange == "Valid reason for correction"),
            Arg.Any<CommandMetadata>(),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
