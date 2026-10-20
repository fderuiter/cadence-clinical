using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Testing.Hosting;
using Cadence.Testing.Persistence;
using Cadence.Testing.Security;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Npgsql;

using Testcontainers.PostgreSql;

using Xunit;

namespace Cadence.E2ETests.E2E;

/// <summary>
/// WebApplicationFactory fixture for Cadence.E2ETests targeting .NET 10 REST API endpoints.
/// The database follows the same opt-ins as <c>ExecutionPostgresFixture</c> in the unit test project:
/// <list type="number">
/// <item><c>CADENCE_TEST_POSTGRES</c>: an Npgsql connection string to an existing server; the factory
/// creates a throwaway database on it and drops it afterwards (works without Docker).</item>
/// <item><c>CADENCE_TEST_DOCKER=1</c>: a Testcontainers container of <see cref="PostgresTestImage.Image"/>.</item>
/// </list>
/// With neither set the host runs on the EF Core in-memory provider, which is enough for routing and
/// read paths. Tests that write through enrollment (which needs real transactions and advisory locks)
/// call <see cref="SkipUnlessPostgres"/> so they report as skipped instead of failing or passing falsely.
/// </summary>
public class ExecutionE2EFactory : WebApplicationFactory<Cadence.Apps.Execution.Program>, IAsyncLifetime
{
    private const string SkipReason =
        "This E2E test writes through PostgreSQL; set CADENCE_TEST_POSTGRES=<connection string> or CADENCE_TEST_DOCKER=1.";

    private PostgreSqlContainer? _postgresContainer;
    private string? _adminConnectionString;
    private string? _databaseName;
    private string? _connectionString;
    private readonly string _inMemoryDbName = $"e2e-db-{Guid.NewGuid():N}";

    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>True when the host runs against a real PostgreSQL database.</summary>
    public bool UsesPostgres => _connectionString is not null;

    /// <summary>Skips the calling test unless the host runs against PostgreSQL.</summary>
    public void SkipUnlessPostgres() => Assert.SkipWhen(!UsesPostgres, SkipReason);

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("CADENCE_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = external;
            _databaseName = $"cadence_e2e_{Guid.NewGuid():N}";
            await using var admin = new NpgsqlConnection(external);
            await admin.OpenAsync().ConfigureAwait(false);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
            _connectionString = new NpgsqlConnectionStringBuilder(external) { Database = _databaseName }.ConnectionString;
        }
        else if (Environment.GetEnvironmentVariable("CADENCE_TEST_DOCKER") == "1")
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
                .WithImage(PostgresTestImage.Image)
                .Build();

            await _postgresContainer.StartAsync().ConfigureAwait(false);
            _connectionString = _postgresContainer.GetConnectionString();
        }
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);

        if (_databaseName is not null && _adminConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync().ConfigureAwait(false);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync().ConfigureAwait(false);
            _postgresContainer = null;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret().UseTestDemographicsKey().UseTestRandomizationKey();
        builder.UseTestPostgresConnectionString();

        builder.ConfigureServices(services =>
        {
            if (_connectionString is not null)
            {
                services.RemoveAll<DbContextOptions<ExecutionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ExecutionDbContext>>();
                services.AddDbContext<ExecutionDbContext>((sp, options) =>
                {
                    options.UseNpgsql(_connectionString);
                    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                    options.AddInterceptors(
                        new ConcurrencyTokenIncrementInterceptor(),
                        new GxPAuditInterceptor(() => sp.GetRequiredService<ICurrentAuditContextAccessor>().UserId, sp.GetRequiredService<IClock>()));
                });
            }
            else
            {
                var inMemoryServices = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.RemoveAll<DbContextOptions<ExecutionDbContext>>();

                services.RemoveAll<IDbContextOptionsConfiguration<ExecutionDbContext>>();
                services.AddDbContext<ExecutionDbContext>((sp, options) =>
                {
                    options.UseInMemoryDatabase(_inMemoryDbName);
                    options.UseInternalServiceProvider(inMemoryServices);
                    options.AddInterceptors(
                        new ConcurrencyTokenIncrementInterceptor(),
                        new GxPAuditInterceptor(() => sp.GetRequiredService<ICurrentAuditContextAccessor>().UserId, sp.GetRequiredService<IClock>()));
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
        if (UsesPostgres)
        {
            db.Database.Migrate();
        }
        else
        {
            db.Database.EnsureCreated();
        }
        return host;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        ExecutionJsonSerialization.Configure(options);
        return options;
    }
}
