using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Tenancy;

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
        using var discoveryScope = ScopeFactory.CreateScope();
        var resolver = discoveryScope.ServiceProvider.GetService<ITenantCodeResolver>();
        var tenantScope = discoveryScope.ServiceProvider.GetService<ITenantScope>();
        if (resolver is not null && tenantScope is not null)
        {
            var tenantIds = await resolver.ListActiveTenantIdsAsync(ct).ConfigureAwait(false);
            if (tenantIds.Count > 0)
            {
                foreach (Guid tenantId in tenantIds)
                {
                    using (tenantScope.Bind(tenantId))
                    {
                        await ProcessPendingForCurrentContextAsync(ct).ConfigureAwait(false);
                    }
                }

                return;
            }
        }

        await ProcessPendingForCurrentContextAsync(ct).ConfigureAwait(false);
    }

    private async Task ProcessPendingForCurrentContextAsync(CancellationToken ct)
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

    private const string MediaTranscoderServiceAccount = "svc:fileshare-media-transcoder";

    private async Task ProcessSingleRecordAsync(
        FileshareDbContext dbContext,
        IMediaTranscoderProcessor processor,
        FileRecord record,
        CancellationToken ct)
    {
        using var _ = StandaloneAuditActorScope.Begin(MediaTranscoderServiceAccount);
        record.TranscodeStatus = "PROCESSING";
        record.TranscodeError = null;
        record.ReasonForChange = "Automated HLS media transcoding started";
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
            record.ReasonForChange = "Automated HLS media transcoding completed";
        }
        else
        {
            record.TranscodeStatus = "FAILED";
            record.TranscodeError = lastError ?? "Transcode failed after maximum retries.";
            record.ReasonForChange = "Automated HLS media transcoding failed";
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
