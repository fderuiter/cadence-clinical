using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public sealed record CliCommandEntry(string CommandPath, string Kind, string Description);

public static partial class CliDocsGeneratorService
{
    public const string CommandsDocRelativePath = "docs/cli-commands.md";

    // Matches the registrations in packages/cli/src/Program.cs: AddCommand(parent, "name", ...) and AddBranch(parent, "name", ...).
    [GeneratedRegex(@"Add(?:Command|Branch)\(\s*\w+,\s*""([^""]+)""")]
    private static partial Regex CommandRegistrationRegex();

    [GeneratedRegex(@"\b(?:5173|7687|8017|8019)\b")]
    private static partial Regex RetiredCliPortRegex();

    [GeneratedRegex(@"(?:var|Command)\s+(\w+)\s*=\s*AddBranch\(\s*(\w+),\s*""((?:\\.|[^""\\])*)""\s*,\s*""((?:\\.|[^""\\])*)""\s*\)")]
    private static partial Regex BranchAssignmentRegex();

    [GeneratedRegex(@"Add(Command|Branch)\(\s*(\w+),\s*""((?:\\.|[^""\\])*)""\s*,\s*""((?:\\.|[^""\\])*)""")]
    private static partial Regex RegistrationDetailRegex();

    public static int GenerateCliDocs(bool check = false, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating CLI documentation (docs/cli.md and docs/cli-commands.md)...");

        string cliMdPath = Path.Combine(repoRoot, "docs", "cli.md");
        string commandsMdPath = Path.Combine(repoRoot, CommandsDocRelativePath);
        string programPath = Path.Combine(repoRoot, "packages", "cli", "src", "Program.cs");

        if (!File.Exists(cliMdPath))
        {
            if (check)
            {
                Console.WriteLine($"[ERROR] CLI documentation not found at {cliMdPath}");
                return 1;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cliMdPath)!);
            File.WriteAllText(cliMdPath, "# Cadence CLI Documentation\n\nCommand line tool reference.\n");
        }

        if (!check)
        {
            if (File.Exists(programPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(commandsMdPath)!);
                File.WriteAllText(commandsMdPath, RenderCommandsDoc(File.ReadAllText(programPath)));
            }

            Console.WriteLine("✔ CLI documentation generated cleanly.");
            return 0;
        }

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

        if (content.Contains("until 2027", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[ERROR] docs/cli.md contains stale 'until 2027' GitHub Actions claim.");
            passed = false;
        }

        if (File.Exists(programPath))
        {
            string programSource = File.ReadAllText(programPath);
            string expectedInventory = RenderCommandsDoc(programSource);
            string inventoryContent = string.Empty;

            if (!File.Exists(commandsMdPath))
            {
                Console.WriteLine($"[ERROR] {CommandsDocRelativePath} not found; run 'pnpm cadence generate-cli-docs' to generate it.");
                passed = false;
            }
            else
            {
                inventoryContent = File.ReadAllText(commandsMdPath).ReplaceLineEndings("\n");
                if (!string.Equals(inventoryContent, expectedInventory, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[ERROR] {CommandsDocRelativePath} does not match packages/cli/src/Program.cs. Run 'pnpm cadence generate-cli-docs'.");
                    passed = false;
                }
            }

            string combinedDocs = string.IsNullOrEmpty(inventoryContent) ? content : content + "\n" + inventoryContent;
            foreach (var command in FindUndocumentedCommands(programSource, combinedDocs))
            {
                Console.WriteLine($"[ERROR] CLI command '{command}' is registered in Program.cs but not documented in docs/cli.md or {CommandsDocRelativePath}");
                passed = false;
            }

            string expectedPorts = PortCheckerService.FormatPortRanges(PortCheckerService.DefaultPorts);
            if (!content.Contains(expectedPorts, StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] docs/cli.md does not match PortCheckerService.DefaultPorts ('{expectedPorts}').");
                passed = false;
            }

            var retiredMatch = RetiredCliPortRegex().Match(content);
            if (retiredMatch.Success)
            {
                Console.WriteLine($"[ERROR] docs/cli.md references retired port '{retiredMatch.Value}'.");
                passed = false;
            }
        }

        if (!passed) return 1;

        Console.WriteLine("✔ CLI documentation parity check passed cleanly.");
        return 0;
    }

    public static IReadOnlyList<CliCommandEntry> ExtractRegisteredCommands(string programSource)
    {
        var branchPaths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["root"] = "cadence",
        };

        foreach (Match branchMatch in BranchAssignmentRegex().Matches(programSource))
        {
            string variableName = branchMatch.Groups[1].Value;
            string parentVariable = branchMatch.Groups[2].Value;
            string branchName = UnescapeCSharpString(branchMatch.Groups[3].Value);
            string parentPath = branchPaths.GetValueOrDefault(parentVariable, "cadence");
            branchPaths[variableName] = $"{parentPath} {branchName}";
        }

        var entries = new List<CliCommandEntry>();
        foreach (Match match in RegistrationDetailRegex().Matches(programSource))
        {
            string kind = match.Groups[1].Value;
            string parentVariable = match.Groups[2].Value;
            string name = UnescapeCSharpString(match.Groups[3].Value);
            string description = UnescapeCSharpString(match.Groups[4].Value);
            string parentPath = branchPaths.GetValueOrDefault(parentVariable, "cadence");
            entries.Add(new CliCommandEntry($"{parentPath} {name}", kind, description));
        }

        return entries;
    }

    public static string RenderCommandsDoc(string programSource)
    {
        var entries = ExtractRegisteredCommands(programSource);
        var sb = new StringBuilder();
        sb.Append("# CLI Commands Inventory\n\n");
        sb.Append("Generated from `packages/cli/src/Program.cs` by `pnpm cadence generate-cli-docs`. Don't edit this file by hand. ");
        sb.Append("See [CLI Reference](cli.md) for usage examples and options.\n\n");
        sb.Append("| Command | Kind | Description |\n");
        sb.Append("| --- | --- | --- |\n");
        foreach (var entry in entries)
        {
            sb.Append("| `")
              .Append(EscapeCell(entry.CommandPath))
              .Append("` | ")
              .Append(EscapeCell(entry.Kind))
              .Append(" | ")
              .Append(EscapeCell(entry.Description))
              .Append(" |\n");
        }

        return sb.ToString();
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

    private static string UnescapeCSharpString(string raw) =>
        raw.Replace("\\\"", "\"", StringComparison.Ordinal)
           .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static string EscapeCell(string cell) =>
        cell.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

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
