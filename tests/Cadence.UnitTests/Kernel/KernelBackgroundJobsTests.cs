using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Registration of the kernel's background jobs (ADR-2449): the outbox relay, the audit sealing pass and the K-LIN-1
/// lineage check (ADR-2471).
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelBackgroundJobsTests
{
    /// <summary>
    /// Validates that a host opting in gets exactly the relay, sealing and lineage-check services, resolvable with the
    /// host's publisher, node visibility, clock and logging, and that the options it configures are the ones they use.
    /// </summary>
    [Fact(DisplayName = "AddKernelBackgroundJobs_RegistersRelaySealingAndLineageCheckServices")]
    public void AddKernelBackgroundJobsRegistersRelaySealingAndLineageCheckServices()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IClock>(new FakeClock())
            .AddSingleton<IKernelEventPublisher>(new NoOpPublisher())
            .AddSingleton<INodeVisibility>(new ShowAll())
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddKernelPersistence("Host=localhost;Database=unused")
            .AddKernelBackgroundJobs(o =>
            {
                o.SealInterval = TimeSpan.FromMinutes(15);
                o.LineageCheckInterval = TimeSpan.FromMinutes(30);
            })
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var hosted = services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();

        hosted.Should().BeEquivalentTo("KernelOutboxRelayService", "KernelAuditSealingService", "KernelLineageCheckService");
        services.GetRequiredService<KernelBackgroundOptions>().SealInterval.Should().Be(TimeSpan.FromMinutes(15));
        services.GetRequiredService<KernelBackgroundOptions>().LineageCheckInterval.Should().Be(TimeSpan.FromMinutes(30));
    }

    /// <summary>
    /// Validates ADR-2471's fail-closed default: kernel persistence registers no node visibility, so a host that never
    /// decided who may see which objects cannot build its container instead of serving unmasked lineage.
    /// </summary>
    [Fact(DisplayName = "AddKernelPersistence_WithoutNodeVisibility_FailsValidation")]
    public void AddKernelPersistenceWithoutNodeVisibilityFailsValidation()
    {
        var build = () => new ServiceCollection()
            .AddSingleton<IClock>(new FakeClock())
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddKernelPersistence("Host=localhost;Database=unused")
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        build.Should().Throw<AggregateException>().WithMessage($"*{nameof(INodeVisibility)}*");
    }

    /// <summary>
    /// Validates that a zero or negative relay batch size is refused at registration rather than failing at run time.
    /// </summary>
    [Fact(DisplayName = "AddKernelBackgroundJobs_RejectsNonPositiveBatchSize")]
    public void AddKernelBackgroundJobsRejectsNonPositiveBatchSize()
    {
        var act = () => new ServiceCollection().AddKernelBackgroundJobs(o => o.RelayBatchSize = 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class ShowAll : INodeVisibility
    {
        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(new Dictionary<NodeRef, MaskReason>());
    }

    private sealed class NoOpPublisher : IKernelEventPublisher
    {
        public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
