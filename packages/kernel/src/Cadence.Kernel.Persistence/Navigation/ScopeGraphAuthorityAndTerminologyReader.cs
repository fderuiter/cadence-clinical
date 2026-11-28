using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Navigation;

internal static class ScopeGraphAuthorityAndTerminologyReader
{
    public static async Task PopulateAuthorityAndTerminologyNodesAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        List<NodeRef> distinct,
        Dictionary<NodeRef, NodeRef> firstByUnversioned,
        Dictionary<NodeRef, ScopeNode> found,
        CancellationToken cancellationToken)
    {
        var roleIds = distinct.Where(n => n.Kind == NodeKinds.Role).Select(n => RoleId.FromGuid(n.Id)).ToList();
        if (roleIds.Count > 0)
        {
            var roles = await db.Roles.AsNoTracking()
                .Where(r => r.TenantId == tenant && roleIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Name, r.Code, r.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var role in roles)
            {
                found[NodeRef.For(role.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(role.Id, role.VersionIndex), role.Name, role.Code),
                    tenantRef);
            }
        }

        var grantIds = distinct.Where(n => n.Kind == NodeKinds.Grant).Select(n => GrantId.FromGuid(n.Id)).ToList();
        if (grantIds.Count > 0)
        {
            foreach (var (grantId, scopeNode) in await LoadGrantsAsync(db, tenant, grantIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(grantId)] = scopeNode;
            }
        }

        var qualificationIds = distinct.Where(n => n.Kind == NodeKinds.Qualification).Select(n => QualificationId.FromGuid(n.Id)).ToList();
        if (qualificationIds.Count > 0)
        {
            var qualifications = await db.Qualifications.AsNoTracking()
                .Where(q => q.TenantId == tenant && qualificationIds.Contains(q.Id))
                .Select(q => new { q.Id, q.Kind, q.Code, q.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var qualification in qualifications)
            {
                found[NodeRef.For(qualification.Id)] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(qualification.Id, qualification.VersionIndex),
                        $"{qualification.Code} qualification",
                        qualification.Code,
                        qualification.Kind.ToString()),
                    tenantRef);
            }
        }

        var requirementIds = distinct.Where(n => n.Kind == NodeKinds.QualificationRequirement).Select(n => QualificationRequirementId.FromGuid(n.Id)).ToList();
        if (requirementIds.Count > 0)
        {
            foreach (var (requirementId, scopeNode) in await LoadQualificationRequirementsAsync(db, tenant, requirementIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(requirementId)] = scopeNode;
            }
        }

        var effectivityIds = distinct.Where(n => n.Kind == NodeKinds.Effectivity).Select(n => EffectivityId.FromGuid(n.Id)).ToList();
        if (effectivityIds.Count > 0)
        {
            foreach (var (effectivityId, scopeNode) in await LoadEffectivitiesAsync(db, tenant, effectivityIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(effectivityId)] = scopeNode;
            }
        }

        var codeSystemIds = distinct.Where(n => n.Kind == NodeKinds.CodeSystem).Select(n => CodeSystemId.FromGuid(n.Id)).ToList();
        if (codeSystemIds.Count > 0)
        {
            var codeSystems = await db.CodeSystems.AsNoTracking()
                .Where(cs => cs.TenantId == tenant && codeSystemIds.Contains(cs.Id))
                .Select(cs => new { cs.Id, cs.Name, cs.Uri, cs.License, cs.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var cs in codeSystems)
            {
                found[NodeRef.For(cs.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(cs.Id, cs.VersionIndex), cs.Name, cs.Uri, cs.License.ToString()),
                    tenantRef);
            }
        }

        var codeSystemVersionIds = distinct.Where(n => n.Kind == NodeKinds.CodeSystemVersion).Select(n => CodeSystemVersionId.FromGuid(n.Id)).ToList();
        if (codeSystemVersionIds.Count > 0)
        {
            foreach (var (csvId, scopeNode) in await LoadCodeSystemVersionsAsync(db, tenant, tenantRef, codeSystemVersionIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(csvId)] = scopeNode;
            }
        }

        var conceptKeys = distinct.Where(n => n.Kind == NodeKinds.Concept).ToList();
        if (conceptKeys.Count > 0)
        {
            var requestedConcepts = conceptKeys.Select(k => firstByUnversioned[k]).ToList();
            foreach (var (conceptId, scopeNode) in await ScopeGraphConceptReader.LoadConceptsAsync(db, tenant, tenantRef, requestedConcepts, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(conceptId)] = scopeNode;
            }
        }

        var conceptMappingIds = distinct.Where(n => n.Kind == NodeKinds.ConceptMapping).Select(n => ConceptMappingId.FromGuid(n.Id)).ToList();
        if (conceptMappingIds.Count > 0)
        {
            var mappings = await db.ConceptMappings.AsNoTracking()
                .Where(m => m.TenantId == tenant && conceptMappingIds.Contains(m.Id))
                .Select(m => new { m.Id, m.ConceptId, m.Code, m.Relation, m.Status, m.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var mapping in mappings)
            {
                found[NodeRef.For(mapping.Id)] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(mapping.Id, mapping.VersionIndex),
                        $"{mapping.Relation} mapping to {mapping.Code}",
                        mapping.Code,
                        mapping.Status.ToString()),
                    NodeRef.For(mapping.ConceptId),
                    FallbackParent: tenantRef);
            }
        }

        var workQueueIds = distinct.Where(n => n.Kind == NodeKinds.WorkQueueDefinition).Select(n => WorkQueueDefinitionId.FromGuid(n.Id)).ToList();
        if (workQueueIds.Count > 0)
        {
            var workQueues = await db.WorkQueueDefinitions.AsNoTracking()
                .Where(q => q.TenantId == tenant && workQueueIds.Contains(q.Id))
                .Select(q => new { q.Id, q.Title, q.Code, q.Version, q.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var workQueue in workQueues)
            {
                found[NodeRef.For(workQueue.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(workQueue.Id, workQueue.VersionIndex), workQueue.Title, workQueue.Code, $"v{workQueue.Version}"),
                    tenantRef);
            }
        }

        var libraryItemIds = distinct.Where(n => n.Kind == NodeKinds.LibraryItem).Select(n => LibraryItemId.FromGuid(n.Id)).ToList();
        if (libraryItemIds.Count > 0)
        {
            var libraryItems = await db.Set<LibraryItem>().AsNoTracking()
                .Where(i => i.TenantId == tenant && libraryItemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Title, i.Key, i.Status, i.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in libraryItems)
            {
                found[NodeRef.For(item.Id)] = new ScopeNode(
                    new NodeSummary(NodeRef.For(item.Id, item.VersionIndex), item.Title, item.Key, item.Status.ToString()),
                    tenantRef);
            }
        }

        var standardVersionIds = distinct.Where(n => n.Kind == NodeKinds.StandardVersion).Select(n => StandardVersionId.FromGuid(n.Id)).ToList();
        if (standardVersionIds.Count > 0)
        {
            var standardVersions = await db.Set<StandardVersion>().AsNoTracking()
                .Where(s => s.TenantId == tenant && standardVersionIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name, s.StandardCode, s.Version, s.Status, s.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var sv in standardVersions)
            {
                found[NodeRef.For(sv.Id)] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(sv.Id, sv.VersionIndex),
                        $"{sv.Name} {sv.Version}",
                        $"{sv.StandardCode} {sv.Version}",
                        sv.Status.ToString()),
                    tenantRef);
            }
        }

        var subscriptionIds = distinct.Where(n => n.Kind == NodeKinds.Subscription).Select(n => SubscriptionId.FromGuid(n.Id)).ToList();
        if (subscriptionIds.Count > 0)
        {
            foreach (var (subscriptionId, scopeNode) in await LoadSubscriptionsAsync(db, tenant, tenantRef, subscriptionIds, cancellationToken).ConfigureAwait(false))
            {
                found[NodeRef.For(subscriptionId)] = scopeNode;
            }
        }

        var entitlementIds = distinct.Where(n => n.Kind == NodeKinds.ProductEntitlement).Select(n => ProductEntitlementId.FromGuid(n.Id)).ToList();
        if (entitlementIds.Count > 0)
        {
            var entitlements = await db.ProductEntitlements.AsNoTracking()
                .Where(e => e.TenantId == tenant && entitlementIds.Contains(e.Id))
                .Select(e => new { e.Id, e.SubscriptionId, e.Family, e.IsEnabled, e.VersionIndex })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var entitlement in entitlements)
            {
                found[NodeRef.For(entitlement.Id)] = new ScopeNode(
                    new NodeSummary(
                        NodeRef.For(entitlement.Id, entitlement.VersionIndex),
                        $"{entitlement.Family} entitlement",
                        entitlement.Family.ToString(),
                        entitlement.IsEnabled ? "Enabled" : "Disabled"),
                    NodeRef.For(entitlement.SubscriptionId),
                    FallbackParent: tenantRef);
            }
        }
    }

    public static async Task<Dictionary<GrantId, ScopeNode>> LoadGrantsAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<GrantId> grantIds,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from g in db.Grants.AsNoTracking()
                join r in db.Roles.AsNoTracking()
                    on new { g.TenantId, Id = g.RoleId } equals new { r.TenantId, Id = (RoleId?)r.Id } into roleGroup
                from r in roleGroup.DefaultIfEmpty()
                where g.TenantId == tenant && grantIds.Contains(g.Id)
                select new
                {
                    g.Id,
                    g.Capability,
                    RoleCode = r != null ? r.Code : null,
                    g.ScopeLevel,
                    g.ScopeTargetId,
                    g.ScopeCountry,
                    g.VersionIndex,
                })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<GrantId, ScopeNode>(rows.Count);
        foreach (var grant in rows)
        {
            var label = grant.Capability ?? grant.RoleCode;
            string title;
            if (grant.Capability is not null)
            {
                title = $"{grant.Capability} grant";
            }
            else if (grant.RoleCode is not null)
            {
                title = $"{grant.RoleCode} role grant";
            }
            else
            {
                title = "Role grant";
            }

            var (parent, group, fallback) = ScopedAuthorityParent(tenant, grant.ScopeLevel, grant.ScopeTargetId, grant.ScopeCountry);
            result[grant.Id] = new ScopeNode(
                new NodeSummary(NodeRef.For(grant.Id, grant.VersionIndex), title, label, grant.ScopeLevel.ToString()),
                parent,
                group,
                fallback);
        }

        return result;
    }

    public static async Task<Dictionary<QualificationRequirementId, ScopeNode>> LoadQualificationRequirementsAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<QualificationRequirementId> requirementIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.QualificationRequirements.AsNoTracking()
            .Where(r => r.TenantId == tenant && requirementIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Capability, r.QualificationCode, r.ScopeLevel, r.ScopeTargetId, r.ScopeCountry, r.VersionIndex })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<QualificationRequirementId, ScopeNode>(rows.Count);
        foreach (var req in rows)
        {
            var (parent, group, fallback) = ScopedAuthorityParent(tenant, req.ScopeLevel, req.ScopeTargetId, req.ScopeCountry);
            result[req.Id] = new ScopeNode(
                new NodeSummary(
                    NodeRef.For(req.Id, req.VersionIndex),
                    $"Requires {req.QualificationCode} for {req.Capability}",
                    req.QualificationCode,
                    req.ScopeLevel.ToString()),
                parent,
                group,
                fallback);
        }

        return result;
    }

    public static async Task<Dictionary<EffectivityId, ScopeNode>> LoadEffectivitiesAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<EffectivityId> effectivityIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.Effectivities.AsNoTracking()
            .Where(e => e.TenantId == tenant && effectivityIds.Contains(e.Id))
            .Select(e => new { e.Id, e.StudyId, e.ScopeLevel, e.ScopeCountry, e.ScopeStudySiteId, e.Status, e.VersionIndex })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<EffectivityId, ScopeNode>(rows.Count);
        foreach (var eff in rows)
        {
            var parent = eff.ScopeLevel == EffectivityLevel.StudySite && eff.ScopeStudySiteId is { IsEmpty: false } effSiteId
                ? NodeRef.For(effSiteId)
                : NodeRef.For(eff.StudyId);
            var group = eff.ScopeLevel == EffectivityLevel.Country ? eff.ScopeCountry : null;
            result[eff.Id] = new ScopeNode(
                new NodeSummary(NodeRef.For(eff.Id, eff.VersionIndex), $"{eff.ScopeLevel} effectivity", Code: null, eff.Status.ToString()),
                parent,
                group,
                FallbackParent: NodeRef.For(eff.StudyId));
        }

        return result;
    }

    public static async Task<Dictionary<CodeSystemVersionId, ScopeNode>> LoadCodeSystemVersionsAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        IReadOnlyCollection<CodeSystemVersionId> codeSystemVersionIds,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from v in db.CodeSystemVersions.AsNoTracking()
                join cs in db.CodeSystems.AsNoTracking()
                    on new { v.TenantId, Id = v.CodeSystemId } equals new { cs.TenantId, cs.Id } into csGroup
                from cs in csGroup.DefaultIfEmpty()
                where v.TenantId == tenant && codeSystemVersionIds.Contains(v.Id)
                select new { v.Id, v.CodeSystemId, v.Version, v.VersionIndex, SystemName = cs != null ? cs.Name : null })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<CodeSystemVersionId, ScopeNode>(rows.Count);
        foreach (var csv in rows)
        {
            var title = csv.SystemName is not null ? $"{csv.SystemName} {csv.Version}" : csv.Version;
            result[csv.Id] = new ScopeNode(
                new NodeSummary(NodeRef.For(csv.Id, csv.VersionIndex), title, csv.Version),
                NodeRef.For(csv.CodeSystemId),
                FallbackParent: tenantRef);
        }

        return result;
    }

    public static async Task<Dictionary<SubscriptionId, ScopeNode>> LoadSubscriptionsAsync(
        KernelDbContext db,
        TenantId tenant,
        NodeRef tenantRef,
        IReadOnlyCollection<SubscriptionId> subscriptionIds,
        CancellationToken cancellationToken)
    {
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenant && subscriptionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.VersionIndex })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (subscriptions.Count == 0)
        {
            return [];
        }

        var matchedIds = subscriptions.Select(s => s.Id).ToList();
        var openPeriods = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.TenantId == tenant && matchedIds.Contains(p.SubscriptionId) && p.Period.Recorded.IsOpen)
            .OrderByDescending(p => p.Period.Valid.Start)
            .Select(p => new { p.SubscriptionId, p.Tier, p.Status })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var periodBySub = new Dictionary<SubscriptionId, (string Tier, string Status)>();
        foreach (var p in openPeriods)
        {
            periodBySub.TryAdd(p.SubscriptionId, (p.Tier.ToString(), p.Status.ToString()));
        }

        var result = new Dictionary<SubscriptionId, ScopeNode>(subscriptions.Count);
        foreach (var sub in subscriptions)
        {
            var hasPeriod = periodBySub.TryGetValue(sub.Id, out var period);
            result[sub.Id] = new ScopeNode(
                new NodeSummary(
                    NodeRef.For(sub.Id, sub.VersionIndex),
                    hasPeriod ? $"{period.Tier} subscription" : "Subscription",
                    hasPeriod ? period.Tier : null,
                    hasPeriod ? period.Status : null),
                tenantRef);
        }

        return result;
    }

    private static (NodeRef Parent, string? Group, NodeRef? FallbackParent) ScopedAuthorityParent(
        TenantId tenant,
        ScopeLevel scopeLevel,
        Guid? scopeTargetId,
        string? scopeCountry)
    {
        var tenantRef = NodeRef.For(tenant);
        return (scopeLevel, scopeTargetId) switch
        {
            (ScopeLevel.StudySite, { } siteGuid) => (NodeRef.For(StudySiteId.FromGuid(siteGuid)), null, tenantRef),
            (ScopeLevel.Study, { } studyGuid) => (NodeRef.For(StudyId.FromGuid(studyGuid)), null, tenantRef),
            (ScopeLevel.StudyCountry, { } countryStudyGuid) => (NodeRef.For(StudyId.FromGuid(countryStudyGuid)), scopeCountry, tenantRef),
            (ScopeLevel.Organization, { } orgGuid) => (NodeRef.For(OrganizationId.FromGuid(orgGuid)), null, tenantRef),
            _ => (tenantRef, null, null),
        };
    }
}
