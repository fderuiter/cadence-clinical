using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Spectre.Console;
using Spectre.Console.Cli;

using Cadence.Testing.Graph;

namespace Cadence.Cli.Commands;

public class TestCommand : Command<TestCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-f|--fast")]
        [Description("Run fast unit tests excluding integration tests")]
        public bool Fast { get; set; }

        [CommandOption("-i|--integration")]
        [Description("Run integration test suites only")]
        public bool Integration { get; set; }

        [CommandOption("-a|--affected")]
        [Description("Run xUnit tests affected by recent source changes using Roslyn static analysis")]
        public bool Affected { get; set; }

        [CommandOption("-s|--service")]
        [Description("Filter tests by service name (e.g. execution, ctms)")]
        public string? Service { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        string filter = "";
        if (settings.Fast)
        {
            filter = "--filter \"Category!=Integration\"";
        }
        else if (settings.Integration)
        {
            filter = "--filter \"Category=Integration\"";
        }

        string repoRoot = Cadence.Cli.Services.QualityGateOrchestrator.FindRepoRoot();

        if (settings.Affected)
        {
            AnsiConsole.MarkupLine("[cyan]Resolving affected xUnit test suites via Roslyn static code analysis...[/]");
            var graph = new TestDependencyGraph(repoRoot);
            graph.LoadOrBuild();

            // Find modified C# files via git diff or scan
            var changedFiles = GetGitModifiedFiles(repoRoot);
            if (changedFiles.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No modified C# files detected. Running full test suite as fallback.[/]");
            }
            else
            {
                var affected = graph.ResolveAffectedTests(changedFiles);
                AnsiConsole.MarkupLine($"[green]Identified {affected.Count} affected test target(s):[/]");
                foreach (var t in affected)
                {
                    AnsiConsole.MarkupLine($"  [dim]- {t}[/]");
                }

                if (affected.Count > 0)
                {
                    string targets = string.Join(" ", affected.Select(a => $"\"{a}\""));
                    var psiAffected = new ProcessStartInfo("dotnet", $"test {targets} {filter}".Trim())
                    {
                        WorkingDirectory = repoRoot,
                        UseShellExecute = false
                    };

                    using var procAffected = Process.Start(psiAffected);
                    if (procAffected == null) return 1;
                    procAffected.WaitForExit();
                    return procAffected.ExitCode;
                }
            }
        }

        if (!string.IsNullOrEmpty(settings.Service))
        {
            string serviceDir = Path.Combine(repoRoot, "tests", Capitalize(settings.Service));
            if (Directory.Exists(serviceDir))
            {
                AnsiConsole.MarkupLine($"[cyan]Executing .NET Test Suites for service '{settings.Service}'...[/]\n");
                var psiService = new ProcessStartInfo("dotnet", $"test \"{serviceDir}\" {filter}".Trim())
                {
                    WorkingDirectory = repoRoot,
                    UseShellExecute = false
                };

                using var procService = Process.Start(psiService);
                if (procService == null) return 1;
                procService.WaitForExit();
                return procService.ExitCode;
            }
        }

        AnsiConsole.MarkupLine($"[cyan]Executing .NET Test Suites {(string.IsNullOrEmpty(filter) ? "(All)" : filter)}...[/]\n");

        string slnPath = Path.Combine(repoRoot, "Cadence.sln");

        var psi = new ProcessStartInfo("dotnet", $"test \"{slnPath}\" {filter}".Trim())
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        if (proc == null) return 1;
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static System.Collections.Generic.List<string> GetGitModifiedFiles(string repoRoot)
    {
        var result = new System.Collections.Generic.List<string>();
        try
        {
            var psi = new ProcessStartInfo("git", "status --porcelain")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length > 3)
                    {
                        string file = line[3..].Trim();
                        if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(file);
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback if git command fails
        }

        return result;
    }

    private static string Capitalize(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        return char.ToUpperInvariant(str[0]) + str[1..];
    }
}
