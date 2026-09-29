using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

using Cadence.Kernel.Persistence;
using Cadence.Kernel.Time;
using Cadence.Testing.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// PostgreSQL fixture for the kernel schema. Opt in with <c>CADENCE_TEST_POSTGRES</c> (an Npgsql connection string
/// to an existing server): the fixture creates a throwaway database, applies the kernel migrations through
/// <see cref="KernelPersistenceServiceCollectionExtensions.AddKernelPersistence"/>, and drops the database afterwards.
/// Without it, tests call <see cref="SkipIfUnavailable"/> and report as skipped rather than passing against a
/// stand-in. Once opted in, any startup failure is rethrown.
/// @req:PRD-SYS-053
/// </summary>
public class KernelPostgresFixture : IAsyncLifetime
{
    public const string Actor = "kernel-it@cadence.test";

    private const string SkipReason = "Kernel PostgreSQL tests need CADENCE_TEST_POSTGRES=<connection string>.";

    private string? _adminConnectionString;
    private string? _databaseName;
    private readonly ConcurrentBag<IServiceScope> _scopes = [];
    private ServiceProvider? _services;

    public string? ConnectionString { get; private set; }

    public FakeClock Clock { get; } = new();

    public void SkipIfUnavailable() => Assert.SkipWhen(ConnectionString is null, SkipReason);

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("CADENCE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(external))
        {
            return;
        }

        _adminConnectionString = external;
        _databaseName = $"cadence_kernel_it_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(external))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(external) { Database = _databaseName }.ConnectionString;

        _services = new ServiceCollection()
            .AddSingleton<IClock>(Clock)
            .AddKernelPersistence(ConnectionString, _ => Actor)
            .BuildServiceProvider();

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
        }

        await OnMigratedAsync(ConnectionString).ConfigureAwait(false);
    }

    /// <summary>Runs once after the kernel migrations are applied, for fixtures that add test-only objects.</summary>
    protected virtual ValueTask OnMigratedAsync(string connectionString) => ValueTask.CompletedTask;

    /// <summary>Creates a context wired exactly as the kernel host wires it (audit interceptor, kernel clock).</summary>
    internal KernelDbContext CreateContext()
    {
        var scope = (_services ?? throw new InvalidOperationException(SkipReason)).CreateScope();
        _scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<KernelDbContext>();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync().ConfigureAwait(false);
        }

        if (_databaseName is not null && _adminConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync().ConfigureAwait(false);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
