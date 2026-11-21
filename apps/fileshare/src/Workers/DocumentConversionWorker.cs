using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Fileshare.Workers;

public sealed class DocumentConversionWorker : FileshareWorkerBase
{
    public DocumentConversionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<FileshareWorkerOptions> options,
        ILogger<DocumentConversionWorker> logger)
        : base(scopeFactory, options, logger)
    {
    }

    protected override TimeSpan PollInterval => Options.ConversionPollInterval;

    protected override Task ProcessPendingAsync(CancellationToken stoppingToken)
        => ProcessPendingConversionsAsync(stoppingToken);

    public async Task ProcessPendingConversionsAsync(CancellationToken ct)
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
        var processor = scope.ServiceProvider.GetService<IDocumentConverterProcessor>()
                        ?? new DocumentConverterProcessor(scope.ServiceProvider.GetRequiredService<ILogger<DocumentConverterProcessor>>());

        var pendingRecords = await dbContext.FileRecords
            .Where(f => f.ConversionStatus == "PENDING" && !f.IsDeleted)
            .Take(Options.BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pendingRecords.Count > 0)
        {
            await Task.WhenAll(pendingRecords.Select(r => ExecuteThrottledAsync(
                () => ProcessSingleRecordAsync(dbContext, processor, r, ct), ct))).ConfigureAwait(false);
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

        while (attempt < Options.MaxRetryCount && !success && !ct.IsCancellationRequested)
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
                Logger.LogWarning(ex, "Conversion attempt {Attempt}/{MaxRetries} failed for FileRecord {RecordId}", attempt, Options.MaxRetryCount, record.Id);

                if (attempt < Options.MaxRetryCount)
                {
                    await DelayBackoffAsync(attempt, ct).ConfigureAwait(false);
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
}
