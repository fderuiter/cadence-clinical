using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Database;
using Cadence.Testing;

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
    private static readonly Type[] ServiceDbContextFactories =
    [
        typeof(Cadence.Apps.Ctms.Infrastructure.Persistence.CtmsDbContextFactory),
        typeof(Cadence.Apps.Econsent.Infrastructure.Persistence.EconsentDbContextFactory),
        typeof(Cadence.Apps.Eisf.Infrastructure.Persistence.EisfDbContextFactory),
        typeof(Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContextFactory),
        typeof(Cadence.Apps.Execution.Infrastructure.Persistence.ExecutionDbContextFactory),
        typeof(Cadence.Apps.Fileshare.Infrastructure.Persistence.FileshareDbContextFactory),
        typeof(Cadence.Apps.Imaging.Infrastructure.Persistence.ImagingDbContextFactory),
        typeof(Cadence.Apps.Interop.Infrastructure.Persistence.InteropDbContextFactory),
        typeof(Cadence.Apps.Knowledge.Infrastructure.Persistence.KnowledgeDbContextFactory),
        typeof(Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContextFactory),
        typeof(Cadence.Apps.Org.Infrastructure.Persistence.OrgDbContextFactory),
        typeof(Cadence.Apps.Payments.Infrastructure.Persistence.PaymentsDbContextFactory),
        typeof(Cadence.Apps.Quality.Infrastructure.Persistence.QualityDbContextFactory),
        typeof(Cadence.Apps.Safety.Infrastructure.Persistence.SafetyDbContextFactory),
        typeof(Cadence.Apps.Tickets.Infrastructure.Persistence.TicketsDbContextFactory),
    ];

    public static IEnumerable<TheoryDataRow<string>> Factories() =>
        ServiceDbContextFactories.Select(t => new TheoryDataRow<string>(t.AssemblyQualifiedName!));

    /// <summary>
    /// Every service's model must match its migration snapshot; otherwise MigrateAsync throws
    /// PendingModelChangesWarning at deploy time (ctms and org did, see readiness checklist).
    /// No database connection is needed.
    /// @req:PRD-SYS-053
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    [MemberData(nameof(Factories))]
    public void ServiceDbContext_HasNoModelChangesMissingAMigration(string factoryTypeName)
    {
        Type factoryType = Type.GetType(factoryTypeName, throwOnError: true)!;
        object factory = Activator.CreateInstance(factoryType)!;
        var createDbContext = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;

        using var context = (DbContext)createDbContext.Invoke(factory, [Array.Empty<string>()])!;

        context.Database.HasPendingModelChanges().Should().BeFalse(
            $"{context.GetType().Name} has model changes without a migration; run scripts/manage_migrations.sh add <service> <Name>");
    }

    /// <summary>
    /// Design-time tooling (dotnet ef) and the running service must target the same module schema, or
    /// `database update` would apply migrations to a schema the service never reads.
    /// @req:PRD-SYS-053
    /// </summary>
    [Theory]
    [GxPRequirement("PRD-SYS-053")]
    [MemberData(nameof(Factories))]
    public void ServiceDbContextFactory_TargetsTheModuleSchemaTheServiceUses(string factoryTypeName)
    {
        Type factoryType = Type.GetType(factoryTypeName, throwOnError: true)!;
        string schema = (string)factoryType.GetField("Schema")!.GetRawConstantValue()!;
        object factory = Activator.CreateInstance(factoryType)!;
        var createDbContext = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;
        using var context = (DbContext)createDbContext.Invoke(factory, [Array.Empty<string>()])!;

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
    [GxPRequirement("PRD-SYS-053")]
    [InlineData("ctms")]
    [InlineData("edc")]
    public void WithModuleSchema_SetsSearchPathToModuleThenPublic(string schema)
    {
        string result = CadenceDatabaseExtensions.WithModuleSchema("Host=localhost;Database=cadence", schema);

        new Npgsql.NpgsqlConnectionStringBuilder(result).SearchPath.Should().Be($"{schema},public");
    }

    [Theory]
    [GxPRequirement("PRD-SYS-053")]
    [InlineData("Ctms")]
    [InlineData("ctms; DROP SCHEMA public")]
    [InlineData("")]
    public void WithModuleSchema_RejectsInvalidSchemaNames(string schema)
    {
        Action act = () => CadenceDatabaseExtensions.WithModuleSchema("Host=localhost", schema);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void AddCadencePostgresDbContext_WithoutMigrationFlags_RegistersNoMigrationService()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });

        builder.AddCadencePostgresDbContext<TestDbContext>();

        using var app = builder.Build();
        app.Services.GetServices<IHostedService>()
            .Should().NotContain(s => s is CadenceMigrationHostedService<TestDbContext>);
    }

    [Theory]
    [GxPRequirement("PRD-SYS-053")]
    [InlineData("Database:MigrateOnStartup")]
    [InlineData("Database:MigrateOnly")]
    public void AddCadencePostgresDbContext_WithMigrationFlag_RegistersMigrationService(string key)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration[key] = "true";

        builder.AddCadencePostgresDbContext<TestDbContext>();

        using var app = builder.Build();
        app.Services.GetServices<IHostedService>()
            .Should().ContainSingle(s => s is CadenceMigrationHostedService<TestDbContext>);
    }
}
