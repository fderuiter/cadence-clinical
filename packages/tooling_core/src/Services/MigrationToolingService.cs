using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Unified EF Core migration workflow across all 18 modules in <see cref="PersistenceModuleCatalog"/>.
/// Generates and formats migrations, writes idempotent review SQL and bundles to untracked <c>TestResults/migrations/</c>,
/// applies schema-scoped migrations via <c>Cadence.DatabaseMigrator</c>, generates compiled models, runs declared seeders,
/// lists migrations, and verifies snapshots, <c>.Designer.cs</c> companions, attributes, and DDL uniqueness via <c>Cadence.SchemaDocExporter</c>.
/// </summary>
public static class MigrationToolingService
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

        repoRoot ??= FindRepoRoot();
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
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (!PersistenceModuleCatalog.TryResolveTargets(moduleOrAll, allowAll: true, out var targets, out string error))
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        repoRoot ??= FindRepoRoot();
        string resolvedOutputDir = ResolveOutputDirectory(repoRoot, outputDir);

        Directory.CreateDirectory(resolvedOutputDir);

        foreach (var target in targets)
        {
            string projectPath = target.GetProjectPath(repoRoot);
            if (!File.Exists(projectPath))
            {
                Console.Error.WriteLine($"[ERROR] Project not found at {projectPath}.");
                return 1;
            }

            string outFile = Path.Combine(resolvedOutputDir, $"{target.Module}.sql");
            var args = new List<string>
            {
                "ef",
                "migrations",
                "script",
                "--idempotent",
                "--project",
                projectPath,
                "--output",
                outFile,
                "--context",
                target.DbContextName,
            };
            AppendDesignTimeConnectionIfUnset(args, getEnvironmentVariable);

            Console.WriteLine($"Generating idempotent SQL migration script for '{target.Module}' ({target.DbContextName})...");
            int exitCode = InvokeProcess(repoRoot, "dotnet", args, processRunner);
            if (exitCode != 0)
            {
                return exitCode;
            }

            Console.WriteLine($"✔ SQL script written to {outFile}");
        }

        return 0;
    }

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

        repoRoot ??= FindRepoRoot();
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

        repoRoot ??= FindRepoRoot();
        var exporterArgs = new List<string> { "--verify-only" };
        if (targets.Count == 1)
        {
            exporterArgs.Add("--module");
            exporterArgs.Add(targets[0].Module);
        }

        if (processRunner is null)
        {
            ExporterBinaryResolver.ExporterInvocation invocation =
                ExporterBinaryResolver.ResolveInvocation(repoRoot, ExporterProject, exporterArgs);
            return InvokeProcess(repoRoot, invocation.FileName, invocation.Arguments, processRunner: null);
        }

        var args = new List<string>(exporterArgs.Count + 4)
        {
            "run",
            "--project",
            ExporterProject,
            "--",
        };
        args.AddRange(exporterArgs);

        return InvokeProcess(repoRoot, "dotnet", args, processRunner);
    }

    /// <summary>
    /// Applies pending EF Core migrations for one module or all 18 modules via <c>Cadence.DatabaseMigrator</c>
    /// so target schemas are pre-created and <c>Search Path</c> schema isolation is preserved (ADR-2447).
    /// </summary>
    public static int Apply(
        string? moduleOrAll = null,
        string? connectionString = null,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null)
    {
        if (!PersistenceModuleCatalog.TryResolveTargets(moduleOrAll, allowAll: true, out var targets, out string error))
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        repoRoot ??= FindRepoRoot();
        var migratorArgs = new List<string>();
        if (targets.Count == 1)
        {
            migratorArgs.Add("--module");
            migratorArgs.Add(targets[0].Module);
        }

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            migratorArgs.Add("--connection");
            migratorArgs.Add(connectionString.Trim());
        }

        string scopeLabel = targets.Count == 1
            ? $"module '{targets[0].Module}' ({targets[0].DbContextName}, schema '{targets[0].Schema}')"
            : $"all {targets.Count} persistence modules";
        Console.WriteLine($"Applying pending EF Core migrations for {scopeLabel}...");

        if (processRunner is null)
        {
            ExporterBinaryResolver.ExporterInvocation invocation =
                ExporterBinaryResolver.ResolveInvocation(repoRoot, MigratorProject, migratorArgs);
            return InvokeProcess(repoRoot, invocation.FileName, invocation.Arguments, processRunner: null);
        }

        var args = new List<string>(migratorArgs.Count + 4)
        {
            "run",
            "--project",
            MigratorProject,
        };
        if (migratorArgs.Count > 0)
        {
            args.Add("--");
            args.AddRange(migratorArgs);
        }

        return InvokeProcess(repoRoot, "dotnet", args, processRunner);
    }

    /// <summary>
    /// Generates self-contained EF Core migration bundles (<c>dotnet ef migrations bundle</c>) for one module or all 18 modules,
    /// supplying an offline placeholder connection when no PostgreSQL connection variable is set.
    /// </summary>
    public static int Bundle(
        string? moduleOrAll = null,
        string? outputDir = null,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (!PersistenceModuleCatalog.TryResolveTargets(moduleOrAll, allowAll: true, out var targets, out string error))
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        repoRoot ??= FindRepoRoot();
        string resolvedOutputDir = ResolveOutputDirectory(repoRoot, outputDir);
        Directory.CreateDirectory(resolvedOutputDir);

        foreach (var target in targets)
        {
            string projectPath = target.GetProjectPath(repoRoot);
            if (!File.Exists(projectPath))
            {
                Console.Error.WriteLine($"[ERROR] Project not found at {projectPath}.");
                return 1;
            }

            string outFile = Path.Combine(resolvedOutputDir, $"{target.Module}_migration_bundle");
            var args = new List<string>
            {
                "ef",
                "migrations",
                "bundle",
                "--project",
                projectPath,
                "--context",
                target.DbContextName,
                "--output",
                outFile,
                "--force",
            };
            AppendDesignTimeConnectionIfUnset(args, getEnvironmentVariable);

            Console.WriteLine($"Generating migration bundle for '{target.Module}' ({target.DbContextName})...");
            int exitCode = InvokeProcess(repoRoot, "dotnet", args, processRunner);
            if (exitCode != 0)
            {
                return exitCode;
            }

            Console.WriteLine($"✔ Migration bundle written to {outFile}");
        }

        return 0;
    }

    /// <summary>
    /// Generates pre-compiled EF Core models (<c>dotnet ef dbcontext optimize</c>) for one module or all 18 modules,
    /// supplying an offline placeholder connection when no PostgreSQL connection variable is set.
    /// </summary>
    public static int Optimize(
        string? moduleOrAll = null,
        string? outputDir = null,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (!PersistenceModuleCatalog.TryResolveTargets(moduleOrAll, allowAll: true, out var targets, out string error))
        {
            Console.Error.WriteLine($"[ERROR] {error}");
            return 1;
        }

        bool isAll = string.IsNullOrWhiteSpace(moduleOrAll)
            || string.Equals(moduleOrAll.Trim(), "all", StringComparison.OrdinalIgnoreCase);

        if (!isAll && !targets[0].SupportsCompiledModel)
        {
            Console.Error.WriteLine(
                $"[ERROR] Module '{targets[0].Module}' ({targets[0].DbContextName}) configures EF Core relational query filters (ADR-2552), which 'dotnet ef dbcontext optimize' does not support.");
            return 1;
        }

        repoRoot ??= FindRepoRoot();
        string effectiveOutputDir = string.IsNullOrWhiteSpace(outputDir)
            ? DefaultCompiledModelsRelativeDir
            : outputDir.Trim();

        foreach (var target in targets)
        {
            if (!target.SupportsCompiledModel)
            {
                Console.WriteLine(
                    $"Skipping '{target.Module}' ({target.DbContextName} configures EF Core relational query filters, which EF Core compiled models do not support).");
                continue;
            }

            string projectPath = target.GetProjectPath(repoRoot);
            if (!File.Exists(projectPath))
            {
                Console.Error.WriteLine($"[ERROR] Project not found at {projectPath}.");
                return 1;
            }

            var args = new List<string>
            {
                "ef",
                "dbcontext",
                "optimize",
                "--project",
                projectPath,
                "--context",
                target.DbContextName,
                "--output-dir",
                effectiveOutputDir,
                "--namespace",
                target.CompiledModelNamespace,
            };
            AppendDesignTimeConnectionIfUnset(args, getEnvironmentVariable);

            Console.WriteLine($"Generating compiled EF Core model for '{target.Module}' ({target.DbContextName})...");
            int exitCode = InvokeProcess(repoRoot, "dotnet", args, processRunner);
            if (exitCode != 0)
            {
                return exitCode;
            }

            Console.WriteLine($"✔ Compiled model generated for '{target.Module}' ({target.CompiledModelNamespace}).");
        }

        return 0;
    }

    /// <summary>
    /// Runs CLI sample-data seeders (<c>-- --seed</c>) for persistence modules that declare one (<c>execution</c>),
    /// skipping non-seeding modules cleanly during <c>seed all</c> and guiding callers who target an unsupported module.
    /// </summary>
    public static int Seed(
        string? moduleOrAll = null,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null,
        TextWriter? output = null,
        TextWriter? errorOutput = null)
    {
        var stdOut = output ?? Console.Out;
        var stdErr = errorOutput ?? Console.Error;

        if (!PersistenceModuleCatalog.TryResolveTargets(moduleOrAll, allowAll: true, out var targets, out string error))
        {
            stdErr.WriteLine($"[ERROR] {error}");
            return 1;
        }

        repoRoot ??= FindRepoRoot();

        if (targets.Count == 1 && !targets[0].SupportsCliSeed)
        {
            stdErr.WriteLine($"[ERROR] {DescribeUnsupportedSeedGuidance(targets[0])}");
            return 1;
        }

        foreach (var target in targets)
        {
            if (!target.SupportsCliSeed)
            {
                stdOut.WriteLine($"  Skipping '{target.Module}': {DescribeUnsupportedSeedGuidance(target)}");
                continue;
            }

            string projectPath = target.GetProjectPath(repoRoot);
            if (!File.Exists(projectPath))
            {
                stdErr.WriteLine($"[ERROR] Project not found at {projectPath}.");
                return 1;
            }

            var args = new List<string>
            {
                "run",
                "--project",
                projectPath,
                "--",
                "--seed",
            };

            stdOut.WriteLine($"Running CLI seeder for '{target.Module}' ({target.DbContextName}, schema '{target.Schema}')...");
            int exitCode = InvokeProcess(repoRoot, "dotnet", args, processRunner);
            if (exitCode != 0)
            {
                return exitCode;
            }

            stdOut.WriteLine($"✔ Seeding completed for '{target.Module}'.");
        }

        return 0;
    }

    private static string DescribeUnsupportedSeedGuidance(PersistenceModule target) =>
        target.Module switch
        {
            "kernel" =>
                "Module 'kernel' is a persistence library and does not expose a standalone '--seed' command. " +
                "Kernel development tenant and demo data are provisioned on apps/kernel-api startup via " +
                "apps/kernel-api/src/DevelopmentTenantBootstrap.cs (set Kernel__DevelopmentTenant__Code=cadence-local " +
                "and Kernel__DevelopmentTenant__DemoData=true).",
            "executionmodule" or "intelligence" =>
                $"Module '{target.Module}' is a composition-host module library rather than a standalone executable. " +
                "Host bootstrap and demo tenant provisioning run via apps/kernel-api/src/DevelopmentTenantBootstrap.cs " +
                "when starting apps/kernel-api.",
            "knowledge" =>
                "Module 'knowledge' does not expose a standalone '--seed' CLI command; SOP catalog articles are seeded " +
                "lazily via apps/knowledge/src/Application/KnowledgeCatalogSeeder.cs. Only 'execution' declares '--seed'.",
            _ =>
                $"Module '{target.Module}' does not declare a '--seed' CLI entrypoint. " +
                "Only 'execution' (apps/execution/src/Program.cs) supports 'migrations seed'.",
        };

    private static Dictionary<string, (long Length, DateTime LastWriteUtc)> SnapshotMigrationCSharpFiles(string migrationsDir)
    {
        var snapshot = new Dictionary<string, (long Length, DateTime LastWriteUtc)>(StringComparer.Ordinal);
        if (!Directory.Exists(migrationsDir))
        {
            return snapshot;
        }

        foreach (string path in Directory.GetFiles(migrationsDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            snapshot[Path.GetFullPath(path)] = (info.Length, info.LastWriteTimeUtc);
        }

        return snapshot;
    }

    private static List<string> GetTouchedRelativeMigrationFiles(
        string repoRoot,
        string migrationsDir,
        Dictionary<string, (long Length, DateTime LastWriteUtc)> beforeSnapshot)
    {
        var touched = new List<string>();
        if (!Directory.Exists(migrationsDir))
        {
            return touched;
        }

        foreach (string path in Directory.GetFiles(migrationsDir, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static p => p, StringComparer.Ordinal))
        {
            string fullPath = Path.GetFullPath(path);
            var info = new FileInfo(fullPath);
            if (!beforeSnapshot.TryGetValue(fullPath, out var previous) ||
                previous.Length != info.Length ||
                previous.LastWriteUtc != info.LastWriteTimeUtc)
            {
                touched.Add(Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/'));
            }
        }

        return touched;
    }

    private static int FormatTouchedMigrationFiles(
        string repoRoot,
        string projectPath,
        IReadOnlyList<string> relativeFiles,
        Func<string, string, IReadOnlyList<string>, int>? processRunner)
    {
        var whitespaceArgs = new List<string> { "format", "whitespace", ".", "--folder", "--include" };
        whitespaceArgs.AddRange(relativeFiles);
        int whitespaceExit = InvokeProcess(repoRoot, "dotnet", whitespaceArgs, processRunner);
        if (whitespaceExit != 0)
        {
            return whitespaceExit;
        }

        var styleArgs = new List<string> { "format", "style", projectPath, "--no-restore", "--include" };
        styleArgs.AddRange(relativeFiles);
        return InvokeProcess(repoRoot, "dotnet", styleArgs, processRunner);
    }

    private static bool HasConfiguredPostgresConnection(Func<string, string?>? getEnvironmentVariable = null)
    {
        var env = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        return (env(PrimaryConnectionEnvVar) is { Length: > 0 } primary && !string.IsNullOrWhiteSpace(primary)) ||
               (env(PostgresConnectionEnvVar) is { Length: > 0 } legacy && !string.IsNullOrWhiteSpace(legacy));
    }

    private static void AppendDesignTimeConnectionIfUnset(
        List<string> args,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (!HasConfiguredPostgresConnection(getEnvironmentVariable))
        {
            args.Add("--");
            args.Add("--connection");
            args.Add(DesignTimePlaceholderConnection);
        }
    }

    private static int InvokeProcess(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        Func<string, string, IReadOnlyList<string>, int>? processRunner)
    {
        if (processRunner is not null)
        {
            return processRunner(workingDirectory, fileName, arguments);
        }

        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            string dotnetTools = Path.Combine(home, ".dotnet", "tools");
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!currentPath.Split(Path.PathSeparator).Contains(dotnetTools, StringComparer.Ordinal))
            {
                psi.Environment["PATH"] = string.IsNullOrEmpty(currentPath)
                    ? dotnetTools
                    : $"{currentPath}{Path.PathSeparator}{dotnetTools}";
            }
        }

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                Console.Error.WriteLine($"[ERROR] Could not start '{fileName}'.");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to execute '{fileName}': {ex.Message}");
            return 1;
        }
    }

    private static string ResolveOutputDirectory(string repoRoot, string? outputDir)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            return Path.Combine(repoRoot, "TestResults", "migrations");
        }

        return Path.IsPathRooted(outputDir)
            ? outputDir
            : Path.GetFullPath(Path.Combine(repoRoot, outputDir));
    }

    private static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
