using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Evaluates readiness gates for form signatures (<see cref="SignCollectionInstance"/>) against canonical kernel
/// evidence, assertions, and validation findings.
/// </summary>
internal static class EvidenceReadiness
{
    public static async Task<KernelError?> CheckCollectionSignableAsync(
        TenantId tenant,
        CollectionInstance instance,
        IReadOnlyList<Observation> memberObservations,
        IKernelUnitOfWork work,
        IValidationFindings? findings,
        CancellationToken cancellationToken)
    {
        var version = await work.ReadAsync<ProtocolVersion>(instance.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        var spec = version is null ? null : CollectionSpecs.Of(version, instance.CollectionSpecId);
        var observationsById = memberObservations.ToDictionary(o => o.Id);

        var recordedItems = instance.Members
            .Where(m => observationsById.TryGetValue(m.ObservationId, out var obs)
                && obs.Value is not MissingValue { Reason: NullFlavor.NotAsked })
            .Select(m => m.ItemId)
            .ToHashSet(StringComparer.Ordinal);
        var missingRequired = spec is null
            ? []
            : spec.Items
                .Where(i => i.Required && !recordedItems.Contains(i.Id))
                .Select(i => i.Id)
                .ToList();

        var memberTargetIds = instance.Members.Select(m => m.ObservationId.Value).ToHashSet();
        var (openQueryCount, answeredQueryCount) = await CountActiveQueriesAsync(
            work,
            instance.StudyId,
            q => q.TargetKind == NodeKinds.Observation && memberTargetIds.Contains(q.TargetId),
            cancellationToken).ConfigureAwait(false);

        var targetKeys = new List<string>(memberTargetIds.Count + 1)
        {
            instance.Id.Value.ToString("D"),
        };
        foreach (var obsGuid in memberTargetIds)
        {
            targetKeys.Add(NodeRef.For(ObservationId.FromGuid(obsGuid)).ToString());
        }

        var activeErrorFindings = await LoadActiveErrorFindingsAsync(
                tenant,
                instance.StudyId,
                targetKeys,
                findings,
                cancellationToken)
            .ConfigureAwait(false);
        var formErrorFindings = activeErrorFindings
            .Where(f => TargetsCollectionOrMembers(f.Target, instance.Id.Value, memberTargetIds))
            .ToList();

        if (CollectionReadiness.IsComplete(missingRequired.Count, openQueryCount, answeredQueryCount, formErrorFindings.Count))
        {
            return null;
        }

        var blockers = CollectionReadiness.BuildBlockers(
            missingRequired,
            missingRequired.Count,
            openQueryCount,
            answeredQueryCount,
            formErrorFindings.Count);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (missingRequired.Count > 0)
        {
            errors["missing_required"] = [.. missingRequired];
        }

        if (openQueryCount + answeredQueryCount > 0)
        {
            errors["active_queries"] = [$"open={openQueryCount}, answered={answeredQueryCount}"];
        }

        if (formErrorFindings.Count > 0)
        {
            errors["error_findings"] = [.. formErrorFindings.Select(f => $"{f.PackKey}/{f.RuleId}: {f.Message}")];
        }

        return CollectionErrors.NotReadyToSign.WithMessage(string.Join(" ", blockers)) with { Errors = errors };
    }

    private static async Task<(int OpenCount, int AnsweredCount)> CountActiveQueriesAsync(
        IKernelUnitOfWork work,
        StudyId studyId,
        Func<Assertion, bool> filter,
        CancellationToken cancellationToken)
    {
        var studyQueries = await work.ReadAllAsync<Assertion>(
            a => a.StudyId == studyId && a.Kind == AssertionKind.Queried && a.RetractsId == null,
            cancellationToken).ConfigureAwait(false);
        var matchingQueries = studyQueries.Where(filter).ToList();
        if (matchingQueries.Count == 0)
        {
            return (0, 0);
        }

        var retractedQueries = await LocksInForce.RetractedAsync(
            work,
            [.. matchingQueries.Select(q => q.Id)],
            cancellationToken).ConfigureAwait(false);
        var activeQueryIds = matchingQueries
            .Where(q => !retractedQueries.Contains(q.Id))
            .Select(q => q.Id.Value)
            .ToList();
        if (activeQueryIds.Count == 0)
        {
            return (0, 0);
        }

        var answers = await work.ReadAllAsync<Assertion>(
            a => a.StudyId == studyId
                && a.Kind == AssertionKind.QueryAnswered
                && a.RetractsId == null
                && activeQueryIds.Contains(a.TargetId),
            cancellationToken).ConfigureAwait(false);
        if (answers.Count == 0)
        {
            return (activeQueryIds.Count, 0);
        }

        var retractedAnswers = await LocksInForce.RetractedAsync(
            work,
            [.. answers.Select(a => a.Id)],
            cancellationToken).ConfigureAwait(false);
        var answeredQueryGuids = answers
            .Where(a => !retractedAnswers.Contains(a.Id))
            .Select(a => a.TargetId)
            .ToHashSet();
        var answeredCount = activeQueryIds.Count(answeredQueryGuids.Contains);
        return (activeQueryIds.Count - answeredCount, answeredCount);
    }

    private static async Task<IReadOnlyList<ValidationFindingView>> LoadActiveErrorFindingsAsync(
        TenantId tenant,
        StudyId studyId,
        IReadOnlyCollection<string> targetKeys,
        IValidationFindings? findings,
        CancellationToken cancellationToken)
    {
        if (findings is null || targetKeys.Count == 0)
        {
            return [];
        }

        var page = await findings.ListAsync(
            tenant,
            new FindingQuery(studyId)
            {
                Severity = ValidationSeverity.Error,
                Statuses = [FindingStatus.Open, FindingStatus.Investigating],
                TargetKeys = targetKeys,
                Take = FindingQuery.MaxTake,
            },
            cancellationToken).ConfigureAwait(false);
        return page.Findings;
    }

    internal static bool TargetsCollectionOrMembers(
        FindingTarget target,
        Guid collectionInstanceGuid,
        IReadOnlySet<Guid> memberObservationGuids) =>
        (target.Kind == FindingTargetKind.DatasetRecord
            && Guid.TryParse(target.Key, out var recordGuid)
            && recordGuid == collectionInstanceGuid)
        || (target.Kind == FindingTargetKind.Observation
            && target.Node is { Kind: NodeKinds.Observation } obsNode
            && memberObservationGuids.Contains(obsNode.Id));
}
