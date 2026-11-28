using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Kernel.Persistence.Authorization;

internal static class AuthorityScopePathReader
{
    public static readonly FrozenSet<string> PathBearingKinds = FrozenSet.Create(
        StringComparer.Ordinal,
        NodeKinds.Organization,
        NodeKinds.Site,
        NodeKinds.Study,
        NodeKinds.StudySite,
        NodeKinds.Enrollment,
        NodeKinds.ProtocolVersion,
        NodeKinds.ProtocolElement,
        NodeKinds.FormVersionSnapshot,
        NodeKinds.StudyStandardsProfile,
        NodeKinds.FormQuery,
        NodeKinds.Artifact,
        NodeKinds.ArtifactVersion,
        NodeKinds.Grant,
        NodeKinds.QualificationRequirement,
        NodeKinds.Effectivity,
        NodeKinds.Proposal,
        NodeKinds.CollectionInstance,
        NodeKinds.Observation,
        NodeKinds.Assertion,
        NodeKinds.Attestation,
        NodeKinds.ValidationRun,
        NodeKinds.ValidationFinding);

    public static async Task LoadStudySitePathsInOpenConnectionAsync(
        KernelDbContext db,
        TenantId tenant,
        List<ParticipantId> participantIds,
        List<SiteId> siteIds,
        Dictionary<(string Kind, Guid Id), List<ScopePath>> reachedByObject,
        CancellationToken cancellationToken)
    {
        if (participantIds.Count > 0)
        {
            var participantRows = await (
                    from e in db.Enrollments.AsNoTracking()
                    join ss in db.StudySites.AsNoTracking() on new { e.TenantId, Id = e.StudySiteId } equals new { ss.TenantId, ss.Id }
                    join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                    join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                    where e.TenantId == tenant && participantIds.Contains(e.ParticipantId)
                    select new { e.ParticipantId, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                .Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in participantRows)
            {
                AddReached(reachedByObject, NodeKinds.Participant, row.ParticipantId.Value, new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId));
            }
        }

        if (siteIds.Count > 0)
        {
            var siteRows = await (
                    from ss in db.StudySites.AsNoTracking()
                    join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                    join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                    where ss.TenantId == tenant && siteIds.Contains(ss.SiteId)
                    select new { ss.SiteId, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in siteRows)
            {
                AddReached(reachedByObject, NodeKinds.Site, row.SiteId.Value, new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId));
            }
        }
    }

    public static async Task LoadPathsForKindAsync(
        KernelDbContext db,
        TenantId tenant,
        string kind,
        Guid[] rawIds,
        Dictionary<(string Kind, Guid Id), ScopePath> resolved,
        HashSet<NodeRef> potentiallyBlinded,
        CancellationToken ct)
    {
        switch (kind)
        {
            case NodeKinds.Organization:
                {
                    var ids = rawIds.Select(OrganizationId.FromGuid).ToList();
                    var rows = await db.Organizations.AsNoTracking()
                        .Where(o => o.TenantId == tenant && ids.Contains(o.Id))
                        .Select(o => o.Id)
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var id in rows)
                    {
                        resolved[(kind, id.Value)] = new ScopePath(id);
                    }

                    break;
                }

            case NodeKinds.Site:
                {
                    var ids = rawIds.Select(SiteId.FromGuid).ToList();
                    var rows = await db.Sites.AsNoTracking()
                        .Where(s => s.TenantId == tenant && ids.Contains(s.Id))
                        .Select(s => new { s.Id, s.InstitutionId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.InstitutionId);
                    }

                    break;
                }

            case NodeKinds.Study:
                {
                    var ids = rawIds.Select(StudyId.FromGuid).ToList();
                    var rows = await db.Studies.AsNoTracking()
                        .Where(s => s.TenantId == tenant && ids.Contains(s.Id))
                        .Select(s => new { s.Id, s.SponsorId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.Id);
                    }

                    break;
                }

            case NodeKinds.StudySite:
                {
                    var ids = rawIds.Select(StudySiteId.FromGuid).ToList();
                    var rows = await (
                            from ss in db.StudySites.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                            join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                            where ss.TenantId == tenant && ids.Contains(ss.Id)
                            select new { ss.Id, study.SponsorId, ss.StudyId, site.Country })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.Id);
                    }

                    break;
                }

            case NodeKinds.Enrollment:
                {
                    var ids = rawIds.Select(EnrollmentId.FromGuid).ToList();
                    var rows = await (
                            from e in db.Enrollments.AsNoTracking()
                            join ss in db.StudySites.AsNoTracking() on new { e.TenantId, Id = e.StudySiteId } equals new { ss.TenantId, ss.Id }
                            join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                            join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                            where e.TenantId == tenant && ids.Contains(e.Id)
                            select new { e.Id, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId);
                    }

                    break;
                }

            case NodeKinds.ProtocolVersion:
                {
                    var ids = rawIds.Select(ProtocolVersionId.FromGuid).ToList();
                    var rows = await (
                            from v in db.ProtocolVersions.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { v.TenantId, Id = v.StudyId } equals new { study.TenantId, study.Id }
                            where v.TenantId == tenant && ids.Contains(v.Id)
                            select new { v.Id, study.SponsorId, v.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.FormVersionSnapshot:
                {
                    var ids = rawIds.Select(FormVersionSnapshotId.FromGuid).ToList();
                    var rows = await (
                            from snapshot in db.FormVersionSnapshots.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { snapshot.TenantId, Id = snapshot.StudyId } equals new { study.TenantId, study.Id }
                            where snapshot.TenantId == tenant && ids.Contains(snapshot.Id)
                            select new { snapshot.Id, study.SponsorId, snapshot.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.StudyStandardsProfile:
                {
                    var ids = rawIds.Select(StudyStandardsProfileId.FromGuid).ToList();
                    var rows = await (
                            from profile in db.Set<Cadence.Kernel.Domain.Standards.StudyStandardsProfile>().AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { profile.TenantId, Id = profile.StudyId } equals new { study.TenantId, study.Id }
                            where profile.TenantId == tenant && ids.Contains(profile.Id)
                            select new { profile.Id, study.SponsorId, profile.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.ValidationRun:
                {
                    var ids = rawIds.Select(ValidationRunId.FromGuid).ToList();
                    var rows = await (
                            from run in db.Set<ValidationRun>().AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { run.TenantId, Id = run.StudyId } equals new { study.TenantId, study.Id }
                            where run.TenantId == tenant && ids.Contains(run.Id)
                            select new { run.Id, study.SponsorId, run.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.ProtocolElement:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityScopePathSql.ProtocolElementPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.FormQuery:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityScopePathSql.FormQueryPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Artifact:
                {
                    var ids = rawIds.Select(ArtifactId.FromGuid).ToList();
                    var rows = await (
                            from a in db.Artifacts.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { a.TenantId, Id = a.StudyId } equals new { study.TenantId, Id = (StudyId?)study.Id }
                            where a.TenantId == tenant && ids.Contains(a.Id)
                            select new { a.Id, study.SponsorId, StudyId = study.Id })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.ArtifactVersion:
                {
                    var ids = rawIds.Select(ArtifactVersionId.FromGuid).ToList();
                    var rows = await (
                            from v in db.ArtifactVersions.AsNoTracking()
                            join a in db.Artifacts.AsNoTracking() on new { v.TenantId, Id = v.ArtifactId } equals new { a.TenantId, a.Id }
                            join study in db.Studies.AsNoTracking() on new { a.TenantId, Id = a.StudyId } equals new { study.TenantId, Id = (StudyId?)study.Id }
                            where v.TenantId == tenant && ids.Contains(v.Id)
                            select new { v.Id, study.SponsorId, StudyId = study.Id })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.Grant:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityScopePathSql.GrantPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.QualificationRequirement:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityScopePathSql.QualificationRequirementPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Effectivity:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityScopePathSql.EffectivityPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Proposal:
                {
                    var ids = rawIds.Select(ProposalId.FromGuid).ToList();
                    var rows = await (
                            from p in db.Proposals.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { p.TenantId, Id = p.StudyId } equals new { study.TenantId, study.Id }
                            where p.TenantId == tenant && ids.Contains(p.Id)
                            select new { p.Id, study.SponsorId, p.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.CollectionInstance:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityScopePathSql.CollectionInstancePathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Observation:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityScopePathSql.ObservationPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Assertion:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityScopePathSql.AssertionPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Attestation:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityClassificationSql.AttestationPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.ValidationFinding:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AuthorityClassificationSql.ValidationFindingPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private static async Task LoadRawPathsAsync(
        KernelDbContext db,
        TenantId tenant,
        string kind,
        Guid[] rawIds,
        string sql,
        Dictionary<(string Kind, Guid Id), ScopePath> resolved,
        HashSet<NodeRef> potentiallyBlinded,
        CancellationToken ct)
    {
        object[] parameters =
        [
            new NpgsqlParameter<Guid>("tenant", tenant.Value),
            new NpgsqlParameter<Guid[]>("ids", rawIds),
        ];
        var rows = await db.Database.SqlQueryRaw<ScopePathRow>(sql, parameters).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (row.ToScopePath() is { } path)
            {
                resolved[(kind, row.Id)] = path;
            }

            if (row.MayBeBlinded)
            {
                potentiallyBlinded.Add(new NodeRef(kind, row.Id));
            }
        }
    }

    private static void AddReached(
        Dictionary<(string Kind, Guid Id), List<ScopePath>> reachedByObject, string kind, Guid id, ScopePath path)
    {
        if (!reachedByObject.TryGetValue((kind, id), out var list))
        {
            reachedByObject[(kind, id)] = list = [];
        }

        list.Add(path);
    }
}
