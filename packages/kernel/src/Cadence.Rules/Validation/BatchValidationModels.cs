using System;
using System.Collections.Generic;

using Cadence.Kernel;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Validation;

/// <summary>
/// Execution metrics for a batch rule compilation and validation run.
/// </summary>
public sealed record ValidationTimingMetrics(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double DurationMs,
    int RulesEvaluated,
    int RecordsEvaluated,
    int ParallelWorkerCount);

/// <summary>
/// Request parameters for study-wide parallel batch rule validation.
/// </summary>
public sealed record BatchValidationRequest(
    TenantId Tenant,
    StudyId Study,
    ValidationStage Stage,
    DateOnly AsOf,
    IReadOnlyList<RuleRecord> Records,
    IReadOnlyList<RulePack> RulePacks)
{
    /// <summary>Whether this execution is a dry-run that does not mutate live state.</summary>
    public bool IsDryRun { get; init; } = true;

    /// <summary>Threshold record count above which batch validation runs asynchronously.</summary>
    public int AsyncThreshold { get; init; } = 10000;

    /// <summary>The standard checked.</summary>
    public string? Standard { get; init; }

    /// <summary>The standard version checked.</summary>
    public string? StandardVersion { get; init; }

    /// <summary>The target agency.</summary>
    public string? Agency { get; init; }

    /// <summary>Loaded rule packs with canonical Metadata Registry keys for <see cref="ValidationFindingId"/> parity.</summary>
    public IReadOnlyList<LoadedRulePack>? LoadedRulePacks { get; init; }
}

/// <summary>
/// Aggregated diagnostic report returned by parallel batch rule validation.
/// </summary>
public sealed record BatchValidationReport(
    string JobId,
    StudyId Study,
    bool IsAsync,
    string Status,
    ValidationTimingMetrics Timing,
    IReadOnlyList<string> SyntaxErrors,
    IReadOnlyList<string> CircularDependencies,
    IReadOnlyList<string> ConflictingRules,
    IReadOnlyList<ValidationDetection> Detections);
