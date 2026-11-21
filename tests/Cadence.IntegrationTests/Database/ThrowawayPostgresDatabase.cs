using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Database.Tenancy;
using Cadence.Testing.Persistence;

using Npgsql;

using Testcontainers.PostgreSql;

using Xunit;

namespace Cadence.IntegrationTests.Database;

/// <summary>
/// An empty PostgreSQL database for one test class, dropped afterwards. It follows the same opt-ins as the
/// other database fixtures (see docs/TESTING.md):
/// <list type="number">
/// <item><c>CADENCE_TEST_POSTGRES</c>: a connection string to a server you run; a <c>cadence_it_*</c> database is
/// created on it.</item>
/// <item><c>CADENCE_TEST_DOCKER=1</c>: a Testcontainers container of <see cref="PostgresTestImage.Image"/>.</item>
/// </list>
/// With neither set, <see cref="ConnectionString"/> is null and tests call <see cref="SkipIfUnavailable"/>.
/// No migrations are applied; that is left to the tests.
/// <para>
/// <see cref="ConnectionString"/> connects as a throwaway application role that owns the database and is
/// <c>NOSUPERUSER NOBYPASSRLS</c>, so a service under test never runs with privileges it would not have in a
/// deployment. The server connection string is used only to create and drop the database and the role.
/// </para>
/// </summary>
public sealed class ThrowawayPostgresDatabase : IAsyncLifetime
{
    private const string SkipReason =
        "PostgreSQL integration tests need CADENCE_TEST_POSTGRES=<connection string> or CADENCE_TEST_DOCKER=1.";

    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;
    private string? _databaseName;
    private string? _roleName;

    public string? ConnectionString { get; private set; }

    public string? AdminDatabaseConnectionString { get; private set; }

    public void SkipIfUnavailable() => Assert.SkipWhen(ConnectionString is null, SkipReason);

    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("CADENCE_TEST_POSTGRES") is { Length: > 0 } server)
        {
            _adminConnectionString = server;
        }
        else if (Environment.GetEnvironmentVariable("CADENCE_TEST_DOCKER") == "1")
        {
            _container = new PostgreSqlBuilder().WithImage(PostgresTestImage.Image).Build();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await _container.StartAsync(cts.Token).ConfigureAwait(false);
            _adminConnectionString = _container.GetConnectionString();
        }
        else
        {
            return;
        }

        string suffix = Guid.NewGuid().ToString("N");
        string password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        _roleName = $"cadence_it_app_{suffix}";
        _databaseName = $"cadence_it_{suffix}";
        await ExecuteAsync(
            _adminConnectionString,
            $"CREATE ROLE \"{_roleName}\" LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{password}'").ConfigureAwait(false);
        await ExecuteAsync(_adminConnectionString, $"CREATE DATABASE \"{_databaseName}\" OWNER \"{_roleName}\"").ConfigureAwait(false);

        AdminDatabaseConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName,
        }.ConnectionString;

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName,
            Username = _roleName,
            Password = password,
        }.ConnectionString;

        await BootstrapStandaloneTenantDirectoryAsync(AdminDatabaseConnectionString, _roleName).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_databaseName is not null && _adminConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(_adminConnectionString, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)").ConfigureAwait(false);
            await ExecuteAsync(_adminConnectionString, $"DROP ROLE IF EXISTS \"{_roleName}\"").ConfigureAwait(false);
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

    public static async Task BootstrapStandaloneTenantDirectoryAsync(string adminDbConnectionString, string? ownerRole = null)
    {
        await using var conn = new NpgsqlConnection(adminDbConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        string ownerGrantSql = string.IsNullOrWhiteSpace(ownerRole)
            ? string.Empty
            : $"""
                GRANT USAGE ON SCHEMA "kernel" TO "{ownerRole}";
                GRANT SELECT, INSERT, UPDATE ON TABLE "kernel"."tenants" TO "{ownerRole}";
                GRANT EXECUTE ON FUNCTION "kernel".current_tenant_id() TO "{ownerRole}";
                GRANT EXECUTE ON FUNCTION "kernel".tenant_id_by_code(text) TO "{ownerRole}";
                GRANT EXECUTE ON FUNCTION "kernel".tenant_ids() TO "{ownerRole}";
                """;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                CREATE SCHEMA IF NOT EXISTS "kernel";
                CREATE TABLE IF NOT EXISTS "kernel"."tenants" (
                    id uuid PRIMARY KEY,
                    code text NOT NULL UNIQUE,
                    name text NOT NULL,
                    is_active boolean NOT NULL DEFAULT true
                );
                CREATE OR REPLACE FUNCTION "kernel".current_tenant_id() RETURNS uuid
                LANGUAGE sql STABLE PARALLEL SAFE AS $fn$
                    SELECT NULLIF(pg_catalog.current_setting('cadence.tenant_id', true), '')::uuid
                $fn$;
                CREATE OR REPLACE FUNCTION "kernel".tenant_id_by_code(tenant_code text) RETURNS uuid
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                AS $fn$
                    SELECT t.id FROM "kernel"."tenants" t WHERE t.code = tenant_code AND t.is_active
                $fn$;
                CREATE OR REPLACE FUNCTION "kernel".tenant_ids() RETURNS SETOF uuid
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp
                AS $fn$
                    SELECT t.id FROM "kernel"."tenants" t WHERE t.is_active ORDER BY t.id
                $fn$;
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cadence_app') THEN
                        CREATE ROLE cadence_app LOGIN PASSWORD 'cadence_app' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                    END IF;
                EXCEPTION
                    WHEN duplicate_object OR unique_violation THEN
                        NULL;
                END
                $$;
                GRANT USAGE ON SCHEMA "kernel" TO cadence_app;
                GRANT SELECT ON TABLE "kernel"."tenants" TO cadence_app;
                GRANT EXECUTE ON FUNCTION "kernel".current_tenant_id() TO cadence_app;
                GRANT EXECUTE ON FUNCTION "kernel".tenant_id_by_code(text) TO cadence_app;
                GRANT EXECUTE ON FUNCTION "kernel".tenant_ids() TO cadence_app;
                {ownerGrantSql}
                """;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (string code in new[] { "tenant-test", "tenant-alpha", "tenant-beta", "tenant-a", "tenant-b" })
        {
            Guid id = DeterministicTenantCodeResolver.ComputeDeterministicGuid(code);
            await using var ins = conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO "kernel"."tenants" (id, code, name, is_active)
                VALUES (@id, @code, @code, true)
                ON CONFLICT (code) DO NOTHING;
                """;
            ins.Parameters.AddWithValue("id", id);
            ins.Parameters.AddWithValue("code", code);
            await ins.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
