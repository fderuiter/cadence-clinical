using System.Collections.Generic;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services.Gates;

/// <summary>Documentation, ADR, environment, CODEOWNERS, workflow security, and ruleset governance gates.</summary>
internal static class DocsAndGovernanceGates
{
    public static IReadOnlyList<QualityGate> Entries { get; } =
    [
        new("cli-docs", ["validate-cli-docs"],
            "`docs/cli.md` documents every command in `Program.cs`, and the gate tables in `docs/cli.md` and `docs/CI.md` match this registry",
            ctx => CheckCliDocs(ctx.RepoRoot)),
        new("adrs", ["validate-adrs"], "ADR schema, `docs/adr/index.md` consistency, and an ADR for architectural changes",
            ctx => AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: ctx.RepoRoot, preCommit: ctx.PreCommit, baseRef: ctx.BaseRef, pushBefore: ctx.PushBefore))
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks staged files for architectural changes, accepting ADRs already added on the branch",
        },
        new("arch-drift", ["validate-architecture-drift"],
            "`docker/docker-compose.yml` exists and declares services; services missing from the architecture docs are warnings",
            ctx => ArchitectureDriftValidatorService.ValidateArchitectureDrift(ctx.RepoRoot) == 0),
        new("sanitizer", ["path-requirement-sanitizer"],
            "Requirement tags in Markdown are upper case (`@req:PRD-…`); fixes them in normal mode and reports errors read-only during pre-commit",
            ctx => ComplianceUtilityService.CheckRequirementTags(ctx.RepoRoot, ctx.Files, ctx.PreCommit, ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "reports lowercase `@req:prd-…` tags in staged Markdown blobs without rewriting working-tree files",
        },
        new("markdown", ["validate-markdown"],
            "Markdown structure, links, and paths into .NET test project folders (`tests/Cadence.*`) that no longer exist",
            ctx => MarkdownValidatorService.ValidateMarkdown(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("env-vars", ["validate-environment-variables"],
            "Environment variables read in code are in `.env.example` and `docs/ENVIRONMENT_VARIABLES.md`",
            ctx => EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: ctx.RepoRoot) == 0),
        new("dev-env", ["validate-dev-environment", "dev-environment"],
            "Service ports in `docs/LOCAL_DEV_ENVIRONMENT.md` match the gateway and the Aspire AppHost",
            ctx => DevEnvironmentValidatorService.ValidateDevEnvironment(ctx.RepoRoot) == 0),
        new("readmes", ["validate-readmes"], "Each directory under `apps/` and `packages/` has a README with a title",
            ctx => ReadmeValidatorService.ValidateReadmes(ctx.RepoRoot) == 0),
        new("codeowners", ["validate-codeowners"], "Path pattern existence and owner handle syntax in `.github/CODEOWNERS`",
            ctx => CodeownersValidatorService.ValidateCodeowners(ctx.RepoRoot) == 0),
        new("workflow-security", ["validate-workflow-security", "action-pinning", "check-action-pinning"],
            "40-hex commit SHAs with version comments, `sha256:` digests on `docker://` refs, `persist-credentials: false` on `actions/checkout`, typed non-interpolated `workflow_dispatch` inputs, ref-guarded dispatch triggers, and least-privilege permissions across `.github/workflows` and `.github/actions`",
            ctx => WorkflowSecurityValidatorService.ValidateWorkflowSecurity(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("rulesets", ["validate-rulesets"],
            "Branch and signed `v*` tag rulesets in `.github/rulesets/*.json`, `pull_request`-only branch bypasses, release-admin-only tag bypasses, `merge_queue` policy, strict status checks, `CI Gate` upstream job coverage, and `pull_request` / `merge_group` workflow parity",
            ctx => RulesetService.Validate(ctx.RepoRoot, preCommit: ctx.PreCommit, indexReader: ctx.IndexReader) == 0)
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "validates staged `.github/rulesets` and `.github/workflows` blobs",
        },
    ];

    /// <summary>Both halves of the <c>cli-docs</c> gate, so one failure does not hide the other.</summary>
    private static bool CheckCliDocs(string repoRoot)
    {
        bool commandsDocumented = CliDocsGeneratorService.GenerateCliDocs(
            check: true,
            repoRoot: repoRoot,
            commandNames: Program.GetCommandNames()) == 0;
        bool tablesCurrent = GateDocs.Check(repoRoot);
        return commandsDocumented && tablesCurrent;
    }
}
