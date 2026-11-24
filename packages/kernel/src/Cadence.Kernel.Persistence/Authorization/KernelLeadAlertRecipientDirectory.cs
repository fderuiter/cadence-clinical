using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Authorization;

/// <summary>
/// Resolves active study-lead and site-investigator person actor IDs over tenant-scoped <see cref="Grant"/> and
/// <see cref="Role"/> records at <see cref="IClock.UtcNow"/> without touching the <c>IdentityVault</c> (K-IDN-1).
/// </summary>
internal sealed class KernelLeadAlertRecipientDirectory(
    KernelTenantDbContextFactory contexts,
    IAuthorityStore authorityStore,
    IClock clock) : ILeadAlertRecipientDirectory
{
    private const string QueryManageCapability = "execution.query.manage";
    private const string CollectionSignCapability = "collection.sign";

    public async Task<IReadOnlyList<string>> ListStudyLeadsAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default)
    {
        var studyPath = await authorityStore.PathOfAsync(tenant, NodeRef.For(studyId), cancellationToken).ConfigureAwait(false);
        if (studyPath == ScopePath.TenantWide)
        {
            return [];
        }

        var (grants, rolesById) = await LoadActivePersonGrantsAsync(tenant, cancellationToken).ConfigureAwait(false);
        return
        [
            .. grants
                .Where(g => g.Scope.Covers(studyPath)
                    && (CarriesCapability(g, QueryManageCapability, rolesById)
                        || HasRoleMarker(g, rolesById, "STUDY_LEAD", "study lead")))
                .Select(g => g.GranteeId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal),
        ];
    }

    public async Task<IReadOnlyList<string>> ListSiteLeadsAsync(
        TenantId tenant,
        StudyId studyId,
        StudySiteId studySiteId,
        CancellationToken cancellationToken = default)
    {
        var sitePath = await authorityStore.PathOfAsync(tenant, NodeRef.For(studySiteId), cancellationToken).ConfigureAwait(false);
        if (sitePath == ScopePath.TenantWide || sitePath.Study != studyId)
        {
            return [];
        }

        var (grants, rolesById) = await LoadActivePersonGrantsAsync(tenant, cancellationToken).ConfigureAwait(false);
        return
        [
            .. grants
                .Where(g => g.Scope.Covers(sitePath)
                    && (CarriesCapability(g, CollectionSignCapability, rolesById)
                        || HasRoleMarker(g, rolesById, "INVESTIGATOR", "SITE_LEAD", "site investigator", "site lead")))
                .Select(g => g.GranteeId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal),
        ];
    }

    private async Task<(IReadOnlyList<Grant> Grants, IReadOnlyDictionary<RoleId, Role> RolesById)> LoadActivePersonGrantsAsync(
        TenantId tenant,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        List<Grant> personGrants;
        await using (var db = contexts.Create(tenant))
        {
            personGrants = await db.Grants
                .AsNoTracking()
                .Include(g => g.Periods)
                .Where(g => g.TenantId == tenant && g.GranteeKind == ActorKind.Person)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var candidates = personGrants.Where(g => g.IsInEffect(now, now)).ToList();
        if (candidates.Count == 0)
        {
            return ([], new Dictionary<RoleId, Role>());
        }

        var inEffectIds = await GrantChain
            .FilterInEffectAsync(authorityStore, tenant, candidates, now, now, cancellationToken)
            .ConfigureAwait(false);
        var active = candidates.Where(g => inEffectIds.Contains(g.Id)).ToList();
        if (active.Count == 0)
        {
            return ([], new Dictionary<RoleId, Role>());
        }

        var roleIds = active
            .Where(g => g.RoleId is not null)
            .Select(g => g.RoleId!.Value)
            .Distinct()
            .ToList();
        var rolesById = roleIds.Count == 0
            ? new Dictionary<RoleId, Role>()
            : (await authorityStore.RolesAsync(tenant, roleIds, cancellationToken).ConfigureAwait(false))
                .ToDictionary(r => r.Id);

        return (active, rolesById);
    }

    private static bool CarriesCapability(Grant grant, string token, IReadOnlyDictionary<RoleId, Role> roles) =>
        string.Equals(grant.Capability, token, StringComparison.Ordinal)
        || (grant.RoleId is { } roleId
            && roles.TryGetValue(roleId, out var role)
            && role.Capabilities.Contains(token, StringComparer.Ordinal));

    private static bool HasRoleMarker(Grant grant, IReadOnlyDictionary<RoleId, Role> roles, params string[] markers)
    {
        if (grant.RoleId is not { } roleId || !roles.TryGetValue(roleId, out var role))
        {
            return false;
        }

        foreach (var marker in markers)
        {
            if (role.Code.Contains(marker, StringComparison.OrdinalIgnoreCase)
                || role.Name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
