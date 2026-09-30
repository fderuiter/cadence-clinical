using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Persistence.Audit;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Outbox;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Persistence.Background;

/// <summary>Schedules for the kernel's background jobs.</summary>
public sealed class KernelBackgroundOptions
{
    /// <summary>How long the relay waits after a pass in which no tenant filled its batch.</summary>
    public TimeSpan RelayInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Messages claimed per tenant in one relay pass, so a busy tenant cannot starve the others.</summary>
    public int RelayBatchSize { get; set; } = 100;

    /// <summary>How often every tenant's audit chain is verified and sealed.</summary>
    public TimeSpan SealInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often every tenant's lineage is checked for derived objects without inputs (K-LIN-1).</summary>
    public TimeSpan LineageCheckInterval { get; set; } = TimeSpan.FromHours(1);
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
                backlog = result.MoreDue;
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

/// <summary>Runs <see cref="KernelLineageOrphanCheck.CheckAllAsync"/> on <see cref="KernelBackgroundOptions.LineageCheckInterval"/>.</summary>
internal sealed partial class KernelLineageCheckService(
    KernelLineageOrphanCheck check,
    KernelBackgroundOptions options,
    ILogger<KernelLineageCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await check.CheckAllAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogPassFailed(logger, ex);
            }

            await KernelOutboxRelayService.Delay(options.LineageCheckInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Kernel lineage check pass failed; retrying after the check interval.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
