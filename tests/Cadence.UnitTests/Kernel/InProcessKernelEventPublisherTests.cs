using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>In-process delivery of committed events to registered consumers (ADR-2600 §4.6, ADR-2601).</summary>
[Trait("Category", "Unit")]
public sealed class InProcessKernelEventPublisherTests
{
    /// <summary>
    /// Validates that every consumer that handles an event is called in its own scope acting as its service account,
    /// that a consumer for another event is skipped, and that one failure does not stop the others but is rethrown so
    /// the relay retries the event.
    /// </summary>
    [Fact(DisplayName = "Publish_CallsEveryHandlingConsumerAsItsServiceAccount_AndRethrowsFailures")]
    public async Task PublishCallsEveryHandlingConsumerAsItsServiceAccountAndRethrowsFailures()
    {
        var ct = TestContext.Current.CancellationToken;
        var log = new DeliveryLog();
        await using var services = new ServiceCollection()
            .AddSingleton(log)
            .AddScoped<ConsumerActor>()
            .AddKernelInProcessEvents()
            .AddKernelEventConsumer<FailingProbeConsumer>("a-failing")
            .AddKernelEventConsumer<ProbeConsumer>("b-recording")
            .AddKernelEventConsumer<OtherEventConsumer>("c-other")
            .BuildServiceProvider();
        var envelope = Envelope("probe.Happened", 1);

        var act = () => services.GetRequiredService<IKernelEventPublisher>().PublishAsync(envelope, ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*'a-failing'*");
        log.Deliveries.Should().Equal(
            ("a-failing", "svc:a-failing", $"a-failing:{envelope.EventId:D}"),
            ("b-recording", "svc:b-recording", $"b-recording:{envelope.EventId:D}"));
    }

    /// <summary>Validates that a typed consumer matches its event's type and schema version only.</summary>
    [Fact(DisplayName = "TypedConsumer_MatchesTypeAndSchemaVersion")]
    public void TypedConsumerMatchesTypeAndSchemaVersion()
    {
        var consumer = new ProbeConsumer(new DeliveryLog(), new ConsumerActor());

        consumer.Handles(Envelope("probe.Happened", 1)).Should().BeTrue();
        consumer.Handles(Envelope("probe.Happened", 2)).Should().BeFalse();
        consumer.Handles(Envelope("probe.Other", 1)).Should().BeFalse();
    }

    /// <summary>Validates that consumer names are service-account safe and unique per host.</summary>
    [Fact(DisplayName = "AddKernelEventConsumer_RequiresUniqueSafeNames")]
    public void AddKernelEventConsumerRequiresUniqueSafeNames()
    {
        var services = new ServiceCollection().AddKernelEventConsumer<ProbeConsumer>("probe");

        var duplicate = () => services.AddKernelEventConsumer<OtherEventConsumer>("probe");
        var unsafeName = () => services.AddKernelEventConsumer<OtherEventConsumer>("Probe:Other");

        duplicate.Should().Throw<InvalidOperationException>();
        unsafeName.Should().Throw<ArgumentException>();
    }

    private static EventEnvelope Envelope(string type, int version) =>
        new(Guid.CreateVersion7(), type, version, TenantId.New(), null, DateTimeOffset.UnixEpoch, "user-1", Guid.CreateVersion7(),
            Guid.CreateVersion7(), null, JsonSerializer.SerializeToElement(new ProbeHappened("value"), JsonSerializerOptions.Web));

    /// <summary>The event the probe consumers handle.</summary>
    [KernelEvent("probe.Happened", 1)]
    public sealed record ProbeHappened(string Value);

    /// <summary>An event nobody here handles.</summary>
    [KernelEvent("probe.Other", 1)]
    public sealed record ProbeOther(string Value);

    internal sealed class DeliveryLog
    {
        public List<(string Consumer, string Actor, string Key)> Deliveries { get; } = [];
    }

    internal sealed class ProbeConsumer(DeliveryLog log, ConsumerActor actor) : KernelEventConsumer<ProbeHappened>
    {
        protected override Task HandleAsync(ProbeHappened payload, KernelEventDelivery delivery, CancellationToken cancellationToken)
        {
            payload.Value.Should().Be("value");
            log.Deliveries.Add((delivery.Consumer, actor.Actor!.Id, delivery.IdempotencyKey));
            return Task.CompletedTask;
        }
    }

    internal sealed class FailingProbeConsumer(DeliveryLog log, ConsumerActor actor) : KernelEventConsumer<ProbeHappened>
    {
        protected override Task HandleAsync(ProbeHappened payload, KernelEventDelivery delivery, CancellationToken cancellationToken)
        {
            log.Deliveries.Add((delivery.Consumer, actor.Actor!.Id, delivery.IdempotencyKey));
            throw new InvalidOperationException("Consumer failed on purpose.");
        }
    }

    internal sealed class OtherEventConsumer(DeliveryLog log) : KernelEventConsumer<ProbeOther>
    {
        protected override Task HandleAsync(ProbeOther payload, KernelEventDelivery delivery, CancellationToken cancellationToken)
        {
            log.Deliveries.Add((delivery.Consumer, string.Empty, delivery.IdempotencyKey));
            return Task.CompletedTask;
        }
    }
}
