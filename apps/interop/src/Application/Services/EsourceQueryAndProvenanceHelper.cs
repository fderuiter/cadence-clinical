using System;
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

/// <summary>
/// Handles eSource discrepancy clinical queries, provenance audit lookups, and candidate wire DTO mapping.
/// </summary>
internal static class EsourceQueryAndProvenanceHelper
{
    public static async Task<IResult> RaiseClinicalQueryAsync(
        InteropDbContext db,
        IInteropUnitOfWork unitOfWork,
        IClock clock,
        EhrReconcileQueryRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateId))
        {
            return Results.Problem("CandidateId is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.QueryExplanation))
        {
            return Results.Problem("Discrepancy explanation is required to raise a clinical query.", statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await db.EhrReconciliationCandidateRecords
            .FirstOrDefaultAsync(c => c.Id == request.CandidateId, ct)
            .ConfigureAwait(false);
        if (item == null)
        {
            return Results.Problem($"Candidate record '{request.CandidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var callerUser = CallerScope.RequiredUserId(httpContext, "An admitted eSource request has no signed caller.");
        var callerRole = CallerScope.Roles(httpContext);
        var now = clock.UtcNow;

        var query = new ClinicalQuery
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = item.StudyId,
            SubjectId = item.SubjectId,
            Domain = item.TargetDomain,
            TestCode = item.CdashTestcd ?? item.TargetVariable,
            Status = "OPEN",
            Explanation = request.QueryExplanation.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ClinicalQueries.Add(query);

        item.ClinicalQueryId = query.Id;
        item.Status = "QUERIED";
        item.ResolutionAction = "QUERY";
        item.ResolutionReason = request.QueryExplanation.Trim();
        item.ResolvedBy = callerUser;
        item.ResolvedAt = now;
        item.VersionIndex += 1;

        db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = callerUser,
            UserRole = callerRole,
            Action = "ESOURCE_CLINICAL_QUERY_RAISED",
            Details = $"Raised clinical query #{query.Id} for candidate {item.Id} ({item.TargetVariable}). Discrepancy: {request.QueryExplanation}",
            ChangeReason = request.ChangeReason ?? "CRC Raised eSource Discrepancy Query"
        });

        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new EhrReconcileActionResponse(
            Success: true,
            Candidate: ToDto(item),
            ClinicalQueryId: query.Id,
            Message: $"Clinical query #{query.Id} opened for record {item.TargetVariable}."));
    }

    public static async Task<IResult> GetProvenanceAuditAsync(
        InteropDbContext db,
        string candidateId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var item = await db.EhrReconciliationCandidateRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == candidateId, ct)
            .ConfigureAwait(false);
        if (item == null || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return Results.Problem($"Candidate record '{candidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        string candidateMarker = $"candidate {item.Id}";
        string candidateSpaceMarker = $"candidate {item.Id} ";
        var auditLogs = (await db.InteropAuditLogs.AsNoTracking()
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

    private static object ParseJsonOrRaw(string? json)
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
