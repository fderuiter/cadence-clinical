using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Background;
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

/// <summary>Registration of the kernel's background jobs (ADR-2449): the outbox relay and the audit sealing pass.</summary>
[Trait("Category", "Unit")]
public sealed class KernelBackgroundJobsTests
{
    /// <summary>
    /// Validates that a host opting in gets exactly the relay and sealing services, resolvable with the host's
    /// publisher, clock and logging, and that the options it configures are the ones they use.
    /// </summary>
    [Fact(DisplayName = "AddKernelBackgroundJobs_RegistersRelayAndSealingServices")]
    public void AddKernelBackgroundJobsRegistersRelayAndSealingServices()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IClock>(new FakeClock())
            .AddSingleton<IKernelEventPublisher>(new NoOpPublisher())
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddKernelPersistence("Host=localhost;Database=unused")
            .AddKernelBackgroundJobs(o => o.SealInterval = TimeSpan.FromMinutes(15))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var hosted = services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();

        hosted.Should().BeEquivalentTo("KernelOutboxRelayService", "KernelAuditSealingService");
        services.GetRequiredService<KernelBackgroundOptions>().SealInterval.Should().Be(TimeSpan.FromMinutes(15));
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

    private sealed class NoOpPublisher : IKernelEventPublisher
    {
        public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
