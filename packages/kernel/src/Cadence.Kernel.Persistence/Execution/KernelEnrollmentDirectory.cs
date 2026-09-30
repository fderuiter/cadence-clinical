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

    private static EnrollmentState StateOf(Enrollment e) =>
        new(e.Id, e.ParticipantId, e.StudyId, e.StudySiteId, e.SubjectCode, e.Status, e.StatusSince, e.EnrollmentNumber, e.ContentHash);
}
