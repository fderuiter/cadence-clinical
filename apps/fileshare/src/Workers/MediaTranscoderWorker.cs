using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Fileshare.Workers;

public sealed partial class MediaTranscoderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FileshareWorkerOptions _options;
    private readonly ILogger<MediaTranscoderWorker> _logger;
    private readonly SemaphoreSlim _throttle;

    public MediaTranscoderWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<FileshareWorkerOptions> options,
        ILogger<MediaTranscoderWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new FileshareWorkerOptions();
        _throttle = new SemaphoreSlim(_options.MaxConcurrentJobs, _options.MaxConcurrentJobs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MediaTranscoderWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingTranscodesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogPassFailed(_logger, ex);
            }

            await Delay(_options.TranscodePollInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("MediaTranscoderWorker stopping.");
    }

    public async Task ProcessPendingTranscodesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FileshareDbContext>();
        var processor = scope.ServiceProvider.GetService<IMediaTranscoderProcessor>()
                        ?? new MediaTranscoderProcessor(scope.ServiceProvider.GetRequiredService<ILogger<MediaTranscoderProcessor>>());

        var pendingRecords = await dbContext.FileRecords
            .Where(f => f.TranscodeStatus == "PENDING" && !f.IsDeleted)
            .Take(_options.BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pendingRecords.Count == 0)
        {
            return;
        }

        var tasks = pendingRecords.Select(async record =>
        {
            await _throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await ProcessSingleRecordAsync(dbContext, processor, record, ct).ConfigureAwait(false);
            }
            finally
            {
                _throttle.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
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

        while (attempt < _options.MaxRetryCount && !success && !ct.IsCancellationRequested)
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
                _logger.LogWarning(ex, "Transcode attempt {Attempt}/{MaxRetries} failed for FileRecord {RecordId}", attempt, _options.MaxRetryCount, record.Id);

                if (attempt < _options.MaxRetryCount)
                {
                    var backoffDelay = TimeSpan.FromMilliseconds(_options.InitialBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    await Delay(backoffDelay, ct).ConfigureAwait(false);
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

    internal static async Task Delay(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "MediaTranscoderWorker pass failed; retrying after poll interval.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
