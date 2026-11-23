using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

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

    /// <summary>Whether <see cref="InProcess"/> forwards <see cref="LocalCiOptions.BaseRef"/>, matching <c>SCOPE_ARGS</c> in CI.</summary>
    public bool InProcessForwardsBaseRef { get; init; }

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

    /// <summary>The step builds or scans container images and needs a reachable Docker daemon.</summary>
    public bool NeedsDocker { get; init; }

    /// <summary>The step runs tests that need PostgreSQL (<c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c>).</summary>
    public bool NeedsPostgres { get; init; }

    /// <summary>
    /// The step runs a filtered PostgreSQL test outside <c>dotnet-tests</c>, so full-repository TRX and <c>*.database.txt</c>
    /// discovery stays scoped to <c>dotnet-tests</c> and <c>compliance</c>.
    /// </summary>
    public bool ScopedDatabaseTest { get; init; }

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

/// <summary>One container image built and scanned by <c>container-security</c> in <c>.github/workflows/ci.yml</c>.</summary>
public sealed record CiContainerScanTarget(string Target, string Context, string Dockerfile, string AppName)
{
    /// <summary>The local tag built and passed to Trivy (<c>cadence-&lt;target&gt;:latest</c>).</summary>
    public string ImageTag => $"cadence-{Target}:latest";
}

/// <summary>
/// The jobs and steps of <c>.github/workflows/ci.yml</c>, parsed directly via <see cref="WorkflowYamlParser"/> and
/// augmented with local-only execution overrides for <c>cadence ci</c> (#5017).
/// </summary>
public static partial class CiPlan
{
    /// <summary>The job key of the container build and Trivy scan job in <c>ci.yml</c>.</summary>
    public const string ContainerSecurityJobKey = "container-security";

    private const string Cli = "packages/cli/src/Cadence.Cli.csproj";
    private const string NativeAotPublish = "native-aot-build/Publish the CLI as Native AOT";
    private const string QualitySentinels = "dotnet-quality/Run Cadence quality sentinels";
    private const string StyleNodeInstall = "lint-and-static/Install Node dependencies";
    private const string StyleFrontendBuild = "lint-and-static/Build frontend workspace";
    private const string E2eJob = "accessibility-e2e"; // deid-ignore: the ci.yml job key, not an account number
    private const string DockerBuildCiText = "docker build";
    private static readonly IReadOnlyList<CiEnvironmentIfPassed> NativeAotCacheHit =
    [
        new(NativeAotPublish, "CADENCE_CLI_CACHE_HIT", "true"),
    ];

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
    public static IReadOnlyDictionary<string, string> GitHubOnlyJobs { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ci-gate"] = "aggregates pull-request validation job outcomes into one required branch ruleset status check",
        ["classify"] = "labels the pull request through the GitHub API",
        ["automerge"] = "approves and merges the pull request through the GitHub API",
        ["release-and-archive"] = "calls the production pipeline on pushes or ref-guarded dispatches to main, release/* and v* tags",
        ["deploy-docs"] = "deploys the documentation portal to GitHub Pages on pushes or ref-guarded dispatches to main",
    };

    /// <summary>Container matrix targets in <c>.github/workflows/ci.yml</c>.</summary>
    public static IReadOnlyList<CiContainerScanTarget> ContainerScanTargets { get; } =
    [
        new("gateway", ".", "docker/Dockerfile.dotnet", "gateway"),
        new("designer", ".", "docker/Dockerfile.dotnet", "designer"),
        new("execution", ".", "docker/Dockerfile.dotnet", "execution"),
        new("etmf", ".", "docker/Dockerfile.dotnet", "etmf"),
        new("interop", ".", "docker/Dockerfile.dotnet", "interop"),
        new("quality", ".", "docker/Dockerfile.dotnet", "quality"),
        new("notifications", ".", "docker/Dockerfile.dotnet", "notifications"),
        new("ctms", ".", "docker/Dockerfile.dotnet", "ctms"),
        new("safety", ".", "docker/Dockerfile.dotnet", "safety"),
        new("tickets", ".", "docker/Dockerfile.dotnet", "tickets"),
        new("eisf", ".", "docker/Dockerfile.dotnet", "eisf"),
        new("econsent", ".", "docker/Dockerfile.dotnet", "econsent"),
        new("org", ".", "docker/Dockerfile.dotnet", "org"),
        new("fileshare", ".", "docker/Dockerfile.dotnet", "fileshare"),
        new("imaging", ".", "docker/Dockerfile.dotnet", "imaging"),
        new("knowledge", ".", "docker/Dockerfile.dotnet", "knowledge"),
        new("payments", ".", "docker/Dockerfile.dotnet", "payments"),
        new("kernel-api", ".", "docker/Dockerfile.dotnet", "kernel-api"),
        new("sce-python", "docker/sce/python", "docker/sce/python/Dockerfile", ""),
        new("sce-r", "docker/sce/r", "docker/sce/r/Dockerfile", ""),
    ];

    /// <summary>
    /// File globs in <c>.github/workflows/ci.yml</c> (<c>classify</c>) that set <c>container_scan=true</c> on pull requests.
    /// </summary>
    public static IReadOnlyList<string> ContainerScanTriggerGlobs { get; } =
    [
        "docker/*",
        "Dockerfile*",
        "*/Dockerfile*",
        ".dockerignore",
        ".github/dependabot.yml",
        ".github/workflows/ci.yml",
        "Cadence.sln",
        "global.json",
        ".editorconfig",
        "BannedSymbols.txt",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "NuGet.Config",
        "*.csproj",
        "apps/**/*.cs",
        "packages/**/*.cs",
    ];

    /// <summary>Whether a repository-relative path matches <see cref="ContainerScanTriggerGlobs"/> as bash <c>case</c> evaluates them.</summary>
    public static bool IsContainerScanTrigger(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        normalized = normalized.TrimStart('/');
        if (normalized.Length == 0)
        {
            return false;
        }

        return normalized.StartsWith("docker/", StringComparison.Ordinal)
            || normalized.StartsWith("Dockerfile", StringComparison.Ordinal)
            || normalized.Contains("/Dockerfile", StringComparison.Ordinal)
            || normalized.EndsWith(".csproj", StringComparison.Ordinal)
            || (normalized.StartsWith("apps/", StringComparison.Ordinal)
                && normalized["apps/".Length..].Contains('/', StringComparison.Ordinal)
                && normalized.EndsWith(".cs", StringComparison.Ordinal))
            || (normalized.StartsWith("packages/", StringComparison.Ordinal)
                && normalized["packages/".Length..].Contains('/', StringComparison.Ordinal)
                && normalized.EndsWith(".cs", StringComparison.Ordinal))
            || normalized is ".dockerignore"
                or ".github/dependabot.yml"
                or ".github/workflows/ci.yml"
                or "Cadence.sln"
                or "global.json"
                or ".editorconfig"
                or "BannedSymbols.txt"
                or "Directory.Build.props"
                or "Directory.Build.targets"
                or "Directory.Packages.props"
                or "NuGet.Config";
    }

    /// <summary>Local-only execution overrides layered onto a workflow step parsed from <c>ci.yml</c>.</summary>
    public sealed record CiStepOverride
    {
        /// <summary>Explicit local commands when arguments differ from CI (e.g. <see cref="AotRuntimeIdentifier"/> or matrix targets).</summary>
        public IReadOnlyList<CiCommand>? Commands { get; init; }

        /// <summary>In-process handler replacing a shell script or CLI subprocess.</summary>
        public Func<string, LocalCiOptions, int>? InProcess { get; init; }

        /// <summary>Human-readable summary of <see cref="InProcess"/>.</summary>
        public string? InProcessDisplay { get; init; }

        /// <summary>The command text in <c>ci.yml</c> represented by <see cref="InProcess"/>.</summary>
        public string? InProcessCiText { get; init; }

        /// <summary>Earlier step (<c>job/name</c>) that already runs the same verification.</summary>
        public string? CoveredBy { get; init; }

        /// <summary>Host binary required on <c>PATH</c>.</summary>
        public string? RequiredTool { get; init; }

        /// <summary>Remediation hint when <see cref="RequiredTool"/> is missing.</summary>
        public string? ToolHint { get; init; }

        /// <summary>Reason the step does not run locally.</summary>
        public string? NotRunLocally { get; init; }

        /// <summary>Whether the step requires a reachable Docker daemon.</summary>
        public bool NeedsDocker { get; init; }

        /// <summary>Whether the step requires PostgreSQL.</summary>
        public bool NeedsPostgres { get; init; }

        /// <summary>Whether database test execution is scoped to a filtered test rather than the full solution.</summary>
        public bool ScopedDatabaseTest { get; init; }

        /// <summary>Dynamic file list appended to the step's command.</summary>
        public CiFileSource Files { get; init; }

        /// <summary>Message printed when <see cref="Files"/> resolves to zero files.</summary>
        public string? NoFilesMessage { get; init; }

        /// <summary>Relative directories cleaned before running the step.</summary>
        public IReadOnlyList<string> CleanDirectories { get; init; } = [];

        /// <summary>Additional local environment variables merged with literal step <c>env:</c> entries.</summary>
        public IReadOnlyDictionary<string, string>? Environment { get; init; }

        /// <summary>Conditional environment variables applied when an earlier step passed.</summary>
        public IReadOnlyList<CiEnvironmentIfPassed> EnvironmentIfPassed { get; init; } = [];

        /// <summary>Tracked files restored after the step completes.</summary>
        public IReadOnlyList<string> RestoreFiles { get; init; } = [];
    }

    private static IReadOnlyDictionary<string, CiStepOverride> CreateStepOverrides() =>
        new Dictionary<string, CiStepOverride>(StringComparer.Ordinal)
        {
            ["actionlint/Run actionlint"] = new()
            {
                RequiredTool = "actionlint",
                ToolHint = "provision actionlint 1.7.12 into .cadence/bin via './scripts/setup_wizard.sh --minimal'",
            },
            [NativeAotPublish] = new()
            {
                Commands =
                [
                    new CiCommand(
                        "dotnet",
                        ["publish", Cli, "-c", "Release", "-r", AotRuntimeIdentifier, "-o", "./bin"],
                        CiText: $"dotnet publish {Cli} -c Release -r linux-x64 -o ./bin"),
                ],
            },
            ["lint-and-static/Run Hadolint on Dockerfiles"] = new()
            {
                RequiredTool = "hadolint",
                ToolHint = "provision hadolint 2.15.1 into .cadence/bin via './scripts/setup_wizard.sh --minimal'",
                Files = CiFileSource.Dockerfiles,
            },
            ["lint-and-static/Run ShellCheck on shell scripts"] = new()
            {
                RequiredTool = "shellcheck",
                ToolHint = $"provision shellcheck {CliLinterGateService.ShellcheckVersion} into .cadence/bin via './scripts/setup_wizard.sh --minimal' (or 'pwsh scripts/setup_wizard.ps1 -Minimal', or pass --allow-missing-tools to skip)",
                Files = CiFileSource.ShellScripts,
            },
            ["lint-and-static/Run installer checksum tests"] = new()
            {
                RequiredTool = "bash",
                ToolHint = "install bash (Git for Windows includes it)",
            },
            ["lint-and-static/Run Markdown validation"] = new() { EnvironmentIfPassed = NativeAotCacheHit },
            ["lint-and-static/Validate web API clients against OpenAPI types"] = new() { EnvironmentIfPassed = NativeAotCacheHit },
            ["docs-build/Install Node dependencies"] = new() { CoveredBy = StyleNodeInstall },
            ["docs-build/Build UI package"] = new() { CoveredBy = StyleFrontendBuild },
            ["docs-build/Verify public bundles"] = new() { EnvironmentIfPassed = NativeAotCacheHit },
            ["security/Run static analysis"] = new()
            {
                CoveredBy = QualitySentinels,
                EnvironmentIfPassed = NativeAotCacheHit,
            },
            ["security/Run dependency audit"] = new() { EnvironmentIfPassed = NativeAotCacheHit },
            ["security/Run secret detection"] = new()
            {
                CoveredBy = QualitySentinels,
                EnvironmentIfPassed = NativeAotCacheHit,
            },
            ["security/Run detect-secrets against the baseline"] = new()
            {
                RequiredTool = "detect-secrets-hook",
                ToolHint = $"run './scripts/setup_wizard.sh --minimal' or 'python3 -m venv .cadence/venv && .cadence/venv/bin/python -m pip install --upgrade pip detect-secrets=={DetectSecretsGateService.PinnedVersion}'",
                EnvironmentIfPassed = NativeAotCacheHit,
            },
            ["security/Run DEID compliance scan"] = new()
            {
                CoveredBy = QualitySentinels,
                EnvironmentIfPassed = NativeAotCacheHit,
            },
            [$"{ContainerSecurityJobKey}/Validate Docker Compose configuration"] = new()
            {
                RequiredTool = "docker",
                NeedsDocker = true,
                ToolHint = "install Docker and start the Docker daemon (docs/LOCAL_DEV_ENVIRONMENT.md)",
            },
            [$"{ContainerSecurityJobKey}/Build container image for security scan"] = new()
            {
                NeedsDocker = true,
                Commands =
                [
                    .. ContainerScanTargets.Select(t => new CiCommand(
                        "docker",
                        string.IsNullOrEmpty(t.AppName)
                            ? ["build", "-t", t.ImageTag, "-f", t.Dockerfile, t.Context]
                            : ["build", "-t", t.ImageTag, "-f", t.Dockerfile, "--target", "runtime", "--build-arg", $"APP_NAME={t.AppName}", t.Context],
                        CiText: DockerBuildCiText)),
                ],
            },
            [$"{ContainerSecurityJobKey}/Run container vulnerability scan"] = new()
            {
                RequiredTool = "trivy",
                NeedsDocker = true,
                ToolHint = "install Trivy on PATH (for example 'brew install trivy' or your system package manager)",
                Commands =
                [
                    .. ContainerScanTargets.Select(t => new CiCommand(
                        "trivy",
                        ["image", "--exit-code", "1", "--ignore-unfixed", "--pkg-types", "os,library", "--severity", "CRITICAL,HIGH", t.ImageTag],
                        CiText: "trivy image")),
                ],
            },
            [$"{ContainerSecurityJobKey}/Upload Trivy scan results to GitHub Security tab"] = new()
            {
                NotRunLocally = "uploads Trivy SARIF files to GitHub Code Scanning",
            },
            ["unit-tests/Install Node dependencies"] = new() { CoveredBy = StyleNodeInstall },
            ["unit-tests/Build UI package"] = new() { CoveredBy = StyleFrontendBuild },
            ["dotnet-tests/Execute .NET 10 xUnit Test Suites"] = new()
            {
                // The TRX files go where 'gxp sync' writes them, so the compliance job builds the RTM from this run
                // instead of running every test a second time.
                NeedsPostgres = true,
                CleanDirectories = [RtmGeneratorService.DefaultResultsDir],
                Commands =
                [
                    new CiCommand(
                        "dotnet",
                        ["test", "Cadence.sln", "--configuration", "Release", "-nologo", "--no-build", "--results-directory", RtmGeneratorService.DefaultResultsDir, "--logger", "trx;LogFilePrefix=gxp"],
                        CiText: $"dotnet test Cadence.sln --configuration Release --no-build --results-directory {RtmGeneratorService.DefaultResultsDir} --logger \"trx;LogFilePrefix=gxp\""),
                ],
            },
            ["dotnet-tests/Fail the run when a PostgreSQL test did not execute"] = new()
            {
                NeedsPostgres = true,
                RequiredTool = "bash",
                ToolHint = "install bash to run the shell gate self-test; SyncGxPService.VerifyDatabaseEvidence checks TRX and *.database.txt reports",
                InProcess = (repoRoot, options) =>
                {
                    if (options.AllowDbSkip)
                    {
                        return 0;
                    }

                    string results = Path.Combine(repoRoot, RtmGeneratorService.DefaultResultsDir);
                    return SyncGxPService.VerifyDatabaseEvidence(results, RtmGeneratorService.DefaultResultsDir, repoRoot) ? 0 : 1;
                },
                InProcessDisplay = $"verify no PostgreSQL test in {RtmGeneratorService.DefaultResultsDir} was skipped and *.database.txt evidence was recorded",
                InProcessCiText = "bash scripts/check-database-test-results.sh",
            },
            [$"{E2eJob}/Install Node dependencies"] = new() { CoveredBy = StyleNodeInstall },
            [$"{E2eJob}/Install Playwright Browsers"] = new()
            {
                NotRunLocally = "cadence ci does not download browsers; run 'pnpm --filter web exec playwright install chromium' once yourself",
            },
            [$"{E2eJob}/Build UI package"] = new() { CoveredBy = StyleFrontendBuild },
            [$"{E2eJob}/Run Web Application E2E Accessibility Tests"] = new()
            {
                Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["CADENCE_TEST_BROWSER"] = "1" },
            },
            [$"{E2eJob}/Run Web Application E2E Workflow Tests"] = new()
            {
                Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["CADENCE_TEST_BROWSER"] = "1" },
            },
            [$"{E2eJob}/Run Browser-to-Database Correction Integration Test"] = new()
            {
                NeedsPostgres = true,
                ScopedDatabaseTest = true,
                Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["CADENCE_TEST_BROWSER"] = "1" },
                Commands =
                [
                    new CiCommand(
                        "dotnet",
                        [
                            "test", "tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj", "--configuration", "Release",
                            "--filter", "FullyQualifiedName~BrowserCorrectionReachesTheDatabaseWithItsReason",
                            "--results-directory", RtmGeneratorService.DefaultResultsDir, "--logger", "trx;LogFilePrefix=gxp_browser",
                        ],
                        CiText: "dotnet test tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~BrowserCorrectionReachesTheDatabaseWithItsReason --results-directory TestResults/gxp-browser --logger \"trx;LogFilePrefix=gxp_browser\""),
                ],
            },
            ["compliance/Install Node dependencies"] = new() { CoveredBy = StyleNodeInstall },
            ["compliance/Generate Compliance Reports"] = new()
            {
                // A feature branch writes a preview and validates its tags, results, and PostgreSQL evidence; only snapshot
                // mode writes docs/SDLC (ADR-2532). Unknown requirement IDs, failing tagged tests, skipped database tests,
                // and missing *.database.txt files fail unless --allow-db-skip is set outside snapshot mode.
                InProcess = (repoRoot, options) =>
                {
                    bool snapshot = GxpSnapshotPolicy.IsSnapshotMode();
                    string outputDir = snapshot ? "docs/SDLC" : GxpSnapshotPolicy.PreviewDir;
                    string results = Path.Combine(repoRoot, RtmGeneratorService.DefaultResultsDir);
                    bool hasTrx = Directory.Exists(results) && Directory.EnumerateFiles(results, "*.trx", SearchOption.AllDirectories).Any();
                    bool draft = options.AllowDbSkip && !snapshot && !hasTrx;
                    int generated = RtmGeneratorService.GenerateRtm(
                        outputDir: outputDir, draft: draft, validateOnly: false, reportPath: results, repoRoot: repoRoot,
                        honorDraftEnvironment: false, evidence: RtmEvidenceContext.Capture(repoRoot, "pnpm cadence ci", SystemClock.Instance));
                    if (generated != 0)
                    {
                        return generated;
                    }

                    int validated = RtmGeneratorService.GenerateRtm(
                        outputDir: "docs/SDLC", draft: draft, validateOnly: true, reportPath: results, repoRoot: repoRoot, honorDraftEnvironment: false,
                        requireCurrentSnapshot: snapshot, requireRecordedEvidence: true);
                    if (validated != 0)
                    {
                        return validated;
                    }

                    if ((!options.AllowDbSkip || snapshot)
                        && !SyncGxPService.VerifyDatabaseEvidence(results, RtmGeneratorService.DefaultResultsDir, repoRoot, requireBrowserEvidence: snapshot))
                    {
                        return 1;
                    }

                    return 0;
                },
                InProcessDisplay = $"generate the RTM and IQ/OQ/PQ report from {RtmGeneratorService.DefaultResultsDir} into {GxpSnapshotPolicy.PreviewDir} " +
                    $"(docs/SDLC with {GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1) and validate tags, results, and PostgreSQL evidence; nothing is staged",
                InProcessCiText = "node scripts/run-cli.js gxp sync --results-dir TestResults/gxp",
            },
            ["compliance/Check GxP Snapshot Policy"] = new()
            {
                InProcess = (repoRoot, options) => GxpSnapshotGuardService.Check(repoRoot, preCommit: false, baseRef: options.BaseRef),
                InProcessDisplay = $"refuse branch changes to the generated GxP snapshot ({GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1: compare the regenerated documents with it)",
                InProcessCiText = "node scripts/run-cli.js check gxp-snapshot",
            },
        };

    /// <summary>The jobs, in the topological order <c>cadence ci</c> runs them.</summary>
    public static IReadOnlyList<CiJob> Jobs { get; } = BuildJobs(LoadDefaultWorkflowLines());

    /// <summary>
    /// Builds the ordered <see cref="CiJob"/> execution plan directly from <paramref name="workflowLines"/> using
    /// <see cref="WorkflowYamlParser"/> and the compact local-override table.
    /// </summary>
    public static IReadOnlyList<CiJob> BuildJobs(string[] workflowLines)
    {
        var parsedJobs = WorkflowYamlParser.Parse(workflowLines)
            .Where(j => !GitHubOnlyJobs.ContainsKey(j.Key))
            .ToList();
        var overrides = CreateStepOverrides();
        var workflowByKey = parsedJobs.ToDictionary(j => j.Key, StringComparer.Ordinal);
        var builtByKey = new Dictionary<string, CiJob>(StringComparer.Ordinal);

        foreach (var workflowJob in parsedJobs)
        {
            var steps = new List<CiStep>();
            foreach (var workflowStep in workflowJob.Steps)
            {
                string stepKey = $"{workflowJob.Key}/{workflowStep.Name}";
                bool hasOverride = overrides.TryGetValue(stepKey, out CiStepOverride? stepOverride);
                if (workflowStep.Run is null && !hasOverride)
                {
                    continue;
                }

                steps.Add(BuildStep(workflowJob.Key, workflowStep, stepOverride));
            }

            string? onRequestOnly = workflowJob.If is { } jobIf && RulesetService.RequiresPushEvent(jobIf)
                ? $"runs only on push in CI; pass --job {workflowJob.Key} to run locally"
                : null;

            builtByKey[workflowJob.Key] = new CiJob(workflowJob.Key, workflowJob.Name, steps)
            {
                Needs = workflowJob.Needs,
                OnRequestOnly = onRequestOnly,
            };
        }

        // Populate NeedsStep when a job's `if:` checks `needs.<dep>.outputs.<output> == 'success'` and `<dep>` maps that
        // output to a single `${{ steps.<id>.outcome }}` step.
        foreach (var workflowJob in parsedJobs)
        {
            var needsStep = ResolveNeedsSteps(workflowJob, workflowByKey, builtByKey);
            if (needsStep.Count > 0)
            {
                builtByKey[workflowJob.Key] = builtByKey[workflowJob.Key] with { NeedsStep = needsStep };
            }
        }

        var ordered = new List<CiJob>(parsedJobs.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(WorkflowJob job)
        {
            if (!visited.Add(job.Key))
            {
                return;
            }

            foreach (string need in job.Needs)
            {
                if (workflowByKey.TryGetValue(need, out WorkflowJob? neededJob))
                {
                    Visit(neededJob);
                }
            }

            ordered.Add(builtByKey[job.Key]);
        }

        foreach (var job in parsedJobs)
        {
            Visit(job);
        }

        return ordered;
    }

    private static CiStep BuildStep(string jobKey, WorkflowStep workflowStep, CiStepOverride? stepOverride)
    {
        bool always = workflowStep.If?.Contains("always()", StringComparison.Ordinal) is true;
        var mergedEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        if (stepOverride?.Environment is not null)
        {
            foreach (var (k, v) in stepOverride.Environment)
            {
                mergedEnv[k] = v;
            }
        }

        foreach (var (k, v) in workflowStep.Environment)
        {
            if (!v.Contains("${{", StringComparison.Ordinal))
            {
                mergedEnv[k] = v;
            }
        }

        string rawRun = workflowStep.Run ?? "";
        bool inProcessForwardsBaseRef = stepOverride?.InProcess is not null
            && stepOverride.InProcessCiText is not null
            && DoesInProcessCommandForwardBaseRef(rawRun, stepOverride.InProcessCiText);

        IReadOnlyList<CiCommand> commands;
        if (stepOverride?.NotRunLocally is not null)
        {
            commands = [];
        }
        else if (stepOverride?.Commands is not null)
        {
            commands = stepOverride.Commands;
        }
        else
        {
            commands = ParseCommandsFromRun(rawRun, stepOverride?.InProcessCiText);
        }

        return new CiStep(jobKey, workflowStep.Name)
        {
            Commands = commands,
            InProcess = stepOverride?.InProcess,
            InProcessDisplay = stepOverride?.InProcessDisplay,
            InProcessCiText = stepOverride?.InProcessCiText,
            InProcessForwardsBaseRef = inProcessForwardsBaseRef,
            Always = always,
            CoveredBy = stepOverride?.CoveredBy,
            RequiredTool = stepOverride?.RequiredTool,
            ToolHint = stepOverride?.ToolHint,
            NotRunLocally = stepOverride?.NotRunLocally,
            NeedsDocker = stepOverride?.NeedsDocker ?? false,
            NeedsPostgres = stepOverride?.NeedsPostgres ?? false,
            ScopedDatabaseTest = stepOverride?.ScopedDatabaseTest ?? false,
            Files = stepOverride?.Files ?? CiFileSource.None,
            NoFilesMessage = stepOverride?.NoFilesMessage,
            CleanDirectories = stepOverride?.CleanDirectories ?? [],
            Environment = mergedEnv,
            EnvironmentIfPassed = stepOverride?.EnvironmentIfPassed ?? [],
            RestoreFiles = stepOverride?.RestoreFiles ?? [],
        };
    }

    private static Dictionary<string, string> ResolveNeedsSteps(
        WorkflowJob job,
        IReadOnlyDictionary<string, WorkflowJob> workflowByKey,
        IReadOnlyDictionary<string, CiJob> builtByKey)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(job.If))
        {
            return result;
        }

        foreach (Match match in NeedsOutputConditionRegex().Matches(job.If))
        {
            string depKey = match.Groups[1].Value;
            string outputName = match.Groups[2].Value;
            if (!job.Needs.Contains(depKey, StringComparer.Ordinal)
                || !workflowByKey.TryGetValue(depKey, out WorkflowJob? depWorkflow)
                || !depWorkflow.Outputs.TryGetValue(outputName, out string? outputExpr))
            {
                continue;
            }

            Match singleStepMatch = SingleStepOutcomeRegex().Match(outputExpr.Trim());
            if (!singleStepMatch.Success)
            {
                continue;
            }

            string stepId = singleStepMatch.Groups[1].Value;
            WorkflowStep? targetWorkflowStep = depWorkflow.Steps.FirstOrDefault(s => s.Id == stepId);
            if (targetWorkflowStep is null || !builtByKey.TryGetValue(depKey, out CiJob? depBuilt))
            {
                continue;
            }

            CiStep? targetBuiltStep = depBuilt.Steps.FirstOrDefault(s => s.Name == targetWorkflowStep.Name);
            if (targetBuiltStep is not null)
            {
                result[depKey] = targetBuiltStep.Key;
            }
        }

        return result;
    }

    private static IReadOnlyList<CiCommand> ParseCommandsFromRun(string rawRun, string? ignoreInProcessCiText)
    {
        if (string.IsNullOrWhiteSpace(rawRun))
        {
            return [];
        }

        string? normalizedIgnore = ignoreInProcessCiText is null ? null : NormalizeWhitespace(ignoreInProcessCiText);
        var commands = new List<CiCommand>();
        foreach (string rawLine in rawRun.Split('\n'))
        {
            string commandHead = ShellControlRegex().Split(NormalizeWhitespace(rawLine))[0];
            if (!IsCheckedWorkflowCommand(commandHead))
            {
                continue;
            }

            if (normalizedIgnore is not null && MatchesPrefixOrExact(commandHead, normalizedIgnore))
            {
                continue;
            }

            bool forwardsBaseRef = CommandLineForwardsBaseRef(commandHead, rawRun);
            var tokens = TokenizeShellCommand(commandHead);
            if (tokens.Count == 0)
            {
                continue;
            }

            while (tokens.Count > 1 && IsShellVariableToken(tokens[^1].Raw))
            {
                tokens.RemoveAt(tokens.Count - 1);
            }

            string fileName = tokens[0].Value;
            string[] arguments = [.. tokens.Skip(1).Select(t => t.Value)];
            string rawPrefix = string.Join(' ', tokens.Select(t => t.Raw));
            var command = new CiCommand(fileName, arguments, ForwardsBaseRef: forwardsBaseRef);
            if (command.Display != rawPrefix)
            {
                command = command with { CiText = rawPrefix };
            }

            commands.Add(command);
        }

        return commands;
    }

    private static bool DoesInProcessCommandForwardBaseRef(string rawRun, string inProcessCiText)
    {
        string normalizedTarget = NormalizeWhitespace(inProcessCiText);
        foreach (string rawLine in rawRun.Split('\n'))
        {
            string commandHead = ShellControlRegex().Split(NormalizeWhitespace(rawLine))[0];
            if (IsCheckedWorkflowCommand(commandHead)
                && MatchesPrefixOrExact(commandHead, normalizedTarget)
                && CommandLineForwardsBaseRef(commandHead, rawRun))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPrefixOrExact(string commandHead, string target) =>
        commandHead == target
        || (commandHead.StartsWith(target + " ", StringComparison.Ordinal)
            && commandHead[(target.Length + 1)..].Split(' ').All(IsShellVariableToken));

    private static bool CommandLineForwardsBaseRef(string commandLine, string stepRun) =>
        commandLine.Contains("--base ", StringComparison.Ordinal)
        || ((commandLine.Contains("\"${SCOPE_ARGS[@]}\"", StringComparison.Ordinal)
                || commandLine.Contains("${SCOPE_ARGS[@]}", StringComparison.Ordinal))
            && stepRun.Contains("--base ", StringComparison.Ordinal));

    private static bool IsShellVariableToken(string rawToken) =>
        rawToken.StartsWith('$') || rawToken.StartsWith("\"$", StringComparison.Ordinal);

    private static bool IsCheckedWorkflowCommand(string line) =>
        line == "actionlint"
        || line.StartsWith("actionlint ", StringComparison.Ordinal)
        || line == "hadolint"
        || line.StartsWith("hadolint ", StringComparison.Ordinal)
        || line == "shellcheck"
        || line.StartsWith("shellcheck ", StringComparison.Ordinal)
        || line.StartsWith("docker ", StringComparison.Ordinal)
        || line == "trivy"
        || line.StartsWith("trivy ", StringComparison.Ordinal)
        || line.StartsWith("dotnet ", StringComparison.Ordinal)
        || line.StartsWith("pnpm ", StringComparison.Ordinal)
        || line.StartsWith("node ", StringComparison.Ordinal)
        || line.StartsWith("bash ", StringComparison.Ordinal)
        || (line.StartsWith("python3 ", StringComparison.Ordinal)
            && !line.StartsWith("python3 -m venv ", StringComparison.Ordinal)
            && !line.StartsWith("python3 -m pip ", StringComparison.Ordinal))
        || line.StartsWith("git diff --exit-code", StringComparison.Ordinal);

    private static List<(string Value, string Raw)> TokenizeShellCommand(string commandLine)
    {
        var tokens = new List<(string Value, string Raw)>();
        int i = 0;
        while (i < commandLine.Length)
        {
            while (i < commandLine.Length && char.IsWhiteSpace(commandLine[i]))
            {
                i++;
            }

            if (i >= commandLine.Length)
            {
                break;
            }

            int start = i;
            var value = new StringBuilder();
            bool inSingle = false;
            bool inDouble = false;
            while (i < commandLine.Length)
            {
                char c = commandLine[i];
                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    i++;
                }
                else if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    i++;
                }
                else if (c == '\\' && inDouble && i + 1 < commandLine.Length && commandLine[i + 1] is '"' or '\\')
                {
                    value.Append(commandLine[i + 1]);
                    i += 2;
                }
                else if (char.IsWhiteSpace(c) && !inSingle && !inDouble)
                {
                    break;
                }
                else
                {
                    value.Append(c);
                    i++;
                }
            }

            tokens.Add((value.ToString(), commandLine[start..i]));
        }

        return tokens;
    }

    private static string NormalizeWhitespace(string text) => WhitespaceRegex().Replace(text, " ").Trim();

    private static string[] LoadDefaultWorkflowLines()
    {
        string? path = TryFindWorkflowFile(Directory.GetCurrentDirectory())
            ?? TryFindWorkflowFile(AppContext.BaseDirectory);
        if (path is null)
        {
            throw new FileNotFoundException("Could not locate .github/workflows/ci.yml above the current directory or CLI binary.");
        }

        return File.ReadAllLines(path);
    }

    private static string? TryFindWorkflowFile(string? startDirectory)
    {
        string? dir = startDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, ".github", "workflows", "ci.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    [GeneratedRegex(@" (\|\||&&|\||2>|>)")]
    private static partial Regex ShellControlRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"needs\.([A-Za-z0-9_-]+)\.outputs\.([A-Za-z0-9_-]+)\s*==\s*'success'")]
    private static partial Regex NeedsOutputConditionRegex();

    [GeneratedRegex(@"^\$\{\{\s*steps\.([A-Za-z0-9_-]+)\.outcome\s*\}\}$")]
    private static partial Regex SingleStepOutcomeRegex();

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
            wanted.UnionWith(job.Needs.Where(need => !GitHubOnlyJobs.ContainsKey(need)));
        }

        return [.. Jobs.Where(j => wanted.Contains(j.Key))];
    }
}
