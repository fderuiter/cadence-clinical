using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

/// <summary>One process a CI step runs: a program and its arguments.</summary>
/// <param name="FileName">The program, for example <c>dotnet</c> or <c>pnpm</c>.</param>
/// <param name="Arguments">The arguments, passed one by one (no shell).</param>
/// <param name="CiText">
/// The text of the matching command in <c>.github/workflows/ci.yml</c>, when the local command differs from it (a
/// different runtime identifier or output folder, for example). <c>LocalCiPlanTests</c> checks that this text, or
/// <see cref="Display"/> when it is null, appears in the step's <c>run:</c> block.
/// </param>
/// <param name="AllowedExitCodes">Exit codes treated as success (defaults to <c>0</c> only when null).</param>
/// <param name="ForwardsBaseRef">Whether <c>cadence ci</c> appends <c>--base &lt;ref&gt;</c> when running this command.</param>
public sealed record CiCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? CiText = null,
    IReadOnlyList<int>? AllowedExitCodes = null,
    bool ForwardsBaseRef = false)
{
    /// <summary>The command as a person would type it.</summary>
    public string Display => string.Join(' ', Arguments.Select(QuoteArgument).Prepend(FileName));

    /// <summary>The text that must appear in the CI step (see <see cref="CiText"/>).</summary>
    public string MatchText => CiText ?? Display;

    public static string QuoteArgument(string argument) =>
        argument.Length > 0 && argument.IndexOfAny([' ', ';', '"']) < 0 ? argument : $"\"{argument}\"";
}

/// <summary>An environment variable a step's commands get when another step passed earlier in the same run.</summary>
/// <param name="StepKey">The step (<c>job/name</c>) that must have passed.</param>
/// <param name="Name">The variable.</param>
/// <param name="Value">Its value.</param>
public sealed record CiEnvironmentIfPassed(string StepKey, string Name, string Value);

/// <summary>Which files a step appends to its command, discovered when it runs.</summary>
public enum CiFileSource
{
    /// <summary>The step's command takes no file list.</summary>
    None,

    /// <summary>Files changed against the base ref (<c>git diff --name-only base...HEAD</c>), else the working tree changes.</summary>
    ChangedFiles,

    /// <summary>Files added, copied, modified or renamed against the base ref (<c>--diff-filter=ACMR</c>).</summary>
    ChangedFilesAcmr,

    /// <summary>Every regular file matching <c>Dockerfile*</c> outside <c>.venv</c> and <c>node_modules</c>.</summary>
    Dockerfiles,

    /// <summary>Every <c>*.sh</c> file outside <c>.venv</c> and <c>node_modules</c>.</summary>
    ShellScripts,
}

/// <summary>One step of a CI job and how <c>cadence ci</c> runs it locally.</summary>
/// <param name="Job">The job key in <c>ci.yml</c>, for example <c>style</c>.</param>
/// <param name="Name">The step name, exactly as in <c>ci.yml</c>.</param>
public sealed record CiStep(string Job, string Name)
{
    /// <summary>The processes the step runs, in order. It stops at the first one that fails.</summary>
    public IReadOnlyList<CiCommand> Commands { get; init; } = [];

    /// <summary>
    /// Work done in the CLI process instead of a child process, returning an exit code. It gets the repository root and
    /// the local CI options (including <see cref="LocalCiOptions.BaseRef"/>).
    /// </summary>
    public Func<string, LocalCiOptions, int>? InProcess { get; init; }

    /// <summary>Describes <see cref="InProcess"/> in the plan and the summary.</summary>
    public string? InProcessDisplay { get; init; }

    /// <summary>The text of the CI command that <see cref="InProcess"/> stands in for.</summary>
    public string? InProcessCiText { get; init; }

    /// <summary>The step has <c>if: always()</c> in CI, so it runs after an earlier step of its job failed.</summary>
    public bool Always { get; init; }

    /// <summary>
    /// Another step (<c>job/name</c>) that runs the same check. When that step passed earlier in the same run, this one
    /// is reported as covered instead of running again; otherwise it runs.
    /// </summary>
    public string? CoveredBy { get; init; }

    /// <summary>
    /// A program the step needs on <c>PATH</c>. Without it the step fails unless <c>--allow-missing-tools</c> skips it with
    /// <see cref="ToolHint"/>.
    /// </summary>
    public string? RequiredTool { get; init; }

    /// <summary>How to install <see cref="RequiredTool"/>.</summary>
    public string? ToolHint { get; init; }

    /// <summary>Why the step never runs locally (it needs Docker, a GitHub token or a cache).</summary>
    public string? NotRunLocally { get; init; }

    /// <summary>The step runs tests that need PostgreSQL (<c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c>).</summary>
    public bool NeedsPostgres { get; init; }

    /// <summary>Files appended to the last command.</summary>
    public CiFileSource Files { get; init; }

    /// <summary>What happens when no file is found: the step passes with this message, as the CI script does.</summary>
    public string? NoFilesMessage { get; init; }

    /// <summary>Directories to delete before the step runs, relative to the repository root.</summary>
    public IReadOnlyList<string> CleanDirectories { get; init; } = [];

    /// <summary>Variables always set for the step's commands.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Variables set for the step's commands when another step passed first (see <see cref="CiEnvironmentIfPassed"/>).</summary>
    public IReadOnlyList<CiEnvironmentIfPassed> EnvironmentIfPassed { get; init; } = [];

    /// <summary>
    /// Tracked files the step rewrites as a side effect, relative to the repository root. They are put back as they
    /// were when the step ends, since a CI runner is thrown away but a working tree is not.
    /// </summary>
    public IReadOnlyList<string> RestoreFiles { get; init; } = [];

    /// <summary>The step's key, <c>job/name</c>.</summary>
    public string Key => $"{Job}/{Name}";
}

/// <summary>A job of <c>ci.yml</c>.</summary>
/// <param name="Key">The job key, for example <c>dotnet-tests</c>.</param>
/// <param name="Name">The job's display name, exactly as in <c>ci.yml</c>.</param>
/// <param name="Steps">The job's steps that run a command, in CI order.</param>
public sealed record CiJob(string Key, string Name, IReadOnlyList<CiStep> Steps)
{
    /// <summary>Jobs that must pass first, as the job's <c>needs:</c> in CI. Selecting this job selects them too.</summary>
    public IReadOnlyList<string> Needs { get; init; } = [];

    /// <summary>
    /// For each needed job, the step whose success CI checks (<c>needs.&lt;job&gt;.outputs.test_outcome</c>). Without
    /// an entry, the whole job must pass.
    /// </summary>
    public IReadOnlyDictionary<string, string> NeedsStep { get; init; } = new Dictionary<string, string>();

    /// <summary>Why the job runs only when asked for with <c>--job</c>, or null when it runs by default.</summary>
    public string? OnRequestOnly { get; init; }
}

/// <summary>
/// The jobs and steps of <c>.github/workflows/ci.yml</c>, as <c>cadence ci</c> runs them on a developer machine while
/// GitHub Actions does not run jobs for this repository (#5017). <c>LocalCiPlanTests</c> reads <c>ci.yml</c> and fails
/// when a job or step is added, renamed, removed or changes its command without this plan following.
/// </summary>
public static class CiPlan
{
    private const string Cli = "packages/cli/src/Cadence.Cli.csproj";
    private const string QualitySentinels = "dotnet-quality/Run Cadence quality sentinels";
    private const string StyleNodeInstall = "lint-and-static/Install Node dependencies";
    private const string StyleFrontendBuild = "lint-and-static/Build frontend workspace";
    private const string E2eJob = "accessibility-e2e"; // deid-ignore: the ci.yml job key, not an account number

    /// <summary>
    /// The portable runtime identifier the Native AOT publish targets: this machine's (<c>linux-x64</c> in CI), since
    /// AOT does not cross-compile. <see cref="RuntimeInformation.RuntimeIdentifier"/> can name a distribution
    /// (<c>ubuntu.24.04-x64</c>) that has no runtime pack, so the identifier is built from the OS and architecture.
    /// </summary>
    public static string AotRuntimeIdentifier
    {
        get
        {
            string os = "linux";
            if (OperatingSystem.IsWindows()) os = "win";
            else if (OperatingSystem.IsMacOS()) os = "osx";
            return $"{os}-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
        }
    }

    /// <summary>Jobs of <c>ci.yml</c> that act on GitHub itself and have no local meaning, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> GitHubOnlyJobs { get; } = new Dictionary<string, string>
    {
        ["container-security"] = "builds container images across a matrix and uploads Trivy SARIF to GitHub Code Scanning",
        ["ci-gate"] = "aggregates pull-request validation job outcomes into one required branch ruleset status check",
        ["classify"] = "labels the pull request through the GitHub API",
        ["automerge"] = "approves and merges the pull request through the GitHub API",
        ["release-and-archive"] = "calls the production pipeline on pushes to main, release/* and v* tags",
        ["deploy-docs"] = "deploys the documentation portal to GitHub Pages on a push to main",
    };

    /// <summary>The jobs, in the order <c>cadence ci</c> runs them.</summary>
    public static IReadOnlyList<CiJob> Jobs { get; } = BuildJobs();

    private static CiCommand Dotnet(params string[] args) => new("dotnet", args);

    private static CiCommand Pnpm(params string[] args) => new(OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm", args)
    {
        CiText = string.Join(' ', args.Prepend("pnpm")),
    };

    /// <summary><c>dotnet run --no-build --project packages/cli/... -- args</c>, as <c>dotnet-quality</c> calls the CLI.</summary>
    private static CiCommand CadenceCliNoBuild(params string[] args) =>
        Dotnet(["run", "--no-build", "--project", Cli, "--", .. args]) with
        {
            ForwardsBaseRef = args.FirstOrDefault() == "check",
        };

    /// <summary><c>node scripts/run-cli.js args</c>, as the Node-equipped CI steps call the CLI.</summary>
    private static CiCommand NodeCli(params string[] args) =>
        new("node", ["scripts/run-cli.js", .. args], ForwardsBaseRef: args.FirstOrDefault() == "check");

    private static List<CiJob> BuildJobs()
    {
        var actionlint = new CiJob("actionlint", "Workflow Action Pinning & Actionlint Gate",
        [
            new("actionlint", "Run actionlint")
            {
                RequiredTool = "actionlint",
                ToolHint = "provision actionlint 1.7.12 into .cadence/bin via './scripts/setup_wizard.sh --minimal'",
                Commands = [new CiCommand("actionlint", [])],
            },
        ]);

        var lintAndStatic = new CiJob("lint-and-static", "Lint and Static Verification",
        [
            new("lint-and-static", "Install Node dependencies") { Commands = [Pnpm("install", "--frozen-lockfile")] },
            new("lint-and-static", "Build frontend workspace") { Commands = [Pnpm("build")] },
            new("lint-and-static", "Run frontend style and lint")
            {
                Commands =
                [
                    Pnpm("exec", "prettier", "--check", "--ignore-path", ".prettierignore", "**/*.{js,jsx,ts,tsx,mjs,cjs,vue,json,css,md,yaml,yml}")
                        with { CiText = "pnpm exec prettier --check --ignore-path .prettierignore \"**/*.{js,jsx,ts,tsx,mjs,cjs,vue,json,css,md,yaml,yml}\"" },
                    Pnpm("-r", "lint"),
                ],
            },
            new("lint-and-static", "Run Hadolint on Dockerfiles")
            {
                RequiredTool = "hadolint",
                ToolHint = "provision hadolint 2.15.1 into .cadence/bin via './scripts/setup_wizard.sh --minimal'",
                Files = CiFileSource.Dockerfiles,
                Commands = [new CiCommand("hadolint", ["--failure-threshold", "error"])],
            },
            new("lint-and-static", "Run ShellCheck on shell scripts")
            {
                RequiredTool = "shellcheck",
                ToolHint = $"provision shellcheck {CliLinterGateService.ShellcheckVersion} into .cadence/bin via './scripts/setup_wizard.sh --minimal' (or 'pwsh scripts/setup_wizard.ps1 -Minimal', or pass --allow-missing-tools to skip)",
                Files = CiFileSource.ShellScripts,
                Commands = [new CiCommand("shellcheck", [])],
            },
            new("lint-and-static", "Run installer checksum tests")
            {
                RequiredTool = "bash",
                ToolHint = "install bash (Git for Windows includes it)",
                Commands = [new CiCommand("bash", ["tests/test_installer_checksums.sh"])],
            },
            new("lint-and-static", "Run Markdown validation") { Commands = [NodeCli("check", "markdown")] },
            new("lint-and-static", "Validate web API clients against OpenAPI types") { Commands = [NodeCli("check", "api-clients")] },
        ])
        {
            Needs = ["native-aot-build"],
        };

        var dotnetQuality = new CiJob("dotnet-quality", ".NET Quality & Sentinel Checks",
        [
            new("dotnet-quality", "Build .NET solution") { Commands = [Dotnet("build", "Cadence.sln")] },
            new("dotnet-quality", "Run Cadence quality sentinels")
            {
                Commands = [CadenceCliNoBuild("check", "--exclude", "prettier", "--exclude", "eslint", "--exclude", "actionlint", "--exclude", "hadolint", "--exclude", "detect-secrets", "--exclude", "api-clients", "--exclude", "vulnerabilities", "--exclude", "public-bundle", "--exclude", "architecture-tests", "--exclude", "unit-tests")],
            },
            new("dotnet-quality", "Run architecture tests")
            {
                Commands = [Dotnet("test", "tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj", "--no-build")],
            },
        ]);

        var nativeAotBuild = new CiJob("native-aot-build", "Native AOT Compilation",
        [
            new("native-aot-build", "Publish the CLI as Native AOT")
            {
                Commands =
                [
                    Dotnet("publish", Cli, "-c", "Release", "-r", AotRuntimeIdentifier, "-o", Path.Combine(Path.GetTempPath(), "cadence-ci-aot"))
                        with { CiText = $"dotnet publish {Cli} -c Release -r linux-x64" },
                ],
            },
        ]);

        var docsBuild = new CiJob("docs-build", "Documentation Compilation",
        [
            new("docs-build", "Install Node dependencies") { CoveredBy = StyleNodeInstall, Commands = [Pnpm("install", "--frozen-lockfile")] },
            new("docs-build", "Build UI package") { CoveredBy = StyleFrontendBuild, Commands = [Pnpm("--filter", "ui", "build")] },
            new("docs-build", "Run documentation compilation")
            {
                Commands = [Pnpm("docs:build")],

                // scripts/build-docs.js runs openapi, schema-doc, and markdown checks before building; it skips those
                // duplicate gates when they already passed in this run (#5028), while still running 'check rtm'.
                EnvironmentIfPassed = [new(QualitySentinels, "CADENCE_DOCS_SKIP_CHECKS", "1")],
            },
            new("docs-build", "Build and stage co-hosted interactive demos") { Commands = [Pnpm("docs:stage-demos")] },
            new("docs-build", "Verify public bundles") { Commands = [NodeCli("check", "public-bundle")] },
        ])
        {
            Needs = ["native-aot-build"],
        };

        var security = new CiJob("security", "Security and Vulnerability Audit",
        [
            new("security", "Run static analysis") { Always = true, CoveredBy = QualitySentinels, Commands = [NodeCli("check", "audit-security")] },
            new("security", "Run dependency audit")
            {
                Always = true,
                Commands = [Dotnet("restore", "Cadence.sln"), NodeCli("check", "vulnerabilities")],
            },
            new("security", "Run secret detection")
            {
                Always = true,
                CoveredBy = QualitySentinels,
                Commands =
                [
                    NodeCli("check", "clean-secrets"),
                    NodeCli("check", "security"),
                ],
            },
            new("security", "Run detect-secrets against the baseline")
            {
                Always = true,
                RequiredTool = "detect-secrets-hook",
                ToolHint = $"run './scripts/setup_wizard.sh --minimal' or 'python3 -m venv .cadence/venv && .cadence/venv/bin/python -m pip install --upgrade pip detect-secrets=={DetectSecretsGateService.PinnedVersion}'",
                Commands = [NodeCli("check", "detect-secrets")],
            },
            new("security", "Run DEID compliance scan")
            {
                Always = true,
                CoveredBy = QualitySentinels,
                Commands = [NodeCli("check", "deid")],
            },
        ])
        {
            Needs = ["native-aot-build"],
        };

        var frontend = new CiJob("unit-tests", "Frontend Unit Tests",
        [
            new("unit-tests", "Install Node dependencies") { CoveredBy = StyleNodeInstall, Commands = [Pnpm("install", "--frozen-lockfile")] },
            new("unit-tests", "Build UI package") { CoveredBy = StyleFrontendBuild, Commands = [Pnpm("--filter", "ui", "build")] },
            new("unit-tests", "Run frontend unit tests")
            {
                Always = true,
                Commands =
                [
                    Pnpm("-r", "test"),
                    Pnpm("exec", "vitest", "run", "--config", "vitest.config.mjs"),
                ],
            },
        ]);

        var dotnetTests = new CiJob("dotnet-tests", ".NET 10 C# xUnit Verification",
        [
            new("dotnet-tests", "Restore NuGet Packages") { Commands = [Dotnet("restore", "Cadence.sln")] },
            new("dotnet-tests", "Build .NET 10 Solution") { Commands = [Dotnet("build", "Cadence.sln", "--configuration", "Release", "--no-restore")] },
            new("dotnet-tests", "Execute .NET 10 xUnit Test Suites")
            {
                // The TRX files go where 'gxp sync' writes them, so the compliance job builds the RTM from this run
                // instead of running every test a second time.
                Always = true,
                NeedsPostgres = true,
                CleanDirectories = [RtmGeneratorService.DefaultResultsDir],
                Commands =
                [
                    Dotnet("test", "Cadence.sln", "--configuration", "Release", "-nologo", "--no-build",
                        "--results-directory", RtmGeneratorService.DefaultResultsDir, "--logger", "trx;LogFilePrefix=gxp")
                        with
                        {
                            CiText = $"dotnet test Cadence.sln --configuration Release --no-build --results-directory {RtmGeneratorService.DefaultResultsDir} --logger \"trx;LogFilePrefix=gxp\"",
                        },
                ],
            },
            new("dotnet-tests", "Fail the run when a PostgreSQL test did not execute")
            {
                // LocalCiRunner checks DatabaseTestEvidence on DefaultResultsDir when NeedsPostgres is set (unless
                // --allow-db-skip is passed), standing in for check-database-test-results.sh while also running its self-test.
                NeedsPostgres = true,
                RequiredTool = "bash",
                ToolHint = "install bash to run the shell gate self-test; DatabaseTestEvidence already checked the TRX reports",
                Commands = [new CiCommand("bash", ["scripts/test-check-database-test-results.sh"])],
                InProcessDisplay = $"verify no PostgreSQL test in {RtmGeneratorService.DefaultResultsDir} was skipped",
                InProcessCiText = "bash scripts/check-database-test-results.sh",
            },
        ]);

        var e2e = new CiJob(E2eJob, "Parallel E2E Accessibility Testing",
        [
            new(E2eJob, "Install Node dependencies") { CoveredBy = StyleNodeInstall, Commands = [Pnpm("install", "--frozen-lockfile")] },
            new(E2eJob, "Install Playwright Browsers")
            {
                NotRunLocally = "cadence ci does not download browsers; run 'pnpm --filter web exec playwright install chromium' once yourself",
            },
            new(E2eJob, "Build UI package") { CoveredBy = StyleFrontendBuild, Commands = [Pnpm("--filter", "ui", "build")] },
            new(E2eJob, "Run Subject Portal E2E Accessibility Tests") { Commands = [Pnpm("--filter", "subject-portal", "test:e2e")] },
            new(E2eJob, "Run Web Application E2E Accessibility Tests")
            {
                Environment = new Dictionary<string, string> { ["CADENCE_TEST_BROWSER"] = "1" },
                Commands = [Pnpm("--filter", "web", "test:e2e")],
            },
            new(E2eJob, "Run Web Application E2E Workflow Tests")
            {
                Environment = new Dictionary<string, string>
                {
                    ["CADENCE_TEST_BROWSER"] = "1",
                    ["DEMO_MODE"] = "false",
                },
                Commands = [Pnpm("--filter", "web", "test:e2e:workflows")],
            },
            new(E2eJob, "Run Browser-to-Database Correction Integration Test")
            {
                NeedsPostgres = true,
                Environment = new Dictionary<string, string> { ["CADENCE_TEST_BROWSER"] = "1" },
                Commands =
                [
                    Dotnet("test", "tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj", "--configuration", "Release",
                        "--filter", "FullyQualifiedName~BrowserCorrectionReachesTheDatabaseWithItsReason"),
                ],
            },
        ]);

        var compliance = new CiJob("compliance", "GxP Compliance & Qualification Reporting",
        [
            new("compliance", "Install Node dependencies") { CoveredBy = StyleNodeInstall, Commands = [Pnpm("install", "--frozen-lockfile")] },
            new("compliance", "Generate Compliance Reports")
            {
                // A feature branch writes a preview and validates its tags and results; only snapshot mode writes
                // docs/SDLC (ADR-2532). Unknown requirement IDs and failing tagged tests fail both.
                InProcess = (repoRoot, _) =>
                {
                    bool snapshot = GxpSnapshotPolicy.IsSnapshotMode();
                    string outputDir = snapshot ? "docs/SDLC" : GxpSnapshotPolicy.PreviewDir;
                    string results = Path.Combine(repoRoot, RtmGeneratorService.DefaultResultsDir);
                    int generated = RtmGeneratorService.GenerateRtm(
                        outputDir: outputDir, draft: false, validateOnly: false, reportPath: results, repoRoot: repoRoot,
                        honorDraftEnvironment: false, evidence: RtmEvidenceContext.Capture(repoRoot, "pnpm cadence ci", SystemClock.Instance));
                    return generated != 0
                        ? generated
                        : RtmGeneratorService.GenerateRtm(
                            outputDir: outputDir, validateOnly: true, reportPath: results, repoRoot: repoRoot, honorDraftEnvironment: false,
                            requireCurrentSnapshot: snapshot, requireRecordedEvidence: true);
                },
                InProcessDisplay = $"generate the RTM and IQ/OQ/PQ report from {RtmGeneratorService.DefaultResultsDir} into {GxpSnapshotPolicy.PreviewDir} " +
                    $"(docs/SDLC with {GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1) and validate tags and results; nothing is staged",
                InProcessCiText = "node scripts/run-cli.js gxp sync --results-dir TestResults/gxp",
            },
            new("compliance", "Check GxP Snapshot Policy")
            {
                // A feature branch must not change the snapshot. In snapshot mode the regenerated documents are compared
                // with the snapshot instead, ignoring the evidence block, and a match is put back to the staged copy.
                InProcess = (repoRoot, options) => GxpSnapshotGuardService.Check(repoRoot, preCommit: false, baseRef: options.BaseRef),
                InProcessDisplay = $"refuse branch changes to the generated GxP snapshot ({GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1: compare the regenerated documents with it)",
                InProcessCiText = "node scripts/run-cli.js check gxp-snapshot",
            },
        ])
        {
            Needs = ["native-aot-build", "unit-tests", "dotnet-tests"],
            NeedsStep = new Dictionary<string, string>
            {
                ["unit-tests"] = "unit-tests/Run frontend unit tests",
            },
        };

        return [actionlint, nativeAotBuild, lintAndStatic, dotnetQuality, docsBuild, security, frontend, dotnetTests, e2e, compliance];
    }

    /// <summary>
    /// The jobs to run for the given <c>--job</c> keys, in plan order: every job that is not on request only when none
    /// is given, and each named job's <see cref="CiJob.Needs"/> otherwise.
    /// </summary>
    /// <exception cref="ArgumentException">A key names no job.</exception>
    public static IReadOnlyList<CiJob> Select(IReadOnlyCollection<string> jobKeys)
    {
        if (jobKeys.Count == 0)
        {
            return [.. Jobs.Where(j => j.OnRequestOnly is null)];
        }

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in jobKeys)
        {
            var job = Jobs.FirstOrDefault(j => j.Key == key)
                ?? throw new ArgumentException(GitHubOnlyJobs.TryGetValue(key, out var why)
                    ? $"Job '{key}' {why}, so it has no local equivalent."
                    : $"Unknown job '{key}'. Jobs: {string.Join(", ", Jobs.Select(j => j.Key))}.");
            wanted.Add(job.Key);
            wanted.UnionWith(job.Needs);
        }

        return [.. Jobs.Where(j => wanted.Contains(j.Key))];
    }
}
