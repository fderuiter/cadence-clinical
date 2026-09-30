using System;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

using Testcontainers.PostgreSql;

using Xunit;

namespace Cadence.UnitTests.Integration;

/// <summary>
/// An empty PostgreSQL database for one test class, dropped afterwards. It follows the same opt-ins as the
/// other database fixtures (see docs/TESTING.md):
/// <list type="number">
/// <item><c>CADENCE_TEST_POSTGRES</c>: a connection string to a server you run; a <c>cadence_it_*</c> database is
/// created on it.</item>
/// <item><c>CADENCE_TEST_DOCKER=1</c>: a Testcontainers <c>postgres:16-alpine</c> container.</item>
/// </list>
/// With neither set, <see cref="ConnectionString"/> is null and tests call <see cref="SkipIfUnavailable"/>.
/// No migrations are applied; that is left to the tests.
/// </summary>
public sealed class ThrowawayPostgresDatabase : IAsyncLifetime
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
        if (Environment.GetEnvironmentVariable("CADENCE_TEST_POSTGRES") is { Length: > 0 } server)
        {
            _adminConnectionString = server;
            _databaseName = $"cadence_it_{Guid.NewGuid():N}";
            await ExecuteAsync(server, $"CREATE DATABASE \"{_databaseName}\"").ConfigureAwait(false);
            ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = _databaseName }.ConnectionString;
        }
        else if (Environment.GetEnvironmentVariable("CADENCE_TEST_DOCKER") == "1")
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await _container.StartAsync(cts.Token).ConfigureAwait(false);
            ConnectionString = _container.GetConnectionString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_databaseName is not null && _adminConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(_adminConnectionString, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)").ConfigureAwait(false);
        }

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
