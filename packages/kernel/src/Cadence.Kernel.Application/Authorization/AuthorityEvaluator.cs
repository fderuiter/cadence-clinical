using System;
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
using Cadence.Kernel.Tenancy;

namespace Cadence.Kernel.Application.Authorization;

/// <summary>
/// The kernel's authorization decision procedure (spec §9.3, ADR-2251, ADR-2455), behind <see cref="IAuthorizationEvaluator"/>:
/// <list type="number">
/// <item>Grants and delegations held by the actor and in effect at <c>(at, known_at)</c>; a delegation also needs
/// every grant above it in effect (K-AUTH-3).</item>
/// <item>The capability is the grant's, or in the granted role, and the grant's scope covers the resource's scope path.</item>
/// <item>Automation agents may only read and propose (spec §11), each for one requesting person who must be permitted the
/// same capability on the same resource (K-AI-6, ADR-2632).</item>
/// <item>Every qualification requirement for the capability in effect over the resource's scope path is met by a
/// qualification the actor holds, in effect at the same <c>(at, known_at)</c> (K-AUTH-5).</item>
/// <item>When an <c>observation</c>, <c>validation_finding</c>, <c>assertion</c>, or <c>form_query</c> resource
/// classifies as blinded treatment data (<c>K-AUTH-4</c>), any capability other than <c>enrollment.unblind</c> also
/// requires an in-effect <c>enrollment.unblind</c> grant and (for accountable people) its <c>K-AUTH-5</c>
/// qualifications over the evidence scope; missing unblinding authority denies with the uniform <see cref="GrantCondition"/>
/// so callers gain no existence or allocation signal.</item>
/// </list>
/// Two rules are about grants themselves: the holder of a grant with the grant option may delegate under it, and a
/// delegator may revoke a delegation it signed. Every decision lists the conditions checked with their evidence
/// (K-AUTH-6); a missing qualification is named, with the one that lapsed when there is one. Separation of duties is a
/// later slice of #4781.
/// </summary>
public sealed class AuthorityEvaluator(IAuthorityStore store, KernelAuthorityOptions options) : IAuthorizationEvaluator
{
    internal const string EnrollmentUnblindToken = "enrollment.unblind";

    /// <summary>Condition: an active grant covers the capability and resource.</summary>
    public const string GrantCondition = "grant.active";

    /// <summary>Condition: the actor holds the delegating grant, in effect, with the grant option.</summary>
    public const string DelegationCondition = "grant.delegable";

    /// <summary>Condition: the actor signed the delegation being revoked.</summary>
    public const string DelegatorCondition = "grant.delegator";

    /// <summary>Condition: the actor is a platform operator.</summary>
    public const string PlatformOperatorCondition = "actor.platform_operator";

    /// <summary>Condition: the actor is the dedicated commercial billing operator.</summary>
    public const string CommercialBillingOperatorCondition = "actor.commercial_billing_operator";

    /// <summary>Condition: an automation agent acts only within its verb classes.</summary>
    public const string AgentVerbCondition = "actor.agent_verb";

    /// <summary>Condition: an automation agent acts for one requesting person, who may do the same (K-AI-6).</summary>
    public const string RequesterCondition = "actor.requester";

    /// <summary>Condition: the actor holds a current qualification a requirement names (K-AUTH-5).</summary>
    public const string QualificationCondition = "qualification.current";

    /// <summary>How many grants a delegation chain may have above it; deeper chains are not in effect.</summary>
    public const int MaxDelegationDepth = 8;

    public ValueTask<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken) =>
        EvaluateCoreAsync(request, enforceEvidenceBlinding: true, cancellationToken);

    /// <summary>
    /// Evaluates grants, delegations, <c>K-AI-6</c> requester bounds, and <c>K-AUTH-5</c> qualifications for
    /// <paramref name="request"/>'s capability over the resource's scope without pre-filtering blinded items that a
    /// work queue masks in place (ADR-2471, ADR-2486).
    /// </summary>
    internal ValueTask<AuthorizationDecision> EvaluateScopeCapabilityAsync(AuthorizationRequest request, CancellationToken cancellationToken) =>
        EvaluateCoreAsync(request, enforceEvidenceBlinding: false, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<NodeRef, AuthorizationDecision>> EvaluateManyAsync(
        Actor actor,
        TenantId tenant,
        Capability capability,
        IReadOnlyCollection<NodeRef> resources,
        DateTimeOffset at,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(resources);
        var distinct = resources.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<NodeRef, AuthorizationDecision>();
        }

        if (actor.Kind == ActorKind.AutomationAgent)
        {
            return await AuthorityBatchGrantEvaluator.EvaluateAgentManyAsync(
                this,
                store,
                actor,
                tenant,
                capability,
                distinct,
                at,
                knownAt,
                cancellationToken).ConfigureAwait(false);
        }

        if (actor.RequestedBy is not null)
        {
            var denied = AuthorizationDecision.Deny(
                "Only an automation agent acts for a requesting person.",
                EvaluatedCondition.Unsatisfied(RequesterCondition, "Act as yourself"));
            return distinct.ToDictionary(r => r, _ => denied);
        }

        var token = capability.Token;
        if (actor.Kind == ActorKind.ServiceAccount && options.PlatformOperators.Contains(actor.Id))
        {
            if (KernelAuthorityOptions.PlatformCapabilities.Contains(token))
            {
                var permitted = AuthorizationDecision.Permit(
                    conditions: EvaluatedCondition.Satisfied(PlatformOperatorCondition, $"{actor.Id} operates the platform"));
                return distinct.ToDictionary(r => r, _ => permitted);
            }

            if (actor == CommercialOperators.BillingOperator
                && KernelAuthorityOptions.CommercialBillingCapabilities.Contains(token))
            {
                var permitted = AuthorizationDecision.Permit(
                    conditions: EvaluatedCondition.Satisfied(CommercialBillingOperatorCondition, $"{actor.Id} operates commercial billing"));
                return distinct.ToDictionary(r => r, _ => permitted);
            }
        }

        var decisions = new Dictionary<NodeRef, AuthorizationDecision>(distinct.Count);
        var grantEvaluated = distinct;
        if (token is "grant.delegate" or "grant.revoke")
        {
            grantEvaluated = new List<NodeRef>(distinct.Count);
            foreach (var resource in distinct)
            {
                if (resource.Kind == NodeKinds.Grant)
                {
                    var decided = await DecideOnGrantAsync(
                        new AuthorizationRequest(actor, tenant, capability, resource, at, knownAt),
                        cancellationToken).ConfigureAwait(false);
                    if (decided is not null)
                    {
                        decisions[resource] = decided;
                        continue;
                    }
                }

                grantEvaluated.Add(resource);
            }
        }

        if (grantEvaluated.Count > 0)
        {
            var fromGrants = await AuthorityBatchGrantEvaluator.EvaluateGrantsManyAsync(
                store,
                actor,
                tenant,
                capability,
                grantEvaluated,
                at,
                knownAt,
                qualified: true,
                cancellationToken).ConfigureAwait(false);
            foreach (var (resource, decision) in fromGrants)
            {
                decisions[resource] = decision;
            }
        }

        return decisions;
    }

    /// <summary>
    /// Builds non-confirming <see cref="GrantCondition"/> denials matching <see cref="EvaluateGrantsAsync"/> for capabilities
    /// probed against a resource hidden from <paramref name="actor"/>, loading the actor's grants at most once.
    /// </summary>
    public static ValueTask<IReadOnlyList<AuthorizationDecision>> DenyHiddenResourceCapabilitiesAsync(
        IAuthorityStore? store,
        TenantId tenant,
        Actor actor,
        IReadOnlyList<Capability> capabilities,
        NodeRef resource,
        CancellationToken cancellationToken) =>
        AuthorityQualificationAndActorHelper.DenyHiddenResourceCapabilitiesAsync(
            store,
            tenant,
            actor,
            capabilities,
            resource,
            cancellationToken);

    private async ValueTask<AuthorizationDecision> EvaluateCoreAsync(
        AuthorizationRequest request,
        bool enforceEvidenceBlinding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Actor.Kind == ActorKind.AutomationAgent)
        {
            return await EvaluateAgentAsync(request, enforceEvidenceBlinding, cancellationToken).ConfigureAwait(false);
        }

        if (request.Actor.RequestedBy is not null)
        {
            return AuthorizationDecision.Deny(
                "Only an automation agent acts for a requesting person.",
                EvaluatedCondition.Unsatisfied(RequesterCondition, "Act as yourself"));
        }

        var token = request.Capability.Token;
        if (request.Actor.Kind == ActorKind.ServiceAccount && options.PlatformOperators.Contains(request.Actor.Id))
        {
            if (KernelAuthorityOptions.PlatformCapabilities.Contains(token))
            {
                return AuthorizationDecision.Permit(
                    conditions: EvaluatedCondition.Satisfied(PlatformOperatorCondition, $"{request.Actor.Id} operates the platform"));
            }

            if (request.Actor == CommercialOperators.BillingOperator
                && KernelAuthorityOptions.CommercialBillingCapabilities.Contains(token))
            {
                return AuthorizationDecision.Permit(
                    conditions: EvaluatedCondition.Satisfied(CommercialBillingOperatorCondition, $"{request.Actor.Id} operates commercial billing"));
            }
        }

        if (request.Resource.Kind == NodeKinds.Grant && token is "grant.delegate" or "grant.revoke")
        {
            var decided = await DecideOnGrantAsync(request, cancellationToken).ConfigureAwait(false);
            if (decided is not null)
            {
                return decided;
            }
        }

        return await EvaluateGrantsAsync(request, qualified: true, enforceEvidenceBlinding, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AuthorizationDecision> EvaluateAgentAsync(
        AuthorizationRequest request,
        bool enforceEvidenceBlinding,
        CancellationToken cancellationToken)
    {
        if (request.Actor.RequestedBy is not { } person)
        {
            return AuthorizationDecision.Deny(
                "An automation agent acts only for the person who asked it (K-AI-6).",
                EvaluatedCondition.Unsatisfied(RequesterCondition, "Run the agent for the person it acts for"));
        }

        if (request.Capability.VerbClass is VerbClass.Execute or VerbClass.Approve)
        {
            return AuthorizationDecision.Deny(
                $"An automation agent cannot {request.Capability.VerbClass.ToString().ToLowerInvariant()}; a person must.",
                EvaluatedCondition.Unsatisfied(AgentVerbCondition, "Have a person perform this action"));
        }

        var own = await EvaluateGrantsAsync(request with { Actor = request.Actor.AsIdentity() }, qualified: false, enforceEvidenceBlinding, cancellationToken).ConfigureAwait(false);
        if (!own.Permitted)
        {
            return own;
        }

        var forPerson = await EvaluateCoreAsync(request with { Actor = person }, enforceEvidenceBlinding, cancellationToken).ConfigureAwait(false);
        var personLabel = AuthorityQualificationAndActorHelper.DescribeActor(person, "the requesting person");
        var personRef = AuthorityQualificationAndActorHelper.ActorEvidence(person);
        NodeRef[] requesterEvidence = personRef is { } pRef ? [pRef] : [];
        var bounded = forPerson.Permitted
            ? EvaluatedCondition.Satisfied(RequesterCondition, $"Acts for {personLabel}, who may {request.Capability.Token}", requesterEvidence)
            : EvaluatedCondition.Unsatisfied(RequesterCondition, $"Act for a person who may {request.Capability.Token}", requesterEvidence);
        EvaluatedCondition[] conditions = [.. own.Conditions, bounded, .. forPerson.Conditions];
        var agentLabel = AuthorityQualificationAndActorHelper.DescribeActor(request.Actor, "The automation agent");
        return forPerson.Permitted
            ? AuthorizationDecision.Permit(conditions: conditions)
            : AuthorizationDecision.Deny($"{agentLabel} acts for {personLabel}, who may not {request.Capability.Token}: {forPerson.Reason}", conditions);
    }

    private async ValueTask<AuthorizationDecision> EvaluateGrantsAsync(
        AuthorizationRequest request,
        bool qualified,
        bool enforceEvidenceBlinding,
        CancellationToken cancellationToken)
    {
        var token = request.Capability.Token;
        var (paths, evidence) = await store.ResolveVisibilityBatchAsync(request.Tenant, [request.Resource], cancellationToken).ConfigureAwait(false);
        var hasEvidence = evidence.TryGetValue(request.Resource.Unversioned(), out var evidenceInfo);
        var path = paths.GetValueOrDefault(request.Resource, ScopePath.TenantWide);
        if (hasEvidence && path == ScopePath.TenantWide && evidenceInfo!.Scope != ScopePath.TenantWide)
        {
            path = evidenceInfo.Scope;
        }

        var actorLabel = AuthorityQualificationAndActorHelper.DescribeActor(request.Actor, "The signed-in user");
        var held = await store.GrantsHeldAsync(request.Tenant, request.Actor, cancellationToken).ConfigureAwait(false);
        var roles = await AuthorityQualificationAndActorHelper.RoleCapabilitiesFromStoreAsync(store, request.Tenant, held, cancellationToken).ConfigureAwait(false);
        var carrying = held.Where(g => AuthorityQualificationAndActorHelper.Carries(g, token, roles)).ToList();

        EvaluatedCondition[] unblindConditions = [];
        if (enforceEvidenceBlinding
            && hasEvidence
            && evidenceInfo!.IsBlinded
            && !string.Equals(token, EnrollmentUnblindToken, StringComparison.Ordinal))
        {
            var resolvedUnblind = await ResolveBlindedEvidenceConditionsAsync(
                request,
                evidenceInfo.Scope,
                held,
                roles,
                qualified,
                cancellationToken).ConfigureAwait(false);
            if (resolvedUnblind is null)
            {
                return AuthorityQualificationAndActorHelper.DenyMissingGrant(request.Actor, token, request.Resource, carrying);
            }

            unblindConditions = resolvedUnblind;
        }

        foreach (var grant in carrying.Where(g => g.Scope.Covers(path)))
        {
            if (await InEffectAsync(request, grant, cancellationToken).ConfigureAwait(false))
            {
                return await PermitIfQualifiedAsync(request, actorLabel, token, grant, path, qualified, unblindConditions, cancellationToken).ConfigureAwait(false);
            }
        }

        if (token == "study.export" && path.Study is not null && carrying.Count > 0)
        {
            var grantPaths = await store.PathsOfAsync(
                request.Tenant,
                [.. carrying.Select(g => NodeRef.For(g.Id))],
                cancellationToken).ConfigureAwait(false);
            foreach (var grant in carrying)
            {
                var grantPath = grantPaths.GetValueOrDefault(NodeRef.For(grant.Id), ScopePath.TenantWide);
                if (grantPath.Study == path.Study && await InEffectAsync(request, grant, cancellationToken).ConfigureAwait(false))
                {
                    return await PermitIfQualifiedAsync(request, actorLabel, token, grant, grantPath, qualified, unblindConditions, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return AuthorityQualificationAndActorHelper.DenyMissingGrant(request.Actor, token, request.Resource, carrying);
    }

    private async ValueTask<EvaluatedCondition[]?> ResolveBlindedEvidenceConditionsAsync(
        AuthorizationRequest request,
        ScopePath evidenceScope,
        IReadOnlyList<Grant> held,
        IReadOnlyDictionary<RoleId, Role> roles,
        bool qualified,
        CancellationToken cancellationToken)
    {
        Grant? unblindGrant = null;
        foreach (var candidate in held.Where(g => AuthorityQualificationAndActorHelper.Carries(g, EnrollmentUnblindToken, roles) && g.Scope.Covers(evidenceScope)))
        {
            if (await InEffectAsync(request, candidate, cancellationToken).ConfigureAwait(false))
            {
                unblindGrant = candidate;
                break;
            }
        }

        if (unblindGrant is null)
        {
            return null;
        }

        IReadOnlyList<EvaluatedCondition> qualificationConditions = qualified
            ? await AuthorityQualificationAndActorHelper.QualificationConditionsAsync(store, request, EnrollmentUnblindToken, evidenceScope, cancellationToken).ConfigureAwait(false)
            : [];
        if (qualificationConditions.Any(c => !c.Met))
        {
            return null;
        }

        return
        [
            EvaluatedCondition.Satisfied(GrantCondition, $"Holds {EnrollmentUnblindToken} for {unblindGrant.Scope}", NodeRef.For(unblindGrant.Id)),
            .. qualificationConditions,
        ];
    }

    private async ValueTask<AuthorizationDecision> PermitIfQualifiedAsync(
        AuthorizationRequest request,
        string actorLabel,
        string token,
        Grant grant,
        ScopePath qualificationPath,
        bool qualified,
        EvaluatedCondition[] extraSatisfiedConditions,
        CancellationToken cancellationToken)
    {
        EvaluatedCondition[] conditions =
        [
            EvaluatedCondition.Satisfied(GrantCondition, $"Holds {token} for {grant.Scope}", NodeRef.For(grant.Id)),
            .. extraSatisfiedConditions,
            .. qualified
                ? await AuthorityQualificationAndActorHelper.QualificationConditionsAsync(store, request, token, qualificationPath, cancellationToken).ConfigureAwait(false)
                : [],
        ];
        return conditions.FirstOrDefault(c => !c.Met) is { } missing
            ? AuthorizationDecision.Deny($"{actorLabel} is not qualified to {token}: {missing.Description}.", conditions)
            : AuthorizationDecision.Permit(conditions: conditions);
    }

    private async Task<AuthorizationDecision?> DecideOnGrantAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        var target = await store.FindGrantAsync(request.Tenant, GrantId.FromGuid(request.Resource.Id), cancellationToken).ConfigureAwait(false);
        if (request.Capability.Token == "grant.delegate")
        {
            var delegable = target is not null
                && target.Grantee == request.Actor
                && target.WithGrantOption
                && await InEffectAsync(request, target, cancellationToken).ConfigureAwait(false);
            return delegable
                ? AuthorizationDecision.Permit(conditions: EvaluatedCondition.Satisfied(DelegationCondition, "Holds the delegating grant with the grant option", request.Resource))
                : AuthorizationDecision.Deny(
                    "Only the holder of a grant in effect with the grant option may delegate under it.",
                    EvaluatedCondition.Unsatisfied(DelegationCondition, "Hold the delegating grant, in effect, with the grant option", request.Resource));
        }

        if (target?.ParentGrantId is { } parentId
            && await store.FindGrantAsync(request.Tenant, parentId, cancellationToken).ConfigureAwait(false) is { } parent
            && parent.Grantee == request.Actor)
        {
            return AuthorizationDecision.Permit(
                conditions: EvaluatedCondition.Satisfied(DelegatorCondition, "Signed this delegation", request.Resource, NodeRef.For(parent.Id)));
        }

        return null;
    }

    private Task<bool> InEffectAsync(AuthorizationRequest request, Grant grant, CancellationToken cancellationToken) =>
        GrantChain.InEffectAsync(store, request.Tenant, grant, request.At, request.KnownAt, cancellationToken);
}
