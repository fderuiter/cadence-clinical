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
using Cadence.Kernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Authorization;

/// <summary>
/// What the authority evaluator reads (spec §9.3). Kernel persistence implements it over the grant tables, under the
/// tenant's Row-Level Security.
/// </summary>
public interface IAuthorityStore
{
    /// <summary>Every grant whose grantee is <paramref name="actor"/>, with its whole validity history.</summary>
    Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken);

    /// <summary>One grant with its validity history, or null.</summary>
    Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken);

    /// <summary>The grants with the given ids that exist in the tenant, each with its whole validity history.</summary>
    async Task<IReadOnlyDictionary<GrantId, Grant>> FindGrantsAsync(
        TenantId tenant, IReadOnlyCollection<GrantId> grants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);
        var found = new Dictionary<GrantId, Grant>();
        foreach (var id in grants.Distinct())
        {
            if (await FindGrantAsync(tenant, id, cancellationToken).ConfigureAwait(false) is { } grant)
            {
                found[id] = grant;
            }
        }

        return found;
    }

    /// <summary>The roles with the given ids that exist in the tenant.</summary>
    Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken);

    /// <summary>
    /// Where <paramref name="resource"/> sits on the scope path. An object that does not exist, or belongs to no
    /// organization or study, has <see cref="ScopePath.TenantWide"/>, which only tenant-wide grants cover.
    /// </summary>
    Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken);

    /// <summary>
    /// Where each resource in <paramref name="resources"/> sits on the scope path. An object that does not exist, or
    /// belongs to no organization or study, has <see cref="ScopePath.TenantWide"/>, which only tenant-wide grants cover.
    /// </summary>
    async Task<IReadOnlyDictionary<NodeRef, ScopePath>> PathsOfAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var distinct = resources.Distinct().ToList();
        var byObject = new Dictionary<NodeRef, ScopePath>();
        foreach (var unversioned in distinct.Select(r => r.Unversioned()).Distinct())
        {
            byObject[unversioned] = await PathOfAsync(tenant, unversioned, cancellationToken).ConfigureAwait(false);
        }

        return distinct.ToDictionary(r => r, r => byObject[r.Unversioned()]);
    }

    /// <summary>
    /// Resolves scope paths for <paramref name="resources"/> together with the subset of unversioned evidence nodes that
    /// may classify as blinded treatment data (<c>null</c> when the store has not pre-filtered evidence nodes, in which
    /// case callers inspect all scope-visible evidence candidates via <see cref="InspectEvidenceNodesAsync"/>).
    /// </summary>
    async Task<(IReadOnlyDictionary<NodeRef, ScopePath> Paths, IReadOnlySet<NodeRef>? PotentiallyBlindedEvidence)> ResolveVisibilityPathsAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken) =>
        (await PathsOfAsync(tenant, resources, cancellationToken).ConfigureAwait(false), null);

    /// <summary>
    /// The scope paths of the study sites reached by an object that belongs to no study itself: the study sites where a
    /// participant has an enrollment, or where a site takes part in a study. Empty for any other object.
    /// </summary>
    Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken);

    /// <summary>
    /// The scope paths of the study sites reached by each object in <paramref name="nodes"/> that belongs to no study
    /// itself: the study sites where a participant has an enrollment, or where a site takes part in a study.
    /// </summary>
    async Task<IReadOnlyDictionary<NodeRef, IReadOnlyList<ScopePath>>> StudySitePathsAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var distinct = nodes.Distinct().ToList();
        var byObject = new Dictionary<NodeRef, IReadOnlyList<ScopePath>>();
        foreach (var unversioned in distinct.Select(n => n.Unversioned()).Distinct())
        {
            byObject[unversioned] = await StudySitePathsAsync(tenant, unversioned, cancellationToken).ConfigureAwait(false);
        }

        return distinct.ToDictionary(n => n, n => byObject[n.Unversioned()]);
    }

    /// <summary>Every qualification held by <paramref name="actor"/>, with its whole validity history (K-AUTH-5).</summary>
    Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken);

    /// <summary>Every requirement defined for <paramref name="capability"/>, with its whole validity history.</summary>
    Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the scope path and treatment-blinding classification (<c>K-AUTH-4</c>) of existing <c>observation</c>,
    /// <c>validation_finding</c>, <c>assertion</c>, and <c>form_query</c> nodes in <paramref name="nodes"/>. Nodes that
    /// do not exist in <paramref name="tenant"/> or are of other kinds are omitted from the returned dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>> InspectEvidenceNodesAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return Task.FromResult<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>>(new Dictionary<NodeRef, EvidenceNodeClassification>());
    }

    /// <summary>
    /// Resolves scope paths for <paramref name="resources"/> together with treatment-blinding classifications (<c>K-AUTH-4</c>)
    /// for any <c>observation</c>, <c>validation_finding</c>, <c>assertion</c>, and <c>form_query</c> nodes in the same batch.
    /// </summary>
    async Task<(IReadOnlyDictionary<NodeRef, ScopePath> Paths, IReadOnlyDictionary<NodeRef, EvidenceNodeClassification> Evidence)> ResolveVisibilityBatchAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var paths = await PathsOfAsync(tenant, resources, cancellationToken).ConfigureAwait(false);
        var evidenceCandidates = resources
            .Select(r => r.Unversioned())
            .Where(r => r.Kind is NodeKinds.Observation or NodeKinds.ValidationFinding or NodeKinds.Assertion or NodeKinds.FormQuery)
            .Distinct()
            .ToList();
        var evidence = evidenceCandidates.Count == 0
            ? new Dictionary<NodeRef, EvidenceNodeClassification>()
            : await InspectEvidenceNodesAsync(tenant, evidenceCandidates, cancellationToken).ConfigureAwait(false);
        return (paths, evidence);
    }
}

/// <summary>
/// Resolved scope path and treatment-blinding status of an <c>observation</c>, <c>validation_finding</c>,
/// <c>assertion</c>, or <c>form_query</c> node (K-AUTH-4).
/// </summary>
/// <param name="Scope">Where the governing enrollment or study sits on the scope path.</param>
/// <param name="IsBlinded">True when reading the node reveals blinded treatment allocation or dispensation.</param>
public sealed record EvidenceNodeClassification(ScopePath Scope, bool IsBlinded);

/// <summary>Settings of the authority evaluator.</summary>
/// <param name="PlatformOperators">
/// Service-account ids (<c>svc:…</c>) that operate the platform. They may provision tenants and issue the first grants
/// and roles in any tenant, and nothing else; everyone else needs a grant.
/// </param>
public sealed record KernelAuthorityOptions(IReadOnlySet<string> PlatformOperators)
{
    /// <summary>Dedicated service account for self-service trial workspace onboarding.</summary>
    public static readonly Actor TrialOnboardingOperator = Actor.ServiceAccount("kernel-trial-onboarding");

    /// <summary>Dedicated service account for commercial billing operations.</summary>
    public static readonly Actor CommercialBillingOperator = CommercialOperators.BillingOperator;

    /// <summary>The capabilities a platform operator holds without a grant.</summary>
    public static readonly FrozenSet<string> PlatformCapabilities = FrozenSet.Create(StringComparer.Ordinal, "tenant.provision", "grant.issue", "role.define");

    /// <summary>The commercial subscription capabilities held exclusively by <see cref="CommercialBillingOperator"/> when registered as a host operator.</summary>
    public static readonly FrozenSet<string> CommercialBillingCapabilities = FrozenSet.Create(
        StringComparer.Ordinal,
        "subscription.provision",
        "subscription.activate",
        "subscription.expire",
        "entitlement.grant",
        "entitlement.revoke");
}

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
/// </list>
/// Two rules are about grants themselves: the holder of a grant with the grant option may delegate under it, and a
/// delegator may revoke a delegation it signed. Every decision lists the conditions checked with their evidence
/// (K-AUTH-6); a missing qualification is named, with the one that lapsed when there is one. Separation of duties is a
/// later slice of #4781.
/// </summary>
public sealed class AuthorityEvaluator(IAuthorityStore store, KernelAuthorityOptions options) : IAuthorizationEvaluator
{
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

    public async ValueTask<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Actor.Kind == ActorKind.AutomationAgent)
        {
            return await EvaluateAgentAsync(request, cancellationToken).ConfigureAwait(false);
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

        return await EvaluateGrantsAsync(request, qualified: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// An automation agent (spec §11, K-AI-1, K-AI-6, ADR-2632): it acts for exactly one requesting person, never
    /// executes or approves, and may do only what its own grants and the person's both allow. Its own grants are checked
    /// without qualification requirements, which describe a person's training; the person's evaluation checks them.
    /// </summary>
    private async ValueTask<AuthorizationDecision> EvaluateAgentAsync(AuthorizationRequest request, CancellationToken cancellationToken)
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

        var own = await EvaluateGrantsAsync(request with { Actor = request.Actor.AsIdentity() }, qualified: false, cancellationToken).ConfigureAwait(false);
        if (!own.Permitted)
        {
            return own;
        }

        var forPerson = await EvaluateAsync(request with { Actor = person }, cancellationToken).ConfigureAwait(false);
        var bounded = forPerson.Permitted
            ? EvaluatedCondition.Satisfied(RequesterCondition, $"Acts for {person.Id}, who may {request.Capability.Token}")
            : EvaluatedCondition.Unsatisfied(RequesterCondition, $"Act for a person who may {request.Capability.Token}");
        EvaluatedCondition[] conditions = [.. own.Conditions, bounded, .. forPerson.Conditions];
        return forPerson.Permitted
            ? AuthorizationDecision.Permit(conditions: conditions)
            : AuthorizationDecision.Deny($"{request.Actor.Id} acts for {person.Id}, who may not {request.Capability.Token}: {forPerson.Reason}", conditions);
    }

    /// <summary>
    /// The grants and delegations <see cref="AuthorizationRequest.Actor"/> holds that carry the capability and cover the
    /// resource, in effect, and (when <paramref name="qualified"/>) the qualification requirements over it.
    /// </summary>
    private async ValueTask<AuthorizationDecision> EvaluateGrantsAsync(AuthorizationRequest request, bool qualified, CancellationToken cancellationToken)
    {
        var token = request.Capability.Token;
        var path = await store.PathOfAsync(request.Tenant, request.Resource, cancellationToken).ConfigureAwait(false);
        var held = await store.GrantsHeldAsync(request.Tenant, request.Actor, cancellationToken).ConfigureAwait(false);
        var roles = await RoleCapabilitiesAsync(request.Tenant, held, cancellationToken).ConfigureAwait(false);

        var carrying = held.Where(g => Carries(g, token, roles)).ToList();
        foreach (var grant in carrying.Where(g => g.Scope.Covers(path)))
        {
            if (await InEffectAsync(request, grant, cancellationToken).ConfigureAwait(false))
            {
                return await PermitIfQualifiedAsync(request, token, grant, path, qualified, cancellationToken).ConfigureAwait(false);
            }
        }

        if (token is "study.export" or "enrollment.export" && path.Study is not null && carrying.Count > 0)
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
                    return await PermitIfQualifiedAsync(request, token, grant, grantPath, qualified, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        var reason = carrying.Count == 0
            ? $"{request.Actor.Id} holds no grant of {token}."
            : $"{request.Actor.Id} holds no grant of {token} in effect for this {request.Resource.Kind}.";
        return AuthorizationDecision.Deny(
            reason,
            EvaluatedCondition.Unsatisfied(
                GrantCondition,
                $"Obtain a grant or delegation of {token} covering this {request.Resource.Kind}",
                [request.Resource, .. carrying.Select(g => NodeRef.For(g.Id))]));
    }

    private async ValueTask<AuthorizationDecision> PermitIfQualifiedAsync(
        AuthorizationRequest request,
        string token,
        Grant grant,
        ScopePath qualificationPath,
        bool qualified,
        CancellationToken cancellationToken)
    {
        EvaluatedCondition[] conditions =
        [
            EvaluatedCondition.Satisfied(GrantCondition, $"Holds {token} for {grant.Scope}", NodeRef.For(grant.Id)),
            .. qualified ? await QualificationConditionsAsync(request, qualificationPath, cancellationToken).ConfigureAwait(false) : [],
        ];
        return conditions.FirstOrDefault(c => !c.Met) is { } missing
            ? AuthorizationDecision.Deny($"{request.Actor.Id} is not qualified to {token}: {missing.Description}.", conditions)
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

        // A delegator may revoke what it delegated; anyone else needs grant.revoke over the grant's scope.
        if (target?.ParentGrantId is { } parentId
            && await store.FindGrantAsync(request.Tenant, parentId, cancellationToken).ConfigureAwait(false) is { } parent
            && parent.Grantee == request.Actor)
        {
            return AuthorizationDecision.Permit(
                conditions: EvaluatedCondition.Satisfied(DelegatorCondition, "Signed this delegation", request.Resource, NodeRef.For(parent.Id)));
        }

        return null;
    }

    /// <summary>
    /// One condition per qualification requirement for the capability that is in effect over <paramref name="path"/>:
    /// met by a qualification the actor holds in effect at the same instant, or unmet naming what to obtain and, when
    /// one lapsed, which and when (K-AUTH-6).
    /// </summary>
    private async Task<IReadOnlyList<EvaluatedCondition>> QualificationConditionsAsync(
        AuthorizationRequest request, ScopePath path, CancellationToken cancellationToken)
    {
        var requirements = (await store.QualificationRequirementsAsync(request.Tenant, request.Capability.Token, cancellationToken).ConfigureAwait(false))
            .Where(r => r.IsInEffect(request.At, request.KnownAt) && r.Scope.Covers(path))
            .OrderBy(r => r.Id)
            .ToList();
        if (requirements.Count == 0)
        {
            return [];
        }

        var held = await store.QualificationsHeldAsync(request.Tenant, request.Actor, cancellationToken).ConfigureAwait(false);
        var conditions = new List<EvaluatedCondition>(requirements.Count);
        foreach (var requirement in requirements)
        {
            var matching = held.Where(requirement.IsMetBy).OrderBy(q => q.Id).ToList();
            var named = Named(requirement);
            if (matching.FirstOrDefault(q => q.IsInEffect(request.At, request.KnownAt)) is { } current)
            {
                conditions.Add(EvaluatedCondition.Satisfied(
                    QualificationCondition, $"Holds current {named}", NodeRef.For(requirement.Id), NodeRef.For(current.Id), NodeRef.For(current.EvidenceArtifactVersionId)));
                continue;
            }

            var lapsed = matching
                .Select(q => (Qualification: q, Ended: q.LapsedBy(request.At, request.KnownAt)))
                .Where(x => x.Ended is not null)
                .OrderByDescending(x => x.Ended)
                .FirstOrDefault();
            conditions.Add(lapsed.Qualification is { } expired
                ? EvaluatedCondition.Unsatisfied(
                    QualificationCondition,
                    $"Renew {named}; it expired on {lapsed.Ended!.Value:yyyy-MM-dd}",
                    NodeRef.For(requirement.Id), NodeRef.For(expired.Id))
                : EvaluatedCondition.Unsatisfied(QualificationCondition, $"Obtain {named}", NodeRef.For(requirement.Id)));
        }

        return conditions;
    }

    private static string Named(QualificationRequirement requirement) =>
        requirement.ProtocolVersionId is { } version
            ? $"{requirement.QualificationCode} for protocol version {version}"
            : requirement.QualificationCode;

    private Task<bool> InEffectAsync(AuthorizationRequest request, Grant grant, CancellationToken cancellationToken) =>
        GrantChain.InEffectAsync(store, request.Tenant, grant, request.At, request.KnownAt, cancellationToken);

    private async Task<IReadOnlyDictionary<RoleId, Role>> RoleCapabilitiesAsync(TenantId tenant, IReadOnlyList<Grant> held, CancellationToken cancellationToken)
    {
        var ids = held.Where(g => g.RoleId is not null).Select(g => g.RoleId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<RoleId, Role>();
        }

        var roles = await store.RolesAsync(tenant, ids, cancellationToken).ConfigureAwait(false);
        return roles.ToDictionary(r => r.Id);
    }

    private static bool Carries(Grant grant, string token, IReadOnlyDictionary<RoleId, Role> roles) =>
        string.Equals(grant.Capability, token, StringComparison.Ordinal)
        || (grant.RoleId is { } roleId && roles.TryGetValue(roleId, out var role) && role.Capabilities.Contains(token, StringComparer.Ordinal));
}

/// <summary>Checks a grant together with every grant above it (K-AUTH-3).</summary>
internal static class GrantChain
{
    /// <summary>
    /// True when <paramref name="grant"/> and every grant above it are in effect at <paramref name="at"/> as known at
    /// <paramref name="knownAt"/>. A chain deeper than <see cref="AuthorityEvaluator.MaxDelegationDepth"/>, or one
    /// naming a parent that does not exist, is not in effect.
    /// </summary>
    public static async Task<bool> InEffectAsync(
        IAuthorityStore store, TenantId tenant, Grant grant, DateTimeOffset at, DateTimeOffset knownAt, CancellationToken cancellationToken)
    {
        var current = grant;
        for (var depth = 0; depth <= AuthorityEvaluator.MaxDelegationDepth; depth++)
        {
            if (!current.IsInEffect(at, knownAt))
            {
                return false;
            }

            if (current.ParentGrantId is not { } parentId)
            {
                return true;
            }

            if (await store.FindGrantAsync(tenant, parentId, cancellationToken).ConfigureAwait(false) is not { } parent)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>
    /// Returns the ids of grants in <paramref name="grants"/> that are in effect at <paramref name="at"/> as known at
    /// <paramref name="knownAt"/> together with every grant above them, fetching parent grants in bulk per delegation
    /// level up to <see cref="AuthorityEvaluator.MaxDelegationDepth"/> (K-AUTH-3).
    /// </summary>
    public static async Task<IReadOnlySet<GrantId>> FilterInEffectAsync(
        IAuthorityStore store,
        TenantId tenant,
        IReadOnlyList<Grant> grants,
        DateTimeOffset at,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        var active = grants.Where(g => g.IsInEffect(at, knownAt)).ToList();
        if (active.Count == 0)
        {
            return FrozenSet<GrantId>.Empty;
        }

        var byId = new Dictionary<GrantId, Grant>(grants.Count);
        foreach (var grant in grants)
        {
            byId.TryAdd(grant.Id, grant);
        }

        var queried = new HashSet<GrantId>(byId.Keys);
        var frontier = active
            .Where(g => g.ParentGrantId is not null)
            .Select(g => g.ParentGrantId!.Value)
            .Distinct()
            .ToList();

        for (var depth = 0; depth < AuthorityEvaluator.MaxDelegationDepth && frontier.Count > 0; depth++)
        {
            var missing = frontier.Where(id => !queried.Contains(id)).ToList();
            if (missing.Count > 0)
            {
                queried.UnionWith(missing);
                var fetched = await store.FindGrantsAsync(tenant, missing, cancellationToken).ConfigureAwait(false);
                foreach (var (id, parent) in fetched)
                {
                    byId[id] = parent;
                }
            }

            var next = new HashSet<GrantId>();
            foreach (var parentId in frontier)
            {
                if (byId.TryGetValue(parentId, out var parent)
                    && parent.IsInEffect(at, knownAt)
                    && parent.ParentGrantId is { } grandParentId)
                {
                    next.Add(grandParentId);
                }
            }

            frontier = [.. next];
        }

        return active
            .Where(grant => IsChainInEffect(grant, byId, at, knownAt))
            .Select(grant => grant.Id)
            .ToHashSet();
    }

    private static bool IsChainInEffect(Grant grant, IReadOnlyDictionary<GrantId, Grant> byId, DateTimeOffset at, DateTimeOffset knownAt)
    {
        var current = grant;
        for (var depth = 0; depth <= AuthorityEvaluator.MaxDelegationDepth; depth++)
        {
            if (!current.IsInEffect(at, knownAt))
            {
                return false;
            }

            if (current.ParentGrantId is not { } parentId)
            {
                return true;
            }

            if (!byId.TryGetValue(parentId, out var parent))
            {
                return false;
            }

            current = parent;
        }

        return false;
    }
}

/// <summary>Registers the authority evaluator.</summary>
public static class AuthorityEvaluatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AuthorityEvaluator"/> as the <see cref="IAuthorizationEvaluator"/> behind
    /// <c>AddKernelAuthorization()</c>. Kernel persistence registers the <see cref="IAuthorityStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="platformOperators">Service-account ids (<c>svc:…</c>) allowed to bootstrap tenants.</param>
    public static IServiceCollection AddKernelAuthorityEvaluator(this IServiceCollection services, IEnumerable<string>? platformOperators = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var operators = (platformOperators ?? []).ToFrozenSet(StringComparer.Ordinal);
        if (operators.FirstOrDefault(id => !id.StartsWith(Actor.ServiceAccountPrefix, StringComparison.Ordinal)) is { } invalid)
        {
            throw new ArgumentException($"Platform operator '{invalid}' is not a service account ({Actor.ServiceAccountPrefix}…).", nameof(platformOperators));
        }

        services.TryAddSingleton(new KernelAuthorityOptions(operators));
        services.TryAddScoped<IAuthorizationEvaluator, AuthorityEvaluator>();
        return services;
    }
}
