using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Fileshare.Workers;

public sealed class MediaTranscoderWorker : FileshareWorkerBase
{
    public MediaTranscoderWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<FileshareWorkerOptions> options,
        ILogger<MediaTranscoderWorker> logger)
        : base(scopeFactory, options, logger)
    {
    }

    protected override TimeSpan PollInterval => Options.TranscodePollInterval;

    protected override Task ProcessPendingAsync(CancellationToken stoppingToken)
        => ProcessPendingTranscodesAsync(stoppingToken);

    public async Task ProcessPendingTranscodesAsync(CancellationToken ct)
    {
        using var scope = ScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FileshareDbContext>();
        var processor = scope.ServiceProvider.GetService<IMediaTranscoderProcessor>()
                        ?? new MediaTranscoderProcessor(scope.ServiceProvider.GetRequiredService<ILogger<MediaTranscoderProcessor>>());

        var pendingRecords = await dbContext.FileRecords
            .Where(f => f.TranscodeStatus == "PENDING" && !f.IsDeleted)
            .Take(Options.BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pendingRecords.Count == 0)
        {
            return;
        }

        await Task.WhenAll(pendingRecords.Select(record => ExecuteThrottledAsync(
            () => ProcessSingleRecordAsync(dbContext, processor, record, ct), ct))).ConfigureAwait(false);
    }

    private async Task ProcessSingleRecordAsync(
        FileshareDbContext dbContext,
        IMediaTranscoderProcessor processor,
        FileRecord record,
        CancellationToken ct)
    {
        record.TranscodeStatus = "PROCESSING";
        record.TranscodeError = null;
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        int attempt = 0;
        bool success = false;
        string? manifestKey = null;
        string? lastError = null;

        while (attempt < Options.MaxRetryCount && !success && !ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                manifestKey = await processor.TranscodeToHlsAsync(record, ct).ConfigureAwait(false);
                success = true;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                Logger.LogWarning(ex, "Transcode attempt {Attempt}/{MaxRetries} failed for FileRecord {RecordId}", attempt, Options.MaxRetryCount, record.Id);

                if (attempt < Options.MaxRetryCount)
                {
                    await DelayBackoffAsync(attempt, ct).ConfigureAwait(false);
                }
            }
        }

        if (success && manifestKey is not null)
        {
            record.TranscodeStatus = "COMPLETED";
            record.HlsManifestKey = manifestKey;
            record.TranscodeError = null;
        }
        else
        {
            record.TranscodeStatus = "FAILED";
            record.TranscodeError = lastError ?? "Transcode failed after maximum retries.";
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
