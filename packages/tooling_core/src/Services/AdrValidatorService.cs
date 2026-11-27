using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Spectre.Console;

namespace Cadence.ToolingCore.Services;

public static class AdrValidatorService
{
    public record DomainDefinition(string Slug, string SectionTitle, string ShortTitle);

    public record DomainAdrHeader(string Id, string Domain, string CompactDate, string Slug, string Title);

    public static readonly IReadOnlyList<DomainDefinition> DomainDefinitions =
    [
        new("kernel", "0. Clinical Kernel (K001–K037)", "Clinical Kernel"),
        new("platform", "1. Core Platform & Execution Engine", "Core Platform & Execution Engine"),
        new("security", "2. API Gateway, Security & Identity", "API Gateway, Security & Identity"),
        new("standards", "3. Clinical Data Interoperability & Standards", "Clinical Data Interoperability & Standards"),
        new("operations", "4. Clinical Operations & Business Modules", "Clinical Operations & Business Modules"),
        new("compliance", "5. Compliance, Audit & Governance", "Compliance, Audit & Governance"),
        new("frontend", "6. Frontend & Design System", "Frontend & Design System"),
        new("tooling", "7. DevOps, Tooling & CI/CD", "DevOps, Tooling & CI/CD"),
    ];

    public static readonly IReadOnlyList<string> DomainSlugs =
        DomainDefinitions.Select(d => d.Slug).ToArray();

    private static readonly Dictionary<string, DomainDefinition> DomainsBySlug =
        DomainDefinitions.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> RootIgnoreFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEMPLATE.md",
        "index.md",
        "legacy-index.md",
    };

    private static readonly HashSet<string> ShardIgnoreFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "index.md",
    };

    private static readonly HashSet<string> BaselineFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "2026-06-06-usdm-pydantic-models.md",
        "2026-07-22-api-first-in-memory-diffing.md",
        "2026-07-22-api-first-validation-and-usdm-integration.md",
        "2026-07-22-audit-log-design.md",
        "2026-07-22-compliance-tracing-and-automated-trial-locks.md",
        "2026-07-22-database-shadow-triggers.md",
        "2026-07-22-gateway-authentication-propagation.md",
        "2026-07-22-gateway-openapi-aggregation.md",
        "2026-07-22-merkle-root-sealing.md",
        "2026-07-22-metadata-driven-grid-layouts.md",
        "2026-07-22-pnpm-frontend-workspace.md",
        "2026-07-22-schema-mapping-design.md",
        "2026-07-22-signature-re-authentication.md",
        "2026-07-22-transactional-sql-engine-gxp-auditing.md",
        "2026-07-22-translation-job-and-layout-engine.md",
        "2026-07-22-unified-database-management-and-pre-boot-migrations.md",
        "2026-07-22-usdm-dynamic-mapper-and-cache.md",
        "2026-07-23-canonical-json-signatures-and-rolling-versioning.md",
        "2026-07-23-core-service-oriented-clinical-engine.md",
        "2026-07-23-declarative-ruleset-automerge.md",
        "2026-07-23-targeted-integration-path-isolation.md",
        "2026-07-24-clinical-query-workflow-authorization.md",
        "2026-07-24-code-formatting-and-style-standardization.md",
        "2026-07-24-continuous-fmea-gxp-aligned-exemption-ledger.md",
        "2026-07-24-database-native-pessimistic-locking-retry.md",
        "2026-07-24-fhir-esource-ecoa-sync-gateway.md",
        "2026-07-24-medical-coding-engine-persistence.md",
        "2026-07-24-minimizing-merge-friction-for-baselines-and-reports.md",
        "2026-07-24-rolling-upgrade-and-canonical-json-signatures.md",
        "2026-07-25-data-driven-edl-model-and-completeness.md",
        "2026-07-25-etmf-qc-review-workflow.md",
        "2026-07-25-part11-offline-sync-ledger.md",
        "2026-07-26-ctms-foundation-infrastructure.md",
        "2026-07-27-backward-compatible-gateway-signature-verification.md",
        "2026-07-27-ci-schema-introspection-and-gateway-aggregation.md",
        "2026-07-27-multi-database-reset-cli-tool.md",
        "2026-07-27-quality-capa-scaffold.md",
        "2026-07-27-standardize-fastapi-identity-dependency.md",
        "2026-07-28-sdv-tsdv-persistence-foundation.md",
        "2026-07-29-tmf-reference-model-taxonomy-integration.md",
        "2026-07-30-lab-reference-range-management.md",
        "2026-07-30-rule-authoring-validation-and-ddf-delivery.md",
        "2026-07-31-ecoa-subject-identity-and-gateway-routing.md",
        "2026-08-01-vue-spa-oidc-rbac-signing-boundaries.md",
        "2026-08-02-econsent-scaffold-and-part11-audit.md",
        "2026-08-02-eligibility-criteria-evaluation-engine.md",
        "2026-08-02-notifications-service-foundation.md",
        "2026-08-02-protocol-rendering-architecture.md",
        "2026-08-02-rtsm-architecture-and-randomization-persistence.md",
        "2026-08-02-sdtm-foundation-models.md",
        "2026-08-02-usdm-v2-v3-canonical-contract-mapping-matrix.md",
        "2026-08-03-subject-randomization-lifecycle-and-state-guards.md",
        "2026-08-04-ctms-service-boundary-and-reporting.md",
        "2026-08-04-notification-delivery-channels-and-retry-dispatcher.md",
        "2026-08-05-quality-capa-management.md",
        "2026-08-06-arm-aware-soa-matrix.md",
        "2026-08-07-epro-sync-durable-reconciliation.md",
        "2026-08-07-gateway-terminology-routing.md",
        "2026-08-07-organization-directory-service-foundation.md",
        "2026-08-08-biomedical-concept-locks.md",
        "2026-08-08-centralized-rbac-toolkit.md",
        "2026-08-08-ecoa-portal-and-interop-deployment.md",
        "2026-08-08-expose-authenticated-sdtm-adam-dataset-json-export-endpoints.md",
        "2026-08-08-gateway-step-up-re-authentication.md",
        "2026-08-08-pi-only-batch-sign-off-execution.md",
        "2026-08-08-rtsm-supply-domain-persistence.md",
        "2026-08-09-automated-etmf-document-redaction.md",
        "2026-08-09-eisf-maintenance-and-formatting.md",
        "2026-08-09-etmf-filterable-paginated-audit-log.md",
        "2026-08-09-global-library-object-instantiation.md",
        "2026-08-09-medical-coding-engine-query-integration.md",
        "2026-08-09-nci-thesaurus-signed-web-client.md",
        "2026-08-09-safety-e2b-icsr-xml-export-pipeline.md",
        "2026-08-10-debounced-clinical-code-lookup-ui-primitive.md",
        "2026-08-11-bidirectional-field-parity-and-rfc7807-validation-schemas.md",
        "2026-08-11-declarative-dependencies-and-signature-verification-fallback.md",
        "2026-08-11-gateway-signature-legacy-v2-fallback.md",
        "2026-08-11-standardize-pr-templates-and-centralize-hashing-grid-layout.md",
        "2026-08-11-unified-parameterized-relational-database-lifespan-wrapper.md",
        "2026-08-11-vitepress-workspace-documentation-portal.md",
        "2026-08-12-deterministic-gxp-report-generation-and-signature-verification.md",
        "2026-08-12-secure-unblinding-signature-fallback-restriction.md",
        "2026-08-13-tickets-service-scaffold-and-gateway-integration.md",
        "2026-08-14-biostatistical-export-pipeline-interoperability.md",
        "2026-08-15-bidirectional-api-contract-enforcement.md",
        "2026-08-16-parallel-ci-workflows-and-local-concurrent-execution.md",
        "2026-07-27-unified-python-markdown-validator.md",
        "2026-07-28-native-vue3-rules-designer-gxp-ledger-sync.md",
        "2026-08-17-centralized-permission-auth.md",
        "2026-08-17-frontend-standardization-css-grid-and-centralized-utilities.md",
        "2026-08-17-interactive-mermaid-diagrams-and-zoom-pan-controls.md",
        "2026-07-27-api-driven-lock-sync-and-ast-validator.md",
    };

    private static readonly Regex FilenamePattern = new(@"^\d{4}-\d{2}-\d{2}-.+\.md$", RegexOptions.Compiled);
    // Legacy line 1: "# ADR-NNNN: Title" (at least three digits, e.g. ADR-049 or ADR-2250).
    private static readonly Regex AdrHeadingPattern = new(@"^# ADR-(\d{3,}): \S.*$", RegexOptions.Compiled);
    // Domain-sharded line 1: "# ADR-<DOMAIN>-YYYYMMDD-<slug>: Title".
    private static readonly Regex DomainAdrHeadingPattern = new(@"^# (ADR-([A-Z]+)-(\d{8})-([a-z0-9-]+)): (\S.*)$", RegexOptions.Compiled);
    private static readonly Regex IndexEntryPattern = new(@"^\s*[-*]\s+\[[^\]]*\]\(([^)\s]+)\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkPattern = new(@"\]\(([^)\s]+)\)", RegexOptions.Compiled);

    private static readonly string[] RequiredSectionsOld =
    [
        "## Status",
        "## Context",
        "## Decision",
        "## Alternatives Considered",
        "## Trade-offs"
    ];

    private static readonly string[] RequiredSectionsNew =
    [
        "## 1. Context & Problem Statement",
        "## 2. Decision Drivers & Constraints",
        "## 3. Options Considered",
        "## 4. Decision Outcome",
        "## 5. Consequences & Trade-offs",
        "## 6. Implementation & Verification"
    ];

    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();

    public static bool IsValidDomain(string? domain) =>
        !string.IsNullOrWhiteSpace(domain) && DomainsBySlug.ContainsKey(domain.Trim());

    public static DomainDefinition? TryGetDomainDefinition(string? domain) =>
        !string.IsNullOrWhiteSpace(domain) && DomainsBySlug.TryGetValue(domain.Trim(), out var def)
            ? def
            : null;

    public static bool ValidateAdrs(
        bool checkGitDiff = true,
        string[]? targets = null,
        string? repoRoot = null,
        bool preCommit = false,
        string? baseRef = null,
        string? pushBefore = null,
        string? eventName = null,
        string? gitRef = null)
    {
        repoRoot ??= FindRepoRoot();
        string adrDir = Path.Combine(repoRoot, "docs", "adr");
        AnsiConsole.MarkupLine("[cyan]Validating Architectural Decision Records (ADRs)...[/]");

        if (!Directory.Exists(adrDir))
        {
            AnsiConsole.MarkupLine($"[red]Error: Directory {Markup.Escape(adrDir)} not found.[/]");
            return false;
        }

        var validReqs = GetValidRequirements(repoRoot);
        bool allPassed = true;

        // 1. Directory-wide integrity across root hub, legacy-index.md, and all 8 domain shards.
        foreach (var error in FindIntegrityErrors(adrDir))
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(error)}[/]");
            allPassed = false;
        }

        var filesToValidate = targets is { Length: > 0 }
            ? targets.Where(File.Exists).ToList()
            : EnumerateAllAdrMarkdownFiles(adrDir);

        foreach (var filepath in filesToValidate)
        {
            string filename = Path.GetFileName(filepath);
            string relToAdr = Path.GetRelativePath(adrDir, filepath).Replace('\\', '/');
            bool isRootAdrFile = !relToAdr.Contains('/', StringComparison.Ordinal);
            if (isRootAdrFile ? RootIgnoreFiles.Contains(filename) : ShardIgnoreFiles.Contains(filename))
            {
                continue;
            }

            // 2. Check filename pattern
            if (!FilenamePattern.IsMatch(filename))
            {
                AnsiConsole.MarkupLine($"[red]Error: File '{Markup.Escape(relToAdr)}' does not follow the standard date pattern (YYYY-MM-DD-...).[/]");
                allPassed = false;
            }

            // 3. Check contents
            string content;
            try
            {
                content = File.ReadAllText(filepath);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error reading '{Markup.Escape(filepath)}': {Markup.Escape(ex.Message)}[/]");
                allPassed = false;
                continue;
            }

            bool isLegacyTemplate = !content.Contains(RequiredSectionsNew[0], StringComparison.Ordinal) &&
                content.Split('\n').Any(l => l.TrimEnd() == "## Status");
            var requiredSections = isLegacyTemplate ? RequiredSectionsOld : RequiredSectionsNew;
            var missingSections = requiredSections.Where(s => !content.Contains(s, StringComparison.Ordinal)).ToList();
            if (missingSections.Count > 0)
            {
                AnsiConsole.MarkupLine($"[red]Error: File '{Markup.Escape(relToAdr)}' missing required sections: {Markup.Escape(string.Join(", ", missingSections))}[/]");
                allPassed = false;
            }

            // 4. Validate requirement links
            var refs = ExtractRequirementReferences(content);
            if (IsPost2026Adr(filename))
            {
                if (refs.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[red]Error: Post-2026 ADR file '{Markup.Escape(relToAdr)}' lacks a requirement reference.[/]");
                    allPassed = false;
                }
                else
                {
                    var invalidRefs = refs.Where(r => !validReqs.Contains(r)).ToList();
                    if (invalidRefs.Count > 0)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Post-2026 ADR '{Markup.Escape(relToAdr)}' references invalid requirement(s): {Markup.Escape(string.Join(", ", invalidRefs))}[/]");
                        allPassed = false;
                    }
                }
            }
        }

        if (checkGitDiff && !CheckArchitecturalChangesRequireAdr(repoRoot, preCommit, baseRef, pushBefore, eventName, gitRef))
        {
            allPassed = false;
        }

        if (allPassed)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] All ADRs compliant.[/]");
        }

        return allPassed;
    }

    private static List<string> EnumerateAllAdrMarkdownFiles(string adrDir)
    {
        var files = new List<string>();
        files.AddRange(
            Directory.GetFiles(adrDir, "*.md", SearchOption.TopDirectoryOnly)
                .Where(f => !RootIgnoreFiles.Contains(Path.GetFileName(f)))
                .Order(StringComparer.Ordinal));

        foreach (var domain in DomainDefinitions)
        {
            string domainDir = Path.Combine(adrDir, domain.Slug);
            if (!Directory.Exists(domainDir))
            {
                continue;
            }

            files.AddRange(
                Directory.GetFiles(domainDir, "*.md", SearchOption.TopDirectoryOnly)
                    .Where(f => !ShardIgnoreFiles.Contains(Path.GetFileName(f)))
                    .Order(StringComparer.Ordinal));
        }

        return files;
    }

    /// <summary>
    /// Inserts an ADR bullet into <c>docs/adr/&lt;domain&gt;/index.md</c> sorted alphabetically by filename,
    /// creating the domain shard file from its canonical template if absent.
    /// </summary>
    public static bool InsertSortedDomainShardEntry(
        string shardIndexPath,
        string domainSlug,
        string filename,
        string adrId,
        string title,
        string isoDate)
    {
        var domainDef = TryGetDomainDefinition(domainSlug)
            ?? throw new ArgumentException($"Unknown ADR domain '{domainSlug}'.", nameof(domainSlug));

        string shardDir = Path.GetDirectoryName(shardIndexPath)!;
        Directory.CreateDirectory(shardDir);

        if (!File.Exists(shardIndexPath))
        {
            string anchor = BuildGitHubHeadingSlug(domainDef.SectionTitle);
            string initialContent = string.Join(
                '\n',
                $"# {domainDef.ShortTitle} ADRs (`{domainDef.Slug}`)",
                "",
                $"Active Architectural Decision Records for **{domainDef.SectionTitle}**.",
                "",
                $"- [ADR Hub](../index.md) · [ADR Template](../TEMPLATE.md) · [Frozen Legacy Decisions](../legacy-index.md#{anchor})",
                $"- Scaffold a new decision in this shard with `pnpm cadence scaffold adr --domain {domainDef.Slug} \"<title>\"`.",
                $"- Entries below use `# ADR-{domainDef.Slug.ToUpperInvariant()}-YYYYMMDD-<slug>: <title>` and are sorted alphabetically by filename.",
                "",
                "## Decisions",
                "");
            File.WriteAllText(shardIndexPath, initialContent);
        }

        var lines = File.ReadAllLines(shardIndexPath).Select(l => l.TrimEnd('\r')).ToList();

        int decisionsHeadingIdx = lines.FindIndex(l => l.Trim().Equals("## Decisions", StringComparison.OrdinalIgnoreCase));
        int searchStart = decisionsHeadingIdx >= 0 ? decisionsHeadingIdx + 1 : 0;

        var bulletPositions = new List<(int LineIndex, string TargetFile)>();
        for (int i = searchStart; i < lines.Count; i++)
        {
            if (decisionsHeadingIdx >= 0 && lines[i].StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            var match = IndexEntryPattern.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            string target = match.Groups[1].Value.Split('#')[0].Trim();
            if (target.StartsWith("./", StringComparison.Ordinal))
            {
                target = target[2..];
            }

            if (target.StartsWith("../", StringComparison.Ordinal) || target.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(target, filename, StringComparison.Ordinal))
            {
                return false;
            }

            bulletPositions.Add((i, target));
        }

        string entryLine = string.IsNullOrEmpty(isoDate)
            ? $"- [{adrId}: {title}]({filename})"
            : $"- [{adrId}: {title}]({filename}) ({isoDate})";

        if (bulletPositions.Count == 0)
        {
            if (decisionsHeadingIdx >= 0)
            {
                int insertAt = decisionsHeadingIdx + 1;
                if (insertAt >= lines.Count || lines[insertAt].Length > 0)
                {
                    lines.Insert(insertAt, "");
                    insertAt++;
                }
                else
                {
                    insertAt++;
                }

                lines.Insert(insertAt, entryLine);
            }
            else
            {
                if (lines.Count > 0 && lines[^1].Length > 0)
                {
                    lines.Add("");
                }

                lines.Add("## Decisions");
                lines.Add("");
                lines.Add(entryLine);
            }
        }
        else
        {
            int insertIdx = -1;
            foreach (var (lineIdx, existingTarget) in bulletPositions)
            {
                if (StringComparer.Ordinal.Compare(filename, existingTarget) < 0)
                {
                    insertIdx = lineIdx;
                    break;
                }
            }

            if (insertIdx >= 0)
            {
                lines.Insert(insertIdx, entryLine);
            }
            else
            {
                lines.Insert(bulletPositions[^1].LineIndex + 1, entryLine);
            }
        }

        string normalized = string.Join('\n', lines).TrimEnd('\n') + "\n";
        File.WriteAllText(shardIndexPath, normalized);
        return true;
    }

    /// <summary>
    /// Synchronizes any unlisted domain ADR files under <c>docs/adr/&lt;domain&gt;/</c> into
    /// <c>docs/adr/&lt;domain&gt;/index.md</c> sorted alphabetically by filename without modifying
    /// <c>docs/adr/index.md</c> or <c>docs/adr/legacy-index.md</c>.
    /// </summary>
    public static int SyncAdrIndex(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        string adrDir = Path.Combine(repoRoot, "docs", "adr");
        if (!Directory.Exists(adrDir))
        {
            return 0;
        }

        int addedCount = 0;
        foreach (var domain in DomainDefinitions)
        {
            string domainDir = Path.Combine(adrDir, domain.Slug);
            if (!Directory.Exists(domainDir))
            {
                continue;
            }

            string shardIndexPath = Path.Combine(domainDir, "index.md");
            var domainAdrFiles = Directory.GetFiles(domainDir, "*.md", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !ShardIgnoreFiles.Contains(name) && FilenamePattern.IsMatch(name))
                .Order(StringComparer.Ordinal)
                .ToList();

            foreach (string name in domainAdrFiles)
            {
                string firstLine = File.ReadLines(Path.Combine(domainDir, name)).FirstOrDefault()?.TrimEnd('\r') ?? "";
                var header = ParseDomainAdrHeader(firstLine);
                if (header is null)
                {
                    continue;
                }

                string datePart = name.Length >= 10 ? name[..10] : "";
                if (InsertSortedDomainShardEntry(shardIndexPath, domain.Slug, name, header.Id, header.Title, datePart))
                {
                    addedCount++;
                }
            }
        }

        if (addedCount > 0)
        {
            AnsiConsole.MarkupLine($"  [green][[OK]] Added {addedCount} missing domain ADR entry(ies) to docs/adr/*/index.md.[/]");
        }

        return 0;
    }

    /// <summary>
    /// Parses the ADR number from a legacy ADR's first line (<c># ADR-NNNN: Title</c>).
    /// Returns <c>null</c> when the line is not a numbered ADR heading.
    /// </summary>
    public static int? ParseAdrNumber(string headingLine)
    {
        var match = AdrHeadingPattern.Match(headingLine.TrimEnd('\r'));
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? number
            : null;
    }

    /// <summary>
    /// Parses a domain-sharded ADR heading (<c># ADR-&lt;DOMAIN&gt;-YYYYMMDD-&lt;slug&gt;: &lt;title&gt;</c>).
    /// </summary>
    public static DomainAdrHeader? ParseDomainAdrHeader(string headingLine)
    {
        var match = DomainAdrHeadingPattern.Match(headingLine.TrimEnd('\r'));
        if (!match.Success)
        {
            return null;
        }

        string id = match.Groups[1].Value;
        string domain = match.Groups[2].Value;
        string compactDate = match.Groups[3].Value;
        string slug = match.Groups[4].Value;
        string title = match.Groups[5].Value.Trim();

        if (!DateOnly.TryParseExact(compactDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return null;
        }

        return new DomainAdrHeader(id, domain, compactDate, slug, title);
    }

    /// <summary>
    /// Validates ADR integrity across <c>docs/adr/index.md</c>, <c>docs/adr/legacy-index.md</c>, and all
    /// eight <c>docs/adr/&lt;domain&gt;/index.md</c> shards: valid headings, globally unique ADR IDs,
    /// complete single-entry shard coverage, alphabetical sorting by filename within every shard section,
    /// and valid relative links across all ADR indexes.
    /// </summary>
    public static IReadOnlyList<string> FindIntegrityErrors(string adrDir)
    {
        var errors = new List<string>();

        string hubIndexFile = Path.Combine(adrDir, "index.md");
        if (!File.Exists(hubIndexFile))
        {
            errors.Add($"ADR index '{hubIndexFile}' not found.");
            return errors;
        }

        ValidateIndexLinks(hubIndexFile, "index.md", errors);

        string legacyIndexFile = Path.Combine(adrDir, "legacy-index.md");
        bool hasLegacyIndex = File.Exists(legacyIndexFile);

        // Reject unrecognized subdirectories under docs/adr/
        foreach (string subDir in Directory.GetDirectories(adrDir).Order(StringComparer.Ordinal))
        {
            string subName = Path.GetFileName(subDir);
            if (subName.StartsWith('.'))
            {
                continue;
            }

            if (!DomainsBySlug.ContainsKey(subName))
            {
                errors.Add($"Unknown ADR domain directory '{subName}' under docs/adr/; expected one of: {string.Join(", ", DomainSlugs)}.");
            }
        }

        var filesByNumber = new SortedDictionary<int, List<string>>();
        var filesById = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        static void RegisterId(Dictionary<string, List<string>> map, string id, string relFile)
        {
            if (!map.TryGetValue(id, out var holders))
            {
                holders = [];
                map[id] = holders;
            }

            holders.Add(relFile);
        }

        // 1. Validate frozen legacy ADRs in root docs/adr/*.md
        var legacyAdrFiles = Directory.GetFiles(adrDir, "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !RootIgnoreFiles.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        foreach (string name in legacyAdrFiles)
        {
            string firstLine = File.ReadLines(Path.Combine(adrDir, name)).FirstOrDefault() ?? "";
            int? number = ParseAdrNumber(firstLine);
            if (number is null)
            {
                errors.Add($"File '{name}' has no '# ADR-NNNN: Title' heading on line 1 (found '{firstLine.TrimEnd('\r')}').");
                continue;
            }

            if (!filesByNumber.TryGetValue(number.Value, out var numHolders))
            {
                numHolders = [];
                filesByNumber[number.Value] = numHolders;
            }

            numHolders.Add(name);
            string canonicalLegacyId = number.Value < 1000 ? $"ADR-{number.Value:D3}" : $"ADR-{number.Value}";
            RegisterId(filesById, canonicalLegacyId, name);
        }

        string legacySourceFile = hasLegacyIndex ? legacyIndexFile : hubIndexFile;
        string legacySourceLabel = hasLegacyIndex ? "legacy-index.md" : "index.md";
        if (hasLegacyIndex)
        {
            ValidateIndexLinks(legacyIndexFile, "legacy-index.md", errors);

            // Ensure root index.md stays static and does not directly list per-ADR YYYY-MM-DD-*.md bullets.
            string[] hubLines = File.ReadAllLines(hubIndexFile);
            for (int i = 0; i < hubLines.Length; i++)
            {
                var hubBullet = IndexEntryPattern.Match(hubLines[i]);
                if (!hubBullet.Success)
                {
                    continue;
                }

                string target = hubBullet.Groups[1].Value.Split('#')[0].Trim();
                if (target.StartsWith("./", StringComparison.Ordinal))
                {
                    target = target[2..];
                }

                string targetFileName = Path.GetFileName(target);
                if (FilenamePattern.IsMatch(targetFileName))
                {
                    errors.Add($"index.md line {i + 1} lists ADR '{target}' directly; place new ADRs in docs/adr/<domain>/index.md and keep legacy ADRs in legacy-index.md.");
                }
            }
        }

        var legacyListedCounts = ParseAndValidateShardBullets(
            legacySourceFile,
            legacySourceLabel,
            ignoreNonAdrNavBullets: true,
            errors);

        foreach (string name in legacyAdrFiles)
        {
            int count = legacyListedCounts.GetValueOrDefault(name);
            if (count == 0)
            {
                errors.Add($"File '{name}' is missing from {legacySourceLabel}.");
            }
            else if (count > 1)
            {
                errors.Add($"File '{name}' is listed {count} times in {legacySourceLabel}; list each ADR exactly once.");
            }
        }

        // 2. Validate each of the 8 domain shards under docs/adr/<domain>/
        foreach (var domain in DomainDefinitions)
        {
            string domainDir = Path.Combine(adrDir, domain.Slug);
            if (!Directory.Exists(domainDir))
            {
                continue;
            }

            string shardIndexFile = Path.Combine(domainDir, "index.md");
            string shardLabel = $"{domain.Slug}/index.md";
            if (!File.Exists(shardIndexFile))
            {
                errors.Add($"Domain ADR shard index '{shardLabel}' not found.");
                continue;
            }

            ValidateIndexLinks(shardIndexFile, shardLabel, errors);

            var shardListedCounts = ParseAndValidateShardBullets(
                shardIndexFile,
                shardLabel,
                ignoreNonAdrNavBullets: true,
                errors);

            var domainFiles = Directory.GetFiles(domainDir, "*.md", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !ShardIgnoreFiles.Contains(name))
                .Order(StringComparer.Ordinal)
                .ToList();

            string expectedDomainToken = domain.Slug.ToUpperInvariant();
            foreach (string name in domainFiles)
            {
                string relFile = $"{domain.Slug}/{name}";
                string firstLine = File.ReadLines(Path.Combine(domainDir, name)).FirstOrDefault()?.TrimEnd('\r') ?? "";

                int? accidentalLegacyNum = ParseAdrNumber(firstLine);
                if (accidentalLegacyNum is not null)
                {
                    if (!filesByNumber.TryGetValue(accidentalLegacyNum.Value, out var numHolders))
                    {
                        numHolders = [];
                        filesByNumber[accidentalLegacyNum.Value] = numHolders;
                    }

                    numHolders.Add(relFile);
                }

                var header = ParseDomainAdrHeader(firstLine);
                if (header is null)
                {
                    errors.Add($"File '{relFile}' has no '# ADR-{expectedDomainToken}-YYYYMMDD-<slug>: <title>' heading on line 1 (found '{firstLine}').");
                }
                else
                {
                    RegisterId(filesById, header.Id, relFile);

                    if (!string.Equals(header.Domain, expectedDomainToken, StringComparison.Ordinal))
                    {
                        errors.Add($"File '{relFile}' heading domain '{header.Domain}' does not match directory domain '{expectedDomainToken}'.");
                    }

                    if (FilenamePattern.IsMatch(name))
                    {
                        string expectedCompactDate = name[..10].Replace("-", "", StringComparison.Ordinal);
                        string expectedSlug = Path.GetFileNameWithoutExtension(name)[11..];
                        if (!string.Equals(header.CompactDate, expectedCompactDate, StringComparison.Ordinal) ||
                            !string.Equals(header.Slug, expectedSlug, StringComparison.Ordinal))
                        {
                            errors.Add($"File '{relFile}' heading identifier '{header.Id}' does not match filename date/slug 'ADR-{expectedDomainToken}-{expectedCompactDate}-{expectedSlug}'.");
                        }
                    }
                }

                int count = shardListedCounts.GetValueOrDefault(name);
                if (count == 0)
                {
                    errors.Add($"File '{relFile}' is missing from {shardLabel}.");
                }
                else if (count > 1)
                {
                    errors.Add($"File '{relFile}' is listed {count} times in {shardLabel}; list each ADR exactly once.");
                }
            }
        }

        // 3. Report duplicate legacy numbers or duplicate ADR IDs across all shards
        var reportedDuplicateFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (number, holders) in filesByNumber.Where(kv => kv.Value.Count > 1))
        {
            errors.Add($"ADR-{number:D3} is used by {holders.Count} files: {string.Join(", ", holders)}.");
            reportedDuplicateFiles.Add(string.Join("|", holders));
        }

        foreach (var (id, holders) in filesById
                     .Where(kv => kv.Value.Count > 1)
                     .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            string key = string.Join("|", holders);
            if (reportedDuplicateFiles.Contains(key))
            {
                continue;
            }

            errors.Add($"{id} is used by {holders.Count} files: {string.Join(", ", holders)}.");
        }

        return errors;
    }

    private static void ValidateIndexLinks(string indexFilePath, string indexLabel, List<string> errors)
    {
        string baseDir = Path.GetDirectoryName(indexFilePath)!;
        string[] lines = File.ReadAllLines(indexFilePath);
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match link in MarkdownLinkPattern.Matches(lines[i]))
            {
                string target = link.Groups[1].Value;
                if (target.Contains("://", StringComparison.Ordinal) ||
                    target.StartsWith('#') ||
                    target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string pathPart = target.Split('#')[0];
                if (string.IsNullOrWhiteSpace(pathPart))
                {
                    continue;
                }

                string resolved = Path.GetFullPath(Path.Combine(baseDir, pathPart));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    errors.Add($"{indexLabel} line {i + 1} links to '{target}', which does not exist.");
                }
            }
        }
    }

    private static Dictionary<string, int> ParseAndValidateShardBullets(
        string indexFilePath,
        string indexLabel,
        bool ignoreNonAdrNavBullets,
        List<string> errors)
    {
        var listedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(indexFilePath);
        string? prevInSection = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith('#'))
            {
                prevInSection = null;
                continue;
            }

            var entry = IndexEntryPattern.Match(lines[i]);
            if (!entry.Success)
            {
                continue;
            }

            string rawTarget = entry.Groups[1].Value.Split('#')[0].Trim();
            if (rawTarget.StartsWith("./", StringComparison.Ordinal))
            {
                rawTarget = rawTarget[2..];
            }

            if (ignoreNonAdrNavBullets &&
                (rawTarget.StartsWith("../", StringComparison.Ordinal) ||
                 rawTarget.Contains("://", StringComparison.Ordinal) ||
                 string.Equals(rawTarget, "TEMPLATE.md", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(rawTarget, "index.md", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(rawTarget, "legacy-index.md", StringComparison.OrdinalIgnoreCase) ||
                 rawTarget.EndsWith("/index.md", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            listedCounts[rawTarget] = listedCounts.GetValueOrDefault(rawTarget) + 1;

            if (prevInSection is not null &&
                StringComparer.Ordinal.Compare(prevInSection, rawTarget) > 0)
            {
                errors.Add($"{indexLabel} line {i + 1}: '{rawTarget}' is not sorted alphabetically by filename (appears after '{prevInSection}').");
            }

            prevInSection = rawTarget;
        }

        return listedCounts;
    }

    private static string BuildGitHubHeadingSlug(string heading)
    {
        var chars = new List<char>(heading.Length);
        foreach (char ch in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
            {
                chars.Add(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                chars.Add('-');
            }
        }

        return new string([.. chars]);
    }

    private static readonly HashSet<string> FrontendExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vue", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".css", ".scss", ".html",
    };

    private static bool CheckArchitecturalChangesRequireAdr(
        string repoRoot,
        bool preCommit,
        string? baseRef,
        string? pushBefore,
        string? eventName,
        string? gitRef)
    {
        var (changedFiles, adrCandidateFiles, compareBase) = ResolveChangedAndAdrFiles(repoRoot, preCommit, baseRef, pushBefore, eventName, gitRef);
        var architecturalChanges = changedFiles.Where(f => IsArchitecturalFile(f, repoRoot, preCommit, compareBase)).ToList();
        if (architecturalChanges.Count == 0) return true;

        bool newAdrAdded = adrCandidateFiles.Any(f => IsExistingAdrFile(f, repoRoot, preCommit));
        if (!newAdrAdded)
        {
            AnsiConsole.MarkupLine("[red]Error: Architectural changes detected, but no corresponding ADR found in docs/adr/<domain>/.[/]");
            AnsiConsole.MarkupLine("Architectural files changed:");
            foreach (var f in architecturalChanges)
            {
                AnsiConsole.MarkupLine($"  - {f}");
            }
            AnsiConsole.MarkupLine("Please scaffold a new ADR with 'pnpm cadence scaffold adr --domain <domain> \"<title>\"'.");
            return false;
        }
        return true;
    }

    private static bool IsExistingAdrFile(string relativePath, string repoRoot, bool preCommit)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (!normalized.StartsWith("docs/adr/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileName(normalized);
        if (!FilenamePattern.IsMatch(fileName))
        {
            return false;
        }

        if (preCommit && GitRevisionScopeService.ReadFileAtRevision(repoRoot, "", normalized) is not null)
        {
            return true;
        }

        return File.Exists(normalized) || File.Exists(Path.Combine(repoRoot, normalized));
    }

    public static bool IsArchitecturalFile(string filepath, string repoRoot, bool preCommit = false, string? compareBase = null)
    {
        string normalized = filepath.Replace('\\', '/');
        if (normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            (Path.GetFileName(normalized).StartsWith('.') && normalized.EndsWith("ignore", StringComparison.OrdinalIgnoreCase)) ||
            normalized.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(normalized, "package.json", StringComparison.OrdinalIgnoreCase))
        {
            return RootPackageJsonDependenciesChanged(repoRoot, compareBase, preCommit);
        }

        if (normalized.StartsWith("apps/gateway/", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalized.StartsWith("packages/security/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("packages/ui/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.StartsWith("apps/web/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("apps/subject-portal/", StringComparison.OrdinalIgnoreCase) ||
            FrontendExtensions.Contains(Path.GetExtension(normalized)))
        {
            return false;
        }

        if (normalized.StartsWith("apps/", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(normalized, @"(?:database|schema|model|migration|\bdb\b)", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    // Only dependency and toolchain sections of the root manifest count as architectural, so script edits need no ADR.
    private static readonly string[] PackageJsonDependencySections =
    [
        "dependencies", "devDependencies", "peerDependencies", "optionalDependencies",
        "overrides", "resolutions", "pnpm", "packageManager", "engines", "workspaces",
    ];

    public static bool PackageJsonDependenciesChanged(string? before, string? after)
    {
        if (before is null || after is null) return true;

        try
        {
            JsonNode? beforeRoot = JsonNode.Parse(before);
            JsonNode? afterRoot = JsonNode.Parse(after);
            if (beforeRoot is not JsonObject beforeObject || afterRoot is not JsonObject afterObject) return true;

            return PackageJsonDependencySections.Any(section =>
                !JsonNode.DeepEquals(beforeObject[section], afterObject[section]));
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool RootPackageJsonDependenciesChanged(string repoRoot, string? compareBase, bool preCommit)
    {
        string baselineRevision = string.IsNullOrEmpty(compareBase) ? "HEAD" : compareBase;
        string? before = GitRevisionScopeService.ReadFileAtRevision(repoRoot, baselineRevision, "package.json");
        string? after;
        if (preCommit)
        {
            after = GitRevisionScopeService.ReadFileAtRevision(repoRoot, "", "package.json");
        }
        else
        {
            string workingPath = Path.Combine(repoRoot, "package.json");
            after = File.Exists(workingPath) ? File.ReadAllText(workingPath) : null;
        }

        return PackageJsonDependenciesChanged(before, after);
    }

    private static (List<string> ChangedFiles, List<string> AdrCandidateFiles, string? CompareBase) ResolveChangedAndAdrFiles(
        string repoRoot,
        bool preCommit,
        string? baseRef,
        string? pushBefore,
        string? eventName,
        string? gitRef)
    {
        var changed = new List<string>();
        var adrCandidates = new List<string>();

        string effectiveBaseRef = GitRevisionScopeService.NormalizeBaseRef(baseRef);
        string? headCommit = GitRevisionScopeService.TryResolveCommitSha(repoRoot, "HEAD");

        if (preCommit)
        {
            foreach (string staged in GitRevisionScopeService.GetStagedFiles(repoRoot, includeDeletions: false))
            {
                changed.Add(staged);
                adrCandidates.Add(staged);
            }

            string? branchCompareBase = GitRevisionScopeService.TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
            if (!string.IsNullOrEmpty(branchCompareBase) &&
                !string.IsNullOrEmpty(headCommit) &&
                !string.Equals(branchCompareBase, headCommit, StringComparison.Ordinal))
            {
                foreach (string committed in GitRevisionScopeService.GetChangedFilesBetweenCommits(repoRoot, branchCompareBase, headCommit, includeDeletions: false))
                {
                    adrCandidates.Add(committed);
                }
            }

            return (changed, adrCandidates, branchCompareBase);
        }

        var eventScope = GitRevisionScopeService.ResolveEventScope(repoRoot, effectiveBaseRef, pushBefore, eventName, gitRef);
        string? compareBase = eventScope.Kind switch
        {
            GitEventScopeKind.TargetRef or GitEventScopeKind.UnprotectedTargetRef => null,
            _ => eventScope.CompareBase,
        };

        if (!string.IsNullOrEmpty(compareBase) &&
            !string.IsNullOrEmpty(headCommit) &&
            !string.Equals(compareBase, headCommit, StringComparison.Ordinal))
        {
            IEnumerable<string> committedChanges = GitRevisionScopeService.GetChangedFilesBetweenCommits(
                repoRoot,
                compareBase,
                headCommit,
                includeDeletions: false);

            if (eventScope.Kind == GitEventScopeKind.BranchDiff)
            {
                var differsFromBaseRef = GitRevisionScopeService.TryGetPathsDifferingFromRevision(repoRoot, effectiveBaseRef, cached: false);
                if (differsFromBaseRef is not null)
                {
                    var differingSet = new HashSet<string>(differsFromBaseRef, StringComparer.OrdinalIgnoreCase);
                    committedChanges = committedChanges.Where(differingSet.Contains);
                }
            }

            foreach (string file in committedChanges)
            {
                changed.Add(file);
                adrCandidates.Add(file);
            }
        }

        foreach (string workingFile in GitRevisionScopeService.GetWorkingTreeChanges(repoRoot, includeDeletions: false, includeUntracked: true))
        {
            changed.Add(workingFile);
            adrCandidates.Add(workingFile);
        }

        return (changed, adrCandidates, compareBase);
    }

    private static HashSet<string> GetValidRequirements(string repoRoot)
    {
        string docsDir = Path.Combine(repoRoot, "docs");

        var reqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(docsDir)) return reqs;

        var files = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);
        var prdRegex = new Regex(@"\b(PRD-[A-Z0-9]+-\d+)\b", RegexOptions.Compiled);
        var traceRegex = new Regex(@"\bTrace-(\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        foreach (var file in files)
        {
            if (Path.GetFileName(file) == "Requirements_Traceability_Matrix.md" ||
                Path.GetFileName(file) == "IQ_OQ_PQ_Execution_Report.md")
            {
                continue;
            }

            try
            {
                string content = File.ReadAllText(file);
                foreach (Match m in prdRegex.Matches(content).Cast<Match>()) reqs.Add(m.Value.ToUpperInvariant());
                foreach (Match m in traceRegex.Matches(content).Cast<Match>()) reqs.Add($"Trace-{m.Groups[1].Value}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read requirement file {file}: {ex.Message}");
            }
        }

        return reqs;
    }

    private static HashSet<string> ExtractRequirementReferences(string content)
    {
        var prdRegex = new Regex(@"\bprd-([a-z0-9]+)-([a-z0-9]+)\b", RegexOptions.IgnoreCase);
        var traceRegex = new Regex(@"\btrace\s*[-]?\s*(\d+)\b", RegexOptions.IgnoreCase);

        var prdRefs = prdRegex.Matches(content)
            .Cast<Match>()
            .Select(m => $"PRD-{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value.ToUpperInvariant()}");

        var traceRefs = traceRegex.Matches(content)
            .Cast<Match>()
            .Select(m => $"Trace-{m.Groups[1].Value}");

        return new HashSet<string>(prdRefs.Concat(traceRefs), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPost2026Adr(string filename)
    {
        var match = Regex.Match(filename, @"^(\d{4})-\d{2}-\d{2}");
        if (!match.Success) return false;
        int year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (year < 2026) return false;
        return !(year == 2026 && BaselineFiles.Contains(filename));
    }
}
