using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Deid;
using Cadence.Kernel.Time;
using Cadence.Testing.Time;

using FellowOakDicom;
using FellowOakDicom.IO.Buffer;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Xunit;

namespace Cadence.UnitTests.Imaging;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideMemoryCollection
{
    public const string Name = "ProcessWideMemory";

    private ProcessWideMemoryCollection()
    {
    }
}

[Collection(ProcessWideMemoryCollection.Name)]
public sealed class DicomIngestionServiceTests
{
    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(DicomDeidentifier.MinimumSigningKeyLength);
    private static readonly byte[] PseudonymizationKey = RandomNumberGenerator.GetBytes(DicomDeidentifier.MinimumSigningKeyLength);

    /// <summary>A de-identifier with this run's random keys; imaging has no default key (#5399).</summary>
    internal static DicomDeidentifier TestDeidentifier() => new(SigningKey, PseudonymizationKey);

    /// <summary>A batch manifest verifies under the service's signing key, and not under another key or once altered (#5399).</summary>
    [Fact]
    public async Task IngestBatchAsync_SignsTheManifestWithTheConfiguredKey()
    {
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingManifestKeyTest_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, TestDeidentifier(), SystemClock.Instance);

        var result = await service.IngestBatchAsync(
            new BatchIngestionRequest([new("slice_001.dcm", CreateSampleDicomBytesWithPhi("70001"))], "SUBJ-7001", "SITE-701", "STUDY-7001"),
            TestContext.Current.CancellationToken);

        var payload = DicomIngestionService.CreateBatchManifestPayloadWithoutSignature(result.Manifest);
        TestDeidentifier().VerifyPayload(payload, result.Signature).Should().BeTrue();
        new DicomDeidentifier(RandomNumberGenerator.GetBytes(32), PseudonymizationKey).VerifyPayload(payload, result.Signature).Should().BeFalse();

        result.Manifest.IngestedSliceCount++;
        TestDeidentifier().VerifyPayload(DicomIngestionService.CreateBatchManifestPayloadWithoutSignature(result.Manifest), result.Signature)
            .Should().BeFalse();
    }

    [Fact(DisplayName = "IngestInstanceAsync_DeidentifiesDicomAndPersistsCertificatesAndEnvelopes")]
    public async Task IngestInstanceAsync_DeidentifiesDicomAndPersistsCertificatesAndEnvelopes()
    {
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingIngestionTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);

        var service = new DicomIngestionService(dbContext, TestDeidentifier(), SystemClock.Instance);

        byte[] rawDicom = CreateSampleDicomBytesWithPhi("99991");

        var result = await service.IngestInstanceAsync(
            rawDicom,
            subjectId: "SUBJ-2001",
            siteId: "SITE-101",
            studyId: "STUDY-3001",
            operatorIdentity: "tester@cadence.org",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.AnonymizedStudyUid.Should().StartWith("1.2.826.0.1.3680043.10.99.");
        result.AnonymizedSeriesUid.Should().StartWith("1.2.826.0.1.3680043.10.99.");
        result.AnonymizedSopUid.Should().StartWith("1.2.826.0.1.3680043.10.99.");
        result.PixelMaskApplied.Should().BeTrue();
        result.Signature.Should().NotBeNullOrEmpty();

        var certInDb = await dbContext.DeidentificationCertificates.FirstOrDefaultAsync(c => c.Id == result.CertificateId, TestContext.Current.CancellationToken);
        certInDb.Should().NotBeNull();
        certInDb!.SubjectId.Should().Be("SUBJ-2001");
        certInDb.SiteId.Should().Be("SITE-101");
        certInDb.StudyId.Should().Be("STUDY-3001");

        var studyInDb = await dbContext.ImagingStudyEnvelopes.FirstOrDefaultAsync(s => s.Id == result.StudyEnvelopeId, TestContext.Current.CancellationToken);
        studyInDb.Should().NotBeNull();
        studyInDb!.InstanceCount.Should().Be(1);
    }

    [Fact(DisplayName = "IngestBatchAsync_DeidentifiesMultiSliceFolderAndFiltersNonDicomFiles")]
    public async Task IngestBatchAsync_DeidentifiesMultiSliceFolderAndFiltersNonDicomFiles()
    {
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingBatchIngestionTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, TestDeidentifier(), SystemClock.Instance);

        var files = new List<BatchFileItem>
        {
            new("slice_001.dcm", CreateSampleDicomBytesWithPhi("80001")),
            new("slice_002.dcm", CreateSampleDicomBytesWithPhi("80002")),
            new("slice_003.dcm", CreateSampleDicomBytesWithPhi("80003")),
            new("lab_report.pdf", Encoding.UTF8.GetBytes("%PDF-1.4 sample PDF content")),
            new("clinical_notes.txt", Encoding.UTF8.GetBytes("Patient screening notes"))
        };

        var request = new BatchIngestionRequest(
            Files: files,
            SubjectId: "SUBJ-ONC-001",
            SiteId: "SITE-101",
            StudyId: "STUDY-ONC-2026",
            TimepointName: "BASELINE",
            OperatorIdentity: "crc.coordinator@site.org");

        var result = await service.IngestBatchAsync(request, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.TotalFilesReceived.Should().Be(5);
        result.IngestedSliceCount.Should().Be(3);
        result.SkippedNonDicomCount.Should().Be(2);
        result.SkippedNonDicomFiles.Should().Contain("lab_report.pdf");
        result.SkippedNonDicomFiles.Should().Contain("clinical_notes.txt");
        result.Signature.Should().NotBeNullOrEmpty();

        var studyInDb = await dbContext.ImagingStudyEnvelopes.FirstOrDefaultAsync(s => s.Id == result.StudyEnvelopeId, TestContext.Current.CancellationToken);
        studyInDb.Should().NotBeNull();
        studyInDb!.InstanceCount.Should().Be(3);

        var certInDb = await dbContext.DeidentificationCertificates.FirstOrDefaultAsync(c => c.Id == result.CertificateId, TestContext.Current.CancellationToken);
        certInDb.Should().NotBeNull();
        certInDb!.ScrubbedTagsCount.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "IngestBatchAsync_ProcessesZipArchiveWithMixedContents")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarAnalyzer.CSharp", "S6966:Use async method", Justification = "Synchronous ZipArchive stream writing in test setup")]
    public async Task IngestBatchAsync_ProcessesZipArchiveWithMixedContents()
    {
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingZipBatchTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, TestDeidentifier(), SystemClock.Instance);

        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var dicomEntry1 = archive.CreateEntry("DICOM/slice_101.dcm");
            using (var s = dicomEntry1.Open())
            {
                byte[] bytes1 = CreateSampleDicomBytesWithPhi("70001");
                s.Write(bytes1, 0, bytes1.Length);
            }

            var dicomEntry2 = archive.CreateEntry("DICOM/slice_102.dcm");
            using (var s = dicomEntry2.Open())
            {
                byte[] bytes2 = CreateSampleDicomBytesWithPhi("70002");
                s.Write(bytes2, 0, bytes2.Length);
            }

            var pdfEntry = archive.CreateEntry("DICOM/study_protocol.pdf");
            using (var s = pdfEntry.Open())
            {
                byte[] pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.7 Protocol document");
                s.Write(pdfBytes, 0, pdfBytes.Length);
            }
        }

        var zipFileItem = new BatchFileItem("study_archive.zip", zipMs.ToArray());
        var request = new BatchIngestionRequest(
            Files: new[] { zipFileItem },
            SubjectId: "SUBJ-ZIP-001",
            SiteId: "SITE-200",
            StudyId: "STUDY-ZIP-100",
            TimepointName: "CYCLE_1_DAY_1");

        var result = await service.IngestBatchAsync(request, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.TotalFilesReceived.Should().Be(3);
        result.IngestedSliceCount.Should().Be(2);
        result.SkippedNonDicomCount.Should().Be(1);
        result.SkippedNonDicomFiles.Should().Contain("DICOM/study_protocol.pdf");

        var seriesInDb = await dbContext.ImagingSeriesEnvelopes.FirstOrDefaultAsync(s => s.StudyEnvelopeId == result.StudyEnvelopeId, TestContext.Current.CancellationToken);
        seriesInDb.Should().NotBeNull();
        seriesInDb!.InstanceCount.Should().Be(2);
    }

    [Fact(DisplayName = "IngestInstanceAsync_AbortsDatabasePersistence_WhenDeidentificationFails")]
    [Cadence.Testing.GxPRequirement("PRD-IMG-002")]
    public async Task IngestInstanceAsync_AbortsDatabasePersistence_WhenDeidentificationFails()
    {
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingAbortTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, TestDeidentifier(), SystemClock.Instance);

        byte[] compressedDicom = CreateUnsupportedCompressedDicomBytes();

        var act = async () => await service.IngestInstanceAsync(
            compressedDicom,
            subjectId: "SUBJ-FAIL-01",
            siteId: "SITE-101",
            studyId: "STUDY-FAIL-01",
            operatorIdentity: "tester@cadence.org",
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Cadence.Deid.DicomPixelDeidException>();

        (await dbContext.DeidentificationCertificates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await dbContext.ImagingStudyEnvelopes.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact(DisplayName = "IngestInstanceAsync_ThrowsArgumentException_WhenPayloadIsEmpty")]
    public async Task IngestInstanceAsync_ThrowsArgumentException_WhenPayloadIsEmpty()
    {
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingEmptyPayloadTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, TestDeidentifier(), SystemClock.Instance);

        var act = async () => await service.IngestInstanceAsync(
            Array.Empty<byte>(),
            subjectId: "SUBJ-EMPTY",
            siteId: "SITE-101",
            studyId: "STUDY-EMPTY",
            operatorIdentity: "tester@cadence.org",
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact(DisplayName = "IngestBatchAsync_AbortsDatabasePersistence_WhenDeidentificationFails")]
    [Cadence.Testing.GxPRequirement("PRD-IMG-002")]
    public async Task IngestBatchAsync_AbortsDatabasePersistence_WhenDeidentificationFails()
    {
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingBatchAbortTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, TestDeidentifier(), SystemClock.Instance);

        var files = new List<BatchFileItem>
        {
            new("slice_001.dcm", CreateSampleDicomBytesWithPhi("80001")),
            new("slice_corrupted.dcm", CreateUnsupportedCompressedDicomBytes())
        };

        var request = new BatchIngestionRequest(
            Files: files,
            SubjectId: "SUBJ-BATCH-FAIL",
            SiteId: "SITE-101",
            StudyId: "STUDY-BATCH-FAIL");

        var act = async () => await service.IngestBatchAsync(request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Cadence.Deid.DicomPixelDeidException>();

        (await dbContext.DeidentificationCertificates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await dbContext.ImagingStudyEnvelopes.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    internal static byte[] CreateUnsupportedCompressedDicomBytes()
    {
        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
            { DicomTag.SOPInstanceUID, "1.2.840.10008.5.1.4.1.1.7.999" },
            { DicomTag.StudyInstanceUID, "1.2.840.10008.5.1.4.1.1.7.998" },
            { DicomTag.SeriesInstanceUID, "1.2.840.10008.5.1.4.1.1.7.998.1" },
            { DicomTag.PatientID, "COMPRESSED-PATIENT" },
            { DicomTag.Modality, "SC" },
            { DicomTag.Rows, (ushort)128 },
            { DicomTag.Columns, (ushort)128 },
            { DicomTag.BitsAllocated, (ushort)16 }
        };

        var fragment = new DicomOtherByteFragment(DicomTag.PixelData);
        fragment.Fragments.Add(new MemoryByteBuffer(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }));
        dataset.AddOrUpdate(fragment);

        var dicomFile = new DicomFile(dataset);
        dicomFile.FileMetaInfo.TransferSyntax = DicomTransferSyntax.JPEG2000Lossless;
        using var ms = new MemoryStream();
        dicomFile.Save(ms);
        return ms.ToArray();
    }

    internal static byte[] CreateSampleDicomBytesWithPhi(string sopSuffix = "99991")
    {
        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.CTImageStorage },
            { DicomTag.SOPInstanceUID, $"1.2.840.10008.5.1.4.1.1.2.{sopSuffix}" },
            { DicomTag.StudyInstanceUID, "1.2.840.10008.5.1.4.1.1.2.99990" },
            { DicomTag.SeriesInstanceUID, "1.2.840.10008.5.1.4.1.1.2.99990.1" },
            { DicomTag.PatientID, "PHI-PATIENT-12345" },
            { DicomTag.PatientName, "DOE^JOHN^A" },
            { DicomTag.PatientBirthDate, "19750515" },
            { DicomTag.Modality, "SC" }, // Secondary Capture modality triggers automatic header pixel masking
            { DicomTag.Rows, (ushort)256 },
            { DicomTag.Columns, (ushort)256 },
            { DicomTag.BitsAllocated, (ushort)16 }
        };

        byte[] pixels = new byte[256 * 256 * 2];
        dataset.Add(new DicomOtherByte(DicomTag.PixelData, pixels));

        var file = new DicomFile(dataset);
        using var ms = new MemoryStream();
        file.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task IngestBatchAsync_StreamingMatchesDeidentify_OnChecksumsSizesUidsMasksAndManifestSignature()
    {
        var ct = TestContext.Current.CancellationToken;
        var deidentifier = TestDeidentifier();
        var clock = new FakeClock();
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingBatchParityTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, deidentifier, clock);

        byte[] rawSlice1 = CreateSampleDicomBytesWithPhi("8101");
        byte[] rawSlice2 = CreateSampleDicomBytesWithPhi("8102");
        byte[] rawSlice3 = CreateSampleDicomBytesWithPhi("8103");

        byte[] zipBytes = CreateArchiveBytes(
        [
            ("DICOM/slice_001.dcm", rawSlice1),
            ("DICOM/readme.pdf", Encoding.UTF8.GetBytes("%PDF-1.7 imaging notes")),
            ("DICOM/sub/slice_002.dcm", rawSlice2)
        ]);

        var request = new BatchIngestionRequest(
            Files: [],
            SubjectId: "SUBJ-PARITY-01",
            SiteId: "SITE-101",
            StudyId: "STUDY-PARITY-01",
            TimepointName: "WEEK_4",
            OperatorIdentity: "crc@cadence.org")
        {
            StreamFiles = EnumerateMixedStreamsAsync(zipBytes, rawSlice3)
        };

        var result = await service.IngestBatchAsync(request, ct);

        result.TotalFilesReceived.Should().Be(5);
        result.IngestedSliceCount.Should().Be(3);
        result.SkippedNonDicomCount.Should().Be(2);
        result.SkippedNonDicomFiles.Should().Equal("DICOM/readme.pdf", "notes.txt");

        var payload = DicomIngestionService.CreateBatchManifestPayloadWithoutSignature(result.Manifest);
        deidentifier.VerifyPayload(payload, result.Signature).Should().BeTrue();

        var expectedSlices = new[] { rawSlice1, rawSlice2, rawSlice3 }
            .Select(bytes => deidentifier.Deidentify(
                bytes,
                subjectId: "SUBJ-PARITY-01",
                siteId: "SITE-101",
                studyId: "STUDY-PARITY-01",
                deidentifiedAt: clock.UtcNow,
                applyPixelMask: true,
                operatorIdentity: "crc@cadence.org"))
            .ToArray();

        result.AnonymizedStudyUid.Should().Be(expectedSlices[0].AnonymizedStudyUid);

        var instances = await dbContext.ImagingInstanceEnvelopes
            .OrderBy(i => i.InstanceNumber)
            .ToListAsync(ct);
        instances.Should().HaveCount(3);

        for (int i = 0; i < expectedSlices.Length; i++)
        {
            instances[i].SopInstanceUid.Should().Be(expectedSlices[i].AnonymizedSopUid);
            instances[i].ChecksumSha256.Should().Be(expectedSlices[i].Certificate.TargetSha256);
            instances[i].SizeBytes.Should().Be(expectedSlices[i].DeidentifiedBytes.Length);
            instances[i].BurnedInPhiMasked.Should().Be(expectedSlices[i].Certificate.PixelMaskApplied);
            instances[i].Rows.Should().Be(expectedSlices[i].Rows);
            instances[i].Columns.Should().Be(expectedSlices[i].Columns);
            instances[i].BitsAllocated.Should().Be(expectedSlices[i].BitsAllocated);
        }

        var cert = await dbContext.DeidentificationCertificates.SingleAsync(c => c.Id == result.CertificateId, ct);
        cert.SourceSha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result.Signature))));
        cert.TargetSha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result.Manifest.ManifestId))));
        cert.OriginalStudyInstanceUidHash.Should().Be(expectedSlices[0].Certificate.OriginalStudyInstanceUidHash);
        cert.OriginalSopInstanceUidHash.Should().Be(expectedSlices[0].Certificate.OriginalSopInstanceUidHash);
        cert.AnonymizedStudyInstanceUid.Should().Be(expectedSlices[0].AnonymizedStudyUid);
        cert.AnonymizedSopInstanceUid.Should().Be(expectedSlices[0].AnonymizedSopUid);
        cert.PixelMaskApplied.Should().BeTrue();
        cert.ScrubbedTagsCount.Should().Be(result.Manifest.ScrubbedTagsCount);
        result.Manifest.PixelMasks.Should().Equal(expectedSlices[0].Certificate.PixelMasks);
    }

    [Fact]
    [Cadence.Testing.GxPRequirement("PRD-IMG-002")]
    public async Task IngestBatchAsync_NonSeekableZipArchive_AbortsPersistenceAndDeletesSpoolFile_WhenPixelMaskingFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingNonSeekableZipAbort_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, TestDeidentifier(), SystemClock.Instance);

        byte[] zipBytes = CreateArchiveBytes(
        [
            ("DICOM/slice_001.dcm", CreateSampleDicomBytesWithPhi("8201")),
            ("DICOM/slice_002.dcm", CreateSampleDicomBytesWithPhi("8202")),
            ("DICOM/slice_bad.dcm", CreateUnsupportedCompressedDicomBytes())
        ]);

        var spoolFilesBefore = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-batch-*.zip").ToHashSet(StringComparer.Ordinal);

        var request = new BatchIngestionRequest(
            Files: [],
            SubjectId: "SUBJ-ZIP-FAIL",
            SiteId: "SITE-101",
            StudyId: "STUDY-ZIP-FAIL")
        {
            StreamFiles = SingleNonSeekableZipStreamAsync("study.zip", zipBytes)
        };

        var act = async () => await service.IngestBatchAsync(request, ct);
        await act.Should().ThrowAsync<DicomPixelDeidException>();

        (await dbContext.DeidentificationCertificates.CountAsync(ct)).Should().Be(0);
        (await dbContext.ImagingStudyEnvelopes.CountAsync(ct)).Should().Be(0);
        (await dbContext.ImagingSeriesEnvelopes.CountAsync(ct)).Should().Be(0);
        (await dbContext.ImagingInstanceEnvelopes.CountAsync(ct)).Should().Be(0);

        var leakedSpoolFiles = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-batch-*.zip")
            .Where(path => !spoolFilesBefore.Contains(path))
            .ToArray();
        leakedSpoolFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestBatchAsync_MultiSlice512ZipArchive_KeepsLiveLohBoundedAtSaveChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var deidentifier = TestDeidentifier();

        // Warm shared ArrayPool buckets before comparing 40-slice vs 100-slice 512x512 runs
        _ = await RunZipLohProbeAsync(sliceCount: 4, deidentifier, ct);

        var (lohAt40Slices, _) = await RunZipLohProbeAsync(sliceCount: 40, deidentifier, ct);
        var (lohAt100Slices, totalAllocatedAt100Slices) = await RunZipLohProbeAsync(sliceCount: 100, deidentifier, ct);

        // 60 extra 512x512x16-bit slices represent >30 MiB raw + >30 MiB de-identified (>60 MiB on LOH if retained).
        // Streaming slice de-identification retains zero slice byte[] buffers at SaveChangesAsync.
        long lohGrowthBytes = lohAt100Slices - lohAt40Slices;
        lohGrowthBytes.Should().BeLessThan(8 * 1024 * 1024);

        // 100 uncompressed 512x512x16-bit slices alone are >50 MiB raw (>100 MiB raw + de-identified);
        // pooled streaming keeps total managed allocations far below a single retained copy of the batch.
        totalAllocatedAt100Slices.Should().BeLessThan(35L * 1024 * 1024);
    }

    internal static byte[] CreateArchiveBytes((string EntryName, byte[] Content)[] entries)
    {
        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(content, 0, content.Length);
            }
        }

        return zipMs.ToArray();
    }

    private static async Task<(long LiveLohBytes, long TotalAllocatedBytes)> RunZipLohProbeAsync(int sliceCount, DicomDeidentifier deidentifier, CancellationToken ct)
    {
        string tempInputZip = Path.Combine(Path.GetTempPath(), $"test-input-{sliceCount}-{Guid.NewGuid():N}.zip");
        var spoolFilesBefore = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-batch-*.zip").ToHashSet(StringComparer.Ordinal);

        try
        {
            await using (var zipFs = new FileStream(tempInputZip, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var archive = await ZipArchive.CreateAsync(zipFs, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: null, ct))
            {
                for (int i = 1; i <= sliceCount; i++)
                {
                    var entry = archive.CreateEntry($"DICOM/slice_{i:D4}.dcm", CompressionLevel.Fastest);
                    await using var entryStream = await entry.OpenAsync(ct);
                    byte[] sliceBytes = Create512CtSliceBytes(i);
                    await entryStream.WriteAsync(sliceBytes, ct);
                }
            }

            var interceptor = new LohSnapshotSaveChangesInterceptor();
            var options = new DbContextOptionsBuilder<ImagingDbContext>()
                .UseInMemoryDatabase($"ImagingLohTest_{sliceCount}_{Guid.NewGuid():N}")
                .AddInterceptors(interceptor)
                .Options;

            await using var dbContext = new ImagingDbContext(options);
            var service = new DicomIngestionService(dbContext, deidentifier, SystemClock.Instance);

            await using var rawZipRead = new FileStream(tempInputZip, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var nonSeekable = new NonSeekableTestReadStream(rawZipRead);

            var request = new BatchIngestionRequest(
                Files: [],
                SubjectId: "SUBJ-LOH-01",
                SiteId: "SITE-101",
                StudyId: "STUDY-LOH-01")
            {
                StreamFiles = SingleStreamAsync(new BatchStreamItem("study.zip", nonSeekable, IsZipHint: true))
            };

            _ = GC.GetTotalMemory(forceFullCollection: true);
            long allocBefore = GC.GetTotalAllocatedBytes(precise: true);

            var result = await service.IngestBatchAsync(request, ct);
            long allocAfter = GC.GetTotalAllocatedBytes(precise: true);

            result.IngestedSliceCount.Should().Be(sliceCount);
            (await dbContext.ImagingInstanceEnvelopes.CountAsync(ct)).Should().Be(sliceCount);

            var leakedSpoolFiles = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-batch-*.zip")
                .Where(path => !spoolFilesBefore.Contains(path))
                .ToArray();
            leakedSpoolFiles.Should().BeEmpty();

            return (interceptor.LiveLohBytesAtSaveChanges, allocAfter - allocBefore);
        }
        finally
        {
            if (File.Exists(tempInputZip))
            {
                File.Delete(tempInputZip);
            }
        }
    }

    private static byte[] Create512CtSliceBytes(int index)
    {
        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.CTImageStorage },
            { DicomTag.SOPInstanceUID, $"1.2.840.10008.5.1.4.1.1.2.950.{index}" },
            { DicomTag.StudyInstanceUID, "1.2.840.10008.5.1.4.1.1.2.950" },
            { DicomTag.SeriesInstanceUID, "1.2.840.10008.5.1.4.1.1.2.950.1" },
            { DicomTag.PatientID, $"RAW-LOH-{index}" },
            { DicomTag.PatientName, "DOE^BENCH" },
            { DicomTag.StudyDate, "20260301" },
            { DicomTag.Modality, "SC" },
            { DicomTag.Rows, (ushort)512 },
            { DicomTag.Columns, (ushort)512 },
            { DicomTag.BitsAllocated, (ushort)16 }
        };

        byte[] pixels = new byte[512 * 512 * 2];
        pixels[0] = (byte)(index & 0xFF);
        dataset.Add(new DicomOtherByte(DicomTag.PixelData, pixels));

        using var ms = new MemoryStream(pixels.Length + 4_096);
        new DicomFile(dataset).Save(ms);
        return ms.ToArray();
    }

    private static async IAsyncEnumerable<BatchStreamItem> EnumerateMixedStreamsAsync(byte[] zipBytes, byte[] standaloneSlice)
    {
        await using var zipMs = new MemoryStream(zipBytes, writable: false);
        await using var nonSeekableZip = new NonSeekableTestReadStream(zipMs);
        yield return new BatchStreamItem("archive.zip", nonSeekableZip);

        await using var sliceMs = new MemoryStream(standaloneSlice, writable: false);
        await using var nonSeekableSlice = new NonSeekableTestReadStream(sliceMs);
        yield return new BatchStreamItem("slice_003.dcm", nonSeekableSlice);

        await using var noteMs = new MemoryStream(Encoding.UTF8.GetBytes("operator note"), writable: false);
        yield return new BatchStreamItem("notes.txt", noteMs);
    }

    private static async IAsyncEnumerable<BatchStreamItem> SingleNonSeekableZipStreamAsync(string fileName, byte[] zipBytes)
    {
        await using var ms = new MemoryStream(zipBytes, writable: false);
        await using var nonSeekable = new NonSeekableTestReadStream(ms);
        yield return new BatchStreamItem(fileName, nonSeekable);
    }

    private static async IAsyncEnumerable<BatchStreamItem> SingleStreamAsync(BatchStreamItem item)
    {
        await Task.CompletedTask;
        yield return item;
    }

    private sealed class LohSnapshotSaveChangesInterceptor : SaveChangesInterceptor
    {
        public long LiveLohBytesAtSaveChanges { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _ = GC.GetTotalMemory(forceFullCollection: true);
            var gcInfo = GC.GetGCMemoryInfo();
            LiveLohBytesAtSaveChanges = gcInfo.GenerationInfo.Length > 3 ? gcInfo.GenerationInfo[3].SizeAfterBytes : 0L;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    internal sealed class NonSeekableTestReadStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
