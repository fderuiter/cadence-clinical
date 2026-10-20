using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
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
/// <item>tenant reference data, roles and the tenant's people, when it holds any grant in the tenant.</item>
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
        var held = agent && reader.RequestedBy is null ? [] : await HeldAsync(tenant, reader.AsIdentity(), cancellationToken).ConfigureAwait(false);
        var requester = agent && reader.RequestedBy is { } person ? await HeldAsync(tenant, person, cancellationToken).ConfigureAwait(false) : null;
        var masked = new Dictionary<NodeRef, MaskReason>();
        foreach (var node in distinct)
        {
            if (!await VisibleAsync(tenant, node, held, cancellationToken).ConfigureAwait(false)
                || (requester is not null && !await VisibleAsync(tenant, node, requester, cancellationToken).ConfigureAwait(false)))
            {
                masked[node] = MaskReason.NotPermitted;
            }
        }

        return masked;
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

    private async Task<bool> VisibleAsync(TenantId tenant, NodeRef node, IReadOnlyList<HeldScope> held, CancellationToken cancellationToken)
    {
        if (held.Count == 0)
        {
            return false;
        }

        if (held.Any(h => h.Scope.Level == ScopeLevel.Tenant) || TenantMemberKinds.Contains(node.Kind))
        {
            return true;
        }

        var path = await store.PathOfAsync(tenant, node, cancellationToken).ConfigureAwait(false);
        if (held.Any(h => h.Scope.Covers(path) || IsAtOrAbove(path, h.Path)))
        {
            return true;
        }

        var reached = await store.StudySitePathsAsync(tenant, node, cancellationToken).ConfigureAwait(false);
        return reached.Any(p => held.Any(h => h.Scope.Covers(p)));
    }

    private async Task<IReadOnlyList<HeldScope>> HeldAsync(TenantId tenant, Actor reader, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var grants = await store.GrantsHeldAsync(tenant, reader, cancellationToken).ConfigureAwait(false);
        var held = new List<HeldScope>(grants.Count);
        foreach (var grant in grants)
        {
            if (held.Any(h => h.Scope == grant.Scope)
                || !await GrantChain.InEffectAsync(store, tenant, grant, now, now, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            // The grant's own path is where its scope sits: its organization, study, country and study site.
            var path = await store.PathOfAsync(tenant, NodeRef.For(grant.Id), cancellationToken).ConfigureAwait(false);
            held.Add(new HeldScope(grant.Scope, path));
        }

        return held;
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
