using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Spectre.Console;

namespace Cadence.ToolingCore.Services;

public static partial class CreateAdrService
{
    public const int MinDeterministicId = 10_000;
    public const int MaxDeterministicId = 99_999;
    private const int DeterministicRangeSize = MaxDeterministicId - MinDeterministicId + 1;
    public const string DefaultDomain = "1. Core Platform & Execution Engine";
    public const string DefaultRequirement = "PRD-SYS-001";

    public sealed record AdrDomainDefinition(int Index, string Slug, string Label, string[] Aliases);

    public static readonly IReadOnlyList<AdrDomainDefinition> CanonicalDomains =
    [
        new(0, "clinical-kernel", "0. Clinical Kernel (K001–K037)",
            ["0", "kernel", "clinical-kernel", "0. clinical kernel", "0. clinical kernel (k001-k037)", "0. clinical kernel (k001–k037)"]),
        new(1, "core-platform", "1. Core Platform & Execution Engine",
            ["1", "platform", "core", "core-platform", "execution", "1. core platform & execution engine"]),
        new(2, "security-gateway", "2. API Gateway, Security & Identity",
            ["2", "security", "gateway", "identity", "security-gateway", "2. api gateway, security & identity"]),
        new(3, "interoperability-standards", "3. Clinical Data Interoperability & Standards",
            ["3", "standards", "interoperability", "interoperability-standards", "3. clinical data interoperability & standards"]),
        new(4, "clinical-operations", "4. Clinical Operations & Business Modules",
            ["4", "operations", "clinical-operations", "modules", "4. clinical operations & business modules"]),
        new(5, "compliance-governance", "5. Compliance, Audit & Governance",
            ["5", "compliance", "audit", "governance", "compliance-governance", "5. compliance, audit & governance"]),
        new(6, "frontend-design-system", "6. Frontend & Design System",
            ["6", "frontend", "ui", "design-system", "frontend-design-system", "6. frontend & design system"]),
        new(7, "devops-tooling", "7. DevOps, Tooling & CI/CD",
            ["7", "tooling", "devops", "ci", "cicd", "devops-tooling", "7. devops, tooling & ci/cd"]),
    ];

    [GeneratedRegex(@"[^\w\s-]")]
    private static partial Regex NonSlugCharsRegex();

    [GeneratedRegex(@"[\s_-]+")]
    private static partial Regex SlugSeparatorRegex();

    public static bool TryResolveCanonicalDomain(string? rawDomain, out string canonicalDomain)
    {
        canonicalDomain = string.Empty;
        if (string.IsNullOrWhiteSpace(rawDomain))
        {
            return false;
        }

        string normalized = rawDomain.Trim();
        if (normalized.Length >= 2 &&
            ((normalized[0] == '"' && normalized[^1] == '"') ||
             (normalized[0] == '\'' && normalized[^1] == '\'')))
        {
            normalized = normalized[1..^1].Trim();
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        foreach (var definition in CanonicalDomains)
        {
            if (string.Equals(definition.Label, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(definition.Slug, normalized, StringComparison.OrdinalIgnoreCase) ||
                definition.Aliases.Any(alias => string.Equals(alias, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                canonicalDomain = definition.Label;
                return true;
            }
        }

        return false;
    }

    public static string SlugifyTitle(string input)
    {
        string slug = NonSlugCharsRegex().Replace(input.ToLowerInvariant().Trim(), string.Empty);
        return SlugSeparatorRegex().Replace(slug, "-").Trim('-');
    }

    /// <summary>
    /// Computes a deterministic ADR numeric identifier from the decision date and file slug, outside
    /// manual blocks (<c>001–2799</c>) and the closed Stitch block (<c>2800–2899</c>).
    /// </summary>
    public static int ComputeDeterministicAdrId(DateOnly date, string slug, IReadOnlySet<int>? occupiedIds = null)
    {
        string normalizedSlug = SlugifyTitle(slug);
        string seed = $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{normalizedSlug}";
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(seed), digest);
        uint hash = BinaryPrimitives.ReadUInt32BigEndian(digest);
        int baseOffset = (int)(hash % (uint)DeterministicRangeSize);

        for (int step = 0; step < DeterministicRangeSize; step++)
        {
            int candidate = MinDeterministicId + ((baseOffset + step) % DeterministicRangeSize);
            if (candidate is >= 2800 and <= 2899)
            {
                continue;
            }

            if (occupiedIds is null || !occupiedIds.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free deterministic ADR identifier available in range {MinDeterministicId}-{MaxDeterministicId}.");
    }

    public static int CreateAdr(
        string title,
        DateOnly date,
        string? repoRoot = null,
        string? domain = null,
        IReadOnlyList<string>? requirements = null,
        string? author = null)
    {
        repoRoot ??= FindRepoRoot();
        string cleanTitle = title.Trim();
        AnsiConsole.MarkupLine($"[cyan]Scaffolding new ADR: {Markup.Escape(cleanTitle)}[/]");

        string slug = SlugifyTitle(cleanTitle);
        if (string.IsNullOrWhiteSpace(slug))
        {
            AnsiConsole.MarkupLine("[red]Error: ADR title must contain at least one alphanumeric character.[/]");
            return 1;
        }

        string canonicalDomain;
        if (string.IsNullOrWhiteSpace(domain))
        {
            canonicalDomain = DefaultDomain;
        }
        else if (!TryResolveCanonicalDomain(domain, out canonicalDomain))
        {
            string allowed = string.Join(", ", CanonicalDomains.Select(d => d.Slug));
            AnsiConsole.MarkupLine($"[red]Error: Unknown ADR domain '{Markup.Escape(domain)}'. Expected one of: {Markup.Escape(allowed)}[/]");
            return 1;
        }

        var normalizedReqs = NormalizeRequirementIds(requirements);
        string primaryReq = normalizedReqs[0];
        string formattedReqTags = string.Join(", ", normalizedReqs.Select(FormatRequirementTag));
        string formattedAuthors = FormatAuthors(author);

        string dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string fileName = $"{dateStr}-{slug}.md";
        string adrDir = Path.Combine(repoRoot, "docs", "adr");
        Directory.CreateDirectory(adrDir);
        string filePath = Path.Combine(adrDir, fileName);

        if (File.Exists(filePath))
        {
            AnsiConsole.MarkupLine($"[red]Error: ADR file already exists: {Markup.Escape(filePath)}[/]");
            return 1;
        }

        var occupiedIds = CollectOccupiedAdrIds(adrDir, fileName);
        int adrNum = ComputeDeterministicAdrId(date, slug, occupiedIds);

        string template = $@"# ADR-{adrNum}: {cleanTitle}

- **Status:** Accepted
- **Date:** {dateStr}
- **Domain:** {canonicalDomain}
- **Authors:** {formattedAuthors}
- **Deciders:** {formattedAuthors}
- **Requirements:** {formattedReqTags}

---

## 1. Context & Problem Statement

Describe the background, architectural context, and the specific technical or business problem this decision addresses ({FormatRequirementTag(primaryReq)}).

## 2. Decision Drivers & Constraints

- **Driver 1:** Technical and operational boundary constraint
- **Driver 2:** Regulatory traceability and verification ({FormatRequirementTag(primaryReq)})

## 3. Options Considered

### Option 1: Selected Architecture (Chosen)

- **Overview:** Primary architectural approach satisfying {primaryReq}.
- **Pros:**
  - Satisfies {primaryReq} with clear module boundaries
  - Preserves deterministic verification and compliance traceability
- **Cons:**
  - Introduces targeted implementation and test maintenance scope

### Option 2: Status Quo / Alternative Approach

- **Overview:** Alternative design evaluated against the decision drivers.
- **Pros:**
  - Minimal immediate code churn
- **Cons:**
  - Does not resolve the architectural constraint or long-term maintenance cost

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Satisfies {formattedReqTags} while maintaining system modularity, auditability, and operational clarity.

## 5. Consequences & Trade-offs

- **Positive Impact:** Enforces clear architectural boundaries and compliance traceability ({formattedReqTags}).
- **Negative Impact / Technical Debt:** Ongoing maintenance of the new contract and automated checks.
- **Mitigation Strategy:** Covered by automated unit and architecture test suites in CI.

## 6. Implementation & Verification

- **Affected Repositories / Services:** Relevant modules and packages.
- **Verification Plan:** Verified via automated tests under `tests/` and `pnpm cadence check adrs`.
";

        File.WriteAllText(filePath, template);
        AnsiConsole.MarkupLine($"[green]Successfully created ADR-{adrNum} at {Markup.Escape(filePath)}[/]");
        return 0;
    }

    private static HashSet<int> CollectOccupiedAdrIds(string adrDir, string excludeFileName)
    {
        var occupied = new HashSet<int>();
        if (!Directory.Exists(adrDir))
        {
            return occupied;
        }

        foreach (string file in Directory.GetFiles(adrDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            if (string.Equals(name, "index.md", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "TEMPLATE.md", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, excludeFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                string? firstLine = File.ReadLines(file).FirstOrDefault();
                if (firstLine is not null && AdrValidatorService.ParseAdrNumber(firstLine) is { } number)
                {
                    occupied.Add(number);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore unreadable files while probing occupied IDs.
            }
        }

        return occupied;
    }

    private static List<string> NormalizeRequirementIds(IReadOnlyList<string>? requirements)
    {
        var list = new List<string>();
        foreach (string rawEntry in requirements ?? [])
        {
            foreach (string token in rawEntry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string cleaned = token.Trim().Trim('`');
                if (cleaned.StartsWith("@req:", StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned[5..].Trim();
                }

                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    continue;
                }

                string normalized = cleaned.StartsWith("trace-", StringComparison.OrdinalIgnoreCase)
                    ? $"Trace-{cleaned[6..]}"
                    : cleaned.ToUpperInvariant();

                if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(normalized);
                }
            }
        }

        if (list.Count == 0)
        {
            list.Add(DefaultRequirement);
        }

        return list;
    }

    private static string FormatRequirementTag(string reqId) =>
        reqId.StartsWith("Trace-", StringComparison.OrdinalIgnoreCase)
            ? $"`{reqId}`"
            : $"`@req:{reqId.ToUpperInvariant()}`";

    private static string FormatAuthors(string? rawAuthors)
    {
        var items = (rawAuthors ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => !item.Contains(' ', StringComparison.Ordinal) && !item.StartsWith('@') ? $"@{item}" : item)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return items.Count == 0 ? "@fderuiter" : string.Join(", ", items);
    }

    private static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
