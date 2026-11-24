using System;
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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryDequeue(out var queuedJobId, out var queuedTenantCode))
                {
                    var tenantScope = _serviceProvider.GetService<ITenantScope>();
                    if (tenantScope is not null && !string.IsNullOrWhiteSpace(queuedTenantCode))
                    {
                        using (tenantScope.Bind(queuedTenantCode))
                        {
                            await ProcessJobAsync(queuedJobId, stoppingToken);
                        }
                    }
                    else
                    {
                        await ProcessJobAsync(queuedJobId, stoppingToken);
                    }

                    continue;
                }

                bool foundAny = await PollNextQueuedJobAsync(stoppingToken);
                if (!foundAny)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in eTMF Export Background Worker loop.");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }

    private async Task<bool> PollNextQueuedJobAsync(CancellationToken stoppingToken)
    {
        var resolver = _serviceProvider.GetService<ITenantCodeResolver>();
        var tenantScope = _serviceProvider.GetService<ITenantScope>();
        if (resolver is not null && tenantScope is not null)
        {
            var tenantIds = await resolver.ListActiveTenantIdsAsync(stoppingToken);
            if (tenantIds.Count > 0)
            {
                foreach (Guid tenantId in tenantIds)
                {
                    using (tenantScope.Bind(tenantId))
                    {
                        using var tenantPollScope = _serviceProvider.CreateScope();
                        var tenantDb = tenantPollScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
                        var tenantJob = await tenantDb.TmfExportJobs
                            .Where(j => j.Status == "QUEUED")
                            .OrderBy(j => j.CreatedAt)
                            .FirstOrDefaultAsync(stoppingToken);
                        if (tenantJob is not null)
                        {
                            await ProcessJobAsync(tenantJob.Id, stoppingToken);
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        using var pollScope = _serviceProvider.CreateScope();
        var db = pollScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
        var queuedJob = await db.TmfExportJobs
            .Where(j => j.Status == "QUEUED")
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(stoppingToken);

        if (queuedJob is null)
        {
            return false;
        }

        await ProcessJobAsync(queuedJob.Id, stoppingToken);
        return true;
    }

    public async Task ProcessJobAsync(string jobId, CancellationToken ct)
    {
        using var _ = AmbientAuditActorScope.BeginScope(
            "svc:etmf-export-worker",
            $"Automated eTMF archive export job {jobId}");
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var storage = scope.ServiceProvider.GetService<IStoragePort<byte[]>>();

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

            if (storage is not null)
            {
                var exportKey = EtmfExportPackager.BuildExportObjectKey(job.StudyId, job.Id);
                await storage.PutObjectAsync(exportKey, zipBytes, "application/zip", ct: ct);
            }
            else
            {
                var exportDir = Path.Combine(Path.GetTempPath(), "etmf_exports");
                Directory.CreateDirectory(exportDir);
                var filePath = Path.Combine(exportDir, $"{job.Id}.zip");
                await File.WriteAllBytesAsync(filePath, zipBytes, ct);
            }

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
