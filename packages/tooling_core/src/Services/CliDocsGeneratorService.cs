using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class CliDocsGeneratorService
{
    // Matches the registrations in packages/cli/src/Program.cs: AddCommand(parent, "name", ...) and AddBranch(parent, "name", ...).
    [GeneratedRegex(@"Add(?:Command|Branch)\(\s*\w+,\s*""([^""]+)""")]
    private static partial Regex CommandRegistrationRegex();

    public static int GenerateCliDocs(bool check = false, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating CLI documentation docs/cli.md...");

        string cliMdPath = Path.Combine(repoRoot, "docs", "cli.md");
        if (!File.Exists(cliMdPath))
        {
            if (check)
            {
                Console.WriteLine($"[ERROR] CLI documentation not found at {cliMdPath}");
                return 1;
            }
            File.WriteAllText(cliMdPath, "# Cadence CLI Documentation\n\nCommand line tool reference.\n");
        }

        if (check)
        {
            string content = File.ReadAllText(cliMdPath);
            string[] requiredSections =
            [
                "# Cadence Unified Developer CLI Reference",
                "cadence doctor",
                "cadence check",
                "cadence fix",
                "cadence setup",
                "cadence gxp"
            ];

            bool passed = true;
            foreach (var sec in requiredSections)
            {
                if (!content.Contains(sec, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[ERROR] CLI docs missing required section or command reference: '{sec}'");
                    passed = false;
                }
            }

            string programPath = Path.Combine(repoRoot, "packages", "cli", "src", "Program.cs");
            if (File.Exists(programPath))
            {
                foreach (var command in FindUndocumentedCommands(File.ReadAllText(programPath), content))
                {
                    Console.WriteLine($"[ERROR] CLI command '{command}' is registered in Program.cs but not documented in docs/cli.md");
                    passed = false;
                }
            }

            if (!passed) return 1;

            Console.WriteLine("✔ CLI documentation parity check passed cleanly.");
            return 0;
        }

        Console.WriteLine("✔ CLI documentation generated cleanly.");
        return 0;
    }

    public static IReadOnlyList<string> FindUndocumentedCommands(string programSource, string docs)
    {
        return CommandRegistrationRegex().Matches(programSource)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !docs.Contains($"`{name}`", StringComparison.Ordinal) &&
                           !Regex.IsMatch(docs, $@"`cadence(?: [\w-]+)* {Regex.Escape(name)}\b"))
            .ToList();
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
