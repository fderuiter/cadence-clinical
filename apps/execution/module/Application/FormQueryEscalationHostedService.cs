using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Background;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Modules.Execution.Application;

/// <summary>Configuration for <see cref="FormQueryEscalationHostedService"/>.</summary>
public sealed class FormQueryEscalationOptions
{
    /// <summary>Whether the background daily scheduler loop is enabled (defaults to <see langword="true"/>).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Delay after host startup before the first scheduled sweep runs (defaults to 1 minute).</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Interval between scheduled 14-day SLA sweeps (defaults to 24 hours).</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromDays(1);
}

/// <summary>Summary of a single leader-locked query SLA sweep across active tenants and studies.</summary>
public sealed record FormQueryEscalationSweepSummary(
    bool AcquiredLeaderLock,
    int TenantsVisited,
    int StudiesEvaluated,
    int EscalatedQueries,
    int DigestsGenerated);

/// <summary>
/// Leader-locked daily scheduler that evaluates 14-day eCRF query SLA thresholds on <see cref="IClock"/> across active,
/// entitled tenants and their studies under the dedicated service account <c>svc:execution-query-sla</c> (<c>PRD-QRY-002</c>,
/// <c>ADR-2601</c>).
/// </summary>
public sealed partial class FormQueryEscalationHostedService(
    IKernelLeaderLock leaderLock,
    ITenantDirectory tenantDirectory,
    IStudyDirectory studyDirectory,
    IServiceScopeFactory scopes,
    IClock clock,
    FormQueryEscalationOptions options,
    ILogger<FormQueryEscalationHostedService> logger,
    ITenantEntitlementReader? entitlements = null) : BackgroundService
{
    /// <summary>PostgreSQL advisory leader lock name for the daily query SLA sweep.</summary>
    public const string JobName = "execution.query-sla-sweep";

    /// <summary>Dedicated service account name (<c>svc:execution-query-sla</c>) used for scheduled SLA passes.</summary>
    public const string ServiceAccountName = "execution-query-sla";

    /// <summary>
    /// Runs one leader-locked 14-day SLA sweep across all active, non-expired, Capture-entitled tenants and their studies.
    /// If another replica currently holds <see cref="JobName"/>'s lock, returns immediately with
    /// <c>AcquiredLeaderLock = false</c>.
    /// </summary>
    public async Task<FormQueryEscalationSweepSummary> RunSweepOnceAsync(CancellationToken cancellationToken = default)
    {
        var tenantsVisited = 0;
        var studiesEvaluated = 0;
        var escalatedQueries = 0;
        var digestsGenerated = 0;

        var acquired = await leaderLock.TryRunAsync(
            JobName,
            async ct =>
            {
                var now = clock.UtcNow;
                var sweepDate = DateOnly.FromDateTime(now.UtcDateTime);
                var activeTenants = await tenantDirectory.ListActiveAsync(ct).ConfigureAwait(false);

                foreach (var tenant in activeTenants)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (entitlements is not null)
                        {
                            var snapshot = await entitlements.GetEntitlementsAsync(tenant, now, ct).ConfigureAwait(false);
                            if (snapshot.IsReadOnly || !snapshot.ProductFamilies.Contains(ProductFamily.Capture))
                            {
                                continue;
                            }
                        }

                        tenantsVisited++;
                        var studies = await studyDirectory.ListStudiesAsync(tenant, ct).ConfigureAwait(false);
                        foreach (var study in studies)
                        {
                            ct.ThrowIfCancellationRequested();
                            await using var scope = scopes.CreateAsyncScope();
                            scope.ServiceProvider.GetRequiredService<ModuleServiceActor>().ActAsServiceAccount(ServiceAccountName);
                            var escalation = scope.ServiceProvider.GetRequiredService<IFormQueryEscalationService>();
                            var reason = $"Scheduled 14-day query SLA sweep for {study.StudyCode} on {sweepDate:yyyy-MM-dd}";
                            var result = await escalation
                                .RunStudyPassAsync(tenant, study.StudyId, reason, ct)
                                .ConfigureAwait(false);
                            studiesEvaluated++;
                            if (result.Value is { } pass)
                            {
                                escalatedQueries += pass.EscalatedQueries;
                                digestsGenerated += pass.Digests.Count;
                            }
                            else if (result.Error is { } error)
                            {
                                LogStudyPassRefused(logger, tenant.Value, study.StudyId.Value, error.Code, error.Message);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        LogTenantSweepFailed(logger, ex, tenant.Value);
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);

        return new FormQueryEscalationSweepSummary(
            acquired,
            tenantsVisited,
            studiesEvaluated,
            escalatedQueries,
            digestsGenerated);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (options.InitialDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(options.InitialDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        var interval = options.SweepInterval <= TimeSpan.Zero ? TimeSpan.FromDays(1) : options.SweepInterval;
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSweepOnceAsync(stoppingToken).ConfigureAwait(false);
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSweepLoopFailed(logger, ex);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Scheduled 14-day query SLA pass for study {StudyId} in tenant {TenantId} was refused with {ErrorCode}: {Detail}")]
    private static partial void LogStudyPassRefused(ILogger logger, Guid tenantId, Guid studyId, string errorCode, string detail);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Scheduled 14-day query SLA sweep failed for tenant {TenantId}.")]
    private static partial void LogTenantSweepFailed(ILogger logger, Exception exception, Guid tenantId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Scheduled 14-day query SLA scheduler loop encountered an unexpected error.")]
    private static partial void LogSweepLoopFailed(ILogger logger, Exception exception);
}
