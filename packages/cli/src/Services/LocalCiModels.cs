using System;
using System.Collections.Generic;

namespace Cadence.Cli.Services;

/// <summary>What happened to one step of a local CI run.</summary>
public enum CiOutcome
{
    /// <summary>Every command exited 0.</summary>
    Passed,

    /// <summary>A command exited non-zero, or the step could not start (for example, no PostgreSQL).</summary>
    Failed,

    /// <summary>The step was not run: <c>--allow-missing-tools</c> skipped a missing tool, or the step only runs on GitHub.</summary>
    Skipped,

    /// <summary>Another step that runs the same check passed earlier in this run.</summary>
    Covered,

    /// <summary>An earlier step of the job, or a job it needs, failed, and CI would not run it either.</summary>
    NotRun,
}

/// <summary>The result of one step.</summary>
/// <param name="Step">The step.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Duration">How long it ran.</param>
/// <param name="Note">Why it was skipped or failed, or what it covered.</param>
public sealed record CiStepResult(CiStep Step, CiOutcome Outcome, TimeSpan Duration, string Note);

/// <summary>Whether the .NET tests can reach PostgreSQL.</summary>
/// <param name="Available">True when <c>CADENCE_TEST_POSTGRES</c> connects or <c>CADENCE_TEST_DOCKER=1</c> is set.</param>
/// <param name="Message">What was found, for the summary.</param>
public sealed record CiPostgresStatus(bool Available, string Message);

/// <summary>Whether container build and scan steps can reach a running Docker daemon.</summary>
/// <param name="Available">True when <c>docker info</c> succeeds.</param>
/// <param name="Message">What was found, for the summary.</param>
public sealed record CiDockerStatus(bool Available, string Message);

/// <summary>Whether <c>container-security</c> runs on a default <c>cadence ci</c> invocation.</summary>
/// <param name="ShouldRun">True when container/dependency inputs changed or the scope fell back to all tracked files.</param>
/// <param name="Note">Why <c>container-security</c> was skipped when <paramref name="ShouldRun"/> is false.</param>
public sealed record CiContainerScanScope(bool ShouldRun, string Note);

/// <summary>The choices given on the command line.</summary>
/// <param name="AllowMissingTools">Skip steps whose required host tool binary or Docker daemon is unavailable.</param>
/// <param name="AllowDbSkip">Allow .NET test and compliance steps to run without PostgreSQL or accept skipped database tests and missing <c>*.database.txt</c> evidence.</param>
/// <param name="BaseRef">The ref that "changed files" and merged worktree synthesis are compared with, as <c>origin/main</c> is in CI.</param>
/// <param name="Offline">Skip <c>git fetch</c> before synthesizing the merged worktree (explicit air-gapped debugging only).</param>
/// <param name="RequestedJobs">Job keys explicitly passed with <c>--job</c> (empty or null on a full default run).</param>
/// <param name="IncludeUncommitted">Stage uncommitted working-tree and index edits in a temporary index copy and synthesize a snapshot commit on <c>HEAD</c>.</param>
public sealed record LocalCiOptions(
    bool AllowMissingTools,
    bool AllowDbSkip,
    string BaseRef,
    bool Offline = false,
    IReadOnlyCollection<string>? RequestedJobs = null,
    bool IncludeUncommitted = false)
{
    public LocalCiOptions(
        bool AllowSkip,
        string BaseRef,
        bool Offline = false,
        IReadOnlyCollection<string>? RequestedJobs = null,
        bool IncludeUncommitted = false)
        : this(
            AllowMissingTools: AllowSkip,
            AllowDbSkip: AllowSkip,
            BaseRef: BaseRef,
            Offline: Offline,
            RequestedJobs: RequestedJobs,
            IncludeUncommitted: IncludeUncommitted)
    {
    }
}

/// <summary>Result of preparing a disposable merged worktree before running local CI jobs.</summary>
/// <param name="Succeeded">Whether remote fetch (when online), merge-tree synthesis, and worktree creation succeeded.</param>
/// <param name="Environment">Worktree-scoped <see cref="LocalCiEnvironment"/> when <paramref name="Succeeded"/> is <c>true</c>.</param>
/// <param name="Lease">Disposable lease that removes and prunes the temporary worktree after the run finishes.</param>
/// <param name="Message">Diagnostic summary or failure details (including dirty or conflicted file paths).</param>
/// <param name="EffectiveBaseRef">Resolved remote-tracking ref, tag ref, or commit SHA propagated into downstream <c>--base</c> scopes.</param>
public sealed record CiWorkspacePreparation(
    bool Succeeded,
    LocalCiEnvironment? Environment,
    IDisposable? Lease,
    string Message,
    string? EffectiveBaseRef = null);
