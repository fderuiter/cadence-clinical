using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

using Spectre.Console;

namespace Cadence.ToolingCore.Services;

public static partial class AdrValidatorService
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

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}-.+\.md$", RegexOptions.CultureInvariant)]
    private static partial Regex FilenameRegex();

    // Legacy line 1: "# ADR-NNNN: Title" (at least three digits, e.g. ADR-049 or ADR-2250).
    [GeneratedRegex(@"^# ADR-(\d{3,}): \S.*$", RegexOptions.CultureInvariant)]
    private static partial Regex AdrHeadingRegex();

    // Domain-sharded line 1: "# ADR-<DOMAIN>-YYYYMMDD-<slug>: Title".
    [GeneratedRegex(@"^# (ADR-([A-Z]+)-(\d{8})-([a-z0-9-]+)): (\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainAdrHeadingRegex();

    [GeneratedRegex(@"\b(PRD-[A-Z0-9]+-\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex PrdDefinitionRegex();

    [GeneratedRegex(@"\bTrace-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TraceDefinitionRegex();

    [GeneratedRegex(@"\bprd-([a-z0-9]+)-([a-z0-9]+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrdReferenceRegex();

    [GeneratedRegex(@"\btrace\s*[-]?\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TraceReferenceRegex();

    [GeneratedRegex(@"^(\d{4})-\d{2}-\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex YearPrefixRegex();

    private static readonly string[] RequiredSectionsOld =
    [
        "## Status",
        "## Context",
        "## Decision",
        "## Alternatives Considered",
        "## Trade-offs",
    ];

    private static readonly string[] RequiredSectionsNew =
    [
        "## 1. Context & Problem Statement",
        "## 2. Decision Drivers & Constraints",
        "## 3. Options Considered",
        "## 4. Decision Outcome",
        "## 5. Consequences & Trade-offs",
        "## 6. Implementation & Verification",
    ];

    public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();

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
            if (!FilenameRegex().IsMatch(filename))
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
                .Where(name => !ShardIgnoreFiles.Contains(name) && FilenameRegex().IsMatch(name))
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
        var match = AdrHeadingRegex().Match(headingLine.TrimEnd('\r'));
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
        var match = DomainAdrHeadingRegex().Match(headingLine.TrimEnd('\r'));
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

    private static HashSet<string> GetValidRequirements(string repoRoot)
    {
        string docsDir = Path.Combine(repoRoot, "docs");

        var reqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(docsDir)) return reqs;

        var files = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);

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
                foreach (Match m in PrdDefinitionRegex().Matches(content)) reqs.Add(m.Value.ToUpperInvariant());
                foreach (Match m in TraceDefinitionRegex().Matches(content)) reqs.Add($"Trace-{m.Groups[1].Value}");
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
        var prdRefs = PrdReferenceRegex().Matches(content)
            .Select(m => $"PRD-{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value.ToUpperInvariant()}");

        var traceRefs = TraceReferenceRegex().Matches(content)
            .Select(m => $"Trace-{m.Groups[1].Value}");

        return new HashSet<string>(prdRefs.Concat(traceRefs), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPost2026Adr(string filename)
    {
        var match = YearPrefixRegex().Match(filename);
        if (!match.Success) return false;
        int year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (year < 2026) return false;
        return !(year == 2026 && BaselineFiles.Contains(filename));
    }
}
