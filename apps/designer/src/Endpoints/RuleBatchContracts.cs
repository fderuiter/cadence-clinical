using System.Collections.Generic;

using Cadence.Rules.Evaluation;
using Cadence.Rules.Packs;

namespace Cadence.Apps.Designer.Endpoints;

/// <summary>
/// API request parameters for batch rule validation.
/// </summary>
public record BatchValidateApiRequest(
    IReadOnlyList<RuleRecord>? Records = null,
    IReadOnlyList<RulePack>? RulePacks = null,
    bool? IsDryRun = null,
    int? AsyncThreshold = null);

/// <summary>
/// API request parameters for protocol amendment regression testing.
/// </summary>
public record AmendmentRegressionApiRequest(
    string? BaselineVersion = null,
    string? TargetVersion = null,
    RulePack? BaselinePack = null,
    RulePack? TargetPack = null,
    IReadOnlyList<RuleRecord>? Records = null);
