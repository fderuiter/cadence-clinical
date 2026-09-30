using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Deid;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Imaging.Services;

public interface IDicomIngestionService
{
    Task<IngestionResult> IngestInstanceAsync(
        byte[] dicomBytes,
        string subjectId,
        string siteId,
        string studyId,
        string? operatorIdentity,
        CancellationToken cancellationToken = default);
}

public record IngestInstanceRequest(
    string SubjectId,
    string SiteId,
    string StudyId,
    string? OperatorIdentity = "cadence.imaging.pipeline");

public record IngestionResult(
    string StudyEnvelopeId,
    string CertificateId,
    string AnonymizedStudyUid,
    string AnonymizedSeriesUid,
    string AnonymizedSopUid,
    string Modality,
    int Rows,
    int Columns,
    int BitsAllocated,
    bool PixelMaskApplied,
    string Signature);

public sealed class DicomIngestionService : IDicomIngestionService
{
    private static readonly byte[] DefaultSigningSecret = Encoding.UTF8.GetBytes("CadenceDeidSigningSecretKey32BytesLong!!");
    private readonly ImagingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly DicomDeidentifier _deidentifier;

    public DicomIngestionService(ImagingDbContext dbContext, IClock? clock = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? SystemClock.Instance;
        _deidentifier = new DicomDeidentifier(DefaultSigningSecret);
    }

    public async Task<IngestionResult> IngestInstanceAsync(
        byte[] dicomBytes,
        string subjectId,
        string siteId,
        string studyId,
        string? operatorIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dicomBytes);
        if (dicomBytes.Length == 0)
        {
            throw new ArgumentException("DICOM payload cannot be empty.", nameof(dicomBytes));
        }

        subjectId = string.IsNullOrWhiteSpace(subjectId) ? "SUBJ-001" : subjectId;
        siteId = string.IsNullOrWhiteSpace(siteId) ? "SITE-001" : siteId;
        studyId = string.IsNullOrWhiteSpace(studyId) ? "STUDY-001" : studyId;
        string opIdentity = string.IsNullOrWhiteSpace(operatorIdentity) ? "cadence.imaging.pipeline" : operatorIdentity;

        DateTimeOffset now = _clock.UtcNow;

        // Execute automated C# de-identification (50+ tags, date shift, pixel masking, certificate signing)
        var deidResult = _deidentifier.Deidentify(
            dicomBytes,
            subjectId: subjectId,
            siteId: siteId,
            studyId: studyId,
            deidentifiedAt: now,
            applyPixelMask: true,
            operatorIdentity: opIdentity);

        var certEntity = new DeidentificationCertificate
        {
            Id = Guid.NewGuid().ToString(),
            SourceSha256 = deidResult.Certificate.SourceSha256,
            TargetSha256 = deidResult.Certificate.TargetSha256,
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = subjectId,
            OriginalStudyInstanceUidHash = deidResult.Certificate.OriginalStudyInstanceUidHash,
            OriginalSopInstanceUidHash = deidResult.Certificate.OriginalSopInstanceUidHash,
            AnonymizedStudyInstanceUid = deidResult.AnonymizedStudyUid,
            AnonymizedSopInstanceUid = deidResult.AnonymizedSopUid,
            ScrubbedTagsCount = deidResult.Certificate.ScrubbedTagsCount,
            ScrubbedTagsJson = JsonSerializer.Serialize(deidResult.Certificate.ScrubbedTags),
            PixelMaskApplied = deidResult.Certificate.PixelMaskApplied,
            PixelMasksJson = JsonSerializer.Serialize(deidResult.Certificate.PixelMasks),
            OperatorIdentity = opIdentity,
            Timestamp = deidResult.Certificate.Timestamp,
            Signature = deidResult.Certificate.Signature ?? string.Empty,
            CreatedAt = now
        };

        _dbContext.DeidentificationCertificates.Add(certEntity);

        // Fetch or create Study Envelope
        var studyEnvelope = await _dbContext.ImagingStudyEnvelopes
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == deidResult.AnonymizedStudyUid, cancellationToken)
            .ConfigureAwait(false);

        if (studyEnvelope == null)
        {
            studyEnvelope = new ImagingStudyEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                StudyInstanceUid = deidResult.AnonymizedStudyUid,
                StudyId = studyId,
                SiteId = siteId,
                SubjectId = subjectId,
                ModalitiesJson = JsonSerializer.Serialize(new[] { deidResult.Modality }),
                StudyDate = now.ToString("yyyyMMdd"),
                StudyDescription = "Anonymized Clinical Trial DICOM Study",
                SeriesCount = 1,
                InstanceCount = 1,
                Status = "STORED",
                AdjudicationStatus = "UNASSIGNED",
                CreatedAt = now,
                CreatedBy = opIdentity,
                UpdatedAt = now,
                VersionIndex = 1
            };
            _dbContext.ImagingStudyEnvelopes.Add(studyEnvelope);
        }
        else
        {
            studyEnvelope.InstanceCount++;
            studyEnvelope.UpdatedAt = now;
        }

        certEntity.StudyEnvelopeId = studyEnvelope.Id;

        // Fetch or create Series Envelope
        var seriesEnvelope = await _dbContext.ImagingSeriesEnvelopes
            .FirstOrDefaultAsync(s => s.StudyEnvelopeId == studyEnvelope.Id && s.SeriesInstanceUid == deidResult.AnonymizedSeriesUid, cancellationToken)
            .ConfigureAwait(false);

        if (seriesEnvelope == null)
        {
            seriesEnvelope = new ImagingSeriesEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                StudyEnvelopeId = studyEnvelope.Id,
                SeriesInstanceUid = deidResult.AnonymizedSeriesUid,
                SeriesNumber = 1,
                Modality = deidResult.Modality,
                SeriesDescription = $"{deidResult.Modality} Series",
                InstanceCount = 1
            };
            _dbContext.ImagingSeriesEnvelopes.Add(seriesEnvelope);
        }
        else
        {
            seriesEnvelope.InstanceCount++;
        }

        // Create Instance Envelope
        var instanceEnvelope = new ImagingInstanceEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            SeriesEnvelopeId = seriesEnvelope.Id,
            SopInstanceUid = deidResult.AnonymizedSopUid,
            SopClassUid = "1.2.840.10008.5.1.4.1.1.2", // CT Image Storage default
            InstanceNumber = seriesEnvelope.InstanceCount,
            Rows = deidResult.Rows,
            Columns = deidResult.Columns,
            BitsAllocated = deidResult.BitsAllocated,
            PixelSpacingJson = JsonSerializer.Serialize(deidResult.PixelSpacing),
            SliceThickness = deidResult.SliceThickness,
            SliceLocation = deidResult.SliceLocation,
            WindowCenter = deidResult.WindowCenter,
            WindowWidth = deidResult.WindowWidth,
            ObjectKey = $"dicom/{studyEnvelope.Id}/{seriesEnvelope.Id}/{deidResult.AnonymizedSopUid}.dcm",
            ChecksumSha256 = deidResult.Certificate.TargetSha256,
            SizeBytes = deidResult.DeidentifiedBytes.Length,
            BurnedInPhiMasked = deidResult.Certificate.PixelMaskApplied,
            CertificateId = certEntity.Id
        };

        _dbContext.ImagingInstanceEnvelopes.Add(instanceEnvelope);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new IngestionResult(
            StudyEnvelopeId: studyEnvelope.Id,
            CertificateId: certEntity.Id,
            AnonymizedStudyUid: deidResult.AnonymizedStudyUid,
            AnonymizedSeriesUid: deidResult.AnonymizedSeriesUid,
            AnonymizedSopUid: deidResult.AnonymizedSopUid,
            Modality: deidResult.Modality,
            Rows: deidResult.Rows,
            Columns: deidResult.Columns,
            BitsAllocated: deidResult.BitsAllocated,
            PixelMaskApplied: deidResult.Certificate.PixelMaskApplied,
            Signature: certEntity.Signature);
    }
}
