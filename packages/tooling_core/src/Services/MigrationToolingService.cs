using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Unified EF Core migration workflow across all 18 modules in <see cref="PersistenceModuleCatalog"/>.
/// Generates migrations, writes idempotent review SQL to untracked <c>TestResults/migrations/</c>, lists migrations,
/// and verifies snapshots, <c>.Designer.cs</c> companions, attributes, and DDL uniqueness via <c>Cadence.SchemaDocExporter</c>.
/// </summary>
public static class MigrationToolingService
{
    public const string DefaultScriptRelativeDir = "TestResults/migrations";
    public const string DesignTimePlaceholderConnection = "Host=localhost;Database=cadence_migrations";
    private const string ExporterProject = "packages/tooling_core/src/Cadence.SchemaDocExporter/Cadence.SchemaDocExporter.csproj";
    private const string PrimaryConnectionEnvVar = "ConnectionStrings__cadence";
    private const string PostgresConnectionEnvVar = "POSTGRES_CONNECTION_STRING";

    /// <summary>Runs <c>dotnet ef migrations add</c> for a single module in <see cref="PersistenceModuleCatalog"/>.</summary>
    public static int Add(
        string module,
        string migrationName,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null)
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
        };
        AppendDesignTimeConnectionIfUnset(args);

        Console.WriteLine($"Generating migration '{migrationName.Trim()}' for module '{target.Module}' ({target.DbContextName}, schema '{target.Schema}')...");
        int exitCode = InvokeProcess(repoRoot, "dotnet", args, processRunner);
        if (exitCode == 0)
        {
            Console.WriteLine($"✔ Migration '{migrationName.Trim()}' created under {target.GetMigrationsDirectory(repoRoot)}.");
        }

        return exitCode;
    }

    /// <summary>
    /// Runs <c>dotnet ef migrations script --idempotent</c> for one module or all 18 modules, writing SQL artifacts to
    /// <c>TestResults/migrations/&lt;module&gt;.sql</c> (or <paramref name="outputDir"/>) outside the tracked source tree.
    /// </summary>
    public static int Script(
        string? moduleOrAll = null,
        string? outputDir = null,
        string? repoRoot = null,
        Func<string, string, IReadOnlyList<string>, int>? processRunner = null)
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
            };
            AppendDesignTimeConnectionIfUnset(args);

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
    /// When <c>ConnectionStrings__cadence</c> and <c>POSTGRES_CONNECTION_STRING</c> are unset, offline mode (<c>--no-connect</c>)
    /// is used for single-module queries and on-disk <c>.Designer.cs</c> inspection is used when listing all 18 modules without a database.
    /// </summary>
    public static int List(
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
        bool hasLiveConnection = HasConfiguredPostgresConnection();

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
            };
            if (!hasLiveConnection)
            {
                args.Add("--no-connect");
                AppendDesignTimeConnectionIfUnset(args);
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

    private static string? GetConfiguredPostgresConnection()
    {
        if (Environment.GetEnvironmentVariable(PrimaryConnectionEnvVar) is { Length: > 0 } primary)
        {
            return primary;
        }

        return Environment.GetEnvironmentVariable(PostgresConnectionEnvVar) is { Length: > 0 } legacy
            ? legacy
            : null;
    }

    private static bool HasConfiguredPostgresConnection() =>
        GetConfiguredPostgresConnection() is not null;

    private static void AppendDesignTimeConnectionIfUnset(List<string> args)
    {
        if (!HasConfiguredPostgresConnection())
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

        if (GetConfiguredPostgresConnection() is { Length: > 0 } configuredConn)
        {
            psi.Environment[PrimaryConnectionEnvVar] = configuredConn;
            psi.Environment[PostgresConnectionEnvVar] = configuredConn;
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

    public static string ResolveOutputDirectory(string repoRoot, string? outputDir)
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
