using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Interop.Application.Services;

public sealed class EsourceIngestionAndQueryService(InteropUnitOfWork uow, IClock clock)
{
    public async Task<IResult> IngestFhirBundleAsync(
        FhirBundleIngestRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StudyId) || string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Results.Problem("StudyId and SubjectId are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.SiteId) || string.IsNullOrWhiteSpace(request.EhrSystemId))
        {
            return Results.Problem("SiteId and EhrSystemId are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        string studyId = request.StudyId.Trim();
        string siteId = request.SiteId.Trim();
        string subjectId = request.SubjectId.Trim();
        string ehrSystemId = request.EhrSystemId.Trim();
        string subjectPseudonym = string.IsNullOrWhiteSpace(request.SubjectPseudonym)
            ? subjectId
            : request.SubjectPseudonym.Trim();

        if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId) is not null)
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var conflictingSites = await uow.Db.EhrReconciliationCandidateRecords
            .AsNoTracking()
            .Where(c => c.StudyId == studyId && c.SubjectId == subjectId && c.SiteId != siteId)
            .Select(c => c.SiteId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (conflictingSites.Exists(existingSite => ClinicalScopeGuard.RequireStudyAndSite(httpContext, studyId, existingSite) is not null))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        if (conflictingSites.Count > 0)
        {
            return Results.Problem(
                title: "Conflicting subject site",
                detail: "Reconciliation candidates for this subject are already recorded under another site in this study.",
                statusCode: StatusCodes.Status409Conflict);
        }

        FhirObservationMapper.Result mapped;
        try
        {
            mapped = FhirObservationMapper.Map(
                request.RawFhirBundleJson ?? string.Empty,
                new FhirObservationMapper.Target(
                    subjectId,
                    subjectPseudonym,
                    ehrSystemId),
                clock.UtcNow);
        }
        catch (FormatException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (mapped.Candidates.Count == 0)
        {
            return Results.Problem(
                "The bundle holds no Observation that maps to a supported CDASH variable.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["skipped"] = mapped.Skipped });
        }

        var dtos = new List<EhrCandidateItemDto>();
        foreach (var candidate in mapped.Candidates.DistinctBy(c => (c.FhirResourceId, c.TargetVariable)))
        {
            candidate.StudyId = studyId;
            candidate.SiteId = siteId;

            var existing = await uow.Db.EhrReconciliationCandidateRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    c => c.StudyId == candidate.StudyId
                        && c.SiteId == candidate.SiteId
                        && c.SubjectId == candidate.SubjectId
                        && c.FhirResourceId == candidate.FhirResourceId
                        && c.TargetVariable == candidate.TargetVariable,
                    ct)
                .ConfigureAwait(false);

            if (existing is null)
            {
                uow.Db.EhrReconciliationCandidateRecords.Add(candidate);
                dtos.Add(ToDto(candidate));
            }
            else
            {
                if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, existing.StudyId, existing.SiteId) is not null)
                {
                    return ClinicalScopeGuard.OutsideStudy();
                }

                dtos.Add(ToDto(existing));
            }
        }

        await uow.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new FhirBundleIngestResponse(
            Success: true,
            CandidateCount: dtos.Count,
            Candidates: dtos,
            Message: mapped.Skipped.Count == 0
                ? $"Ingested {dtos.Count} Observation(s) for subject {subjectId}."
                : $"Ingested {dtos.Count} Observation(s) for subject {subjectId}; skipped {mapped.Skipped.Count}: {string.Join("; ", mapped.Skipped)}."));
    }

    public async Task<IResult> ListCandidatesAsync(
        string? studyId,
        string? subjectId,
        string? siteId,
        string? domain,
        string? status,
        int skip,
        int limit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        HashSet<string>? siteFilter = null;
        if (ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null
            && !ClinicalScopeGuard.ResolveStudyAndSiteFilters(httpContext, studyId, siteId, out _, out siteFilter, out var scopeDenied))
        {
            return scopeDenied!;
        }

        var query = ClinicalScopeGuard.NarrowToStudyScope(uow.Db.EhrReconciliationCandidateRecords.AsNoTracking(), httpContext, c => c.StudyId);
        if (siteFilter is not null)
        {
            var allowedSites = siteFilter.ToList();
            query = query.Where(c => c.SiteId != null && allowedSites.Contains(c.SiteId));
        }

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(c => c.StudyId == studyId);
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(c => c.SubjectId == subjectId || c.SubjectPseudonym == subjectId);
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            query = query.Where(c => c.SiteId == siteId);
        }

        if (!string.IsNullOrWhiteSpace(domain) && domain != "ALL")
        {
            query = query.Where(c => c.TargetDomain == domain);
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            query = query.Where(c => c.Status == status);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip(skip > 0 ? skip : 0)
            .Take(limit > 0 ? limit : 50)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = items.Select(ToDto).ToList();
        return Results.Ok(new EhrCandidateListResponse(dtos, total));
    }

    public async Task<IResult> GetCandidateByIdAsync(
        string candidateId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var item = await uow.Db.EhrReconciliationCandidateRecords.AsNoTracking().FirstOrDefaultAsync(c => c.Id == candidateId, ct).ConfigureAwait(false);
        if (item == null || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return Results.Problem($"Candidate record '{candidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(ToDto(item));
    }

    public async Task<IResult> GetProvenanceAuditAsync(
        string candidateId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var item = await uow.Db.EhrReconciliationCandidateRecords.AsNoTracking().FirstOrDefaultAsync(c => c.Id == candidateId, ct).ConfigureAwait(false);
        if (item == null || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return Results.Problem($"Candidate record '{candidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        string candidateMarker = $"candidate {item.Id}";
        string candidateSpaceMarker = $"candidate {item.Id} ";
        var auditLogs = (await uow.Db.InteropAuditLogs.AsNoTracking()
            .Where(a => a.Details.Contains(candidateMarker))
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct)
            .ConfigureAwait(false))
            .Where(a => a.Details.Contains(candidateSpaceMarker, StringComparison.Ordinal)
                || a.Details.EndsWith(candidateMarker, StringComparison.Ordinal))
            .ToList();

        object candVal = ParseJsonOrRaw(item.CandidateValueJson);
        object? ecrfVal = ParseJsonOrRaw(item.CurrentEcrfValueJson);
        object rawFhir = ParseJsonOrRaw(item.RawFhirResourceJson);
        object? eSig = ParseJsonOrRaw(item.ElectronicSignatureJson);

        return Results.Ok(new EhrProvenanceResponse(
            CandidateId: item.Id,
            StudyId: item.StudyId,
            SiteId: item.SiteId,
            SubjectId: item.SubjectId,
            SubjectPseudonym: item.SubjectPseudonym,
            EhrSystemId: item.EhrSystemId,
            FhirResourceType: item.FhirResourceType,
            FhirResourceId: item.FhirResourceId,
            SourceTimestamp: item.SourceTimestamp,
            TargetDomain: item.TargetDomain,
            TargetVariable: item.TargetVariable,
            CdashTestcd: item.CdashTestcd,
            CdashTest: item.CdashTest,
            CandidateValue: candVal,
            CandidateUnit: item.CandidateUnit,
            CurrentEcrfValue: ecrfVal,
            CurrentEcrfUpdatedAt: item.CurrentEcrfUpdatedAt,
            DiscrepancyType: item.DiscrepancyType,
            ConfidenceScore: item.ConfidenceScore,
            MappingProvenance: item.MappingProvenance,
            RawFhirResource: rawFhir,
            Status: item.Status,
            ResolutionAction: item.ResolutionAction,
            ResolutionReason: item.ResolutionReason,
            ResolvedBy: item.ResolvedBy,
            ResolvedAt: item.ResolvedAt,
            ElectronicSignature: eSig,
            ClinicalQueryId: item.ClinicalQueryId,
            CreatedAt: item.CreatedAt,
            VersionIndex: item.VersionIndex,
            AuditTrail: auditLogs));
    }

    internal static EhrCandidateItemDto ToDto(EhrReconciliationCandidateRecord item)
    {
        object candVal = ParseJsonOrRaw(item.CandidateValueJson);
        object? ecrfVal = ParseJsonOrRaw(item.CurrentEcrfValueJson);
        object rawFhir = ParseJsonOrRaw(item.RawFhirResourceJson);
        object? eSig = ParseJsonOrRaw(item.ElectronicSignatureJson);

        return new EhrCandidateItemDto(
            Id: item.Id,
            StudyId: item.StudyId,
            SiteId: item.SiteId,
            SubjectId: item.SubjectId,
            SubjectPseudonym: string.IsNullOrWhiteSpace(item.SubjectPseudonym) ? item.SubjectId : item.SubjectPseudonym,
            EhrSystemId: item.EhrSystemId,
            FhirResourceType: item.FhirResourceType,
            FhirResourceId: item.FhirResourceId,
            SourceTimestamp: item.SourceTimestamp,
            TargetDomain: item.TargetDomain,
            TargetVariable: item.TargetVariable,
            CdashTestcd: item.CdashTestcd,
            CdashTest: item.CdashTest,
            CandidateValue: candVal,
            CandidateUnit: item.CandidateUnit,
            CurrentEcrfValue: ecrfVal,
            CurrentEcrfUpdatedAt: item.CurrentEcrfUpdatedAt,
            DiscrepancyType: item.DiscrepancyType,
            ConfidenceScore: item.ConfidenceScore,
            MappingProvenance: item.MappingProvenance,
            RawFhirResource: rawFhir,
            Status: item.Status,
            ResolutionAction: item.ResolutionAction,
            ResolutionReason: item.ResolutionReason,
            ResolvedBy: item.ResolvedBy,
            ResolvedAt: item.ResolvedAt,
            ElectronicSignature: eSig,
            ClinicalQueryId: item.ClinicalQueryId,
            CreatedAt: item.CreatedAt,
            VersionIndex: item.VersionIndex);
    }

    internal static object ParseJsonOrRaw(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return json;
        }
    }
}
