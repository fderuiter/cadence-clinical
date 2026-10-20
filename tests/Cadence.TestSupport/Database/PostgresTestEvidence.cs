using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

using Cadence.Testing.Persistence;

using Npgsql;

namespace Cadence.TestSupport.Database;

/// <summary>
/// Checks and records the database a PostgreSQL fixture ran against (#5324). <see cref="VerifyAndRecordAsync"/>
/// refuses an application role that is a superuser or bypasses Row-Level Security, because a pass under such a role
/// says nothing about tenant isolation, and a server of another major than <see cref="PostgresTestImage.Major"/>,
/// because its pass qualifies a version the stack does not run (#5438). When <c>CADENCE_TEST_EVIDENCE_DIR</c> is set (<c>gxp sync</c> and
/// <c>cadence ci</c> set it to <c>TestResults/gxp</c>), it also writes one <c>*.database.txt</c> file there, which
/// the RTM and IQ/OQ/PQ report print as their database environment.
/// </summary>
public static class PostgresTestEvidence
{
    /// <summary>The variable naming the directory evidence files are written to.</summary>
    public const string DirectoryVariable = "CADENCE_TEST_EVIDENCE_DIR";

    private static readonly ConcurrentDictionary<string, bool> Recorded = new(StringComparer.Ordinal);

    /// <summary>
    /// Verifies that <paramref name="appRole"/> cannot bypass Row-Level Security and records
    /// the server version, the extensions installed in the migrated database, its latest migration and the role's
    /// attributes, once per fixture name. <paramref name="adminConnectionString"/> is a superuser connection to the migrated database.
    /// </summary>
    public static async Task VerifyAndRecordAsync(
        string fixture, PostgresTestServerKind source, string adminConnectionString, string migrationsHistoryTable, string appRole)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        bool superuser;
        bool bypassRls;
        await using (var role = new NpgsqlCommand("SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = @role", connection))
        {
            role.Parameters.AddWithValue("role", appRole);
            await using var reader = await role.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await reader.ReadAsync().ConfigureAwait(false))
            {
                throw new InvalidOperationException($"The application role '{appRole}' does not exist.");
            }

            superuser = reader.GetBoolean(0);
            bypassRls = reader.GetBoolean(1);
        }

        if (superuser || bypassRls)
        {
            throw new InvalidOperationException(
                $"The application role '{appRole}' is a superuser or has BYPASSRLS, so tests run as it would not exercise Row-Level Security.");
        }

        string versionNumber = await ScalarAsync(connection, "SHOW server_version_num").ConfigureAwait(false);
        if (PostgresTestImage.Mismatch(int.Parse(versionNumber, System.Globalization.CultureInfo.InvariantCulture)) is { } mismatch)
        {
            throw new InvalidOperationException(mismatch);
        }

        string? directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory) || !Recorded.TryAdd(fixture, true))
        {
            return;
        }

        string version = await ScalarAsync(connection, "SHOW server_version").ConfigureAwait(false);
        string extensions = await ScalarAsync(connection,
            "SELECT coalesce(string_agg(extname || ' ' || extversion, ', ' ORDER BY extname), 'none') FROM pg_extension").ConfigureAwait(false);
        string migration = await ScalarAsync(connection,
            $"SELECT coalesce(max(\"MigrationId\"), 'none') FROM {migrationsHistoryTable}").ConfigureAwait(false);

        Directory.CreateDirectory(directory);
        await File.WriteAllLinesAsync(Path.Combine(directory, $"{fixture}.database.txt"),
        [
            $"fixture={fixture}",
            $"source={source switch { PostgresTestServerKind.Docker => $"Testcontainers {PostgresTestServer.DockerImage} (CADENCE_TEST_DOCKER=1)", _ => "CADENCE_TEST_POSTGRES" }}",
            $"server_version={version}",
            $"extensions={extensions}",
            $"latest_migration={migration}",
            "app_role=NOSUPERUSER NOBYPASSRLS",
        ]).ConfigureAwait(false);
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync().ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
}
