using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Database;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.TestSupport.Database;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class MigrationUnitTests
{
    /// <summary>Design-time factories have no fallback credential; tests pass a dummy connection explicitly.</summary>
    private static readonly string[] DesignTimeArgs =
        [CadenceDatabaseExtensions.DesignTimeConnectionArgument, TestHostSettings.PostgresConnectionString];

    public static IEnumerable<TheoryDataRow<string>> Factories() =>
        ServiceDbContextFactories.All.Select(t => new TheoryDataRow<string>(t.AssemblyQualifiedName!));

    /// <summary>
    /// Every service's model must match its migration snapshot; otherwise MigrateAsync throws
    /// PendingModelChangesWarning at deploy time (ctms and org did, see readiness checklist).
    /// No database connection is needed.
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [MemberData(nameof(Factories))]
    public void ServiceDbContext_HasNoModelChangesMissingAMigration(string factoryTypeName)
    {
        Type factoryType = Type.GetType(factoryTypeName, throwOnError: true)!;
        object factory = Activator.CreateInstance(factoryType)!;
        var createDbContext = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;

        using var context = (DbContext)createDbContext.Invoke(factory, [DesignTimeArgs])!;

        context.Database.HasPendingModelChanges().Should().BeFalse(
            $"{context.GetType().Name} has model changes without a migration; run scripts/manage_migrations.sh add <service> <Name>");
    }

    /// <summary>
    /// Design-time tooling (dotnet ef) and the running service must target the same module schema, or
    /// `database update` would apply migrations to a schema the service never reads.
    /// </summary>
    [Theory]
    [MemberData(nameof(Factories))]
    public void ServiceDbContextFactory_TargetsTheModuleSchemaTheServiceUses(string factoryTypeName)
    {
        Type factoryType = Type.GetType(factoryTypeName, throwOnError: true)!;
        string schema = (string)factoryType.GetField("Schema")!.GetRawConstantValue()!;
        object factory = Activator.CreateInstance(factoryType)!;
        var createDbContext = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;
        using var context = (DbContext)createDbContext.Invoke(factory, [DesignTimeArgs])!;

        new Npgsql.NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).SearchPath
            .Should().Be($"{schema},public");

        // Execution passes its schema ("edc", ADR-2247) explicitly; every other service relies on the derived name.
        if (context is not Cadence.Apps.Execution.Infrastructure.Persistence.ExecutionDbContext)
        {
            string derived = context.GetType().Name.Replace("DbContext", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            schema.Should().Be(derived);
        }
    }

    [Theory]
    [InlineData("ctms")]
    [InlineData("edc")]
    public void WithModuleSchema_SetsSearchPathToModuleThenPublic(string schema)
    {
        string result = CadenceDatabaseExtensions.WithModuleSchema("Host=localhost;Database=cadence", schema);

        new Npgsql.NpgsqlConnectionStringBuilder(result).SearchPath.Should().Be($"{schema},public");
    }

    [Theory]
    [InlineData("Ctms")]
    [InlineData("ctms; DROP SCHEMA public")]
    [InlineData("")]
    public void WithModuleSchema_RejectsInvalidSchemaNames(string schema)
    {
        Action act = () => CadenceDatabaseExtensions.WithModuleSchema("Host=localhost", schema);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddCadencePostgresDbContext_WithoutMigrationFlags_RegistersNoMigrationService()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration["ConnectionStrings:cadence"] = TestHostSettings.PostgresConnectionString;

        builder.AddCadencePostgresDbContext<TestDbContext>();

        using var app = builder.Build();
        app.Services.GetServices<IHostedService>()
            .Should().NotContain(s => s is CadenceMigrationHostedService<TestDbContext>);
    }

    [Theory]
    [InlineData("Database:MigrateOnStartup")]
    [InlineData("Database:MigrateOnly")]
    public void AddCadencePostgresDbContext_WithMigrationFlag_RegistersMigrationService(string key)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration["ConnectionStrings:cadence"] = TestHostSettings.PostgresConnectionString;
        builder.Configuration[key] = "true";

        builder.AddCadencePostgresDbContext<TestDbContext>();

        using var app = builder.Build();
        app.Services.GetServices<IHostedService>()
            .Should().ContainSingle(s => s is CadenceMigrationHostedService<TestDbContext>);
    }
}
