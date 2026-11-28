using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Database.Audit;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Fileshare.Workers;

public abstract class FileshareWorkerBase : BackgroundService
{
    private readonly SemaphoreSlim _throttle;

    protected IServiceScopeFactory ScopeFactory { get; }
    protected FileshareWorkerOptions Options { get; }
    protected ILogger Logger { get; }

    protected FileshareWorkerBase(
        IServiceScopeFactory scopeFactory,
        IOptions<FileshareWorkerOptions> options,
        ILogger logger)
    {
        ScopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Options = options?.Value ?? new FileshareWorkerOptions();
        _throttle = new SemaphoreSlim(Options.MaxConcurrentJobs, Options.MaxConcurrentJobs);
    }

    protected abstract TimeSpan PollInterval { get; }

    protected abstract Task ProcessPendingAsync(CancellationToken stoppingToken);

    protected async Task ProcessForDiscoveredTenantsAsync(
        string postgresDirectorySql,
        Expression<Func<FileRecord, bool>> pendingPredicate,
        Func<CancellationToken, Task> processBoundContextAsync,
        CancellationToken ct)
    {
        await using var discoveryScope = ScopeFactory.CreateAsyncScope();
        var discoveryDb = discoveryScope.ServiceProvider.GetRequiredService<FileshareDbContext>();
        var tenantScope = discoveryScope.ServiceProvider.GetService<ITenantScope>();

        bool isNpgsql = discoveryDb.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;
        if (isNpgsql && tenantScope is not null)
        {
            List<Guid> pendingTenantIds;
            using (tenantScope.Suppress())
            {
                pendingTenantIds = await discoveryDb.Database
                    .SqlQueryRaw<Guid>(postgresDirectorySql)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
            }

            foreach (Guid tenantId in pendingTenantIds)
            {
                using (tenantScope.Bind(tenantId))
                {
                    await processBoundContextAsync(ct).ConfigureAwait(false);
                }
            }

            return;
        }

        if (!discoveryDb.Database.IsRelational() && tenantScope is not null)
        {
            var pendingTenantIds = await discoveryDb.FileRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(pendingPredicate)
                .Select(f => EF.Property<Guid>(f, StandaloneTenantModelExtensions.TenantPropertyName))
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (pendingTenantIds.Count == 0)
            {
                return;
            }

            foreach (Guid tenantId in pendingTenantIds)
            {
                if (tenantId == Guid.Empty)
                {
                    await processBoundContextAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    using (tenantScope.Bind(tenantId))
                    {
                        await processBoundContextAsync(ct).ConfigureAwait(false);
                    }
                }
            }

            return;
        }

        await processBoundContextAsync(ct).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string workerName = GetType().Name;
        Logger.LogInformation("{WorkerName} started.", workerName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var _ = AmbientAuditActorScope.BeginScope(
                    "svc:fileshare-worker",
                    $"Automated {workerName} background processing");
                await ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                Logger.LogError(ex, "{WorkerName} pass failed; retrying after poll interval.", workerName);
            }

            await Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }

        Logger.LogInformation("{WorkerName} stopping.", workerName);
    }

    protected async Task ExecuteThrottledAsync(Func<Task> action, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    protected async Task ExecuteThrottledAsync<T>(T item, Func<T, Task> action, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action(item).ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    public TimeSpan CalculateBackoffDelay(int attempt)
    {
        if (attempt <= 1)
        {
            return Options.InitialBackoff;
        }

        // Clamp attempt exponent to prevent arithmetic overflow
        int exponent = Math.Min(attempt - 1, 30);
        double factor = Math.Pow(2, exponent);
        double ms = Options.InitialBackoff.TotalMilliseconds * factor;

        double maxDelayMs = int.MaxValue;
        if (double.IsNaN(ms) || double.IsInfinity(ms) || ms > maxDelayMs)
        {
            return TimeSpan.FromMilliseconds(maxDelayMs);
        }

        if (ms < 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(ms);
    }

    protected async Task DelayBackoffAsync(int attempt, CancellationToken ct)
    {
        var backoffDelay = CalculateBackoffDelay(attempt);
        await Delay(backoffDelay, ct).ConfigureAwait(false);
    }

    public static async Task Delay(TimeSpan interval, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (interval < TimeSpan.Zero)
        {
            interval = TimeSpan.Zero;
        }

        var maxDelay = TimeSpan.FromMilliseconds(int.MaxValue);
        if (interval > maxDelay)
        {
            interval = maxDelay;
        }

        try
        {
            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown
        }
    }

    public override void Dispose()
    {
        _throttle.Dispose();
        base.Dispose();
    }
}
