using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Conformance;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Application.Standards;

/// <summary>
/// K-STD-2 (spec §6.3, §10.2, ADR-2464): a draft protocol version whose study pins rule packs is submitted only when a
/// design-stage validation run evaluated this draft, as it was last saved, over both design fact datasets, with every
/// rule pack and mapping specification version the study pins now, and none of that run's Error findings blocks. A run
/// names the draft's lineage node among its inputs, and that node's version changes whenever the draft is saved, so a
/// run of an earlier draft never counts. Only the run's own detections are read: a run of another protocol version
/// cannot clear or raise a block on this one. An Error finding the run detected blocks until the design is fixed and
/// re-run, or the finding is accepted or explained with an attestation (K-FIND-2); a later run of another version
/// resolving it does not. Warnings and Notices never block. A study that pins no rule pack has nothing to evaluate and
/// submits as before.
/// </summary>
internal sealed class DesignConformanceCheck(IStandardsRegistry registry, IValidationFindings findings) : IProtocolSubmissionCheck
{
    /// <summary>The most blocking findings one refusal lists.</summary>
    private const int MaxListed = 10;

    /// <summary>
    /// The statuses of a finding no person has decided on. Resolved counts: the run read this draft, so a finding it
    /// detected can only have been resolved by a run of another protocol version.
    /// </summary>
    private static readonly FindingStatus[] Undecided = [FindingStatus.Open, FindingStatus.Investigating, FindingStatus.Resolved];

    public async Task<KernelError?> CheckAsync(ProtocolSubmission submission, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(context);
        if (await registry.FindStudyStandardsAsync(context.TenantId, submission.Study, cancellationToken).ConfigureAwait(false) is not { } profile)
        {
            return null;
        }

        var pins = await PinnedMappings.LoadAsync(registry, context.TenantId, profile.LibraryPins, cancellationToken).ConfigureAwait(false);
        var packs = pins.Where(p => p.Kind == LibraryItemKind.RulePack).Select(p => p.Id).ToHashSet();
        if (packs.Count == 0)
        {
            return null;
        }

        var run = await findings.FindLatestRunAsync(context.TenantId, submission.Study, ValidationStage.Design, submission.Node, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return NotRun($"No design-stage run names {submission.Node}.");
        }

        var missing = packs.Where(p => !run.RulePacks.Any(r => r.LibraryItemId == p)).Order().ToList();
        if (missing.Count > 0)
        {
            return NotRun($"Run {run.Id} did not evaluate the pinned rule pack(s) {string.Join(", ", missing)}.");
        }

        var uncovered = DesignConformance.Datasets.Where(d => !run.Datasets.Contains(d, StringComparer.Ordinal)).ToList();
        if (uncovered.Count > 0)
        {
            return NotRun($"Run {run.Id} did not cover the design fact dataset(s) {string.Join(", ", uncovered)}.");
        }

        var stale = pins
            .Where(p => p.Kind == LibraryItemKind.MappingSpecification)
            .Where(p => !run.Inputs.Any(i => string.Equals(i.Kind, NodeKinds.LibraryItem, StringComparison.Ordinal) && i.Id == p.Id.Value))
            .Select(p => $"{p.Key} v{p.Version}")
            .Order(StringComparer.Ordinal)
            .ToList();
        if (stale.Count > 0)
        {
            return NotRun($"Run {run.Id} did not use the pinned mapping specification(s) {string.Join(", ", stale)}.");
        }

        var query = new FindingQuery(submission.Study)
        {
            Stage = ValidationStage.Design,
            DetectedBy = run.Id,
            Severity = ValidationSeverity.Error,
            Statuses = Undecided,
            Take = MaxListed,
        };
        var blocking = await findings.ListAsync(context.TenantId, query, cancellationToken).ConfigureAwait(false);
        if (blocking.Total == 0)
        {
            return null;
        }

        var listed = string.Join("; ", blocking.Findings.Select(f => $"{f.PackKey} {f.RuleId} on {f.Target.Dataset} {f.Target.Key}: {f.Message}"));
        var more = blocking.Total > blocking.Findings.Count ? $" and {blocking.Total - blocking.Findings.Count} more" : string.Empty;
        return StandardsErrors.DesignNotConformant.WithMessage(
            $"{StandardsErrors.DesignNotConformant.Message} Blocking: {listed}{more}. Fix the design and run design conformance again, or accept each finding with a Responsibility attestation.");
    }

    private static KernelError NotRun(string detail) =>
        StandardsErrors.ConformanceNotRun.WithMessage($"{StandardsErrors.ConformanceNotRun.Message} {detail}");
}
