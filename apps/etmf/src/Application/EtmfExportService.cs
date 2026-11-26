using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Etmf.Workers;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;
using Cadence.Storage;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Etmf.Application;

public interface IEtmfExportService
{
    Task<IResult> ExportDocumentsAsync(
        ExportDocumentsRequest request,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> GetExportJobAsync(
        string jobId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> DownloadExportJobAsync(
        string jobId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> ListExportJobsAsync(
        string? studyId,
        HttpContext httpContext,
        CancellationToken ct);
}

public sealed class EtmfExportService(
    EtmfDbContext dbContext,
    IEtmfUnitOfWork unitOfWork,
    EtmfExportQueue exportQueue,
    IClock clock,
    IStoragePort<byte[]>? storage = null) : IEtmfExportService
{
    public async Task<IResult> ExportDocumentsAsync(
        ExportDocumentsRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            return TypedResults.BadRequest(new { error = "study_id is required: an export is one study's binder." });
        }

        var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, request.StudyId))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.StudyAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(principal))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        var query = dbContext.TmfDocuments.AsNoTracking().Where(d => d.StudyId == request.StudyId);

        if (request.DocumentIds != null && request.DocumentIds.Count > 0)
        {
            query = query.Where(d => request.DocumentIds.Contains(d.Id));
        }
        else if (!string.IsNullOrWhiteSpace(request.ArtifactCode))
        {
            query = query.Where(d => d.ArtifactCode == request.ArtifactCode);
        }
        else if (!string.IsNullOrWhiteSpace(request.Section))
        {
            query = query.Where(d => d.Section == request.Section);
        }
        else if (request.Zone.HasValue)
        {
            query = query.Where(d => d.Zone == request.Zone.Value);
        }

        if (ClinicalScopeGuard.IsSiteBound(principal))
        {
            var allowedSites = principal!.AssignedSites
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            query = query.Where(d =>
                d.SiteId == null
                || d.SiteId.Trim().Length == 0
                || allowedSites.Contains(d.SiteId.Trim().ToUpper()));
        }

        int count = await query.CountAsync(ct);
        var studyId = request.StudyId;

        if (count <= 50 && !request.IsFullBinder)
        {
            IReadOnlyList<TmfDocument> matchingDocs = count == 0
                ? Array.Empty<TmfDocument>()
                : await query
                    .OrderBy(d => d.Zone)
                    .ThenBy(d => d.Section)
                    .ThenBy(d => d.Filename)
                    .ThenBy(d => d.Id)
                    .ToListAsync(ct);

            if (matchingDocs.Count <= 50)
            {
                var exportId = "EXP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                var syncNow = clock.UtcNow;
                var zipBytes = await EtmfExportPackager.CreateZipArchiveAsync(
                    exportId,
                    studyId,
                    matchingDocs,
                    storage,
                    syncNow,
                    ct: ct);

                var filename = $"etmf_export_{studyId}_{syncNow:yyyyMMdd_HHmmss}.zip";
                return TypedResults.File(zipBytes, "application/zip", filename);
            }

            count = matchingDocs.Count;
        }

        var jobId = "JOB-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var user = CallerScope.RequiredUserId(httpContext, "An admitted eTMF request has no signed caller.");
        var now = clock.UtcNow;

        var job = new TmfExportJob(jobId)
        {
            StudyId = studyId,
            RequestedBy = user,
            Status = "QUEUED",
            ProgressPercentage = 0.0,
            TotalDocuments = count,
            ProcessedDocuments = 0,
            CreatedAt = now.UtcDateTime,
            ReasonForChange = "Study binder bulk export request"
        };

        dbContext.TmfExportJobs.Add(job);
        await unitOfWork.CommitAsync(ct);

        await exportQueue.EnqueueAsync(jobId, CallerScope.TenantId(httpContext), ct);

        return TypedResults.Accepted(
            uri: $"/api/v1/etmf/export-jobs/{jobId}",
            value: new
            {
                job_id = jobId,
                study_id = job.StudyId,
                requested_by = user,
                status = "QUEUED",
                progress_percentage = 0.0,
                total_documents = count,
                processed_documents = 0,
                download_url = (string?)null,
                expires_at = (DateTimeOffset?)null,
                created_at = job.CreatedAt,
                status_url = $"/api/v1/etmf/export-jobs/{jobId}",
                message = "Export job enqueued successfully for background processing."
            }
        );
    }

    public async Task<IResult> GetExportJobAsync(
        string jobId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        var job = await dbContext.TmfExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null
            || !CallerScope.CanAccessStudy(httpContext, job.StudyId)
            || !ClinicalScopeGuard.HasValidSiteScopeIfRequired(principal))
        {
            return TypedResults.NotFound(new { error = $"Export job '{jobId}' not found." });
        }

        return TypedResults.Ok(new
        {
            job_id = job.Id,
            study_id = job.StudyId,
            requested_by = job.RequestedBy,
            status = job.Status,
            progress_percentage = job.ProgressPercentage,
            total_documents = job.TotalDocuments,
            processed_documents = job.ProcessedDocuments,
            download_url = job.DownloadUrl,
            expires_at = job.ExpiresAt,
            created_at = job.CreatedAt
        });
    }

    public async Task<IResult> DownloadExportJobAsync(
        string jobId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        var job = await dbContext.TmfExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null
            || !CallerScope.CanAccessStudy(httpContext, job.StudyId)
            || !ClinicalScopeGuard.HasValidSiteScopeIfRequired(principal))
        {
            return TypedResults.NotFound(new { error = $"Export job '{jobId}' not found." });
        }

        if (job.Status != "COMPLETED")
        {
            return TypedResults.BadRequest(new { error = $"Export job '{jobId}' is not in COMPLETED status (Current status: {job.Status})." });
        }

        if (job.ExpiresAt.HasValue && clock.UtcNow > job.ExpiresAt.Value)
        {
            return TypedResults.StatusCode(StatusCodes.Status410Gone);
        }

        string downloadFilename = $"etmf_export_{job.StudyId}_{job.Id}.zip";

        if (!ClinicalScopeGuard.IsSiteBound(principal))
        {
            if (storage is not null)
            {
                var exportKey = EtmfExportPackager.BuildExportObjectKey(job.StudyId, job.Id);
                try
                {
                    var (storedZipBytes, _) = await storage.GetObjectAsync(exportKey, ct);
                    return TypedResults.File(storedZipBytes, "application/zip", downloadFilename);
                }
                catch (StorageObjectNotFoundException)
                {
                    // Fall through to repackaging if archive was not persisted in object storage
                }
            }

            var filePath = Path.Combine(Path.GetTempPath(), "etmf_exports", $"{jobId}.zip");
            if (storage is null && File.Exists(filePath))
            {
                var fileStream = File.OpenRead(filePath);
                return TypedResults.File(fileStream, "application/zip", downloadFilename);
            }
        }

        var docs = (await dbContext.TmfDocuments
            .AsNoTracking()
            .Where(d => d.StudyId == job.StudyId)
            .OrderBy(d => d.Zone)
            .ThenBy(d => d.Section)
            .ThenBy(d => d.Filename)
            .ThenBy(d => d.Id)
            .ToListAsync(ct))
            .Where(d => string.IsNullOrWhiteSpace(d.SiteId) || ClinicalScopeGuard.CanAccessSite(httpContext, d.SiteId))
            .ToList();

        var zipBytes = await EtmfExportPackager.CreateZipArchiveAsync(job.Id, job.StudyId, docs, storage, clock.UtcNow, ct: ct);
        return TypedResults.File(zipBytes, "application/zip", downloadFilename);
    }

    public async Task<IResult> ListExportJobsAsync(
        string? studyId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(principal))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        if (!string.IsNullOrWhiteSpace(studyId) && !ClinicalScopeGuard.CanAccessStudy(httpContext, studyId))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.StudyAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        var query = ClinicalScopeGuard.NarrowToStudyScope(dbContext.TmfExportJobs.AsNoTracking(), httpContext, j => j.StudyId);
        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(j => j.StudyId == studyId);
        }

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Select(job => new
            {
                job_id = job.Id,
                study_id = job.StudyId,
                requested_by = job.RequestedBy,
                status = job.Status,
                progress_percentage = job.ProgressPercentage,
                total_documents = job.TotalDocuments,
                processed_documents = job.ProcessedDocuments,
                download_url = job.DownloadUrl,
                expires_at = job.ExpiresAt,
                created_at = job.CreatedAt
            })
            .ToListAsync(ct);

        return TypedResults.Ok(jobs);
    }
}
