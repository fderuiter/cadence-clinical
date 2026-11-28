using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Deid;
using Cadence.Kernel.Time;
using Cadence.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

public sealed record BatchStreamItem(
    string FileName,
    Stream ContentStream,
    bool? IsZipHint = null);

public record BatchIngestionRequest(
    IReadOnlyList<BatchFileItem> Files,
    string SubjectId,
    string SiteId,
    string StudyId,
    string? TimepointName = null,
    string? OperatorIdentity = "cadence.imaging.pipeline")
{
    public IAsyncEnumerable<BatchStreamItem>? StreamFiles { get; init; }
}

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
    public const long MaxFrameSizeBytes = 64L * 1024 * 1024; // 64 MiB

    private readonly ImagingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly DicomDeidentifier _deidentifier;
    private readonly IScratchWorkspaceProvider _scratchWorkspace;
    private readonly BatchArchiveQuotas _quotas;

    internal IScratchWorkspaceProvider ScratchWorkspace => _scratchWorkspace;

    /// <summary>
    /// Creates the service. <paramref name="deidentifier"/> carries the configured signing and pseudonymization keys
    /// (<c>AddImagingDeidentification</c>, #5399); certificates and batch manifests are signed with it.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public DicomIngestionService(
        ImagingDbContext dbContext,
        DicomDeidentifier deidentifier,
        IScratchWorkspaceProvider scratchWorkspace,
        IClock? clock = null)
        : this(dbContext, deidentifier, scratchWorkspace, clock, quotas: null)
    {
    }

    public DicomIngestionService(
        ImagingDbContext dbContext,
        DicomDeidentifier deidentifier,
        IScratchWorkspaceProvider scratchWorkspace,
        IClock? clock,
        BatchArchiveQuotas? quotas)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _deidentifier = deidentifier ?? throw new ArgumentNullException(nameof(deidentifier));
        _scratchWorkspace = scratchWorkspace ?? throw new ArgumentNullException(nameof(scratchWorkspace));
        _clock = clock ?? SystemClock.Instance;
        _quotas = quotas ?? BatchArchiveQuotas.Default;
    }

    public static void ValidateFrameDimensions(int rows, int columns, int bitsAllocated = 16)
    {
        if (rows <= 0 || columns <= 0)
        {
            throw new DicomInvalidDimensionsException(
                $"DICOM Rows and Columns must be positive (got {rows}x{columns}).");
        }

        if (bitsAllocated <= 0 || bitsAllocated > 64)
        {
            throw new DicomInvalidDimensionsException(
                $"DICOM BitsAllocated ({bitsAllocated}) must be between 1 and 64.");
        }

        long bytesPerPixel = Math.Max(2L, (bitsAllocated + 7L) / 8L);
        long pixelCount = (long)rows * columns;
        if (pixelCount <= 0 || pixelCount > MaxFrameSizeBytes / bytesPerPixel)
        {
            long frameBytes = pixelCount > long.MaxValue / bytesPerPixel
                ? long.MaxValue
                : pixelCount * bytesPerPixel;
            throw new DicomInvalidDimensionsException(
                $"DICOM frame buffer ({rows}x{columns}, {frameBytes} bytes) exceeds maximum {MaxFrameSizeBytes} byte (64 MB) limit.");
        }
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

        if (dicomBytes.Length > _quotas.MaxEntryUncompressedBytes)
        {
            throw new BatchPayloadTooLargeException(
                $"DICOM instance payload ({dicomBytes.Length} bytes) exceeds maximum {_quotas.MaxEntryUncompressedBytes} byte limit.");
        }

        ValidateScopeIdentifiers(subjectId, siteId, studyId);
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

        ValidateFrameDimensions(deidResult.Rows, deidResult.Columns, deidResult.BitsAllocated);

        // Fetch or create Study Envelope and verify ownership against (studyId, siteId, subjectId)
        var studyEnvelope = await _dbContext.ImagingStudyEnvelopes
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == deidResult.AnonymizedStudyUid, cancellationToken)
            .ConfigureAwait(false);

        if (studyEnvelope != null)
        {
            EnsureStudyEnvelopeOwnership(studyEnvelope, studyId, siteId, subjectId);
        }

        var existingSopStudyOwner = await (
                from inst in _dbContext.ImagingInstanceEnvelopes
                join series in _dbContext.ImagingSeriesEnvelopes on inst.SeriesEnvelopeId equals series.Id
                join study in _dbContext.ImagingStudyEnvelopes on series.StudyEnvelopeId equals study.Id
                where inst.SopInstanceUid == deidResult.AnonymizedSopUid
                select new { study.StudyId, study.SiteId, study.SubjectId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingSopStudyOwner != null
            && !IsMatchingScope(existingSopStudyOwner.StudyId, existingSopStudyOwner.SiteId, existingSopStudyOwner.SubjectId, studyId, siteId, subjectId))
        {
            throw new InvalidOperationException("Existing imaging instance envelope ownership does not match the requested study, site, and subject.");
        }

        var certEntity = new DeidentificationCertificate
        {
            // The stored id is the signed certificate_id, so GET /certificates/{id} can verify the signature.
            Id = deidResult.Certificate.CertificateId,
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

        bool isNewStudyEnvelope = studyEnvelope == null;
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
            if (!string.IsNullOrEmpty(deidResult.Modality))
            {
                var existingModalities = JsonSerializer.Deserialize<List<string>>(studyEnvelope.ModalitiesJson) ?? [];
                if (!existingModalities.Contains(deidResult.Modality, StringComparer.Ordinal))
                {
                    existingModalities.Add(deidResult.Modality);
                    studyEnvelope.ModalitiesJson = JsonSerializer.Serialize(existingModalities);
                }
            }

            studyEnvelope.UpdatedAt = now;
        }

        certEntity.StudyEnvelopeId = studyEnvelope.Id;

        // Fetch or create Series Envelope
        var seriesEnvelope = await _dbContext.ImagingSeriesEnvelopes
            .FirstOrDefaultAsync(s => s.StudyEnvelopeId == studyEnvelope.Id && s.SeriesInstanceUid == deidResult.AnonymizedSeriesUid, cancellationToken)
            .ConfigureAwait(false);

        if (seriesEnvelope == null)
        {
            if (!isNewStudyEnvelope)
            {
                studyEnvelope.SeriesCount++;
            }

            seriesEnvelope = new ImagingSeriesEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                StudyEnvelopeId = studyEnvelope.Id,
                SeriesInstanceUid = deidResult.AnonymizedSeriesUid,
                SeriesNumber = studyEnvelope.SeriesCount,
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

    public async Task<BatchIngestionResult> IngestBatchAsync(
        BatchIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScopeIdentifiers(request.SubjectId, request.SiteId, request.StudyId);

        string subjectId = request.SubjectId;
        string siteId = request.SiteId;
        string studyId = request.StudyId;
        string opIdentity = string.IsNullOrWhiteSpace(request.OperatorIdentity) ? "cadence.imaging.pipeline" : request.OperatorIdentity;

        DateTimeOffset now = _clock.UtcNow;

        var (validList, skippedList, totalFilesProcessed) = await ProcessSlicesBoundedAsync(
            request,
            subjectId: subjectId,
            siteId: siteId,
            studyId: studyId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (validList.Count == 0)
        {
            var emptyManifest = new BatchDeidentificationManifest
            {
                ManifestId = Guid.NewGuid().ToString(),
                StudyEnvelopeId = string.Empty,
                StudyId = studyId,
                SiteId = siteId,
                SubjectId = subjectId,
                TotalFilesProcessed = totalFilesProcessed,
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
            emptyManifest.Signature = _deidentifier.SignPayload(emptyPayload);

            return new BatchIngestionResult(
                StudyEnvelopeId: string.Empty,
                CertificateId: emptyManifest.ManifestId,
                AnonymizedStudyUid: string.Empty,
                TotalFilesReceived: totalFilesProcessed,
                IngestedSliceCount: 0,
                SkippedNonDicomCount: skippedList.Count,
                SkippedNonDicomFiles: skippedList,
                SeriesSummary: [],
                Manifest: emptyManifest,
                Signature: emptyManifest.Signature);
        }

        var studyGroups = validList
            .GroupBy(x => x.DeidResult.AnonymizedStudyUid, StringComparer.Ordinal)
            .ToList();

        var existingEnvelopesByUid = new Dictionary<string, ImagingStudyEnvelope?>(studyGroups.Count, StringComparer.Ordinal);
        foreach (var studyGroup in studyGroups)
        {
            var existingStudy = await _dbContext.ImagingStudyEnvelopes
                .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyGroup.Key, cancellationToken)
                .ConfigureAwait(false);
            if (existingStudy != null)
            {
                EnsureStudyEnvelopeOwnership(existingStudy, studyId, siteId, subjectId);
            }

            existingEnvelopesByUid[studyGroup.Key] = existingStudy;
        }

        var candidateSopUids = validList
            .Select(x => x.DeidResult.AnonymizedSopUid)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var existingSopOwners = await (
                from inst in _dbContext.ImagingInstanceEnvelopes
                join series in _dbContext.ImagingSeriesEnvelopes on inst.SeriesEnvelopeId equals series.Id
                join study in _dbContext.ImagingStudyEnvelopes on series.StudyEnvelopeId equals study.Id
                where candidateSopUids.Contains(inst.SopInstanceUid)
                select new { study.StudyId, study.SiteId, study.SubjectId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingSopOwners.Any(owner => !IsMatchingScope(owner.StudyId, owner.SiteId, owner.SubjectId, studyId, siteId, subjectId)))
        {
            throw new InvalidOperationException("Existing imaging instance envelope ownership does not match the requested study, site, and subject.");
        }

        var seriesSummaries = new List<SeriesIngestionSummary>();
        ImagingStudyEnvelope? primaryStudyEnvelope = null;
        string mainAnonymizedStudyUid = studyGroups[0].Key;

        foreach (var studyGroup in studyGroups)
        {
            string studyUid = studyGroup.Key;
            var studySlices = studyGroup.ToList();
            var studyModalities = studySlices
                .Select(x => x.DeidResult.Modality)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var seriesGroups = studySlices
                .GroupBy(x => x.DeidResult.AnonymizedSeriesUid, StringComparer.Ordinal)
                .ToList();

            var studyEnvelope = existingEnvelopesByUid[studyUid];
            bool isNewStudyEnvelope = studyEnvelope == null;

            if (studyEnvelope == null)
            {
                studyEnvelope = new ImagingStudyEnvelope
                {
                    Id = Guid.NewGuid().ToString(),
                    StudyInstanceUid = studyUid,
                    StudyId = studyId,
                    SiteId = siteId,
                    SubjectId = subjectId,
                    ModalitiesJson = JsonSerializer.Serialize(studyModalities),
                    StudyDate = now.ToString("yyyyMMdd"),
                    StudyDescription = "Anonymized Clinical Trial DICOM Study",
                    SeriesCount = seriesGroups.Count,
                    InstanceCount = studySlices.Count,
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
                studyEnvelope.InstanceCount += studySlices.Count;
                var existingModalities = JsonSerializer.Deserialize<List<string>>(studyEnvelope.ModalitiesJson) ?? [];
                var mergedModalities = existingModalities.Union(studyModalities, StringComparer.Ordinal).ToList();
                studyEnvelope.ModalitiesJson = JsonSerializer.Serialize(mergedModalities);
                studyEnvelope.UpdatedAt = now;
            }

            primaryStudyEnvelope ??= studyEnvelope;

            int seriesIndex = isNewStudyEnvelope ? 1 : studyEnvelope.SeriesCount + 1;

            foreach (var seriesGroup in seriesGroups)
            {
                string seriesUid = seriesGroup.Key;
                var seriesSlices = seriesGroup.ToList();
                var firstInstance = seriesSlices[0].DeidResult;

                var seriesEnvelope = await _dbContext.ImagingSeriesEnvelopes
                    .FirstOrDefaultAsync(s => s.StudyEnvelopeId == studyEnvelope.Id && s.SeriesInstanceUid == seriesUid, cancellationToken)
                    .ConfigureAwait(false);

                int startingInstanceNumber;
                if (seriesEnvelope == null)
                {
                    if (!isNewStudyEnvelope)
                    {
                        studyEnvelope.SeriesCount++;
                    }

                    seriesEnvelope = new ImagingSeriesEnvelope
                    {
                        Id = Guid.NewGuid().ToString(),
                        StudyEnvelopeId = studyEnvelope.Id,
                        SeriesInstanceUid = seriesUid,
                        SeriesNumber = seriesIndex++,
                        Modality = firstInstance.Modality,
                        SeriesDescription = $"{firstInstance.Modality} Series",
                        InstanceCount = seriesSlices.Count
                    };
                    _dbContext.ImagingSeriesEnvelopes.Add(seriesEnvelope);
                    startingInstanceNumber = 1;
                }
                else
                {
                    startingInstanceNumber = seriesEnvelope.InstanceCount + 1;
                    seriesEnvelope.InstanceCount += seriesSlices.Count;
                }

                seriesSummaries.Add(new SeriesIngestionSummary(
                    SeriesEnvelopeId: seriesEnvelope.Id,
                    AnonymizedSeriesUid: seriesUid,
                    Modality: firstInstance.Modality,
                    InstanceCount: seriesEnvelope.InstanceCount));

                int instNum = startingInstanceNumber;
                foreach (var (_, deidResult) in seriesSlices)
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
                        ChecksumSha256 = deidResult.TargetSha256,
                        SizeBytes = deidResult.DeidentifiedSizeBytes,
                        BurnedInPhiMasked = deidResult.PixelMaskApplied
                    };
                    _dbContext.ImagingInstanceEnvelopes.Add(instEnvelope);
                }
            }
        }

        var primaryEnvelope = primaryStudyEnvelope!;

        var distinctScrubbedTags = validList
            .SelectMany(x => x.DeidResult.ScrubbedTags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var allPixelMasks = validList
            .SelectMany(x => x.DeidResult.PixelMasks)
            .Distinct()
            .ToList();

        bool anyPixelMask = validList.Any(x => x.DeidResult.PixelMaskApplied);

        var manifest = new BatchDeidentificationManifest
        {
            ManifestId = Guid.NewGuid().ToString(),
            StudyEnvelopeId = primaryEnvelope.Id,
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = subjectId,
            TotalFilesProcessed = totalFilesProcessed,
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
        string signature = _deidentifier.SignPayload(manifestPayload);
        manifest.Signature = signature;

        var certEntity = new DeidentificationCertificate
        {
            Id = manifest.ManifestId,
            StudyEnvelopeId = primaryEnvelope.Id,
            SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signature))),
            TargetSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ManifestId))),
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = subjectId,
            OriginalStudyInstanceUidHash = validList[0].DeidResult.OriginalStudyInstanceUidHash,
            OriginalSopInstanceUidHash = validList[0].DeidResult.OriginalSopInstanceUidHash,
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
            StudyEnvelopeId: primaryEnvelope.Id,
            CertificateId: certEntity.Id,
            AnonymizedStudyUid: mainAnonymizedStudyUid,
            TotalFilesReceived: totalFilesProcessed,
            IngestedSliceCount: validList.Count,
            SkippedNonDicomCount: skippedList.Count,
            SkippedNonDicomFiles: skippedList,
            SeriesSummary: seriesSummaries,
            Manifest: manifest,
            Signature: signature);
    }

    private async Task<(List<SliceIngestionMetadata> ValidSlices, List<string> SkippedFiles, int TotalFilesProcessed)> ProcessSlicesBoundedAsync(
        BatchIngestionRequest request,
        string subjectId,
        string siteId,
        string studyId,
        CancellationToken cancellationToken)
    {
        int maxConcurrency = Math.Clamp(Environment.ProcessorCount, 2, 8);
        var channel = Channel.CreateBounded<QueuedSliceWorkItem>(new BoundedChannelOptions(maxConcurrency * 2)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var successfulSlices = new ConcurrentBag<SliceIngestionMetadata>();
        var skippedNonDicomFiles = new ConcurrentBag<string>();
        int totalFilesProcessed = 0;

        ExceptionDispatchInfo? fatalDeidException = null;
        ExceptionDispatchInfo? producerException = null;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken workerCt = linkedCts.Token;

        var workers = new Task[maxConcurrency];
        for (int i = 0; i < maxConcurrency; i++)
        {
            workers[i] = Task.Run(async () =>
            {
                try
                {
                    await foreach (var workItem in channel.Reader.ReadAllAsync(workerCt).ConfigureAwait(false))
                    {
                        try
                        {
                            if (workItem.PreloadedStream.Length == 0)
                            {
                                skippedNonDicomFiles.Add(workItem.FileName);
                                continue;
                            }

                            var sliceResult = _deidentifier.DeidentifyPreloadedSlice(
                                workItem.PreloadedStream,
                                sourceSha256: workItem.SourceSha256,
                                subjectId: subjectId,
                                siteId: siteId,
                                studyId: studyId,
                                applyPixelMask: true);

                            ValidateFrameDimensions(sliceResult.Rows, sliceResult.Columns, sliceResult.BitsAllocated);

                            successfulSlices.Add(new SliceIngestionMetadata(workItem.FileName, sliceResult));
                        }
                        catch (Exception ex) when (ex is DicomPixelDeidException or DicomInvalidDimensionsException or ArgumentException)
                        {
                            Interlocked.CompareExchange(ref fatalDeidException, ExceptionDispatchInfo.Capture(ex), null);
                            await linkedCts.CancelAsync().ConfigureAwait(false);
                            return;
                        }
                        catch
                        {
                            skippedNonDicomFiles.Add(workItem.FileName);
                        }
                        finally
                        {
                            await workItem.PreloadedStream.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) when (workerCt.IsCancellationRequested)
                {
                    // Propagated below once all workers and queued buffers have been cleaned up.
                }
            }, CancellationToken.None);
        }

        try
        {
            await ProduceSliceWorkItemsAsync(
                request,
                channel.Writer,
                skippedNonDicomFiles,
                count => Interlocked.Add(ref totalFilesProcessed, count),
                new BatchQuotaTracker(_quotas),
                _scratchWorkspace,
                workerCt).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !workerCt.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            producerException = ExceptionDispatchInfo.Capture(ex);
            await linkedCts.CancelAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Worker aborted via linkedCts (e.g. DicomPixelDeidException).
        }
        finally
        {
            channel.Writer.TryComplete();
            await Task.WhenAll(workers).ConfigureAwait(false);

            while (channel.Reader.TryRead(out var leftover))
            {
                await leftover.PreloadedStream.DisposeAsync().ConfigureAwait(false);
            }
        }

        fatalDeidException?.Throw();
        producerException?.Throw();
        cancellationToken.ThrowIfCancellationRequested();

        var validList = successfulSlices.OrderBy(x => x.FileName, StringComparer.Ordinal).ToList();
        var skippedList = skippedNonDicomFiles.OrderBy(x => x, StringComparer.Ordinal).ToList();
        return (validList, skippedList, totalFilesProcessed);
    }

    private static async Task ProduceSliceWorkItemsAsync(
        BatchIngestionRequest request,
        ChannelWriter<QueuedSliceWorkItem> writer,
        ConcurrentBag<string> skippedNonDicomFiles,
        Action<int> incrementTotalFiles,
        BatchQuotaTracker quotaTracker,
        IScratchWorkspaceProvider scratchWorkspace,
        CancellationToken cancellationToken)
    {
        await foreach (var item in EnumerateUnifiedBatchStreamsAsync(request, skippedNonDicomFiles, incrementTotalFiles, quotaTracker, cancellationToken).ConfigureAwait(false))
        {
            byte[] headerBytes = new byte[4];
            int headerRead = await item.ContentStream.ReadAtLeastAsync(headerBytes, headerBytes.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

            if (headerRead == 0)
            {
                quotaTracker.RecordEntry();
                incrementTotalFiles(1);
                skippedNonDicomFiles.Add(item.FileName);
                continue;
            }

            bool isZipCandidate = headerRead >= 4 && IsZipSignatureOrExtension(item.FileName, item.IsZipHint, headerBytes);
            if (!isZipCandidate)
            {
                quotaTracker.RecordEntry();
                await using var combined = new PrefixReadStream(headerBytes, headerRead, item.ContentStream);
                var (pooledSlice, sha256, _) = await quotaTracker.CopyAndHashSliceBoundedAsync(combined, item.FileName, cancellationToken).ConfigureAwait(false);
                try
                {
                    incrementTotalFiles(1);
                    await writer.WriteAsync(new QueuedSliceWorkItem(item.FileName, pooledSlice, sha256), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await pooledSlice.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                continue;
            }

            await ProcessZipCandidateAsync(
                item,
                headerBytes,
                headerRead,
                writer,
                skippedNonDicomFiles,
                incrementTotalFiles,
                quotaTracker,
                scratchWorkspace,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ProcessZipCandidateAsync(
        BatchStreamItem item,
        byte[] headerBytes,
        int headerRead,
        ChannelWriter<QueuedSliceWorkItem> writer,
        ConcurrentBag<string> skippedNonDicomFiles,
        Action<int> incrementTotalFiles,
        BatchQuotaTracker quotaTracker,
        IScratchWorkspaceProvider scratchWorkspace,
        CancellationToken cancellationToken)
    {
        string? tempZipPath = null;
        FileStream? tempZipStream = null;
        try
        {
            Stream seekableZipStream;
            long archiveStartPosition = 0;

            if (item.ContentStream.CanSeek)
            {
                archiveStartPosition = item.ContentStream.Position - headerRead;
                item.ContentStream.Position = archiveStartPosition;
                quotaTracker.ValidateCompressedArchiveLength(item.ContentStream.Length - archiveStartPosition);
                seekableZipStream = item.ContentStream;
            }
            else
            {
                (tempZipPath, tempZipStream) = scratchWorkspace.CreateFile(
                    "cadence-dicom-batch",
                    ".zip",
                    FileAccess.ReadWrite,
                    bufferSize: BatchStreamHelpers.PooledBufferSize,
                    FileOptions.Asynchronous);

                await tempZipStream.WriteAsync(headerBytes.AsMemory(0, headerRead), cancellationToken).ConfigureAwait(false);
                await quotaTracker.CopySpooledArchiveBoundedAsync(item.ContentStream, tempZipStream, headerRead, cancellationToken).ConfigureAwait(false);
                await tempZipStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                tempZipStream.Position = 0;
                seekableZipStream = tempZipStream;
            }

            ZipArchive? zip = null;
            try
            {
                zip = await ZipArchive.CreateAsync(seekableZipStream, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not BatchPayloadTooLargeException)
            {
                quotaTracker.RecordEntry();
                seekableZipStream.Position = archiveStartPosition;
                var (fallbackSlice, fallbackSha256, _) = await quotaTracker.CopyAndHashSliceBoundedAsync(seekableZipStream, item.FileName, cancellationToken).ConfigureAwait(false);
                try
                {
                    incrementTotalFiles(1);
                    await writer.WriteAsync(new QueuedSliceWorkItem(item.FileName, fallbackSlice, fallbackSha256), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await fallbackSlice.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            if (zip is not null)
            {
                await using (zip.ConfigureAwait(false))
                {
                    long compressedArchiveBytes = Math.Max(1L, seekableZipStream.Length - archiveStartPosition);
                    await EnumerateZipEntriesBoundedAsync(
                        zip,
                        compressedArchiveBytes,
                        writer,
                        skippedNonDicomFiles,
                        incrementTotalFiles,
                        quotaTracker,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (tempZipStream is not null)
            {
                await tempZipStream.DisposeAsync().ConfigureAwait(false);
            }

            BatchStreamHelpers.DeleteTempFileSafely(tempZipPath);
        }
    }

    private static async Task EnumerateZipEntriesBoundedAsync(
        ZipArchive zip,
        long compressedArchiveBytes,
        ChannelWriter<QueuedSliceWorkItem> writer,
        ConcurrentBag<string> skippedNonDicomFiles,
        Action<int> incrementTotalFiles,
        BatchQuotaTracker quotaTracker,
        CancellationToken cancellationToken)
    {
        quotaTracker.ValidateDeclaredArchiveEntryCount(zip.Entries.Count);
        long archiveUncompressedBytes = 0;

        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            quotaTracker.RecordEntry();
            quotaTracker.ValidateDeclaredEntryLength(entry.FullName, entry.Length);

            incrementTotalFiles(1);
            PooledBlockStream? entryPooled = null;
            string entrySha256;
            long effectiveEntryCompressedBytes = entry.CompressedLength > 0
                ? Math.Min(entry.CompressedLength, compressedArchiveBytes)
                : compressedArchiveBytes;

            try
            {
                await using var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                (entryPooled, entrySha256, long entryBytesRead) = await quotaTracker.CopyAndHashZipEntryBoundedAsync(
                    entryStream,
                    entryName: entry.FullName,
                    effectiveEntryCompressedBytes: effectiveEntryCompressedBytes,
                    compressedArchiveBytes: compressedArchiveBytes,
                    archiveUncompressedBeforeEntry: archiveUncompressedBytes,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                archiveUncompressedBytes += entryBytesRead;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not BatchPayloadTooLargeException)
            {
                if (entryPooled is not null)
                {
                    await entryPooled.DisposeAsync().ConfigureAwait(false);
                }

                skippedNonDicomFiles.Add(entry.FullName);
                continue;
            }

            try
            {
                await writer.WriteAsync(new QueuedSliceWorkItem(entry.FullName, entryPooled, entrySha256), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await entryPooled.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private static async IAsyncEnumerable<BatchStreamItem> EnumerateUnifiedBatchStreamsAsync(
        BatchIngestionRequest request,
        ConcurrentBag<string> skippedNonDicomFiles,
        Action<int> incrementTotalFiles,
        BatchQuotaTracker quotaTracker,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request.Files is { Count: > 0 })
        {
            quotaTracker.ValidateDeclaredArchiveEntryCount(request.Files.Count);
            foreach (var file in request.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file is null)
                {
                    continue;
                }

                if (file.Content is null || file.Content.Length == 0)
                {
                    quotaTracker.RecordEntry();
                    incrementTotalFiles(1);
                    skippedNonDicomFiles.Add(file.FileName);
                    continue;
                }

                using var ms = new MemoryStream(file.Content, writable: false);
                yield return new BatchStreamItem(file.FileName, ms);
            }
        }

        if (request.StreamFiles is not null)
        {
            await foreach (var streamItem in request.StreamFiles.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (streamItem is null)
                {
                    continue;
                }

                yield return streamItem;
            }
        }
    }

    private static bool IsZipSignatureOrExtension(string? fileName, bool? isZipHint, ReadOnlySpan<byte> headerBytes)
    {
        if (isZipHint is true)
        {
            return true;
        }

        if (fileName is not null && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return headerBytes.Length >= 4
            && headerBytes[0] == 0x50
            && headerBytes[1] == 0x4B
            && ((headerBytes[2] == 0x03 && headerBytes[3] == 0x04)
                || (headerBytes[2] == 0x05 && headerBytes[3] == 0x06));
    }

    private static void ValidateScopeIdentifiers(string subjectId, string siteId, string studyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);
    }

    private static bool IsMatchingScope(
        string storedStudy,
        string storedSite,
        string storedSubject,
        string expectedStudy,
        string expectedSite,
        string expectedSubject) =>
        string.Equals(storedStudy, expectedStudy, StringComparison.Ordinal)
        && string.Equals(storedSite, expectedSite, StringComparison.Ordinal)
        && string.Equals(storedSubject, expectedSubject, StringComparison.Ordinal);

    private static void EnsureStudyEnvelopeOwnership(
        ImagingStudyEnvelope studyEnvelope,
        string studyId,
        string siteId,
        string subjectId)
    {
        if (!IsMatchingScope(studyEnvelope.StudyId, studyEnvelope.SiteId, studyEnvelope.SubjectId, studyId, siteId, subjectId))
        {
            throw new InvalidOperationException("Existing imaging study envelope ownership does not match the requested study, site, and subject.");
        }
    }

    private sealed record QueuedSliceWorkItem(
        string FileName,
        PooledBlockStream PreloadedStream,
        string SourceSha256);

    private sealed record SliceIngestionMetadata(
        string FileName,
        DicomSliceDeidResult DeidResult);

    /// <summary>The canonical payload a batch manifest's signature covers: every field except the signature.</summary>
    public static object CreateBatchManifestPayloadWithoutSignature(BatchDeidentificationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
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
