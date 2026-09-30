using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Database;
using Cadence.UnitTests.Packages;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Integration;

/// <summary>
/// Applies every service's migrations the way a deployed service does (<c>AddCadencePostgresDbContext</c> plus
/// <see cref="CadenceMigrationHostedService{TContext}"/>) into one shared database, and checks that each service's
/// tables and migration history land in its own schema (ADR-2247). Needs <c>CADENCE_TEST_POSTGRES</c> or
/// <c>CADENCE_TEST_DOCKER=1</c>; without either the tests report as skipped.
/// </summary>
public sealed class ServiceSchemaPostgresTests(ThrowawayPostgresDatabase database) : IClassFixture<ThrowawayPostgresDatabase>
{
    public static IEnumerable<TheoryDataRow<string>> Factories() => MigrationUnitTests.Factories();

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
}
