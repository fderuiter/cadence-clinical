using System;
using System.IO;

namespace Cadence.ToolingCore.Services;

public static class CreateAdrService
{
    public static int CreateAdr(string title, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine($"Scaffolding new Architectural Decision Record (ADR) for '{title}'...");

        string adrDir = Path.Combine(repoRoot, "docs", "adr");
        Directory.CreateDirectory(adrDir);

        var existing = Directory.GetFiles(adrDir, "*.md");
        int nextNum = existing.Length + 1;
        string dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string slug = title.ToLowerInvariant().Replace(' ', '-');
        string fileName = $"{dateStr}-{slug}.md";
        string filePath = Path.Combine(adrDir, fileName);

        string template = $@"# ADR-{nextNum:D4}: {title}

* **Status:** Draft
* **Date:** {dateStr}

## 1. Context & Problem Statement
Describe context.

## 2. Decision Drivers
- Driver 1

## 3. Considered Options
- Option 1
- Option 2

## 4. Decision Outcome
Chosen option: Option 1

## 5. Pros & Cons of the Options
### Option 1
* Good because X
* Bad because Y

## 6. Compliance Traceability
* **PRD Traceability:** `@req:PRD-SYS-001`
";

        File.WriteAllText(filePath, template);
        Console.WriteLine($"✔ Scaffolded new ADR at {filePath}");

        return 0;
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
