using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

using Cadence.Security;

using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;

namespace Cadence.Deid;

public class DicomDeidentifier
{
    public const string CadenceDicomUidRoot = "1.2.826.0.1.3680043.10.99.";

    private readonly byte[] _signingSecret;
    private readonly byte[] _pseudonymizationKey;

    private static readonly DicomTag[] TagsToRemove =
    [
        DicomTag.PatientBirthDate,
        DicomTag.PatientBirthTime,
        DicomTag.PatientSex,
        DicomTag.PatientAge,
        DicomTag.PatientWeight,
        DicomTag.PatientSize,
        DicomTag.PatientAddress,
        DicomTag.PatientMotherBirthName,
        DicomTag.PatientTelephoneNumbers,
        new DicomTag(0x0010, 0x2160), // EthnicGroup, retired by DICOM; fo-dicom 5.2 names it EthnicGroupRETIRED
        DicomTag.EthnicGroupCodeSequence,
        DicomTag.EthnicGroups,
        new DicomTag(0x0010, 0x1000), // OtherPatientIDs
        DicomTag.OtherPatientIDsSequence,
        DicomTag.OtherPatientNames,
        DicomTag.IssuerOfPatientID,
        new DicomTag(0x0010, 0x1090), // MedicalRecordLocator
        DicomTag.MilitaryRank,
        DicomTag.BranchOfService,
        DicomTag.PatientComments,
        DicomTag.SpecialNeeds,
        DicomTag.PatientState,
        DicomTag.Occupation,
        DicomTag.AdditionalPatientHistory,
        DicomTag.PatientReligiousPreference,
        DicomTag.InstitutionAddress,
        DicomTag.InstitutionalDepartmentName,
        DicomTag.StationName,
        DicomTag.DeviceSerialNumber,
        DicomTag.InstitutionalDepartmentTypeCodeSequence,
        DicomTag.ReferringPhysicianName,
        DicomTag.ReferringPhysicianAddress,
        DicomTag.ReferringPhysicianTelephoneNumbers,
        DicomTag.ReferringPhysicianIdentificationSequence,
        DicomTag.PerformingPhysicianName,
        DicomTag.PerformingPhysicianIdentificationSequence,
        new DicomTag(0x0008, 0x1048), // PhysicianOfRecord
        new DicomTag(0x0008, 0x1049), // PhysicianOfRecordIdentificationSequence
        DicomTag.ConsultingPhysicianName,
        DicomTag.ConsultingPhysicianIdentificationSequence,
        DicomTag.NameOfPhysiciansReadingStudy,
        DicomTag.PhysiciansReadingStudyIdentificationSequence,
        DicomTag.OperatorsName,
        DicomTag.OperatorIdentificationSequence,
        DicomTag.RequestingPhysician,
        DicomTag.OrderCallbackPhoneNumber,
        DicomTag.AccessionNumber,
        DicomTag.IssuerOfAccessionNumberSequence,
        DicomTag.RequestedProcedureID,
        DicomTag.ScheduledProcedureStepID,
        DicomTag.ScheduledProcedureStepDescription,
        DicomTag.RequestedProcedureDescription,
        DicomTag.StudyID,
        DicomTag.FillerOrderNumberImagingServiceRequest,
        DicomTag.PlacerOrderNumberImagingServiceRequest,
        DicomTag.StudyDescription,
        DicomTag.SeriesDescription
    ];

    private static readonly DicomTag[] DateTags =
    [
        DicomTag.StudyDate,
        DicomTag.SeriesDate,
        DicomTag.AcquisitionDate,
        DicomTag.ContentDate,
        new DicomTag(0x0008, 0x0024), // OverlayDate
        new DicomTag(0x0008, 0x0025)  // CurveDate
    ];

    private static readonly DicomTag[] TimeTags =
    [
        DicomTag.StudyTime,
        DicomTag.SeriesTime,
        DicomTag.AcquisitionTime,
        DicomTag.ContentTime,
        new DicomTag(0x0008, 0x0034), // OverlayTime
        new DicomTag(0x0008, 0x0035)  // CurveTime
    ];

    /// <summary>The shortest certificate signing key accepted, in bytes (#5016).</summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>
    /// Creates a de-identifier. <paramref name="signingSecret"/> signs certificates and manifests;
    /// <paramref name="pseudonymizationKey"/> keys the anonymized UIDs and the original-UID hashes, so nobody without it
    /// can link a de-identified study back to a UID they hold (#5399). Both are secrets from configuration of at least
    /// <see cref="MinimumSigningKeyLength"/> bytes, and they must differ. There is no default key: a published one would
    /// let anyone forge a certificate (#5016).
    /// </summary>
    public DicomDeidentifier(byte[] signingSecret, byte[] pseudonymizationKey)
    {
        RequireKey(signingSecret, "certificate signing", nameof(signingSecret));
        RequireKey(pseudonymizationKey, "UID pseudonymization", nameof(pseudonymizationKey));
        if (CryptographicOperations.FixedTimeEquals(signingSecret, pseudonymizationKey))
        {
            throw new ArgumentException(
                "The de-identification signing key and pseudonymization key must be different keys.", nameof(pseudonymizationKey));
        }

        _signingSecret = [.. signingSecret];
        _pseudonymizationKey = [.. pseudonymizationKey];
    }

    private static void RequireKey(byte[]? key, string purpose, string parameterName)
    {
        if (key is null) throw new ArgumentNullException(parameterName);
        if (key.Length < MinimumSigningKeyLength)
        {
            throw new ArgumentException(
                $"The de-identification {purpose} key must be at least {MinimumSigningKeyLength} bytes.", parameterName);
        }
    }

    /// <summary>
    /// A DICOM UID under <see cref="CadenceDicomUidRoot"/> derived from <paramref name="originalUid"/> with
    /// HMAC-SHA256 under the pseudonymization key: stable for one key, unlinkable without it.
    /// </summary>
    public string GenerateUid(string originalUid, string namespaceSalt)
    {
        string combined = $"{originalUid}:{namespaceSalt}";
        byte[] hash = HMACSHA256.HashData(_pseudonymizationKey, Encoding.UTF8.GetBytes(combined));
        byte[] first16 = hash[..16];
        var num = new BigInteger(first16, isUnsigned: true, isBigEndian: true);
        string fullUid = $"{CadenceDicomUidRoot}{num}";
        return fullUid.Substring(0, Math.Min(64, fullUid.Length));
    }

    /// <summary>The keyed hash of an original UID recorded on the certificate (HMAC-SHA256, lower-case hex).</summary>
    public string HashOriginalUid(string originalUid)
    {
        ArgumentNullException.ThrowIfNull(originalUid);
        return Convert.ToHexStringLower(HMACSHA256.HashData(_pseudonymizationKey, Encoding.UTF8.GetBytes($"original-uid:{originalUid}")));
    }

    /// <summary>Signs <paramref name="payload"/> (canonical JSON, HMAC-SHA256) with the certificate signing key.</summary>
    public string SignPayload(object payload) => CanonicalSigner.GenerateCanonicalSignature(payload, _signingSecret);

    /// <summary>Whether <paramref name="signature"/> is the certificate signing key's signature of <paramref name="payload"/>.</summary>
    public bool VerifyPayload(object payload, string? signature) =>
        !string.IsNullOrWhiteSpace(signature) && CanonicalSigner.VerifyCanonicalSignature(payload, signature, _signingSecret);

    private static string ShiftDicomDate(string dateStr, int days)
    {
        if (string.IsNullOrEmpty(dateStr) || dateStr.Length != 8)
        {
            return dateStr;
        }

        if (DateTime.TryParseExact(dateStr, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
        {
            return dt.AddDays(days).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        }

        return dateStr;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods should be static", Justification = "Instance method API consistency")]
    public List<PixelMaskBoundingBox> MaskBurnedInPixels(
        DicomDataset dataset,
        List<PixelMaskBoundingBox>? customMasks = null,
        bool autoDetectHeaders = true)
    {
        if (!TryBuildPixelMasks(dataset, customMasks, autoDetectHeaders, out ushort rows, out ushort cols, out var masksToApply))
        {
            return [];
        }

        try
        {
            EnsureUncompressedTransferSyntax(dataset);

            var pixelData = DicomPixelData.Create(dataset);
            IByteBuffer frameBuffer = pixelData.GetFrame(0);
            byte[] bytes = frameBuffer.Data.ToArray();
            int bytesPerPixel = ResolveBytesPerPixel(dataset);

            var appliedMasks = ApplyMaskBoxesToArray(bytes, rows, cols, bytesPerPixel, masksToApply);

            dataset.AddOrUpdate(new DicomOtherByte(DicomTag.PixelData, bytes));
            dataset.AddOrUpdate(DicomTag.BurnedInAnnotation, "NO");
            return appliedMasks;
        }
        catch (Exception ex) when (ex is not DicomPixelDeidException)
        {
            throw new DicomPixelDeidException("Failed to decode or mask pixel data for DICOM dataset.", ex);
        }
    }

    private static List<PixelMaskBoundingBox> MaskBurnedInPixelsPooled(
        DicomDataset dataset,
        List<PixelMaskBoundingBox>? customMasks,
        bool autoDetectHeaders,
        out PooledBlockStream? maskedPixelStream)
    {
        maskedPixelStream = null;
        if (!TryBuildPixelMasks(dataset, customMasks, autoDetectHeaders, out ushort rows, out ushort cols, out var masksToApply))
        {
            return [];
        }

        PooledBlockStream? framePool = null;
        try
        {
            EnsureUncompressedTransferSyntax(dataset);

            var pixelData = DicomPixelData.Create(dataset);
            IByteBuffer frameBuffer = pixelData.GetFrame(0);

            framePool = new PooledBlockStream();
            frameBuffer.CopyToStream(framePool);
            int bytesPerPixel = ResolveBytesPerPixel(dataset);

            var appliedMasks = ApplyMaskBoxesToPooledStream(framePool, rows, cols, bytesPerPixel, masksToApply);

            dataset.AddOrUpdate(new DicomOtherByte(DicomTag.PixelData, new StreamByteBuffer(framePool, 0, framePool.Length)));
            dataset.AddOrUpdate(DicomTag.BurnedInAnnotation, "NO");
            maskedPixelStream = framePool;
            framePool = null;
            return appliedMasks;
        }
        catch (Exception ex) when (ex is not DicomPixelDeidException)
        {
            throw new DicomPixelDeidException("Failed to decode or mask pixel data for DICOM dataset.", ex);
        }
        finally
        {
            framePool?.Dispose();
        }
    }

    private static bool TryBuildPixelMasks(
        DicomDataset dataset,
        List<PixelMaskBoundingBox>? customMasks,
        bool autoDetectHeaders,
        out ushort rows,
        out ushort cols,
        out List<PixelMaskBoundingBox> masksToApply)
    {
        rows = 0;
        cols = 0;
        masksToApply = [];

        if (!dataset.Contains(DicomTag.PixelData))
        {
            return false;
        }

        rows = dataset.GetSingleValueOrDefault(DicomTag.Rows, (ushort)0);
        cols = dataset.GetSingleValueOrDefault(DicomTag.Columns, (ushort)0);
        if (rows == 0 || cols == 0)
        {
            return false;
        }

        string modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, "").ToUpperInvariant();
        string burnedInHeader = dataset.GetSingleValueOrDefault(DicomTag.BurnedInAnnotation, "").ToUpperInvariant();

        if (autoDetectHeaders && (modality == "US" || modality == "SC" || modality == "OT" || burnedInHeader == "YES"))
        {
            int topBarHeight = Math.Max(1, (int)(rows * 0.12));
            masksToApply.Add(new PixelMaskBoundingBox(0, 0, cols, topBarHeight));
        }

        if (customMasks != null)
        {
            masksToApply.AddRange(customMasks);
        }

        return masksToApply.Count > 0;
    }

    private static void EnsureUncompressedTransferSyntax(DicomDataset dataset)
    {
        if (dataset.InternalTransferSyntax.IsEncapsulated)
        {
            throw new DicomPixelDeidException(
                $"Unsupported compressed DICOM transfer syntax '{dataset.InternalTransferSyntax.UID.Name}'. Convert to uncompressed DICOM prior to de-identification.");
        }
    }

    private static int ResolveBytesPerPixel(DicomDataset dataset)
    {
        ushort bitsAllocated = dataset.GetSingleValueOrDefault(DicomTag.BitsAllocated, (ushort)16);
        ushort samplesPerPixel = dataset.GetSingleValueOrDefault(DicomTag.SamplesPerPixel, (ushort)1);
        return (bitsAllocated > 8 ? 2 : 1) * samplesPerPixel;
    }

    private static List<PixelMaskBoundingBox> ApplyMaskBoxesToArray(
        byte[] bytes,
        ushort rows,
        ushort cols,
        int bytesPerPixel,
        List<PixelMaskBoundingBox> masksToApply) =>
        ApplyMaskBoxesCore(
            rows,
            cols,
            bytesPerPixel,
            bytes.Length,
            masksToApply,
            (offset, count) => bytes.AsSpan((int)offset, Math.Min(count, bytes.Length - (int)offset)).Clear());

    private static List<PixelMaskBoundingBox> ApplyMaskBoxesToPooledStream(
        PooledBlockStream framePool,
        ushort rows,
        ushort cols,
        int bytesPerPixel,
        List<PixelMaskBoundingBox> masksToApply) =>
        ApplyMaskBoxesCore(
            rows,
            cols,
            bytesPerPixel,
            framePool.Length,
            masksToApply,
            framePool.ZeroRange);

    private static List<PixelMaskBoundingBox> ApplyMaskBoxesCore(
        ushort rows,
        ushort cols,
        int bytesPerPixel,
        long frameLength,
        List<PixelMaskBoundingBox> masksToApply,
        Action<long, int> clearAction)
    {
        var appliedMasks = new List<PixelMaskBoundingBox>(masksToApply.Count);
        foreach (var box in masksToApply)
        {
            if (!TryClampMaskBounds(box, rows, cols, out int x0, out int x1, out int y0, out int y1))
            {
                continue;
            }

            int spanBytes = (x1 - x0) * bytesPerPixel;
            for (int y = y0; y < y1; y++)
            {
                long rowStartOffset = ((long)y * cols + x0) * bytesPerPixel;
                if (rowStartOffset >= frameLength)
                {
                    break;
                }

                clearAction(rowStartOffset, spanBytes);
            }

            appliedMasks.Add(box);
        }

        return appliedMasks;
    }

    private static bool TryClampMaskBounds(
        PixelMaskBoundingBox box,
        ushort rows,
        ushort cols,
        out int x0,
        out int x1,
        out int y0,
        out int y1)
    {
        x0 = Math.Max(0, Math.Min(box.XMin, (int)cols));
        x1 = Math.Max(0, Math.Min(box.XMax, (int)cols));
        y0 = Math.Max(0, Math.Min(box.YMin, (int)rows));
        y1 = Math.Max(0, Math.Min(box.YMax, (int)rows));
        return x1 > x0 && y1 > y0;
    }

    public DicomDeidResult Deidentify(
        byte[] dicomBytes,
        string subjectId,
        string siteId,
        string studyId,
        DateTimeOffset deidentifiedAt,
        int dateShiftDays = 0,
        bool applyPixelMask = true,
        List<PixelMaskBoundingBox>? customPixelMasks = null,
        string operatorIdentity = "cadence.imaging.pipeline")
    {
        ArgumentNullException.ThrowIfNull(dicomBytes);
        ValidateScopeIdentifiers(subjectId, siteId, studyId);

        string sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(dicomBytes));

        using var inStream = new MemoryStream(dicomBytes);
        var dicomFile = DicomFile.Open(inStream, FileReadOption.ReadAll);

        var scrubbed = ScrubHeadersAndExtractGeometry(
            dicomFile,
            subjectId: subjectId,
            siteId: siteId,
            studyId: studyId,
            dateShiftDays: dateShiftDays);

        var appliedMasks = applyPixelMask
            ? MaskBurnedInPixels(dicomFile.Dataset, customPixelMasks, autoDetectHeaders: true)
            : [];

        using var outStream = new MemoryStream();
        dicomFile.Save(outStream);
        byte[] outBytes = outStream.ToArray();

        string targetSha256 = Convert.ToHexStringLower(SHA256.HashData(outBytes));

        var cert = new DicomDeidCertificate
        {
            SourceSha256 = sourceSha256,
            TargetSha256 = targetSha256,
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = subjectId,
            OriginalStudyInstanceUidHash = scrubbed.OriginalStudyUidHash,
            OriginalSopInstanceUidHash = scrubbed.OriginalSopUidHash,
            AnonymizedStudyInstanceUid = scrubbed.AnonymizedStudyUid,
            AnonymizedSopInstanceUid = scrubbed.AnonymizedSopUid,
            ScrubbedTagsCount = scrubbed.ScrubbedTags.Count,
            ScrubbedTags = scrubbed.ScrubbedTags,
            PixelMaskApplied = appliedMasks.Count > 0,
            PixelMasks = appliedMasks,
            OperatorIdentity = operatorIdentity,
            Timestamp = deidentifiedAt.UtcDateTime.ToString("o")
        };

        var certPayload = CreateCertPayloadWithoutSignature(cert);
        cert.Signature = SignPayload(certPayload);

        return new DicomDeidResult(
            DeidentifiedBytes: outBytes,
            Certificate: cert,
            AnonymizedStudyUid: scrubbed.AnonymizedStudyUid,
            AnonymizedSeriesUid: scrubbed.AnonymizedSeriesUid,
            AnonymizedSopUid: scrubbed.AnonymizedSopUid,
            Modality: scrubbed.Modality,
            Rows: scrubbed.Rows,
            Columns: scrubbed.Columns,
            BitsAllocated: scrubbed.BitsAllocated,
            PixelSpacing: scrubbed.PixelSpacing,
            SliceThickness: scrubbed.SliceThickness,
            SliceLocation: scrubbed.SliceLocation,
            WindowCenter: scrubbed.WindowCenter,
            WindowWidth: scrubbed.WindowWidth
        );
    }

    /// <summary>
    /// Scrubs PHI tags, shifts dates, masks burned-in pixels, and computes source/target SHA-256 digests and
    /// de-identified byte length from <paramref name="dicomStream"/> without retaining output DICOM buffers.
    /// </summary>
    public DicomSliceDeidResult DeidentifySlice(
        Stream dicomStream,
        string subjectId,
        string siteId,
        string studyId,
        int dateShiftDays = 0,
        bool applyPixelMask = true,
        List<PixelMaskBoundingBox>? customPixelMasks = null)
    {
        ArgumentNullException.ThrowIfNull(dicomStream);
        ValidateScopeIdentifiers(subjectId, siteId, studyId);

        var (pooledInput, sourceSha256) = PooledBlockStream.CopyAndHash(dicomStream);
        try
        {
            return DeidentifyPreloadedSlice(
                pooledInput,
                sourceSha256: sourceSha256,
                subjectId: subjectId,
                siteId: siteId,
                studyId: studyId,
                dateShiftDays: dateShiftDays,
                applyPixelMask: applyPixelMask,
                customPixelMasks: customPixelMasks);
        }
        finally
        {
            pooledInput.Dispose();
        }
    }

    /// <summary>
    /// Scrubs PHI tags, shifts dates, masks burned-in pixels, and computes the target SHA-256 digest and byte length
    /// over a preloaded <see cref="PooledBlockStream"/> with known <paramref name="sourceSha256"/> without retaining output buffers.
    /// </summary>
    public DicomSliceDeidResult DeidentifyPreloadedSlice(
        PooledBlockStream preloadedStream,
        string sourceSha256,
        string subjectId,
        string siteId,
        string studyId,
        int dateShiftDays = 0,
        bool applyPixelMask = true,
        List<PixelMaskBoundingBox>? customPixelMasks = null)
    {
        ArgumentNullException.ThrowIfNull(preloadedStream);
        ArgumentNullException.ThrowIfNull(sourceSha256);
        ValidateScopeIdentifiers(subjectId, siteId, studyId);

        preloadedStream.Position = 0;
        var dicomFile = DicomFile.Open(preloadedStream, FileReadOption.ReadLargeOnDemand, PooledBlockStream.DefaultBlockSize);

        var scrubbed = ScrubHeadersAndExtractGeometry(
            dicomFile,
            subjectId: subjectId,
            siteId: siteId,
            studyId: studyId,
            dateShiftDays: dateShiftDays);

        PooledBlockStream? maskedPixelStream = null;
        try
        {
            var appliedMasks = applyPixelMask
                ? MaskBurnedInPixelsPooled(dicomFile.Dataset, customPixelMasks, autoDetectHeaders: true, out maskedPixelStream)
                : [];

            using var hashingSink = new HashingCountingWriteStream();
            dicomFile.Save(hashingSink);
            string targetSha256 = hashingSink.CompleteHashHex();

            return new DicomSliceDeidResult(
                SourceSha256: sourceSha256,
                TargetSha256: targetSha256,
                DeidentifiedSizeBytes: hashingSink.BytesWritten,
                OriginalStudyInstanceUidHash: scrubbed.OriginalStudyUidHash,
                OriginalSopInstanceUidHash: scrubbed.OriginalSopUidHash,
                AnonymizedStudyUid: scrubbed.AnonymizedStudyUid,
                AnonymizedSeriesUid: scrubbed.AnonymizedSeriesUid,
                AnonymizedSopUid: scrubbed.AnonymizedSopUid,
                Modality: scrubbed.Modality,
                Rows: scrubbed.Rows,
                Columns: scrubbed.Columns,
                BitsAllocated: scrubbed.BitsAllocated,
                PixelSpacing: scrubbed.PixelSpacing,
                SliceThickness: scrubbed.SliceThickness,
                SliceLocation: scrubbed.SliceLocation,
                WindowCenter: scrubbed.WindowCenter,
                WindowWidth: scrubbed.WindowWidth,
                ScrubbedTagsCount: scrubbed.ScrubbedTags.Count,
                ScrubbedTags: scrubbed.ScrubbedTags,
                PixelMaskApplied: appliedMasks.Count > 0,
                PixelMasks: appliedMasks);
        }
        finally
        {
            maskedPixelStream?.Dispose();
        }
    }

    private static void ValidateScopeIdentifiers(string subjectId, string siteId, string studyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);
    }

    private static string BuildScopeSalt(string studyId, string siteId, string subjectId) =>
        $"{studyId.Length}:{studyId}:{siteId.Length}:{siteId}:{subjectId.Length}:{subjectId}";

    private ScrubbedDatasetSummary ScrubHeadersAndExtractGeometry(
        DicomFile dicomFile,
        string subjectId,
        string siteId,
        string studyId,
        int dateShiftDays)
    {
        ValidateScopeIdentifiers(subjectId, siteId, studyId);

        var dataset = dicomFile.Dataset;
        var scrubbedTags = new HashSet<string>(StringComparer.Ordinal);

        string origStudyUid = dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, Guid.NewGuid().ToString());
        string origSeriesUid = dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, Guid.NewGuid().ToString());
        string origSopUid = dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, Guid.NewGuid().ToString());
        string origForUid = dataset.GetSingleValueOrDefault(DicomTag.FrameOfReferenceUID, "");

        string origStudyHash = HashOriginalUid(origStudyUid);
        string origSopHash = HashOriginalUid(origSopUid);

        string scopeSalt = BuildScopeSalt(studyId, siteId, subjectId);
        string newStudyUid = GenerateUid(origStudyUid, $"study:{scopeSalt}");
        string newSeriesUid = GenerateUid(origSeriesUid, $"series:{scopeSalt}");
        string newSopUid = GenerateUid(origSopUid, $"sop:{scopeSalt}");

        dataset.AddOrUpdate(DicomTag.StudyInstanceUID, newStudyUid);
        dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, newSeriesUid);
        dataset.AddOrUpdate(DicomTag.SOPInstanceUID, newSopUid);

        if (!string.IsNullOrEmpty(origForUid))
        {
            dataset.AddOrUpdate(DicomTag.FrameOfReferenceUID, GenerateUid(origForUid, $"for:{scopeSalt}"));
        }

        if (dicomFile.FileMetaInfo != null)
        {
            dicomFile.FileMetaInfo.MediaStorageSOPInstanceUID = DicomUID.Parse(newSopUid);
        }

        scrubbedTags.Add("StudyInstanceUID");
        scrubbedTags.Add("SeriesInstanceUID");
        scrubbedTags.Add("SOPInstanceUID");

        foreach (var tag in TagsToRemove.Where(dataset.Contains))
        {
            dataset.Remove(tag);
            scrubbedTags.Add(tag.DictionaryEntry.Keyword);
        }

        dataset.Remove(d => d.Tag.IsPrivate);
        scrubbedTags.Add("PrivateTagsGroup");

        if (dateShiftDays != 0)
        {
            foreach (var dTag in DateTags.Where(dataset.Contains))
            {
                string val = dataset.GetSingleValueOrDefault(dTag, "");
                string shiftedVal = ShiftDicomDate(val, dateShiftDays);
                dataset.AddOrUpdate(dTag, shiftedVal);
                scrubbedTags.Add(dTag.DictionaryEntry.Keyword);
            }
        }

        foreach (var tTag in TimeTags.Where(dataset.Contains))
        {
            dataset.AddOrUpdate(tTag, "000000.000000");
            scrubbedTags.Add(tTag.DictionaryEntry.Keyword);
        }

        dataset.AddOrUpdate(DicomTag.PatientID, subjectId);
        dataset.AddOrUpdate(DicomTag.PatientName, $"SUBJ^{subjectId}");
        dataset.AddOrUpdate(DicomTag.InstitutionName, siteId);
        dataset.AddOrUpdate(DicomTag.StudyID, studyId);
        dataset.AddOrUpdate(DicomTag.ClinicalTrialSponsorName, "Cadence Clinical");
        dataset.AddOrUpdate(DicomTag.ClinicalTrialProtocolID, studyId);
        dataset.AddOrUpdate(DicomTag.ClinicalTrialSiteID, siteId);
        dataset.AddOrUpdate(DicomTag.ClinicalTrialSubjectID, subjectId);
        dataset.AddOrUpdate(DicomTag.PatientIdentityRemoved, "YES");
        dataset.AddOrUpdate(DicomTag.DeidentificationMethod, "Cadence Clinical HIPAA Safe Harbor + DICOM PS 3.15 Annex E");

        scrubbedTags.Add("PatientID");
        scrubbedTags.Add("PatientName");
        scrubbedTags.Add("InstitutionName");
        scrubbedTags.Add("ClinicalTrialProtocolID");
        scrubbedTags.Add("ClinicalTrialSiteID");
        scrubbedTags.Add("ClinicalTrialSubjectID");

        string modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, "OT").ToUpperInvariant();
        int rows = dataset.GetSingleValueOrDefault(DicomTag.Rows, (ushort)0);
        int cols = dataset.GetSingleValueOrDefault(DicomTag.Columns, (ushort)0);
        int bitsAllocated = dataset.GetSingleValueOrDefault(DicomTag.BitsAllocated, (ushort)16);

        var pixelSpacing = new List<double>();
        if (dataset.Contains(DicomTag.PixelSpacing))
        {
            try
            {
                var spacingVals = dataset.GetValues<double>(DicomTag.PixelSpacing);
                if (spacingVals != null)
                {
                    pixelSpacing.AddRange(spacingVals);
                }
            }
            catch
            {
                // Suppress parse failure on malformed DICOM header elements
            }
        }

        double? sliceThickness = dataset.Contains(DicomTag.SliceThickness) ? dataset.GetSingleValueOrDefault<double>(DicomTag.SliceThickness, 0.0) : null;
        double? sliceLocation = dataset.Contains(DicomTag.SliceLocation) ? dataset.GetSingleValueOrDefault<double>(DicomTag.SliceLocation, 0.0) : null;
        double? windowCenter = dataset.Contains(DicomTag.WindowCenter) ? dataset.GetSingleValueOrDefault<double>(DicomTag.WindowCenter, 0.0) : null;
        double? windowWidth = dataset.Contains(DicomTag.WindowWidth) ? dataset.GetSingleValueOrDefault<double>(DicomTag.WindowWidth, 0.0) : null;

        var orderedScrubbedTags = scrubbedTags.OrderBy(x => x, StringComparer.Ordinal).ToList();

        return new ScrubbedDatasetSummary(
            OriginalStudyUidHash: origStudyHash,
            OriginalSopUidHash: origSopHash,
            AnonymizedStudyUid: newStudyUid,
            AnonymizedSeriesUid: newSeriesUid,
            AnonymizedSopUid: newSopUid,
            Modality: modality,
            Rows: rows,
            Columns: cols,
            BitsAllocated: bitsAllocated,
            PixelSpacing: pixelSpacing,
            SliceThickness: sliceThickness,
            SliceLocation: sliceLocation,
            WindowCenter: windowCenter,
            WindowWidth: windowWidth,
            ScrubbedTags: orderedScrubbedTags);
    }

    private sealed record ScrubbedDatasetSummary(
        string OriginalStudyUidHash,
        string OriginalSopUidHash,
        string AnonymizedStudyUid,
        string AnonymizedSeriesUid,
        string AnonymizedSopUid,
        string Modality,
        int Rows,
        int Columns,
        int BitsAllocated,
        List<double> PixelSpacing,
        double? SliceThickness,
        double? SliceLocation,
        double? WindowCenter,
        double? WindowWidth,
        List<string> ScrubbedTags);

    private static object CreateCertPayloadWithoutSignature(DicomDeidCertificate cert)
    {
        return new Dictionary<string, object?>
        {
            ["certificate_id"] = cert.CertificateId,
            ["source_sha256"] = cert.SourceSha256,
            ["target_sha256"] = cert.TargetSha256,
            ["study_id"] = cert.StudyId,
            ["site_id"] = cert.SiteId,
            ["subject_id"] = cert.SubjectId,
            ["original_study_instance_uid_hash"] = cert.OriginalStudyInstanceUidHash,
            ["original_sop_instance_uid_hash"] = cert.OriginalSopInstanceUidHash,
            ["anonymized_study_instance_uid"] = cert.AnonymizedStudyInstanceUid,
            ["anonymized_sop_instance_uid"] = cert.AnonymizedSopInstanceUid,
            ["scrubbed_tags_count"] = cert.ScrubbedTagsCount,
            ["scrubbed_tags"] = cert.ScrubbedTags,
            ["pixel_mask_applied"] = cert.PixelMaskApplied,
            ["pixel_masks"] = cert.PixelMasks,
            ["operator_identity"] = cert.OperatorIdentity,
            ["timestamp"] = cert.Timestamp
        };
    }

    public bool VerifyCertificate(DicomDeidCertificate cert)
    {
        if (cert == null || string.IsNullOrWhiteSpace(cert.Signature))
        {
            return false;
        }

        return VerifyPayload(CreateCertPayloadWithoutSignature(cert), cert.Signature);
    }
}
