using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Cadence.Compliance;
using Cadence.Kernel.Time;

namespace Cadence.Cli.Services;

/// <summary>One process a CI step runs: a program and its arguments.</summary>
/// <param name="FileName">The program, for example <c>dotnet</c> or <c>pnpm</c>.</param>
/// <param name="Arguments">The arguments, passed one by one (no shell).</param>
/// <param name="CiText">
/// The text of the matching command in <c>.github/workflows/ci.yml</c>, when the local command differs from it (a
/// different runtime identifier or output folder, for example). <c>LocalCiPlanTests</c> checks that this text, or
/// <see cref="Display"/> when it is null, appears in the step's <c>run:</c> block.
/// </param>
public sealed record CiCommand(string FileName, IReadOnlyList<string> Arguments, string? CiText = null)
{
    /// <summary>The command as a person would type it.</summary>
    public string Display => string.Join(' ', Arguments.Select(Quote).Prepend(FileName));

    /// <summary>The text that must appear in the CI step (see <see cref="CiText"/>).</summary>
    public string MatchText => CiText ?? Display;

    private static string Quote(string argument) =>
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

    /// <summary>Every regular file named <c>Dockerfile</c> (<c>find . -type f -name Dockerfile</c>).</summary>
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
    /// Work done in the CLI process instead of a child process, returning an exit code. It gets the repository root.
    /// </summary>
    public Func<string, int>? InProcess { get; init; }

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

    /// <summary>A program the step needs on <c>PATH</c>. Without it the step is skipped with <see cref="ToolHint"/>.</summary>
    public string? RequiredTool { get; init; }

    /// <summary>How to install <see cref="RequiredTool"/>.</summary>
    public string? ToolHint { get; init; }

    /// <summary>Why the step never runs locally (it needs Docker, a GitHub token or a cache).</summary>
    public string? NotRunLocally { get; init; }

    /// <summary>The step runs tests that need PostgreSQL (<c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c>).</summary>
    public bool NeedsPostgres { get; init; }

    /// <summary>Files appended to the last command.</summary>
    public CiFileSource Files { get; init; }

    /// <summary>Only files whose path matches this pattern are appended.</summary>
    public string? FilePattern { get; init; }

    /// <summary>What happens when no file is found: the step passes with this message, as the CI script does.</summary>
    public string? NoFilesMessage { get; init; }

    /// <summary>Directories to delete before the step runs, relative to the repository root.</summary>
    public IReadOnlyList<string> CleanDirectories { get; init; } = [];

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
    private const string StyleUiBuild = "lint-and-static/Build UI package";
    private const string PushOnly = "CI runs it only on a push to main";
    private const string RtmReport = "docs/SDLC/Requirements_Traceability_Matrix.md";
    private const string IqReport = "docs/SDLC/IQ_OQ_PQ_Execution_Report.md";
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
        ["actionlint"] = "runs actionlint and workflow action pinning in GitHub Actions",
        ["classify"] = "labels the pull request through the GitHub API",
        ["automerge"] = "approves and merges the pull request through the GitHub API",
        ["release-and-archive"] = "calls the production pipeline on a push to main",
        ["deploy-docs"] = "deploys the documentation site on a push to main",
    };

    /// <summary>The jobs, in the order <c>cadence ci</c> runs them.</summary>
    public static IReadOnlyList<CiJob> Jobs { get; } = BuildJobs();

    private static CiCommand Dotnet(params string[] args) => new("dotnet", args);

    private static CiCommand Pnpm(params string[] args) => new(OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm", args)
    {
        CiText = string.Join(' ', args.Prepend("pnpm")),
    };

    /// <summary><c>dotnet run [--no-build] --project packages/cli/... -- args</c>, as the CI steps call the CLI.</summary>
    private static CiCommand CadenceCli(bool noBuild, params string[] args) =>
        Dotnet([.. new[] { "run" }, .. noBuild ? new[] { "--no-build" } : [], "--project", Cli, "--", .. args]);

    private static List<CiJob> BuildJobs()
    {
        var lintAndStatic = new CiJob("lint-and-static", "Lint and Static Verification",
        [
            new("lint-and-static", "Install Node dependencies") { Commands = [Pnpm("install", "--frozen-lockfile")] },
            new("lint-and-static", "Build UI package") { Commands = [Pnpm("--filter", "ui", "build")] },
            new("lint-and-static", "Run frontend style and lint")
            {
                Commands =
                [
                    Pnpm("--filter", "web", "--filter", "subject-portal", "--filter", "ui", "exec", "prettier", "--check", "."),
                    Pnpm("-r", "lint"),
                ],
            },
            new("lint-and-static", "Run Hadolint on Dockerfiles")
            {
                RequiredTool = "hadolint",
                ToolHint = "install hadolint 2.15.1 (CI downloads it from github.com/hadolint/hadolint/releases)",
                Files = CiFileSource.Dockerfiles,
                Commands = [new CiCommand("hadolint", ["--failure-threshold", "error"])],
            },
            new("lint-and-static", "Run ShellCheck on shell scripts")
            {
                RequiredTool = "shellcheck",
                ToolHint = "install ShellCheck (CI runs 'apt-get install shellcheck')",
                Files = CiFileSource.ShellScripts,
                Commands = [new CiCommand("shellcheck", [])],
            },
            new("lint-and-static", "Run Markdown validation") { Commands = [CadenceCli(false, "check", "markdown")] },
        ]);

        var dotnetQuality = new CiJob("dotnet-quality", ".NET Quality & Sentinel Checks",
        [
            new("dotnet-quality", "Build .NET solution") { Commands = [Dotnet("build", "Cadence.sln")] },
            new("dotnet-quality", "Verify .NET C# code formatting (.editorconfig)")
            {
                Commands = [Dotnet("format", "Cadence.sln", "--verify-no-changes", "--no-restore")],
            },
            new("dotnet-quality", "Run Cadence quality sentinels") { Commands = [CadenceCli(true, "check")] },
            new("dotnet-quality", "Run architecture tests")
            {
                Commands = [Dotnet("test", "tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj", "--no-build")],
            },
            new("dotnet-quality", "Run hexagonal contract verification")
            {
                CoveredBy = QualitySentinels,
                Commands = [CadenceCli(true, "check", "contracts")],
            },
            new("dotnet-quality", "Run ADR validation") { CoveredBy = QualitySentinels, Commands = [CadenceCli(true, "check", "adrs")] },
            new("dotnet-quality", "Run CODEOWNERS coverage validation") { Commands = [CadenceCli(true, "validate-codeowners")] },
            new("dotnet-quality", "Verify schema documentation freshness")
            {
                CoveredBy = QualitySentinels,
                Commands = [CadenceCli(true, "check", "schema-doc")],
            },
            new("dotnet-quality", "Verify OpenAPI specification drift") { CoveredBy = QualitySentinels, Commands = [CadenceCli(true, "check", "openapi")] },
            new("dotnet-quality", "Run requirements traceability validation") { Commands = [CadenceCli(true, "check", "rtm")] },
            new("dotnet-quality", "Run code duplication verification")
            {
                CoveredBy = QualitySentinels,
                Files = CiFileSource.ChangedFiles,
                FilePattern = @"\.(py|js|vue|css|cs)$",
                NoFilesMessage = "No relevant files changed.",
                Commands = [CadenceCli(true, "check", "duplication")],
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
            new("docs-build", "Install Node dependencies") { Commands = [Pnpm("install", "--frozen-lockfile")] },
            new("docs-build", "Run documentation compilation")
            {
                Commands = [Pnpm("docs:build")],

                // scripts/build-docs.js runs the full 'cadence check' again before building; it skips that when the
                // same gates already passed in this run (#5028). It also rewrites the RTM as a draft.
                EnvironmentIfPassed = [new(QualitySentinels, "CADENCE_DOCS_SKIP_CHECKS", "1")],
                RestoreFiles = [RtmReport, IqReport],
            },
        ]);

        var security = new CiJob("security", "Security and Vulnerability Audit",
        [
            new("security", "Run static analysis") { Always = true, CoveredBy = QualitySentinels, Commands = [CadenceCli(false, "check", "audit-security")] },
            new("security", "Build container image for security scan") { NotRunLocally = $"{PushOnly}, and it needs Docker" },
            new("security", "Run container vulnerability scan") { NotRunLocally = $"{PushOnly}, and it needs Docker and Trivy" },
            new("security", "Upload Trivy scan results to GitHub Security tab") { NotRunLocally = $"{PushOnly}, and it needs GitHub SARIF upload" },
            new("security", "Run dependency audit") { Always = true, CoveredBy = QualitySentinels, Commands = [CadenceCli(false, "check", "vulnerabilities")] },
            new("security", "Run secret detection")
            {
                Always = true,
                CoveredBy = QualitySentinels,
                Commands =
                [
                    CadenceCli(false, "check", "clean-secrets"),
                    new CiCommand("git", ["diff", "--exit-code", ".secrets.baseline"]),
                    CadenceCli(false, "check", "security"),
                ],
            },
            new("security", "Run detect-secrets against the baseline")
            {
                Always = true,
                RequiredTool = "detect-secrets-hook",
                ToolHint = "pip install detect-secrets==1.5.0",
                Files = CiFileSource.ChangedFilesAcmr,
                NoFilesMessage = "No changed files to scan.",
                Commands = [new CiCommand("detect-secrets-hook", ["--baseline", ".secrets.baseline", "--exclude-files", @"'(pnpm-lock\.yaml|package-lock\.json|yarn\.lock)'"])],
            },
            new("security", "Run DEID compliance scan")
            {
                Always = true,
                CoveredBy = QualitySentinels,
                Files = CiFileSource.ChangedFiles,
                FilePattern = @"\.(py|js|ts|tsx|jsx|json|md|log|txt|cs)$",
                NoFilesMessage = "No changed files to scan.",
                Commands = [CadenceCli(false, "check", "deid")],
            },
        ]);

        var frontend = new CiJob("unit-tests", "Frontend Unit Tests",
        [
            new("unit-tests", "Install Node dependencies") { CoveredBy = StyleNodeInstall, Commands = [Pnpm("install", "--frozen-lockfile")] },
            new("unit-tests", "Build UI package") { CoveredBy = StyleUiBuild, Commands = [Pnpm("--filter", "ui", "build")] },
            new("unit-tests", "Run frontend unit tests") { Always = true, Commands = [Pnpm("-r", "test")] },
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
                    Dotnet("test", "Cadence.sln", "--configuration", "Release", "--no-build",
                        "--results-directory", RtmGeneratorService.DefaultResultsDir, "--logger", "trx;LogFilePrefix=gxp")
                        with { CiText = "dotnet test Cadence.sln --configuration Release --no-build" },
                ],
            },
        ]);

        var e2e = new CiJob(E2eJob, "Parallel E2E Accessibility Testing",
        [
            new(E2eJob, "Install Node dependencies") { CoveredBy = StyleNodeInstall, Commands = [Pnpm("install", "--frozen-lockfile")] },
            new(E2eJob, "Install Playwright Browsers")
            {
                NotRunLocally = "cadence ci does not download browsers; run 'pnpm exec playwright install chromium' once yourself",
            },
            new(E2eJob, "Build UI package") { CoveredBy = StyleUiBuild, Commands = [Pnpm("--filter", "ui", "build")] },
            new(E2eJob, "Run Subject Portal E2E Accessibility Tests") { Commands = [Pnpm("--filter", "subject-portal", "test:e2e")] },
            new(E2eJob, "Run Web Application E2E Accessibility Tests") { Commands = [Pnpm("--filter", "web", "test:e2e")] },
        ])
        {
            OnRequestOnly = $"{PushOnly}; run it with '--job {E2eJob}'",
        };

        var compliance = new CiJob("compliance", "GxP Compliance & Qualification Reporting",
        [
            new("compliance", "Install Node dependencies") { CoveredBy = StyleNodeInstall, Commands = [Pnpm("install", "--frozen-lockfile")] },
            new("compliance", "Generate Compliance Reports")
            {
                InProcess = repoRoot => RtmGeneratorService.GenerateRtm(
                    draft: false, validateOnly: false, reportPath: Path.Combine(repoRoot, RtmGeneratorService.DefaultResultsDir), repoRoot: repoRoot,
                    honorDraftEnvironment: false, evidence: RtmEvidenceContext.Capture(repoRoot, "pnpm cadence ci", SystemClock.Instance)),
                InProcessDisplay = $"generate the RTM and IQ/OQ/PQ report from {RtmGeneratorService.DefaultResultsDir} (what 'gxp sync' does after its test run; nothing is staged)",
                InProcessCiText = $"dotnet run --project {Cli} -- gxp sync",
            },
            new("compliance", "Assert No Documentation Git Diff")
            {
                // The evidence block (revision, time, database, runner) changes on every run, so the documents are
                // compared without it. A match is put back to the committed copy so the run leaves no change behind.
                InProcess = repoRoot => GxpDocsDiffService.Compare(repoRoot, restoreWhenEquivalent: true),
                InProcessDisplay = "compare the regenerated RTM and IQ/OQ/PQ report with the committed copies, ignoring the evidence block",
                InProcessCiText = $"dotnet run --project {Cli} -- gxp diff",
            },
        ])
        {
            Needs = ["unit-tests", "dotnet-tests"],
            NeedsStep = new Dictionary<string, string>
            {
                ["unit-tests"] = "unit-tests/Run frontend unit tests",
                ["dotnet-tests"] = "dotnet-tests/Execute .NET 10 xUnit Test Suites",
            },
        };

        return [lintAndStatic, dotnetQuality, nativeAotBuild, docsBuild, security, frontend, dotnetTests, e2e, compliance];
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

    /// <summary>Whether a path matches a step's <see cref="CiStep.FilePattern"/> (all paths match when it has none).</summary>
    public static bool MatchesFilePattern(CiStep step, string path) =>
        step.FilePattern is null || Regex.IsMatch(path, step.FilePattern, RegexOptions.None, TimeSpan.FromSeconds(1));
}
