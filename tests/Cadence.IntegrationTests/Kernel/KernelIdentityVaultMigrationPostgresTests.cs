using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.IdentityVault;
using Cadence.TestSupport.Database;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The IdentityVault migration never destroys an identifier (ADR-2458): it drops the identifier columns of
/// <c>kernel.people</c> only when they are empty, and its <c>Down</c> and <c>Up</c> round-trip. Each test migrates its
/// own throwaway database. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelIdentityVaultMigrationPostgresTests
{
    private const string BeforeVault = "20260930094042_EffectivityAndStudySiteActivation";
    private const string Vault = "20260930110610_IdentityVault";

    /// <summary>
    /// Validates that the migration refuses to drop a person's e-mail or name: it fails with <c>KV003</c> and leaves the
    /// columns and the vault schema as they were, and it succeeds once the columns are empty.
    /// </summary>
    [Fact(DisplayName = "Migration_RefusesToDropPersonIdentifiers_AndRunsOnceTheyAreEmpty")]
    public async Task MigrationRefusesToDropPersonIdentifiersAndRunsOnceTheyAreEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await ThrowawayDatabase.CreateAsync(ct);
        if (database is null)
        {
            Assert.Skip(PostgresTestServer.SkipReason);
        }

        await MigrateAsync(database.ConnectionString, BeforeVault, ct);
        var person = Guid.CreateVersion7();
        await database.ExecuteAsync(
            $"""
            INSERT INTO kernel.people (id, email, first_name, last_name, is_active, created_at, created_by, reason_for_change, version_index)
            VALUES ('{person}', 'someone@site.test', '', '', true, now(), 'it', 'seed', 1)
            """,
            ct);

        var migrate = () => MigrateAsync(database.ConnectionString, Vault, ct);

        (await migrate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(IdentityVaultSql.PeopleHoldIdentifiersSqlState);
        (await database.ScalarAsync("SELECT count(*) FROM kernel.people WHERE email = 'someone@site.test'", ct)).Should().Be(1L);
        (await database.ScalarAsync("SELECT count(*) FROM pg_namespace WHERE nspname = 'identity_vault'", ct)).Should().Be(0L);

        await database.ExecuteAsync($"UPDATE kernel.people SET email = '' WHERE id = '{person}'", ct);
        await migrate();

        (await database.ScalarAsync("SELECT count(*) FROM kernel.people", ct)).Should().Be(1L);
        (await database.ScalarAsync("SELECT count(*) FROM pg_namespace WHERE nspname = 'identity_vault'", ct)).Should().Be(1L);
    }

    /// <summary>
    /// Validates that the migration reverts and re-applies with people present: <c>Down</c> writes placeholders the
    /// guard accepts, so a rollback never blocks the next deployment.
    /// </summary>
    [Fact(DisplayName = "Migration_DownAndUpAgain_WithPeoplePresent")]
    public async Task MigrationDownAndUpAgainWithPeoplePresent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await ThrowawayDatabase.CreateAsync(ct);
        if (database is null)
        {
            Assert.Skip(PostgresTestServer.SkipReason);
        }

        await MigrateAsync(database.ConnectionString, Vault, ct);
        await database.ExecuteAsync(
            $"""
            INSERT INTO kernel.people (id, is_active, created_at, created_by, reason_for_change, version_index)
            VALUES ('{Guid.CreateVersion7()}', true, now(), 'it', 'seed', 1), ('{Guid.CreateVersion7()}', true, now(), 'it', 'seed', 1)
            """,
            ct);

        await MigrateAsync(database.ConnectionString, BeforeVault, ct);
        (await database.ScalarAsync("SELECT count(DISTINCT email) FROM kernel.people", ct)).Should().Be(2L);

        await MigrateAsync(database.ConnectionString, Vault, ct);
        (await database.ScalarAsync("SELECT count(*) FROM kernel.people", ct)).Should().Be(2L);
    }

    private static async Task MigrateAsync(string connectionString, string target, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>();
        KernelDatabase.UseKernelNpgsql(options, connectionString);
        await using var db = new KernelDbContext(options.Options);
        await db.GetService<IMigrator>().MigrateAsync(target, ct);
    }

    private sealed class ThrowawayDatabase : IAsyncDisposable
    {
        private readonly PostgresTestServer _host;
        private readonly string _server;
        private readonly string _name;

        private ThrowawayDatabase(PostgresTestServer host, string name)
        {
            _host = host;
            _server = host.SuperuserConnectionString;
            _name = name;
            ConnectionString = new NpgsqlConnectionStringBuilder(_server) { Database = name }.ConnectionString;
        }

        public string ConnectionString { get; }

        public static async Task<ThrowawayDatabase?> CreateAsync(CancellationToken ct)
        {
            var host = await PostgresTestServer.StartAsync();
            if (host is null)
            {
                return null;
            }

            var database = new ThrowawayDatabase(host, $"cadence_vault_migration_{Guid.NewGuid():N}");
            await ExecuteOnAsync(database._server, $"CREATE DATABASE \"{database._name}\"", ct);
            return database;
        }

        public Task ExecuteAsync(string sql, CancellationToken ct) => ExecuteOnAsync(ConnectionString, sql, ct);

        public async Task<object?> ScalarAsync(string sql, CancellationToken ct)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            using (var conn = new NpgsqlConnection(ConnectionString))
            {
                NpgsqlConnection.ClearPool(conn);
            }

            await ExecuteOnAsync(_server, $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", CancellationToken.None);
            await _host.DisposeAsync();
        }

        private static async Task ExecuteOnAsync(string connectionString, string sql, CancellationToken ct)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
