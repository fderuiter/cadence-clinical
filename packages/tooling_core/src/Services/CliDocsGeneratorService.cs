using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class CliDocsGeneratorService
{
    // Matches the registrations in packages/cli/src/Program.cs: AddCommand(parent, "name", ...) and AddBranch(parent, "name", ...).
    [GeneratedRegex(@"Add(?:Command|Branch)\(\s*\w+,\s*""([^""]+)""")]
    private static partial Regex CommandRegistrationRegex();

    public static int GenerateCliDocs(bool check = false, string? repoRoot = null)
    {
        return GenerateCliDocs(rootCommand: null, check: check, repoRoot: repoRoot);
    }

    public static int GenerateCliDocs(Command? rootCommand, bool check = false, string? repoRoot = null)
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
            File.WriteAllText(cliMdPath, "# Cadence Unified Developer CLI Reference\n\nCommand line tool reference.\n");
        }

        string content = File.ReadAllText(cliMdPath);

        if (check)
        {
            var errors = new List<string>();

            // 1. Required top-level sections
            string[] requiredSections =
            [
                "# Cadence Unified Developer CLI Reference",
                "cadence doctor",
                "cadence check",
                "cadence fix",
                "cadence setup",
                "cadence gxp"
            ];

            foreach (var sec in requiredSections)
            {
                if (!content.Contains(sec, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"CLI docs missing required section or command reference: '{sec}'");
                }
            }

            // 2. Validate using live Command tree if provided, else fallback to Program.cs regex check
            if (rootCommand != null)
            {
                ValidateCommandTree(rootCommand, content, errors);
            }
            else
            {
                string programPath = Path.Combine(repoRoot, "packages", "cli", "src", "Program.cs");
                if (File.Exists(programPath))
                {
                    foreach (var command in FindUndocumentedCommands(File.ReadAllText(programPath), content))
                    {
                        errors.Add($"CLI command '{command}' is registered in Program.cs but not documented in docs/cli.md");
                    }
                }
            }

            if (errors.Count > 0)
            {
                foreach (var err in errors)
                {
                    Console.WriteLine($"[ERROR] {err}");
                }
                return 1;
            }

            Console.WriteLine("✔ CLI documentation parity check passed cleanly.");
            return 0;
        }

        // Auto-generation mode (check: false)
        if (rootCommand != null)
        {
            string updatedContent = SyncCliDocsMarkdown(rootCommand, content);
            if (!string.Equals(updatedContent, content, StringComparison.Ordinal))
            {
                File.WriteAllText(cliMdPath, updatedContent);
                Console.WriteLine($"Updated {cliMdPath} with current CLI option and argument definitions.");
            }
        }

        Console.WriteLine("✔ CLI documentation generated cleanly.");
        return 0;
    }

    public static List<string> ValidateCommandTree(Command rootCommand, string docsContent)
    {
        var errors = new List<string>();
        TraverseAndValidate(rootCommand, parentPath: "", docsContent, errors);
        return errors;
    }

    public static void ValidateCommandTree(Command rootCommand, string docsContent, List<string> errors)
    {
        TraverseAndValidate(rootCommand, parentPath: "", docsContent, errors);
    }

    private static void TraverseAndValidate(Command command, string parentPath, string docsContent, List<string> errors)
    {
        string fullPath = ResolveFullPath(command, parentPath);

        bool isRoot = command is RootCommand || string.Equals(fullPath, "cadence", StringComparison.OrdinalIgnoreCase);

        if (!isRoot)
        {
            if (!IsCommandDocumented(command, fullPath, docsContent))
            {
                errors.Add($"CLI command '{fullPath}' is registered in Program.cs but not documented in docs/cli.md");
            }

            string sectionText = GetSectionContent(docsContent, fullPath, command.Name);

            // Validate Options & Aliases
            foreach (var opt in command.Options)
            {
                var allAliases = opt.Aliases.Prepend(opt.Name).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.Ordinal);
                foreach (var alias in allAliases)
                {
                    if (!IsOptionAliasDocumented(alias, sectionText))
                    {
                        errors.Add($"Option alias '{alias}' for command '{fullPath}' is not documented in docs/cli.md");
                    }
                }

                if (opt.HasDefaultValue)
                {
                    object? defVal = GetDefaultValueSafe(opt);
                    if (defVal != null)
                    {
                        string defValStr = defVal.ToString() ?? "";
                        if (!string.IsNullOrEmpty(defValStr) &&
                            !string.Equals(defValStr, "False", StringComparison.OrdinalIgnoreCase) &&
                            !sectionText.Contains(defValStr, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"Default value '{defValStr}' for option '{opt.Name}' in command '{fullPath}' is not documented in docs/cli.md");
                        }
                    }
                }
            }

            // Validate Arguments
            foreach (var arg in command.Arguments)
            {
                if (!IsArgumentDocumented(arg.Name, sectionText))
                {
                    errors.Add($"Argument '{arg.Name}' for command '{fullPath}' is not documented in docs/cli.md");
                }

                if (arg.HasDefaultValue)
                {
                    object? defVal = GetDefaultValueSafe(arg);
                    if (defVal != null)
                    {
                        string defValStr = defVal.ToString() ?? "";
                        if (!string.IsNullOrEmpty(defValStr) && !sectionText.Contains(defValStr, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"Default value '{defValStr}' for argument '{arg.Name}' in command '{fullPath}' is not documented in docs/cli.md");
                        }
                    }
                }
            }
        }

        foreach (var sub in command.Subcommands)
        {
            TraverseAndValidate(sub, fullPath, docsContent, errors);
        }
    }

    private static bool IsOptionAliasDocumented(string alias, string sectionText)
    {
        string pattern = $@"(?<![\w-]|\-\-){Regex.Escape(alias)}(?![\w-])";
        return Regex.IsMatch(sectionText, pattern);
    }

    private static bool IsArgumentDocumented(string argName, string sectionText)
    {
        if (sectionText.Contains($"[{argName}]", StringComparison.OrdinalIgnoreCase) ||
            sectionText.Contains($"<{argName}>", StringComparison.OrdinalIgnoreCase) ||
            sectionText.Contains($"`{argName}`", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var regex = new Regex($@"\b{Regex.Escape(argName)}\b", RegexOptions.IgnoreCase);
        return regex.IsMatch(sectionText);
    }

    private static string ResolveFullPath(Command command, string parentPath)
    {
        if (string.IsNullOrEmpty(parentPath))
        {
            return "cadence";
        }

        return $"{parentPath} {command.Name}";
    }

    private static bool IsCommandDocumented(Command command, string fullPath, string docsContent)
    {
        if (docsContent.Contains($"`{fullPath}`", StringComparison.OrdinalIgnoreCase) ||
            docsContent.Contains($"`{command.Name}`", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var regex = new Regex($@"^#+\s+.*?\b{Regex.Escape(command.Name)}\b", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return regex.IsMatch(docsContent);
    }

    private static string GetSectionContent(string docsContent, string fullPath, string commandName)
    {
        string[] lines = docsContent.Split(["\r\n", "\n"], StringSplitOptions.None);
        int startIndex = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith('#') && (line.Contains(fullPath, StringComparison.OrdinalIgnoreCase) || line.Contains($"`{commandName}`", StringComparison.OrdinalIgnoreCase) || line.Contains($"cadence {commandName}", StringComparison.OrdinalIgnoreCase)))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex != -1)
        {
            int startHeaderLevel = GetHeaderLevel(lines[startIndex]);
            var sectionBuilder = new StringBuilder();
            sectionBuilder.AppendLine(lines[startIndex]);

            for (int i = startIndex + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith('#'))
                {
                    int currentLevel = GetHeaderLevel(line);
                    if (currentLevel <= startHeaderLevel) break;
                }
                sectionBuilder.AppendLine(line);
            }

            return sectionBuilder.ToString();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('|') && lines[i].Contains($"`{commandName}`", StringComparison.OrdinalIgnoreCase))
            {
                return lines[i];
            }
        }

        return string.Empty;
    }

    private static int GetHeaderLevel(string line)
    {
        int level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }
        return level;
    }

    private static object? GetDefaultValueSafe(Option opt)
    {
        try
        {
            return opt.GetDefaultValue();
        }
        catch
        {
            return null;
        }
    }

    private static object? GetDefaultValueSafe(Argument arg)
    {
        try
        {
            return arg.GetDefaultValue();
        }
        catch
        {
            return null;
        }
    }

    public static string SyncCliDocsMarkdown(Command rootCommand, string markdown)
    {
        string[] lines = markdown.Split(["\r\n", "\n"], StringSplitOptions.None);
        var resultLines = new List<string>(lines);

        TraverseAndSync(rootCommand, parentPath: "", resultLines);

        return string.Join("\n", resultLines);
    }

    private static void TraverseAndSync(Command command, string parentPath, List<string> resultLines)
    {
        string fullPath = ResolveFullPath(command, parentPath);

        bool isRoot = command is RootCommand || string.Equals(fullPath, "cadence", StringComparison.OrdinalIgnoreCase);

        if (!isRoot && (command.Options.Count > 0 || command.Arguments.Count > 0))
        {
            SyncCommandTable(command, fullPath, resultLines);
        }

        foreach (var sub in command.Subcommands)
        {
            TraverseAndSync(sub, fullPath, resultLines);
        }
    }

    private static void SyncCommandTable(Command command, string fullPath, List<string> lines)
    {
        int headerIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.StartsWith('#') && (line.Contains(fullPath, StringComparison.OrdinalIgnoreCase) || line.Contains($"`{command.Name}`", StringComparison.OrdinalIgnoreCase)))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex == -1) return;

        int headerLevel = GetHeaderLevel(lines[headerIndex]);
        int sectionEndIndex = lines.Count;
        for (int i = headerIndex + 1; i < lines.Count; i++)
        {
            if (lines[i].StartsWith('#') && GetHeaderLevel(lines[i]) <= headerLevel)
            {
                sectionEndIndex = i;
                break;
            }
        }

        int tableStartIndex = -1;
        int tableEndIndex = -1;
        for (int i = headerIndex + 1; i < sectionEndIndex; i++)
        {
            if (lines[i].TrimStart().StartsWith('|'))
            {
                if (tableStartIndex == -1) tableStartIndex = i;
                tableEndIndex = i;
            }
            else if (tableStartIndex != -1)
            {
                break;
            }
        }

        if (tableStartIndex != -1)
        {
            for (int i = tableStartIndex; i <= tableEndIndex; i++)
            {
                foreach (var opt in command.Options.Where(o => o.Aliases.Prepend(o.Name).Any(alias => lines[i].Contains(alias, StringComparison.Ordinal))))
                {
                    var allAliases = opt.Aliases.Prepend(opt.Name).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.Ordinal);
                    bool allAliasesPresent = allAliases.All(a => IsOptionAliasDocumented(a, lines[i]));
                    if (!allAliasesPresent)
                    {
                        string formattedOptionCol = FormatOptionColumn(opt);
                        string[] parts = lines[i].Split('|');
                        if (parts.Length >= 3)
                        {
                            parts[1] = $" {formattedOptionCol} ";
                            lines[i] = string.Join("|", parts);
                        }
                    }
                }
            }
        }
        else
        {
            // If arguments/options are already in section title/narrative (e.g. scaffold service <name>), skip table creation
            string sectionText = string.Join("\n", lines.Skip(headerIndex).Take(sectionEndIndex - headerIndex));
            bool optionsMissing = command.Options.Any(o => o.Aliases.Prepend(o.Name).Any(a => !IsOptionAliasDocumented(a, sectionText)));
            bool argsMissing = command.Arguments.Any(a => !IsArgumentDocumented(a.Name, sectionText));

            if (optionsMissing || argsMissing)
            {
                var tableLines = new List<string>
                {
                    "",
                    "| Option / Argument | Description |",
                    "| --- | --- |"
                };

                foreach (var arg in command.Arguments)
                {
                    tableLines.Add($"| `{FormatArgumentColumn(arg)}` | {arg.Description ?? "Argument"} |");
                }

                foreach (var opt in command.Options)
                {
                    tableLines.Add($"| `{FormatOptionColumn(opt)}` | {opt.Description ?? "Option"} |");
                }

                tableLines.Add("");
                lines.InsertRange(headerIndex + 1, tableLines);
            }
        }
    }

    private static string FormatOptionColumn(Option opt)
    {
        var all = opt.Aliases.Prepend(opt.Name).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.Ordinal);
        var shorts = all.Where(a => a.StartsWith('-') && !a.StartsWith("--")).OrderBy(a => a, StringComparer.Ordinal);
        var longs = all.Where(a => a.StartsWith("--")).OrderBy(a => a, StringComparer.Ordinal);
        var sorted = shorts.Concat(longs).ToList();

        string joined = string.Join(@"\|", sorted);

        if (opt.ValueType != typeof(bool))
        {
            joined += $" `<{opt.Name.TrimStart('-')}>`";
        }

        return joined;
    }

    private static string FormatArgumentColumn(Argument arg)
    {
        if (arg.Arity.MinimumNumberOfValues == 0)
        {
            return $"[{arg.Name}]";
        }
        return $"<{arg.Name}>";
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
