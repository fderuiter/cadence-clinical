using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Application.Authorization;

internal static class AuthorityBatchGrantEvaluator
{
    internal static async ValueTask<IReadOnlyDictionary<NodeRef, AuthorizationDecision>> EvaluateAgentManyAsync(
        AuthorityEvaluator evaluator,
        IAuthorityStore store,
        Actor actor,
        TenantId tenant,
        Capability capability,
        IReadOnlyList<NodeRef> resources,
        DateTimeOffset at,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (actor.RequestedBy is not { } person)
        {
            var noRequester = AuthorizationDecision.Deny(
                "An automation agent acts only for the person who asked it (K-AI-6).",
                EvaluatedCondition.Unsatisfied(AuthorityEvaluator.RequesterCondition, "Run the agent for the person it acts for"));
            return resources.ToDictionary(r => r, _ => noRequester);
        }

        if (capability.VerbClass is VerbClass.Execute or VerbClass.Approve)
        {
            var verbDenied = AuthorizationDecision.Deny(
                $"An automation agent cannot {capability.VerbClass.ToString().ToLowerInvariant()}; a person must.",
                EvaluatedCondition.Unsatisfied(AuthorityEvaluator.AgentVerbCondition, "Have a person perform this action"));
            return resources.ToDictionary(r => r, _ => verbDenied);
        }

        var ownDecisions = await EvaluateGrantsManyAsync(
            store,
            actor.AsIdentity(),
            tenant,
            capability,
            resources,
            at,
            knownAt,
            qualified: false,
            cancellationToken).ConfigureAwait(false);

        var permittedByAgent = resources.Where(r => ownDecisions[r].Permitted).ToList();
        var personDecisions = permittedByAgent.Count == 0
            ? new Dictionary<NodeRef, AuthorizationDecision>()
            : await evaluator.EvaluateManyAsync(person, tenant, capability, permittedByAgent, at, knownAt, cancellationToken).ConfigureAwait(false);

        var personLabel = AuthorityQualificationAndActorHelper.DescribeActor(person, "the requesting person");
        var personRef = AuthorityQualificationAndActorHelper.ActorEvidence(person);
        NodeRef[] requesterEvidence = personRef is { } pRef ? [pRef] : [];
        var agentLabel = AuthorityQualificationAndActorHelper.DescribeActor(actor, "The automation agent");
        var combined = new Dictionary<NodeRef, AuthorizationDecision>(resources.Count);
        foreach (var resource in resources)
        {
            var own = ownDecisions[resource];
            if (!own.Permitted)
            {
                combined[resource] = own;
                continue;
            }

            var forPerson = personDecisions[resource];
            var bounded = forPerson.Permitted
                ? EvaluatedCondition.Satisfied(AuthorityEvaluator.RequesterCondition, $"Acts for {personLabel}, who may {capability.Token}", requesterEvidence)
                : EvaluatedCondition.Unsatisfied(AuthorityEvaluator.RequesterCondition, $"Act for a person who may {capability.Token}", requesterEvidence);
            EvaluatedCondition[] conditions = [.. own.Conditions, bounded, .. forPerson.Conditions];
            combined[resource] = forPerson.Permitted
                ? AuthorizationDecision.Permit(conditions: conditions)
                : AuthorizationDecision.Deny($"{agentLabel} acts for {personLabel}, who may not {capability.Token}: {forPerson.Reason}", conditions);
        }

        return combined;
    }

    internal static async ValueTask<IReadOnlyDictionary<NodeRef, AuthorizationDecision>> EvaluateGrantsManyAsync(
        IAuthorityStore store,
        Actor actor,
        TenantId tenant,
        Capability capability,
        IReadOnlyList<NodeRef> resources,
        DateTimeOffset at,
        DateTimeOffset knownAt,
        bool qualified,
        CancellationToken cancellationToken)
    {
        var token = capability.Token;
        var held = await store.GrantsHeldAsync(tenant, actor, cancellationToken).ConfigureAwait(false);
        var roles = await AuthorityQualificationAndActorHelper.RoleCapabilitiesFromStoreAsync(store, tenant, held, cancellationToken).ConfigureAwait(false);
        var carrying = held.Where(g => AuthorityQualificationAndActorHelper.Carries(g, token, roles)).ToList();

        var decisions = new Dictionary<NodeRef, AuthorizationDecision>(resources.Count);
        if (carrying.Count == 0)
        {
            foreach (var resource in resources)
            {
                decisions[resource] = AuthorityQualificationAndActorHelper.DenyMissingGrant(actor, token, resource, carrying);
            }

            return decisions;
        }

        var inEffectIds = await GrantChain.FilterInEffectAsync(store, tenant, carrying, at, knownAt, cancellationToken).ConfigureAwait(false);
        NodeRef[] carryingRefs = [.. carrying.Select(g => NodeRef.For(g.Id))];
        if (inEffectIds.Count == 0)
        {
            foreach (var resource in resources)
            {
                decisions[resource] = AuthorityQualificationAndActorHelper.DenyMissingGrant(actor, token, resource, carrying);
            }

            return decisions;
        }

        var unversionedResources = resources.Select(r => r.Unversioned()).Distinct().ToList();
        var (paths, evidence) = await store.ResolveVisibilityBatchAsync(tenant, unversionedResources, cancellationToken).ConfigureAwait(false);

        var checkBlindedEvidence = !string.Equals(token, AuthorityEvaluator.EnrollmentUnblindToken, StringComparison.Ordinal)
            && unversionedResources.Any(r => evidence.TryGetValue(r, out var ev) && ev.IsBlinded);
        List<Grant>? unblindCarrying = null;
        IReadOnlySet<GrantId>? unblindInEffectIds = null;
        List<QualificationRequirement>? unblindActiveRequirements = null;
        if (checkBlindedEvidence)
        {
            unblindCarrying = held.Where(g => AuthorityQualificationAndActorHelper.Carries(g, AuthorityEvaluator.EnrollmentUnblindToken, roles)).ToList();
            unblindInEffectIds = unblindCarrying.Count > 0
                ? await GrantChain.FilterInEffectAsync(store, tenant, unblindCarrying, at, knownAt, cancellationToken).ConfigureAwait(false)
                : FrozenSet<GrantId>.Empty;
            if (qualified && unblindInEffectIds.Count > 0)
            {
                unblindActiveRequirements = (await store.QualificationRequirementsAsync(tenant, AuthorityEvaluator.EnrollmentUnblindToken, cancellationToken).ConfigureAwait(false))
                    .Where(r => r.IsInEffect(at, knownAt))
                    .OrderBy(r => r.Id)
                    .ToList();
            }
        }

        IReadOnlyDictionary<NodeRef, ScopePath>? exportGrantPaths = null;
        var matched = new Dictionary<NodeRef, (Grant Grant, ScopePath QualificationPath, Grant? UnblindGrant, ScopePath? EvidenceScope)>(resources.Count);
        foreach (var resource in resources)
        {
            var unversioned = resource.Unversioned();
            var hasEvidence = evidence.TryGetValue(unversioned, out var evidenceInfo);
            var path = paths.GetValueOrDefault(unversioned, ScopePath.TenantWide);
            if (hasEvidence && path == ScopePath.TenantWide && evidenceInfo!.Scope != ScopePath.TenantWide)
            {
                path = evidenceInfo.Scope;
            }

            Grant? unblindGrant = null;
            ScopePath? blindedEvidenceScope = null;
            if (checkBlindedEvidence && hasEvidence && evidenceInfo!.IsBlinded)
            {
                unblindGrant = unblindCarrying!.FirstOrDefault(g => g.Scope.Covers(evidenceInfo.Scope) && unblindInEffectIds!.Contains(g.Id));
                if (unblindGrant is null)
                {
                    decisions[resource] = AuthorityQualificationAndActorHelper.DenyMissingGrant(actor, token, resource, carrying);
                    continue;
                }

                blindedEvidenceScope = evidenceInfo.Scope;
            }

            var coveringGrant = carrying.FirstOrDefault(g => g.Scope.Covers(path) && inEffectIds.Contains(g.Id));
            if (coveringGrant is not null)
            {
                matched[resource] = (coveringGrant, path, unblindGrant, blindedEvidenceScope);
                continue;
            }

            if (token == "study.export" && path.Study is not null)
            {
                exportGrantPaths ??= await store.PathsOfAsync(tenant, carryingRefs, cancellationToken).ConfigureAwait(false);
                foreach (var grant in carrying)
                {
                    if (!inEffectIds.Contains(grant.Id))
                    {
                        continue;
                    }

                    var grantPath = exportGrantPaths.GetValueOrDefault(NodeRef.For(grant.Id), ScopePath.TenantWide);
                    if (grantPath.Study == path.Study)
                    {
                        matched[resource] = (grant, grantPath, unblindGrant, blindedEvidenceScope);
                        break;
                    }
                }

                if (matched.ContainsKey(resource))
                {
                    continue;
                }
            }

            decisions[resource] = AuthorityQualificationAndActorHelper.DenyMissingGrant(actor, token, resource, carrying);
        }

        if (matched.Count == 0)
        {
            return decisions;
        }

        if (!qualified)
        {
            foreach (var (resource, (grant, _, unblindGrant, _)) in matched)
            {
                EvaluatedCondition[] conditions = unblindGrant is null
                    ? [EvaluatedCondition.Satisfied(AuthorityEvaluator.GrantCondition, $"Holds {token} for {grant.Scope}", NodeRef.For(grant.Id))]
                    :
                    [
                        EvaluatedCondition.Satisfied(AuthorityEvaluator.GrantCondition, $"Holds {token} for {grant.Scope}", NodeRef.For(grant.Id)),
                        EvaluatedCondition.Satisfied(AuthorityEvaluator.GrantCondition, $"Holds {AuthorityEvaluator.EnrollmentUnblindToken} for {unblindGrant.Scope}", NodeRef.For(unblindGrant.Id)),
                    ];
                decisions[resource] = AuthorizationDecision.Permit(conditions: conditions);
            }

            return decisions;
        }

        var activeRequirements = (await store.QualificationRequirementsAsync(tenant, token, cancellationToken).ConfigureAwait(false))
            .Where(r => r.IsInEffect(at, knownAt))
            .OrderBy(r => r.Id)
            .ToList();
        var needsPrimaryQualifications = activeRequirements.Count > 0
            && matched.Values.Any(m => activeRequirements.Any(r => r.Scope.Covers(m.QualificationPath)));
        var needsUnblindQualifications = unblindActiveRequirements is { Count: > 0 }
            && matched.Values.Any(m => m.EvidenceScope is { } scope && unblindActiveRequirements.Any(r => r.Scope.Covers(scope)));
        var heldQualifications = needsPrimaryQualifications || needsUnblindQualifications
            ? await store.QualificationsHeldAsync(tenant, actor, cancellationToken).ConfigureAwait(false)
            : [];
        IReadOnlyDictionary<ProtocolVersionId, int> versionNumbers = AuthorityQualificationAndActorHelper.EmptyVersionNumbers;
        if (needsPrimaryQualifications || needsUnblindQualifications)
        {
            var versionIds = (needsPrimaryQualifications ? activeRequirements : Enumerable.Empty<QualificationRequirement>())
                .Concat(needsUnblindQualifications && unblindActiveRequirements is not null ? unblindActiveRequirements : Enumerable.Empty<QualificationRequirement>())
                .Where(r => r.ProtocolVersionId is not null)
                .Select(r => r.ProtocolVersionId!.Value)
                .Distinct()
                .ToList();
            if (versionIds.Count > 0)
            {
                versionNumbers = await store.ProtocolVersionNumbersAsync(tenant, versionIds, cancellationToken).ConfigureAwait(false);
            }
        }

        var actorLabel = AuthorityQualificationAndActorHelper.DescribeActor(actor, "The signed-in user");
        foreach (var (resource, (grant, qualificationPath, unblindGrant, evidenceScope)) in matched)
        {
            EvaluatedCondition[] unblindConditions = [];
            if (unblindGrant is not null && evidenceScope is not null)
            {
                var unblindQualConditions = unblindActiveRequirements is { Count: > 0 }
                    ? AuthorityQualificationAndActorHelper.EvaluateQualificationConditions(unblindActiveRequirements, heldQualifications, versionNumbers, evidenceScope, at, knownAt)
                    : [];
                if (unblindQualConditions.Any(c => !c.Met))
                {
                    decisions[resource] = AuthorityQualificationAndActorHelper.DenyMissingGrant(actor, token, resource, carrying);
                    continue;
                }

                unblindConditions =
                [
                    EvaluatedCondition.Satisfied(AuthorityEvaluator.GrantCondition, $"Holds {AuthorityEvaluator.EnrollmentUnblindToken} for {unblindGrant.Scope}", NodeRef.For(unblindGrant.Id)),
                    .. unblindQualConditions,
                ];
            }

            var qualConditions = AuthorityQualificationAndActorHelper.EvaluateQualificationConditions(activeRequirements, heldQualifications, versionNumbers, qualificationPath, at, knownAt);
            EvaluatedCondition[] conditions =
            [
                EvaluatedCondition.Satisfied(AuthorityEvaluator.GrantCondition, $"Holds {token} for {grant.Scope}", NodeRef.For(grant.Id)),
                .. unblindConditions,
                .. qualConditions,
            ];
            decisions[resource] = conditions.FirstOrDefault(c => !c.Met) is { } missing
                ? AuthorizationDecision.Deny($"{actorLabel} is not qualified to {token}: {missing.Description}.", conditions)
                : AuthorizationDecision.Permit(conditions: conditions);
        }

        return decisions;
    }
}
