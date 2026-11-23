using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Database;
using Cadence.Database.Temporal;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.TestSupport.Database;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Database;

/// <summary>
/// Applies every service's migrations the way a deployed service does (<c>AddCadencePostgresDbContext</c> plus
/// <see cref="CadenceMigrationHostedService{TContext}"/>) into one shared database, and checks that each service's
/// tables and migration history land in its own schema (ADR-2247). Needs <c>CADENCE_TEST_POSTGRES</c> or
/// <c>CADENCE_TEST_DOCKER=1</c>; without either the tests report as skipped.
/// </summary>
public sealed class ServiceSchemaPostgresTests(ThrowawayPostgresDatabase database) : IClassFixture<ThrowawayPostgresDatabase>
{
    public static IEnumerable<TheoryDataRow<string>> Factories() => ServiceDbContextFactories.All.Select(t => new TheoryDataRow<string>(t.AssemblyQualifiedName!));

    [Theory]
    [MemberData(nameof(Factories))]
    public async Task ServiceMigrations_ApplyIntoTheSharedDatabase_InsideTheServicesOwnSchema(string factoryTypeName)
    {
        database.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        Type factoryType = Type.GetType(factoryTypeName, throwOnError: true)!;
        string schema = (string)factoryType.GetField("Schema")!.GetRawConstantValue()!;
        Type contextType = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!.ReturnType;

        var migrate = typeof(ServiceSchemaPostgresTests)
            .GetMethod(nameof(MigrateLikeADeployedServiceAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(contextType);
        var expectedTables = await (Task<IReadOnlyList<string>>)migrate.Invoke(null, [database.ConnectionString!, schema, ct])!;

        expectedTables.Should().NotBeEmpty();
        var tablesInSchema = await TablesInSchemaAsync(schema, ct);
        tablesInSchema.Should().Contain("__EFMigrationsHistory", "the migration history belongs to the module's schema");
        tablesInSchema.Should().Contain(expectedTables, $"every table in {contextType.Name}'s model is created in \"{schema}\"");

        (await TablesInSchemaAsync("public", ct)).Should().NotContain(expectedTables,
            "a module's tables must never land in the shared public schema");
    }

    private static async Task<IReadOnlyList<string>> MigrateLikeADeployedServiceAsync<TContext>(
        string connectionString, string schema, CancellationToken ct)
        where TContext : DbContext
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration[$"ConnectionStrings:{CadenceDatabaseExtensions.DefaultConnectionName}"] = connectionString;
        builder.Configuration["Database:MigrateOnStartup"] = "true";
        // Payments and eConsent contexts take the field encryptor (ADR-2494).
        builder.Services.AddSingleton(TestFieldProtection.CreateEncryptor());
        builder.AddCadencePostgresDbContext<TContext>(schema);

        await using var app = builder.Build();
        var migrator = app.Services.GetServices<IHostedService>().OfType<CadenceMigrationHostedService<TContext>>().Single();
        await migrator.StartingAsync(ct);

        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        (await context.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();

        return [.. context.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null && e.GetViewName() is null && e.GetSchema() is null)
            .Select(e => e.GetTableName()!)
            .Distinct()];
    }

    private async Task<IReadOnlyList<string>> TablesInSchemaAsync(string schema, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
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

    [Fact]
    [Trait("Category", "GxP_IQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task AllStandaloneSchemas_InstallTemporalAndInsertOnlyTriggers()
    {
        database.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        foreach (Type factoryType in ServiceDbContextFactories.All)
        {
            string schema = (string)factoryType.GetField("Schema")!.GetRawConstantValue()!;
            Type contextType = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!.ReturnType;

            var migrate = typeof(ServiceSchemaPostgresTests)
                .GetMethod(nameof(MigrateLikeADeployedServiceAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(contextType);
            await (Task)migrate.Invoke(null, [database.ConnectionString!, schema, ct])!;
        }

        await using var verifyConn = new NpgsqlConnection(database.ConnectionString);
        await verifyConn.OpenAsync(ct);

        string[] expectedBitemporalTables =
        [
            "ctms.ctms_delegation_periods",
            "ctms.doa_delegation_periods",
            "edc.site_activation_periods",
            "org.delegation_of_authority_periods",
            "knowledge.delegation_record_periods",
        ];

        foreach (string qualified in expectedBitemporalTables)
        {
            string[] parts = qualified.Split('.');
            string expectedRowTrigger = StandaloneTemporalSql.RowTriggerName(parts[1]);

            await using var trigCmd = verifyConn.CreateCommand();
            trigCmd.CommandText = """
                SELECT COUNT(*)
                FROM pg_trigger t
                JOIN pg_class c ON c.oid = t.tgrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema AND c.relname = @table AND t.tgname = @trigger;
                """;
            trigCmd.Parameters.AddWithValue("schema", parts[0]);
            trigCmd.Parameters.AddWithValue("table", parts[1]);
            trigCmd.Parameters.AddWithValue("trigger", expectedRowTrigger);
            long trigCount = (long)(await trigCmd.ExecuteScalarAsync(ct))!;
            trigCount.Should().Be(1L, $"{qualified} must install bitemporal trigger {expectedRowTrigger}");

            await using var exclCmd = verifyConn.CreateCommand();
            exclCmd.CommandText = """
                SELECT COUNT(*)
                FROM pg_constraint con
                JOIN pg_class c ON c.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema AND c.relname = @table AND con.contype = 'x';
                """;
            exclCmd.Parameters.AddWithValue("schema", parts[0]);
            exclCmd.Parameters.AddWithValue("table", parts[1]);
            long exclCount = (long)(await exclCmd.ExecuteScalarAsync(ct))!;
            exclCount.Should().Be(1L, $"{qualified} must enforce a GiST exclusion constraint");
        }

        string[] sampleInsertOnlyTables =
        [
            "payments.payments_participant_transactions",
            "tickets.ticket_revisions",
            "tickets.ticket_audit_logs",
            "safety.periodic_safety_report_revisions",
            "safety.safety_narrative_revisions",
            "etmf.tmf_document_revisions",
            "econsent.consent_document_revisions",
            "eisf.eisf_document_revisions",
            "knowledge.knowledge_article_versions",
        ];

        foreach (string qualified in sampleInsertOnlyTables)
        {
            string[] parts = qualified.Split('.');
            string expectedTrigger = StandaloneInsertOnlySql.RowTriggerName(parts[1]);

            await using var ioCmd = verifyConn.CreateCommand();
            ioCmd.CommandText = """
                SELECT COUNT(*)
                FROM pg_trigger t
                JOIN pg_class c ON c.oid = t.tgrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema AND c.relname = @table AND t.tgname = @trigger;
                """;
            ioCmd.Parameters.AddWithValue("schema", parts[0]);
            ioCmd.Parameters.AddWithValue("table", parts[1]);
            ioCmd.Parameters.AddWithValue("trigger", expectedTrigger);
            long ioCount = (long)(await ioCmd.ExecuteScalarAsync(ct))!;
            ioCount.Should().Be(1L, $"{qualified} must install insert-only trigger {expectedTrigger}");
        }
    }
}
