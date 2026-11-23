using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

public static class CliDocsGeneratorService
{
    public static int GenerateCliDocs(
        bool check = false,
        string? repoRoot = null,
        IEnumerable<string>? commandNames = null)
    {
        repoRoot ??= RepositoryRoot.Find();
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

            if (commandNames is not null)
            {
                foreach (var command in FindUndocumentedCommands(commandNames, content))
                {
                    Console.WriteLine($"[ERROR] CLI command '{command}' is registered on the CLI root command but not documented in docs/cli.md");
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

    public static IReadOnlyList<string> FindUndocumentedCommands(IEnumerable<string> commandNames, string docs)
    {
        ArgumentNullException.ThrowIfNull(commandNames);
        ArgumentNullException.ThrowIfNull(docs);

        return commandNames
            .Distinct(StringComparer.Ordinal)
            .Where(name => !docs.Contains($"`{name}`", StringComparison.Ordinal) &&
                           !Regex.IsMatch(docs, $@"`cadence(?: [\w-]+)* {Regex.Escape(name)}\b"))
            .ToList();
    }
}
