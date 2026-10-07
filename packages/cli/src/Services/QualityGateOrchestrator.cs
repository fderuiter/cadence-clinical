using System;
using System.Diagnostics;
using System.IO;

using Cadence.Compliance;
using Cadence.ToolingCore.OpenApi;
using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class QualityGateOrchestrator
{
    public static int RunQualityGates(string? gateName = null, string[]? targetFiles = null, bool preCommit = false)
    {
        AnsiConsole.MarkupLine("[bold cyan]Executing Cadence Quality Sentinels & Policy Gates...[/]\n");

        if (preCommit && (targetFiles == null || targetFiles.Length == 0))
        {
            var staged = PreCommitOpenApiService.GetStagedFiles(FindRepoRoot());
            if (staged.Count > 0)
            {
                targetFiles = staged.ToArray();
            }
        }

        if (!string.IsNullOrEmpty(gateName))
        {
            return RunSingleGate(gateName, targetFiles, preCommit);
        }

        // Run all 21 pre-commit quality gates natively in C# via ToolingCore services
        bool allPassed = true;

        // 1. Formatting
        AnsiConsole.MarkupLine("1/21 Validating C# Code Formatting (.editorconfig)...");
        if (preCommit)
        {
            AnsiConsole.MarkupLine("Skipped: the pre-commit hook runs 'dotnet format' on the staged files.");
        }
        else if (RunProcess("dotnet", "format whitespace Cadence.sln --verify-no-changes --no-restore") != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 1 (dotnet-format) failed![/]");
            allPassed = false;
        }

        // 2. Roslyn Style
        AnsiConsole.MarkupLine("\n2/21 Validating Roslyn Analyzer Rules...");
        if (preCommit)
        {
            AnsiConsole.MarkupLine("Skipped: the pre-commit hook runs 'dotnet format' on the staged files.");
        }
        else if (RunProcess("dotnet", "format style Cadence.sln --verify-no-changes --no-restore") != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 2 (dotnet-format-style) failed![/]");
            allPassed = false;
        }

        // 3. Clean Secrets Baseline
        AnsiConsole.MarkupLine("\n3/21 Cleaning Secrets Baseline...");
        if (SecretsBaselineCleanerService.CleanSecretsBaseline() != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 3 (clean-secrets-baseline) failed![/]");
            allPassed = false;
        }

        // 4. Code Duplication
        AnsiConsole.MarkupLine("\n4/21 Detecting Code Duplication...");
        if (DuplicationDetectorService.DetectDuplication(targetFiles) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 4 (detect-code-duplication) failed![/]");
            allPassed = false;
        }

        // 5. CLI Docs
        AnsiConsole.MarkupLine("\n5/21 Validating CLI Docs Parity...");
        if (CliDocsGeneratorService.GenerateCliDocs(check: true) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 5 (validate-cli-docs) failed![/]");
            allPassed = false;
        }

        // 6. ADRs
        AnsiConsole.MarkupLine("\n6/21 Validating ADR Integrity...");
        if (!Cadence.ToolingCore.Services.AdrValidatorService.ValidateAdrs(checkGitDiff: true))
        {
            AnsiConsole.MarkupLine("[red]Gate 6 (validate-adrs) failed![/]");
            allPassed = false;
        }

        // 7. Architecture Drift
        AnsiConsole.MarkupLine("\n7/21 Validating Architecture Drift...");
        if (Cadence.ToolingCore.Services.ArchitectureDriftValidatorService.ValidateArchitectureDrift() != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 7 (validate-architecture-drift) failed![/]");
            allPassed = false;
        }

        // 8. Path Requirement Sanitizer
        AnsiConsole.MarkupLine("\n8/21 Sanitizing Requirement Tag Prefixes...");
        if (ComplianceUtilityService.ExecuteComplianceUtility(sanitize: true, targetFiles: targetFiles) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 8 (path-requirement-sanitizer) failed![/]");
            allPassed = false;
        }

        // 9. Markdown Validation
        AnsiConsole.MarkupLine("\n9/21 Validating Markdown Workspaces & Links...");
        if (Cadence.ToolingCore.Services.MarkdownValidatorService.ValidateMarkdown(targetFiles) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 9 (validate-markdown) failed![/]");
            allPassed = false;
        }

        // 10. Environment Variables
        AnsiConsole.MarkupLine("\n10/21 Validating Environment Variables Synchronization...");
        if (EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 10 (validate-environment-variables) failed![/]");
            allPassed = false;
        }

        // 10b. Dev Environment
        AnsiConsole.MarkupLine("\nValidating Dev Environment Documentation Parity...");
        if (DevEnvironmentValidatorService.ValidateDevEnvironment() != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate (validate-dev-environment) failed![/]");
            allPassed = false;
        }

        // 11. Readmes
        AnsiConsole.MarkupLine("\n11/21 Validating README Coverage...");
        if (ReadmeValidatorService.ValidateReadmes() != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 11 (validate-readmes) failed![/]");
            allPassed = false;
        }

        // 12. Imports
        AnsiConsole.MarkupLine("\n12/21 Validating Cross-Service Imports...");
        if (ImportValidatorService.ValidateImports() != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 12 (validate-imports) failed![/]");
            allPassed = false;
        }

        // 13. Audit Security
        AnsiConsole.MarkupLine("\n13/21 Executing Security Audit & Secret Scanner...");
        if (SecurityAuditorService.AuditSecurity(targetFiles) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 13 (audit-security) failed![/]");
            allPassed = false;
        }

        // 14. DEID Scan
        AnsiConsole.MarkupLine("\n14/21 Executing DEID Compliance Scan...");
        if (Cadence.ToolingCore.Services.DeidComplianceService.ScanDeidCompliance(targetFiles) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 14 (deid-compliance-scan) failed![/]");
            allPassed = false;
        }

        // 15. Auto-stage OpenAPI
        AnsiConsole.MarkupLine("\n15/21 Staging & Validating OpenAPI Schemas...");
        if (PreCommitOpenApiService.ExecutePreCommitOpenApi(quick: preCommit, targetFiles: targetFiles) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 15 (auto-stage-openapi) failed![/]");
            allPassed = false;
        }

        // 16. API Clients
        AnsiConsole.MarkupLine("\n16/21 Validating Frontend API Clients AST Contract...");
        if (ApiClientValidatorService.ValidateApiClients() != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 16 (validate-api-clients) failed![/]");
            allPassed = false;
        }

        // 17. Schema Doc
        AnsiConsole.MarkupLine("\n17/21 Validating Database Schema Documentation...");
        if (preCommit)
        {
            AnsiConsole.MarkupLine("Skipped: rendering every module's EF Core model runs in 'cadence check' and CI.");
        }
        else if (SchemaDocGeneratorService.GenerateSchemaDocumentation(validate: true) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 17 (schema-doc) failed![/]");
            allPassed = false;
        }

        // 18. Path Patterns
        AnsiConsole.MarkupLine("\n18/21 Validating Path Patterns...");
        if (PathPatternValidatorService.ValidatePathPatterns(targetFiles) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 18 (validate-path-patterns) failed![/]");
            allPassed = false;
        }

        // 19. Backend Static Verify
        AnsiConsole.MarkupLine("\n19/21 Verifying Backend Contracts...");
        if (ContractVerifierService.VerifyContracts() != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 19 (backend-static-verify) failed![/]");
            allPassed = false;
        }

        // 20. Query Style Linter
        AnsiConsole.MarkupLine("\n20/21 Executing Query Style Linter...");
        if (QueryLinterService.LintQueries(targetFiles) != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 20 (query-style-linter) failed![/]");
            allPassed = false;
        }

        // 21. Vulnerabilities & Dependencies
        AnsiConsole.MarkupLine("\n21/21 Validating Dependencies & Vulnerabilities...");
        if (VulnerabilityAuditorService.AuditVulnerabilities() != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 21a (validate-vulnerabilities) failed![/]");
            allPassed = false;
        }
        if (DependencyValidatorService.ValidateDependencies() != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 21b (validate-dependencies) failed![/]");
            allPassed = false;
        }

        if (!allPassed)
        {
            AnsiConsole.MarkupLine("\n[bold red]One or more quality gate checks failed.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("\n[bold green]All 21 native quality sentinels passed.[/]");
        return 0;
    }

    private static int RunSingleGate(string gateName, string[]? targetFiles, bool preCommit)
    {
        string normalized = gateName.ToLowerInvariant().Replace("_", "-");
        bool passed = normalized switch
        {
            "prettier-check" => true,
            "dotnet-format" => RunProcess("dotnet", "format whitespace Cadence.sln --verify-no-changes --no-restore") == 0,
            "dotnet-format-style" => RunProcess("dotnet", "format style Cadence.sln --verify-no-changes --no-restore") == 0,
            "clean-secrets-baseline" or "clean-secrets" => SecretsBaselineCleanerService.CleanSecretsBaseline() == 0,
            "detect-code-duplication" or "duplication" => DuplicationDetectorService.DetectDuplication(targetFiles) == 0,
            "validate-cli-docs" or "cli-docs" => CliDocsGeneratorService.GenerateCliDocs(check: true) == 0,
            "validate-adrs" or "adrs" => Cadence.ToolingCore.Services.AdrValidatorService.ValidateAdrs(checkGitDiff: true),
            "validate-architecture-drift" or "arch-drift" => Cadence.ToolingCore.Services.ArchitectureDriftValidatorService.ValidateArchitectureDrift() == 0,
            "path-requirement-sanitizer" or "sanitizer" => ComplianceUtilityService.ExecuteComplianceUtility(sanitize: true, targetFiles: targetFiles) == 0,
            "validate-markdown" or "markdown" => Cadence.ToolingCore.Services.MarkdownValidatorService.ValidateMarkdown(targetFiles) == 0,
            "validate-environment-variables" or "env-vars" => EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true) == 0,
            "validate-dev-environment" or "dev-environment" or "dev-env" => DevEnvironmentValidatorService.ValidateDevEnvironment() == 0,
            "validate-readmes" or "readmes" => ReadmeValidatorService.ValidateReadmes() == 0,
            "validate-imports" or "imports" => ImportValidatorService.ValidateImports() == 0,
            "audit-security" or "security" => SecurityAuditorService.AuditSecurity(targetFiles) == 0,
            "deid-compliance-scan" or "deid" => Cadence.ToolingCore.Services.DeidComplianceService.ScanDeidCompliance(targetFiles) == 0,
            "auto-stage-openapi" or "openapi" => PreCommitOpenApiService.ExecutePreCommitOpenApi(quick: preCommit, targetFiles: targetFiles) == 0,
            "validate-api-clients" or "api-clients" => ApiClientValidatorService.ValidateApiClients() == 0,
            "auto-stage-schema-doc" or "schema-doc" => SchemaDocGeneratorService.GenerateSchemaDocumentation(validate: true) == 0,
            "validate-path-patterns" or "path-patterns" => PathPatternValidatorService.ValidatePathPatterns(targetFiles) == 0,
            "backend-static-verify" or "contracts" => ContractVerifierService.VerifyContracts() == 0,
            "query-style-linter" or "query-linter" => QueryLinterService.LintQueries(targetFiles) == 0,
            "validate-vulnerabilities" or "vulnerabilities" => VulnerabilityAuditorService.AuditVulnerabilities() == 0,
            "validate-dependencies" or "dependencies" => DependencyValidatorService.ValidateDependencies() == 0,
            "gxp-rtm-validate" or "rtm" => RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true) == 0,
            _ => ReportUnknownGate(gateName)
        };

        return passed ? 0 : 1;
    }

    private static bool ReportUnknownGate(string gateName)
    {
        AnsiConsole.MarkupLine($"[red]Unknown quality gate '{Markup.Escape(gateName)}'. Run 'cadence check --help' for the list of gates.[/]");
        return false;
    }

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

    private static int RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = FindRepoRoot(),
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null) return 1;
        process.WaitForExit();
        return process.ExitCode;
    }
}
