using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Testcontainers.PostgreSql;

using Xunit;

namespace Cadence.E2ETests.E2E;

/// <summary>
/// WebApplicationFactory fixture for Cadence.E2ETests targeting .NET 10 REST API endpoints.
/// Manages Testcontainers.PostgreSql container lifecycle with resilient InMemory fallback when Docker is offline.
/// @req:PRD-SYS-001, PRD-SUB-001
/// </summary>
public class ExecutionE2EFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private bool _isDockerAvailable;
    private readonly string _inMemoryDbName = $"e2e-db-{Guid.NewGuid():N}";

    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async ValueTask InitializeAsync()
    {
        if (CheckDockerSocketAvailable())
        {
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

                _postgresContainer = new PostgreSqlBuilder()
                    .WithImage("postgres:16-alpine")
                    .Build();

                await _postgresContainer.StartAsync().ConfigureAwait(false);
                _isDockerAvailable = true;
            }
            catch
            {
                _isDockerAvailable = false;
                if (_postgresContainer != null)
                {
                    await _postgresContainer.DisposeAsync().ConfigureAwait(false);
                    _postgresContainer = null;
                }
            }
        }
    }

    public new async ValueTask DisposeAsync()
    {
        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync().ConfigureAwait(false);
            _postgresContainer = null;
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            if (_isDockerAvailable && _postgresContainer != null)
            {
                services.RemoveAll<DbContextOptions<ExecutionDbContext>>();
                services.AddDbContext<ExecutionDbContext>((sp, options) =>
                {
                    options.UseNpgsql(_postgresContainer.GetConnectionString());
                    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                    options.AddInterceptors(
                        new ConcurrencyTokenIncrementInterceptor(),
                        new GxPAuditInterceptor(() => sp.GetRequiredService<ICurrentAuditContextAccessor>().UserId));
                });
            }
            else
            {
                services.RemoveAll<DbContextOptions<ExecutionDbContext>>();
                services.AddDbContext<ExecutionDbContext>((sp, options) =>
                {
                    options.UseInMemoryDatabase(_inMemoryDbName);
                    options.AddInterceptors(
                        new ConcurrencyTokenIncrementInterceptor(),
                        new GxPAuditInterceptor(() => sp.GetRequiredService<ICurrentAuditContextAccessor>().UserId));
                });

                services.AddLogging(logging =>
                {
                    logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
                    logging.AddFilter("MassTransit", LogLevel.None);
                });
            }
        });

        base.ConfigureWebHost(builder);
    }

    protected override Microsoft.Extensions.Hosting.IHost CreateHost(Microsoft.Extensions.Hosting.IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        if (_isDockerAvailable && _postgresContainer != null)
        {
            db.Database.Migrate();
        }
        else
        {
            db.Database.EnsureCreated();
        }
        return host;
    }

    private static bool CheckDockerSocketAvailable()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrEmpty(dockerHost))
        {
            return true;
        }

        if (File.Exists("/var/run/docker.sock"))
        {
            return true;
        }

        var userSocket = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker/run/docker.sock");
        return File.Exists(userSocket);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        ExecutionJsonSerialization.Configure(options);
        return options;
    }
}
