using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private static readonly Regex TitlePatternOld = new(@"^# .*\d{4}-\d{2}-\d{2}.*$", RegexOptions.Compiled);
    private static readonly Regex TitlePatternNew = new(@"^# ADR-(?:\d+|\[NUMBER\]): .*$", RegexOptions.Compiled);

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

    public static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    public static bool ValidateAdrs(bool checkGitDiff = true, string[]? targets = null)
    {
        string repoRoot = FindRepoRoot();
        string adrDir = Path.Combine(repoRoot, "docs", "adr");
        string indexFile = Path.Combine(adrDir, "index.md");

        AnsiConsole.MarkupLine("[cyan]Validating Architectural Decision Records (ADRs)...[/]");

        if (!Directory.Exists(adrDir))
        {
            AnsiConsole.MarkupLine($"[red]Error: Directory {adrDir} not found.[/]");
            return false;
        }

        string indexContent = "";
        try
        {
            if (File.Exists(indexFile))
            {
                indexContent = File.ReadAllText(indexFile);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error reading {indexFile}: {ex.Message}[/]");
            return false;
        }

        var validReqs = GetValidRequirements();
        bool allPassed = true;

        var filesToValidate = targets != null && targets.Length > 0
            ? targets.Where(File.Exists).ToList()
            : Directory.GetFiles(adrDir, "*.md", SearchOption.TopDirectoryOnly).ToList();

        foreach (var filepath in filesToValidate)
        {
            string filename = Path.GetFileName(filepath);
            if (IgnoreFiles.Contains(filename)) continue;

            // 1. Check filename pattern
            if (!FilenamePattern.IsMatch(filename))
            {
                AnsiConsole.MarkupLine($"[red]Error: File '{filename}' does not follow the standard date pattern (YYYY-MM-DD-...).[/]");
                allPassed = false;
            }

            // 2. Check if in index
            if (!indexContent.Contains($"({filename})"))
            {
                AnsiConsole.MarkupLine($"[red]Error: File '{filename}' is missing from index ({indexFile}).[/]");
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
                AnsiConsole.MarkupLine($"[red]Error reading '{filepath}': {ex.Message}[/]");
                allPassed = false;
                continue;
            }

            var lines = content.Split('\n');
            bool isNewFormat = false;
            if (lines.Length > 0 && TitlePatternNew.IsMatch(lines[0]))
            {
                isNewFormat = true;
            }
            else if (lines.Length == 0 || !TitlePatternOld.IsMatch(lines[0]))
            {
                AnsiConsole.MarkupLine($"[red]Error: File '{filename}' title (line 1) does not match required format.[/]");
                allPassed = false;
            }

            var requiredSections = isNewFormat ? RequiredSectionsNew : RequiredSectionsOld;
            var missingSections = requiredSections.Where(s => !content.Contains(s)).ToList();
            if (missingSections.Count > 0)
            {
                AnsiConsole.MarkupLine($"[red]Error: File '{filename}' missing required sections: {string.Join(", ", missingSections)}[/]");
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

        if (checkGitDiff && !CheckArchitecturalChangesRequireAdr())
        {
            allPassed = false;
        }

        if (allPassed)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] All ADRs compliant.[/]");
        }

        return allPassed;
    }

    private static bool CheckArchitecturalChangesRequireAdr()
    {
        string repoRoot = FindRepoRoot();
        var changedFiles = GetChangedFiles();
        var architecturalChanges = changedFiles.Where(IsArchitecturalFile).ToList();
        if (architecturalChanges.Count == 0) return true;

        bool newAdrAdded = changedFiles.Any(f => f.StartsWith("docs/adr/") && FilenamePattern.IsMatch(Path.GetFileName(f)) && (File.Exists(f) || File.Exists(Path.Combine(repoRoot, f))));
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

    private static bool IsArchitecturalFile(string filepath)
    {
        if (filepath.StartsWith("tests/") || filepath.Contains("/tests/") ||
            filepath.StartsWith("docs/") || filepath.StartsWith("scripts/") ||
            filepath.StartsWith(".github/") || filepath.EndsWith(".md") ||
            filepath.EndsWith(".txt") || filepath.EndsWith(".gitignore") ||
            filepath.EndsWith(".d.ts"))
        {
            return false;
        }

        if (filepath == "package.json") return RootPackageJsonDependenciesChanged();
        if (filepath.StartsWith("apps/gateway/")) return true;
        if (filepath.StartsWith("packages/security/") || filepath.StartsWith("packages/ui/")) return true;

        if (filepath.StartsWith("apps/") && Regex.IsMatch(filepath, @"(?:database|schema|model|migration|\bdb\b)", RegexOptions.IgnoreCase))
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

    private static bool RootPackageJsonDependenciesChanged()
    {
        string repoRoot = FindRepoRoot();
        string workingPath = Path.Combine(repoRoot, "package.json");
        string? after = File.Exists(workingPath) ? File.ReadAllText(workingPath) : null;
        return PackageJsonDependenciesChanged(ReadCommittedFile(repoRoot, "package.json"), after);
    }

    private static string? ReadCommittedFile(string repoRoot, string relativePath)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"show HEAD:{relativePath}")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0 ? output : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to read committed {relativePath}: {ex.Message}");
            return null;
        }
    }

    private static HashSet<string> GetChangedFiles()
    {
        string repoRoot = FindRepoRoot();
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string changedFilesTxt = Path.Combine(repoRoot, "changed_files.txt");
        if (File.Exists(changedFilesTxt))
        {
            foreach (var line in File.ReadAllLines(changedFilesTxt))
            {
                if (!string.IsNullOrWhiteSpace(line)) changed.Add(line.Trim());
            }
            if (changed.Count > 0) return changed;
        }

        try
        {
            var psi = new ProcessStartInfo("git", "status --porcelain")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                foreach (var line in output.Split('\n'))
                {
                    if (line.Length > 3)
                    {
                        string filepath = line[3..].Trim().Trim('"');
                        if (filepath.Contains(" -> ")) filepath = filepath.Split(" -> ")[1].Trim();
                        if (!string.IsNullOrWhiteSpace(filepath)) changed.Add(filepath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to retrieve changed files via git: {ex.Message}");
        }

        return changed;
    }

    private static HashSet<string> GetValidRequirements()
    {
        string repoRoot = FindRepoRoot();
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
