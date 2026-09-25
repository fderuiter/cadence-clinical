using System.ComponentModel;
using System.Diagnostics;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class ScaffoldServiceCommand : Command<ScaffoldServiceCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine($"[cyan]Scaffolding Clean Architecture service: {settings.Name}[/]");
        var psi = new ProcessStartInfo("bash", $"scripts/migrate_service_wizard.sh -ServiceName {settings.Name}") { UseShellExecute = false };
        using var p = Process.Start(psi);
        p?.WaitForExit();
        return p?.ExitCode ?? 0;
    }
}

public class ScaffoldAdrCommand : Command<ScaffoldAdrCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<title>")]
        public string Title { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine($"[cyan]Scaffolding new ADR: {settings.Title}[/]");
        string repoRoot = Cadence.Cli.Services.QualityGateOrchestrator.FindRepoRoot();
        string adrDir = System.IO.Path.Combine(repoRoot, "docs", "adr");
        System.IO.Directory.CreateDirectory(adrDir);

        int maxNum = 100;
        foreach (var file in System.IO.Directory.GetFiles(adrDir, "*.md"))
        {
            string fileName = System.IO.Path.GetFileName(file);
            if (fileName.Equals("TEMPLATE.md", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("index.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                string firstLine = System.IO.File.ReadLines(file).FirstOrDefault() ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(firstLine, @"ADR-(\d+):");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num) && num > maxNum)
                {
                    maxNum = num;
                }
            }
            catch (Exception ex)
            {
                // Unreadable ADR file skipped during number discovery
                AnsiConsole.MarkupLine($"[yellow]Warning: Could not read ADR file '{fileName}': {ex.Message}[/]");
            }
        }

        int adrNum = maxNum + 1;
        string todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string slug = System.Text.RegularExpressions.Regex.Replace(settings.Title.ToLowerInvariant().Trim(), @"[^\w\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[\s_-]+", "-").Trim('-');
        string filename = $"{todayStr}-{slug}.md";
        string filepath = System.IO.Path.Combine(adrDir, filename);

        if (System.IO.File.Exists(filepath))
        {
            AnsiConsole.MarkupLine($"[red]Error: ADR file already exists: {filepath}[/]");
            return 1;
        }

        string content = $@"# ADR-{adrNum}: {settings.Title}

- **Status:** Accepted
- **Date:** {todayStr}
- **Authors:** @fderuiter
- **Requirements:** PRD-SYS-053

## Context
Provide background context and the problem statement here.

## Decision
Detail the architecture decision made and why.

## Consequences
Detail positive and negative consequences of this decision.
";
        System.IO.File.WriteAllText(filepath, content, System.Text.Encoding.UTF8);
        AnsiConsole.MarkupLine($"[bold green]Successfully created ADR-{adrNum} at docs/adr/{filename}[/]");
        return 0;
    }
}
