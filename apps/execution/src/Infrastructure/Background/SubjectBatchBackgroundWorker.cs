using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Execution.Infrastructure.Background;

public sealed class SubjectBatchBackgroundWorker(
    ISubjectBatchQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<SubjectBatchBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SubjectBatchBackgroundWorker starting.");

        await foreach (var batchId in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                logger.LogInformation("Processing batch validation for batch '{BatchId}'", batchId);
                using var scope = scopeFactory.CreateScope();
                var validationService = scope.ServiceProvider.GetRequiredService<ISubjectBatchValidationService>();
                await validationService.ValidateBatchAsync(batchId, stoppingToken).ConfigureAwait(false);
                logger.LogInformation("Completed batch validation for batch '{BatchId}'", batchId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while processing batch validation for batch '{BatchId}'", batchId);
            }
        }
    }
}
