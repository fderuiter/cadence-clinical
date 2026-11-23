using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.IntegrationTests.Database;
using Cadence.Kernel.Time;
using Cadence.Testing.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Testcontainers.PostgreSql;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// PostgreSQL fixture for execution-subjects integration and concurrency testing.
/// Two opt-in sources, checked in order:
/// <list type="number">
/// <item><c>CADENCE_TEST_POSTGRES</c>: an Npgsql connection string to an existing server; the fixture
/// creates a throwaway database on it and drops it afterwards (works without Docker).</item>
/// <item><c>CADENCE_TEST_DOCKER=1</c>: a Testcontainers container of <see cref="PostgresTestImage.Image"/>.</item>
/// </list>
/// With neither set, <see cref="ConnectionString"/> is null and tests call <see cref="SkipIfUnavailable"/>
/// so they report as skipped rather than passing against a stand-in. Once opted in, any startup
/// failure is rethrown so a broken database never masquerades as a green run.
/// </summary>
public sealed class ExecutionPostgresFixture : IAsyncLifetime
{
    private const string SkipReason =
        "PostgreSQL integration tests need CADENCE_TEST_POSTGRES=<connection string> or CADENCE_TEST_DOCKER=1.";

    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;
    private string? _databaseName;

    public string? ConnectionString { get; private set; }

    public void SkipIfUnavailable() => Assert.SkipWhen(ConnectionString is null, SkipReason);

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("CADENCE_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            await CreateThrowawayDatabaseAsync(external).ConfigureAwait(false);
        }
        else if (Environment.GetEnvironmentVariable("CADENCE_TEST_DOCKER") == "1")
        {
            await StartContainerAsync().ConfigureAwait(false);
        }
        else
        {
            return;
        }

        await ApplyMigrationsAsync(ConnectionString!).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_databaseName is not null && _adminConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync().ConfigureAwait(false);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        if (_container != null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
            _container = null;
        }
    }

    private async Task CreateThrowawayDatabaseAsync(string adminConnectionString)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = $"cadence_it_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = _databaseName }.ConnectionString;
    }

    private async Task StartContainerAsync()
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

        _container = new PostgreSqlBuilder(PostgresTestImage.Image)
            .Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _container.StartAsync(cts.Token).ConfigureAwait(false);
        ConnectionString = _container.GetConnectionString();
    }

    private static readonly CurrentTenantAccessor DefaultFixtureTenantAccessor = CreateFixtureTenantAccessor();

    private static CurrentTenantAccessor CreateFixtureTenantAccessor()
    {
        var accessor = new CurrentTenantAccessor(new DeterministicTenantCodeResolver());
        accessor.UseNonHttpSeedingFallbackTenant("tenant-test");
        return accessor;
    }

    public static ExecutionDbContext CreateDbContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ExecutionDbContext>();
        optionsBuilder.UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, ExecutionDbContextFactory.Schema));
        optionsBuilder.UseCadenceStandaloneTenancy(DefaultFixtureTenantAccessor, isRelational: true);
        optionsBuilder.AddInterceptors(
            StandaloneTenantConnectionInterceptor.Instance,
            StandaloneTenantSaveChangesInterceptor.Instance,
            new GxPAuditInterceptor(() => "svc:integration-test", SystemClock.Instance));
        return new ExecutionDbContext(optionsBuilder.Options);
    }

    public static async Task ApplyMigrationsAsync(string connectionString)
    {
        await ThrowawayPostgresDatabase.BootstrapStandaloneTenantDirectoryAsync(connectionString).ConfigureAwait(false);
        using var _ = DefaultFixtureTenantAccessor.Suppress();
        await using var context = CreateDbContext(connectionString);
        await context.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{ExecutionDbContextFactory.Schema}\"").ConfigureAwait(false);
        await context.Database.MigrateAsync().ConfigureAwait(false);
    }
}
