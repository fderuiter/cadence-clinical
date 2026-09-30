using System;
using System.IO;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Kernel.Time;

using FellowOakDicom;
using FellowOakDicom.IO.Buffer;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Imaging;

public sealed class DicomIngestionServiceTests
{
    [Fact(DisplayName = "IngestInstanceAsync_DeidentifiesDicomAndPersistsCertificatesAndEnvelopes")]
    public async Task IngestInstanceAsync_DeidentifiesDicomAndPersistsCertificatesAndEnvelopes()
    {
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingIngestionTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);

        var service = new DicomIngestionService(dbContext, SystemClock.Instance);

        byte[] rawDicom = CreateSampleDicomBytesWithPhi();

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

    private static byte[] CreateSampleDicomBytesWithPhi()
    {
        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.CTImageStorage },
            { DicomTag.SOPInstanceUID, "1.2.840.10008.5.1.4.1.1.2.99991" },
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
