using System;
using System.Collections.Generic;

namespace Cadence.Core.Domain.Operations;

/// <summary>
/// Multi-stage greenlight decision status.
/// @req:PRD-SYS-053
/// </summary>
public enum GreenlightStatus
{
    BLOCKED,
    CONDITIONAL_RELEASE,
    GREENLIGHT_CLEARED
}

/// <summary>
/// Severity classification of greenlight deficiencies.
/// @req:PRD-SYS-053
/// </summary>
public enum DeficiencySeverity
{
    CRITICAL_BLOCKER,
    MAJOR_WARNING,
    INFORMATIONAL
}

/// <summary>
/// Categorical classification of regulatory gating requirements.
/// @req:PRD-SYS-053
/// </summary>
public enum DeficiencyCategory
{
    REGULATORY_IRB,
    CONTRACT_CTA,
    ESSENTIAL_DOCUMENTS,
    TRAINING_COMPLIANCE,
    DELEGATION_OF_AUTHORITY,
    FACILITY_CERTIFICATION,
    INVESTIGATIONAL_PRODUCT_RELEASE
}

/// <summary>
/// Clinical site staff role taxonomy.
/// @req:PRD-SYS-053
/// </summary>
public enum StaffRole
{
    PRINCIPAL_INVESTIGATOR,
    SUB_INVESTIGATOR,
    CLINICAL_RESEARCH_COORDINATOR,
    STUDY_NURSE,
    PHARMACIST,
    LAB_TECHNICIAN
}

/// <summary>
/// Granular actionable diagnostic for unmet startup criteria.
/// @req:PRD-SYS-053
/// </summary>
public record DeficiencyItem(
    string Code,
    DeficiencyCategory Category,
    DeficiencySeverity Severity,
    string Message,
    string RemediationAction,
    string? RequiredRole = null,
    string? DocumentReference = null
);

/// <summary>
/// Institutional Review Board / Independent Ethics Committee record.
/// @req:PRD-SYS-053
/// </summary>
public record IrbApprovalRecord(
    string IrbName,
    string ApprovalNumber,
    string ProtocolVersion,
    DateTime ApprovalDate,
    DateTime ExpirationDate,
    bool IsFullBoardApproval = true,
    bool SafetyReportingAcknowledged = true
);

/// <summary>
/// Clinical trial legal agreement or financial contract.
/// @req:PRD-SYS-053
/// </summary>
public record ContractRecord(
    string ContractId,
    bool IsFullyExecuted,
    string ContractType = "CTA",
    DateTime? ExecutedDate = null,
    bool SponsorSigned = true,
    bool InstitutionSigned = true,
    bool PiSigned = true
);

/// <summary>
/// Individual investigator or site staff qualification &amp; training checklist.
/// @req:PRD-SYS-053
/// </summary>
public record StaffQualificationRecord(
    string StaffId,
    string StaffName,
    StaffRole Role,
    bool IsCvCurrent = true,
    bool IsMedicalLicenseActive = true,
    bool IsGcpCertified = true,
    bool FinancialDisclosureSigned = true,
    bool ProtocolTrainingCompleted = true,
    string TrainedProtocolVersion = "1.0",
    bool EdcTrainingCompleted = true,
    bool DoaSigned = true,
    DateTime? DoaStartDate = null
);

/// <summary>
/// Site facility, lab accreditation, and cold chain readiness record.
/// @req:PRD-SYS-053
/// </summary>
public record SiteFacilityRecord(
    string SiteId,
    bool CliaCapCertified = true,
    DateTime? CliaExpiration = null,
    bool NormalLabRangesCurrent = true,
    bool IpStorageTemperatureCalibrated = true,
    bool BackupPowerGeneratorVerified = true
);

/// <summary>
/// Investigational Product depot distribution and QP authorization record.
/// @req:PRD-SYS-053
/// </summary>
public record IPDepotReleaseRecord(
    string StudyId,
    string SiteId,
    bool QpReleaseAuthorized = true,
    bool ImportLicenseApproved = true,
    bool DepotStockAvailable = true
);

/// <summary>
/// Comprehensive site startup evaluation dossier.
/// @req:PRD-SYS-053
/// </summary>
public record SiteStartupCriteria(
    string StudyId,
    string SiteId,
    string TargetProtocolVersion = "1.0",
    IrbApprovalRecord? IrbApproval = null,
    IReadOnlyList<ContractRecord>? Contracts = null,
    IReadOnlyList<StaffQualificationRecord>? StaffQualifications = null,
    SiteFacilityRecord? Facility = null,
    IPDepotReleaseRecord? IpDepotRelease = null,
    bool Fda1572Signed = false,
    bool SiteInitiationVisitCompleted = false,
    IReadOnlyDictionary<string, object?>? Metadata = null
)
{
    public IReadOnlyList<ContractRecord> Contracts { get; init; } = Contracts ?? Array.Empty<ContractRecord>();
    public IReadOnlyList<StaffQualificationRecord> StaffQualifications { get; init; } = StaffQualifications ?? Array.Empty<StaffQualificationRecord>();
}

/// <summary>
/// Multi-criteria regulatory clearance decision with 21 CFR Part 11 digital seal.
/// @req:PRD-SYS-053
/// </summary>
public record GreenlightDecision(
    string AssessmentId,
    string StudyId,
    string SiteId,
    GreenlightStatus Status,
    bool CanShipIp,
    bool CanScreenSubjects,
    bool CanEnrollSubjects,
    bool IrbCleared,
    bool ContractsCleared,
    bool DocumentsCleared,
    bool StaffCleared,
    bool FacilityCleared,
    bool IpDepotCleared,
    IReadOnlyList<DeficiencyItem> Deficiencies,
    DateTime AssessedAt,
    string DigitalSignatureHash
);
