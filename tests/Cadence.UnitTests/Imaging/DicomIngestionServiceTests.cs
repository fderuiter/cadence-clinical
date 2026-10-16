using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Deid;
using Cadence.Kernel.Time;

using FellowOakDicom;
using FellowOakDicom.IO.Buffer;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Imaging;

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

    private static byte[] CreateUnsupportedCompressedDicomBytes()
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
}
