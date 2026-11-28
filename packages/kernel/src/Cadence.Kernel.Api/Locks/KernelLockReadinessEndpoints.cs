using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api.Locks;

internal static class KernelLockReadinessEndpoints
{
    internal static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    internal static async Task<Results<Ok<KernelLockReadinessResponse>, ProblemHttpResult>> GetReadinessAsync(
        string studyRef,
        [FromQuery(Name = "scope_ref")] string? scopeRef,
        HttpContext context,
        IKernelNavigator navigator,
        ILockRecords locks,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        NodeRef? explicitScope = null;
        if (!string.IsNullOrWhiteSpace(scopeRef))
        {
            if (!NodeRef.TryParse(scopeRef.Trim(), out var parsedScope) || !LockScopes.IsScope(parsedScope))
            {
                return KernelProblems.BadRequest(
                    "Invalid lock scope",
                    "Name scope_ref as an unversioned study, study_site, enrollment, collection_instance or observation NodeRef.");
            }

            explicitScope = parsedScope;
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolvedStudy = await KernelStudyLookup.ResolveVisibleStudyAsync(
            tenant,
            studyRef,
            context,
            visibility,
            actor,
            (_, studyMask) => $"You may not see this study ({studyMask}), so its lock readiness is not available.",
            allowVersionedStudyRef: false,
            cancellationToken).ConfigureAwait(false);
        if (resolvedStudy.Problem is { } studyProblem)
        {
            return studyProblem;
        }

        var studyNode = resolvedStudy.Study.UnversionedRef;
        var targetScope = explicitScope ?? studyNode;
        if (targetScope != studyNode)
        {
            var scopeMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [targetScope], cancellationToken).ConfigureAwait(false);
            if (scopeMasks.For(targetScope) is { } scopeMask)
            {
                return KernelProblems.NotVisible($"You may not see this lock scope ({scopeMask}), so its lock readiness is not available.");
            }
        }

        var snapshot = await locks.ReadReadinessAsync(
            tenant,
            resolvedStudy.Study.StudyId,
            targetScope,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return KernelProblems.NotFound(targetScope);
        }

        var (items, maskedEvidenceRefs) = await MaskReadinessBlockersAsync(
            snapshot,
            tenant,
            studyNode,
            visibility,
            probe,
            actor,
            cancellationToken).ConfigureAwait(false);

        var explanationResponse = BuildMaskedExplanationResponse(snapshot.Explanation, items, maskedEvidenceRefs);
        return TypedResults.Ok(new KernelLockReadinessResponse(
            studyNode.ToString(),
            snapshot.StudyCode,
            snapshot.Scope.ToString(),
            snapshot.Scope.Kind,
            snapshot.ReadyForLock,
            snapshot.CanFreeze,
            snapshot.OpenQueryCount,
            snapshot.MissingRequiredItemCount,
            snapshot.OpenErrorFindingCount,
            items.Count,
            items,
            explanationResponse,
            snapshot.UnverifiedValueCount,
            snapshot.UnsignedFormCount,
            snapshot.UnopenedVisitCount));
    }

    internal static async Task<KernelError> MaskScopeNotReadyErrorAsync(
        HttpContext context,
        TenantId tenant,
        StudyId studyId,
        NodeRef studyNode,
        NodeRef targetScope,
        ILockRecords locks,
        KernelError applyError,
        CancellationToken cancellationToken)
    {
        if (applyError.Explanation is null
            || context.RequestServices.GetService<INodeVisibility>() is not { } visibility
            || context.RequestServices.GetService<IAuthorizationProbe>() is not { } probe
            || context.RequestServices.GetService<ICurrentActor>() is not { } actor)
        {
            return applyError;
        }

        var snapshot = await locks.ReadReadinessAsync(tenant, studyId, targetScope, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return applyError;
        }

        var (items, maskedEvidenceRefs) = await MaskReadinessBlockersAsync(
            snapshot,
            tenant,
            studyNode,
            visibility,
            probe,
            actor,
            cancellationToken).ConfigureAwait(false);
        if (!items.Any(b => b.Masked))
        {
            return applyError;
        }

        var maskedResponse = BuildMaskedExplanationResponse(snapshot.Explanation, items, maskedEvidenceRefs);
        static NodeRef[] ParseRefs(IEnumerable<string> refs) =>
            [.. refs.Select(r => NodeRef.TryParse(r, out var parsed) ? parsed : null).Where(r => r is not null).Select(r => r!)];

        var maskedExplanation = new Explanation(
            snapshot.Explanation.Subject,
            snapshot.Explanation.Claim,
            snapshot.Explanation.Producer,
            [.. maskedResponse.Steps.Select(s => new ExplanationStep(s.Claim, ParseRefs(s.Evidence)))],
            [.. maskedResponse.Conditions.Select(c =>
                c.Met
                    ? EvaluatedCondition.Satisfied(c.Code, c.Description, ParseRefs(c.Evidence))
                    : EvaluatedCondition.Unsatisfied(c.Code, c.Description, ParseRefs(c.Evidence)))],
            [.. maskedResponse.NextActions.Select(a =>
                new NextAction(
                    a.Description,
                    a.Capability,
                    a.Target is not null && NodeRef.TryParse(a.Target, out var parsedTarget) ? parsedTarget : null))]);

        return applyError with { Explanation = maskedExplanation };
    }

    private static async Task<(IReadOnlyList<KernelLockBlockerResponse> Items, HashSet<string> MaskedEvidenceRefs)> MaskReadinessBlockersAsync(
        LockReadinessSnapshot snapshot,
        TenantId tenant,
        NodeRef studyNode,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var nodesToCheck = new List<NodeRef>();
        foreach (var blocker in snapshot.Blockers)
        {
            nodesToCheck.Add(blocker.Target);
            if (blocker.StudySiteId is { } siteId)
            {
                nodesToCheck.Add(NodeRef.For(siteId));
            }

            if (blocker.EnrollmentId is { } enrollmentId)
            {
                nodesToCheck.Add(NodeRef.For(enrollmentId));
            }

            if (blocker.ObservationId is { } obsId)
            {
                nodesToCheck.Add(NodeRef.For(obsId));
            }

            if (blocker.QueryAssertionId is { } queryId)
            {
                nodesToCheck.Add(NodeRef.For(queryId));
            }

            if (blocker.FindingId is { } findingId)
            {
                nodesToCheck.Add(NodeRef.For(findingId));
            }
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, nodesToCheck, cancellationToken).ConfigureAwait(false);
        var unblindByTarget = new Dictionary<NodeRef, bool>();
        var items = new List<KernelLockBlockerResponse>(snapshot.Blockers.Count);
        var maskedEvidenceRefs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var blocker in snapshot.Blockers)
        {
            var mask = masks.For(blocker.Target)
                ?? (blocker.FindingId is { } fidCheck ? masks.For(NodeRef.For(fidCheck)) : null)
                ?? (blocker.QueryAssertionId is { } qidCheck ? masks.For(NodeRef.For(qidCheck)) : null)
                ?? (blocker.ObservationId is { } oidCheck ? masks.For(NodeRef.For(oidCheck)) : null)
                ?? (blocker.EnrollmentId is { } eid ? masks.For(NodeRef.For(eid)) : null)
                ?? (blocker.StudySiteId is { } sid ? masks.For(NodeRef.For(sid)) : null);

            if (mask is null && blocker.Blinded)
            {
                var unblindTarget = blocker.EnrollmentId is { } targetEnrollment
                    ? NodeRef.For(targetEnrollment)
                    : studyNode;
                if (!unblindByTarget.TryGetValue(unblindTarget, out var mayUnblind))
                {
                    var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, unblindTarget, cancellationToken).ConfigureAwait(false);
                    mayUnblind = decision.Permitted;
                    unblindByTarget[unblindTarget] = mayUnblind;
                }

                if (!mayUnblind)
                {
                    mask = MaskReason.Blinded;
                }
            }

            if (mask is { } reason)
            {
                if (blocker.Target != snapshot.Scope)
                {
                    maskedEvidenceRefs.Add(blocker.Target.ToString());
                }

                if (blocker.ObservationId is { } oid)
                {
                    maskedEvidenceRefs.Add(NodeRef.For(oid).ToString());
                }

                if (blocker.QueryAssertionId is { } qid)
                {
                    maskedEvidenceRefs.Add(NodeRef.For(qid).ToString());
                }

                if (blocker.FindingId is { } fid)
                {
                    maskedEvidenceRefs.Add(NodeRef.For(fid).ToString());
                }

                items.Add(KernelLockBlockerResponse.FromMasked(blocker, reason));
            }
            else
            {
                items.Add(KernelLockBlockerResponse.FromVisible(blocker));
            }
        }

        return (items, maskedEvidenceRefs);
    }

    private static KernelExplanationResponse BuildMaskedExplanationResponse(
        Explanation explanation,
        IReadOnlyList<KernelLockBlockerResponse> blockerItems,
        IReadOnlySet<string> maskedRefs)
    {
        if (maskedRefs.Count == 0)
        {
            return KernelExplanationResponse.From(explanation);
        }

        IReadOnlyList<string> Scrub(IEnumerable<NodeRef> refs) =>
            [.. refs.Select(r => r.ToString()).Where(r => !maskedRefs.Contains(r))];

        var summaryConditions = explanation.Conditions
            .Where(c => c.Code is LockReadinessEvaluator.QueriesClosedCondition
                or LockReadinessEvaluator.RequiredItemsCompleteCondition
                or LockReadinessEvaluator.ObservationsVerifiedCondition
                or LockReadinessEvaluator.FormsSignedCondition
                or LockReadinessEvaluator.ScheduledVisitsOpenedCondition
                or LockReadinessEvaluator.ErrorFindingsResolvedCondition)
            .Select(c => new KernelConditionResponse(c.Code, c.Description, c.Met, Scrub(c.Evidence)))
            .ToList();

        static IReadOnlyList<string> BlockerEvidence(KernelLockBlockerResponse blocker)
        {
            if (blocker.Masked || blocker.TargetRef is null)
            {
                return [];
            }

            if (blocker.QueryRef is not null)
            {
                return [blocker.QueryRef, blocker.TargetRef];
            }

            if (blocker.FindingRef is not null)
            {
                return [blocker.FindingRef, blocker.TargetRef];
            }

            return [blocker.TargetRef];
        }

        var blockerConditions = blockerItems
            .Select(b => new KernelConditionResponse(
                b.Kind switch
                {
                    nameof(LockBlockerKind.OpenQuery) => LockReadinessEvaluator.OpenQueryBlockerCondition,
                    nameof(LockBlockerKind.UnverifiedValue) => LockReadinessEvaluator.UnverifiedValueBlockerCondition,
                    nameof(LockBlockerKind.UnsignedForm) => LockReadinessEvaluator.UnsignedFormBlockerCondition,
                    nameof(LockBlockerKind.UnopenedVisit) => LockReadinessEvaluator.UnopenedVisitBlockerCondition,
                    nameof(LockBlockerKind.ErrorFinding) => LockReadinessEvaluator.ErrorFindingBlockerCondition,
                    _ => LockReadinessEvaluator.MissingRequiredItemBlockerCondition,
                },
                b.NextAction,
                Met: false,
                BlockerEvidence(b)))
            .ToList();

        var steps = blockerItems
            .Select(b => new KernelExplanationStepResponse(
                b.Masked
                    ? $"{b.Owner} owns a masked {b.Kind} blocker ({b.Mask})."
                    : b.NextAction,
                BlockerEvidence(b)))
            .ToList();

        var nextActions = blockerItems
            .Select(b => new KernelNextActionResponse(
                b.NextAction,
                b.NextCapability,
                b.Masked ? null : b.FindingRef ?? b.TargetRef))
            .ToList();

        return new KernelExplanationResponse(
            explanation.Subject?.ToString(),
            explanation.Claim,
            explanation.Producer,
            steps,
            [.. summaryConditions, .. blockerConditions],
            nextActions);
    }
}
