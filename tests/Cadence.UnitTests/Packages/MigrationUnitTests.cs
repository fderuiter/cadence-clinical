using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.Database;
using Cadence.Kernel.Persistence;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Modules.Intelligence.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.TestSupport.Database;
using Cadence.ToolingCore.Services;

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
            $"{context.GetType().Name} has model changes without a migration; run pnpm cadence migrations add <module> <Name>");
    }

    /// <summary>
    /// Host-mounted Intelligence module DbContext must match its checked-in migration snapshot.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public void IntelligenceDbContext_HasNoModelChangesMissingAMigration()
    {
        using var context = new IntelligenceDbContextDesignTimeFactory().CreateDbContext(DesignTimeArgs);

        context.Database.GetMigrations().Should().NotBeEmpty();
        context.Database.HasPendingModelChanges().Should().BeFalse(
            "IntelligenceDbContext has model changes without a migration; run pnpm cadence migrations add intelligence <Name>");
    }

    /// <summary>
    /// Host-mounted Execution module DbContext must match its checked-in migration snapshot.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public void ExecutionModuleDbContext_HasNoModelChangesMissingAMigration()
    {
        using var context = new ExecutionModuleDbContextDesignTimeFactory().CreateDbContext(DesignTimeArgs);

        context.Database.GetMigrations().Should().NotBeEmpty();
        context.Database.HasPendingModelChanges().Should().BeFalse(
            "ExecutionModuleDbContext has model changes without a migration; run pnpm cadence migrations add executionmodule <Name>");
    }

    [Fact]
    public void PersistenceModuleCatalog_DefinesAllEighteenModules_WithExistingProjectsAndMigrationsDirectories()
    {
        string repoRoot = SecretsBaselineCleanerService.FindRepoRoot();

        PersistenceModuleCatalog.All.Should().HaveCount(18);
        PersistenceModuleCatalog.All.Select(m => m.Module)
            .Should().BeInAscendingOrder(StringComparer.Ordinal)
            .And.OnlyHaveUniqueItems();

        PersistenceModuleCatalog.Find("kernel")!.DbContextName.Should().Be(nameof(KernelDbContext));
        PersistenceModuleCatalog.Find("intelligence")!.DbContextName.Should().Be(nameof(IntelligenceDbContext));
        PersistenceModuleCatalog.Find("executionmodule")!.DbContextName.Should().Be(nameof(ExecutionModuleDbContext));
        PersistenceModuleCatalog.Find("execution-module")!.Module.Should().Be("executionmodule");

        foreach (var module in PersistenceModuleCatalog.All)
        {
            File.Exists(module.GetProjectPath(repoRoot))
                .Should().BeTrue($"{module.Module} project must exist at {module.ProjectRelativePath}");
            Directory.Exists(module.GetMigrationsDirectory(repoRoot))
                .Should().BeTrue($"{module.Module} migrations directory must exist at {module.MigrationsRelativeDir}");
        }
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

    [Theory]
    [InlineData("Host=cli", "Host=primary", "Host=legacy", "Host=cli")]
    [InlineData(null, "Host=primary", "Host=legacy", "Host=primary")]
    [InlineData(null, null, "Host=legacy", "Host=legacy")]
    public void KernelAndModuleDesignTimeFactories_HonorConnectionPrecedence(
        string? cliConnection,
        string? primaryEnv,
        string? legacyEnv,
        string expected)
    {
        string[] args = cliConnection is null
            ? []
            : [CadenceDatabaseExtensions.DesignTimeConnectionArgument, cliConnection];

        string? LookupEnv(string key) => key switch
        {
            CadenceDatabaseExtensions.PrimaryConnectionStringEnvVar => primaryEnv,
            CadenceDatabaseExtensions.LegacyConnectionStringKey => legacyEnv,
            _ => null,
        };

        KernelDbContextDesignTimeFactory.ResolveConnectionString(args, LookupEnv).Should().Be(expected);
        ExecutionModuleDbContextDesignTimeFactory.ResolveConnectionString(args, LookupEnv).Should().Be(expected);
        IntelligenceDbContextDesignTimeFactory.ResolveConnectionString(args, LookupEnv).Should().Be(expected);
    }

    [Fact]
    public void KernelAndModuleDesignTimeFactories_FailClosedWhenAllConnectionSourcesOmitted()
    {
        Func<string, string?> emptyEnv = _ => null;

        Action kernelAct = () => KernelDbContextDesignTimeFactory.ResolveConnectionString([], emptyEnv);
        Action execModAct = () => ExecutionModuleDbContextDesignTimeFactory.ResolveConnectionString([], emptyEnv);
        Action intelAct = () => IntelligenceDbContextDesignTimeFactory.ResolveConnectionString([], emptyEnv);

        kernelAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__cadence*POSTGRES_CONNECTION_STRING*--connection*");
        execModAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__cadence*POSTGRES_CONNECTION_STRING*--connection*");
        intelAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__cadence*POSTGRES_CONNECTION_STRING*--connection*");

        bool anyEnvConfigured =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CadenceDatabaseExtensions.PrimaryConnectionStringEnvVar)) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CadenceDatabaseExtensions.LegacyConnectionStringKey));
        if (!anyEnvConfigured)
        {
            Action createIntel = () =>
            {
                using var _ = new IntelligenceDbContextDesignTimeFactory().CreateDbContext([]);
            };
            createIntel.Should().Throw<InvalidOperationException>(
                "IntelligenceDbContextDesignTimeFactory must never fall back to a hardcoded cadence_design connection string");
        }
    }
}
