using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Database;
using Cadence.Testing.Security;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Boots the real execution <c>Program</c> against a throwaway PostgreSQL database, the way it runs when deployed
/// (#4990). Only configuration is supplied: the connection string and <c>Database:MigrateOnStartup</c>, so
/// <c>Program.cs</c>'s own Npgsql registration, module schema (<c>execution</c>), interceptors and migration
/// hosted service all run unchanged. Nothing replaces the <c>ExecutionDbContext</c> provider.
/// <para>
/// The database follows the opt-ins of <see cref="ThrowawayPostgresDatabase"/> (<c>CADENCE_TEST_POSTGRES</c> or
/// <c>CADENCE_TEST_DOCKER=1</c>). Without either, every test calls <see cref="SkipIfUnavailable"/> first and
/// reports as skipped. One instance is shared by a test class (<c>IClassFixture</c>), so rows written by one test
/// are visible to the next; tests use run-unique codes rather than relying on an empty database.
/// </para>
/// </summary>
public sealed class ExecutionPostgresApiFactory : WebApplicationFactory<Cadence.Apps.Execution.Program>, IAsyncLifetime
{
    private readonly ThrowawayPostgresDatabase _database = new();
    private readonly ConcurrentQueue<string> _serverWarnings = new();

    /// <summary>Warnings and errors the host logged, for assertion messages when a request fails unexpectedly.</summary>
    public IReadOnlyCollection<string> ServerWarnings => _serverWarnings;

    /// <summary>Skips the calling test unless a PostgreSQL database is available.</summary>
    public void SkipIfUnavailable() => _database.SkipIfUnavailable();

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _database.DisposeAsync().ConfigureAwait(false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret().UseTestDemographicsKey();

        if (_database.ConnectionString is { } connectionString)
        {
            // ConnectionStrings:cadence wins over POSTGRES_CONNECTION_STRING, so a value exported in the shell
            // cannot point the test host at another database.
            builder.UseSetting("ConnectionStrings:cadence", connectionString);
            builder.UseSetting("Database:MigrateOnStartup", "true");
        }

        builder.ConfigureServices(services => services.AddLogging(logging =>
            logging.AddProvider(new WarningCaptureProvider(_serverWarnings))));

        base.ConfigureWebHost(builder);
    }

    private sealed class WarningCaptureProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new WarningCapture(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class WarningCapture(string categoryName, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    sink.Enqueue($"[{logLevel}] {categoryName}: {formatter(state, exception)} {exception}");
                }
            }
        }
    }
}
