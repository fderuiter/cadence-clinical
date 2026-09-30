using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

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
        string title = settings.Title;
        string todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string slug = Regex.Replace(title.ToLowerInvariant().Trim(), @"[^\w\s-]", "");
        slug = Regex.Replace(slug, @"[\s_-]+", "-").Trim('-');
        string filename = $"{todayStr}-{slug}.md";
        string adrDir = Path.Combine(repoRoot, "docs", "adr");
        Directory.CreateDirectory(adrDir);
        string filepath = Path.Combine(adrDir, filename);

        if (File.Exists(filepath))
        {
            AnsiConsole.MarkupLine($"[red]Error: ADR file already exists: {filepath}[/]");
            return 1;
        }

        int maxNum = 0;
        foreach (var f in Directory.GetFiles(adrDir, "*.md"))
        {
            string fname = Path.GetFileName(f);
            if (fname == "TEMPLATE.md" || fname == "index.md") continue;
            try
            {
                using var reader = new StreamReader(f);
                string? firstLine = reader.ReadLine();
                if (firstLine != null)
                {
                    var match = Regex.Match(firstLine, @"ADR-(\d+):");
                    if (match.Success)
                    {
                        maxNum = Math.Max(maxNum, int.Parse(match.Groups[1].Value));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore unreadable ADR header files during number discovery
            }
        }
        int adrNum = maxNum > 0 ? maxNum + 1 : 100;

        string template = $@"# ADR-{adrNum}: {title}

- **Status:** Accepted
- **Date:** {todayStr}
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Describe the background, architectural context, and the specific technical or business problem this decision addresses. Reference the applicable system requirement (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **Driver 1:** Technical constraint 1
- **Driver 2:** Business/GxP requirement (PRD-SYS-001)

## 3. Options Considered

### Option 1: Proposed Architecture / Primary Approach (Selected)

- **Overview:** Brief description of primary option and architecture.
- **Pros:**
  - ✅ Satisfies requirement PRD-SYS-001
  - ✅ Maintains operational boundaries and compliance tracing
- **Cons:**
  - ❌ Requires additional component or abstraction layer

### Option 2: Alternative Architecture / Status Quo

- **Overview:** Brief description of alternative option.
- **Pros:**
  - ✅ Requires minimal initial scaffolding
- **Cons:**
  - ❌ Fails to fully address requirements or long-term maintainability

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it satisfies PRD-SYS-001 while ensuring system maintainability, auditability, and clear operational boundaries.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clear operational boundaries and compliance tracing for requirement PRD-SYS-001.
- **Negative Impact / Technical Debt:** Maintenance overhead of additional structured abstractions.
- **Mitigation Strategy:** Automated test coverage and CI lint validation.

## 6. Implementation & Verification

- **Affected Repositories / Services:** Relevant modules and services.
- **Verification Plan:** Automated unit and architecture tests added under `tests/`.
";

        File.WriteAllText(filepath, template);
        AnsiConsole.MarkupLine($"[green]Successfully created ADR file: {filepath}[/]");

        string indexFile = Path.Combine(adrDir, "index.md");
        if (File.Exists(indexFile))
        {
            string indexContent = File.ReadAllText(indexFile);
            string entry = $"- [ADR-{adrNum}: {title}]({filename}) ({todayStr})";
            if (!indexContent.Contains(filename))
            {
                // Append directly under the last list so the index keeps no blank-line gaps inside lists.
                string separator = indexContent.EndsWith('\n') ? "" : "\n";
                File.AppendAllText(indexFile, $"{separator}{entry}\n");
                AnsiConsole.MarkupLine($"[green]Successfully indexed {filename} in {indexFile}[/]");
            }
        }

        return 0;
    }
}

