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
/// Evaluates readiness gates for form signatures (<see cref="SignCollectionInstance"/>) and scope freezes/locks
/// (<see cref="ApplyLock"/>) against canonical kernel evidence, assertions, and validation findings.
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

        var activeErrorFindings = await LoadActiveErrorFindingsAsync(tenant, instance.StudyId, findings, cancellationToken)
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

    public static async Task<KernelError?> CheckScopeLockableAsync(
        TenantId tenant,
        StudyId studyId,
        NodeRef scope,
        IKernelUnitOfWork work,
        IValidationFindings? findings,
        CancellationToken cancellationToken)
    {
        var summary = await EvaluateScopeAsync(tenant, studyId, scope, work, findings, cancellationToken).ConfigureAwait(false);
        if (summary.Ready)
        {
            return null;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (summary.IncompleteFormCount > 0)
        {
            errors["incomplete_forms"] = [$"{summary.IncompleteFormCount} incomplete form(s) or unrecorded placeholder value(s) in scope."];
        }

        if (summary.OpenQueryCount + summary.AnsweredQueryCount > 0)
        {
            errors["active_queries"] = [$"open={summary.OpenQueryCount}, answered={summary.AnsweredQueryCount}"];
        }

        if (summary.OpenErrorFindingCount > 0)
        {
            errors["error_findings"] = [$"{summary.OpenErrorFindingCount} active Error validation finding(s) in scope."];
        }

        return AssertionErrors.ScopeNotReady.WithMessage(string.Join(" ", summary.Blockers)) with { Errors = errors };
    }

    public static async Task<ScopeReadinessSummary> EvaluateScopeAsync(
        TenantId tenant,
        StudyId studyId,
        NodeRef scope,
        IKernelUnitOfWork work,
        IValidationFindings? findings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CollectionInstance> forms;
        HashSet<Guid> coveredObservationGuids;
        HashSet<Guid> coveredFormGuids;
        string? singleObservationItemId = null;
        Guid? singleObservationParentFormGuid = null;
        var placeholderObservationCount = 0;

        switch (scope.Kind)
        {
            case NodeKinds.Study:
                {
                    forms = await work.ReadAllAsync<CollectionInstance>(c => c.StudyId == studyId, cancellationToken).ConfigureAwait(false);
                    var studyObs = await work.ReadAllAsync<Observation>(o => o.StudyId == studyId, cancellationToken).ConfigureAwait(false);
                    coveredObservationGuids = studyObs.Select(o => o.Id.Value).ToHashSet();
                    coveredFormGuids = forms.Select(f => f.Id.Value).ToHashSet();
                    break;
                }
            case NodeKinds.StudySite:
                {
                    var siteId = StudySiteId.FromGuid(scope.Id);
                    var enrollments = await work.ReadAllAsync<Enrollment>(e => e.StudySiteId == siteId, cancellationToken).ConfigureAwait(false);
                    var enrollmentIds = enrollments.Select(e => e.Id).ToHashSet();
                    forms = await work.ReadAllAsync<CollectionInstance>(c => c.StudySiteId == siteId, cancellationToken).ConfigureAwait(false);
                    var studyObs = enrollmentIds.Count == 0
                        ? []
                        : await work.ReadAllAsync<Observation>(o => o.StudyId == studyId, cancellationToken).ConfigureAwait(false);
                    coveredObservationGuids = studyObs
                        .Where(o => enrollmentIds.Contains(o.EnrollmentId))
                        .Select(o => o.Id.Value)
                        .ToHashSet();
                    coveredFormGuids = forms.Select(f => f.Id.Value).ToHashSet();
                    break;
                }
            case NodeKinds.Enrollment:
                {
                    var enrollmentId = EnrollmentId.FromGuid(scope.Id);
                    forms = await work.ReadAllAsync<CollectionInstance>(c => c.EnrollmentId == enrollmentId, cancellationToken).ConfigureAwait(false);
                    var enrollmentObs = await work.ReadAllAsync<Observation>(o => o.EnrollmentId == enrollmentId, cancellationToken).ConfigureAwait(false);
                    coveredObservationGuids = enrollmentObs.Select(o => o.Id.Value).ToHashSet();
                    coveredFormGuids = forms.Select(f => f.Id.Value).ToHashSet();
                    break;
                }
            case NodeKinds.CollectionInstance:
                {
                    var formId = CollectionInstanceId.FromGuid(scope.Id);
                    var instance = await work.ReadAsync<CollectionInstance>(formId, cancellationToken).ConfigureAwait(false);
                    forms = instance is null ? [] : [instance];
                    coveredObservationGuids = instance?.Members.Select(m => m.ObservationId.Value).ToHashSet() ?? [];
                    coveredFormGuids = instance is null ? [] : [instance.Id.Value];
                    break;
                }
            default:
                {
                    var obsId = ObservationId.FromGuid(scope.Id);
                    forms = [];
                    coveredFormGuids = [];
                    var obs = await work.ReadAsync<Observation>(obsId, cancellationToken).ConfigureAwait(false);
                    coveredObservationGuids = obs is null ? [] : [obs.Id.Value];
                    if (obs?.Value is MissingValue { Reason: NullFlavor.NotAsked })
                    {
                        placeholderObservationCount = 1;
                    }

                    var parentForms = await work.ReadAllAsync<CollectionInstance>(
                        c => c.Members.Any(m => m.ObservationId == obsId),
                        cancellationToken).ConfigureAwait(false);
                    if (parentForms.Count > 0)
                    {
                        singleObservationParentFormGuid = parentForms[0].Id.Value;
                        singleObservationItemId = parentForms[0].Members.FirstOrDefault(m => m.ObservationId == obsId)?.ItemId;
                    }

                    break;
                }
        }

        var incompleteFormCount = placeholderObservationCount + await CountFormsMissingRequiredAsync(forms, work, cancellationToken).ConfigureAwait(false);
        var (openQueryCount, answeredQueryCount) = coveredObservationGuids.Count == 0 && scope.Kind != NodeKinds.Study
            ? (0, 0)
            : await CountActiveQueriesAsync(
                work,
                studyId,
                q => scope.Kind == NodeKinds.Study
                    || (q.TargetKind == NodeKinds.Observation && coveredObservationGuids.Contains(q.TargetId)),
                cancellationToken).ConfigureAwait(false);

        var activeErrors = await LoadActiveErrorFindingsAsync(tenant, studyId, findings, cancellationToken).ConfigureAwait(false);
        var errorFindingCount = scope.Kind switch
        {
            NodeKinds.Study => activeErrors.Count,
            NodeKinds.Observation => activeErrors.Count(f =>
                (f.Target.Kind == FindingTargetKind.Observation && f.Target.Node is { } obsNode && coveredObservationGuids.Contains(obsNode.Id))
                || (singleObservationParentFormGuid is { } pGuid
                    && f.Target.Kind == FindingTargetKind.DatasetRecord
                    && Guid.TryParse(f.Target.Key, out var rGuid)
                    && rGuid == pGuid
                    && (string.IsNullOrWhiteSpace(f.Target.Variable)
                        || string.Equals(f.Target.Variable, singleObservationItemId, StringComparison.Ordinal)))),
            _ => activeErrors.Count(f => TargetsCoveredScope(f.Target, coveredFormGuids, coveredObservationGuids)),
        };

        return CollectionReadiness.BuildScopeSummary(scope, incompleteFormCount, openQueryCount, answeredQueryCount, errorFindingCount);
    }

    private static async Task<int> CountFormsMissingRequiredAsync(
        IReadOnlyList<CollectionInstance> forms,
        IKernelUnitOfWork work,
        CancellationToken cancellationToken)
    {
        if (forms.Count == 0)
        {
            return 0;
        }

        var versionIds = forms.Select(f => f.ProtocolVersionId).Distinct().ToList();
        var versions = (await work.ReadAllAsync<ProtocolVersion>(v => versionIds.Contains(v.Id), cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.Id);
        var memberIds = forms.SelectMany(f => f.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var observations = memberIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : (await work.ReadAllAsync<Observation>(o => memberIds.Contains(o.Id), cancellationToken).ConfigureAwait(false))
                .ToDictionary(o => o.Id);

        var incomplete = 0;
        foreach (var form in forms)
        {
            if (!versions.TryGetValue(form.ProtocolVersionId, out var version))
            {
                continue;
            }

            var spec = CollectionSpecs.Of(version, form.CollectionSpecId);
            if (spec is null || !spec.Items.Any(i => i.Required))
            {
                continue;
            }

            var recordedItems = form.Members
                .Where(m => observations.TryGetValue(m.ObservationId, out var obs)
                    && obs.Value is not MissingValue { Reason: NullFlavor.NotAsked })
                .Select(m => m.ItemId)
                .ToHashSet(StringComparer.Ordinal);
            if (spec.Items.Any(i => i.Required && !recordedItems.Contains(i.Id)))
            {
                incomplete++;
            }
        }

        return incomplete;
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
        IValidationFindings? findings,
        CancellationToken cancellationToken)
    {
        if (findings is null)
        {
            return [];
        }

        var page = await findings.ListAsync(
            tenant,
            new FindingQuery(studyId)
            {
                Severity = ValidationSeverity.Error,
                Statuses = [FindingStatus.Open, FindingStatus.Investigating],
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

    private static bool TargetsCoveredScope(
        FindingTarget target,
        IReadOnlySet<Guid> coveredFormGuids,
        IReadOnlySet<Guid> coveredObservationGuids) =>
        (target.Kind == FindingTargetKind.DatasetRecord
            && Guid.TryParse(target.Key, out var recordGuid)
            && coveredFormGuids.Contains(recordGuid))
        || (target.Kind == FindingTargetKind.Observation
            && target.Node is { Kind: NodeKinds.Observation } obsNode
            && coveredObservationGuids.Contains(obsNode.Id));
}
