using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
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
