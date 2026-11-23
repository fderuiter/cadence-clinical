using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class DbCommands
{
    public static void ConfigureReset(Command command)
    {
        var allowOffline = new Option<bool>("--allow-offline")
        {
            Description = "Exit 0 with a warning when no local PostgreSQL connection is configured or reachable",
        };
        command.Options.Add(allowOffline);
        command.SetAction(result => Reset(result.GetValue(allowOffline)));
    }

    public static void ConfigureMigrate(Command command)
    {
        var target = CreateOptionalModuleArgument("Target persistence module name or 'all' (defaults to 'all')");
        command.Arguments.Add(target);
        command.SetAction(result => Migrate(result.GetValue(target) ?? "all"));
    }

    public static void ConfigureAdd(Command command)
    {
        var moduleArg = new Argument<string>("module")
        {
            Description = $"Single persistence module ({string.Join(", ", PersistenceModuleRegistry.AllNames)})",
        };
        moduleArg.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        moduleArg.Validators.Add(argResult =>
        {
            if (argResult.Tokens.Any(static token => string.Equals(token.Value, "all", StringComparison.OrdinalIgnoreCase)))
            {
                argResult.AddError("'cadence db add' requires a single persistence module name, not 'all'.");
            }
        });

        var nameArg = new Argument<string>("name")
        {
            Description = "PascalCase EF Core migration name (for example AddVisitWindowIndex)",
        };
        nameArg.Validators.Add(CheckCommand.RejectOptionLikeTokens);

        command.Arguments.Add(moduleArg);
        command.Arguments.Add(nameArg);
        command.SetAction(result => Add(result.GetRequiredValue(moduleArg), result.GetRequiredValue(nameArg)));
    }

    public static void ConfigureScript(Command command)
    {
        var target = CreateOptionalModuleArgument("Target persistence module name or 'all' (defaults to 'all')");
        var outputOption = new Option<string?>("--output-dir", "-o")
        {
            Description = $"Output directory for generated idempotent SQL files (default: {MigrationToolingService.DefaultScriptRelativeDir})",
        };
        outputOption.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        command.Arguments.Add(target);
        command.Options.Add(outputOption);
        command.SetAction(result => Script(result.GetValue(target) ?? "all", result.GetValue(outputOption)));
    }

    public static void ConfigureBundle(Command command)
    {
        var target = CreateOptionalModuleArgument("Target persistence module name or 'all' (defaults to 'all')");
        command.Arguments.Add(target);
        command.SetAction(result => Bundle(result.GetValue(target) ?? "all"));
    }

    public static void ConfigureList(Command command)
    {
        var target = CreateOptionalModuleArgument("Target persistence module name or 'all' (defaults to 'all')");
        command.Arguments.Add(target);
        command.SetAction(result => List(result.GetValue(target) ?? "all"));
    }

    public static void ConfigureOptimize(Command command)
    {
        var target = CreateOptionalModuleArgument("Target persistence module name or 'all' (defaults to 'all')");
        command.Arguments.Add(target);
        command.SetAction(result => Optimize(result.GetValue(target) ?? "all"));
    }

    public static void ConfigureSeed(Command command)
    {
        var target = CreateOptionalModuleArgument("Target persistence module name or 'all' (defaults to 'all')");
        command.Arguments.Add(target);
        command.SetAction(result => Seed(result.GetValue(target) ?? "all"));
    }

    public static int Reset(bool allowOffline = false) =>
        DatabaseResetService.ResetDatabasesAsync(allowOffline: allowOffline).GetAwaiter().GetResult();

    public static int Migrate(string target = "all")
    {
        if (!PersistenceModuleRegistry.TryResolveTargets(target, out var modules, out string? targetError))
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(targetError)}[/]");
            return 1;
        }

        if (!TryResolveOnlineConnectionString(out string? connStr, out string? connError))
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(connError)}[/]");
            return 1;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string? moduleFilter = modules.Count == 1 ? modules[0].Name : null;
        AnsiConsole.MarkupLine(
            moduleFilter is null
                ? "[cyan]Applying EF Core PostgreSQL migrations across all 18 persistence modules...[/]"
                : $"[cyan]Applying EF Core PostgreSQL migrations for module '{Markup.Escape(moduleFilter)}'...[/]");

        int exitCode = DatabaseResetService.RunDatabaseMigrator(connStr, repoRoot, moduleFilter);
        if (exitCode != 0)
        {
            AnsiConsole.MarkupLine($"[red]ERROR: Database migration failed (exit code {exitCode}).[/]");
            return exitCode;
        }

        AnsiConsole.MarkupLine("\n[bold green]All requested EF Core migrations applied cleanly.[/]");
        return 0;
    }

    internal static int Add(string moduleName, string migrationName)
    {
        if (string.Equals(moduleName, "all", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[red]ERROR: 'cadence db add' requires a single persistence module name, not 'all'.[/]");
            return 1;
        }

        if (!PersistenceModuleRegistry.TryGet(moduleName, out var module))
        {
            AnsiConsole.MarkupLine(
                $"[red]ERROR: Unknown persistence module '{Markup.Escape(moduleName)}'. Expected one of: {Markup.Escape(string.Join(", ", PersistenceModuleRegistry.AllNames))}.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(migrationName))
        {
            AnsiConsole.MarkupLine("[red]ERROR: Migration name is required for 'cadence db add'.[/]");
            return 1;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string projectFullPath = Path.Combine(repoRoot, module.ProjectPath);
        string projectDir = Path.GetDirectoryName(projectFullPath)!;
        string migrationsFullDir = Path.Combine(projectDir, module.MigrationsDir);

        HashSet<string> existingFiles = Directory.Exists(migrationsFullDir)
            ? Directory.GetFiles(migrationsFullDir, "*.cs", SearchOption.AllDirectories).ToHashSet(StringComparer.Ordinal)
            : [];

        AnsiConsole.MarkupLine(
            $"[cyan]Generating migration '{Markup.Escape(migrationName)}' for module '{Markup.Escape(module.Name)}'...[/]");

        int addExit = RunDotnetEf(
            repoRoot,
            ["migrations", "add", migrationName, "--project", module.ProjectPath, "--context", module.DbContextName, "--output-dir", module.MigrationsDir]);
        if (addExit != 0)
        {
            return addExit;
        }

        string[] touchedFiles = Directory.Exists(migrationsFullDir)
            ? Directory.GetFiles(migrationsFullDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !existingFiles.Contains(f) || f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
                .Select(f => Path.GetRelativePath(repoRoot, f))
                .ToArray()
            : [];

        if (touchedFiles.Length > 0)
        {
            AnsiConsole.MarkupLine($"[grey]Formatting {touchedFiles.Length} generated migration file(s)...[/]");
            int formatExit = FormatGeneratedMigrationFiles(repoRoot, module.ProjectPath, touchedFiles);
            if (formatExit != 0)
            {
                AnsiConsole.MarkupLine($"[red]ERROR: dotnet format failed on generated migration files (exit code {formatExit}).[/]");
                return formatExit;
            }
        }

        AnsiConsole.MarkupLine($"[green][[OK]] Migration '{Markup.Escape(migrationName)}' created in {Markup.Escape(module.MigrationsDir)}.[/]");
        return 0;
    }

    internal static int Script(string target = "all", string? outputDir = null)
    {
        if (!PersistenceModuleRegistry.TryResolveTargets(target, out var modules, out string? error))
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(error)}[/]");
            return 1;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        if (!TryPrepareMultiModuleBuild(repoRoot, modules.Count, out bool noBuild, out int buildExit))
        {
            return buildExit;
        }

        string resolvedOutputDir = MigrationToolingService.ResolveOutputDirectory(repoRoot, outputDir);
        Directory.CreateDirectory(resolvedOutputDir);

        foreach (var module in modules)
        {
            string outputFile = Path.Combine(resolvedOutputDir, $"{module.Name}.sql");
            AnsiConsole.MarkupLine($"[cyan]==> [[{Markup.Escape(module.Name)}]] Writing idempotent SQL script...[/]");

            List<string> efArgs = ["migrations", "script", "--idempotent", "--project", module.ProjectPath, "--context", module.DbContextName, "--output", outputFile];
            if (noBuild)
            {
                efArgs.Add("--no-build");
            }

            int exitCode = RunDotnetEf(repoRoot, efArgs);
            if (exitCode != 0)
            {
                return exitCode;
            }

            AnsiConsole.MarkupLine($"[green][[OK]] Wrote {Markup.Escape(Path.GetRelativePath(repoRoot, outputFile))}[/]");
        }

        return 0;
    }

    internal static int Bundle(string target = "all")
    {
        if (!PersistenceModuleRegistry.TryResolveTargets(target, out var modules, out string? error))
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(error)}[/]");
            return 1;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string outputDir = Path.Combine(repoRoot, MigrationToolingService.DefaultScriptRelativeDir);
        Directory.CreateDirectory(outputDir);

        foreach (var module in modules)
        {
            string outputFile = Path.Combine(outputDir, $"{module.Name}_migration_bundle");
            AnsiConsole.MarkupLine($"[cyan]==> [[{Markup.Escape(module.Name)}]] Building self-contained migration bundle...[/]");

            int exitCode = RunDotnetEf(
                repoRoot,
                ["migrations", "bundle", "--project", module.ProjectPath, "--context", module.DbContextName, "--output", outputFile, "--force"]);
            if (exitCode != 0)
            {
                return exitCode;
            }

            AnsiConsole.MarkupLine($"[green][[OK]] Bundle written to {Markup.Escape(Path.GetRelativePath(repoRoot, outputFile))}[/]");
        }

        return 0;
    }

    internal static int List(string target = "all")
    {
        if (!PersistenceModuleRegistry.TryResolveTargets(target, out var modules, out string? targetError))
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(targetError)}[/]");
            return 1;
        }

        if (!TryResolveOnlineConnectionString(out string? connStr, out string? connError))
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(connError)}[/]");
            return 1;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        if (!TryPrepareMultiModuleBuild(repoRoot, modules.Count, out bool noBuild, out int buildExit))
        {
            return buildExit;
        }

        foreach (var module in modules)
        {
            AnsiConsole.MarkupLine($"\n[bold cyan]==> [[{Markup.Escape(module.Name)}]] schema '{Markup.Escape(module.Schema)}'[/]");
            List<string> efArgs = ["migrations", "list", "--project", module.ProjectPath, "--context", module.DbContextName];
            if (noBuild)
            {
                efArgs.Add("--no-build");
            }

            int exitCode = RunDotnetEf(repoRoot, efArgs, connStr);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        return 0;
    }

    internal static int Optimize(string target = "all")
    {
        if (!PersistenceModuleRegistry.TryResolveTargets(target, out var modules, out string? error))
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(error)}[/]");
            return 1;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        if (!TryPrepareMultiModuleBuild(repoRoot, modules.Count, out bool noBuild, out int buildExit))
        {
            return buildExit;
        }

        foreach (var module in modules)
        {
            AnsiConsole.MarkupLine($"[cyan]==> [[{Markup.Escape(module.Name)}]] Generating compiled EF Core model...[/]");
            List<string> efArgs =
            [
                "dbcontext",
                "optimize",
                "--project",
                module.ProjectPath,
                "--context",
                module.DbContextName,
                "--output-dir",
                "CompiledModels",
                "--namespace",
                module.CompiledModelsNamespace,
            ];
            if (noBuild)
            {
                efArgs.Add("--no-build");
            }

            int exitCode = RunDotnetEf(repoRoot, efArgs);
            if (exitCode != 0)
            {
                return exitCode;
            }

            AnsiConsole.MarkupLine($"[green][[OK]] Compiled model generated for {Markup.Escape(module.Name)}.[/]");
        }

        return 0;
    }

    internal static int Seed(string target = "all")
    {
        if (!PersistenceModuleRegistry.TryResolveTargets(target, out var modules, out string? targetError))
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(targetError)}[/]");
            return 1;
        }

        if (!TryResolveOnlineConnectionString(out string? connStr, out string? connError))
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(connError)}[/]");
            return 1;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        int skippedCount = 0;

        foreach (var module in modules)
        {
            if (module.SupportsSeed)
            {
                AnsiConsole.MarkupLine($"[cyan]==> [[{Markup.Escape(module.Name)}]] Running deterministic --seed entrypoint...[/]");
                int exitCode = RunProcess(
                    repoRoot,
                    "dotnet",
                    ["run", "--project", module.ProjectPath, "--", "--seed"],
                    connStr);
                if (exitCode != 0)
                {
                    AnsiConsole.MarkupLine($"[red]ERROR: Seeding failed for '{Markup.Escape(module.Name)}' (exit code {exitCode}).[/]");
                    return exitCode;
                }
            }
            else if (string.Equals(module.Name, "kernel", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine(
                    "[grey]==> [[kernel]] Kernel tenant and demo study bootstrap runs on host startup via DevelopmentTenantBootstrap " +
                    "(apps/kernel-api/src/DevelopmentTenantBootstrap.cs): start apps/kernel-api in Development with " +
                    "Kernel__DevelopmentTenant__Code=cadence-local and Kernel__DevelopmentTenant__DemoData=true.[/]");
            }
            else if (modules.Count == 1)
            {
                AnsiConsole.MarkupLine(
                    $"[grey]==> [[{Markup.Escape(module.Name)}]] Module has no standalone --seed entrypoint; skipping cleanly.[/]");
            }
            else
            {
                skippedCount++;
            }
        }

        if (skippedCount > 0)
        {
            AnsiConsole.MarkupLine(
                $"[grey]Skipped {skippedCount} service/module host(s) that have no standalone --seed CLI entrypoint.[/]");
        }

        return 0;
    }

    internal static bool TryResolveOnlineConnectionString(
        [NotNullWhen(true)] out string? connectionString,
        [NotNullWhen(false)] out string? error,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        try
        {
            connectionString = DatabaseResetService.ResolvePostgresConnectionString(getEnvironmentVariable);
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            connectionString = null;
            error = ex.Message;
            return false;
        }
    }

    private static int FormatGeneratedMigrationFiles(string repoRoot, string projectPath, string[] relativeFilePaths)
    {
        string[] subcommands = ["whitespace", "style"];
        foreach (string subcommand in subcommands)
        {
            var args = new List<string>(6 + relativeFilePaths.Length)
            {
                "format",
                subcommand,
                projectPath,
                "--no-restore",
                "--include",
            };
            args.AddRange(relativeFilePaths);

            int exitCode = RunProcess(repoRoot, "dotnet", args);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        return 0;
    }

    private static bool TryPrepareMultiModuleBuild(string repoRoot, int moduleCount, out bool noBuild, out int buildExit)
    {
        if (moduleCount <= 1)
        {
            noBuild = false;
            buildExit = 0;
            return true;
        }

        AnsiConsole.MarkupLine("[grey]Building solution once before multi-module EF Core workflow...[/]");
        buildExit = RunProcess(repoRoot, "dotnet", ["build", "Cadence.sln"]);
        noBuild = buildExit == 0;
        return noBuild;
    }

    private static int RunDotnetEf(string repoRoot, IReadOnlyList<string> efArguments, string? onlineConnectionString = null)
    {
        var args = new List<string>(3 + efArguments.Count) { "tool", "run", "dotnet-ef" };
        args.AddRange(efArguments);
        return RunProcess(repoRoot, "dotnet", args, onlineConnectionString);
    }

    private static int RunProcess(
        string repoRoot,
        string fileName,
        IReadOnlyList<string> arguments,
        string? onlineConnectionString = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(onlineConnectionString))
        {
            psi.Environment["ConnectionStrings__cadence"] = onlineConnectionString;
            psi.Environment["POSTGRES_CONNECTION_STRING"] = onlineConnectionString;
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static Argument<string?> CreateOptionalModuleArgument(string description)
    {
        var arg = new Argument<string?>("module")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = description,
        };
        arg.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        return arg;
    }
}
