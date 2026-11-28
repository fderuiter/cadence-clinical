using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Unified EF Core migration workflow across all 18 modules in <see cref="PersistenceModuleCatalog"/>.
/// Generates and formats migrations, writes idempotent review SQL and bundles to untracked <c>TestResults/migrations/</c>,
/// applies schema-scoped migrations via <c>Cadence.DatabaseMigrator</c>, generates compiled models, runs declared seeders,
/// lists migrations, and verifies snapshots, <c>.Designer.cs</c> companions, attributes, and DDL uniqueness via <c>Cadence.SchemaDocExporter</c>.
/// </summary>
public static partial class MigrationToolingService
{
    public const string DefaultScriptRelativeDir = "TestResults/migrations";
    public const string DefaultCompiledModelsRelativeDir = "CompiledModels";
    public const string DesignTimePlaceholderConnection = "Host=localhost;Database=cadence_migrations";
    private const string ExporterProject = "packages/tooling_core/src/Cadence.SchemaDocExporter/Cadence.SchemaDocExporter.csproj";
    private const string MigratorProject = "packages/tooling_core/src/Cadence.DatabaseMigrator/Cadence.DatabaseMigrator.csproj";
    private const string PrimaryConnectionEnvVar = "ConnectionStrings__cadence";
    private const string PostgresConnectionEnvVar = "POSTGRES_CONNECTION_STRING";

    /// <summary>
    /// Runs <c>dotnet ef migrations add</c> for a single module in <see cref="PersistenceModuleCatalog"/>,
    /// then runs scoped whitespace and style formatting on any newly created or modified migration <c>.cs</c> files.
    /// </summary>
    public static int Add(
        string module,
        string migrationName,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (string.IsNullOrWhiteSpace(migrationName))
        {
            Console.Error.WriteLine("[ERROR] Migration name is required for 'migrations add'.");
            return 1;
        }

        if (!PersistenceModuleCatalog.TryResolveTargets(module, allowAll: false, out var targets, out string error))
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        var target = targets[0];
        string projectPath = target.GetProjectPath(repoRoot);
        if (!File.Exists(projectPath))
        {
            Console.Error.WriteLine($"[ERROR] Project not found at {projectPath}.");
            return 1;
        }

        string migrationsDir = target.GetMigrationsDirectory(repoRoot);
        var beforeSnapshot = SnapshotMigrationCSharpFiles(migrationsDir);

        var args = new List<string>
        {
            "ef",
            "migrations",
            "add",
            migrationName.Trim(),
            "--project",
            projectPath,
            "--output-dir",
            target.MigrationsRelativeDir,
            "--context",
            target.DbContextName,
        };
        AppendDesignTimeConnectionIfUnset(args, getEnvironmentVariable);

        Console.WriteLine($"Generating migration '{migrationName.Trim()}' for module '{target.Module}' ({target.DbContextName}, schema '{target.Schema}')...");
        int exitCode = InvokeProcess(repoRoot, "dotnet", args, processRunner);
        if (exitCode != 0)
        {
            return exitCode;
        }

        var touchedRelativeFiles = GetTouchedRelativeMigrationFiles(repoRoot, migrationsDir, beforeSnapshot);
        if (touchedRelativeFiles.Count > 0)
        {
            int formatExitCode = FormatTouchedMigrationFiles(repoRoot, projectPath, touchedRelativeFiles, processRunner);
            if (formatExitCode != 0)
            {
                return formatExitCode;
            }
        }

        Console.WriteLine($"✔ Migration '{migrationName.Trim()}' created under {migrationsDir}.");
        return 0;
    }

    /// <summary>
    /// Runs <c>dotnet ef migrations script --idempotent</c> for one module or all 18 modules, writing SQL artifacts to
    /// <c>TestResults/migrations/&lt;module&gt;.sql</c> (or <paramref name="outputDir"/>) outside the tracked source tree.
    /// </summary>
    public static int Script(
        string? moduleOrAll = null,
        string? outputDir = null,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null,
        Func<string, string?>? getEnvironmentVariable = null) =>
        RunPerModuleEfArtifact(
            moduleOrAll,
            outputDir,
            repoRoot,
            processRunner,
            getEnvironmentVariable,
            buildFileName: static module => $"{module}.sql",
            buildEfArgs: static (projectPath, dbContextName, outFile) =>
            [
                "ef",
                "migrations",
                "script",
                "--idempotent",
                "--project",
                projectPath,
                "--output",
                outFile,
                "--context",
                dbContextName,
            ],
            startBanner: static (module, dbContextName) => $"Generating idempotent SQL migration script for '{module}' ({dbContextName})...",
            doneBanner: static outFile => $"✔ SQL script written to {outFile}");

    /// <summary>
    /// Lists migrations for one module via <c>dotnet ef migrations list</c>, or across all 18 modules.
    /// When neither <c>ConnectionStrings__cadence</c> nor <c>POSTGRES_CONNECTION_STRING</c> is set, offline mode
    /// (<c>--no-connect</c>) is used for single-module queries and on-disk <c>.Designer.cs</c> inspection is used
    /// when listing all 18 modules without a database.
    /// </summary>
    public static int List(
        string? moduleOrAll = null,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (!PersistenceModuleCatalog.TryResolveTargets(moduleOrAll, allowAll: true, out var targets, out string error))
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        bool hasLiveConnection = HasConfiguredPostgresConnection(getEnvironmentVariable);

        if (targets.Count > 1 && !hasLiveConnection && processRunner is null)
        {
            Console.WriteLine($"Persistence modules ({targets.Count}):");
            foreach (var target in targets)
            {
                string migrationsDir = target.GetMigrationsDirectory(repoRoot);
                var migrationIds = Directory.Exists(migrationsDir)
                    ? Directory.GetFiles(migrationsDir, "*.Designer.cs")
                        .Select(f => Path.GetFileName(f)[..^".Designer.cs".Length])
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToList()
                    : [];

                Console.WriteLine($"  {target.Module,-16} schema={target.Schema,-14} context={target.DbContextName,-26} migrations={migrationIds.Count}");
                foreach (string id in migrationIds)
                {
                    Console.WriteLine($"    - {id}");
                }
            }

            return 0;
        }

        foreach (var target in targets)
        {
            string projectPath = target.GetProjectPath(repoRoot);
            if (!File.Exists(projectPath))
            {
                Console.Error.WriteLine($"[ERROR] Project not found at {projectPath}.");
                return 1;
            }

            var args = new List<string>
            {
                "ef",
                "migrations",
                "list",
                "--project",
                projectPath,
                "--context",
                target.DbContextName,
            };
            if (!hasLiveConnection)
            {
                args.Add("--no-connect");
                AppendDesignTimeConnectionIfUnset(args, getEnvironmentVariable);
            }

            Console.WriteLine($"Listing migrations for '{target.Module}' ({target.DbContextName}, schema '{target.Schema}')...");
            int exitCode = InvokeProcess(repoRoot, "dotnet", args, processRunner);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        return 0;
    }

    /// <summary>
    /// Verifies <c>HasPendingModelChanges()</c>, <c>[DbContext]</c>, <c>[Migration]</c>, <c>.Designer.cs</c> files,
    /// and <c>CreateTable</c>/<c>AddColumn</c> uniqueness for one module or all 18 modules via <c>Cadence.SchemaDocExporter</c>.
    /// </summary>
    public static int Verify(
        string? moduleOrAll = null,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null)
    {
        if (!PersistenceModuleCatalog.TryResolveTargets(moduleOrAll, allowAll: true, out var targets, out string error))
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        var args = new List<string>
        {
            "run",
            "--project",
            ExporterProject,
            "--",
            "--verify-only",
        };
        if (targets.Count == 1)
        {
            args.Add("--module");
            args.Add(targets[0].Module);
        }

        return InvokeProcess(repoRoot, "dotnet", args, processRunner);
    }
}
