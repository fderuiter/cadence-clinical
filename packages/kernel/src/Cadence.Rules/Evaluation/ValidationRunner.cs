using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Evaluation;

/// <summary>
/// What to validate: a study's records at one stage (spec §10.2). The runner evaluates every rule pack the study pinned
/// in its standards profile and records the result as one validation run.
/// </summary>
/// <param name="Tenant">The tenant.</param>
/// <param name="Study">The study whose records these are.</param>
/// <param name="Stage">The lifecycle stage evaluated.</param>
/// <param name="AsOf">The date the rules' effective periods are evaluated at; evaluation never reads the clock.</param>
/// <param name="Records">The records to check.</param>
/// <param name="IdempotencyKey">The idempotency key of the <see cref="RecordValidationRun"/> command.</param>
/// <param name="ReasonForChange">Why the run is recorded.</param>
public sealed record ValidationRequest(
    TenantId Tenant,
    StudyId Study,
    ValidationStage Stage,
    DateOnly AsOf,
    IReadOnlyList<RuleRecord> Records,
    string IdempotencyKey,
    string ReasonForChange)
{
    /// <summary>The standard checked (for example <c>SDTMIG</c>); rules naming another standard do not apply.</summary>
    public string? Standard { get; init; }

    /// <summary>The standard version checked.</summary>
    public string? StandardVersion { get; init; }

    /// <summary>The agency the data is prepared for.</summary>
    public string? Agency { get; init; }

    /// <summary>The versioned specifications the records were produced under, recorded as the run's lineage inputs.</summary>
    public IReadOnlyList<NodeRef> Inputs { get; init; } = [];

    /// <summary>
    /// The datasets the run covers. Null covers the datasets of <see cref="Records"/>; otherwise only records of these
    /// datasets are evaluated. Name a dataset with no records left to resolve the findings it had.
    /// </summary>
    public IReadOnlyList<string>? Datasets { get; init; }

    /// <summary>
    /// What a record key identifies: a dataset row by default, or, when the records are kernel objects, the object's
    /// NodeRef without a version (<see cref="FindingTargetKind.Observation"/>, <see cref="FindingTargetKind.DesignElement"/>).
    /// </summary>
    public FindingTargetKind TargetKind { get; init; } = FindingTargetKind.DatasetRecord;
}

/// <summary>
/// Validates a study's records with its pinned rule packs and records the run (spec §10.3): loads the released packs
/// the study's standards profile pins (<see cref="RulePackLoader"/>), evaluates them with <see cref="IRuleEvaluator"/>,
/// and sends one <see cref="RecordValidationRun"/> whose detections are the violations. The kernel then derives the
/// findings: a problem detected again keeps its finding, and one no longer detected is resolved by the run (K-FIND-1).
/// </summary>
public sealed class ValidationRunner(RulePackLoader loader, IRuleEvaluator evaluator, IKernelCommandSender? commands = null, IClock? clock = null)
{
    /// <summary>Runs validation and records the run.</summary>
    public async Task<CommandResult<ValidationRunOutcome>> RunAsync(ValidationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Records);

        var packs = await loader.LoadForStudyAsync(request.Tenant, request.Study, cancellationToken).ConfigureAwait(false);
        if (packs.Count == 0)
        {
            return RuleErrors.NoRulePacks;
        }

        var datasets = request.Datasets ?? [.. request.Records.Select(r => r.Dataset).Distinct(StringComparer.Ordinal)];
        var covered = request.Datasets is null ? null : datasets.ToHashSet(StringComparer.Ordinal);
        var effectiveClock = clock ?? SystemClock.Instance;
        var started = effectiveClock.UtcNow;
        var context = new RuleEvaluationContext(request.Stage, request.AsOf)
        {
            Standard = request.Standard,
            StandardVersion = request.StandardVersion,
            Agency = request.Agency,
        };
        var detections = new List<ValidationDetection>();
        foreach (var pack in packs)
        {
            var sink = new ValidationDetectionSink(pack.Key, request.TargetKind, detections);
            evaluator.Evaluate(pack.Pack, context, new RuleRecordListCursor(request.Records, covered), sink);
        }

        var completed = effectiveClock.UtcNow;
        if (commands is null)
        {
            throw new InvalidOperationException("IKernelCommandSender is not registered.");
        }

        return await commands.SendAsync(
            new RecordValidationRun(
                request.Tenant,
                ValidationRunId.New(started),
                request.Study,
                request.Stage,
                [.. packs.Select(p => p.Id)],
                request.AsOf,
                request.Standard,
                request.StandardVersion,
                request.Agency,
                request.Inputs,
                datasets,
                started,
                completed,
                detections,
                request.IdempotencyKey,
                request.ReasonForChange),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The detection a violation records. Observed values longer than a detection holds are shortened.</summary>
    public static ValidationDetection Detection(string packKey, FindingTargetKind targetKind, RuleViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        return new ValidationDetection(
            packKey,
            violation.RuleId,
            violation.RuleVersion,
            violation.Severity,
            new FindingTarget(targetKind, violation.Dataset, violation.RecordKey),
            violation.Explanation,
            Expected: null,
            [.. violation.Observed.Select(o => new ObservedVariable(o.Variable, o.Value.IsNull ? null : Shorten(o.Value.ToString())))]);
    }

    /// <summary>The detection a streamed violation hit records. Observed values longer than a detection holds are shortened.</summary>
    public static ValidationDetection Detection(string packKey, FindingTargetKind targetKind, RuleViolationHit violation)
    {
        ObservedVariable[] observed;
        if (violation.Observed.Length == 0)
        {
            observed = [];
        }
        else
        {
            observed = new ObservedVariable[violation.Observed.Length];
            for (var i = 0; i < violation.Observed.Length; i++)
            {
                var item = violation.Observed[i];
                observed[i] = new ObservedVariable(item.Variable, item.Value.IsNull ? null : Shorten(item.Value.ToString()));
            }
        }

        return new ValidationDetection(
            packKey,
            violation.RuleId,
            violation.RuleVersion,
            violation.Severity,
            new FindingTarget(targetKind, violation.Dataset, violation.RecordKey),
            violation.Explanation,
            Expected: null,
            observed);
    }

    private static string Shorten(string value) =>
        value.Length <= ValidationDetection.MaxTextLength ? value : string.Concat(value.AsSpan(0, ValidationDetection.MaxTextLength - 1), "…");

    private sealed class ValidationDetectionSink(
        string packKey,
        FindingTargetKind targetKind,
        List<ValidationDetection> detections) : IRuleViolationSink
    {
        public void OnViolation(RuleViolationHit violation) =>
            detections.Add(Detection(packKey, targetKind, violation));
    }
}
