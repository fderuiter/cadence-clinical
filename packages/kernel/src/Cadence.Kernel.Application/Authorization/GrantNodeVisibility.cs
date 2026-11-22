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
using Cadence.Kernel.Time;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Authorization;

/// <summary>
/// Decides what a reader may see from the grants it holds (ADR-2457, spec §9.3). Only grants in effect now count, each
/// together with every grant above it. Any capability counts, because authority to act at a scope includes seeing what
/// is there. The reader sees:
/// <list type="bullet">
/// <item>an object whose scope path a held scope covers;</item>
/// <item>an object above a held scope on the scope path: the organization, study and country of a study-site grant,
/// so a site's staff see their study and its protocol versions but not another site's objects;</item>
/// <item>a participant or site through the study sites it reaches (its enrollments, its study links);</item>
/// <item>tenant reference data, roles and the tenant's people, when it holds any grant in effect in the tenant.</item>
/// </list>
/// Everything else is masked <see cref="MaskReason.NotPermitted"/>, and a reader with no grant in effect sees nothing.
/// An object the kernel places on no scope path is visible only through a tenant-wide grant. An automation agent sees an
/// object only when both it and the person it acts for would (K-AI-6, ADR-2632), and nothing when it acts for no one. Blinding and the
/// IdentityVault (#4782) are not decided here yet.
/// </summary>
public sealed class GrantNodeVisibility(IAuthorityStore store, IClock clock) : INodeVisibility
{
    /// <summary>Kinds visible to every reader holding a grant in effect in the tenant: reference data and the tenant's people.</summary>
    public static readonly FrozenSet<string> TenantMemberKinds = FrozenSet.Create(
        StringComparer.Ordinal,
        NodeKinds.Tenant,
        NodeKinds.Person,
        NodeKinds.TenantMembership,
        NodeKinds.Role,
        NodeKinds.CodeSystem,
        NodeKinds.CodeSystemVersion,
        NodeKinds.Concept,
        NodeKinds.ConceptMapping,
        NodeKinds.StandardVersion,
        NodeKinds.LibraryItem);

    public async ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(nodes);
        var distinct = nodes.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        // An automation agent sees only what both it and the person it acts for may see; acting for nobody, nothing (K-AI-6).
        var agent = reader.Kind == ActorKind.AutomationAgent;
        if (agent && reader.RequestedBy is null)
        {
            return MaskAll(distinct);
        }

        var now = clock.UtcNow;
        var readerHeld = await store.GrantsHeldAsync(tenant, reader.AsIdentity(), cancellationToken).ConfigureAwait(false);
        var readerCandidates = readerHeld.Where(g => g.IsInEffect(now, now)).ToList();
        if (readerCandidates.Count == 0)
        {
            return MaskAll(distinct);
        }

        List<Grant>? requesterCandidates = null;
        if (agent && reader.RequestedBy is { } person)
        {
            var requesterHeld = await store.GrantsHeldAsync(tenant, person, cancellationToken).ConfigureAwait(false);
            requesterCandidates = requesterHeld.Where(g => g.IsInEffect(now, now)).ToList();
            if (requesterCandidates.Count == 0)
            {
                return MaskAll(distinct);
            }
        }

        IReadOnlyList<Grant> chainCandidates = requesterCandidates is null
            ? readerCandidates
            : [.. readerCandidates, .. requesterCandidates];
        var inEffect = await GrantChain.FilterInEffectAsync(store, tenant, chainCandidates, now, now, cancellationToken).ConfigureAwait(false);

        var readerInEffect = readerCandidates.Where(g => inEffect.Contains(g.Id)).ToList();
        var readerGrants = readerInEffect.DistinctBy(g => g.Scope).ToList();
        if (readerGrants.Count == 0)
        {
            return MaskAll(distinct);
        }

        var requesterInEffect = requesterCandidates?.Where(g => inEffect.Contains(g.Id)).ToList();
        var requesterGrants = requesterInEffect?.DistinctBy(g => g.Scope).ToList();
        if (requesterGrants is { Count: 0 })
        {
            return MaskAll(distinct);
        }

        var readerHasTenant = readerGrants.Exists(g => g.Scope.Level == ScopeLevel.Tenant);
        var requesterHasTenant = requesterGrants is null || requesterGrants.Exists(g => g.Scope.Level == ScopeLevel.Tenant);
        var hasObservations = distinct.Exists(n => n.Kind == NodeKinds.Observation);
        if (readerHasTenant && requesterHasTenant && !hasObservations)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var candidates = distinct
            .Select(n => n.Unversioned())
            .Where(n => !TenantMemberKinds.Contains(n.Kind))
            .Distinct()
            .ToList();
        if (candidates.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        IReadOnlyDictionary<NodeRef, ScopePath>? paths = null;
        var maskedObjects = new HashSet<NodeRef>();
        if (!readerHasTenant || !requesterHasTenant)
        {
            var pathBatch = new HashSet<NodeRef>(candidates);
            if (!readerHasTenant)
            {
                foreach (var grant in readerGrants)
                {
                    pathBatch.Add(NodeRef.For(grant.Id));
                }
            }

            if (requesterGrants is not null && !requesterHasTenant)
            {
                foreach (var grant in requesterGrants)
                {
                    pathBatch.Add(NodeRef.For(grant.Id));
                }
            }

            paths = await store.PathsOfAsync(tenant, pathBatch, cancellationToken).ConfigureAwait(false);
            var held = readerHasTenant ? null : BuildHeldScopes(readerGrants, paths);
            var requester = requesterGrants is null || requesterHasTenant ? null : BuildHeldScopes(requesterGrants, paths);

            var uncoveredReachable = new List<NodeRef>();
            foreach (var node in candidates)
            {
                var path = paths.GetValueOrDefault(node, ScopePath.TenantWide);
                if (CoversPath(held, path) && CoversPath(requester, path))
                {
                    continue;
                }

                if (node.Kind is NodeKinds.Participant or NodeKinds.Site)
                {
                    uncoveredReachable.Add(node);
                }
                else
                {
                    maskedObjects.Add(node);
                }
            }

            if (uncoveredReachable.Count > 0)
            {
                var reachedByNode = await store.StudySitePathsAsync(tenant, uncoveredReachable, cancellationToken).ConfigureAwait(false);
                foreach (var node in uncoveredReachable)
                {
                    var path = paths.GetValueOrDefault(node, ScopePath.TenantWide);
                    var reached = reachedByNode.GetValueOrDefault(node, []);
                    var visibleToReader = CoversPath(held, path) || CoversReached(held, reached);
                    var visibleToRequester = CoversPath(requester, path) || CoversReached(requester, reached);
                    if (!visibleToReader || !visibleToRequester)
                    {
                        maskedObjects.Add(node);
                    }
                }
            }
        }

        var blindedObjects = new HashSet<NodeRef>();
        if (hasObservations)
        {
            var visibleObservationNodes = candidates
                .Where(n => n.Kind == NodeKinds.Observation && !maskedObjects.Contains(n))
                .ToList();
            if (visibleObservationNodes.Count > 0)
            {
                var blindedIds = await store.BlindedObservationsAsync(
                    tenant,
                    [.. visibleObservationNodes.Select(n => ObservationId.FromGuid(n.Id))],
                    cancellationToken).ConfigureAwait(false);
                if (blindedIds.Count > 0)
                {
                    var blindedNodes = visibleObservationNodes
                        .Where(n => blindedIds.Contains(ObservationId.FromGuid(n.Id)))
                        .ToList();
                    paths ??= await store.PathsOfAsync(tenant, blindedNodes, cancellationToken).ConfigureAwait(false);
                    var roles = await LoadRolesAsync(store, tenant, chainCandidates, cancellationToken).ConfigureAwait(false);
                    var unblindReqs = await store.QualificationRequirementsAsync(tenant, "enrollment.unblind", cancellationToken).ConfigureAwait(false);
                    var activeUnblindReqs = unblindReqs.Where(r => r.IsInEffect(now, now)).ToList();
                    Actor? qualifyingPerson = agent ? reader.RequestedBy : reader;
                    IReadOnlyList<Qualification> heldQualifications = activeUnblindReqs.Count > 0 && qualifyingPerson is not null
                        ? await store.QualificationsHeldAsync(tenant, qualifyingPerson, cancellationToken).ConfigureAwait(false)
                        : [];

                    foreach (var blindedNode in blindedNodes)
                    {
                        var obsPath = paths.GetValueOrDefault(blindedNode, ScopePath.TenantWide);
                        var readerMayUnblind = CarriesCapabilityAtPath(readerInEffect, roles, "enrollment.unblind", obsPath);
                        var requesterMayUnblind = requesterInEffect is null
                            || CarriesCapabilityAtPath(requesterInEffect, roles, "enrollment.unblind", obsPath);
                        var qualifiedToUnblind = MeetsQualifications(activeUnblindReqs, heldQualifications, obsPath, now);
                        if (!readerMayUnblind || !requesterMayUnblind || !qualifiedToUnblind)
                        {
                            blindedObjects.Add(blindedNode);
                        }
                    }
                }
            }
        }

        if (maskedObjects.Count == 0 && blindedObjects.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var masks = new Dictionary<NodeRef, MaskReason>();
        foreach (var node in distinct)
        {
            var unversioned = node.Unversioned();
            if (maskedObjects.Contains(unversioned))
            {
                masks[node] = MaskReason.NotPermitted;
            }
            else if (blindedObjects.Contains(unversioned))
            {
                masks[node] = MaskReason.Blinded;
            }
        }

        return masks;
    }

    /// <summary>True when <paramref name="path"/> is on the way from the tenant down to <paramref name="below"/>, or is it.</summary>
    public static bool IsAtOrAbove(ScopePath path, ScopePath below)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(below);
        return path.Organization is not null
            && path.Organization == below.Organization
            && (path.Study is null || path.Study == below.Study)
            && (path.Country is null || string.Equals(path.Country, below.Country, StringComparison.Ordinal))
            && (path.StudySite is null || path.StudySite == below.StudySite);
    }

    private static Dictionary<NodeRef, MaskReason> MaskAll(List<NodeRef> distinct) =>
        distinct.ToDictionary(n => n, _ => MaskReason.NotPermitted);

    private static List<HeldScope> BuildHeldScopes(List<Grant> grants, IReadOnlyDictionary<NodeRef, ScopePath> paths) =>
        [.. grants.Select(g => new HeldScope(g.Scope, paths.GetValueOrDefault(NodeRef.For(g.Id), ScopePath.TenantWide)))];

    private static bool CoversPath(IReadOnlyList<HeldScope>? held, ScopePath path) =>
        held is null || held.Any(h => h.Scope.Covers(path) || IsAtOrAbove(path, h.Path));

    private static bool CoversReached(IReadOnlyList<HeldScope>? held, IReadOnlyList<ScopePath> reached) =>
        held is null || reached.Any(p => held.Any(h => h.Scope.Covers(p)));

    private static async Task<IReadOnlyDictionary<RoleId, Role>> LoadRolesAsync(
        IAuthorityStore store,
        TenantId tenant,
        IReadOnlyList<Grant> grants,
        CancellationToken cancellationToken)
    {
        var roleIds = grants.Where(g => g.RoleId is not null).Select(g => g.RoleId!.Value).Distinct().ToList();
        if (roleIds.Count == 0)
        {
            return new Dictionary<RoleId, Role>();
        }

        var roles = await store.RolesAsync(tenant, roleIds, cancellationToken).ConfigureAwait(false);
        return roles.ToDictionary(r => r.Id);
    }

    private static bool CarriesCapabilityAtPath(
        IReadOnlyList<Grant> grants,
        IReadOnlyDictionary<RoleId, Role> roles,
        string capability,
        ScopePath path) =>
        grants.Any(g =>
            g.Scope.Covers(path)
            && (string.Equals(g.Capability, capability, StringComparison.Ordinal)
                || (g.RoleId is { } roleId && roles.TryGetValue(roleId, out var role) && role.Capabilities.Contains(capability, StringComparer.Ordinal))));

    private static bool MeetsQualifications(
        IReadOnlyList<QualificationRequirement> activeRequirements,
        IReadOnlyList<Qualification> heldQualifications,
        ScopePath path,
        DateTimeOffset now)
    {
        foreach (var req in activeRequirements.Where(r => r.Scope.Covers(path)))
        {
            var satisfied = heldQualifications.Any(q =>
                string.Equals(q.Code, req.QualificationCode, StringComparison.Ordinal)
                && (req.ProtocolVersionId is null || q.ProtocolVersionId == req.ProtocolVersionId)
                && q.IsInEffect(now, now));
            if (!satisfied)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record HeldScope(AuthorityScope Scope, ScopePath Path);
}

/// <summary>Registers the grant-backed visibility.</summary>
public static class GrantNodeVisibilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GrantNodeVisibility"/> as the host's <see cref="INodeVisibility"/>. Kernel persistence
    /// registers the <see cref="IAuthorityStore"/> it reads; the host registers the <see cref="IClock"/>.
    /// </summary>
    public static IServiceCollection AddKernelGrantVisibility(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<INodeVisibility, GrantNodeVisibility>();
        return services;
    }
}
