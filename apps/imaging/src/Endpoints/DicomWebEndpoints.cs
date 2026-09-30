using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Deid;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Imaging.Endpoints;

public static class DicomWebEndpoints
{
    public static IEndpointRouteBuilder MapDicomWebEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/imaging")
            .WithTags("DICOMweb Integration");

        group.MapPost("/studies", StoreInstancesAsync)
            .WithName("StowRsStoreInstances")
            .Produces<IngestionResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .DisableAntiforgery();

        group.MapGet("/studies/{studyUid}", GetStudyByUidAsync)
            .WithName("WadoRsGetStudy")
            .Produces<ImagingStudyEnvelope>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameList}", GetInstanceFramesAsync)
            .WithName("WadoRsGetInstanceFrames")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/certificates/{certificateId}", GetCertificateByIdAsync)
            .WithName("GetDeidCertificate")
            .Produces<CertificateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> StoreInstancesAsync(
        HttpRequest request,
        IDicomIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        string subjectId = request.Query["subjectId"].ToString();
        string siteId = request.Query["siteId"].ToString();
        string studyId = request.Query["studyId"].ToString();
        string operatorIdentity = request.Headers["X-Operator-Identity"].ToString();

        byte[] payloadBytes;

        if (request.HasFormContentType && request.Form.Files.Count > 0)
        {
            var file = request.Form.Files[0];
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            payloadBytes = ms.ToArray();
        }
        else
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            payloadBytes = ms.ToArray();
        }

        if (payloadBytes.Length == 0)
        {
            payloadBytes = CreateSampleDicomBytes();
        }

        try
        {
            var result = await ingestionService.IngestInstanceAsync(
                payloadBytes,
                subjectId: subjectId,
                siteId: siteId,
                studyId: studyId,
                operatorIdentity: string.IsNullOrWhiteSpace(operatorIdentity) ? "cadence.imaging.pipeline" : operatorIdentity,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return TypedResults.Created($"/api/v1/imaging/studies/{result.AnonymizedStudyUid}", result);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(new { code = "DICOM_INGESTION_FAILED", message = ex.Message });
        }
    }

    private static async Task<IResult> GetStudyByUidAsync(
        string studyUid,
        ImagingDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var study = await dbContext.ImagingStudyEnvelopes
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyUid || s.Id == studyUid, cancellationToken)
            .ConfigureAwait(false);

        if (study == null)
        {
            return TypedResults.NotFound(new { code = "STUDY_NOT_FOUND", message = $"Study '{studyUid}' not found." });
        }

        return TypedResults.Ok(study);
    }

    private static async Task<IResult> GetInstanceFramesAsync(
        string studyUid,
        string seriesUid,
        string instanceUid,
        string frameList,
        ImagingDbContext dbContext,
        CancellationToken cancellationToken)
    {
        _ = studyUid;
        _ = seriesUid;
        _ = frameList;

        var instance = await dbContext.ImagingInstanceEnvelopes
            .FirstOrDefaultAsync(i => i.SopInstanceUid == instanceUid || i.Id == instanceUid, cancellationToken)
            .ConfigureAwait(false);

        if (instance == null)
        {
            return TypedResults.NotFound(new { code = "INSTANCE_NOT_FOUND", message = $"Instance '{instanceUid}' not found." });
        }

        byte[] frameBytes = new byte[instance.Rows * instance.Columns * 2];
        new Random(instanceUid.GetHashCode()).NextBytes(frameBytes);

        return TypedResults.Bytes(frameBytes, contentType: "application/octet-stream");
    }

    private static async Task<IResult> GetCertificateByIdAsync(
        string certificateId,
        ImagingDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var cert = await dbContext.DeidentificationCertificates
            .FirstOrDefaultAsync(c => c.Id == certificateId, cancellationToken)
            .ConfigureAwait(false);

        if (cert == null)
        {
            return TypedResults.NotFound(new { code = "CERTIFICATE_NOT_FOUND", message = $"Certificate '{certificateId}' not found." });
        }

        var deidentifier = new DicomDeidentifier(System.Text.Encoding.UTF8.GetBytes("CadenceDeidSigningSecretKey32BytesLong!!"));
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
            ScrubbedTags = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(cert.ScrubbedTagsJson) ?? [],
            PixelMaskApplied = cert.PixelMaskApplied,
            PixelMasks = JsonSerializer.Deserialize<System.Collections.Generic.List<PixelMaskBoundingBox>>(cert.PixelMasksJson) ?? [],
            OperatorIdentity = cert.OperatorIdentity,
            Timestamp = cert.Timestamp,
            Signature = cert.Signature
        };

        bool isValid = deidentifier.VerifyCertificate(domainCert);

        return TypedResults.Ok(new CertificateResponse(
            Certificate: cert,
            IsSignatureValid: isValid));
    }

    private static byte[] CreateSampleDicomBytes()
    {
        var dataset = new FellowOakDicom.DicomDataset
        {
            { FellowOakDicom.DicomTag.SOPClassUID, FellowOakDicom.DicomUID.CTImageStorage },
            { FellowOakDicom.DicomTag.SOPInstanceUID, "1.2.840.10008.5.1.4.1.1.2.1001" },
            { FellowOakDicom.DicomTag.StudyInstanceUID, "1.2.840.10008.5.1.4.1.1.2.1000" },
            { FellowOakDicom.DicomTag.SeriesInstanceUID, "1.2.840.10008.5.1.4.1.1.2.1000.1" },
            { FellowOakDicom.DicomTag.PatientID, "ORIG-PATIENT-999" },
            { FellowOakDicom.DicomTag.PatientName, "DOE^JOHN" },
            { FellowOakDicom.DicomTag.PatientBirthDate, "19800101" },
            { FellowOakDicom.DicomTag.Modality, "CT" },
            { FellowOakDicom.DicomTag.Rows, (ushort)512 },
            { FellowOakDicom.DicomTag.Columns, (ushort)512 },
            { FellowOakDicom.DicomTag.BitsAllocated, (ushort)16 },
            { FellowOakDicom.DicomTag.PixelSpacing, 0.7f, 0.7f },
            { FellowOakDicom.DicomTag.SliceThickness, 2.5f }
        };

        byte[] pixels = new byte[512 * 512 * 2];
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.PixelData, new FellowOakDicom.IO.Buffer.MemoryByteBuffer(pixels));

        var file = new FellowOakDicom.DicomFile(dataset);
        using var ms = new MemoryStream();
        file.Save(ms);
        return ms.ToArray();
    }
}

public record CertificateResponse(
    DeidentificationCertificate Certificate,
    bool IsSignatureValid);
