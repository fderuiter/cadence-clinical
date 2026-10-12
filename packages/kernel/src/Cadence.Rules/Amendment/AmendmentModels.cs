using System.Collections.Generic;

using Cadence.Kernel.Validation;
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;

namespace Cadence.Rules.Amendment;

/// <summary>
/// Differences identified between two rule pack versions during protocol amendment testing.
/// </summary>
public sealed record RuleDiffSummary(
    IReadOnlyList<Rule> AddedRules,
    IReadOnlyList<Rule> RemovedRules,
    IReadOnlyList<Rule> ModifiedRules);

/// <summary>
/// A broken AST reference identified in a rule expression.
/// </summary>
public sealed record BrokenAstReference(
    string RuleId,
    int RuleVersion,
    string ReferencedVariable,
    string Detail);

/// <summary>
/// Aggregated diagnostic report produced by protocol amendment regression diff testing.
/// </summary>
public sealed record AmendmentRegressionReport(
    string BaselineVersion,
    string TargetVersion,
    RuleDiffSummary RuleDiffs,
    IReadOnlyList<BrokenAstReference> BrokenAstReferences,
    IReadOnlyList<ValidationDetection> NewViolations,
    IReadOnlyList<ValidationDetection> ResolvedFindings,
    ValidationTimingMetrics Timing);
