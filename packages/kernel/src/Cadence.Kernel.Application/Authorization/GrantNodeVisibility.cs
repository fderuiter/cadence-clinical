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
/// Decides what a reader may see from the grants it holds (ADR-2457, spec §9.3, K-AUTH-4). Only grants in effect now
/// count, each together with every grant above it. Any capability counts for scope visibility, because authority to act
/// at a scope includes seeing what is there. The reader sees:
/// <list type="bullet">
/// <item>an object whose scope path a held scope covers;</item>
/// <item>an object above a held scope on the scope path: the organization, study and country of a study-site grant,
/// so a site's staff see their study and its protocol versions but not another site's objects;</item>
/// <item>a participant or site through the study sites it reaches (its enrollments, its study links);</item>
/// <item>tenant reference data, roles and the tenant's people, when it holds any grant in effect in the tenant.</item>
/// </list>
/// Everything out of scope is masked <see cref="MaskReason.NotPermitted"/>, and a reader with no grant in effect sees
/// nothing. Scope-visible <c>observation</c> and <c>validation_finding</c> nodes that classify as blinded treatment data
/// are masked <see cref="MaskReason.Blinded"/> unless the reader (and, for an automation agent under K-AI-6, the
/// requesting person together with any required K-AUTH-5 qualifications) holds <c>enrollment.unblind</c> covering the
/// node's scope path. The IdentityVault (#4782) is enforced separately.
/// </summary>
public sealed class GrantNodeVisibility(IAuthorityStore store, IClock clock) : INodeVisibility
{
    private const string EnrollmentUnblindToken = "enrollment.unblind";

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
        NodeKinds.LibraryItem,
        NodeKinds.TaskConfiguration);

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
        if ((agent && reader.RequestedBy is null) || (!agent && reader.RequestedBy is not null))
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
        if (readerInEffect.Count == 0)
        {
            return MaskAll(distinct);
        }

        var requesterInEffect = requesterCandidates?.Where(g => inEffect.Contains(g.Id)).ToList();
        if (requesterInEffect is { Count: 0 })
        {
            return MaskAll(distinct);
        }

        var readerGrants = readerInEffect.DistinctBy(g => g.Scope).ToList();
        var requesterGrants = requesterInEffect?.DistinctBy(g => g.Scope).ToList();

        var evidenceCandidates = distinct
            .Select(n => n.Unversioned())
            .Where(n => n.Kind is NodeKinds.Observation or NodeKinds.ValidationFinding)
            .Distinct()
            .ToList();

        var readerHasTenant = readerGrants.Exists(g => g.Scope.Level == ScopeLevel.Tenant);
        var requesterHasTenant = requesterGrants is null || requesterGrants.Exists(g => g.Scope.Level == ScopeLevel.Tenant);
        if (readerHasTenant && requesterHasTenant && evidenceCandidates.Count == 0)
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

        var evidenceInfo = evidenceCandidates.Count == 0
            ? new Dictionary<NodeRef, EvidenceNodeClassification>()
            : await store.InspectEvidenceNodesAsync(tenant, evidenceCandidates, cancellationToken).ConfigureAwait(false);

        var maskedByObject = new Dictionary<NodeRef, MaskReason>();
        if (!(readerHasTenant && requesterHasTenant))
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

            var paths = await store.PathsOfAsync(tenant, pathBatch, cancellationToken).ConfigureAwait(false);
            var held = readerHasTenant ? null : BuildHeldScopes(readerGrants, paths);
            var requester = requesterGrants is null || requesterHasTenant ? null : BuildHeldScopes(requesterGrants, paths);

            var uncoveredReachable = new List<NodeRef>();
            foreach (var node in candidates)
            {
                var path = evidenceInfo.TryGetValue(node, out var info)
                    ? info.Scope
                    : paths.GetValueOrDefault(node, ScopePath.TenantWide);
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
                    maskedByObject[node] = MaskReason.NotPermitted;
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
                        maskedByObject[node] = MaskReason.NotPermitted;
                    }
                }
            }
        }

        var visibleBlinded = evidenceInfo
            .Where(kvp => kvp.Value.IsBlinded && !maskedByObject.ContainsKey(kvp.Key))
            .ToList();
        if (visibleBlinded.Count > 0)
        {
            await MaskBlindedWithoutUnblindAsync(
                tenant,
                reader,
                now,
                readerInEffect,
                requesterInEffect,
                visibleBlinded,
                maskedByObject,
                cancellationToken).ConfigureAwait(false);
        }

        if (maskedByObject.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var result = new Dictionary<NodeRef, MaskReason>();
        foreach (var node in distinct)
        {
            if (maskedByObject.TryGetValue(node.Unversioned(), out var reason))
            {
                result[node] = reason;
            }
        }

        return result;
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

    private async Task MaskBlindedWithoutUnblindAsync(
        TenantId tenant,
        Actor reader,
        DateTimeOffset now,
        IReadOnlyList<Grant> readerInEffect,
        IReadOnlyList<Grant>? requesterInEffect,
        IReadOnlyList<KeyValuePair<NodeRef, EvidenceNodeClassification>> visibleBlinded,
        Dictionary<NodeRef, MaskReason> maskedByObject,
        CancellationToken cancellationToken)
    {
        var allRoleIds = readerInEffect
            .Concat(requesterInEffect ?? [])
            .Where(g => g.RoleId is not null)
            .Select(g => g.RoleId!.Value)
            .Distinct()
            .ToList();

        var rolesById = allRoleIds.Count == 0
            ? new Dictionary<RoleId, Role>()
            : (await store.RolesAsync(tenant, allRoleIds, cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Id);

        var readerUnblindScopes = readerInEffect
            .Where(g => CarriesCapability(g, EnrollmentUnblindToken, rolesById))
            .Select(g => g.Scope)
            .Distinct()
            .ToList();
        var requesterUnblindScopes = requesterInEffect?
            .Where(g => CarriesCapability(g, EnrollmentUnblindToken, rolesById))
            .Select(g => g.Scope)
            .Distinct()
            .ToList();

        if (readerUnblindScopes.Count == 0 || requesterUnblindScopes is { Count: 0 })
        {
            foreach (var (node, _) in visibleBlinded)
            {
                maskedByObject[node] = MaskReason.Blinded;
            }

            return;
        }

        IReadOnlyList<QualificationRequirement>? activeRequirements = null;
        IReadOnlyList<Qualification>? heldQualifications = null;
        var personForQualification = reader.Kind == ActorKind.AutomationAgent ? reader.RequestedBy : reader;

        foreach (var (node, info) in visibleBlinded)
        {
            var readerCovers = readerUnblindScopes.Exists(s => s.Covers(info.Scope));
            var requesterCovers = requesterUnblindScopes is null || requesterUnblindScopes.Exists(s => s.Covers(info.Scope));
            if (!readerCovers || !requesterCovers)
            {
                maskedByObject[node] = MaskReason.Blinded;
                continue;
            }

            activeRequirements ??= (await store.QualificationRequirementsAsync(tenant, EnrollmentUnblindToken, cancellationToken).ConfigureAwait(false))
                .Where(r => r.IsInEffect(now, now))
                .ToList();

            var coveringRequirements = activeRequirements.Where(r => r.Scope.Covers(info.Scope)).ToList();
            if (coveringRequirements.Count == 0 || personForQualification is null)
            {
                continue;
            }

            heldQualifications ??= (await store.QualificationsHeldAsync(tenant, personForQualification, cancellationToken).ConfigureAwait(false))
                .Where(q => q.IsInEffect(now, now))
                .ToList();

            if (!coveringRequirements.All(req => heldQualifications.Any(req.IsMetBy)))
            {
                maskedByObject[node] = MaskReason.Blinded;
            }
        }
    }

    private static bool CarriesCapability(Grant grant, string token, IReadOnlyDictionary<RoleId, Role> roles) =>
        string.Equals(grant.Capability, token, StringComparison.Ordinal)
        || (grant.RoleId is { } roleId
            && roles.TryGetValue(roleId, out var role)
            && role.Capabilities.Contains(token, StringComparer.Ordinal));

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
