using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Etmf.Workers;

public sealed partial class EtmfExportWorker : BackgroundService
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
        LogWorkerStarted(_logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string? jobId = null;

                if (!_queue.TryDequeue(out var queuedJobId))
                {
                    // Fallback polling for QUEUED jobs in DbContext
                    using var pollScope = _serviceProvider.CreateScope();
                    var db = pollScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
                    var queuedJob = await db.TmfExportJobs
                        .Where(j => j.Status == "QUEUED")
                        .OrderBy(j => j.CreatedAt)
                        .FirstOrDefaultAsync(stoppingToken);

                    if (queuedJob != null)
                    {
                        jobId = queuedJob.Id;
                    }
                }
                else
                {
                    jobId = queuedJobId;
                }

                if (string.IsNullOrEmpty(jobId))
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogWorkerLoopFailed(_logger, ex);
                await Task.Delay(2000, stoppingToken);
            }
        }
    }

    public async Task ProcessJobAsync(string jobId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var job = await db.TmfExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null)
        {
            LogJobNotFound(_logger, jobId);
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
                .ToListAsync(ct);

            job.TotalDocuments = documents.Count;
            job.ProgressPercentage = 15.0;
            await db.SaveChangesAsync(ct);

            var exportDir = Path.Combine(Path.GetTempPath(), "etmf_exports");
            Directory.CreateDirectory(exportDir);
            var filePath = Path.Combine(exportDir, $"{job.Id}.zip");

            var now = clock.UtcNow;

            // Stream / package zip in memory or file
            var zipBytes = EtmfExportPackager.CreateZipArchive(
                exportId: job.Id,
                studyId: job.StudyId,
                documents: documents,
                exportedAt: now,
                progressCallback: (processed, total) =>
                {
                    job.ProcessedDocuments = processed;
                    job.ProgressPercentage = 15.0 + Math.Min(80.0, (double)processed / Math.Max(1, total) * 80.0);
                }
            );

            await File.WriteAllBytesAsync(filePath, zipBytes, ct);

            job.Status = "COMPLETED";
            job.ProgressPercentage = 100.0;
            job.ProcessedDocuments = documents.Count;
            job.DownloadUrl = $"/api/v1/etmf/export-jobs/{job.Id}/download";
            job.ExpiresAt = now.AddHours(24);

            await db.SaveChangesAsync(ct);
            LogJobCompleted(_logger, job.Id, documents.Count);
        }
        catch (Exception ex)
        {
            LogJobFailed(_logger, ex, jobId);
            job.Status = "FAILED";
            await db.SaveChangesAsync(ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "eTMF Export Background Worker started.")]
    private static partial void LogWorkerStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error occurred in eTMF Export Background Worker loop.")]
    private static partial void LogWorkerLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Export job {JobId} not found.")]
    private static partial void LogJobNotFound(ILogger logger, string? jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Export job {JobId} completed successfully with {DocCount} documents.")]
    private static partial void LogJobCompleted(ILogger logger, string jobId, int docCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to execute export job {JobId}.")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, string? jobId);
}
