using System;

namespace Cadence.Apps.Imaging.Domain;

public class ImagingStudyEnvelope
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyInstanceUid { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string ModalitiesJson { get; set; } = "[]";
    public string? StudyDate { get; set; }
    public string? StudyDescription { get; set; }
    public int SeriesCount { get; set; }
    public int InstanceCount { get; set; }
    public string? StoragePrefix { get; set; }
    public string Status { get; set; } = "STORED";
    public string AdjudicationStatus { get; set; } = "UNASSIGNED";
    public string TenantId { get; set; } = "tenant_default";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public required string CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class ImagingSeriesEnvelope
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyEnvelopeId { get; set; } = string.Empty;
    public string SeriesInstanceUid { get; set; } = string.Empty;
    public int? SeriesNumber { get; set; }
    public string Modality { get; set; } = string.Empty;
    public string? BodyPartExamined { get; set; }
    public string? SeriesDescription { get; set; }
    public int InstanceCount { get; set; }
    public string? ProtocolName { get; set; }
    public string? FrameOfReferenceUid { get; set; }
}

public class ImagingInstanceEnvelope
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SeriesEnvelopeId { get; set; } = string.Empty;
    public string SopInstanceUid { get; set; } = string.Empty;
    public string SopClassUid { get; set; } = string.Empty;
    public int? InstanceNumber { get; set; }
    public int Rows { get; set; }
    public int Columns { get; set; }
    public int BitsAllocated { get; set; } = 16;
    public string? PixelSpacingJson { get; set; }
    public double? SliceThickness { get; set; }
    public double? SliceLocation { get; set; }
    public double? WindowCenter { get; set; }
    public double? WindowWidth { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool BurnedInPhiMasked { get; set; }
    public string? CertificateId { get; set; }
}

public class DeidentificationCertificate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? StudyEnvelopeId { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public string TargetSha256 { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string OriginalStudyInstanceUidHash { get; set; } = string.Empty;
    public string OriginalSopInstanceUidHash { get; set; } = string.Empty;
    public string AnonymizedStudyInstanceUid { get; set; } = string.Empty;
    public string AnonymizedSopInstanceUid { get; set; } = string.Empty;
    public int ScrubbedTagsCount { get; set; }
    public string ScrubbedTagsJson { get; set; } = "[]";
    public bool PixelMaskApplied { get; set; }
    public string PixelMasksJson { get; set; } = "[]";
    public string OperatorIdentity { get; set; } = "cadence.imaging.pipeline";
    public string Timestamp { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ImagingAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? StudyEnvelopeId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ImagingAnnotation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? StudyEnvelopeId { get; set; }
    public string? SeriesEnvelopeId { get; set; }
    public string? SopInstanceUid { get; set; }
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string TimepointName { get; set; } = string.Empty;
    public string LesionId { get; set; } = string.Empty;
    public string LesionType { get; set; } = "TARGET";
    public string OrganSite { get; set; } = string.Empty;
    public string MeasurementCriteria { get; set; } = "RECIST_1_1";
    public string MeasurementType { get; set; } = "1D_RECIST";
    public double LongestDiameterMm { get; set; }
    public double? PerpendicularDiameterMm { get; set; }
    public double? ProductPerpendicularMm2 { get; set; }
    public double? VolumeMm3 { get; set; }
    public int? SliceIndex { get; set; }
    public double? SliceLocation { get; set; }
    public int? DeauvilleScore { get; set; }
    public string PointsJson { get; set; } = "[]";
    public string? HounsfieldStatsJson { get; set; }
    public required string ReaderId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public required string CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class TumorResponseAssessment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string TimepointName { get; set; } = string.Empty;
    public string AssessmentCriteria { get; set; } = "RECIST_1_1";
    public double? BaselineSldMm { get; set; }
    public double? NadirSldMm { get; set; }
    public double? CurrentSldMm { get; set; }
    public double? PctChangeBaseline { get; set; }
    public double? PctChangeNadir { get; set; }
    public double? AbsoluteChangeNadirMm { get; set; }
    public double? BaselineSpdMm2 { get; set; }
    public double? NadirSpdMm2 { get; set; }
    public double? CurrentSpdMm2 { get; set; }
    public double? PctChangeSpdBaseline { get; set; }
    public double? PctChangeSpdNadir { get; set; }
    public string TargetResponse { get; set; } = "SD";
    public string NonTargetStatus { get; set; } = "NON_CR_NON_PD";
    public bool NewLesionsPresent { get; set; }
    public int NewLesionsCount { get; set; }
    public int? MaxDeauvilleScore { get; set; }
    public string OverallResponse { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }
    public required string ReaderId { get; set; }
    public string ManifestSha256 { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public required string CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class ImagingAdjudicationSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? StudyEnvelopeId { get; set; }
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string TimepointName { get; set; } = string.Empty;
    public string AssessmentCriteria { get; set; } = "RECIST_1_1";
    public string Reader1Id { get; set; } = string.Empty;
    public string Reader1Name { get; set; } = "Reader 1";
    public string Reader1Status { get; set; } = "PENDING";
    public string? Reader1AssessmentJson { get; set; }
    public string Reader2Id { get; set; } = string.Empty;
    public string Reader2Name { get; set; } = "Reader 2";
    public string Reader2Status { get; set; } = "PENDING";
    public string? Reader2AssessmentJson { get; set; }
    public string? AdjudicatorId { get; set; }
    public string? AdjudicatorName { get; set; } = "Adjudication Committee Chair";
    public string? AdjudicatorAssessmentJson { get; set; }
    public double SldDiscordanceDelta { get; set; }
    public bool IsResponseDiscordant { get; set; }
    public string DiscordanceReasonsJson { get; set; } = "[]";
    public string WorkflowStatus { get; set; } = "PENDING_PRIMARY_READS";
    public string? FinalConsensusResponse { get; set; }
    public double? FinalConsensusSldMm { get; set; }
    public string? BindingReader { get; set; }
    public string? AdjudicationRationale { get; set; }
    public string? ElectronicSignatureJson { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }
    public string SessionDigest { get; set; } = string.Empty;
    public string TenantId { get; set; } = "tenant_default";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public required string CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}
