using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;

namespace Cadence.Kernel.Validation;

/// <summary>
/// Reads validation runs and findings (spec §10.3). Findings are derived on read from the study's runs and the
/// decisions people made, so this read model holds no state of its own and is disposable by construction (K-MOD-2).
/// Every read is scoped to the tenant by Row-Level Security.
/// </summary>
public interface IValidationFindings
{
    /// <summary>One finding with its derived status, or null when no run has detected it.</summary>
    Task<ValidationFindingView?> FindAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken);

    /// <summary>The findings of a study that match <paramref name="query"/>, ordered by severity, stage, rule and target.</summary>
    Task<FindingPage> ListAsync(TenantId tenant, FindingQuery query, CancellationToken cancellationToken);

    /// <summary>One run, or null when it does not exist.</summary>
    Task<ValidationRunSummary?> FindRunAsync(TenantId tenant, ValidationRunId run, CancellationToken cancellationToken);

    /// <summary>Distinct datasets of findings recorded in <paramref name="study"/>, optionally narrowed to <paramref name="stage"/>.</summary>
    Task<IReadOnlyList<string>> ListDatasetsAsync(
        TenantId tenant, StudyId study, ValidationStage? stage = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Rebuilds the disposable <c>kernel.validation_finding_projections</c> read model deterministically from immutable
/// validation runs, detections, and finding decisions (ADR-2462 §5).
/// </summary>
public interface IValidationFindingProjector
{
    /// <summary>Rebuilds projected finding states for <paramref name="study"/> in <paramref name="tenant"/>, returning the projected row count.</summary>
    Task<int> RebuildStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken = default);

    /// <summary>Rebuilds projected finding states for every study in <paramref name="tenant"/>, returning the projected row count.</summary>
    Task<int> RebuildTenantAsync(TenantId tenant, CancellationToken cancellationToken = default);
}

/// <summary>
/// A filter over a study's findings. Null criteria match everything; <see cref="Statuses"/> matches any of its
/// statuses. <see cref="Take"/> is 1 to <see cref="MaxTake"/>.
/// </summary>
public sealed record FindingQuery(StudyId Study)
{
    /// <summary>The most findings one page returns.</summary>
    public const int MaxTake = 500;

    /// <summary>Only findings at this stage.</summary>
    public ValidationStage? Stage { get; init; }

    /// <summary>Only findings whose latest detection has this severity.</summary>
    public ValidationSeverity? Severity { get; init; }

    /// <summary>Only findings with one of these statuses.</summary>
    public IReadOnlyCollection<FindingStatus>? Statuses { get; init; }

    /// <summary>Only findings with this classification.</summary>
    public FindingClassification? Classification { get; init; }

    /// <summary>Only findings of this rule pack.</summary>
    public string? PackKey { get; init; }

    /// <summary>Only findings on this dataset.</summary>
    public string? Dataset { get; init; }

    /// <summary>Only findings whose target key is in this set.</summary>
    public IReadOnlyCollection<string>? TargetKeys { get; init; }

    /// <summary>Only findings that block their stage (<see cref="FindingBlocking.Blocks"/>).</summary>
    public bool BlockingOnly { get; init; }

    /// <summary>How many matching findings to skip.</summary>
    public int Skip { get; init; }

    /// <summary>How many matching findings to return.</summary>
    public int Take { get; init; } = 100;
}

/// <summary>A page of findings and how many match in total.</summary>
public sealed record FindingPage(IReadOnlyList<ValidationFindingView> Findings, int Total);

/// <summary>A run as recorded.</summary>
public sealed record ValidationRunSummary(
    ValidationRunId Id,
    StudyId Study,
    ValidationStage Stage,
    DateOnly AsOf,
    string? Standard,
    string? StandardVersion,
    string? Agency,
    IReadOnlyList<ValidationRunPackView> RulePacks,
    IReadOnlyList<string> Datasets,
    IReadOnlyList<NodeRef> Inputs,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    int Detected);

/// <summary>A rule pack version a run evaluated.</summary>
public sealed record ValidationRunPackView(LibraryItemId LibraryItemId, string Key, int Version, string ContentHash);

/// <summary>
/// A finding (spec §10.3) as of now: what its latest detection found, its derived status and the decision in force.
/// </summary>
/// <param name="Id">The finding.</param>
/// <param name="Study">Its study.</param>
/// <param name="Stage">Its stage.</param>
/// <param name="PackKey">The rule pack that detects it.</param>
/// <param name="RulePack">The rule pack version of its latest detection.</param>
/// <param name="RuleId">The rule.</param>
/// <param name="RuleVersion">The rule version of its latest detection.</param>
/// <param name="Severity">The severity of its latest detection.</param>
/// <param name="Target">What it is about.</param>
/// <param name="Message">What the rule reported.</param>
/// <param name="Expected">What the rule expected, when it said.</param>
/// <param name="Observed">The values the rule read.</param>
/// <param name="Standard">The standard the latest detecting run evaluated against.</param>
/// <param name="StandardVersion">That standard's version.</param>
/// <param name="Agency">The agency the latest detecting run prepared for.</param>
/// <param name="Status">Its derived status.</param>
/// <param name="FirstDetectedRun">The first run that detected it.</param>
/// <param name="LastDetectedRun">The latest run that detected it; decisions are signed against it.</param>
/// <param name="ResolvedBy">For a resolved finding, the run that no longer detected it (the resolution evidence).</param>
/// <param name="Classification">Its classification.</param>
/// <param name="Decision">The decision in force, when <see cref="Status"/> comes from one.</param>
/// <param name="Enrollment">The owning enrollment when the target resolves to one, or null for study-level findings.</param>
/// <param name="BlindedVariables">Variable identifiers on this finding that carry blinded treatment information.</param>
/// <param name="BlindedObservation">Representative blinded observation on this finding's enrollment for visibility probing.</param>
public sealed record ValidationFindingView(
    ValidationFindingId Id,
    StudyId Study,
    ValidationStage Stage,
    string PackKey,
    LibraryItemId RulePack,
    string RuleId,
    int RuleVersion,
    ValidationSeverity Severity,
    FindingTarget Target,
    string Message,
    string? Expected,
    IReadOnlyList<ObservedVariable> Observed,
    string? Standard,
    string? StandardVersion,
    string? Agency,
    FindingStatus Status,
    ValidationRunId FirstDetectedRun,
    ValidationRunId LastDetectedRun,
    ValidationRunId? ResolvedBy,
    FindingClassification? Classification,
    FindingDecision? Decision,
    EnrollmentId? Enrollment = null,
    IReadOnlySet<string>? BlindedVariables = null,
    ObservationId? BlindedObservation = null)
{
    /// <summary>Placeholder substituted for blinded observed values when the reader lacks <c>enrollment.unblind</c>.</summary>
    public const string MaskedValue = "masked";

    /// <summary>True when the finding blocks its stage's gate (<see cref="FindingBlocking.Blocks"/>).</summary>
    public bool Blocks => FindingBlocking.Blocks(Stage, Severity, Status);

    /// <summary>Returns a copy with blinded observed values, messages, and decision rationale redacted (K-AUTH-4).</summary>
    public ValidationFindingView MaskBlinded()
    {
        if (BlindedVariables is null || BlindedVariables.Count == 0)
        {
            return this;
        }

        var maskedMessage = Message;
        foreach (var raw in Observed
                     .Where(o => BlindedVariables.Contains(o.Variable) && !string.IsNullOrWhiteSpace(o.Value))
                     .Select(o => o.Value!))
        {
            maskedMessage = maskedMessage.Replace(raw, MaskedValue, StringComparison.Ordinal);
        }

        IReadOnlyList<ObservedVariable> maskedObserved =
        [
            .. Observed.Select(o => BlindedVariables.Contains(o.Variable) ? o with { Value = MaskedValue } : o),
        ];

        var maskedExpected = Expected is null ? null : MaskedValue;
        FindingDecision? maskedDecision = null;
        if (Decision is not null)
        {
            var maskedDecisionText = Decision.Text is null ? null : MaskedValue;
            maskedDecision = Decision with { Text = maskedDecisionText };
        }

        return this with
        {
            Message = maskedMessage,
            Expected = maskedExpected,
            Observed = maskedObserved,
            Decision = maskedDecision,
        };
    }

    /// <summary>The producer name of this finding's explanation (ADR-2482, K-EXP-3).</summary>
    public string Producer => $"quality.finding/{RuleId}@{RuleVersion}";

    /// <summary>
    /// Why the finding exists and stands where it does (ADR-2482, K-EXP-1), from the same detection and runs that decided
    /// it. The evidence names the rule pack version, the target when it is a kernel object, the runs and the attestation,
    /// so the root cause can be followed through lineage (K-FIND-3).
    /// </summary>
    public Explanation Explain()
    {
        var packRef = NodeRef.For(RulePack);
        var firstRun = NodeRef.For(FirstDetectedRun);
        var lastRun = NodeRef.For(LastDetectedRun);
        var targetEvidence = Target.Node is { } node ? new[] { node } : [];
        var observed = Observed.Count == 0 ? "no values" : string.Join(", ", Observed.Select(o => $"{o.Variable} = {o.Value ?? "null"}"));
        var steps = new List<ExplanationStep>
        {
            new($"Rule {RuleId} v{RuleVersion} of rule pack {PackKey} reported: {Message}", [packRef]),
            new($"It read {observed} on {Target.Dataset} record {Target.Key}{(Target.Variable is null ? string.Empty : $", variable {Target.Variable}")}.", targetEvidence),
            new(FirstDetectedRun == LastDetectedRun
                ? "One run detected it."
                : "Runs have detected it since the first one.", FirstDetectedRun == LastDetectedRun ? [lastRun] : [firstRun, lastRun]),
        };

        var conditions = new List<EvaluatedCondition>
        {
            new("finding.detected", "The latest run that covers the finding still detects it", Status != FindingStatus.Resolved,
                ResolvedBy is { } resolved ? [NodeRef.For(resolved)] : [lastRun]),
        };
        if (Decision is { AttestationId: { } attestation } decision)
        {
            steps.Add(new($"{decision.Status} by {decision.DecidedBy}: {decision.Text}", [NodeRef.For(attestation)]));
        }

        if (Severity == ValidationSeverity.Error && Stage != ValidationStage.Collection)
        {
            conditions.Add(new("finding.dispositioned", "An error finding is accepted or explained with an attestation, or no longer detected",
                !Blocks, Decision?.AttestationId is { } a ? [NodeRef.For(a)] : []));
        }

        var claim = Status switch
        {
            FindingStatus.Resolved => $"Rule {RuleId} no longer finds this problem on {Target.Dataset} {Target.Key}.",
            _ when Blocks => $"Rule {RuleId} finds an error on {Target.Dataset} {Target.Key} that blocks {Stage}.",
            _ => $"Rule {RuleId} finds a {Severity.ToString().ToLowerInvariant()} on {Target.Dataset} {Target.Key}; it is {Status.ToString().ToLowerInvariant()}.",
        };

        var next = new List<NextAction>();
        if (Status is FindingStatus.Open or FindingStatus.Investigating)
        {
            next.Add(new NextAction("Correct the source, mapping or metadata and validate again", Target: Target.Node));
            next.Add(new NextAction("Explain the finding for the reviewer guide", "validation_finding.explain", NodeRef.For(Id)));
            next.Add(new NextAction("Accept the finding as it is", "validation_finding.accept", NodeRef.For(Id)));
        }

        return new Explanation(NodeRef.For(Id), claim, Producer, steps, conditions, next);
    }
}

/// <summary>A decision in force on a finding.</summary>
/// <param name="Status">Investigating, Accepted, Explained, or Open after a reopen.</param>
/// <param name="DecidedBy">Who decided (the resolution authority).</param>
/// <param name="DecidedAt">When.</param>
/// <param name="Text">The acceptance rationale or explanation.</param>
/// <param name="DecidedOnRun">The run an acceptance or explanation was made against.</param>
/// <param name="AttestationId">The attestation behind an acceptance or explanation (the resolution evidence).</param>
public sealed record FindingDecision(
    FindingStatus Status, string DecidedBy, DateTimeOffset DecidedAt, string? Text, ValidationRunId? DecidedOnRun, AttestationId? AttestationId);
