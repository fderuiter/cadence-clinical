using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

/// <summary>How the pre-commit hook (<c>cadence check --pre-commit</c>) runs a gate.</summary>
public enum PreCommitMode
{
    /// <summary>The hook runs the gate as <c>cadence check</c> does, limited to the staged files when it takes files.</summary>
    Full,

    /// <summary>The hook runs a quicker variant, described by <see cref="QualityGate.PreCommitNote"/>.</summary>
    Quick,

    /// <summary>The hook skips the gate, for the reason in <see cref="QualityGate.PreCommitNote"/>.</summary>
    Skipped,
}

/// <summary>
/// What a gate checks: the repository root, the files to limit it to, whether the pre-commit hook runs it, an optional
/// shared Git index reader, and optional <c>--base</c> / <c>--push-before</c> Git scope overrides.
/// </summary>
public sealed record GateContext(
    string RepoRoot,
    string[]? TargetFiles = null,
    bool PreCommit = false,
    GitIndexReader? IndexReader = null,
    string? BaseRef = null,
    string? PushBefore = null)
{
    /// <summary>The target files (including an empty array when zero files are staged or a commit only deletes files), or <c>null</c> for the whole repository or Git revision scope.</summary>
    public string[]? Files => TargetFiles;
}

/// <summary>
/// One quality gate of <c>cadence check</c>: its short <paramref name="Name"/>, the longer names it also answers to,
/// one sentence on what it checks, and the check itself, which returns <c>true</c> when the gate passes.
/// </summary>
public sealed record QualityGate(string Name, IReadOnlyList<string> Aliases, string Checks, Func<GateContext, bool> Run)
{
    /// <summary>How the pre-commit hook runs the gate.</summary>
    public PreCommitMode PreCommit { get; init; } = PreCommitMode.Full;

    /// <summary>What the hook does instead, when <see cref="PreCommit"/> is not <see cref="PreCommitMode.Full"/>.</summary>
    public string? PreCommitNote { get; init; }

    /// <summary>Whether the gate limits itself to the files given to <c>cadence check &lt;gate&gt; [files]</c>.</summary>
    public bool TakesFiles { get; init; }

    /// <summary>
    /// Whether the gate inspects compiled build artifacts that unscoped <c>cadence check</c> skips only when none of its
    /// output directories exist yet (while partial output or direct <c>cadence check &lt;gate&gt;</c> fails closed).
    /// </summary>
    public bool PostBuild { get; init; }

    /// <summary>Why unscoped <c>cadence check</c> skipped a <see cref="PostBuild"/> gate when all build outputs are absent.</summary>
    public string? PostBuildSkipNote { get; init; }

    /// <summary>The short name followed by the aliases.</summary>
    public IEnumerable<string> AllNames => Aliases.Prepend(Name);
}

/// <summary>
/// The registry of quality gates. <c>cadence check</c> runs them in this order, <c>cadence check &lt;gate&gt;</c> looks
/// a gate up here, and the gate count, the <c>check --help</c> text and the gate tables in <c>docs/cli.md</c> and
/// <c>docs/CI.md</c> (<see cref="GateDocs"/>) are generated from it. Each gate must be able to fail:
/// <c>QualityGateRegistryTests</c> runs every gate against a fixture that breaks it.
/// </summary>
public static class QualityGates
{
    /// <summary>Every gate, in the order <c>cadence check</c> runs them.</summary>
    public static IReadOnlyList<QualityGate> All { get; } =
    [
        new("dotnet-format", [], "Whitespace formatting against `.editorconfig` (`dotnet format whitespace`)",
            ctx => CheckDotnetFormat(ctx, "whitespace"))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks staged C# blobs; fails closed when `dotnet` is missing",
        },
        new("dotnet-format-style", [], "Roslyn code-style rules (`dotnet format style`)",
            ctx => CheckDotnetFormat(ctx, "style"))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks staged C# files and fails closed when `dotnet` is missing or unstaged edits overlap MSBuild inputs",
        },
        new("prettier", [], "Prettier formatting across JS, TS, Vue, CSS, JSON, Markdown and YAML files",
            ctx => FrontendQualityGateService.CheckPrettier(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "streams staged JS, TS, Vue, CSS, JSON, Markdown and YAML blobs over stdin; fails closed when `pnpm` or `node_modules` is missing",
        },
        new("eslint", [], "ESLint and duplicate store identifier checks across JS, TS and Vue files",
            ctx => FrontendQualityGateService.CheckEslint(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "lints staged JS, TS and Vue blobs over stdin; fails closed when `pnpm` or `node_modules` is missing",
        },
        new("actionlint", ["validate-workflows"],
            "GitHub Actions workflow and composite-action static analysis via `actionlint` from `.cadence/bin` or `PATH` (fails closed when missing)",
            ctx => CliLinterGateService.RunActionlint(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "validates staged `.github/workflows` and `.github/actions` blobs; fails closed when `actionlint` is missing",
        },
        new("hadolint", ["hadolint-docker", "validate-dockerfiles"],
            "Dockerfile best-practice and syntax linting (`hadolint --failure-threshold error`) from `.cadence/bin` or `PATH` (fails closed when missing)",
            ctx => CliLinterGateService.RunHadolint(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "lints staged `Dockerfile*` blobs; fails closed when `hadolint` is missing",
        },
        new("yaml", ["check-yaml", "validate-yaml"],
            "YAML 1.2 syntax, indentation, quoted scalars, flow brackets and duplicate mapping keys",
            ctx => YamlSyntaxValidatorService.ValidateYaml(ctx.TargetFiles, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("trailing-whitespace", ["check-trailing-whitespace"],
            "No trailing spaces or tabs at the end of lines in text files",
            ctx => FileHygieneValidatorService.ValidateTrailingWhitespace(ctx.TargetFiles, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("end-of-file", ["end-of-file-fixer", "eof-newline"],
            "Non-empty UTF-8 text files end with a newline character",
            ctx => FileHygieneValidatorService.ValidateEndOfFileNewline(ctx.TargetFiles, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("clean-secrets", ["clean-secrets-baseline"],
            "Verifies `.secrets.baseline` is valid, normalized JSON with the canonical exclusion filter without mutating it",
            ctx => SecretsBaselineCleanerService.CleanSecretsBaseline(repoRoot: ctx.RepoRoot, check: true, preCommit: ctx.PreCommit, indexReader: ctx.IndexReader) == 0)
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "validates staged `.secrets.baseline` JSON without mutating the working tree",
        },
        new("detect-secrets", ["secrets-scan"],
            "Scans changed or tracked files against `.secrets.baseline` using `detect-secrets-hook` from `.cadence/venv`, `PATH` or `uvx` (fails closed when missing)",
            ctx => DetectSecretsGateService.Scan(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, indexReader: ctx.IndexReader, baseRef: ctx.BaseRef, pushBefore: ctx.PushBefore) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "scans materialized staged blobs instead of the working tree",
        },
        new("duplication", ["detect-code-duplication"], "Duplicated code blocks over 15 normalized lines involving a C# file",
            ctx => DuplicationDetectorService.DetectDuplication(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
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
            "40-hex commit SHAs with version comments, `sha256:` digests on `docker://` refs, `persist-credentials: false` on `actions/checkout`, and least-privilege permissions across `.github/workflows` and `.github/actions`",
            ctx => WorkflowSecurityValidatorService.ValidateWorkflowSecurity(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("rulesets", ["validate-rulesets"],
            "Branch and tag rulesets in `.github/rulesets/*.json`, strict status check policy, `CI Gate` and `Check for Merge Conflicts` workflow coverage, and `classify` manifest guardrails",
            ctx => RulesetService.Validate(ctx.RepoRoot) == 0),
        new("imports", ["validate-imports"], "No `using` of another service's namespace across `apps/<service>`",
            ctx => ImportValidatorService.ValidateImports(ctx.RepoRoot) == 0),
        new("security", ["audit-security"], "Hard-coded secret patterns across source, Vue templates and build scripts (private keys, cloud keys, API tokens, compliance secrets)",
            ctx => SecurityAuditorService.AuditSecurity(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("deid", ["deid-compliance-scan"],
            "PHI/PII in changed or tracked files including authored `docs/`, root portal docs, `.vue` templates and `stores/` (vendored `docs/CDISC/`, `docs/NCI/` and `docs/SDLC/runs/` excluded), ratcheted by `.deid-baseline.txt`; a line that is not PHI takes a `deid-ignore` comment",
            ctx => DeidComplianceService.ScanDeidCompliance(ctx.Files, ctx.RepoRoot, preCommit: ctx.PreCommit, indexReader: ctx.IndexReader, baseRef: ctx.BaseRef, pushBefore: ctx.PushBefore) == 0)
        { TakesFiles = true },
        new("openapi", ["auto-stage-openapi"], "`docs/openapi` matches what the .NET services serve, and `docs/api` matches `docs/openapi`",
            ctx => PreCommitOpenApiService.ExecutePreCommitOpenApi(ctx.RepoRoot, quick: ctx.PreCommit, targetFiles: ctx.TargetFiles, indexReader: ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "exports specs only for the services the staged C# files touch, otherwise checks staged `docs/api` against staged `docs/openapi`",
        },
        new("api-clients", ["validate-api-clients"],
            "The web API types are generated from the current `docs/openapi`, and the web API clients typecheck against them",
            ctx => ApiClientValidatorService.ValidateApiClients(ctx.RepoRoot) == 0),
        new("schema-doc", ["auto-stage-schema-doc"], "`docs/schema` matches the EF Core models",
            ctx => CheckSchemaDoc(ctx))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "runs the drift check when staged files include Entity Framework entities, migrations, configurations, or `docs/schema`",
        },
        new("path-patterns", ["validate-path-patterns"],
            "No `tmp`, `temp`, `scratch` or `old_src` directories, `.vue` files only under `apps/`, `packages/` and `docs/.vitepress/`, `.sh` files only under `scripts/`",
            ctx => PathPatternValidatorService.ValidatePathPatterns(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("query-linter", ["query-style-linter"], "No interpolated SQL string passed to `FromSqlRaw` or `ExecuteSqlRaw`, and no comparison with a boolean literal",
            ctx => QueryLinterService.LintQueries(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("vulnerabilities", ["validate-vulnerabilities"],
            "NuGet, pnpm and Python dependency vulnerabilities not exempted by a valid `docs/SDLC/vulnerability_exclusions_ledger.json` entry (fails closed on missing or broken scanners)",
            ctx => VulnerabilityAuditorService.AuditVulnerabilities(ctx.RepoRoot) == 0),
        new("dependencies", ["validate-dependencies"],
            "Valid JSON across workspace `package.json` and `.config/dotnet-tools.json` manifests, complete `.github/dependabot.yml` group coverage, staggered weekday schedules and PR-limit caps, and no client-side crypto package (`node-forge`, `elliptic`, `jose`)",
            ctx => DependencyValidatorService.ValidateDependencies(ctx.RepoRoot) == 0),
        new("doc-index", ["validate-doc-index"], "Every Markdown document under `docs/` is listed in `docs/DOCUMENTATION_INDEX.md`",
            ctx => DocIndexSyncService.SyncDocIndex(check: true, repoRoot: ctx.RepoRoot) == 0),
        new("demo-manifest", ["validate-demo-manifest"],
            "`packages/demo-fixtures` matches the canonical demo seed in `packages/demo-fixtures/demo-seed.json`, `ExecutionDbSeeder.cs` and `docker/cadence-realm.json`",
            ctx => CheckDemoManifest(ctx))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "runs when staged files touch `packages/demo-fixtures`, `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs` or `docker/cadence-realm.json`, otherwise skips it",
        },
        new("rtm", ["gxp-rtm-validate"],
            "Requirement tags name known requirements and sit on test methods, and the generated documents match their integrity stamps and are not drafts",
            ctx => GxpRtmValidatorService.ValidateGxpRtm(ctx.RepoRoot)),
        new("gxp-snapshot", ["gxp-snapshot-guard"],
            "The branch or push range does not change the generated RTM, IQ/OQ/PQ report or `docs/SDLC/runs/` unless `CADENCE_GXP_SNAPSHOT=1` (ADR-2532)",
            ctx => GxpSnapshotGuardService.Check(ctx.RepoRoot, ctx.PreCommit, baseRef: ctx.BaseRef, pushBefore: ctx.PushBefore) == 0)
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks the staged files instead of the branch",
        },
        new("baseline-ratchet", ["baselines"],
            "No ratchet baseline (`*.baseline.txt`, `.deid-baseline.txt`, `.secrets.baseline`) gained an entry without a `Baseline-Growth: <path> - <reason>` trailer on the commit that grew it",
            ctx => BaselineRatchetService.Check(ctx.RepoRoot, ctx.PreCommit, baseRef: ctx.BaseRef, pushBefore: ctx.PushBefore) == 0)
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks staged files against `HEAD`; `CADENCE_BASELINE_GROWTH=\"<reason>\"` admits growth",
        },
        new("public-bundle", ["validate-public-bundle"],
            "Zero-exemption post-build scan of `docs/.vitepress/dist`, `apps/web/dist`, and `apps/subject-portal/dist` for national IDs, unapproved emails, secrets, external assets, executable local URLs, and CSP meta headers (skipped in unscoped `cadence check` only when all three `dist/` folders are absent; fails closed on partial `dist/` or direct `check public-bundle`)",
            ctx => PublicBundleValidatorService.ValidatePublicBundles(ctx.RepoRoot) == 0)
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "post-build gate; skipped in unscoped `check` only when all three `dist/` folders are absent, and enforced fail-closed in `docs-build` and `deploy-docs`",
            PostBuild = true,
            PostBuildSkipNote = "post-build gate; none of docs/.vitepress/dist, apps/web/dist, or apps/subject-portal/dist exist yet (build bundles or run 'pnpm cadence check public-bundle')",
        },
        new("branch-freshness", ["check-branch-freshness", "validate-branch-freshness"],
            "Branch `HEAD` includes `origin/main` (or `--base <ref>`) so local and PR runs enforce strict branch-freshness policy",
            ctx => CheckBranchFreshness(ctx))
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "skipped on commit so local work-in-progress commits stay offline; run by `cadence check` before push",
        },
        new("architecture-tests", ["arch-tests"],
            "Hexagonal layering and kernel boundary invariants (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`)",
            ctx => CheckDotnetTestSuite(ctx.RepoRoot, "tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj"))
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "full xUnit architecture suite; run by `cadence check` and CI's `dotnet-quality` job",
        },
        new("unit-tests", ["dotnet-unit-tests"],
            ".NET unit test suite (`tests/Cadence.UnitTests/Cadence.UnitTests.csproj`)",
            ctx => CheckDotnetTestSuite(ctx.RepoRoot, "tests/Cadence.UnitTests/Cadence.UnitTests.csproj"))
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "full xUnit unit test suite; run by `cadence check` and CI's `dotnet-unit-tests` job",
        },
    ];

    /// <summary>Finds a gate by its short name or an alias; case is ignored and underscores count as hyphens.</summary>
    public static QualityGate? Find(string name)
    {
        string normalized = Normalize(name);
        return All.FirstOrDefault(g => g.AllNames.Any(n => n == normalized));
    }

    /// <summary>The short names, comma-separated, for help text.</summary>
    public static string NameList => string.Join(", ", All.Select(g => g.Name));

    /// <summary>Both halves of the <c>cli-docs</c> gate, so one failure does not hide the other.</summary>
    private static bool CheckCliDocs(string repoRoot)
    {
        bool commandsDocumented = CliDocsGeneratorService.GenerateCliDocs(check: true, repoRoot: repoRoot) == 0;
        bool tablesCurrent = GateDocs.Check(repoRoot);
        return commandsDocumented && tablesCurrent;
    }

    private static bool CheckDotnetFormat(GateContext ctx, string subcommand)
    {
        if (ctx.TargetFiles is { Length: 0 })
        {
            return true;
        }

        if (!IsDotnetAvailable())
        {
            Console.WriteLine("[ERROR] .NET SDK ('dotnet') was not found on PATH.");
            return false;
        }

        if (ctx.PreCommit || ctx.TargetFiles is not null)
        {
            var rawFiles = ctx.PreCommit && ctx.TargetFiles is null
                ? PreCommitOpenApiService.GetStagedFiles(ctx.RepoRoot)
                : (IReadOnlyList<string>)ctx.TargetFiles!;
            if (rawFiles.Count == 0)
            {
                return true;
            }

            var reader = GitIndexReader.ResolvePreCommitReader(ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader, out var ownedReader);
            using (ownedReader)
            {
                var csFiles = rawFiles
                    .Select(f => GitIndexReader.TryNormalizeRelativePath(ctx.RepoRoot, f))
                    .Where(f => !string.IsNullOrEmpty(f) && f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .Select(f => f!)
                    .Where(f => reader is { HasAuthoritativeIndex: true }
                        ? reader.FileExists(f)
                        : File.Exists(Path.Combine(ctx.RepoRoot, f)))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList();

                if (csFiles.Count == 0)
                {
                    return true;
                }

                if (string.Equals(subcommand, "whitespace", StringComparison.Ordinal))
                {
                    if (reader is { HasAuthoritativeIndex: true })
                    {
                        var materializePaths = new List<string>(csFiles);
                        materializePaths.AddRange(reader.Paths.Where(IsEditorConfigPath));

                        using var stagedWorkspace = reader.Materialize(materializePaths);
                        var stagedWhitespaceArgs = new List<string> { "format", "whitespace", ".", "--folder", "--verify-no-changes", "--include" };
                        stagedWhitespaceArgs.AddRange(csFiles);
                        return RunProcess(stagedWorkspace.RootPath, "dotnet", [.. stagedWhitespaceArgs]) == 0;
                    }

                    var whitespaceArgs = new List<string> { "format", "whitespace", ".", "--folder", "--verify-no-changes", "--include" };
                    whitespaceArgs.AddRange(csFiles);
                    return RunProcess(ctx.RepoRoot, "dotnet", [.. whitespaceArgs]) == 0;
                }

                if (ctx.PreCommit && reader is { HasAuthoritativeIndex: true })
                {
                    var overlappingUnstaged = GitIndexReader.GetOverlappingUnstagedPaths(ctx.RepoRoot, GitIndexReader.IsMsBuildInputPath);
                    if (overlappingUnstaged.Count > 0)
                    {
                        string sample = string.Join(", ", overlappingUnstaged.Take(5));
                        Console.WriteLine($"[ERROR] Unstaged or untracked working-tree edits overlap MSBuild inputs ({sample}). Stage or stash them before committing so 'dotnet format {subcommand}' can verify the exact staged snapshot.");
                        return false;
                    }
                }

                var groups = csFiles.GroupBy(f => FindNearestCsproj(ctx.RepoRoot, f), StringComparer.Ordinal).ToList();
                if (groups.Count >= 3 && File.Exists(Path.Combine(ctx.RepoRoot, "Cadence.sln")))
                {
                    var slnArgs = new List<string> { "format", subcommand, "Cadence.sln", "--verify-no-changes", "--no-restore", "--include" };
                    slnArgs.AddRange(csFiles);
                    return RunProcess(ctx.RepoRoot, "dotnet", [.. slnArgs]) == 0;
                }

                foreach (var group in groups)
                {
                    string workspace = group.Key ?? "Cadence.sln";
                    var args = new List<string> { "format", subcommand, workspace, "--verify-no-changes", "--no-restore", "--include" };
                    args.AddRange(group);
                    if (RunProcess(ctx.RepoRoot, "dotnet", [.. args]) != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        return RunProcess(ctx.RepoRoot, "dotnet", "format", subcommand, "Cadence.sln", "--verify-no-changes", "--no-restore") == 0;
    }

    private static bool IsEditorConfigPath(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        return string.Equals(fileName, ".editorconfig", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, ".globalconfig", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindNearestCsproj(string repoRoot, string relativeFile) =>
        PreCommitOpenApiService.FindNearestCsproj(repoRoot, relativeFile);

    private static bool IsDotnetAvailable() =>
        DetectSecretsGateService.FindExecutableOnPath("dotnet", Environment.GetEnvironmentVariable("PATH")) is not null;

    private static bool CheckSchemaDoc(GateContext ctx)
    {
        if (ctx.TargetFiles is { Length: 0 })
        {
            return true;
        }

        if (ctx.PreCommit)
        {
            var files = ctx.TargetFiles ?? (IEnumerable<string>)PreCommitOpenApiService.GetStagedFiles(ctx.RepoRoot);
            if (!files.Any(AffectsSchemaDocGate))
            {
                Console.WriteLine("Staged changes do not include Entity Framework entities, migrations, or configurations. Skipping schema documentation check...");
                return true;
            }
        }
        else if (ctx.Files is not null && !ctx.Files.Any(AffectsSchemaDocGate))
        {
            return true;
        }

        return SchemaDocGeneratorService.GenerateSchemaDocumentation(
            validate: true,
            repoRoot: ctx.RepoRoot,
            preCommit: ctx.PreCommit,
            indexReader: ctx.IndexReader) == 0;
    }

    private static bool AffectsSchemaDocGate(string file)
    {
        if (PreCommitOpenApiService.AffectsEfCoreSchema(file))
        {
            return true;
        }

        string normalized = file.Replace('\\', '/');
        return normalized.StartsWith("docs/schema/", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CheckDemoManifest(GateContext ctx)
    {
        if (ctx.TargetFiles is { Length: 0 })
        {
            return true;
        }

        if (ctx.PreCommit)
        {
            var files = ctx.TargetFiles is not null
                ? (IEnumerable<string>)ctx.TargetFiles
                : PreCommitOpenApiService.GetStagedFiles(ctx.RepoRoot);

            if (!files.Any(f => AffectsDemoManifest(ctx.RepoRoot, f)))
            {
                Console.WriteLine("Staged changes do not touch demo seed or fixture inputs. Skipping demo manifest check...");
                return true;
            }

            var reader = GitIndexReader.ResolvePreCommitReader(ctx.RepoRoot, preCommit: true, ctx.IndexReader, out var ownedReader);
            using (ownedReader)
            {
                if (reader is { HasAuthoritativeIndex: true })
                {
                    using var stagedWorkspace = reader.Materialize(
                    [
                        DemoManifestGeneratorService.DemoSeedRelativePath,
                        DemoManifestGeneratorService.ExecutionSeedRelativePath,
                        DemoManifestGeneratorService.RealmRelativePath,
                        DemoManifestGeneratorService.ManifestJsRelativePath,
                        DemoManifestGeneratorService.ManifestDtsRelativePath,
                    ]);
                    return DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: stagedWorkspace.RootPath) == 0;
                }
            }
        }
        else if (ctx.Files is not null && !ctx.Files.Any(f => AffectsDemoManifest(ctx.RepoRoot, f)))
        {
            return true;
        }

        return DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: ctx.RepoRoot) == 0;
    }

    private static bool AffectsDemoManifest(string repoRoot, string file)
    {
        string rel = Path.IsPathRooted(file)
            ? Path.GetRelativePath(repoRoot, file)
            : file;
        string normalized = rel.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.StartsWith("packages/demo-fixtures/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DemoManifestGeneratorService.DemoSeedRelativePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DemoManifestGeneratorService.KernelSeedRelativePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DemoManifestGeneratorService.ExecutionSeedRelativePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DemoManifestGeneratorService.RealmRelativePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "packages/tooling_core/src/Services/DemoManifestGeneratorService.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CheckBranchFreshness(GateContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.PushBefore))
        {
            Console.WriteLine("Push-scoped run (--push-before): skipping branch-freshness ancestor check.");
            return true;
        }

        string baseRef = GitRevisionScopeService.NormalizeBaseRef(ctx.BaseRef);
        if (RunQuietProcess(ctx.RepoRoot, "git", "rev-parse", "--verify", "--quiet", baseRef) != 0)
        {
            Console.WriteLine($"[BRANCH-FRESHNESS ERROR] '{baseRef}' was not found. Fetch the base branch so branch ancestry can be verified.");
            return false;
        }

        if (RunQuietProcess(ctx.RepoRoot, "git", "merge-base", "--is-ancestor", baseRef, "HEAD") != 0)
        {
            Console.WriteLine($"[BRANCH-FRESHNESS] Branch HEAD is behind '{baseRef}'. Rebase or merge '{baseRef}' before merging (enforces strict status check policy locally).");
            return false;
        }

        Console.WriteLine($"✔ Branch HEAD includes {baseRef}.");
        return true;
    }

    private static bool CheckDotnetTestSuite(string repoRoot, string relativeProjectPath)
    {
        if (!IsDotnetAvailable())
        {
            Console.WriteLine("[ERROR] .NET SDK ('dotnet') was not found on PATH.");
            return false;
        }

        string fullProjectPath = Path.Combine(repoRoot, relativeProjectPath);
        if (!File.Exists(fullProjectPath))
        {
            Console.WriteLine($"[ERROR] Required test project '{relativeProjectPath}' was not found.");
            return false;
        }

        string? projectDir = Path.GetDirectoryName(fullProjectPath);
        bool hasAssets = projectDir is not null && File.Exists(Path.Combine(projectDir, "obj", "project.assets.json"));
        string trxDir = Path.Combine(Path.GetTempPath(), $"cadence-gate-trx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(trxDir);
        try
        {
            int exitCode = hasAssets
                ? RunProcess(repoRoot, "dotnet", "test", relativeProjectPath, "--no-restore", "--nologo", "--results-directory", trxDir, "--logger", "trx;LogFileName=gate.trx")
                : RunProcess(repoRoot, "dotnet", "test", relativeProjectPath, "--nologo", "--results-directory", trxDir, "--logger", "trx;LogFileName=gate.trx");
            if (exitCode != 0)
            {
                return false;
            }

            if (!File.Exists(Path.Combine(trxDir, "gate.trx")))
            {
                Console.WriteLine($"[ERROR] Test suite '{relativeProjectPath}' produced no TRX test report (ensure IsTestProject is enabled).");
                return false;
            }

            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(trxDir))
                {
                    Directory.Delete(trxDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of temporary TRX directory.
            }
        }
    }
    private static string Normalize(string name) => name.Trim().ToLowerInvariant().Replace('_', '-');

    private static int RunQuietProcess(string workingDirectory, string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return 1;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = stderrTask.GetAwaiter().GetResult();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not run {fileName}: {ex.Message}");
            return 1;
        }
    }

    private static int RunProcess(string workingDirectory, string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not run {fileName}: {ex.Message}");
            return 1;
        }
    }
}
