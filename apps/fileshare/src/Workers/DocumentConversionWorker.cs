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

public sealed partial class DocumentConversionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FileshareWorkerOptions _options;
    private readonly ILogger<DocumentConversionWorker> _logger;
    private readonly SemaphoreSlim _throttle;

    public DocumentConversionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<FileshareWorkerOptions> options,
        ILogger<DocumentConversionWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new FileshareWorkerOptions();
        _throttle = new SemaphoreSlim(_options.MaxConcurrentJobs, _options.MaxConcurrentJobs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingConversionsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogPassFailed(_logger, ex);
            }

            await Delay(_options.ConversionPollInterval, stoppingToken).ConfigureAwait(false);
        }

        LogStopping(_logger);
    }

    public async Task ProcessPendingConversionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FileshareDbContext>();
        var processor = scope.ServiceProvider.GetService<IDocumentConverterProcessor>()
                        ?? new DocumentConverterProcessor(scope.ServiceProvider.GetRequiredService<ILogger<DocumentConverterProcessor>>());

        var pendingRecords = await dbContext.FileRecords
            .Where(f => f.ConversionStatus == "PENDING" && !f.IsDeleted)
            .Take(_options.BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pendingRecords.Count > 0)
        {
            await Task.WhenAll(pendingRecords.Select(r => ProcessThrottledRecordAsync(r, rec => ProcessSingleRecordAsync(dbContext, processor, rec, ct), ct))).ConfigureAwait(false);
        }
    }

    private async Task ProcessThrottledRecordAsync(FileRecord record, Func<FileRecord, Task> action, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action(record).ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    private async Task ProcessSingleRecordAsync(
        FileshareDbContext dbContext,
        IDocumentConverterProcessor processor,
        FileRecord record,
        CancellationToken ct)
    {
        record.ConversionStatus = "PROCESSING";
        record.ConversionError = null;
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        int attempt = 0;
        bool success = false;
        string? pdfKey = null;
        string? lastError = null;

        while (attempt < _options.MaxRetryCount && !success && !ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                pdfKey = await processor.ConvertToPdfAsync(record, ct).ConfigureAwait(false);
                success = true;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                LogConversionAttemptFailed(_logger, ex, attempt, _options.MaxRetryCount, record.Id);

                if (attempt < _options.MaxRetryCount)
                {
                    var backoffDelay = TimeSpan.FromMilliseconds(_options.InitialBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    await Delay(backoffDelay, ct).ConfigureAwait(false);
                }
            }
        }

        if (success && pdfKey is not null)
        {
            record.ConversionStatus = "COMPLETED";
            record.PdfRenditionKey = pdfKey;
            record.ConversionError = null;
        }
        else
        {
            record.ConversionStatus = "FAILED";
            record.ConversionError = lastError ?? "Document conversion failed after maximum retries.";
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

    [LoggerMessage(Level = LogLevel.Error, Message = "DocumentConversionWorker pass failed; retrying after poll interval.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "DocumentConversionWorker started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "DocumentConversionWorker stopping.")]
    private static partial void LogStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversion attempt {Attempt}/{MaxRetries} failed for FileRecord {RecordId}")]
    private static partial void LogConversionAttemptFailed(ILogger logger, Exception exception, int attempt, int maxRetries, string recordId);
}
