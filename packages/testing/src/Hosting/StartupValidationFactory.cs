using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cadence.Testing.Hosting;

/// <summary>
/// Builds a service's real <c>Program</c> host and runs its startup validation (every <c>ValidateOnStart</c>
/// registration) on the test thread, without starting the host.
/// <para>
/// Asserting a startup failure through <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/> is racy. The
/// entry point runs <c>Program</c> on its own thread, where the failed start disposes the host; the factory's
/// <c>DeferredHost.StartAsync</c> reads that host's services on the test thread. When the entry point wins, the test
/// sees an <see cref="ObjectDisposedException"/> instead of the <see cref="OptionsValidationException"/>, which is
/// what made these tests fail intermittently on busy full runs. Here the entry point's start is held before
/// validation, and the factory never starts the host, so its services stay alive while the test validates them.
/// </para>
/// </summary>
/// <typeparam name="TEntryPoint">The service's <c>Program</c>.</typeparam>
public sealed class StartupValidationFactory<TEntryPoint>(Action<IWebHostBuilder> configure) : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly HeldHostLifetime _lifetime = new();

    /// <summary>
    /// Runs the host's startup validation and returns the exception it throws, or <see langword="null"/> when the
    /// configuration is valid.
    /// </summary>
    public Exception? ValidateStartup()
    {
        IServiceProvider services = Services;
        try
        {
            services.GetService<IStartupValidator>()?.Validate();
            return null;
        }
        catch (Exception ex) when (ex is OptionsValidationException or AggregateException)
        {
            return ex;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        configure(builder);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostLifetime>();
            services.AddSingleton<IHostLifetime>(_lifetime);
        });

        // Build, but do not start: the entry point's own start waits in HeldHostLifetime until Dispose.
        return new UnstartedHost(builder.Build());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _lifetime.Cancel();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _lifetime.Cancel();
    }

    /// <summary>
    /// The built host, which is never started here. Stopping it is a no-op, because a host that never started has
    /// nothing to stop and <c>Host.StopAsync</c> fails on one.
    /// </summary>
    private sealed class UnstartedHost(IHost inner) : IHost
    {
        public IServiceProvider Services => inner.Services;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() => inner.Dispose();
    }

    /// <summary>
    /// Holds the entry point's <c>StartAsync</c> before startup validation. Once the factory has disposed the host,
    /// the held start is cancelled, so the entry point ends without starting anything.
    /// </summary>
    private sealed class HeldHostLifetime : IHostLifetime
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Cancel() => _released.TrySetCanceled();

        public Task WaitForStartAsync(CancellationToken cancellationToken) =>
            _released.Task.WaitAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
