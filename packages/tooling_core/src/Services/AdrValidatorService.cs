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
    private static readonly HashSet<string> IgnoreFiles = new(StringComparer.OrdinalIgnoreCase) { "TEMPLATE.md", "index.md" };

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
    // Line 1 of every ADR: "# ADR-NNNN: Title" (at least three digits, e.g. ADR-049 or ADR-2250).
    private static readonly Regex AdrHeadingPattern = new(@"^# ADR-(\d{3,}): \S.*$", RegexOptions.Compiled);
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

    public static bool ValidateAdrs(
        bool checkGitDiff = true,
        string[]? targets = null,
        string? repoRoot = null,
        bool preCommit = false,
        string? baseRef = null,
        string? pushBefore = null)
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

        // 1. Directory-wide integrity: unique numbers, "# ADR-NNNN:" headings, index coverage and links.
        foreach (var error in FindIntegrityErrors(adrDir))
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(error)}[/]");
            allPassed = false;
        }

        var filesToValidate = targets != null && targets.Length > 0
            ? targets.Where(File.Exists).ToList()
            : Directory.GetFiles(adrDir, "*.md", SearchOption.TopDirectoryOnly).ToList();

        foreach (var filepath in filesToValidate)
        {
            string filename = Path.GetFileName(filepath);
            if (IgnoreFiles.Contains(filename)) continue;

            // 2. Check filename pattern
            if (!FilenamePattern.IsMatch(filename))
            {
                AnsiConsole.MarkupLine($"[red]Error: File '{Markup.Escape(filename)}' does not follow the standard date pattern (YYYY-MM-DD-...).[/]");
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

            // Legacy ADRs use the "## Status / ## Context / ..." template; all others use the numbered template.
            bool isLegacyTemplate = !content.Contains(RequiredSectionsNew[0]) &&
                content.Split('\n').Any(l => l.TrimEnd() == "## Status");
            var requiredSections = isLegacyTemplate ? RequiredSectionsOld : RequiredSectionsNew;
            var missingSections = requiredSections.Where(s => !content.Contains(s)).ToList();
            if (missingSections.Count > 0)
            {
                AnsiConsole.MarkupLine($"[red]Error: File '{Markup.Escape(filename)}' missing required sections: {Markup.Escape(string.Join(", ", missingSections))}[/]");
                allPassed = false;
            }

            // 4. Validate requirement links
            var refs = ExtractRequirementReferences(content);
            if (IsPost2026Adr(filename))
            {
                if (refs.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[red]Error: Post-2026 ADR file '{filename}' lacks a requirement reference.[/]");
                    allPassed = false;
                }
                else
                {
                    var invalidRefs = refs.Where(r => !validReqs.Contains(r)).ToList();
                    if (invalidRefs.Count > 0)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Post-2026 ADR '{filename}' references invalid requirement(s): {string.Join(", ", invalidRefs)}[/]");
                        allPassed = false;
                    }
                }
            }
        }

        if (checkGitDiff && !CheckArchitecturalChangesRequireAdr(repoRoot, preCommit, baseRef, pushBefore))
        {
            allPassed = false;
        }

        if (allPassed)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] All ADRs compliant.[/]");
        }

        return allPassed;
    }

    /// <summary>
    /// Appends any unlisted numbered ADR markdown files in <c>docs/adr/</c> to <c>docs/adr/index.md</c>.
    /// </summary>
    public static int SyncAdrIndex(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        string adrDir = Path.Combine(repoRoot, "docs", "adr");
        string indexFile = Path.Combine(adrDir, "index.md");
        if (!Directory.Exists(adrDir) || !File.Exists(indexFile))
        {
            return 0;
        }

        string[] indexLines = File.ReadAllLines(indexFile);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in indexLines)
        {
            var entry = IndexEntryPattern.Match(line);
            if (entry.Success)
            {
                listed.Add(entry.Groups[1].Value.Split('#')[0]);
            }
        }

        var adrFiles = Directory.GetFiles(adrDir, "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !IgnoreFiles.Contains(name) && FilenamePattern.IsMatch(name) && !listed.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (adrFiles.Count == 0)
        {
            return 0;
        }

        string indexContent = File.ReadAllText(indexFile);
        var additions = new List<string>();
        foreach (string name in adrFiles)
        {
            string firstLine = File.ReadLines(Path.Combine(adrDir, name)).FirstOrDefault()?.TrimEnd('\r') ?? "";
            int? number = ParseAdrNumber(firstLine);
            if (number is null)
            {
                continue;
            }

            int colonIdx = firstLine.IndexOf(':', StringComparison.Ordinal);
            string title = colonIdx >= 0 ? firstLine[(colonIdx + 1)..].Trim() : name;
            string datePart = name.Length >= 10 ? name[..10] : "";
            additions.Add(string.IsNullOrEmpty(datePart)
                ? $"- [ADR-{number.Value}: {title}]({name})"
                : $"- [ADR-{number.Value}: {title}]({name}) ({datePart})");
        }

        if (additions.Count > 0)
        {
            string separator = indexContent.EndsWith('\n') ? "" : "\n";
            File.AppendAllText(indexFile, $"{separator}{string.Join('\n', additions)}\n");
            AnsiConsole.MarkupLine($"  [green][[OK]] Added {additions.Count} missing ADR entry(ies) to docs/adr/index.md.[/]");
        }

        return 0;
    }

    /// <summary>
    /// Parses the ADR number from an ADR's first line (<c># ADR-NNNN: Title</c>).
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
    /// Checks the ADR directory as a whole: every ADR opens with a <c># ADR-NNNN: Title</c> heading,
    /// no two ADRs share a number, every ADR is listed exactly once in <c>index.md</c>, and every
    /// relative link in <c>index.md</c> resolves to an existing file.
    /// </summary>
    /// <returns>One message per problem found; empty when the directory is consistent.</returns>
    public static IReadOnlyList<string> FindIntegrityErrors(string adrDir)
    {
        var errors = new List<string>();
        var adrFiles = Directory.GetFiles(adrDir, "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !IgnoreFiles.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        var filesByNumber = new SortedDictionary<int, List<string>>();
        foreach (var name in adrFiles)
        {
            string firstLine = File.ReadLines(Path.Combine(adrDir, name)).FirstOrDefault() ?? "";
            int? number = ParseAdrNumber(firstLine);
            if (number is null)
            {
                errors.Add($"File '{name}' has no '# ADR-NNNN: Title' heading on line 1 (found '{firstLine.TrimEnd('\r')}').");
                continue;
            }

            if (!filesByNumber.TryGetValue(number.Value, out var holders))
            {
                holders = [];
                filesByNumber[number.Value] = holders;
            }
            holders.Add(name);
        }

        foreach (var (number, holders) in filesByNumber.Where(kv => kv.Value.Count > 1))
        {
            errors.Add($"ADR-{number:D3} is used by {holders.Count} files: {string.Join(", ", holders)}.");
        }

        string indexFile = Path.Combine(adrDir, "index.md");
        if (!File.Exists(indexFile))
        {
            errors.Add($"ADR index '{indexFile}' not found.");
            return errors;
        }

        var listedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        string[] indexLines = File.ReadAllLines(indexFile);
        for (int i = 0; i < indexLines.Length; i++)
        {
            foreach (Match link in MarkdownLinkPattern.Matches(indexLines[i]))
            {
                string target = link.Groups[1].Value;
                if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith('#') ||
                    target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string path = target.Split('#')[0];
                if (!File.Exists(Path.GetFullPath(Path.Combine(adrDir, path))))
                {
                    errors.Add($"index.md line {i + 1} links to '{target}', which does not exist.");
                }
            }

            var entry = IndexEntryPattern.Match(indexLines[i]);
            if (entry.Success)
            {
                string listed = entry.Groups[1].Value.Split('#')[0];
                listedCounts[listed] = listedCounts.GetValueOrDefault(listed) + 1;
            }
        }

        foreach (var name in adrFiles)
        {
            int count = listedCounts.GetValueOrDefault(name);
            if (count == 0)
            {
                errors.Add($"File '{name}' is missing from index.md.");
            }
            else if (count > 1)
            {
                errors.Add($"File '{name}' is listed {count} times in index.md; list each ADR exactly once.");
            }
        }

        return errors;
    }

    private static readonly HashSet<string> FrontendExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vue", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".css", ".scss", ".html",
    };

    private static bool CheckArchitecturalChangesRequireAdr(
        string repoRoot,
        bool preCommit,
        string? baseRef,
        string? pushBefore)
    {
        var (changedFiles, adrCandidateFiles, compareBase) = ResolveChangedAndAdrFiles(repoRoot, preCommit, baseRef, pushBefore);
        var architecturalChanges = changedFiles.Where(f => IsArchitecturalFile(f, repoRoot, preCommit, compareBase)).ToList();
        if (architecturalChanges.Count == 0) return true;

        bool newAdrAdded = adrCandidateFiles.Any(f => IsExistingAdrFile(f, repoRoot, preCommit));
        if (!newAdrAdded)
        {
            AnsiConsole.MarkupLine("[red]Error: Architectural changes detected, but no corresponding ADR found in docs/adr/.[/]");
            AnsiConsole.MarkupLine("Architectural files changed:");
            foreach (var f in architecturalChanges)
            {
                AnsiConsole.MarkupLine($"  - {f}");
            }
            AnsiConsole.MarkupLine("Please add a new ADR markdown file under docs/adr/ following the template.");
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

    private static (HashSet<string> ChangedFiles, HashSet<string> AdrCandidateFiles, string? CompareBase) ResolveChangedAndAdrFiles(
        string repoRoot,
        bool preCommit,
        string? baseRef,
        string? pushBefore)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var adrCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string changedFilesTxt = Path.Combine(repoRoot, "changed_files.txt");
        if (File.Exists(changedFilesTxt))
        {
            foreach (var line in File.ReadAllLines(changedFilesTxt))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    string entry = line.Trim().Replace('\\', '/');
                    changed.Add(entry);
                    adrCandidates.Add(entry);
                }
            }
        }

        if (!GitRevisionScopeService.HasGitRepository(repoRoot))
        {
            return (changed, adrCandidates, null);
        }

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

        string? compareBase = !string.IsNullOrWhiteSpace(pushBefore)
            ? GitRevisionScopeService.TryResolveValidPushBase(repoRoot, pushBefore, headCommit)
            : GitRevisionScopeService.TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);

        if (!string.IsNullOrEmpty(compareBase) &&
            !string.IsNullOrEmpty(headCommit) &&
            !string.Equals(compareBase, headCommit, StringComparison.Ordinal))
        {
            IEnumerable<string> committedChanges = GitRevisionScopeService.GetChangedFilesBetweenCommits(
                repoRoot,
                compareBase,
                headCommit,
                includeDeletions: false);

            // When comparing against a branch ref (for example after merging origin/main into a feature branch), exclude files
            // whose content matches baseRef verbatim so upstream architectural commits do not require a second ADR on the branch.
            if (string.IsNullOrWhiteSpace(pushBefore))
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
        int year = int.Parse(match.Groups[1].Value);
        if (year < 2026) return false;
        return !(year == 2026 && BaselineFiles.Contains(filename));
    }
}
