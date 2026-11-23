using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Execution;

/// <summary>
/// Reads participants and enrollments through a context bound to the caller's tenant, so another tenant's rows are
/// never seen (ADR-2250). Read-only: no tracking.
/// </summary>
internal sealed class KernelEnrollmentDirectory(KernelTenantDbContextFactory contexts) : IEnrollmentDirectory
{
    public async Task<EnrollmentState?> ResolveParticipantAsync(TenantId tenant, StudyId study, SubjectCode code, CancellationToken cancellationToken)
    {
        if (!code.IsValid)
        {
            return null;
        }

        await using var db = contexts.Create(tenant);
        var enrollment = await db.Enrollments.AsNoTracking()
            .SingleOrDefaultAsync(e => e.StudyId == study && e.SubjectCode == code, cancellationToken)
            .ConfigureAwait(false);
        return enrollment is null ? null : StateOf(enrollment);
    }

    public async Task<EnrollmentState?> FindAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var found = await db.Enrollments.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == enrollment, cancellationToken)
            .ConfigureAwait(false);
        return found is null ? null : StateOf(found);
    }

    public async Task<EnrollmentStatus?> StatusAsOfAsync(
        TenantId tenant, EnrollmentId enrollment, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var statuses = await db.EnrollmentStatusPeriods.AsNoTracking()
            .Where(p => p.EnrollmentId == enrollment && p.Period.Valid.Contains(validAt) && p.Period.Recorded.Contains(knownAt))
            .Select(p => p.Status)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // K-TIME-2 allows at most one row at a (valid, known) point.
        return statuses.Count == 0 ? null : statuses.Single();
    }

    public async Task<(StudyId StudyId, StudySiteId StudySiteId)?> FindStudySiteAsync(
        TenantId tenant, string studyCode, string siteCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studyCode) || string.IsNullOrWhiteSpace(siteCode))
        {
            return null;
        }

        await using var db = contexts.Create(tenant);
        var match = await (
                from studySite in db.StudySites.AsNoTracking()
                join study in db.Studies.AsNoTracking() on studySite.StudyId equals study.Id
                join site in db.Sites.AsNoTracking() on studySite.SiteId equals site.Id
                where studySite.IsActive && study.IsActive && site.IsActive
                    && study.StudyCode == studyCode && site.SiteCode == siteCode
                select new { study.Id, StudySiteId = studySite.Id })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return match is null ? null : (match.Id, match.StudySiteId);
    }

    public async Task<int> NextEnrollmentNumberAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var highest = await db.Enrollments.AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(e => e.StudyId == study)
            .MaxAsync(e => e.EnrollmentNumber, cancellationToken)
            .ConfigureAwait(false);
        return (highest ?? 0) + 1;
    }

    public async Task<EnrollmentCodeLookupResult?> LookupByCodeAsync(
        TenantId tenant,
        string studyCode,
        SubjectCode subjectCode,
        string? siteCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyCode) || !subjectCode.IsValid)
        {
            return null;
        }

        var trimmedStudyCode = studyCode.Trim();
        var trimmedSiteCode = string.IsNullOrWhiteSpace(siteCode) ? null : siteCode.Trim();

        await using var db = contexts.Create(tenant);

        // Fast path hits IX_studies_tenant_id_study_code and IX_enrollments_tenant_id_study_id_subject_code directly.
        var match = await QueryEnrollmentByStudyPredicateAsync(
                db,
                tenant,
                db.Studies.AsNoTracking().Where(s => s.TenantId == tenant && s.StudyCode == trimmedStudyCode),
                subjectCode,
                cancellationToken)
            .ConfigureAwait(false);

        if (match is null)
        {
            var normalizedStudyCode = trimmedStudyCode.ToUpperInvariant();
            match = await QueryEnrollmentByStudyPredicateAsync(
                    db,
                    tenant,
                    db.Studies.AsNoTracking().Where(s => s.TenantId == tenant && s.StudyCode.ToUpper() == normalizedStudyCode),
                    subjectCode,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (match is null)
        {
            return null;
        }

        if (trimmedSiteCode is not null && !string.Equals(match.SiteCode, trimmedSiteCode, StringComparison.Ordinal))
        {
            return null;
        }

        return match;
    }

    private static Task<EnrollmentCodeLookupResult?> QueryEnrollmentByStudyPredicateAsync(
        KernelDbContext db,
        TenantId tenant,
        IQueryable<Study> studies,
        SubjectCode subjectCode,
        CancellationToken cancellationToken) =>
        (from study in studies
         join enrollment in db.Enrollments.AsNoTracking()
             on new { study.TenantId, StudyId = study.Id } equals new { enrollment.TenantId, enrollment.StudyId }
         join studySite in db.StudySites.AsNoTracking()
             on new { enrollment.TenantId, Id = enrollment.StudySiteId } equals new { studySite.TenantId, studySite.Id }
         join site in db.Sites.AsNoTracking()
             on new { studySite.TenantId, Id = studySite.SiteId } equals new { site.TenantId, site.Id }
         where enrollment.TenantId == tenant && enrollment.SubjectCode == subjectCode
         select new EnrollmentCodeLookupResult(
             study.Id,
             study.VersionIndex,
             study.StudyCode,
             study.Title,
             studySite.Id,
             studySite.VersionIndex,
             site.SiteCode,
             site.Name,
             enrollment.Id,
             enrollment.VersionIndex,
             enrollment.SubjectCode,
             enrollment.Status,
             enrollment.EnrollmentNumber))
        .SingleOrDefaultAsync(cancellationToken);

    private static EnrollmentState StateOf(Enrollment e) =>
        new(e.Id, e.ParticipantId, e.StudyId, e.StudySiteId, e.SubjectCode, e.Status, e.StatusSince, e.EnrollmentNumber, e.ContentHash);
}
