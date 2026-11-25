using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Cli.Services;
using Cadence.TestSupport.Database;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Database;

public sealed class DatabaseResetAndMigratorPostgresTests(ThrowawayPostgresDatabase database)
    : IClassFixture<ThrowawayPostgresDatabase>
{
    private static readonly string[] ExpectedSchemas =
    [
        "kernel",
        "identity_vault",
        "execution",
        "intelligence",
        "ctms",
        "econsent",
        "eisf",
        "etmf",
        "edc",
        "fileshare",
        "imaging",
        "interop",
        "knowledge",
        "notifications",
        "org",
        "payments",
        "quality",
        "safety",
        "tickets",
    ];

    [Fact]
    public async Task ResetAndMigrate_DropsStaleSchemas_LeavesPublicEmpty_AndProvisionsAll19Schemas()
    {
        database.SkipIfUnavailable();
        CancellationToken ct = TestContext.Current.CancellationToken;
        string connectionString = database.AdminDatabaseConnectionString!;

        DatabaseResetService.ValidateLocalOnly("Postgres", connectionString);
        await SeedStaleArtifactsAsync(connectionString, ct);

        await DatabaseResetService.ResetPostgresAsync(connectionString, ct);
        (await NonSystemSchemasAsync(connectionString, ct)).Should().Equal("public");
        (await TablesInSchemaAsync(connectionString, "public", ct)).Should().BeEmpty();

        int firstExitCode = DatabaseResetService.RunDatabaseMigrator(connectionString);
        firstExitCode.Should().Be(0, "initial migration across all 18 DbContext assemblies must succeed");

        IReadOnlyList<string> schemas = await NonSystemSchemasAsync(connectionString, ct);
        schemas.Should().NotContain("stale_legacy_schema");
        schemas.Should().BeEquivalentTo([.. ExpectedSchemas, "public"]);

        (await TablesInSchemaAsync(connectionString, "public", ct)).Should().BeEmpty(
            "public must stay empty so unqualified queries cannot hit retired tables (ADR-2447, ADR-2622)");

        foreach (string schema in ExpectedSchemas)
        {
            (await TablesInSchemaAsync(connectionString, schema, ct)).Should().NotBeEmpty(
                $"schema '{schema}' should contain migrated tables");
        }

        int secondExitCode = DatabaseResetService.RunDatabaseMigrator(connectionString);
        secondExitCode.Should().Be(0, "incremental db migrate on an up-to-date database must be idempotent");
        (await TablesInSchemaAsync(connectionString, "public", ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplySingleModule_CreatesTargetSearchPathSchema_AndKeepsPublicEmpty()
    {
        database.SkipIfUnavailable();
        CancellationToken ct = TestContext.Current.CancellationToken;
        string connectionString = database.AdminDatabaseConnectionString!;

        DatabaseResetService.ValidateLocalOnly("Postgres", connectionString);
        await DatabaseResetService.ResetPostgresAsync(connectionString, ct);
        (await NonSystemSchemasAsync(connectionString, ct)).Should().Equal("public");
        (await TablesInSchemaAsync(connectionString, "public", ct)).Should().BeEmpty();

        int exitCode = MigrationToolingService.Apply("ctms", connectionString);
        exitCode.Should().Be(0, "single-module apply for ctms must create schema ctms and apply its migrations");

        (await NonSystemSchemasAsync(connectionString, ct)).Should().BeEquivalentTo("ctms", "kernel", "public");
        (await TablesInSchemaAsync(connectionString, "ctms", ct)).Should().NotBeEmpty(
            "ctms migrations must populate tables inside schema 'ctms'");
        (await TablesInSchemaAsync(connectionString, "kernel", ct)).Should().BeEmpty(
            "standalone RLS bootstrap creates only kernel.current_tenant_id(), never kernel tables");
        (await TablesInSchemaAsync(connectionString, "public", ct)).Should().BeEmpty(
            "public must remain completely empty when applying a standalone SearchPath module");
    }

    private static async Task SeedStaleArtifactsAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE SCHEMA IF NOT EXISTS stale_legacy_schema;
            CREATE TABLE IF NOT EXISTS stale_legacy_schema.leftover_records (id uuid PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS public.form_submissions (id uuid PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS public.clinical_observations (id uuid PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS public.clinical_queries (id uuid PRIMARY KEY);
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<string>> NonSystemSchemasAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT nspname
            FROM pg_catalog.pg_namespace
            WHERE nspname NOT LIKE 'pg_%'
              AND nspname <> 'information_schema'
            ORDER BY nspname;
            """,
            connection);

        var schemas = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
    }

    private static async Task<IReadOnlyList<string>> TablesInSchemaAsync(
        string connectionString,
        string schema,
        CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema AND table_type = 'BASE TABLE'",
            connection);
        command.Parameters.AddWithValue("schema", schema);

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
