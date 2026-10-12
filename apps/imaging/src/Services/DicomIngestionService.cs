using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Deid;
using Cadence.Kernel.Time;
using Cadence.Security;

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

    Task<BatchIngestionResult> IngestBatchAsync(
        BatchIngestionRequest request,
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

public record BatchFileItem(string FileName, byte[] Content);

public record BatchIngestionRequest(
    IReadOnlyList<BatchFileItem> Files,
    string SubjectId,
    string SiteId,
    string StudyId,
    string? TimepointName = null,
    string? OperatorIdentity = "cadence.imaging.pipeline");

public record SeriesIngestionSummary(
    string SeriesEnvelopeId,
    string AnonymizedSeriesUid,
    string Modality,
    int InstanceCount);

public record BatchIngestionResult(
    string StudyEnvelopeId,
    string CertificateId,
    string AnonymizedStudyUid,
    int TotalFilesReceived,
    int IngestedSliceCount,
    int SkippedNonDicomCount,
    IReadOnlyList<string> SkippedNonDicomFiles,
    IReadOnlyList<SeriesIngestionSummary> SeriesSummary,
    BatchDeidentificationManifest Manifest,
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
            SopClassUid = "1.2.840.10008.5.1.4.1.1.2", // CT Image Storage default - deid-ignore
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarAnalyzer.CSharp", "S6966:Use async method", Justification = "ZipArchiveEntry.Open does not have an async overload in .NET")]
    public async Task<BatchIngestionResult> IngestBatchAsync(
        BatchIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string subjectId = string.IsNullOrWhiteSpace(request.SubjectId) ? "SUBJ-001" : request.SubjectId;
        string siteId = string.IsNullOrWhiteSpace(request.SiteId) ? "SITE-001" : request.SiteId;
        string studyId = string.IsNullOrWhiteSpace(request.StudyId) ? "STUDY-001" : request.StudyId;
        string opIdentity = string.IsNullOrWhiteSpace(request.OperatorIdentity) ? "cadence.imaging.pipeline" : request.OperatorIdentity;

        DateTimeOffset now = _clock.UtcNow;

        var expandedFiles = new List<BatchFileItem>();
        if (request.Files != null)
        {
            foreach (var file in request.Files)
            {
                if (IsZipArchive(file))
                {
                    try
                    {
                        using var ms = new MemoryStream(file.Content);
                        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                        foreach (var entry in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                            {
                                continue;
                            }
                            using var entryStream = entry.Open();
                            using var entryMs = new MemoryStream();
                            await entryStream.CopyToAsync(entryMs, cancellationToken).ConfigureAwait(false);
                            expandedFiles.Add(new BatchFileItem(entry.FullName, entryMs.ToArray()));
                        }
                    }
                    catch
                    {
                        expandedFiles.Add(file);
                    }
                }
                else
                {
                    expandedFiles.Add(file);
                }
            }
        }

        var successfulInstances = new ConcurrentBag<(BatchFileItem FileItem, DicomDeidResult DeidResult)>();
        var skippedNonDicomFiles = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(expandedFiles, cancellationToken, (fileItem, ct) =>
        {
            if (fileItem.Content == null || fileItem.Content.Length == 0)
            {
                skippedNonDicomFiles.Add(fileItem.FileName);
                return ValueTask.CompletedTask;
            }

            try
            {
                var deidResult = _deidentifier.Deidentify(
                    fileItem.Content,
                    subjectId: subjectId,
                    siteId: siteId,
                    studyId: studyId,
                    deidentifiedAt: now,
                    applyPixelMask: true,
                    operatorIdentity: opIdentity);

                successfulInstances.Add((fileItem, deidResult));
            }
            catch (DicomPixelDeidException)
            {
                throw;
            }
            catch
            {
                skippedNonDicomFiles.Add(fileItem.FileName);
            }

            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        var validList = successfulInstances.OrderBy(x => x.FileItem.FileName, StringComparer.Ordinal).ToList();
        var skippedList = skippedNonDicomFiles.OrderBy(x => x, StringComparer.Ordinal).ToList();

        if (validList.Count == 0)
        {
            var emptyManifest = new BatchDeidentificationManifest
            {
                ManifestId = Guid.NewGuid().ToString(),
                StudyEnvelopeId = string.Empty,
                StudyId = studyId,
                SiteId = siteId,
                SubjectId = subjectId,
                TotalFilesProcessed = expandedFiles.Count,
                IngestedSliceCount = 0,
                SkippedNonDicomCount = skippedList.Count,
                SkippedNonDicomFiles = skippedList,
                ScrubbedTagsCount = 0,
                ScrubbedTags = [],
                PixelMaskApplied = false,
                PixelMasks = [],
                OperatorIdentity = opIdentity,
                Timestamp = now.UtcDateTime.ToString("o")
            };

            var emptyPayload = CreateBatchManifestPayloadWithoutSignature(emptyManifest);
            emptyManifest.Signature = CanonicalSigner.GenerateCanonicalSignature(emptyPayload, DefaultSigningSecret);

            return new BatchIngestionResult(
                StudyEnvelopeId: string.Empty,
                CertificateId: emptyManifest.ManifestId,
                AnonymizedStudyUid: string.Empty,
                TotalFilesReceived: expandedFiles.Count,
                IngestedSliceCount: 0,
                SkippedNonDicomCount: skippedList.Count,
                SkippedNonDicomFiles: skippedList,
                SeriesSummary: [],
                Manifest: emptyManifest,
                Signature: emptyManifest.Signature);
        }

        string mainAnonymizedStudyUid = validList[0].DeidResult.AnonymizedStudyUid;

        var studyEnvelope = await _dbContext.ImagingStudyEnvelopes
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == mainAnonymizedStudyUid, cancellationToken)
            .ConfigureAwait(false);

        var allModalities = validList.Select(x => x.DeidResult.Modality).Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList();
        var seriesGroups = validList.GroupBy(x => x.DeidResult.AnonymizedSeriesUid).ToList();

        if (studyEnvelope == null)
        {
            studyEnvelope = new ImagingStudyEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                StudyInstanceUid = mainAnonymizedStudyUid,
                StudyId = studyId,
                SiteId = siteId,
                SubjectId = subjectId,
                ModalitiesJson = JsonSerializer.Serialize(allModalities),
                StudyDate = now.ToString("yyyyMMdd"),
                StudyDescription = "Anonymized Clinical Trial DICOM Study",
                SeriesCount = seriesGroups.Count,
                InstanceCount = validList.Count,
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
            studyEnvelope.InstanceCount += validList.Count;
            studyEnvelope.SeriesCount = Math.Max(studyEnvelope.SeriesCount, seriesGroups.Count);
            var existingModalities = JsonSerializer.Deserialize<List<string>>(studyEnvelope.ModalitiesJson) ?? [];
            var mergedModalities = existingModalities.Union(allModalities).Distinct().ToList();
            studyEnvelope.ModalitiesJson = JsonSerializer.Serialize(mergedModalities);
            studyEnvelope.UpdatedAt = now;
        }

        var seriesSummaries = new List<SeriesIngestionSummary>();
        int seriesIndex = 1;

        foreach (var seriesGroup in seriesGroups)
        {
            string seriesUid = seriesGroup.Key;
            var firstInstance = seriesGroup.First().DeidResult;

            var seriesEnvelope = await _dbContext.ImagingSeriesEnvelopes
                .FirstOrDefaultAsync(s => s.StudyEnvelopeId == studyEnvelope.Id && s.SeriesInstanceUid == seriesUid, cancellationToken)
                .ConfigureAwait(false);

            if (seriesEnvelope == null)
            {
                seriesEnvelope = new ImagingSeriesEnvelope
                {
                    Id = Guid.NewGuid().ToString(),
                    StudyEnvelopeId = studyEnvelope.Id,
                    SeriesInstanceUid = seriesUid,
                    SeriesNumber = seriesIndex++,
                    Modality = firstInstance.Modality,
                    SeriesDescription = $"{firstInstance.Modality} Series",
                    InstanceCount = seriesGroup.Count()
                };
                _dbContext.ImagingSeriesEnvelopes.Add(seriesEnvelope);
            }
            else
            {
                seriesEnvelope.InstanceCount += seriesGroup.Count();
            }

            seriesSummaries.Add(new SeriesIngestionSummary(
                SeriesEnvelopeId: seriesEnvelope.Id,
                AnonymizedSeriesUid: seriesUid,
                Modality: firstInstance.Modality,
                InstanceCount: seriesEnvelope.InstanceCount));

            int instNum = 1;
            foreach (var (_, deidResult) in seriesGroup)
            {
                var instEnvelope = new ImagingInstanceEnvelope
                {
                    Id = Guid.NewGuid().ToString(),
                    SeriesEnvelopeId = seriesEnvelope.Id,
                    SopInstanceUid = deidResult.AnonymizedSopUid,
                    SopClassUid = "1.2.840.10008.5.1.4.1.1.2", // deid-ignore
                    InstanceNumber = instNum++,
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
                    BurnedInPhiMasked = deidResult.Certificate.PixelMaskApplied
                };
                _dbContext.ImagingInstanceEnvelopes.Add(instEnvelope);
            }
        }

        var distinctScrubbedTags = validList
            .SelectMany(x => x.DeidResult.Certificate.ScrubbedTags)
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var allPixelMasks = validList
            .SelectMany(x => x.DeidResult.Certificate.PixelMasks)
            .Distinct()
            .ToList();

        bool anyPixelMask = validList.Any(x => x.DeidResult.Certificate.PixelMaskApplied);

        var manifest = new BatchDeidentificationManifest
        {
            ManifestId = Guid.NewGuid().ToString(),
            StudyEnvelopeId = studyEnvelope.Id,
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = subjectId,
            TotalFilesProcessed = expandedFiles.Count,
            IngestedSliceCount = validList.Count,
            SkippedNonDicomCount = skippedList.Count,
            SkippedNonDicomFiles = skippedList,
            ScrubbedTagsCount = distinctScrubbedTags.Count,
            ScrubbedTags = distinctScrubbedTags,
            PixelMaskApplied = anyPixelMask,
            PixelMasks = allPixelMasks,
            OperatorIdentity = opIdentity,
            Timestamp = now.UtcDateTime.ToString("o")
        };

        var manifestPayload = CreateBatchManifestPayloadWithoutSignature(manifest);
        string signature = CanonicalSigner.GenerateCanonicalSignature(manifestPayload, DefaultSigningSecret);
        manifest.Signature = signature;

        var certEntity = new DeidentificationCertificate
        {
            Id = manifest.ManifestId,
            StudyEnvelopeId = studyEnvelope.Id,
            SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signature))),
            TargetSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ManifestId))),
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = subjectId,
            OriginalStudyInstanceUidHash = validList[0].DeidResult.Certificate.OriginalStudyInstanceUidHash,
            OriginalSopInstanceUidHash = validList[0].DeidResult.Certificate.OriginalSopInstanceUidHash,
            AnonymizedStudyInstanceUid = mainAnonymizedStudyUid,
            AnonymizedSopInstanceUid = validList[0].DeidResult.AnonymizedSopUid,
            ScrubbedTagsCount = manifest.ScrubbedTagsCount,
            ScrubbedTagsJson = JsonSerializer.Serialize(manifest.ScrubbedTags),
            PixelMaskApplied = manifest.PixelMaskApplied,
            PixelMasksJson = JsonSerializer.Serialize(manifest.PixelMasks),
            OperatorIdentity = opIdentity,
            Timestamp = manifest.Timestamp,
            Signature = signature,
            CreatedAt = now
        };

        _dbContext.DeidentificationCertificates.Add(certEntity);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new BatchIngestionResult(
            StudyEnvelopeId: studyEnvelope.Id,
            CertificateId: certEntity.Id,
            AnonymizedStudyUid: mainAnonymizedStudyUid,
            TotalFilesReceived: expandedFiles.Count,
            IngestedSliceCount: validList.Count,
            SkippedNonDicomCount: skippedList.Count,
            SkippedNonDicomFiles: skippedList,
            SeriesSummary: seriesSummaries,
            Manifest: manifest,
            Signature: signature);
    }

    private static bool IsZipArchive(BatchFileItem item)
    {
        if (item == null || item.Content == null || item.Content.Length < 4)
        {
            return false;
        }

        if (item.FileName != null && item.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return item.Content[0] == 0x50 && item.Content[1] == 0x4B && item.Content[2] == 0x03 && item.Content[3] == 0x04;
    }

    private static object CreateBatchManifestPayloadWithoutSignature(BatchDeidentificationManifest manifest)
    {
        return new Dictionary<string, object?>
        {
            ["manifest_id"] = manifest.ManifestId,
            ["study_envelope_id"] = manifest.StudyEnvelopeId,
            ["study_id"] = manifest.StudyId,
            ["site_id"] = manifest.SiteId,
            ["subject_id"] = manifest.SubjectId,
            ["total_files_processed"] = manifest.TotalFilesProcessed,
            ["ingested_slice_count"] = manifest.IngestedSliceCount,
            ["skipped_non_dicom_count"] = manifest.SkippedNonDicomCount,
            ["skipped_non_dicom_files"] = manifest.SkippedNonDicomFiles,
            ["scrubbed_tags_count"] = manifest.ScrubbedTagsCount,
            ["scrubbed_tags"] = manifest.ScrubbedTags,
            ["pixel_mask_applied"] = manifest.PixelMaskApplied,
            ["pixel_masks"] = manifest.PixelMasks,
            ["operator_identity"] = manifest.OperatorIdentity,
            ["timestamp"] = manifest.Timestamp
        };
    }
}
