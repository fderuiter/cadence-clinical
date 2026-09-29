using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Cadence.Database;

/// <summary>
/// How a service applies its EF Core migrations, bound from the <c>Database</c> configuration section.
/// </summary>
public sealed class CadenceMigrationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations before the service starts serving (single-instance and local stacks).</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>Apply pending migrations, then stop the process with exit code 0 (a deployment job or init container).</summary>
    public bool MigrateOnly { get; set; }
}

/// <summary>
/// Applies <typeparamref name="TContext"/>'s pending migrations in <see cref="IHostedLifecycleService.StartingAsync"/>,
/// which runs before the web server or any other hosted service starts, so a service never takes traffic on an
/// out-of-date schema. A failed migration fails startup.
/// </summary>
public sealed partial class CadenceMigrationHostedService<TContext>(
    IServiceScopeFactory scopeFactory,
    CadenceMigrationOptions options,
    IHostApplicationLifetime lifetime,
    ILogger<CadenceMigrationHostedService<TContext>> logger) : IHostedLifecycleService
    where TContext : DbContext
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Applying EF Core migrations for {Context}")]
    private static partial void LogApplying(ILogger logger, string context);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "EF Core migrations for {Context} are up to date; stopping because Database:MigrateOnly is set")]
    private static partial void LogMigrateOnlyComplete(ILogger logger, string context);

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        LogApplying(logger, typeof(TContext).Name);

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        // The module schema (first entry of the search path, see WithModuleSchema) must exist before EF creates
        // the migrations history table in it. The name was validated when the connection string was built.
        if (new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).SearchPath?.Split(',')[0].Trim() is { Length: > 0 } schema)
        {
#pragma warning disable EF1002 // Identifier validated by CadenceDatabaseExtensions.WithModuleSchema; DDL cannot take parameters.
            await context.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", cancellationToken);
#pragma warning restore EF1002
        }

        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Stopping from StartingAsync would cancel the web server mid-start and crash the process, so a
    // migrate-only run lets startup finish and then shuts down cleanly with exit code 0.
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        if (options.MigrateOnly)
        {
            LogMigrateOnlyComplete(logger, typeof(TContext).Name);
            lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class CadenceMigrationExtensions
{
    /// <summary>
    /// Registers <see cref="CadenceMigrationHostedService{TContext}"/> when <c>Database:MigrateOnStartup</c> or
    /// <c>Database:MigrateOnly</c> is true (environment: <c>Database__MigrateOnStartup=true</c>). Does nothing otherwise.
    /// </summary>
    public static IServiceCollection AddCadenceMigrations<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(CadenceMigrationOptions.SectionName).Get<CadenceMigrationOptions>()
            ?? new CadenceMigrationOptions();

        if (options.MigrateOnStartup || options.MigrateOnly)
        {
            services.TryAddSingleton(options);
            services.AddHostedService<CadenceMigrationHostedService<TContext>>();
        }

        return services;
    }
}
