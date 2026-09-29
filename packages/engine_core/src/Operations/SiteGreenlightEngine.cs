using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.Core.Domain.Operations;

/// <summary>
/// Site Startup (SSU) Multi-Criteria Regulatory Greenlight Engine.
/// Evaluates site readiness dossiers across regulatory approvals, legal contracts,
/// essential documents, investigator qualifications, facility calibrations, and
/// depot release criteria to produce 21 CFR Part 11 compliant greenlight decisions.
/// @req:PRD-SYS-053
/// </summary>
public static class SiteGreenlightEngine
{
    public static GreenlightDecision EvaluateReadiness(
        SiteStartupCriteria criteria,
        DateTime? asOfDate = null)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        DateTime nowTs = asOfDate ?? DateTime.UtcNow;
        var deficiencies = new List<DeficiencyItem>();

        // 1. Evaluate IRB
        bool irbCleared = CheckIrbApproval(criteria, nowTs, deficiencies);

        // 2. Evaluate Legal Contracts
        bool contractsCleared = CheckContracts(criteria, deficiencies);

        // 3. Evaluate Essential Documents
        bool docsCleared = CheckEssentialDocuments(criteria, deficiencies);

        // 4. Evaluate Staff Qualifications
        bool staffCleared = CheckStaffQualifications(criteria, deficiencies);

        // 5. Evaluate Facility Readiness
        bool facilityCleared = CheckFacilityReadiness(criteria, nowTs, deficiencies);

        // 6. Evaluate IP Depot Release
        bool ipDepotCleared = CheckIpDepotRelease(criteria, deficiencies);

        // Determine Final Status & Capability Flags
        int criticalCount = deficiencies.Count(d => d.Severity == DeficiencySeverity.CRITICAL_BLOCKER);
        int majorCount = deficiencies.Count(d => d.Severity == DeficiencySeverity.MAJOR_WARNING);

        GreenlightStatus status;
        bool canShipIp;
        bool canScreen;
        bool canEnroll;

        if (criticalCount > 0)
        {
            status = GreenlightStatus.BLOCKED;
            canShipIp = false;
            canScreen = false;
            canEnroll = false;
        }
        else if (majorCount > 0)
        {
            status = GreenlightStatus.CONDITIONAL_RELEASE;
            canShipIp = true;
            canScreen = irbCleared && docsCleared;
            canEnroll = false;
        }
        else
        {
            status = GreenlightStatus.GREENLIGHT_CLEARED;
            canShipIp = true;
            canScreen = true;
            canEnroll = true;
        }

        string assessmentId = Guid.NewGuid().ToString("D");
        string sigHash = GenerateDecisionSignature(
            assessmentId: assessmentId,
            studyId: criteria.StudyId,
            siteId: criteria.SiteId,
            status: status.ToString(),
            deficiencyCodes: deficiencies.Select(d => d.Code).ToList(),
            assessedAtIso: nowTs.ToString("o")
        );

        return new GreenlightDecision(
            AssessmentId: assessmentId,
            StudyId: criteria.StudyId,
            SiteId: criteria.SiteId,
            Status: status,
            CanShipIp: canShipIp,
            CanScreenSubjects: canScreen,
            CanEnrollSubjects: canEnroll,
            IrbCleared: irbCleared,
            ContractsCleared: contractsCleared,
            DocumentsCleared: docsCleared,
            StaffCleared: staffCleared,
            FacilityCleared: facilityCleared,
            IpDepotCleared: ipDepotCleared,
            Deficiencies: deficiencies,
            AssessedAt: nowTs,
            DigitalSignatureHash: sigHash
        );
    }

    private static bool CheckIrbApproval(
        SiteStartupCriteria criteria,
        DateTime nowTs,
        List<DeficiencyItem> deficiencies)
    {
        var irb = criteria.IrbApproval;
        if (irb == null)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "IRB_MISSING",
                Category: DeficiencyCategory.REGULATORY_IRB,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: "Institutional Review Board (IRB/EC) approval record is missing.",
                RemediationAction: "Submit protocol dossier to IRB/EC and upload unconditional approval notice.",
                RequiredRole: "Regulatory Affairs Specialist"
            ));
            return false;
        }

        bool cleared = true;
        if (irb.ExpirationDate < nowTs)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "IRB_EXPIRED",
                Category: DeficiencyCategory.REGULATORY_IRB,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: $"IRB/EC approval expired on {irb.ExpirationDate:yyyy-MM-dd}.",
                RemediationAction: "Submit continuing review / renewal application to IRB/EC.",
                DocumentReference: irb.ApprovalNumber
            ));
            cleared = false;
        }

        if (irb.ProtocolVersion != criteria.TargetProtocolVersion)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "IRB_VERSION_MISMATCH",
                Category: DeficiencyCategory.REGULATORY_IRB,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: $"IRB approved Protocol v{irb.ProtocolVersion}, but target version is v{criteria.TargetProtocolVersion}.",
                RemediationAction: $"Submit protocol amendment v{criteria.TargetProtocolVersion} to IRB for approval."
            ));
            cleared = false;
        }

        if (!irb.IsFullBoardApproval)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "IRB_CONDITIONAL",
                Category: DeficiencyCategory.REGULATORY_IRB,
                Severity: DeficiencySeverity.MAJOR_WARNING,
                Message: "IRB approval is conditional or expedited; pending full board ratification.",
                RemediationAction: "Obtain unconditional full board approval minute from IRB."
            ));
        }

        return cleared;
    }

    private static bool CheckContracts(
        SiteStartupCriteria criteria,
        List<DeficiencyItem> deficiencies)
    {
        var ctaContracts = criteria.Contracts.Where(c => c.ContractType == "CTA").ToList();
        if (ctaContracts.Count == 0)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "CTA_MISSING",
                Category: DeficiencyCategory.CONTRACT_CTA,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: "No Clinical Trial Agreement (CTA) contract record on file.",
                RemediationAction: "Execute tri-partite CTA agreement with Sponsor, Site, and Principal Investigator.",
                RequiredRole: "Contract Specialist"
            ));
            return false;
        }

        bool cleared = true;
        foreach (var cta in ctaContracts)
        {
            if (!cta.IsFullyExecuted)
            {
                deficiencies.Add(new DeficiencyItem(
                    Code: "CTA_UNEXECUTED",
                    Category: DeficiencyCategory.CONTRACT_CTA,
                    Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                    Message: $"Clinical Trial Agreement {cta.ContractId} is not fully executed.",
                    RemediationAction: "Obtain missing signatures from Sponsor, Site Legal, and PI."
                ));
                cleared = false;
            }

            if (!cta.PiSigned)
            {
                deficiencies.Add(new DeficiencyItem(
                    Code: "CTA_PI_SIGNATURE_MISSING",
                    Category: DeficiencyCategory.CONTRACT_CTA,
                    Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                    Message: "Principal Investigator signature missing on CTA agreement.",
                    RemediationAction: "Secure PI counter-signature on CTA."
                ));
                cleared = false;
            }
        }

        return cleared;
    }

    private static bool CheckEssentialDocuments(
        SiteStartupCriteria criteria,
        List<DeficiencyItem> deficiencies)
    {
        bool cleared = true;
        if (!criteria.Fda1572Signed)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "FDA_1572_MISSING",
                Category: DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: "Form FDA 1572 (Statement of Investigator) is not signed or approved.",
                RemediationAction: "Generate Form FDA 1572, obtain PI electronic signature, and file in eTMF.",
                RequiredRole: "Principal Investigator"
            ));
            cleared = false;
        }

        if (!criteria.SiteInitiationVisitCompleted)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "SIV_INCOMPLETE",
                Category: DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                Severity: DeficiencySeverity.MAJOR_WARNING,
                Message: "Site Initiation Visit (SIV) has not been completed or signed off by CRA.",
                RemediationAction: "Conduct SIV, document meeting minutes, and obtain CRA sign-off.",
                RequiredRole: "Clinical Research Associate"
            ));
        }

        return cleared;
    }

    private static bool CheckStaffQualifications(
        SiteStartupCriteria criteria,
        List<DeficiencyItem> deficiencies)
    {
        var staffList = criteria.StaffQualifications;
        var pis = staffList.Where(s => s.Role == StaffRole.PRINCIPAL_INVESTIGATOR).ToList();

        if (pis.Count == 0)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "PI_MISSING",
                Category: DeficiencyCategory.TRAINING_COMPLIANCE,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: "No Principal Investigator (PI) assigned to site staff roster.",
                RemediationAction: "Assign a qualified Principal Investigator to the site delegation log."
            ));
            return false;
        }

        bool cleared = true;
        foreach (var staff in staffList)
        {
            string prefix = $"{staff.Role} ({staff.StaffName})";

            if (!staff.IsCvCurrent)
            {
                deficiencies.Add(new DeficiencyItem(
                    Code: "STAFF_CV_EXPIRED",
                    Category: DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                    Severity: DeficiencySeverity.MAJOR_WARNING,
                    Message: $"{prefix} curriculum vitae (CV) is expired (> 2 years old).",
                    RemediationAction: $"Request updated signed CV from {staff.StaffName}."
                ));
            }

            if ((staff.Role == StaffRole.PRINCIPAL_INVESTIGATOR || staff.Role == StaffRole.SUB_INVESTIGATOR) &&
                !staff.IsMedicalLicenseActive)
            {
                deficiencies.Add(new DeficiencyItem(
                    Code: "STAFF_LICENSE_INACTIVE",
                    Category: DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                    Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                    Message: $"{prefix} state medical license is inactive or expired.",
                    RemediationAction: "Upload active medical license verification."
                ));
                cleared = false;
            }

            if (!staff.IsGcpCertified)
            {
                deficiencies.Add(new DeficiencyItem(
                    Code: "STAFF_GCP_EXPIRED",
                    Category: DeficiencyCategory.TRAINING_COMPLIANCE,
                    Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                    Message: $"{prefix} Good Clinical Practice (GCP) certification is missing or expired.",
                    RemediationAction: $"Complete ICH GCP E6(R2) training module for {staff.StaffName}."
                ));
                cleared = false;
            }

            if ((staff.Role == StaffRole.PRINCIPAL_INVESTIGATOR || staff.Role == StaffRole.SUB_INVESTIGATOR) &&
                !staff.FinancialDisclosureSigned)
            {
                deficiencies.Add(new DeficiencyItem(
                    Code: "STAFF_FDF_MISSING",
                    Category: DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                    Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                    Message: $"{prefix} Financial Disclosure Form (FDF) is not signed.",
                    RemediationAction: "Sign 21 CFR 54 Financial Disclosure Form."
                ));
                cleared = false;
            }

            if (!staff.ProtocolTrainingCompleted || staff.TrainedProtocolVersion != criteria.TargetProtocolVersion)
            {
                deficiencies.Add(new DeficiencyItem(
                    Code: "STAFF_TRAINING_MISMATCH",
                    Category: DeficiencyCategory.TRAINING_COMPLIANCE,
                    Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                    Message: $"{prefix} trained on Protocol v{staff.TrainedProtocolVersion}, target is v{criteria.TargetProtocolVersion}.",
                    RemediationAction: $"Complete training on Protocol v{criteria.TargetProtocolVersion}."
                ));
                cleared = false;
            }

            if (!staff.DoaSigned)
            {
                deficiencies.Add(new DeficiencyItem(
                    Code: "STAFF_DOA_MISSING",
                    Category: DeficiencyCategory.DELEGATION_OF_AUTHORITY,
                    Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                    Message: $"{prefix} is not signed onto the Delegation of Authority (DOA) log.",
                    RemediationAction: $"Add {staff.StaffName} to DOA log and obtain PI sign-off."
                ));
                cleared = false;
            }
        }

        return cleared;
    }

    private static bool CheckFacilityReadiness(
        SiteStartupCriteria criteria,
        DateTime nowTs,
        List<DeficiencyItem> deficiencies)
    {
        var fac = criteria.Facility;
        if (fac == null)
        {
            return true;
        }

        bool cleared = true;
        if (!fac.CliaCapCertified)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "CLIA_CAP_MISSING",
                Category: DeficiencyCategory.FACILITY_CERTIFICATION,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: "Site laboratory is missing CLIA/CAP accreditation.",
                RemediationAction: "Upload valid CLIA or CAP laboratory accreditation certificate."
            ));
            cleared = false;
        }

        if (fac.CliaExpiration.HasValue && fac.CliaExpiration.Value < nowTs)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "CLIA_EXPIRED",
                Category: DeficiencyCategory.FACILITY_CERTIFICATION,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: $"CLIA laboratory accreditation expired on {fac.CliaExpiration.Value:yyyy-MM-dd}.",
                RemediationAction: "Upload renewed CLIA certificate."
            ));
            cleared = false;
        }

        if (!fac.NormalLabRangesCurrent)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "LAB_RANGES_OUTDATED",
                Category: DeficiencyCategory.FACILITY_CERTIFICATION,
                Severity: DeficiencySeverity.MAJOR_WARNING,
                Message: "Local laboratory normal reference ranges have not been certified for current protocol.",
                RemediationAction: "Verify and submit local lab reference ranges in MDR."
            ));
        }

        if (!fac.IpStorageTemperatureCalibrated)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "IP_TEMP_UNCALIBRATED",
                Category: DeficiencyCategory.FACILITY_CERTIFICATION,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: "Investigational product storage refrigeration temperature log is not calibrated.",
                RemediationAction: "Calibrate temperature monitoring equipment and upload certificate."
            ));
            cleared = false;
        }

        if (!fac.BackupPowerGeneratorVerified)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "BACKUP_POWER_UNVERIFIED",
                Category: DeficiencyCategory.FACILITY_CERTIFICATION,
                Severity: DeficiencySeverity.MAJOR_WARNING,
                Message: "Emergency backup power generator status is not verified for IP storage facility.",
                RemediationAction: "Document backup generator inspection and annual test."
            ));
        }

        return cleared;
    }

    private static bool CheckIpDepotRelease(
        SiteStartupCriteria criteria,
        List<DeficiencyItem> deficiencies)
    {
        var ipDepot = criteria.IpDepotRelease;
        if (ipDepot == null)
        {
            return true;
        }

        bool cleared = true;
        if (!ipDepot.QpReleaseAuthorized)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "QP_RELEASE_PENDING",
                Category: DeficiencyCategory.INVESTIGATIONAL_PRODUCT_RELEASE,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: "Qualified Person (QP) release authorization is pending for clinical batch.",
                RemediationAction: "Obtain QP Batch Certificate of Analysis and release sign-off."
            ));
            cleared = false;
        }

        if (!ipDepot.ImportLicenseApproved)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "IMPORT_LICENSE_MISSING",
                Category: DeficiencyCategory.INVESTIGATIONAL_PRODUCT_RELEASE,
                Severity: DeficiencySeverity.CRITICAL_BLOCKER,
                Message: "Customs / health authority import license is missing for clinical supplies.",
                RemediationAction: "Obtain import permit from destination country health authority."
            ));
            cleared = false;
        }

        if (!ipDepot.DepotStockAvailable)
        {
            deficiencies.Add(new DeficiencyItem(
                Code: "DEPOT_STOCKOUT",
                Category: DeficiencyCategory.INVESTIGATIONAL_PRODUCT_RELEASE,
                Severity: DeficiencySeverity.MAJOR_WARNING,
                Message: "Central depot IP stock is currently depleted.",
                RemediationAction: "Manufacture / resupply IP inventory to regional distribution depot."
            ));
        }

        return cleared;
    }

    private static string GenerateDecisionSignature(
        string assessmentId,
        string studyId,
        string siteId,
        string status,
        List<string> deficiencyCodes,
        string assessedAtIso)
    {
        var canonicalObj = new SortedDictionary<string, object>
        {
            { "assessed_at", assessedAtIso },
            { "assessment_id", assessmentId },
            { "deficiencies", deficiencyCodes.OrderBy(c => c).ToList() },
            { "site_id", siteId },
            { "status", status },
            { "study_id", studyId }
        };

        string json = JsonSerializer.Serialize(canonicalObj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
