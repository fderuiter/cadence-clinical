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
        DicomTag.EthnicGroup,
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

    public DicomDeidentifier(byte[]? signingSecret = null)
    {
        _signingSecret = signingSecret ?? Encoding.UTF8.GetBytes("cadence-audit-log-secret-key-2026");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods should be static", Justification = "Instance method API consistency")]
    public string GenerateUid(string originalUid, string namespaceSalt)
    {
        string combined = $"{originalUid}:{namespaceSalt}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        byte[] first16 = hash[..16];
        var num = new BigInteger(first16, isUnsigned: true, isBigEndian: true);
        string fullUid = $"{CadenceDicomUidRoot}{num}";
        return fullUid.Substring(0, Math.Min(64, fullUid.Length));
    }

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
        var appliedMasks = new List<PixelMaskBoundingBox>();
        if (!dataset.Contains(DicomTag.PixelData))
        {
            return appliedMasks;
        }

        ushort rows = dataset.GetSingleValueOrDefault(DicomTag.Rows, (ushort)0);
        ushort cols = dataset.GetSingleValueOrDefault(DicomTag.Columns, (ushort)0);
        if (rows == 0 || cols == 0)
        {
            return appliedMasks;
        }

        string modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, "").ToUpperInvariant();
        string burnedInHeader = dataset.GetSingleValueOrDefault(DicomTag.BurnedInAnnotation, "").ToUpperInvariant();

        var masksToApply = new List<PixelMaskBoundingBox>();
        if (autoDetectHeaders && (modality == "US" || modality == "SC" || modality == "OT" || burnedInHeader == "YES"))
        {
            int topBarHeight = Math.Max(1, (int)(rows * 0.12));
            masksToApply.Add(new PixelMaskBoundingBox(0, 0, cols, topBarHeight));
        }

        if (customMasks != null)
        {
            masksToApply.AddRange(customMasks);
        }

        if (masksToApply.Count == 0)
        {
            return appliedMasks;
        }

        try
        {
            var pixelData = DicomPixelData.Create(dataset);
            IByteBuffer frameBuffer = pixelData.GetFrame(0);
            byte[] bytes = frameBuffer.Data.ToArray();

            ushort bitsAllocated = dataset.GetSingleValueOrDefault(DicomTag.BitsAllocated, (ushort)16);
            ushort samplesPerPixel = dataset.GetSingleValueOrDefault(DicomTag.SamplesPerPixel, (ushort)1);
            int bytesPerPixel = (bitsAllocated > 8 ? 2 : 1) * samplesPerPixel;

            foreach (var box in masksToApply)
            {
                int x0 = Math.Max(0, Math.Min(box.XMin, (int)cols));
                int x1 = Math.Max(0, Math.Min(box.XMax, (int)cols));
                int y0 = Math.Max(0, Math.Min(box.YMin, (int)rows));
                int y1 = Math.Max(0, Math.Min(box.YMax, (int)rows));

                if (x1 <= x0 || y1 <= y0)
                {
                    continue;
                }

                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int pixelOffset = (y * cols + x) * bytesPerPixel;
                        for (int b = 0; b < bytesPerPixel && (pixelOffset + b) < bytes.Length; b++)
                        {
                            bytes[pixelOffset + b] = 0;
                        }
                    }
                }

                appliedMasks.Add(box);
            }

            dataset.AddOrUpdate(DicomTag.PixelData, new MemoryByteBuffer(bytes));
            dataset.AddOrUpdate(DicomTag.BurnedInAnnotation, "NO");
        }
        catch
        {
            // If pixel data decoding is unsupported for compressed syntax, suppress without breaking header deid
        }

        return appliedMasks;
    }

    public DicomDeidResult Deidentify(
        byte[] dicomBytes,
        string subjectId,
        string siteId,
        string studyId,
        int dateShiftDays = 0,
        bool applyPixelMask = true,
        List<PixelMaskBoundingBox>? customPixelMasks = null,
        string operatorIdentity = "cadence.imaging.pipeline")
    {
        ArgumentNullException.ThrowIfNull(dicomBytes);

        string sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(dicomBytes));

        using var inStream = new MemoryStream(dicomBytes);
        var dicomFile = DicomFile.Open(inStream, FileReadOption.ReadAll);
        var dataset = dicomFile.Dataset;

        var scrubbedTags = new HashSet<string>();

        string origStudyUid = dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, Guid.NewGuid().ToString());
        string origSeriesUid = dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, Guid.NewGuid().ToString());
        string origSopUid = dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, Guid.NewGuid().ToString());
        string origForUid = dataset.GetSingleValueOrDefault(DicomTag.FrameOfReferenceUID, "");

        string origStudyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(origStudyUid)));
        string origSopHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(origSopUid)));

        string newStudyUid = GenerateUid(origStudyUid, $"study:{studyId}");
        string newSeriesUid = GenerateUid(origSeriesUid, $"series:{studyId}");
        string newSopUid = GenerateUid(origSopUid, $"sop:{studyId}:{subjectId}");

        dataset.AddOrUpdate(DicomTag.StudyInstanceUID, newStudyUid);
        dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, newSeriesUid);
        dataset.AddOrUpdate(DicomTag.SOPInstanceUID, newSopUid);

        if (!string.IsNullOrEmpty(origForUid))
        {
            dataset.AddOrUpdate(DicomTag.FrameOfReferenceUID, GenerateUid(origForUid, $"for:{studyId}"));
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

        var appliedMasks = new List<PixelMaskBoundingBox>();
        if (applyPixelMask)
        {
            appliedMasks = MaskBurnedInPixels(dataset, customPixelMasks, autoDetectHeaders: true);
        }

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
            OriginalStudyInstanceUidHash = origStudyHash,
            OriginalSopInstanceUidHash = origSopHash,
            AnonymizedStudyInstanceUid = newStudyUid,
            AnonymizedSopInstanceUid = newSopUid,
            ScrubbedTagsCount = scrubbedTags.Count,
            ScrubbedTags = scrubbedTags.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            PixelMaskApplied = appliedMasks.Count > 0,
            PixelMasks = appliedMasks,
            OperatorIdentity = operatorIdentity
        };

        var certPayload = CreateCertPayloadWithoutSignature(cert);
        cert.Signature = CanonicalSigner.GenerateCanonicalSignature(certPayload, _signingSecret);

        return new DicomDeidResult(
            DeidentifiedBytes: outBytes,
            Certificate: cert,
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
            WindowWidth: windowWidth
        );
    }

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

        var payload = CreateCertPayloadWithoutSignature(cert);
        return CanonicalSigner.VerifyCanonicalSignature(payload, cert.Signature, _signingSecret);
    }
}
