using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using Cadence.Deid;
using Cadence.Kernel.Time;
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
    private static readonly byte[] PseudonymKey = RandomNumberGenerator.GetBytes(DeidTransforms.MinimumPseudonymizationKeyLength);
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

    [Fact]
    public void DeidTransforms_AppliesMaskPseudonymizeDateShiftAndAgeCap()
    {
        var detector = new DeidDetector();
        string text = "Patient DOE^JOHN email john.doe@example.com, SSN 123-45-6789, date 2026-01-15, age 95.";
        var results = detector.Detect(text);

        var strategies = new Dictionary<string, string>
        {
            [DetectorCategory.Email] = "mask",
            [DetectorCategory.SsnNationalId] = "pseudonymize",
            [DetectorCategory.Dates] = "date_shift",
            [DetectorCategory.Age] = "age_cap"
        };

        var (transformed, record) = DeidTransforms.ApplyDeidTransforms(
            text: text,
            results: results,
            strategies: strategies,
            salt: PseudonymKey,
            shiftDays: 10,
            ageCap: 89
        );

        transformed.Should().Contain("[EMAIL]");
        transformed.Should().NotContain("123-45-6789");
        transformed.Should().Contain("2026-01-25");
        transformed.Should().Contain("89+");

        record.Should().NotBeEmpty();
        foreach (var item in record)
        {
            item.Replacement.Should().NotBeNullOrWhiteSpace();
            item.Start.Should().BeGreaterThanOrEqualTo(0);
            item.End.Should().BeGreaterThan(item.Start);
        }
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
        static DicomDeidResult Run(DicomDeidentifier d) => d.Deidentify(
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
    public void DeidTransforms_Pseudonymize_WithoutAKey_IsRefused()
    {
        var detector = new DeidDetector();
        string text = "SSN 123-45-6789.";
        var results = detector.Detect(text);
        var strategies = new Dictionary<string, string> { [DetectorCategory.SsnNationalId] = "pseudonymize" };

        var withoutKey = () => DeidTransforms.ApplyDeidTransforms(text, results, strategies);
        var withShortKey = () => DeidTransforms.ApplyDeidTransforms(text, results, strategies, salt: new byte[16]);

        withoutKey.Should().Throw<ArgumentException>().WithMessage("*no default key*");
        withShortKey.Should().Throw<ArgumentException>().WithMessage("*no default key*");
    }

    [Fact]
    public void DeidTransforms_MaskOnly_NeedsNoKey()
    {
        var detector = new DeidDetector();
        string text = "Email john.doe@example.com.";

        var (transformed, _) = DeidTransforms.ApplyDeidTransforms(text, detector.Detect(text));

        transformed.Should().Contain("[EMAIL]");
    }

    [Fact]
    public void DeidTransforms_PseudonymsDependOnTheKey()
    {
        byte[] otherKey = RandomNumberGenerator.GetBytes(32);

        DeidTransforms.PseudonymizeValue("123-45-6789", PseudonymKey)
            .Should().Be(DeidTransforms.PseudonymizeValue("123-45-6789", PseudonymKey))
            .And.NotBe(DeidTransforms.PseudonymizeValue("123-45-6789", otherKey));
    }

    [Fact]
    public void DeidTransforms_ShiftDateString_PreservesFormats()
    {
        DeidTransforms.ShiftDateString("2026-01-15", 10).Should().Be("2026-01-25");
        DeidTransforms.ShiftDateString("2026/01/15", 10).Should().Be("2026/01/25");
        DeidTransforms.ShiftDateString("01/15/2026", 10).Should().Be("01/25/2026");
        DeidTransforms.ShiftDateString("15-Jan-2026", 10).Should().Be("25-Jan-2026");
        DeidTransforms.ShiftDateString("Jan 15, 2026", 10).Should().Be("Jan 25, 2026");
        DeidTransforms.ShiftDateString("invalid date", 10).Should().Be("[DATE_INVALID]");
    }

    [Fact]
    public void DeidTransforms_SubjectDateShift_And_CapAge()
    {
        string salt = Convert.ToBase64String(PseudonymKey);
        int shift1 = DeidTransforms.GetSubjectDateShift("SUBJ-101", salt);
        int shift2 = DeidTransforms.GetSubjectDateShift("SUBJ-101", salt);

        shift1.Should().Be(shift2);
        shift1.Should().BeInRange(-365, 365);

        var shiftedIso = DeidTransforms.ShiftDateBySubject("2026-01-15", "SUBJ-101", salt);
        shiftedIso.Should().NotBe("2026-01-15");

        var shiftedSas = DeidTransforms.ShiftDateBySubject(22000, "SUBJ-101", salt);
        shiftedSas.Should().Be(22000 + shift1);

        DeidTransforms.CapAgeNumeric(95, 89).Should().Be(89);
        DeidTransforms.CapAgeNumeric(80, 89).Should().Be(80);

        DeidTransforms.NormalizeAndCapAge(95, 89).Should().Be(89);
        DeidTransforms.NormalizeAndCapAge("95 years old", 89).Should().Be("89");
        DeidTransforms.NormalizeAndCapAge("80 years old", 89).Should().Be("80 years old");
    }

    [Fact]
    public void DeidTransforms_ScrubErrorMessage_StripsIdentifiersAndQuotedValues()
    {
        string rawMsg = "Failed processing SUBJ-101 at SITE-01 for STUDY-2026: SSN 123-45-6789, email john@example.com with value 'secret_token'";
        string scrubbed = DeidTransforms.ScrubErrorMessage(rawMsg);

        scrubbed.Should().Contain("[REDACTED_SUBJECT]");
        scrubbed.Should().Contain("[REDACTED_SITE]");
        scrubbed.Should().Contain("[REDACTED_STUDY]");
        scrubbed.Should().Contain("[REDACTED_SSN]");
        scrubbed.Should().Contain("[REDACTED_EMAIL]");
        scrubbed.Should().Contain("'[REDACTED_VALUE]'");
        scrubbed.Should().NotContain("123-45-6789");
        scrubbed.Should().NotContain("john@example.com");
        scrubbed.Should().NotContain("secret_token");
    }

    [Fact]
    public void DeidAirGapVault_DeidentifiesAndRehydratesTextStructuredDataAndChatMessages()
    {
        using var vault = new DeidAirGapVault();
        string text = "Patient email john.doe@example.com, SSN 123-45-6789.";

        string deidentifiedText = vault.DeidentifyText(text);
        deidentifiedText.Should().Contain("[SURROGATE_EMAIL_1]");
        deidentifiedText.Should().Contain("[SURROGATE_SSN_NATIONAL_ID_1]");
        vault.HasSurrogates.Should().BeTrue();
        vault.SurrogateCount.Should().Be(2);

        string rehydratedText = vault.RehydrateText(deidentifiedText);
        rehydratedText.Should().Be(text);

        var structuredData = new Dictionary<string, object?>
        {
            ["patient_email"] = "john.doe@example.com",
            ["details"] = new List<object> { "Contact at 123-45-6789" }
        };

        var deidentifiedStruct = vault.DeidentifyStructuredData(structuredData) as Dictionary<string, object?>;
        deidentifiedStruct.Should().NotBeNull();
        deidentifiedStruct!["patient_email"].Should().Be("[SURROGATE_EMAIL_1]");

        var rehydratedStruct = vault.RehydrateStructuredData(deidentifiedStruct) as Dictionary<string, object?>;
        rehydratedStruct.Should().NotBeNull();
        rehydratedStruct!["patient_email"].Should().Be("john.doe@example.com");

        var messages = new List<Dictionary<string, object?>>
        {
            new() { ["role"] = "user", ["content"] = "Please contact john.doe@example.com" }
        };

        var deidentifiedMsgs = vault.DeidentifyMessages(messages);
        deidentifiedMsgs[0]["content"].Should().Be("Please contact [SURROGATE_EMAIL_1]");
    }

    [Fact]
    public void DeidAirGapVault_IDisposable_PurgesInMemoryMappings()
    {
        DeidAirGapVault vault = new DeidAirGapVault();
        vault.DeidentifyText("john.doe@example.com");
        vault.HasSurrogates.Should().BeTrue();

        vault.Dispose();

        vault.HasSurrogates.Should().BeFalse();
        vault.SurrogateCount.Should().Be(0);
    }

    [Fact]
    public void RedactionManifest_BuildsAndVerifiesSymmetricAndAsymmetricSignatures()
    {
        var record = new List<RedactionRecordItem>
        {
            new("email", "mask", 0, 20, "[EMAIL]"),
            new("ssn_national_id", "pseudonymize", 25, 36, "pseudo_123")
        };

        var manifest = RedactionManifestBuilder.BuildRedactionManifest(
            redactionRecord: record,
            operatorIdentity: "compliance.officer",
            reason: "Protocol 101 Anonymization",
            sourceVersion: "v1.0",
            targetVersion: "v1.1",
            recordedAt: FakeClock.DefaultStart
        );

        manifest.CategoriesCounts["email"].Should().Be(1);
        manifest.CategoriesCounts["ssn_national_id"].Should().Be(1);
        manifest.Timestamp.Should().Be("2026-01-01T00:00:00.0000000Z");

        byte[] hmacKey = "secret-hmac-key-999"u8.ToArray();
        RedactionManifestBuilder.SignManifestSymmetric(manifest, hmacKey);
        manifest.Signature.Should().NotBeNullOrWhiteSpace();
        RedactionManifestBuilder.VerifyManifestSymmetric(manifest, hmacKey).Should().BeTrue();

        // Key material is generated per test run (issue #4765): no private key is committed to the repository.
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest(
            "CN=Cadence Deid Test Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(SystemClock.Instance.UtcNow.AddMinutes(-5), SystemClock.Instance.UtcNow.AddDays(1));
        string privateKeyPem = rsa.ExportRSAPrivateKeyPem();
        string certPem = certificate.ExportCertificatePem();

        RedactionManifestBuilder.SignManifestAsymmetric(manifest, privateKeyPem);
        manifest.Signature.Should().NotBeNullOrWhiteSpace();
        RedactionManifestBuilder.VerifyManifestAsymmetric(manifest, certPem).Should().BeTrue();

        var invalidManifest = new RedactionManifest();
        Action act = () => invalidManifest.Reason = "";
        act.Should().Throw<ArgumentException>();
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
}
