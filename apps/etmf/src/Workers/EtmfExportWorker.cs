using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Database.Audit;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Etmf.Workers;

public sealed class EtmfExportWorker : BackgroundService
{
    private const string QueuedExportTenantsSql = "SELECT \"etmf\".\"queued_export_tenant_ids\"() AS \"Value\"";

    private readonly IServiceProvider _serviceProvider;
    private readonly EtmfExportQueue _queue;
    private readonly ILogger<EtmfExportWorker> _logger;

    public EtmfExportWorker(
        IServiceProvider serviceProvider,
        EtmfExportQueue queue,
        ILogger<EtmfExportWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("eTMF Export Background Worker started.");

        bool startupRecoveryCompleted = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (_queue.TryDequeue(out var readyJobId, out var readyTenantCode))
                {
                    await DispatchQueuedItemAsync(readyJobId, readyTenantCode, stoppingToken).ConfigureAwait(false);
                }

                if (!startupRecoveryCompleted)
                {
                    await DrainPendingQueuedJobsAsync(stoppingToken).ConfigureAwait(false);
                    startupRecoveryCompleted = true;
                }

                var (queuedJobId, queuedTenantCode) = await _queue.DequeueWithTenantAsync(stoppingToken).ConfigureAwait(false);
                await DispatchQueuedItemAsync(queuedJobId, queuedTenantCode, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in eTMF Export Background Worker loop.");
                await Task.Delay(2000, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Discovers tenants with <c>QUEUED</c> export jobs via <c>etmf.queued_export_tenant_ids()</c> (opening tenant
    /// scopes only for tenants with pending work) and drains their queued exports.
    /// </summary>
    public async Task<int> DrainPendingQueuedJobsAsync(CancellationToken stoppingToken = default)
    {
        int processed = 0;

        await using var discoveryScope = _serviceProvider.CreateAsyncScope();
        var discoveryDb = discoveryScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
        var tenantScope = discoveryScope.ServiceProvider.GetService<ITenantScope>();
        bool isNpgsql = discoveryDb.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;

        if (isNpgsql)
        {
            List<Guid> pendingTenantIds;
            using (tenantScope?.Suppress())
            {
                pendingTenantIds = await discoveryDb.Database
                    .SqlQueryRaw<Guid>(QueuedExportTenantsSql)
                    .ToListAsync(stoppingToken)
                    .ConfigureAwait(false);
            }

            foreach (Guid tenantId in pendingTenantIds)
            {
                using (tenantScope?.Bind(tenantId))
                {
                    processed += await DrainCurrentTenantQueuedJobsAsync(stoppingToken).ConfigureAwait(false);
                }
            }

            return processed;
        }

        if (!discoveryDb.Database.IsRelational())
        {
            var inMemoryPending = await discoveryDb.TmfExportJobs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(j => j.Status == "QUEUED")
                .OrderBy(j => j.CreatedAt)
                .Select(j => new
                {
                    j.Id,
                    TenantId = EF.Property<Guid>(j, StandaloneTenantModelExtensions.TenantPropertyName),
                })
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);

            foreach (var item in inMemoryPending)
            {
                if (tenantScope is not null && item.TenantId != Guid.Empty)
                {
                    using (tenantScope.Bind(item.TenantId))
                    {
                        await ProcessJobAsync(item.Id, stoppingToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await ProcessJobAsync(item.Id, stoppingToken).ConfigureAwait(false);
                }

                processed++;
            }

            return processed;
        }

        return await DrainCurrentTenantQueuedJobsAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task DispatchQueuedItemAsync(string jobId, string? tenantCode, CancellationToken stoppingToken)
    {
        await using var dispatchScope = _serviceProvider.CreateAsyncScope();
        var tenantScope = dispatchScope.ServiceProvider.GetService<ITenantScope>();
        if (tenantScope is not null && !string.IsNullOrWhiteSpace(tenantCode))
        {
            using (tenantScope.Bind(tenantCode))
            {
                await ProcessJobAsync(jobId, stoppingToken).ConfigureAwait(false);
            }

            return;
        }

        if (tenantScope is not null)
        {
            var discoveryDb = dispatchScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            bool isNpgsql = discoveryDb.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;
            if (isNpgsql)
            {
                List<Guid> pendingTenantIds;
                using (tenantScope.Suppress())
                {
                    pendingTenantIds = await discoveryDb.Database
                        .SqlQueryRaw<Guid>(QueuedExportTenantsSql)
                        .ToListAsync(stoppingToken)
                        .ConfigureAwait(false);
                }

                foreach (Guid tenantId in pendingTenantIds)
                {
                    using (tenantScope.Bind(tenantId))
                    {
                        await using var checkScope = _serviceProvider.CreateAsyncScope();
                        var checkDb = checkScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
                        bool existsInTenant = await checkDb.TmfExportJobs
                            .AsNoTracking()
                            .AnyAsync(j => j.Id == jobId, stoppingToken)
                            .ConfigureAwait(false);
                        if (existsInTenant)
                        {
                            await ProcessJobAsync(jobId, stoppingToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }

                return;
            }
        }

        await ProcessJobAsync(jobId, stoppingToken).ConfigureAwait(false);
    }

    private async Task<int> DrainCurrentTenantQueuedJobsAsync(CancellationToken stoppingToken)
    {
        List<string> jobIds;
        await using (var pollScope = _serviceProvider.CreateAsyncScope())
        {
            var db = pollScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            jobIds = await db.TmfExportJobs
                .AsNoTracking()
                .Where(j => j.Status == "QUEUED")
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.Id)
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);
        }

        foreach (string id in jobIds)
        {
            await ProcessJobAsync(id, stoppingToken).ConfigureAwait(false);
        }

        return jobIds.Count;
    }

    public async Task ProcessJobAsync(string jobId, CancellationToken ct)
    {
        using var _ = AmbientAuditActorScope.BeginScope(
            "svc:etmf-export-worker",
            $"Automated eTMF archive export job {jobId}");
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var storage = scope.ServiceProvider.GetRequiredService<IStoragePort<byte[]>>();

        var job = await db.TmfExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null)
        {
            _logger.LogWarning("Export job {JobId} not found.", jobId);
            return;
        }

        try
        {
            job.Status = "PROCESSING";
            job.ProgressPercentage = 5.0;
            await db.SaveChangesAsync(ct);

            // Fetch documents for the target study in batches
            var documents = await db.TmfDocuments
                .AsNoTracking()
                .Where(d => d.StudyId == job.StudyId)
                .OrderBy(d => d.Zone)
                .ThenBy(d => d.Section)
                .ThenBy(d => d.Filename)
                .ThenBy(d => d.Id)
                .ToListAsync(ct);

            job.TotalDocuments = documents.Count;
            job.ProgressPercentage = 15.0;
            await db.SaveChangesAsync(ct);

            var now = clock.UtcNow;

            var zipBytes = await EtmfExportPackager.CreateZipArchiveAsync(
                exportId: job.Id,
                studyId: job.StudyId,
                documents: documents,
                storage: storage,
                exportedAt: now,
                progressCallback: (processed, total) =>
                {
                    job.ProcessedDocuments = processed;
                    job.ProgressPercentage = 15.0 + Math.Min(80.0, (double)processed / Math.Max(1, total) * 80.0);
                },
                ct: ct
            );

            var exportKey = EtmfExportPackager.BuildExportObjectKey(job.StudyId, job.Id);
            await storage.PutObjectAsync(exportKey, zipBytes, "application/zip", ct: ct);

            job.Status = "COMPLETED";
            job.ProgressPercentage = 100.0;
            job.ProcessedDocuments = documents.Count;
            job.DownloadUrl = $"/api/v1/etmf/export-jobs/{job.Id}/download";
            job.ExpiresAt = now.AddHours(24);

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Export job {JobId} completed successfully with {DocCount} documents.", job.Id, documents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute export job {JobId}.", jobId);
            job.Status = "FAILED";
            await db.SaveChangesAsync(ct);
        }
    }
}
