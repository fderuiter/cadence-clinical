using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Execution.Application.Exports;

public sealed class ExportJobBackgroundWorker : BackgroundService
{
    private readonly IExportJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExportJobBackgroundWorker> _logger;

    public ExportJobBackgroundWorker(
        IExportJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ExportJobBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExportJobBackgroundWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("Processing background export job {JobId} for study {StudyId}", job.JobId, job.StudyId);

                using var scope = _scopeFactory.CreateScope();
                var exportJobService = scope.ServiceProvider.GetRequiredService<IExportJobService>();
                var etlService = scope.ServiceProvider.GetRequiredService<IDatasetJsonEtlService>();

                try
                {
                    await exportJobService.UpdateJobProgressAsync(job.JobId, 15, "Extracting clinical observations from PostgreSQL execution ledger...", stoppingToken).ConfigureAwait(false);
                    await Task.Delay(100, stoppingToken).ConfigureAwait(false);

                    await exportJobService.UpdateJobProgressAsync(job.JobId, 45, "Applying deterministic HMAC-SHA256 pseudonymization and population filters...", stoppingToken).ConfigureAwait(false);
                    await Task.Delay(100, stoppingToken).ConfigureAwait(false);

                    await exportJobService.UpdateJobProgressAsync(job.JobId, 75, $"Serializing datasets to {job.Format} format...", stoppingToken).ConfigureAwait(false);
                    await Task.Delay(100, stoppingToken).ConfigureAwait(false);

                    var artifact = await etlService.SerializeAsync(job, stoppingToken).ConfigureAwait(false);

                    await exportJobService.StoreArtifactAndCompleteJobAsync(job.JobId, artifact, stoppingToken).ConfigureAwait(false);
                    _logger.LogInformation("Completed background export job {JobId}", job.JobId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed background export job {JobId}", job.JobId);
                    await exportJobService.FailJobAsync(job.JobId, ex.Message, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ExportJobBackgroundWorker execution loop.");
            }
        }
    }
}
