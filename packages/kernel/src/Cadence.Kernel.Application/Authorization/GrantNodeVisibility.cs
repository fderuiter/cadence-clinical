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

        var readerActiveGrants = readerCandidates.Where(g => inEffect.Contains(g.Id)).ToList();
        if (readerActiveGrants.Count == 0)
        {
            return MaskAll(distinct);
        }

        var requesterActiveGrants = requesterCandidates?.Where(g => inEffect.Contains(g.Id)).ToList();
        if (requesterActiveGrants is { Count: 0 })
        {
            return MaskAll(distinct);
        }

        var readerGrants = readerActiveGrants.DistinctBy(g => g.Scope).ToList();
        var requesterGrants = requesterActiveGrants?.DistinctBy(g => g.Scope).ToList();

        var blindableNodes = distinct
            .Select(n => n.Unversioned())
            .Where(n => n.Kind is NodeKinds.Observation or NodeKinds.Assertion or NodeKinds.FormQuery)
            .Distinct()
            .ToList();
        var blindedByNode = blindableNodes.Count == 0
            ? new Dictionary<NodeRef, EnrollmentId>()
            : await store.BlindedNodeEnrollmentsAsync(tenant, blindableNodes, cancellationToken).ConfigureAwait(false);

        var readerHasTenant = readerGrants.Exists(g => g.Scope.Level == ScopeLevel.Tenant);
        var requesterHasTenant = requesterGrants is null || requesterGrants.Exists(g => g.Scope.Level == ScopeLevel.Tenant);
        if (blindedByNode.Count == 0 && readerHasTenant && requesterHasTenant)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var candidates = distinct
            .Select(n => n.Unversioned())
            .Where(n => !TenantMemberKinds.Contains(n.Kind))
            .Distinct()
            .ToList();
        if (candidates.Count == 0 && blindedByNode.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        List<Grant>? unblindGrants = null;
        Actor? unblindActor = null;
        if (blindedByNode.Count > 0)
        {
            unblindActor = agent ? reader.RequestedBy! : reader.AsIdentity();
            var unblindSourceGrants = agent ? requesterActiveGrants! : readerActiveGrants;
            unblindGrants = await FilterUnblindGrantsAsync(tenant, unblindSourceGrants, cancellationToken).ConfigureAwait(false);
        }

        var pathBatch = new HashSet<NodeRef>();
        if (!readerHasTenant || !requesterHasTenant)
        {
            foreach (var candidate in candidates)
            {
                pathBatch.Add(candidate);
            }
        }

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

        if (blindedByNode.Count > 0 && unblindGrants is { Count: > 0 })
        {
            foreach (var enrollmentId in blindedByNode.Values.Distinct())
            {
                pathBatch.Add(NodeRef.For(enrollmentId));
            }
        }

        var paths = pathBatch.Count == 0
            ? (IReadOnlyDictionary<NodeRef, ScopePath>)new Dictionary<NodeRef, ScopePath>()
            : await store.PathsOfAsync(tenant, pathBatch, cancellationToken).ConfigureAwait(false);

        var maskedObjects = new HashSet<NodeRef>();
        if (!readerHasTenant || !requesterHasTenant)
        {
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

        HashSet<EnrollmentId>? unblindedEnrollments = null;
        if (blindedByNode.Count > 0)
        {
            unblindedEnrollments = await EvaluateUnblindedEnrollmentsAsync(
                tenant,
                unblindActor!,
                now,
                unblindGrants!,
                blindedByNode.Values.Distinct(),
                paths,
                cancellationToken).ConfigureAwait(false);
        }

        var result = new Dictionary<NodeRef, MaskReason>();
        foreach (var node in distinct)
        {
            var unversioned = node.Unversioned();
            if (maskedObjects.Contains(unversioned))
            {
                result[node] = MaskReason.NotPermitted;
            }
            else if (blindedByNode.TryGetValue(unversioned, out var enrollmentId)
                && unblindedEnrollments is not null
                && !unblindedEnrollments.Contains(enrollmentId))
            {
                result[node] = MaskReason.Blinded;
            }
        }

        return result;
    }

    private const string EnrollmentUnblindToken = "enrollment.unblind";

    private async Task<List<Grant>> FilterUnblindGrantsAsync(
        TenantId tenant, List<Grant> activeGrants, CancellationToken cancellationToken)
    {
        var roleIds = activeGrants.Where(g => g.RoleId is not null).Select(g => g.RoleId!.Value).Distinct().ToList();
        var roles = roleIds.Count == 0
            ? new Dictionary<RoleId, Role>()
            : (await store.RolesAsync(tenant, roleIds, cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Id);

        return
        [
            .. activeGrants.Where(g =>
                string.Equals(g.Capability, EnrollmentUnblindToken, StringComparison.Ordinal)
                || (g.RoleId is { } roleId
                    && roles.TryGetValue(roleId, out var role)
                    && role.Capabilities.Contains(EnrollmentUnblindToken, StringComparer.Ordinal))),
        ];
    }

    private async Task<HashSet<EnrollmentId>> EvaluateUnblindedEnrollmentsAsync(
        TenantId tenant,
        Actor actor,
        DateTimeOffset now,
        List<Grant> unblindGrants,
        IEnumerable<EnrollmentId> enrollments,
        IReadOnlyDictionary<NodeRef, ScopePath> paths,
        CancellationToken cancellationToken)
    {
        var permitted = new HashSet<EnrollmentId>();
        if (unblindGrants.Count == 0)
        {
            return permitted;
        }

        var requirements = (await store.QualificationRequirementsAsync(tenant, EnrollmentUnblindToken, cancellationToken).ConfigureAwait(false))
            .Where(r => r.IsInEffect(now, now))
            .ToList();
        IReadOnlyList<Qualification>? qualificationsHeld = null;

        foreach (var enrollmentId in enrollments)
        {
            var enrollmentPath = paths.GetValueOrDefault(NodeRef.For(enrollmentId), ScopePath.TenantWide);
            if (!unblindGrants.Exists(g => g.Scope.Covers(enrollmentPath)))
            {
                continue;
            }

            var applicable = requirements.Where(r => r.Scope.Covers(enrollmentPath)).ToList();
            if (applicable.Count > 0)
            {
                qualificationsHeld ??= await store.QualificationsHeldAsync(tenant, actor, cancellationToken).ConfigureAwait(false);
                if (!applicable.All(req => qualificationsHeld.Any(q => req.IsMetBy(q) && q.IsInEffect(now, now))))
                {
                    continue;
                }
            }

            permitted.Add(enrollmentId);
        }

        return permitted;
    }

    /// <summary>
    /// Evaluates <paramref name="nodes"/> in one visibility pass and returns the unmasked subset <paramref name="reader"/> may see.
    /// </summary>
    public async ValueTask<IReadOnlySet<NodeRef>> FilterVisibleAsync(
        TenantId tenant,
        Actor reader,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(nodes);
        var distinct = nodes.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return FrozenSet<NodeRef>.Empty;
        }

        var masked = await MaskAsync(tenant, reader, distinct, cancellationToken).ConfigureAwait(false);
        return distinct.Where(node => !masked.ContainsKey(node)).ToHashSet();
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
