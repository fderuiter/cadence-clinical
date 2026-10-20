using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Time;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// A correction without a reason for change is refused with the pipeline's one code,
/// <c>K.Command.ReasonForChangeRequired</c>, as every kernel mutation is (#5437). The pipeline refuses it before a
/// unit of work opens; the entity guard returns the same code.
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

    [Theory(DisplayName = "Pipeline_CorrectObservation_WithoutAReason_IsRefusedBeforeAUnitOfWork")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PipelineCorrectObservationWithoutAReasonIsRefusedBeforeAUnitOfWork(string reason)
    {
        var ct = TestContext.Current.CancellationToken;
        var actor = Substitute.For<ICurrentActor>();
        actor.Actor.Returns(Actor.Person("user-1"));
        var unitOfWork = Substitute.For<IKernelUnitOfWorkFactory>();
        await using var services = new ServiceCollection()
            .AddSingleton<IClock>(new FakeClock())
            .AddSingleton(actor)
            .AddSingleton(Substitute.For<IAuthorizer>())
            .AddSingleton(unitOfWork)
            .AddKernelCommandPipeline()
            .BuildServiceProvider();

        var result = await services.GetRequiredService<IKernelCommandSender>().SendAsync(
            new CorrectObservation(Tenant, ObservationId.New(), Enrollment, new QuantityValue(120m, "mm[Hg]"), ExpectedVersion: 1, IdempotencyKey: "k1", reason),
            cancellationToken: ct);

        result.Error.Should().Be(KernelErrors.ReasonForChangeRequired);
        unitOfWork.DidNotReceive().Begin(Arg.Any<CommandExecution>());
    }

    [Theory(DisplayName = "Domain_ObservationCorrect_WithoutAReason_ReturnsThePipelineCode")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DomainObservationCorrectWithoutAReasonReturnsThePipelineCode(string? reason)
    {
        var observation = Record();

        observation.Correct(new QuantityValue(120m, "mm[Hg]"), reason!, "user", T0.AddMinutes(5)).Should().Be(KernelErrors.ReasonForChangeRequired);

        observation.Value.Should().Be(new QuantityValue(100m, "mm[Hg]"), "a refused correction does not change the value");
        observation.Revisions.Should().ContainSingle();
    }

    [Fact(DisplayName = "Domain_ObservationCorrect_WithAReason_AppendsARevision")]
    public void DomainObservationCorrectWithAReasonAppendsARevision()
    {
        var observation = Record();

        observation.Correct(new QuantityValue(120m, "mm[Hg]"), "Transcription error", "user", T0.AddMinutes(5)).Should().BeNull();

        observation.Value.Should().Be(new QuantityValue(120m, "mm[Hg]"));
        observation.Revisions.Should().HaveCount(2);
        observation.Revisions[1].ReasonForChange.Should().Be("Transcription error");
    }

    private static Observation Record() =>
        Observation.Record(
            ObservationId.New(), Tenant, Enrollment, Participant, Study, Concept,
            value: new QuantityValue(100m, "mm[Hg]"), observedAt: T0, reasonForChange: string.Empty, recordedBy: "user", recordedAt: T0).Value!;
}
