using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Safety.Application;

public interface IDsurApplicationService
{
    Task<PeriodicSafetyReportRecord?> FindReportAsync(string id, CancellationToken cancellationToken = default);

    Task<(PeriodicSafetyReportRecord? Report, PeriodicSafetyReportRecord? ResolvedVersion, bool RevisionNotFound)> GetReportOrRevisionAsync(
        string id,
        int? revision,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken = default);

    Task<PeriodicSafetyReportRecord[]> ListReportsAsync(
        IReadOnlySet<string>? authorizedStudies,
        CancellationToken cancellationToken = default);

    Task<DsurCompileResult> CompileAsync(
        DsurCompileRequest request,
        string callerUserId,
        CancellationToken cancellationToken = default);

    Task<DsurApprovalResult> ApproveAsync(
        string id,
        DsurApproveRequest request,
        string callerUserId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    Task<DsurUpdateResult> UpdateAsync(
        string id,
        DsurUpdateRequest request,
        string callerUserId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}

public sealed class DsurApplicationService(SafetyDbContext dbContext, IClock clock) : IDsurApplicationService
{
    private readonly SafetyDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly DsurCompilerEngine _compiler = new(dbContext, clock ?? throw new ArgumentNullException(nameof(clock)));

    public Task<PeriodicSafetyReportRecord?> FindReportAsync(string id, CancellationToken cancellationToken = default) =>
        _dbContext.PeriodicSafetyReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<(PeriodicSafetyReportRecord? Report, PeriodicSafetyReportRecord? ResolvedVersion, bool RevisionNotFound)> GetReportOrRevisionAsync(
        string id,
        int? revision,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken = default)
    {
        var record = await FindReportAsync(id, cancellationToken);
        if (record is null)
        {
            return (null, null, false);
        }

        if (!revision.HasValue && !asOf.HasValue)
        {
            return (record, record, false);
        }

        var revQuery = _dbContext.PeriodicSafetyReportRevisions
            .AsNoTracking()
            .Where(r => r.ReportId == record.Id);

        PeriodicSafetyReportRevision? matchedRevision = revision.HasValue
            ? await revQuery.FirstOrDefaultAsync(r => r.RevisionNumber == revision.Value, cancellationToken)
            : await revQuery
                .Where(r => r.RecordedAt <= asOf!.Value)
                .OrderByDescending(r => r.RevisionNumber)
                .FirstOrDefaultAsync(cancellationToken);

        if (matchedRevision is null)
        {
            return (record, null, true);
        }

        return (record, matchedRevision.ToRecord(record.CreatedAt, record.CreatedBy), false);
    }

    public Task<PeriodicSafetyReportRecord[]> ListReportsAsync(
        IReadOnlySet<string>? authorizedStudies,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PeriodicSafetyReports.AsNoTracking();
        if (authorizedStudies is not null)
        {
            query = query.Where(r => r.StudyId != null && authorizedStudies.Contains(r.StudyId));
        }

        return query.ToArrayAsync(cancellationToken);
    }

    public Task<DsurCompileResult> CompileAsync(
        DsurCompileRequest request,
        string callerUserId,
        CancellationToken cancellationToken = default) =>
        _compiler.CompileDsurAsync(request, callerUserId, cancellationToken);

    public Task<DsurApprovalResult> ApproveAsync(
        string id,
        DsurApproveRequest request,
        string callerUserId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default) =>
        _compiler.ApproveDsurAsync(id, request, callerUserId, cancellationToken, httpContext);

    public Task<DsurUpdateResult> UpdateAsync(
        string id,
        DsurUpdateRequest request,
        string callerUserId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default) =>
        _compiler.UpdateDsurAsync(id, request, callerUserId, cancellationToken, httpContext);
}
