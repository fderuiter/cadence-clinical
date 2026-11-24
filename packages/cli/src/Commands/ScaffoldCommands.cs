using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Kernel.Time;
using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class ScaffoldCommands
{
    public static void ConfigureService(Command command)
    {
        var name = new Argument<string>("name") { Description = "Name of the service to scaffold" };
        command.Arguments.Add(name);
        command.SetAction(result => ScaffoldService(result.GetRequiredValue(name)));
    }

    public static void ConfigureAdr(Command command)
    {
        var title = new Argument<string>("title") { Description = "Title of the decision" };
        command.Arguments.Add(title);
        command.SetAction(result => ScaffoldAdr(result.GetRequiredValue(title)));
    }

    public static void ConfigurePost(Command command)
    {
        var title = new Argument<string>("title") { Description = "Title of the engineering blog post" };
        var author = new Option<string>("--author", "-a")
        {
            Description = "Author GitHub handle or display name (comma-separated for multiple, e.g. fderuiter)",
            DefaultValueFactory = _ => "@fderuiter",
        };
        var tags = new Option<string[]>("--tag", "-t")
        {
            Description = "Topic tag slug (repeat or comma-separate for multiple)",
            AllowMultipleArgumentsPerToken = false,
        };
        var summary = new Option<string?>("--summary", "-s")
        {
            Description = "One-sentence summary excerpt for the blog index and RSS feed",
        };
        var publish = new Option<bool>("--publish")
        {
            Description = "Write draft: false so the post is published immediately instead of starting as a draft",
        };

        command.Arguments.Add(title);
        command.Options.Add(author);
        command.Options.Add(tags);
        command.Options.Add(summary);
        command.Options.Add(publish);
        command.SetAction(result => ScaffoldPost(
            result.GetRequiredValue(title),
            author: result.GetValue(author),
            tags: result.GetValue(tags),
            summary: result.GetValue(summary),
            draft: !result.GetValue(publish)));
    }

    /// <summary>Builds the <see cref="ProcessStartInfo"/> that runs <c>scripts/migrate_service_wizard.sh</c> with <paramref name="name"/> as <c>$1</c>.</summary>
    public static ProcessStartInfo CreateScaffoldServiceStartInfo(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var psi = new ProcessStartInfo("bash") { UseShellExecute = false };
        psi.ArgumentList.Add("scripts/migrate_service_wizard.sh");
        psi.ArgumentList.Add(name);
        return psi;
    }

    private static int ScaffoldService(string name)
    {
        AnsiConsole.MarkupLine($"[cyan]Scaffolding Clean Architecture service: {Markup.Escape(name)}[/]");
        var psi = CreateScaffoldServiceStartInfo(name);
        using var p = Process.Start(psi);
        p?.WaitForExit();
        return p?.ExitCode ?? 0;
    }

    private static int ScaffoldAdr(string title)
    {
        AnsiConsole.MarkupLine($"[cyan]Scaffolding new ADR: {Markup.Escape(title)}[/]");
        string repoRoot = Cadence.Cli.Services.QualityGateOrchestrator.FindRepoRoot();
        string todayStr = SystemClock.Instance.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string slug = SlugifyTitle(title);
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

    public static int ScaffoldPost(
        string title,
        string? repoRoot = null,
        IClock? clock = null,
        string? author = null,
        IReadOnlyList<string>? tags = null,
        string? summary = null,
        bool draft = true)
    {
        AnsiConsole.MarkupLine($"[cyan]Scaffolding new engineering blog post: {Markup.Escape(title)}[/]");
        string resolvedRoot = repoRoot ?? Services.QualityGateOrchestrator.FindRepoRoot();
        string todayStr = (clock ?? SystemClock.Instance).UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string slug = SlugifyTitle(title);
        if (string.IsNullOrWhiteSpace(slug))
        {
            AnsiConsole.MarkupLine("[red]Error: Post title must contain at least one alphanumeric character.[/]");
            return 1;
        }

        string filename = $"{todayStr}-{slug}.md";
        string blogDir = Path.Combine(resolvedRoot, "docs", "blog");
        Directory.CreateDirectory(blogDir);
        string filepath = Path.Combine(blogDir, filename);

        if (File.Exists(filepath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Blog post file already exists: {Markup.Escape(filepath)}[/]");
            return 1;
        }

        string cleanTitle = title.Trim();
        var authorList = ParseDelimitedItems(author, fallback: "@fderuiter");
        var tagList = (tags ?? [])
            .SelectMany(entry => entry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(SlugifyTitle)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tagList.Count == 0)
        {
            tagList.Add("engineering");
        }

        string cleanSummary = string.IsNullOrWhiteSpace(summary)
            ? $"Engineering notes and architectural context for {cleanTitle}."
            : summary.Trim();

        string authorsYaml = string.Join("\n", authorList.Select(a => $"  - \"{EscapeYamlString(a)}\""));
        string tagsYaml = string.Join(", ", tagList.Select(t => $"\"{EscapeYamlString(t)}\""));
        string draftYaml = draft ? "true" : "false";

        string template = $@"---
title: ""{EscapeYamlString(cleanTitle)}""
date: ""{todayStr}""
authors:
{authorsYaml}
summary: ""{EscapeYamlString(cleanSummary)}""
tags: [{tagsYaml}]
draft: {draftYaml}
prev: false
next: false
related:
  - text: ""Architecture Decision Records""
    link: ""../adr/index.md""
---

# {cleanTitle}

{cleanSummary}

## Context

Summarize the clinical workflow, platform boundary, or operational problem that motivated this change, linking the relevant [Architecture Decision Records](../adr/index.md) or [Kernel Specification](../kernel/CLINICAL_KERNEL_SPECIFICATION.md) sections.

## Design and Implementation

Explain the key architectural choices, domain invariants, and trade-offs evaluated during implementation.

## Verification and Trade-offs

Document how the change is verified across automated unit, architecture, or end-to-end suites, and flip `draft: false` in the frontmatter when ready to publish.
";

        File.WriteAllText(filepath, template);
        AnsiConsole.MarkupLine($"[green]Successfully created blog post: {Markup.Escape(filepath)}[/]");

        string indexPath = Path.Combine(resolvedRoot, "docs", "DOCUMENTATION_INDEX.md");
        if (File.Exists(indexPath))
        {
            int syncExit = DocIndexSyncService.SyncDocIndex(check: false, repoRoot: resolvedRoot);
            if (syncExit != 0)
            {
                return syncExit;
            }
        }

        return 0;
    }

    private static string SlugifyTitle(string input)
    {
        string slug = Regex.Replace(input.ToLowerInvariant().Trim(), @"[^\w\s-]", "");
        return Regex.Replace(slug, @"[\s_-]+", "-").Trim('-');
    }

    private static List<string> ParseDelimitedItems(string? raw, string fallback)
    {
        var items = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => !item.Contains(' ') && !item.StartsWith('@') ? $"@{item}" : item)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (items.Count == 0)
        {
            items.Add(fallback);
        }

        return items;
    }

    private static string EscapeYamlString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }
}

