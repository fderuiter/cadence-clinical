using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Persistence.Audit;
using Cadence.Kernel.Persistence.Outbox;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Persistence.Background;

/// <summary>Schedules for the kernel's background jobs.</summary>
public sealed class KernelBackgroundOptions
{
    /// <summary>How long the relay waits after a pass that found fewer than <see cref="RelayBatchSize"/> messages.</summary>
    public TimeSpan RelayInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Messages claimed per relay pass.</summary>
    public int RelayBatchSize { get; set; } = 100;

    /// <summary>How often every tenant's audit chain is verified and sealed.</summary>
    public TimeSpan SealInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>Runs <see cref="KernelOutboxRelay"/> continuously: back to back while there is a backlog, then on an interval.</summary>
internal sealed partial class KernelOutboxRelayService(
    KernelOutboxRelay relay,
    KernelBackgroundOptions options,
    ILogger<KernelOutboxRelayService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var backlog = false;
            try
            {
                var result = await relay.RelayBatchAsync(options.RelayBatchSize, stoppingToken).ConfigureAwait(false);
                backlog = result.Claimed == options.RelayBatchSize && result.Failed == 0;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogPassFailed(logger, ex);
            }

            if (!backlog)
            {
                await Delay(options.RelayInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal static async Task Delay(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Kernel outbox relay pass failed; retrying after the relay interval.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}

/// <summary>Runs <see cref="KernelAuditChainSealer.SealAllAsync"/> on <see cref="KernelBackgroundOptions.SealInterval"/>.</summary>
internal sealed partial class KernelAuditSealingService(
    KernelAuditChainSealer sealer,
    KernelBackgroundOptions options,
    ILogger<KernelAuditSealingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await sealer.SealAllAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogPassFailed(logger, ex);
            }

            await KernelOutboxRelayService.Delay(options.SealInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Kernel audit sealing pass failed; retrying after the seal interval.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
