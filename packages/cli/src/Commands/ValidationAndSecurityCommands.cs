using System.CommandLine;

using Cadence.ToolingCore.Services;

using static Cadence.Cli.Commands.CommandRegistrationHelpers;

namespace Cadence.Cli.Commands;

/// <summary>Registers static analysis, boundary, hygiene, and security audit commands.</summary>
internal static class ValidationAndSecurityCommands
{
    public static void Register(RootCommand root)
    {
        AddCommand(root, "detect-duplication", "Finds duplicated C# and TypeScript code blocks", () => DuplicationDetectorService.DetectDuplication());
        AddCommand(root, "validate-imports", "Checks cross-service import boundaries", () => ImportValidatorService.ValidateImports());
        AddCommand(root, "validate-dependencies", "Checks project and package dependency rules", () => DependencyValidatorService.ValidateDependencies());
        AddCommand(root, "validate-path-patterns", "Checks file path and directory conventions", () => PathPatternValidatorService.ValidatePathPatterns());
        AddCommand(root, "validate-readmes", "Checks that every app and package has a README", () => ReadmeValidatorService.ValidateReadmes());
        AddCommand(root, "validate-codeowners", "Checks the CODEOWNERS file", () => CodeownersValidatorService.ValidateCodeowners());
        AddCommand(root, "validate-workflow-security", "Checks GitHub Actions SHA pinning, checkout credentials and least-privilege permissions", () => WorkflowSecurityValidatorService.ValidateWorkflowSecurity());
        AddCommand(root, "validate-markdown", "Checks Markdown formatting and workspace links", () => MarkdownValidatorService.ValidateMarkdown());
        AddCommand(root, "query-linter", "Lints database and ORM query statements", () => QueryLinterService.LintQueries());
        AddCommand(root, "extract-env-vars", "Checks that environment variables read in code are documented", ToolCommands.ConfigureExtractEnvVars);
        AddCommand(root, "audit-security", "Scans for hardcoded secrets", () => SecurityAuditorService.AuditSecurity());
        AddCommand(root, "validate-public-bundle", "Verifies built VitePress and demo web bundles with zero comment exemptions", () => PublicBundleValidatorService.ValidatePublicBundles());
        AddCommand(root, "validate-vulnerabilities", "Audits dependency vulnerabilities against the exclusions ledger", () => VulnerabilityAuditorService.AuditVulnerabilities());
        AddCommand(root, "clean-secrets-baseline", "Normalizes or checks the detect-secrets baseline", ToolCommands.ConfigureCleanSecretsBaseline);
        AddCommand(root, "validate-architecture-drift", "Checks that compose services are described in the architecture docs", () => ArchitectureDriftValidatorService.ValidateArchitectureDrift());
    }
}
