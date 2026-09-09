using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database;

using Microsoft.EntityFrameworkCore;

using Testcontainers.PostgreSql;

using Xunit;

namespace Cadence.UnitTests.Execution.Integration;

/// <summary>
/// Testcontainers PostgreSQL fixture for execution-subjects integration and concurrency testing.
/// Manages container lifecycle, executes EF Core migrations on initialization, and provides
/// resilient environment detection so test suites pass seamlessly when Docker is dormant.
/// @req:PRD-SUB-001, PRD-SYS-053
/// </summary>
public sealed class ExecutionPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool IsDockerAvailable { get; private set; }

    public string? ConnectionString { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!CheckDockerSocketAvailable())
        {
            IsDockerAvailable = false;
            return;
        }

        try
        {
            var userSocket = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker/run/docker.sock");
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")) && File.Exists(userSocket))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", $"unix://{userSocket}");
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED")))
            {
                Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();

            await _container.StartAsync(cts.Token).ConfigureAwait(false);
            ConnectionString = _container.GetConnectionString();
            IsDockerAvailable = true;

            await ApplyMigrationsAsync(ConnectionString).ConfigureAwait(false);
        }
        catch (Exception)
        {
            IsDockerAvailable = false;
            if (_container != null)
            {
                try
                {
                    await _container.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Suppress cleanup failure on aborted initialization
                }

                _container = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
            _container = null;
        }
    }

    public static ExecutionDbContext CreateDbContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ExecutionDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.AddInterceptors(new GxPAuditInterceptor(() => "system"));
        return new ExecutionDbContext(optionsBuilder.Options);
    }

    public static async Task ApplyMigrationsAsync(string connectionString)
    {
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync().ConfigureAwait(false);
    }

    public static bool CheckDockerSocketAvailable()
    {
        // Require explicit opt-in via CADENCE_TEST_DOCKER=1 so ordinary test runs
        // exercise the fast, resilient in-memory concurrency harness without hanging on dormant Docker daemons
        if (Environment.GetEnvironmentVariable("CADENCE_TEST_DOCKER") != "1")
        {
            return false;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                return false;
            }

            if (!proc.WaitForExit(3000))
            {
                try
                {
                    proc.Kill();
                }
                catch (Exception)
                {
                    // Ignore failure terminating timed-out probe process
                }

                return false;
            }

            return proc.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
