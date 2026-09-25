using System;
using System.Diagnostics;
using System.IO;

using Cadence.ToolingCore.OpenApi;

using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class QualityGateOrchestrator
{
    public static int RunQualityGates(string? gateName = null, string[]? targetFiles = null)
    {
        AnsiConsole.MarkupLine("[bold cyan]Executing Cadence Quality Sentinels & Policy Gates...[/]\n");

        if (!string.IsNullOrEmpty(gateName))
        {
            return RunSingleGate(gateName, targetFiles);
        }

        // Run all 21 pre-commit quality gates natively in C#
        bool allPassed = true;

        // 1. Formatting
        AnsiConsole.MarkupLine("1/21 Validating C# Code Formatting (.editorconfig)...");
        if (RunProcess("dotnet", "format whitespace Cadence.sln --verify-no-changes --no-restore") != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 1 (dotnet-format) failed![/]");
            allPassed = false;
        }

        // 2. Roslyn Style
        AnsiConsole.MarkupLine("\n2/21 Validating Roslyn Analyzer Rules...");
        if (RunProcess("dotnet", "format style Cadence.sln --verify-no-changes --no-restore") != 0)
        {
            AnsiConsole.MarkupLine("[red]Gate 2 (dotnet-format-style) failed![/]");
            allPassed = false;
        }

        // 3. Clean Secrets Baseline
        AnsiConsole.MarkupLine("\n3/21 Cleaning Secrets Baseline...");
        if (!SecretsBaselineService.CleanBaseline())
        {
            AnsiConsole.MarkupLine("[red]Gate 3 (clean-secrets-baseline) failed![/]");
            allPassed = false;
        }

        // 4. Code Duplication
        AnsiConsole.MarkupLine("\n4/21 Detecting Code Duplication...");
        if (!CodeDuplicationService.DetectDuplication(targetFiles))
        {
            AnsiConsole.MarkupLine("[red]Gate 4 (detect-code-duplication) failed![/]");
            allPassed = false;
        }

        // 5. CLI Docs
        AnsiConsole.MarkupLine("\n5/21 Validating CLI Docs Parity...");
        if (!CliDocsValidatorService.ValidateCliDocs())
        {
            AnsiConsole.MarkupLine("[red]Gate 5 (validate-cli-docs) failed![/]");
            allPassed = false;
        }

        // 6. ADRs
        AnsiConsole.MarkupLine("\n6/21 Validating ADR Integrity...");
        if (!AdrValidatorService.ValidateAdrs(checkGitDiff: true))
        {
            AnsiConsole.MarkupLine("[red]Gate 6 (validate-adrs) failed![/]");
            allPassed = false;
        }

        // 7. Architecture Drift
        AnsiConsole.MarkupLine("\n7/21 Validating Architecture Drift...");
        if (!ArchitectureDriftValidatorService.ValidateArchitectureDrift())
        {
            AnsiConsole.MarkupLine("[red]Gate 7 (validate-architecture-drift) failed![/]");
            allPassed = false;
        }

        // 8. Path Requirement Sanitizer
        AnsiConsole.MarkupLine("\n8/21 Sanitizing Requirement Tag Prefixes...");
        if (!ComplianceSanitizerService.SanitizeComplianceTags(targetFiles))
        {
            AnsiConsole.MarkupLine("[red]Gate 8 (path-requirement-sanitizer) failed![/]");
            allPassed = false;
        }

        // 9. Markdown Validation
        AnsiConsole.MarkupLine("\n9/21 Validating Markdown Workspaces & Links...");
        if (!MarkdownValidatorService.ValidateMarkdown(targetFiles))
        {
            AnsiConsole.MarkupLine("[red]Gate 9 (validate-markdown) failed![/]");
            allPassed = false;
        }

        // 10. Environment Variables
        AnsiConsole.MarkupLine("\n10/21 Validating Environment Variables Synchronization...");
        if (!EnvironmentVariablesValidatorService.ValidateEnvironmentVariables())
        {
            AnsiConsole.MarkupLine("[red]Gate 10 (validate-environment-variables) failed![/]");
            allPassed = false;
        }

        // 11. Readmes
        AnsiConsole.MarkupLine("\n11/21 Validating README Coverage...");
        if (!ReadmesValidatorService.ValidateReadmes())
        {
            AnsiConsole.MarkupLine("[red]Gate 11 (validate-readmes) failed![/]");
            allPassed = false;
        }

        // 12. Imports
        AnsiConsole.MarkupLine("\n12/21 Validating Cross-Service Imports...");
        if (!ImportsValidatorService.ValidateImports())
        {
            AnsiConsole.MarkupLine("[red]Gate 12 (validate-imports) failed![/]");
            allPassed = false;
        }

        // 13. Audit Security
        AnsiConsole.MarkupLine("\n13/21 Executing Security Audit & Secret Scanner...");
        if (!SecurityAuditService.AuditSecurity(targetFiles))
        {
            AnsiConsole.MarkupLine("[red]Gate 13 (audit-security) failed![/]");
            allPassed = false;
        }

        // 14. DEID Scan
        AnsiConsole.MarkupLine("\n14/21 Executing DEID Compliance Scan...");
        if (!DeidComplianceService.ScanDeidCompliance(targetFiles))
        {
            AnsiConsole.MarkupLine("[red]Gate 14 (deid-compliance-scan) failed![/]");
            allPassed = false;
        }

        // 15. Auto-stage OpenAPI
        AnsiConsole.MarkupLine("\n15/21 Staging & Validating OpenAPI Schemas...");
        if (!OpenApiStageService.StageAndValidateOpenApi())
        {
            AnsiConsole.MarkupLine("[red]Gate 15 (auto-stage-openapi) failed![/]");
            allPassed = false;
        }

        // 16. API Clients
        AnsiConsole.MarkupLine("\n16/21 Validating Frontend API Clients AST Contract...");
        if (!ApiClientsValidatorService.ValidateApiClients())
        {
            AnsiConsole.MarkupLine("[red]Gate 16 (validate-api-clients) failed![/]");
            allPassed = false;
        }

        // 17. Schema Doc
        AnsiConsole.MarkupLine("\n17/21 Staging Schema Documentation...");
        if (!SchemaDocStageService.StageSchemaDoc())
        {
            AnsiConsole.MarkupLine("[red]Gate 17 (auto-stage-schema-doc) failed![/]");
            allPassed = false;
        }

        // 18. Path Patterns
        AnsiConsole.MarkupLine("\n18/21 Validating Path Patterns...");
        if (!PathPatternsValidatorService.ValidatePathPatterns(targetFiles))
        {
            AnsiConsole.MarkupLine("[red]Gate 18 (validate-path-patterns) failed![/]");
            allPassed = false;
        }

        // 19. Backend Static Verify
        AnsiConsole.MarkupLine("\n19/21 Verifying Backend Contracts...");
        if (!ContractsVerifierService.VerifyContracts())
        {
            AnsiConsole.MarkupLine("[red]Gate 19 (backend-static-verify) failed![/]");
            allPassed = false;
        }

        // 20. Query Style Linter
        AnsiConsole.MarkupLine("\n20/21 Executing Query Style Linter...");
        if (!QueryStyleLinterService.LintQueryStyle(targetFiles))
        {
            AnsiConsole.MarkupLine("[red]Gate 20 (query-style-linter) failed![/]");
            allPassed = false;
        }

        // 21. Vulnerabilities & Dependencies
        AnsiConsole.MarkupLine("\n21/21 Validating Dependencies & Vulnerabilities...");
        if (!VulnerabilitiesValidatorService.ValidateVulnerabilities())
        {
            AnsiConsole.MarkupLine("[red]Gate 21a (validate-vulnerabilities) failed![/]");
            allPassed = false;
        }
        if (!DependenciesValidatorService.ValidateDependencies())
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

    private static int RunSingleGate(string gateName, string[]? targetFiles)
    {
        string normalized = gateName.ToLowerInvariant().Replace("_", "-");
        bool passed = normalized switch
        {
            "prettier-check" => true,
            "dotnet-format" => RunProcess("dotnet", "format whitespace Cadence.sln --verify-no-changes --no-restore") == 0,
            "dotnet-format-style" => RunProcess("dotnet", "format style Cadence.sln --verify-no-changes --no-restore") == 0,
            "clean-secrets-baseline" or "clean-secrets" => SecretsBaselineService.CleanBaseline(),
            "detect-code-duplication" or "duplication" => CodeDuplicationService.DetectDuplication(targetFiles),
            "validate-cli-docs" or "cli-docs" => CliDocsValidatorService.ValidateCliDocs(),
            "validate-adrs" or "adrs" => AdrValidatorService.ValidateAdrs(checkGitDiff: true),
            "validate-architecture-drift" or "arch-drift" => ArchitectureDriftValidatorService.ValidateArchitectureDrift(),
            "path-requirement-sanitizer" or "sanitizer" => ComplianceSanitizerService.SanitizeComplianceTags(targetFiles),
            "validate-markdown" or "markdown" => MarkdownValidatorService.ValidateMarkdown(targetFiles),
            "validate-environment-variables" or "env-vars" => EnvironmentVariablesValidatorService.ValidateEnvironmentVariables(),
            "validate-readmes" or "readmes" => ReadmesValidatorService.ValidateReadmes(),
            "validate-imports" or "imports" => ImportsValidatorService.ValidateImports(),
            "audit-security" or "security" => SecurityAuditService.AuditSecurity(targetFiles),
            "deid-compliance-scan" or "deid" => DeidComplianceService.ScanDeidCompliance(targetFiles),
            "auto-stage-openapi" or "openapi" => OpenApiStageService.StageAndValidateOpenApi(),
            "validate-api-clients" or "api-clients" => ApiClientsValidatorService.ValidateApiClients(),
            "auto-stage-schema-doc" or "schema-doc" => SchemaDocStageService.StageSchemaDoc(),
            "validate-path-patterns" or "path-patterns" => PathPatternsValidatorService.ValidatePathPatterns(targetFiles),
            "backend-static-verify" or "contracts" => ContractsVerifierService.VerifyContracts(),
            "query-style-linter" or "query-linter" => QueryStyleLinterService.LintQueryStyle(targetFiles),
            "validate-vulnerabilities" or "vulnerabilities" => VulnerabilitiesValidatorService.ValidateVulnerabilities(),
            "validate-dependencies" or "dependencies" => DependenciesValidatorService.ValidateDependencies(),
            "gxp-rtm-validate" or "rtm" => GxpRtmValidatorService.ValidateGxpRtm(),
            _ => true
        };

        return passed ? 0 : 1;
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
