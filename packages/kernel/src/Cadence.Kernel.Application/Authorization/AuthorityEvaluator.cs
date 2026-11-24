using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    /// <summary>The sequential protocol version numbers (from 1) of the given protocol versions in the tenant.</summary>
    Task<IReadOnlyDictionary<ProtocolVersionId, int>> ProtocolVersionNumbersAsync(
        TenantId tenant, IReadOnlyCollection<ProtocolVersionId> versions, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<ProtocolVersionId, int>>(new Dictionary<ProtocolVersionId, int>());

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
public sealed partial class AuthorityEvaluator(IAuthorityStore store, KernelAuthorityOptions options) : IAuthorizationEvaluator
{
    private const string EnrollmentUnblindToken = "enrollment.unblind";

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

    /// <summary>
    /// An automation agent (spec §11, K-AI-1, K-AI-6, ADR-2632): it acts for exactly one requesting person, never
    /// executes or approves, and may do only what its own grants and the person's both allow. Its own grants are checked
    /// without qualification requirements, which describe a person's training; the person's evaluation checks them.
    /// </summary>
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
        var personLabel = DescribeActor(person, "the requesting person");
        var personRef = ActorEvidence(person);
        NodeRef[] requesterEvidence = personRef is { } pRef ? [pRef] : [];
        var bounded = forPerson.Permitted
            ? EvaluatedCondition.Satisfied(RequesterCondition, $"Acts for {personLabel}, who may {request.Capability.Token}", requesterEvidence)
            : EvaluatedCondition.Unsatisfied(RequesterCondition, $"Act for a person who may {request.Capability.Token}", requesterEvidence);
        EvaluatedCondition[] conditions = [.. own.Conditions, bounded, .. forPerson.Conditions];
        var agentLabel = DescribeActor(request.Actor, "The automation agent");
        return forPerson.Permitted
            ? AuthorizationDecision.Permit(conditions: conditions)
            : AuthorizationDecision.Deny($"{agentLabel} acts for {personLabel}, who may not {request.Capability.Token}: {forPerson.Reason}", conditions);
    }

    /// <summary>
    /// Builds non-confirming <see cref="GrantCondition"/> denials matching <see cref="EvaluateGrantsAsync"/> for capabilities
    /// probed against a resource hidden from <paramref name="actor"/>, loading the actor's grants at most once.
    /// </summary>
    /// <param name="store">The optional authority store used to inspect the actor's held grants.</param>
    /// <param name="tenant">The tenant scope of the dry run.</param>
    /// <param name="actor">The resolved actor asking the dry run.</param>
    /// <param name="capabilities">The capabilities to deny uniformly on <paramref name="resource"/>.</param>
    /// <param name="resource">The hidden or undetected resource being probed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask<IReadOnlyList<AuthorizationDecision>> DenyHiddenResourceCapabilitiesAsync(
        IAuthorityStore? store,
        TenantId tenant,
        Actor actor,
        IReadOnlyList<Capability> capabilities,
        NodeRef resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(resource);
        if (capabilities.Count == 0)
        {
            return [];
        }

        if (actor.Kind == ActorKind.AutomationAgent && actor.RequestedBy is null)
        {
            var missingRequester = AuthorizationDecision.Deny(
                "An automation agent acts only for the person who asked it (K-AI-6).",
                EvaluatedCondition.Unsatisfied(RequesterCondition, "Run the agent for the person it acts for"));
            return [.. capabilities.Select(_ => missingRequester)];
        }

        if (actor.Kind != ActorKind.AutomationAgent && actor.RequestedBy is not null)
        {
            var invalidRequester = AuthorizationDecision.Deny(
                "Only an automation agent acts for a requesting person.",
                EvaluatedCondition.Unsatisfied(RequesterCondition, "Act as yourself"));
            return [.. capabilities.Select(_ => invalidRequester)];
        }

        var grantee = actor.AsIdentity();
        IReadOnlyList<Grant> held = store is null
            ? []
            : await store.GrantsHeldAsync(tenant, grantee, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<RoleId, Role> roles = store is null || held.Count == 0
            ? new Dictionary<RoleId, Role>()
            : await RoleCapabilitiesFromStoreAsync(store, tenant, held, cancellationToken).ConfigureAwait(false);

        var decisions = new List<AuthorizationDecision>(capabilities.Count);
        foreach (var capability in capabilities)
        {
            if (actor.Kind == ActorKind.AutomationAgent && capability.VerbClass is VerbClass.Execute or VerbClass.Approve)
            {
                decisions.Add(AuthorizationDecision.Deny(
                    $"An automation agent cannot {capability.VerbClass.ToString().ToLowerInvariant()}; a person must.",
                    EvaluatedCondition.Unsatisfied(AgentVerbCondition, "Have a person perform this action")));
                continue;
            }

            var carrying = held.Where(g => Carries(g, capability.Token, roles)).ToList();
            decisions.Add(DenyMissingGrant(grantee, capability.Token, resource, carrying));
        }

        return decisions;
    }

    /// <summary>
    /// The grants and delegations <see cref="AuthorizationRequest.Actor"/> holds that carry the capability and cover the
    /// resource, in effect, and (when <paramref name="qualified"/>) the qualification requirements over it.
    /// </summary>
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

        var actorLabel = DescribeActor(request.Actor, "The signed-in user");
        var held = await store.GrantsHeldAsync(request.Tenant, request.Actor, cancellationToken).ConfigureAwait(false);
        var roles = await RoleCapabilitiesFromStoreAsync(store, request.Tenant, held, cancellationToken).ConfigureAwait(false);
        var carrying = held.Where(g => Carries(g, token, roles)).ToList();

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
                return DenyMissingGrant(request.Actor, token, request.Resource, carrying);
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
                    return await PermitIfQualifiedAsync(request, actorLabel, token, grant, grantPath, qualified, unblindConditions, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return DenyMissingGrant(request.Actor, token, request.Resource, carrying);
    }

    private static AuthorizationDecision DenyMissingGrant(
        Actor actor,
        string token,
        NodeRef resource,
        IReadOnlyList<Grant> carrying)
    {
        var actorLabel = DescribeActor(actor, "The signed-in user");
        var reason = carrying.Count == 0
            ? $"{actorLabel} holds no grant of {token}."
            : $"{actorLabel} holds no grant of {token} in effect for this {resource.Kind}.";
        NodeRef[] grantEvidence = ActorEvidence(actor) is { } actorNode
            ? [resource, actorNode, .. carrying.Select(g => NodeRef.For(g.Id))]
            : [resource, .. carrying.Select(g => NodeRef.For(g.Id))];
        return AuthorizationDecision.Deny(
            reason,
            EvaluatedCondition.Unsatisfied(
                GrantCondition,
                $"Obtain a grant or delegation of {token} covering this {resource.Kind}",
                grantEvidence));
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
        foreach (var candidate in held.Where(g => Carries(g, EnrollmentUnblindToken, roles) && g.Scope.Covers(evidenceScope)))
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
            ? await QualificationConditionsAsync(request, EnrollmentUnblindToken, evidenceScope, cancellationToken).ConfigureAwait(false)
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
            .. qualified ? await QualificationConditionsAsync(request, token, qualificationPath, cancellationToken).ConfigureAwait(false) : [],
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
        AuthorizationRequest request, string capabilityToken, ScopePath path, CancellationToken cancellationToken)
    {
        var requirements = (await store.QualificationRequirementsAsync(request.Tenant, capabilityToken, cancellationToken).ConfigureAwait(false))
            .Where(r => r.IsInEffect(request.At, request.KnownAt) && r.Scope.Covers(path))
            .OrderBy(r => r.Id)
            .ToList();
        if (requirements.Count == 0)
        {
            return [];
        }

        var versionIds = requirements
            .Where(r => r.ProtocolVersionId is not null)
            .Select(r => r.ProtocolVersionId!.Value)
            .Distinct()
            .ToList();
        IReadOnlyDictionary<ProtocolVersionId, int> versionNumbers = versionIds.Count == 0
            ? EmptyVersionNumbers
            : await store.ProtocolVersionNumbersAsync(request.Tenant, versionIds, cancellationToken).ConfigureAwait(false);

        var held = await store.QualificationsHeldAsync(request.Tenant, request.Actor, cancellationToken).ConfigureAwait(false);
        var conditions = new List<EvaluatedCondition>(requirements.Count);
        foreach (var requirement in requirements)
        {
            var matching = held.Where(requirement.IsMetBy).OrderBy(q => q.Id).ToList();
            var named = Named(requirement, versionNumbers);
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
            NodeRef[] missingEvidence = requirement.ProtocolVersionId is { } requiredVersion
                ? [NodeRef.For(requirement.Id), NodeRef.For(requiredVersion)]
                : [NodeRef.For(requirement.Id)];
            if (lapsed.Qualification is { } expired)
            {
                NodeRef[] expiredEvidence = requirement.ProtocolVersionId is { } expiredVersion
                    ? [NodeRef.For(requirement.Id), NodeRef.For(expired.Id), NodeRef.For(expiredVersion)]
                    : [NodeRef.For(requirement.Id), NodeRef.For(expired.Id)];
                conditions.Add(EvaluatedCondition.Unsatisfied(
                    QualificationCondition,
                    $"Renew {named}; it expired on {lapsed.Ended!.Value:yyyy-MM-dd}",
                    expiredEvidence));
            }
            else
            {
                conditions.Add(EvaluatedCondition.Unsatisfied(QualificationCondition, $"Obtain {named}", missingEvidence));
            }
        }

        return conditions;
    }

    private static readonly IReadOnlyDictionary<ProtocolVersionId, int> EmptyVersionNumbers =
        new Dictionary<ProtocolVersionId, int>();

    private static string Named(QualificationRequirement requirement, IReadOnlyDictionary<ProtocolVersionId, int> versionNumbers)
    {
        if (requirement.ProtocolVersionId is not { } version)
        {
            return requirement.QualificationCode;
        }

        return versionNumbers.TryGetValue(version, out var number)
            ? $"{requirement.QualificationCode} for protocol version {number}"
            : $"{requirement.QualificationCode} for the required protocol version";
    }

    private static string DescribeActor(Actor actor, string personFallback)
    {
        if (!UuidPattern().IsMatch(actor.Id))
        {
            return actor.Id;
        }

        return actor.Kind switch
        {
            ActorKind.ServiceAccount => "The service account",
            ActorKind.AutomationAgent => "The automation agent",
            _ => personFallback,
        };
    }

    private static NodeRef? ActorEvidence(Actor actor) =>
        actor.Kind == ActorKind.Person && Guid.TryParse(actor.Id, out var personGuid) && personGuid != Guid.Empty
            ? NodeRef.For(PersonId.FromGuid(personGuid))
            : null;

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex UuidPattern();

    private Task<bool> InEffectAsync(AuthorizationRequest request, Grant grant, CancellationToken cancellationToken) =>
        GrantChain.InEffectAsync(store, request.Tenant, grant, request.At, request.KnownAt, cancellationToken);

    private static async Task<IReadOnlyDictionary<RoleId, Role>> RoleCapabilitiesFromStoreAsync(
        IAuthorityStore store,
        TenantId tenant,
        IReadOnlyList<Grant> held,
        CancellationToken cancellationToken)
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
