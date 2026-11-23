using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Cli.Services.Gates;
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
/// The registry of quality gates, composed statically with zero reflection from the slice classes under
/// <c>Cadence.Cli.Services.Gates</c> for Native AOT compatibility. <c>cadence check</c> runs them in this order,
/// <c>cadence check &lt;gate&gt;</c> looks a gate up here, and the gate count, the <c>check --help</c> text and the
/// gate tables in <c>docs/cli.md</c> and <c>docs/CI.md</c> (<see cref="GateDocs"/>) are generated from it. Each gate
/// must be able to fail: <c>QualityGateRegistryTests</c> runs every gate against a fixture that breaks it.
/// </summary>
public static class QualityGates
{
    /// <summary>Every gate, in the order <c>cadence check</c> runs them.</summary>
    public static IReadOnlyList<QualityGate> All { get; } =
    [
        .. FormattingGates.Entries,
        .. HygieneAndSecretsGates.Entries,
        .. DocsAndGovernanceGates.Entries,
        .. ContractAndCodeGates.Entries,
        .. ReleaseAndTestSuiteGates.Entries,
    ];

    /// <summary>Finds a gate by its short name or an alias; case is ignored and underscores count as hyphens.</summary>
    public static QualityGate? Find(string name)
    {
        string normalized = Normalize(name);
        return All.FirstOrDefault(g => g.AllNames.Any(n => n == normalized));
    }

    /// <summary>The short names, comma-separated, for help text.</summary>
    public static string NameList => string.Join(", ", All.Select(g => g.Name));

    private static string Normalize(string name) => name.Trim().ToLowerInvariant().Replace('_', '-');
}
