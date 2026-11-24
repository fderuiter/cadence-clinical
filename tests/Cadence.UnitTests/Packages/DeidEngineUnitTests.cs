using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Cadence.Deid;
using Cadence.Testing;
using Cadence.Testing.Time;

using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class DeidEngineUnitTests
{
    // Random per run: the de-identification keys are secrets and have no defaults (#5016).
    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(DicomDeidentifier.MinimumSigningKeyLength);
    private static readonly byte[] PseudonymKey = RandomNumberGenerator.GetBytes(DicomDeidentifier.MinimumSigningKeyLength);
    [Fact]
    public void DicomDeidentifier_DeidentifiesDicomStream_StripsHeaderTags_ShiftsDates_MasksPixels_ReturnsSignedCertificate()
    {
        byte[] inputBytes = MinimalDicom();

        var deidentifier = new DicomDeidentifier(SigningKey, PseudonymKey);
        var result = deidentifier.Deidentify(
            dicomBytes: inputBytes,
            subjectId: "SUBJ-101",
            siteId: "SITE-01",
            studyId: "STUDY-2026",
            deidentifiedAt: FakeClock.DefaultStart,
            dateShiftDays: 30,
            applyPixelMask: true
        );

        result.Should().NotBeNull();
        result.AnonymizedStudyUid.Should().StartWith(DicomDeidentifier.CadenceDicomUidRoot);
        result.AnonymizedStudyUid.Length.Should().BeLessThanOrEqualTo(64);
        result.AnonymizedSopUid.Should().StartWith(DicomDeidentifier.CadenceDicomUidRoot);
        result.Modality.Should().Be("US");
        result.Rows.Should().Be(100);
        result.Columns.Should().Be(100);

        using var outMs = new MemoryStream(result.DeidentifiedBytes);
        var outputDicom = DicomFile.Open(outMs);
        var outDataset = outputDicom.Dataset;

        outDataset.GetSingleValue<string>(DicomTag.PatientID).Should().Be("SUBJ-101");
        outDataset.GetSingleValue<string>(DicomTag.PatientName).Should().Be("SUBJ^SUBJ-101");
        outDataset.GetSingleValue<string>(DicomTag.InstitutionName).Should().Be("SITE-01");
        outDataset.GetSingleValue<string>(DicomTag.StudyID).Should().Be("STUDY-2026");

        outDataset.Contains(DicomTag.PatientBirthDate).Should().BeFalse();
        outDataset.Contains(DicomTag.PatientAge).Should().BeFalse();

        outDataset.GetSingleValue<string>(DicomTag.StudyDate).Should().Be("20260214");
        outDataset.GetSingleValue<string>(DicomTag.StudyTime).Should().Be("000000.000000");

        result.Certificate.Should().NotBeNull();
        result.Certificate.PixelMaskApplied.Should().BeTrue();
        result.Certificate.SubjectId.Should().Be("SUBJ-101");
        result.Certificate.Timestamp.Should().Be("2026-01-01T00:00:00.0000000Z");
        deidentifier.VerifyCertificate(result.Certificate).Should().BeTrue();
    }

    /// <summary>A 100x100 ultrasound image with patient, institution and date tags to strip.</summary>
    private static byte[] MinimalDicom()
    {
        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.UltrasoundImageStorage },
            { DicomTag.SOPInstanceUID, "1.2.840.10008.5.1.4.1.1.6.1.1001" },
            { DicomTag.StudyInstanceUID, "1.2.840.10008.5.1.4.1.1.6.1.2001" },
            { DicomTag.SeriesInstanceUID, "1.2.840.10008.5.1.4.1.1.6.1.3001" },
            { DicomTag.PatientName, "DOE^JOHN" },
            { DicomTag.PatientBirthDate, "19800101" },
            { DicomTag.PatientAge, "046Y" },
            { DicomTag.StudyDate, "20260115" },
            { DicomTag.StudyTime, "143000.000000" },
            { DicomTag.InstitutionName, "ORIGINAL_CLINIC" },
            { DicomTag.Modality, "US" },
            { DicomTag.Rows, (ushort)100 },
            { DicomTag.Columns, (ushort)100 },
            { DicomTag.BitsAllocated, (ushort)16 },
            { DicomTag.BitsStored, (ushort)12 },
            { DicomTag.HighBit, (ushort)11 },
            { DicomTag.PixelRepresentation, (ushort)0 },
            { DicomTag.SamplesPerPixel, (ushort)1 },
            { DicomTag.PhotometricInterpretation, PhotometricInterpretation.Monochrome2.Value }
        };

        byte[] pixelBytes = new byte[100 * 100 * 2];
        Array.Fill<byte>(pixelBytes, 0xFF);
        dataset.AddOrUpdate(new DicomOtherByte(DicomTag.PixelData, pixelBytes));

        var dicomFile = new DicomFile(dataset);
        using var ms = new MemoryStream();
        dicomFile.Save(ms);
        return ms.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void DicomDeidentifier_RefusesAMissingOrShortSigningKey(int length)
    {
        var missing = () => new DicomDeidentifier(null!, PseudonymKey);
        var shortKey = () => new DicomDeidentifier(new byte[length], PseudonymKey);

        missing.Should().Throw<ArgumentNullException>();
        shortKey.Should().Throw<ArgumentException>().WithMessage("*at least 32 bytes*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void DicomDeidentifier_RefusesAMissingOrShortPseudonymizationKey(int length)
    {
        var missing = () => new DicomDeidentifier(SigningKey, null!);
        var shortKey = () => new DicomDeidentifier(SigningKey, new byte[length]);

        missing.Should().Throw<ArgumentNullException>();
        shortKey.Should().Throw<ArgumentException>().WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void DicomDeidentifier_RefusesOneKeyForBothPurposes()
    {
        var shared = () => new DicomDeidentifier(SigningKey, [.. SigningKey]);

        shared.Should().Throw<ArgumentException>().WithMessage("*must be different keys*");
    }

    [Fact]
    public void DicomDeidentifier_CertificateSignedWithOneKey_DoesNotVerifyWithAnother()
    {
        var signer = new DicomDeidentifier(SigningKey, PseudonymKey);
        var result = signer.Deidentify(
            dicomBytes: MinimalDicom(),
            subjectId: "SUBJ-102",
            siteId: "SITE-01",
            studyId: "STUDY-2026",
            deidentifiedAt: FakeClock.DefaultStart,
            dateShiftDays: 0,
            applyPixelMask: false);

        signer.VerifyCertificate(result.Certificate).Should().BeTrue();
        new DicomDeidentifier(RandomNumberGenerator.GetBytes(32), PseudonymKey).VerifyCertificate(result.Certificate).Should().BeFalse();
    }

    /// <summary>A payload (a host's batch manifest) signed under one key does not verify under another, or once altered.</summary>
    [Fact]
    public void DicomDeidentifier_PayloadSignedWithOneKey_DoesNotVerifyWithAnotherOrWhenAltered()
    {
        var signer = new DicomDeidentifier(SigningKey, PseudonymKey);
        var payload = new Dictionary<string, object?> { ["manifest_id"] = "m-1", ["ingested_slice_count"] = 3 };
        string signature = signer.SignPayload(payload);

        signer.VerifyPayload(payload, signature).Should().BeTrue();
        new DicomDeidentifier(RandomNumberGenerator.GetBytes(32), PseudonymKey).VerifyPayload(payload, signature).Should().BeFalse();
        signer.VerifyPayload(new Dictionary<string, object?> { ["manifest_id"] = "m-1", ["ingested_slice_count"] = 4 }, signature)
            .Should().BeFalse();
        signer.VerifyPayload(payload, null).Should().BeFalse();
    }

    /// <summary>
    /// The anonymized UIDs and original-UID hashes are keyed by the pseudonymization key (#5399): stable under one key,
    /// different under another, and not the plain SHA-256 a holder of the original UID could recompute.
    /// </summary>
    [Fact]
    public void DicomDeidentifier_AnonymizedUidsAndOriginalHashes_AreKeyedByThePseudonymizationKey()
    {
        var first = new DicomDeidentifier(SigningKey, PseudonymKey);
        var again = new DicomDeidentifier(RandomNumberGenerator.GetBytes(32), PseudonymKey);
        var otherKey = new DicomDeidentifier(SigningKey, RandomNumberGenerator.GetBytes(32));
        DicomDeidResult Run(DicomDeidentifier d) => d.Deidentify(
            MinimalDicom(), "SUBJ-103", "SITE-01", "STUDY-2026", FakeClock.DefaultStart, applyPixelMask: false);

        var a = Run(first);
        var b = Run(again);
        var c = Run(otherKey);

        b.AnonymizedStudyUid.Should().Be(a.AnonymizedStudyUid);
        b.AnonymizedSopUid.Should().Be(a.AnonymizedSopUid);
        b.Certificate.OriginalStudyInstanceUidHash.Should().Be(a.Certificate.OriginalStudyInstanceUidHash);

        c.AnonymizedStudyUid.Should().NotBe(a.AnonymizedStudyUid);
        c.AnonymizedSeriesUid.Should().NotBe(a.AnonymizedSeriesUid);
        c.AnonymizedSopUid.Should().NotBe(a.AnonymizedSopUid);
        c.Certificate.OriginalStudyInstanceUidHash.Should().NotBe(a.Certificate.OriginalStudyInstanceUidHash);
        c.Certificate.OriginalSopInstanceUidHash.Should().NotBe(a.Certificate.OriginalSopInstanceUidHash);

        const string originalStudyUid = "1.2.840.10008.5.1.4.1.1.6.1.2001"; // MinimalDicom's StudyInstanceUID
        a.Certificate.OriginalStudyInstanceUidHash.Should()
            .NotBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(originalStudyUid))));
        a.Certificate.OriginalStudyInstanceUidHash.Should().Be(first.HashOriginalUid(originalStudyUid));
    }

    [Fact]
    public void DicomDeidentifier_ScopesAllFourUidsByLengthPrefixedStudySiteAndSubject_WithoutColonCollisions()
    {
        var deidentifier = new DicomDeidentifier(SigningKey, PseudonymKey);
        byte[] sample = MinimalDicomWithFrameOfReference();

        (string StudyUid, string SeriesUid, string SopUid, string FrameUid) DeidentifyWithScope(
            string studyId,
            string siteId,
            string subjectId)
        {
            var res = deidentifier.Deidentify(
                dicomBytes: sample,
                subjectId: subjectId,
                siteId: siteId,
                studyId: studyId,
                deidentifiedAt: FakeClock.DefaultStart,
                applyPixelMask: false);

            using var sliceStream = new MemoryStream(sample, writable: false);
            var sliceRes = deidentifier.DeidentifySlice(
                dicomStream: sliceStream,
                subjectId: subjectId,
                siteId: siteId,
                studyId: studyId,
                applyPixelMask: false);

            sliceRes.AnonymizedStudyUid.Should().Be(res.AnonymizedStudyUid);
            sliceRes.AnonymizedSeriesUid.Should().Be(res.AnonymizedSeriesUid);
            sliceRes.AnonymizedSopUid.Should().Be(res.AnonymizedSopUid);

            using var outMs = new MemoryStream(res.DeidentifiedBytes, writable: false);
            var outDataset = DicomFile.Open(outMs).Dataset;
            string frameUid = outDataset.GetSingleValue<string>(DicomTag.FrameOfReferenceUID);
            frameUid.Should().StartWith(DicomDeidentifier.CadenceDicomUidRoot);

            return (res.AnonymizedStudyUid, res.AnonymizedSeriesUid, res.AnonymizedSopUid, frameUid);
        }

        var baseline = DeidentifyWithScope("STUDY-01", "SITE-01", "SUBJ-01");
        var repeat = DeidentifyWithScope("STUDY-01", "SITE-01", "SUBJ-01");
        repeat.Should().Be(baseline);

        var differentSite = DeidentifyWithScope("STUDY-01", "SITE-02", "SUBJ-01");
        var differentSubject = DeidentifyWithScope("STUDY-01", "SITE-01", "SUBJ-02");
        var differentStudy = DeidentifyWithScope("STUDY-02", "SITE-01", "SUBJ-01");

        foreach (var other in new[] { differentSite, differentSubject, differentStudy })
        {
            other.StudyUid.Should().NotBe(baseline.StudyUid);
            other.SeriesUid.Should().NotBe(baseline.SeriesUid);
            other.SopUid.Should().NotBe(baseline.SopUid);
            other.FrameUid.Should().NotBe(baseline.FrameUid);
        }

        var colonPairs = new[]
        {
            (Left: DeidentifyWithScope("STUDY:A", "SITE", "SUBJ"), Right: DeidentifyWithScope("STUDY", "A:SITE", "SUBJ")),
            (Left: DeidentifyWithScope("STUDY", "SITE:B", "SUBJ"), Right: DeidentifyWithScope("STUDY", "SITE", "B:SUBJ")),
            (Left: DeidentifyWithScope("STUDY:SITE", "X", "SUBJ"), Right: DeidentifyWithScope("STUDY", "SITE:X", "SUBJ")),
        };

        foreach (var (left, right) in colonPairs)
        {
            left.StudyUid.Should().NotBe(right.StudyUid);
            left.SeriesUid.Should().NotBe(right.SeriesUid);
            left.SopUid.Should().NotBe(right.SopUid);
            left.FrameUid.Should().NotBe(right.FrameUid);
        }
    }

    [Theory]
    [InlineData(null, "SITE-01", "STUDY-01")]
    [InlineData("", "SITE-01", "STUDY-01")]
    [InlineData("   ", "SITE-01", "STUDY-01")]
    [InlineData("SUBJ-01", null, "STUDY-01")]
    [InlineData("SUBJ-01", "", "STUDY-01")]
    [InlineData("SUBJ-01", "   ", "STUDY-01")]
    [InlineData("SUBJ-01", "SITE-01", null)]
    [InlineData("SUBJ-01", "SITE-01", "")]
    [InlineData("SUBJ-01", "SITE-01", "   ")]
    public void DicomDeidentifier_RefusesNullOrWhitespaceScopeIdentifiers(string? subjectId, string? siteId, string? studyId)
    {
        var deidentifier = new DicomDeidentifier(SigningKey, PseudonymKey);
        byte[] sample = MinimalDicom();

        Action deidentifyAct = () => deidentifier.Deidentify(
            dicomBytes: sample,
            subjectId: subjectId!,
            siteId: siteId!,
            studyId: studyId!,
            deidentifiedAt: FakeClock.DefaultStart,
            applyPixelMask: false);

        Action sliceAct = () =>
        {
            using var ms = new MemoryStream(sample, writable: false);
            _ = deidentifier.DeidentifySlice(
                dicomStream: ms,
                subjectId: subjectId!,
                siteId: siteId!,
                studyId: studyId!,
                applyPixelMask: false);
        };

        deidentifyAct.Should().Throw<ArgumentException>();
        sliceAct.Should().Throw<ArgumentException>();
    }

    private static byte[] MinimalDicomWithFrameOfReference()
    {
        using var ms = new MemoryStream(MinimalDicom());
        var file = DicomFile.Open(ms);
        file.Dataset.AddOrUpdate(DicomTag.FrameOfReferenceUID, "1.2.840.10008.5.1.4.1.1.6.1.4001");
        using var outMs = new MemoryStream();
        file.Save(outMs);
        return outMs.ToArray();
    }

    [Fact]
    [GxPRequirement("PRD-IMG-002")]
    public void MaskBurnedInPixels_ThrowsDicomPixelDeidException_WhenPixelMaskingDecodingFails()
    {
        byte[] corruptedBytes = UnsupportedCompressedDicom();
        using var ms = new MemoryStream(corruptedBytes);
        var dicomFile = DicomFile.Open(ms);
        var deidentifier = new DicomDeidentifier(SigningKey, PseudonymKey);

        Action act = () => deidentifier.MaskBurnedInPixels(dicomFile.Dataset, autoDetectHeaders: true);

        act.Should().Throw<DicomPixelDeidException>()
            .WithMessage("*compressed DICOM transfer syntax*");
    }

    [Fact]
    [GxPRequirement("PRD-IMG-002")]
    public void Deidentify_DoesNotGenerateOrSignCertificate_WhenPixelMaskingFails()
    {
        byte[] corruptedBytes = UnsupportedCompressedDicom();
        var deidentifier = new DicomDeidentifier(SigningKey, PseudonymKey);

        Action act = () => deidentifier.Deidentify(
            dicomBytes: corruptedBytes,
            subjectId: "SUBJ-999",
            siteId: "SITE-99",
            studyId: "STUDY-2026",
            deidentifiedAt: FakeClock.DefaultStart,
            applyPixelMask: true);

        act.Should().Throw<DicomPixelDeidException>();
    }

    private static byte[] UnsupportedCompressedDicom()
    {
        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
            { DicomTag.SOPInstanceUID, "1.2.840.10008.5.1.4.1.1.7.1001" },
            { DicomTag.StudyInstanceUID, "1.2.840.10008.5.1.4.1.1.7.2001" },
            { DicomTag.SeriesInstanceUID, "1.2.840.10008.5.1.4.1.1.7.3001" },
            { DicomTag.PatientName, "CORRUPTED^PATIENT" },
            { DicomTag.Modality, "SC" },
            { DicomTag.Rows, (ushort)100 },
            { DicomTag.Columns, (ushort)100 },
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

    [Fact]
    public void DeidentifySlice_MatchesDeidentify_OnHashesSizesAnonymizedUidsMasksAndScrubbedTags()
    {
        var deidentifier = new DicomDeidentifier(SigningKey, PseudonymKey);

        foreach (byte[] sample in new[] { MinimalDicom(), MinimalCtWithSequenceDicom() })
        {
            var expected = deidentifier.Deidentify(
                dicomBytes: sample,
                subjectId: "SUBJ-SLICE-01",
                siteId: "SITE-01",
                studyId: "STUDY-2026",
                deidentifiedAt: FakeClock.DefaultStart,
                dateShiftDays: 21,
                applyPixelMask: true);

            using var sourceStream = new MemoryStream(sample, writable: false);
            var actual = deidentifier.DeidentifySlice(
                dicomStream: sourceStream,
                subjectId: "SUBJ-SLICE-01",
                siteId: "SITE-01",
                studyId: "STUDY-2026",
                dateShiftDays: 21,
                applyPixelMask: true);

            actual.SourceSha256.Should().Be(expected.Certificate.SourceSha256);
            actual.TargetSha256.Should().Be(expected.Certificate.TargetSha256);
            actual.DeidentifiedSizeBytes.Should().Be(expected.DeidentifiedBytes.Length);
            actual.AnonymizedStudyUid.Should().Be(expected.AnonymizedStudyUid);
            actual.AnonymizedSeriesUid.Should().Be(expected.AnonymizedSeriesUid);
            actual.AnonymizedSopUid.Should().Be(expected.AnonymizedSopUid);
            actual.OriginalStudyInstanceUidHash.Should().Be(expected.Certificate.OriginalStudyInstanceUidHash);
            actual.OriginalSopInstanceUidHash.Should().Be(expected.Certificate.OriginalSopInstanceUidHash);
            actual.PixelMaskApplied.Should().Be(expected.Certificate.PixelMaskApplied);
            actual.PixelMasks.Should().Equal(expected.Certificate.PixelMasks);
            actual.ScrubbedTagsCount.Should().Be(expected.Certificate.ScrubbedTagsCount);
            actual.ScrubbedTags.Should().Equal(expected.Certificate.ScrubbedTags);
            actual.Modality.Should().Be(expected.Modality);
            actual.Rows.Should().Be(expected.Rows);
            actual.Columns.Should().Be(expected.Columns);
            actual.BitsAllocated.Should().Be(expected.BitsAllocated);
        }
    }

    [Fact]
    [GxPRequirement("PRD-IMG-002")]
    public void DeidentifySlice_ThrowsDicomPixelDeidException_WhenPixelMaskingFails()
    {
        byte[] corruptedBytes = UnsupportedCompressedDicom();
        var deidentifier = new DicomDeidentifier(SigningKey, PseudonymKey);
        using var stream = new MemoryStream(corruptedBytes, writable: false);

        Action act = () => deidentifier.DeidentifySlice(
            dicomStream: stream,
            subjectId: "SUBJ-999",
            siteId: "SITE-99",
            studyId: "STUDY-2026",
            applyPixelMask: true);

        act.Should().Throw<DicomPixelDeidException>();
    }

    [Fact]
    public async Task PooledBlockStream_CopyAndHash_ZeroRange_And_Seek_OperateAcrossRentedBlocks()
    {
        Action zeroSize = () => _ = new PooledBlockStream(0);
        Action lohSize = () => _ = new PooledBlockStream(85_000);
        zeroSize.Should().Throw<ArgumentOutOfRangeException>();
        lohSize.Should().Throw<ArgumentOutOfRangeException>();

        byte[] payload = new byte[200 * 1024];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)((i * 31 + 7) & 0xFF);
        }

        string expectedSha = Convert.ToHexStringLower(SHA256.HashData(payload));

        await using var sourceMs = new MemoryStream(payload, writable: false);
        var (pooled, actualSha) = await PooledBlockStream.CopyAndHashAsync(sourceMs, TestContext.Current.CancellationToken);
        try
        {
            actualSha.Should().Be(expectedSha);
            pooled.Length.Should().Be(payload.Length);
            pooled.Position.Should().Be(0);

            // Zero across the first 65,536-byte block boundary
            pooled.ZeroRange(65_500, 100);
            pooled.Seek(65_499, SeekOrigin.Begin).Should().Be(65_499);

            byte[] window = new byte[102];
            int read = await pooled.ReadAsync(window, TestContext.Current.CancellationToken);
            read.Should().Be(102);
            window[0].Should().Be(payload[65_499]);
            window.AsSpan(1, 100).ToArray().Should().OnlyContain(b => b == 0);
            window[101].Should().Be(payload[65_600]);

            pooled.Seek(-4, SeekOrigin.End).Should().Be(payload.Length - 4);
            pooled.ReadByte().Should().Be(payload[^4]);
        }
        finally
        {
            await pooled.DisposeAsync();
        }

        Action readDisposed = () => _ = pooled.Length;
        readDisposed.Should().Throw<ObjectDisposedException>();
    }

    private static byte[] MinimalCtWithSequenceDicom()
    {
        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.CTImageStorage },
            { DicomTag.SOPInstanceUID, "1.2.840.10008.5.1.4.1.1.2.4001" },
            { DicomTag.StudyInstanceUID, "1.2.840.10008.5.1.4.1.1.2.4000" },
            { DicomTag.SeriesInstanceUID, "1.2.840.10008.5.1.4.1.1.2.4000.1" },
            { DicomTag.PatientName, "DOE^JANE" },
            { DicomTag.PatientID, "RAW-CT-01" },
            { DicomTag.StudyDate, "20260201" },
            { DicomTag.Modality, "CT" },
            { DicomTag.Rows, (ushort)64 },
            { DicomTag.Columns, (ushort)64 },
            { DicomTag.BitsAllocated, (ushort)16 },
            { DicomTag.PixelSpacing, 0.8, 0.8 },
            { DicomTag.SliceThickness, 2.5 },
            { DicomTag.SliceLocation, 12.5 },
            { DicomTag.WindowCenter, 40.0 },
            { DicomTag.WindowWidth, 350.0 }
        };

        dataset.AddOrUpdate(new DicomSequence(DicomTag.RequestAttributesSequence, new DicomDataset
        {
            { DicomTag.RequestedProcedureID, "PROC-CT-01" },
            { DicomTag.ScheduledProcedureStepDescription, "CHEST_CT" }
        }));

        byte[] pixels = new byte[64 * 64 * 2];
        Array.Fill<byte>(pixels, 0x2A);
        dataset.AddOrUpdate(new DicomOtherByte(DicomTag.PixelData, pixels));

        using var ms = new MemoryStream();
        new DicomFile(dataset).Save(ms);
        return ms.ToArray();
    }
}
