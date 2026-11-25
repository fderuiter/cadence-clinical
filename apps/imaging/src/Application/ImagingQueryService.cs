using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Endpoints;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Deid;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Imaging.Application;

public interface IImagingQueryService
{
    Task<IResult> GetStudyByUidAsync(
        string studyUid,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> GetInstanceFramesAsync(
        string studyUid,
        string seriesUid,
        string instanceUid,
        string frameList,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> GetCertificateByIdAsync(
        string certificateId,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

public sealed class ImagingQueryService(
    ImagingDbContext dbContext,
    DicomDeidentifier deidentifier) : IImagingQueryService
{
    public async Task<IResult> GetStudyByUidAsync(
        string studyUid,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var study = await dbContext.ImagingStudyEnvelopes
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyUid || s.Id == studyUid, cancellationToken)
            .ConfigureAwait(false);

        if (study == null
            || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, study.StudyId, study.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { code = "STUDY_NOT_FOUND", message = $"Study '{studyUid}' not found." });
        }

        return TypedResults.Ok(study);
    }

    public async Task<IResult> GetInstanceFramesAsync(
        string studyUid,
        string seriesUid,
        string instanceUid,
        string frameList,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        _ = frameList;

        if (string.IsNullOrWhiteSpace(studyUid)
            || string.IsNullOrWhiteSpace(seriesUid)
            || string.IsNullOrWhiteSpace(instanceUid))
        {
            return TypedResults.NotFound(new { code = "INSTANCE_NOT_FOUND", message = $"Instance '{instanceUid}' not found." });
        }

        var instance = await dbContext.ImagingInstanceEnvelopes
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.SopInstanceUid == instanceUid || i.Id == instanceUid, cancellationToken)
            .ConfigureAwait(false);

        var parentEnvelope = instance == null
            ? null
            : await (from series in dbContext.ImagingSeriesEnvelopes.AsNoTracking()
                     join study in dbContext.ImagingStudyEnvelopes.AsNoTracking() on series.StudyEnvelopeId equals study.Id
                     where series.Id == instance.SeriesEnvelopeId
                     select new
                     {
                         study.Id,
                         study.StudyInstanceUid,
                         study.StudyId,
                         study.SiteId,
                         SeriesEnvelopeId = series.Id,
                         series.SeriesInstanceUid
                     })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        if (instance == null
            || parentEnvelope == null
            || (parentEnvelope.StudyInstanceUid != studyUid && parentEnvelope.Id != studyUid)
            || (parentEnvelope.SeriesInstanceUid != seriesUid && parentEnvelope.SeriesEnvelopeId != seriesUid)
            || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, parentEnvelope.StudyId, parentEnvelope.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { code = "INSTANCE_NOT_FOUND", message = $"Instance '{instanceUid}' not found." });
        }

        long maxFrameBytes = DicomIngestionService.MaxFrameSizeBytes;
        long frameByteLength = instance.Rows > 0 && instance.Columns > 0
            ? (long)instance.Rows * instance.Columns * 2L
            : 0L;

        if (instance.Rows <= 0 || instance.Columns <= 0 || frameByteLength <= 0 || frameByteLength > maxFrameBytes)
        {
            return TypedResults.Problem(
                title: "Invalid DICOM frame dimensions",
                detail: $"Instance '{instanceUid}' frame dimensions ({instance.Rows}x{instance.Columns}) must be positive and require at most {maxFrameBytes / (1024 * 1024)} MB.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        byte[] frameBytes = new byte[(int)frameByteLength];
        new Random(instanceUid.GetHashCode()).NextBytes(frameBytes);

        return TypedResults.Bytes(frameBytes, contentType: "application/octet-stream");
    }

    public async Task<IResult> GetCertificateByIdAsync(
        string certificateId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var cert = await dbContext.DeidentificationCertificates
            .FirstOrDefaultAsync(c => c.Id == certificateId, cancellationToken)
            .ConfigureAwait(false);

        if (cert == null || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, cert.StudyId, cert.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { code = "CERTIFICATE_NOT_FOUND", message = $"Certificate '{certificateId}' not found." });
        }

        var domainCert = new DicomDeidCertificate
        {
            CertificateId = cert.Id,
            SourceSha256 = cert.SourceSha256,
            TargetSha256 = cert.TargetSha256,
            StudyId = cert.StudyId,
            SiteId = cert.SiteId,
            SubjectId = cert.SubjectId,
            OriginalStudyInstanceUidHash = cert.OriginalStudyInstanceUidHash,
            OriginalSopInstanceUidHash = cert.OriginalSopInstanceUidHash,
            AnonymizedStudyInstanceUid = cert.AnonymizedStudyInstanceUid,
            AnonymizedSopInstanceUid = cert.AnonymizedSopInstanceUid,
            ScrubbedTagsCount = cert.ScrubbedTagsCount,
            ScrubbedTags = JsonSerializer.Deserialize<List<string>>(cert.ScrubbedTagsJson) ?? [],
            PixelMaskApplied = cert.PixelMaskApplied,
            PixelMasks = JsonSerializer.Deserialize<List<PixelMaskBoundingBox>>(cert.PixelMasksJson) ?? [],
            OperatorIdentity = cert.OperatorIdentity,
            Timestamp = cert.Timestamp,
            Signature = cert.Signature
        };

        bool isValid = deidentifier.VerifyCertificate(domainCert);

        return TypedResults.Ok(new CertificateResponse(
            Certificate: cert,
            IsSignatureValid: isValid));
    }
}
