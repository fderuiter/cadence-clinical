using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Security.Authentication;
using Cadence.Testing.Hosting;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class TestHostSettingsTests
{
    [Fact]
    public void RemoveBackgroundHostedServices_PreservesSigTokenUseStoreStartupCheckAndRequestedWorkers()
    {
        var services = new ServiceCollection();
        services.AddHostedService<PollingHostedService>();
        services.AddSingleton<IHostedService>(_ => new PollingHostedService());
        services.AddHostedService<RetainedWorkerHostedService>();
        services.AddHostedService<SigTokenUseStoreStartupCheck>();

        services.RemoveBackgroundHostedServices(typeof(RetainedWorkerHostedService));

        var remainingTypes = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();

        remainingTypes.Should().BeEquivalentTo(
        [
            typeof(RetainedWorkerHostedService),
            typeof(SigTokenUseStoreStartupCheck),
        ]);
    }

    private sealed class PollingHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RetainedWorkerHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
