using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Cli.Commands;
using Cadence.Cli.Services;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Modules.Intelligence.Infrastructure.Persistence;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class CliDatabaseWorkflowTests
{
    [Fact]
    public void PersistenceModuleRegistry_CoversAllEighteenModulesWithKernelFirstAndMatchesSchemaExporterProject()
    {
        PersistenceModuleRegistry.All.Should().HaveCount(18);
        PersistenceModuleRegistry.All[0].Name.Should().Be("kernel");
        PersistenceModuleRegistry.All[0].Schema.Should().Be("kernel");
        PersistenceModuleRegistry.AllSchemas.Should().OnlyHaveUniqueItems();
        PersistenceModuleRegistry.AllNames.Should().OnlyHaveUniqueItems();

        PersistenceModuleRegistry.All.Where(static m => m.SupportsSeed)
            .Select(static m => m.Name)
            .Should().Equal("execution");

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        foreach (var module in PersistenceModuleRegistry.All)
        {
            string projectFullPath = Path.Combine(repoRoot, module.ProjectPath);
            File.Exists(projectFullPath).Should().BeTrue($"project file for '{module.Name}' must exist at {module.ProjectPath}");

            string migrationsFullPath = Path.Combine(Path.GetDirectoryName(projectFullPath)!, module.MigrationsDir);
            Directory.Exists(migrationsFullPath).Should().BeTrue($"migrations directory for '{module.Name}' must exist at {module.MigrationsDir}");

            string snapshotFullPath = Path.Combine(migrationsFullPath, $"{module.DbContextName}ModelSnapshot.cs");
            File.Exists(snapshotFullPath).Should().BeTrue($"snapshot '{module.DbContextName}ModelSnapshot.cs' must exist for '{module.Name}'");
        }

        string exporterCsproj = Path.Combine(
            repoRoot,
            "packages",
            "tooling_core",
            "src",
            "Cadence.SchemaDocExporter",
            "Cadence.SchemaDocExporter.csproj");
        var doc = XDocument.Load(exporterCsproj);
        var referencedCsprojs = doc.Descendants("ProjectReference")
            .Select(static el => Path.GetFileNameWithoutExtension(((string?)el.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .Where(static name => !string.Equals(name, "Cadence.Database", StringComparison.Ordinal) &&
                                  !string.Equals(name, "Cadence.ToolingCore", StringComparison.Ordinal))
            .ToArray();

        string migratorCsproj = Path.Combine(
            repoRoot,
            "packages",
            "tooling_core",
            "src",
            "Cadence.DatabaseMigrator",
            "Cadence.DatabaseMigrator.csproj");
        var migratorDoc = XDocument.Load(migratorCsproj);
        var migratorReferencedCsprojs = migratorDoc.Descendants("ProjectReference")
            .Select(static el => Path.GetFileNameWithoutExtension(((string?)el.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .Where(static name => !string.Equals(name, "Cadence.Database", StringComparison.Ordinal) &&
                                  !string.Equals(name, "Cadence.ToolingCore", StringComparison.Ordinal))
            .ToArray();

        PersistenceModuleRegistry.All.Select(static m => m.AssemblyName)
            .Should().BeEquivalentTo(referencedCsprojs);
        PersistenceModuleRegistry.All.Select(static m => m.AssemblyName)
            .Should().BeEquivalentTo(migratorReferencedCsprojs);
        PersistenceModuleRegistry.All.Select(static m => m.AssemblyName)
            .Should().BeEquivalentTo(PersistenceModuleCatalog.All.Select(static m => m.AssemblyName));
        PersistenceModuleRegistry.AllSchemas
            .Should().BeEquivalentTo(PersistenceModuleCatalog.All.Select(static m => m.Schema));
    }

    [Fact]
    public void PersistenceModuleRegistry_TryResolveTargets_ResolvesAllSingleAndUnknown()
    {
        PersistenceModuleRegistry.TryResolveTargets(null, out var allDefault, out string? errDefault).Should().BeTrue();
        errDefault.Should().BeNull();
        allDefault.Should().HaveCount(18);
        allDefault[0].Name.Should().Be("kernel");

        PersistenceModuleRegistry.TryResolveTargets("execution-module", out var single, out string? errSingle).Should().BeTrue();
        errSingle.Should().BeNull();
        single.Should().ContainSingle().Which.Schema.Should().Be("execution");

        PersistenceModuleRegistry.TryResolveTargets("executionmodule", out var aliasMatch, out string? errAlias).Should().BeTrue();
        errAlias.Should().BeNull();
        aliasMatch.Should().ContainSingle().Which.Name.Should().Be("execution-module");

        PersistenceModuleRegistry.TryResolveTargets("modules/execution", out var pathAliasMatch, out string? errPathAlias).Should().BeTrue();
        errPathAlias.Should().BeNull();
        pathAliasMatch.Should().ContainSingle().Which.Name.Should().Be("execution-module");

        PersistenceModuleRegistry.TryResolveTargets("intelligence-module", out var intelAliasMatch, out string? errIntelAlias).Should().BeTrue();
        errIntelAlias.Should().BeNull();
        intelAliasMatch.Should().ContainSingle().Which.Name.Should().Be("intelligence");

        PersistenceModuleRegistry.TryResolveTargets("nonexistent", out var none, out string? errUnknown).Should().BeFalse();
        none.Should().BeEmpty();
        errUnknown.Should().Contain("Unknown persistence module 'nonexistent'");
    }

    [Fact]
    public void KernelAndModuleDesignTimeFactories_RespectPrecedenceAndCredentialFreeFallback()
    {
        KernelDbContextDesignTimeFactory.ResolveConnectionString(["--connection", "Host=localhost;Database=arg"], _ => "Host=localhost;Database=env")
            .Should().Be("Host=localhost;Database=arg");
        KernelDbContextDesignTimeFactory.ResolveConnectionString([], key => key == "ConnectionStrings__cadence" ? "Host=localhost;Database=primary" : "Host=localhost;Database=legacy")
            .Should().Be("Host=localhost;Database=primary");
        KernelDbContextDesignTimeFactory.ResolveConnectionString([], key => key == "POSTGRES_CONNECTION_STRING" ? "Host=localhost;Database=legacy" : null)
            .Should().Be("Host=localhost;Database=legacy");
        KernelDbContextDesignTimeFactory.ResolveConnectionString([], _ => null)
            .Should().Be("Host=localhost;Database=cadence_design");

        ModuleMigrationServiceCollectionExtensions.ResolveDesignTimeConnectionStringCore(["--connection", "Host=localhost;Database=arg"], _ => "Host=localhost;Database=env")
            .Should().Be("Host=localhost;Database=arg");
        ModuleMigrationServiceCollectionExtensions.ResolveDesignTimeConnectionStringCore([], key => key == "ConnectionStrings__cadence" ? "Host=localhost;Database=primary" : "Host=localhost;Database=legacy")
            .Should().Be("Host=localhost;Database=primary");
        ModuleMigrationServiceCollectionExtensions.ResolveDesignTimeConnectionStringCore([], key => key == "POSTGRES_CONNECTION_STRING" ? "Host=localhost;Database=legacy" : null)
            .Should().Be("Host=localhost;Database=legacy");
        ModuleMigrationServiceCollectionExtensions.ResolveDesignTimeConnectionStringCore([], _ => null)
            .Should().Be("Host=localhost;Database=cadence_design");

        using var kernelCtx = new KernelDbContextDesignTimeFactory().CreateDbContext([]);
        using var execModuleCtx = new ExecutionModuleDbContextDesignTimeFactory().CreateDbContext([]);
        using var intelligenceCtx = new IntelligenceDbContextDesignTimeFactory().CreateDbContext(
            ["--connection", "Host=localhost;Database=custom_intel"]);

        new NpgsqlConnectionStringBuilder(intelligenceCtx.Database.GetConnectionString()).Database
            .Should().Be("custom_intel");
    }

    [Fact]
    public void DatabaseResetService_ConnectionResolutionAndSchemaScopedSql_PreservePublicExtensions()
    {
        var fromPrimary = new NpgsqlConnectionStringBuilder(
            DatabaseResetService.ResolvePostgresConnectionString(
                key => key == "ConnectionStrings__cadence" ? "Host=localhost;Database=primary" : "Host=localhost;Database=other"));
        fromPrimary.Host.Should().Be("localhost");
        fromPrimary.Database.Should().Be("primary");

        var fromLegacy = new NpgsqlConnectionStringBuilder(
            DatabaseResetService.ResolvePostgresConnectionString(
                key => key == "POSTGRES_CONNECTION_STRING" ? "postgresql://u@localhost:5432/cadence" : null));
        fromLegacy.Host.Should().Be("localhost");
        fromLegacy.Database.Should().Be("cadence");
        fromLegacy.Username.Should().Be("u");

        string sql = DatabaseResetService.BuildResetPostgresSql(
            ["audit_schema", "identity_vault", "public", .. PersistenceModuleRegistry.AllSchemas]);
        sql.Should().NotContain("DROP SCHEMA IF EXISTS \"public\"", "public schema extensions like btree_gist must be preserved");
        sql.Should().NotContain("DROP SCHEMA IF EXISTS public", "public schema extensions like btree_gist must be preserved");
        sql.Should().Contain("CREATE SCHEMA IF NOT EXISTS public;");
        sql.Should().Contain("DROP SCHEMA IF EXISTS \"audit_schema\" CASCADE;");
        sql.Should().Contain("DROP SCHEMA IF EXISTS \"identity_vault\" CASCADE;");
        sql.Should().Contain("d.deptype = 'e'");

        foreach (string schema in PersistenceModuleRegistry.AllSchemas)
        {
            sql.Should().Contain($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");
        }
    }

    [Fact]
    public async Task DatabaseResetService_FailsClosedWhenOfflineByDefaultAndWarnsWhenAllowOffline()
    {
        int strictExit = await DatabaseResetService.ResetDatabasesAsync(allowOffline: false, getEnvironmentVariable: _ => null);
        strictExit.Should().Be(1);

        int offlineExit = await DatabaseResetService.ResetDatabasesAsync(allowOffline: true, getEnvironmentVariable: _ => null);
        offlineExit.Should().Be(0);

        Func<Task> prodAct = () => DatabaseResetService.ResetDatabasesAsync(
            allowOffline: true,
            getEnvironmentVariable: key => key == "CADENCE_ENV" ? "Production" : null);
        await prodAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Safety Guardrail Violation*");

        Action remoteHostAct = () => DatabaseResetService.ValidateLocalOnly("Postgres", "Host=db.internal.example.org;Database=cadence");
        remoteHostAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-local*");
    }

    [Fact]
    public void DbCommands_OnlineConnectionResolution_RequiresConfiguredVariable()
    {
        DbCommands.TryResolveOnlineConnectionString(out string? none, out string? error, _ => null).Should().BeFalse();
        none.Should().BeNull();
        error.Should().Contain("ConnectionStrings__cadence").And.Contain("POSTGRES_CONNECTION_STRING");

        DbCommands.TryResolveOnlineConnectionString(
                out string? primary,
                out string? primaryError,
                key => key == "ConnectionStrings__cadence" ? "Host=localhost;Database=cadence" : "Host=localhost;Database=other")
            .Should().BeTrue();
        primaryError.Should().BeNull();
        var builder = new NpgsqlConnectionStringBuilder(primary);
        builder.Host.Should().Be("localhost");
        builder.Database.Should().Be("cadence");
    }

    [Fact]
    public void DoctorCommand_ChecksLocalDotnetEfManifestAndRemediatesWithAutoFix()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string? pinned = DoctorCommand.ReadPinnedDotnetEfVersion(repoRoot);
        pinned.Should().Be("10.0.15");

        DoctorCommand.CheckDotnetEf(repoRoot, autoFix: false, probeVersion: _ => null, restoreTools: _ => true)
            .Should().BeFalse();

        bool restoreCalled = false;
        DoctorCommand.CheckDotnetEf(
                repoRoot,
                autoFix: true,
                probeVersion: _ => restoreCalled ? pinned : null,
                restoreTools: _ =>
                {
                    restoreCalled = true;
                    return true;
                })
            .Should().BeTrue();
        restoreCalled.Should().BeTrue();
    }
}
