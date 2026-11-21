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

/// <summary>What a gate checks: the repository root, the files to limit it to, and whether the pre-commit hook runs it.</summary>
public sealed record GateContext(string RepoRoot, string[]? TargetFiles = null, bool PreCommit = false)
{
    /// <summary>The target files, or <c>null</c> for the whole repository.</summary>
    public string[]? Files => TargetFiles is { Length: > 0 } ? TargetFiles : null;
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
            PreCommitNote = "checks only the staged C# files; fails closed when `dotnet` is missing",
        },
        new("dotnet-format-style", [], "Roslyn code-style rules (`dotnet format style`)",
            ctx => CheckDotnetFormat(ctx, "style"))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks only the staged C# files; fails closed when `dotnet` is missing",
        },
        new("prettier", [], "Prettier formatting across JS, TS, Vue, CSS, JSON, Markdown and YAML files",
            ctx => FrontendQualityGateService.CheckPrettier(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks staged JS, TS, Vue, CSS, JSON, Markdown and YAML files; fails closed when `pnpm` or `node_modules` is missing",
        },
        new("eslint", [], "ESLint and duplicate store identifier checks across JS, TS and Vue files",
            ctx => FrontendQualityGateService.CheckEslint(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "lints staged JS, TS and Vue files; fails closed when `pnpm` or `node_modules` is missing",
        },
        new("yaml", ["check-yaml", "validate-yaml"],
            "YAML 1.2 syntax, indentation, quoted scalars, flow brackets and duplicate mapping keys",
            ctx => YamlSyntaxValidatorService.ValidateYaml(ctx.TargetFiles, ctx.RepoRoot, ctx.PreCommit) == 0)
        { TakesFiles = true },
        new("trailing-whitespace", ["check-trailing-whitespace"],
            "No trailing spaces or tabs at the end of lines in text files",
            ctx => FileHygieneValidatorService.ValidateTrailingWhitespace(ctx.TargetFiles, ctx.RepoRoot, ctx.PreCommit) == 0)
        { TakesFiles = true },
        new("end-of-file", ["end-of-file-fixer", "eof-newline"],
            "Non-empty UTF-8 text files end with a newline character",
            ctx => FileHygieneValidatorService.ValidateEndOfFileNewline(ctx.TargetFiles, ctx.RepoRoot, ctx.PreCommit) == 0)
        { TakesFiles = true },
        new("clean-secrets", ["clean-secrets-baseline"],
            "Normalizes `.secrets.baseline`; fails when it is not valid JSON (CI then checks that it did not change)",
            ctx => SecretsBaselineCleanerService.CleanSecretsBaseline(repoRoot: ctx.RepoRoot) == 0),
        new("detect-secrets", ["secrets-scan"],
            "Scans changed files against `.secrets.baseline` using `detect-secrets-hook` from `.cadence/venv`, `PATH` or `uvx` (fails closed when missing)",
            ctx => DetectSecretsGateService.Scan(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "scans staged files instead of the branch diff",
        },
        new("duplication", ["detect-code-duplication"], "Duplicated code blocks over 15 normalized lines involving a C# file",
            ctx => DuplicationDetectorService.DetectDuplication(ctx.Files, ctx.RepoRoot) == 0)
        { TakesFiles = true },
        new("cli-docs", ["validate-cli-docs"],
            "`docs/cli.md` documents every command in `Program.cs`, and the gate tables in `docs/cli.md` and `docs/CI.md` match this registry",
            ctx => CheckCliDocs(ctx.RepoRoot)),
        new("adrs", ["validate-adrs"], "ADR schema, `docs/adr/index.md` consistency, and an ADR for architectural changes",
            ctx => AdrValidatorService.ValidateAdrs(checkGitDiff: true, repoRoot: ctx.RepoRoot)),
        new("arch-drift", ["validate-architecture-drift"],
            "`docker/docker-compose.yml` exists and declares services; services missing from the architecture docs are warnings",
            ctx => ArchitectureDriftValidatorService.ValidateArchitectureDrift(ctx.RepoRoot) == 0),
        new("sanitizer", ["path-requirement-sanitizer"],
            "Requirement tags in Markdown are upper case (`@req:PRD-…`); it fixes them and fails so the fix is staged",
            ctx => ComplianceUtilityService.CheckRequirementTags(ctx.RepoRoot, ctx.Files) == 0)
        { TakesFiles = true },
        new("markdown", ["validate-markdown"],
            "Markdown structure, links, and paths into .NET test project folders (`tests/Cadence.*`) that no longer exist",
            ctx => MarkdownValidatorService.ValidateMarkdown(ctx.Files, ctx.RepoRoot) == 0)
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
            ctx => WorkflowSecurityValidatorService.ValidateWorkflowSecurity(ctx.Files, ctx.RepoRoot) == 0)
        { TakesFiles = true },
        new("rulesets", ["validate-rulesets"],
            "Branch and tag rulesets in `.github/rulesets/*.json`, strict status check policy, `CI Gate` upstream job coverage, and workflow check parity",
            ctx => RulesetService.Validate(ctx.RepoRoot) == 0),
        new("imports", ["validate-imports"], "No `using` of another service's namespace across `apps/<service>`",
            ctx => ImportValidatorService.ValidateImports(ctx.RepoRoot) == 0),
        new("security", ["audit-security"], "Hard-coded secret patterns across source, Vue templates and build scripts (private keys, cloud keys, API tokens, compliance secrets)",
            ctx => SecurityAuditorService.AuditSecurity(ctx.Files, ctx.RepoRoot) == 0)
        { TakesFiles = true },
        new("deid", ["deid-compliance-scan"],
            "PHI/PII in tracked files including authored `docs/`, root portal docs, `.vue` templates and `stores/` (vendored `docs/CDISC/`, `docs/NCI/` and `docs/SDLC/runs/` excluded), ratcheted by `.deid-baseline.txt`",
            ctx => DeidComplianceService.ScanDeidCompliance(ctx.Files, ctx.RepoRoot) == 0)
        { TakesFiles = true },
        new("openapi", ["auto-stage-openapi"], "`docs/openapi` matches what the .NET services serve, and `docs/api` matches `docs/openapi`",
            ctx => PreCommitOpenApiService.ExecutePreCommitOpenApi(ctx.RepoRoot, quick: ctx.PreCommit, targetFiles: ctx.TargetFiles) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "exports specs only for the services the staged C# files touch, otherwise checks `docs/api` against the committed `docs/openapi`",
        },
        new("api-clients", ["validate-api-clients"],
            "The web API types are generated from the current `docs/openapi`, and the web API clients typecheck against them",
            ctx => ApiClientValidatorService.ValidateApiClients(ctx.RepoRoot) == 0),
        new("schema-doc", ["auto-stage-schema-doc"], "`docs/schema` matches the EF Core models",
            ctx => CheckSchemaDoc(ctx))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "runs the drift check when staged files include Entity Framework entities, migrations, or configurations, otherwise skips it",
        },
        new("path-patterns", ["validate-path-patterns"],
            "No `tmp`, `temp`, `scratch` or `old_src` directories, `.vue` files only under `apps/`, `packages/` and `docs/.vitepress/`, `.sh` files only under `scripts/`",
            ctx => PathPatternValidatorService.ValidatePathPatterns(ctx.Files, ctx.RepoRoot) == 0)
        { TakesFiles = true },
        new("query-linter", ["query-style-linter"], "No interpolated SQL string passed to `FromSqlRaw` or `ExecuteSqlRaw`, and no comparison with a boolean literal",
            ctx => QueryLinterService.LintQueries(ctx.Files, ctx.RepoRoot) == 0)
        { TakesFiles = true },
        new("vulnerabilities", ["validate-vulnerabilities"],
            "NuGet, pnpm and Python dependency vulnerabilities not exempted by a valid `docs/SDLC/vulnerability_exclusions_ledger.json` entry (fails closed on missing or broken scanners)",
            ctx => VulnerabilityAuditorService.AuditVulnerabilities(ctx.RepoRoot) == 0),
        new("dependencies", ["validate-dependencies"], "`apps/web/package.json` lists no client-side crypto package (`node-forge`, `elliptic`, `jose`)",
            ctx => DependencyValidatorService.ValidateDependencies(ctx.RepoRoot) == 0),
        new("doc-index", ["validate-doc-index"], "Every Markdown document under `docs/` is listed in `docs/DOCUMENTATION_INDEX.md`",
            ctx => DocIndexSyncService.SyncDocIndex(check: true, repoRoot: ctx.RepoRoot) == 0),
        new("demo-manifest", ["validate-demo-manifest"],
            "`packages/demo-fixtures` matches the canonical demo seed in `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs` and `docker/cadence-realm.json`",
            ctx => DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: ctx.RepoRoot) == 0),
        new("rtm", ["gxp-rtm-validate"],
            "Requirement tags name known requirements and sit on test methods, and the generated documents match their integrity stamps and are not drafts",
            ctx => GxpRtmValidatorService.ValidateGxpRtm(ctx.RepoRoot)),
        new("gxp-snapshot", ["gxp-snapshot-guard"],
            "The branch does not change the generated RTM, IQ/OQ/PQ report or `docs/SDLC/runs/` unless `CADENCE_GXP_SNAPSHOT=1` (ADR-2532)",
            ctx => GxpSnapshotGuardService.Check(ctx.RepoRoot, ctx.PreCommit) == 0)
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks the staged files instead of the branch",
        },
        new("baseline-ratchet", ["baselines"],
            "No ratchet baseline (`*.baseline.txt`, `.deid-baseline.txt`, `.secrets.baseline`) gained an entry since the `origin/main` merge base, unless a commit has a `Baseline-Growth:` trailer",
            ctx => BaselineRatchetService.Check(ctx.RepoRoot, ctx.PreCommit) == 0)
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks the staged files; `CADENCE_BASELINE_GROWTH=\"<reason>\"` admits growth",
        },
        new("public-bundle", ["validate-public-bundle"],
            "Zero-exemption post-build scan of `docs/.vitepress/dist`, `apps/web/dist`, and `apps/subject-portal/dist` for national IDs, unapproved emails, secrets, external assets, executable local URLs, and CSP meta headers",
            ctx => PublicBundleValidatorService.ValidatePublicBundles(ctx.RepoRoot) == 0)
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "checks built VitePress, staff demo, and participant demo bundles in CI and deploy-docs after compilation",
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
        if (!IsDotnetAvailable())
        {
            Console.WriteLine("[ERROR] .NET SDK ('dotnet') was not found on PATH.");
            return false;
        }

        if (ctx.PreCommit || ctx.TargetFiles is not null)
        {
            var rawFiles = ctx.PreCommit && (ctx.TargetFiles is null || ctx.TargetFiles.Length == 0)
                ? PreCommitOpenApiService.GetStagedFiles(ctx.RepoRoot)
                : (IReadOnlyList<string>)(ctx.TargetFiles ?? []);

            var csFiles = rawFiles
                .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.IsPathRooted(f) ? Path.GetRelativePath(ctx.RepoRoot, f) : f)
                .Where(f => File.Exists(Path.Combine(ctx.RepoRoot, f)))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (csFiles.Count == 0)
            {
                return true;
            }

            if (string.Equals(subcommand, "whitespace", StringComparison.Ordinal))
            {
                var whitespaceArgs = new List<string> { "format", "whitespace", ".", "--folder", "--verify-no-changes", "--include" };
                whitespaceArgs.AddRange(csFiles);
                return RunProcess(ctx.RepoRoot, "dotnet", [.. whitespaceArgs]) == 0;
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

        return RunProcess(ctx.RepoRoot, "dotnet", "format", subcommand, "Cadence.sln", "--verify-no-changes", "--no-restore") == 0;
    }

    private static string? FindNearestCsproj(string repoRoot, string relativeFile)
    {
        string fullRepoRoot = Path.GetFullPath(repoRoot);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(fullRepoRoot, relativeFile)));
        while (!string.IsNullOrEmpty(dir) && dir.StartsWith(fullRepoRoot, StringComparison.Ordinal))
        {
            string[] projects = Directory.GetFiles(dir, "*.csproj");
            if (projects.Length == 1)
            {
                return Path.GetRelativePath(fullRepoRoot, projects[0]);
            }

            if (string.Equals(dir, fullRepoRoot, StringComparison.Ordinal))
            {
                break;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static bool IsDotnetAvailable() =>
        DetectSecretsGateService.FindExecutableOnPath("dotnet", Environment.GetEnvironmentVariable("PATH")) is not null;

    private static bool CheckSchemaDoc(GateContext ctx)
    {
        if (ctx.PreCommit)
        {
            var files = ctx.TargetFiles is { Length: > 0 }
                ? (IEnumerable<string>)ctx.TargetFiles
                : PreCommitOpenApiService.GetStagedFiles(ctx.RepoRoot);

            if (!files.Any(AffectsEfCoreSchema))
            {
                Console.WriteLine("Staged changes do not include Entity Framework entities, migrations, or configurations. Skipping schema documentation check...");
                return true;
            }
        }
        else if (ctx.Files is { Length: > 0 } && !ctx.Files.Any(AffectsEfCoreSchema))
        {
            return true;
        }

        return SchemaDocGeneratorService.GenerateSchemaDocumentation(validate: true, repoRoot: ctx.RepoRoot) == 0;
    }

    private static bool AffectsEfCoreSchema(string file)
    {
        string normalized = file.Replace('\\', '/');
        if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileNameWithoutExtension(normalized);

        return normalized.Contains("/migrations/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("migrations/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/configurations/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("configurations/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/entities/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("entities/", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("migration", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("configuration", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("entities", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("entity", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("dbcontext", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant().Replace('_', '-');

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
