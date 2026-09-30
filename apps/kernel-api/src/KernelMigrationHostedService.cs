using System.Threading;
using System.Threading.Tasks;

using Cadence.Database;
using Cadence.Kernel.Persistence;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Host;

/// <summary>
/// Applies the kernel migrations before the host serves traffic when <c>Database:MigrateOnStartup</c> or
/// <c>Database:MigrateOnly</c> is set, like every other service (<see cref="CadenceMigrationOptions"/>). Only for stacks
/// whose connection string is the schema owner (local development, a deployment job): a host that connects as the
/// application role leaves both settings off and has its migrations applied separately (ADR-2250).
/// </summary>
internal sealed partial class KernelMigrationHostedService(
    string connectionString,
    CadenceMigrationOptions options,
    IHostApplicationLifetime lifetime,
    ILogger<KernelMigrationHostedService> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        LogApplying(logger);
        await KernelDatabase.MigrateAsync(connectionString, cancellationToken).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        if (options.MigrateOnly)
        {
            LogMigrateOnlyComplete(logger);
            lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Registers the service when the configuration asks for migrations; does nothing otherwise.</summary>
    public static void Register(IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        var options = configuration.GetSection(CadenceMigrationOptions.SectionName).Get<CadenceMigrationOptions>()
            ?? new CadenceMigrationOptions();
        if (options.MigrateOnStartup || options.MigrateOnly)
        {
            services.AddHostedService(sp => new KernelMigrationHostedService(
                connectionString,
                options,
                sp.GetRequiredService<IHostApplicationLifetime>(),
                sp.GetRequiredService<ILogger<KernelMigrationHostedService>>()));
        }
    }

    [LoggerMessage(EventId = 2540, Level = LogLevel.Information, Message = "Applying the kernel migrations")]
    private static partial void LogApplying(ILogger logger);

    [LoggerMessage(EventId = 2541, Level = LogLevel.Information, Message = "The kernel migrations are up to date; stopping because Database:MigrateOnly is set")]
    private static partial void LogMigrateOnlyComplete(ILogger logger);
}
