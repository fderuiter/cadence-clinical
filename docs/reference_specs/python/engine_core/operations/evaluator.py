"""Site Startup (SSU) Multi-Criteria Regulatory Greenlight Engine.

Evaluates site readiness dossiers across regulatory approvals, legal contracts,
essential documents, investigator qualifications, facility calibrations, and
depot release criteria to produce 21 CFR Part 11 compliant greenlight decisions.
"""

from __future__ import annotations

import hashlib
import json
import uuid
from datetime import UTC, datetime

from packages.engine_core.operations.models import (
    DeficiencyCategory,
    DeficiencyItem,
    DeficiencySeverity,
    GreenlightDecision,
    GreenlightStatus,
    SiteStartupCriteria,
    StaffRole,
)


class SiteGreenlightEngine:
    """Deterministic in-memory regulatory clearance and site startup greenlight evaluator."""

    @classmethod
    def evaluate_readiness(
        cls,
        criteria: SiteStartupCriteria,
        as_of_date: datetime | None = None,
    ) -> GreenlightDecision:
        """Evaluate site startup criteria and produce a comprehensive greenlight assessment.

        Args:
            criteria: The site startup dossier containing regulatory, staff, and facility data.
            as_of_date: Evaluation timestamp (defaults to UTC now).

        Returns:
            GreenlightDecision: Final clearance status, capability flags, and diagnosed deficiencies.
        """
        now_ts = as_of_date or datetime.now(UTC)
        deficiencies: list[DeficiencyItem] = []

        # 1. Evaluate Institutional Review Board / Ethics Committee
        irb_cleared = cls._check_irb_approval(criteria, now_ts, deficiencies)

        # 2. Evaluate Legal & Financial Agreements (CTA)
        contracts_cleared = cls._check_contracts(criteria, deficiencies)

        # 3. Evaluate Essential Regulatory Documents (1572, SIV)
        docs_cleared = cls._check_essential_documents(criteria, deficiencies)

        # 4. Evaluate Staff Qualifications, Training, and DOA
        staff_cleared = cls._check_staff_qualifications(criteria, deficiencies)

        # 5. Evaluate Facility, CLIA/CAP, and Cold Chain Calibration
        facility_cleared = cls._check_facility_readiness(criteria, now_ts, deficiencies)

        # 6. Evaluate IP Depot Release & QP Authorization
        ip_depot_cleared = cls._check_ip_depot_release(criteria, deficiencies)

        # Determine Final Status & Capabilities
        critical_count = sum(
            1 for d in deficiencies if d.severity == DeficiencySeverity.CRITICAL_BLOCKER
        )
        major_count = sum(
            1 for d in deficiencies if d.severity == DeficiencySeverity.MAJOR_WARNING
        )

        if critical_count > 0:
            status = GreenlightStatus.BLOCKED
            can_ship_ip = False
            can_screen = False
            can_enroll = False
        elif major_count > 0:
            status = GreenlightStatus.CONDITIONAL_RELEASE
            # Conditional release permits pre-shipping IP for staging, but blocks enrollment
            can_ship_ip = True
            can_screen = irb_cleared and docs_cleared
            can_enroll = False
        else:
            status = GreenlightStatus.GREENLIGHT_CLEARED
            can_ship_ip = True
            can_screen = True
            can_enroll = True

        assessment_id = str(uuid.uuid4())
        sig_hash = cls._generate_decision_signature(
            assessment_id=assessment_id,
            study_id=criteria.study_id,
            site_id=criteria.site_id,
            status=status.value,
            deficiency_codes=[d.code for d in deficiencies],
            assessed_at_iso=now_ts.isoformat(),
        )

        return GreenlightDecision(
            assessment_id=assessment_id,
            study_id=criteria.study_id,
            site_id=criteria.site_id,
            status=status,
            can_ship_ip=can_ship_ip,
            can_screen_subjects=can_screen,
            can_enroll_subjects=can_enroll,
            irb_cleared=irb_cleared,
            contracts_cleared=contracts_cleared,
            documents_cleared=docs_cleared,
            staff_cleared=staff_cleared,
            facility_cleared=facility_cleared,
            ip_depot_cleared=ip_depot_cleared,
            deficiencies=deficiencies,
            assessed_at=now_ts,
            digital_signature_hash=sig_hash,
        )

    @classmethod
    def _check_irb_approval(
        cls,
        criteria: SiteStartupCriteria,
        now_ts: datetime,
        deficiencies: list[DeficiencyItem],
    ) -> bool:
        """Validate IRB/EC approval active status and protocol version parity."""
        irb = criteria.irb_approval
        if not irb:
            deficiencies.append(
                DeficiencyItem(
                    code="IRB_MISSING",
                    category=DeficiencyCategory.REGULATORY_IRB,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message="Institutional Review Board (IRB/EC) approval record is missing.",
                    remediation_action="Submit protocol dossier to IRB/EC and upload unconditional approval notice.",
                    required_role="Regulatory Affairs Specialist",
                )
            )
            return False

        cleared = True
        # Normalize timezone awareness for comparison
        exp_date = (
            irb.expiration_date.replace(tzinfo=UTC)
            if irb.expiration_date.tzinfo is None
            else irb.expiration_date
        )
        now_cmp = now_ts.replace(tzinfo=UTC) if now_ts.tzinfo is None else now_ts

        if exp_date < now_cmp:
            deficiencies.append(
                DeficiencyItem(
                    code="IRB_EXPIRED",
                    category=DeficiencyCategory.REGULATORY_IRB,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message=f"IRB/EC approval expired on {irb.expiration_date.strftime('%Y-%m-%d')}.",
                    remediation_action="Submit continuing review / renewal application to IRB/EC.",
                    document_reference=irb.approval_number,
                )
            )
            cleared = False

        if irb.protocol_version != criteria.target_protocol_version:
            deficiencies.append(
                DeficiencyItem(
                    code="IRB_VERSION_MISMATCH",
                    category=DeficiencyCategory.REGULATORY_IRB,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message=(
                        f"IRB approved Protocol v{irb.protocol_version}, but target version is "
                        f"v{criteria.target_protocol_version}."
                    ),
                    remediation_action=f"Submit protocol amendment v{criteria.target_protocol_version} to IRB for approval.",
                )
            )
            cleared = False

        if not irb.is_full_board_approval:
            deficiencies.append(
                DeficiencyItem(
                    code="IRB_CONDITIONAL",
                    category=DeficiencyCategory.REGULATORY_IRB,
                    severity=DeficiencySeverity.MAJOR_WARNING,
                    message="IRB approval is conditional or expedited; pending full board ratification.",
                    remediation_action="Obtain unconditional full board approval minute from IRB.",
                )
            )

        return cleared

    @classmethod
    def _check_contracts(
        cls,
        criteria: SiteStartupCriteria,
        deficiencies: list[DeficiencyItem],
    ) -> bool:
        """Validate Clinical Trial Agreement (CTA) execution and signatures."""
        cta_contracts = [c for c in criteria.contracts if c.contract_type == "CTA"]
        if not cta_contracts:
            deficiencies.append(
                DeficiencyItem(
                    code="CTA_MISSING",
                    category=DeficiencyCategory.CONTRACT_CTA,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message="No Clinical Trial Agreement (CTA) contract record on file.",
                    remediation_action="Execute tri-partite CTA agreement with Sponsor, Site, and Principal Investigator.",
                    required_role="Contract Specialist",
                )
            )
            return False

        cleared = True
        for cta in cta_contracts:
            if not cta.is_fully_executed:
                deficiencies.append(
                    DeficiencyItem(
                        code="CTA_UNEXECUTED",
                        category=DeficiencyCategory.CONTRACT_CTA,
                        severity=DeficiencySeverity.CRITICAL_BLOCKER,
                        message=f"Clinical Trial Agreement {cta.contract_id} is not fully executed.",
                        remediation_action="Obtain missing signatures from Sponsor, Site Legal, and PI.",
                    )
                )
                cleared = False

            if not cta.pi_signed:
                deficiencies.append(
                    DeficiencyItem(
                        code="CTA_PI_SIGNATURE_MISSING",
                        category=DeficiencyCategory.CONTRACT_CTA,
                        severity=DeficiencySeverity.CRITICAL_BLOCKER,
                        message="Principal Investigator signature missing on CTA agreement.",
                        remediation_action="Secure PI counter-signature on CTA.",
                    )
                )
                cleared = False

        return cleared

    @classmethod
    def _check_essential_documents(
        cls,
        criteria: SiteStartupCriteria,
        deficiencies: list[DeficiencyItem],
    ) -> bool:
        """Validate 21 CFR 312.60 Form FDA 1572 and Site Initiation Visit completion."""
        cleared = True
        if not criteria.fda_1572_signed:
            deficiencies.append(
                DeficiencyItem(
                    code="FDA_1572_MISSING",
                    category=DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message="Form FDA 1572 (Statement of Investigator) is not signed or approved.",
                    remediation_action="Generate Form FDA 1572, obtain PI electronic signature, and file in eTMF.",
                    required_role="Principal Investigator",
                )
            )
            cleared = False

        if not criteria.site_initiation_visit_completed:
            deficiencies.append(
                DeficiencyItem(
                    code="SIV_INCOMPLETE",
                    category=DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                    severity=DeficiencySeverity.MAJOR_WARNING,
                    message="Site Initiation Visit (SIV) has not been completed or signed off by CRA.",
                    remediation_action="Conduct SIV, document meeting minutes, and obtain CRA sign-off.",
                    required_role="Clinical Research Associate",
                )
            )
            # SIV is a major warning (allows IP shipping, but blocks enrollment)

        return cleared

    @classmethod
    def _check_staff_qualifications(
        cls,
        criteria: SiteStartupCriteria,
        deficiencies: list[DeficiencyItem],
    ) -> bool:
        """Validate site staff credentials, GCP certification, protocol training, and DOA log."""
        staff_list = criteria.staff_qualifications
        pis = [s for s in staff_list if s.role == StaffRole.PRINCIPAL_INVESTIGATOR]

        if not pis:
            deficiencies.append(
                DeficiencyItem(
                    code="PI_MISSING",
                    category=DeficiencyCategory.TRAINING_COMPLIANCE,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message="No Principal Investigator (PI) assigned to site staff roster.",
                    remediation_action="Assign a qualified Principal Investigator to the site delegation log.",
                )
            )
            return False

        cleared = True
        for staff in staff_list:
            prefix = f"{staff.role.value} ({staff.staff_name})"

            # 1. Current CV
            if not staff.is_cv_current:
                deficiencies.append(
                    DeficiencyItem(
                        code="STAFF_CV_EXPIRED",
                        category=DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                        severity=DeficiencySeverity.MAJOR_WARNING,
                        message=f"{prefix} curriculum vitae (CV) is expired (> 2 years old).",
                        remediation_action=f"Request updated signed CV from {staff.staff_name}.",
                    )
                )

            # 2. Medical License (for PI and Sub-I)
            if (
                staff.role
                in (
                    StaffRole.PRINCIPAL_INVESTIGATOR,
                    StaffRole.SUB_INVESTIGATOR,
                )
                and not staff.is_medical_license_active
            ):
                deficiencies.append(
                    DeficiencyItem(
                        code="STAFF_LICENSE_INACTIVE",
                        category=DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                        severity=DeficiencySeverity.CRITICAL_BLOCKER,
                        message=f"{prefix} state medical license is inactive or expired.",
                        remediation_action="Upload active medical license verification.",
                    )
                )
                cleared = False

            # 3. GCP Certification
            if not staff.is_gcp_certified:
                deficiencies.append(
                    DeficiencyItem(
                        code="STAFF_GCP_EXPIRED",
                        category=DeficiencyCategory.TRAINING_COMPLIANCE,
                        severity=DeficiencySeverity.CRITICAL_BLOCKER,
                        message=f"{prefix} Good Clinical Practice (GCP) certification is missing or expired.",
                        remediation_action=f"Complete ICH GCP E6(R2) training module for {staff.staff_name}.",
                    )
                )
                cleared = False

            # 4. Financial Disclosure (FDF)
            if (
                staff.role
                in (
                    StaffRole.PRINCIPAL_INVESTIGATOR,
                    StaffRole.SUB_INVESTIGATOR,
                )
                and not staff.financial_disclosure_signed
            ):
                deficiencies.append(
                    DeficiencyItem(
                        code="STAFF_FDF_MISSING",
                        category=DeficiencyCategory.ESSENTIAL_DOCUMENTS,
                        severity=DeficiencySeverity.CRITICAL_BLOCKER,
                        message=f"{prefix} Financial Disclosure Form (FDF) is not signed.",
                        remediation_action="Sign 21 CFR 54 Financial Disclosure Form.",
                    )
                )
                cleared = False

            # 5. Protocol Training Parity
            if (
                not staff.protocol_training_completed
                or staff.trained_protocol_version != criteria.target_protocol_version
            ):
                deficiencies.append(
                    DeficiencyItem(
                        code="STAFF_TRAINING_MISMATCH",
                        category=DeficiencyCategory.TRAINING_COMPLIANCE,
                        severity=DeficiencySeverity.CRITICAL_BLOCKER,
                        message=(
                            f"{prefix} trained on Protocol v{staff.trained_protocol_version}, "
                            f"target is v{criteria.target_protocol_version}."
                        ),
                        remediation_action=f"Complete training on Protocol v{criteria.target_protocol_version}.",
                    )
                )
                cleared = False

            # 6. Delegation of Authority (DOA)
            if not staff.doa_signed:
                deficiencies.append(
                    DeficiencyItem(
                        code="STAFF_DOA_MISSING",
                        category=DeficiencyCategory.DELEGATION_OF_AUTHORITY,
                        severity=DeficiencySeverity.CRITICAL_BLOCKER,
                        message=f"{prefix} is not signed onto the Delegation of Authority (DOA) log.",
                        remediation_action=f"Add {staff.staff_name} to DOA log and obtain PI sign-off.",
                    )
                )
                cleared = False

        return cleared

    @classmethod
    def _check_facility_readiness(
        cls,
        criteria: SiteStartupCriteria,
        now_ts: datetime,
        deficiencies: list[DeficiencyItem],
    ) -> bool:
        """Validate site lab certifications, reference ranges, and cold-chain equipment."""
        fac = criteria.facility
        if not fac:
            return True  # Facility optional if not tracked separately

        cleared = True
        if not fac.clia_cap_certified:
            deficiencies.append(
                DeficiencyItem(
                    code="CLIA_CAP_MISSING",
                    category=DeficiencyCategory.FACILITY_CERTIFICATION,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message="Site laboratory is missing CLIA/CAP accreditation.",
                    remediation_action="Upload valid CLIA or CAP laboratory accreditation certificate.",
                )
            )
            cleared = False

        if fac.clia_expiration:
            clia_exp = (
                fac.clia_expiration.replace(tzinfo=UTC)
                if fac.clia_expiration.tzinfo is None
                else fac.clia_expiration
            )
            now_cmp = now_ts.replace(tzinfo=UTC) if now_ts.tzinfo is None else now_ts
            if clia_exp < now_cmp:
                deficiencies.append(
                    DeficiencyItem(
                        code="CLIA_EXPIRED",
                        category=DeficiencyCategory.FACILITY_CERTIFICATION,
                        severity=DeficiencySeverity.CRITICAL_BLOCKER,
                        message=f"CLIA laboratory accreditation expired on {fac.clia_expiration.strftime('%Y-%m-%d')}.",
                        remediation_action="Upload renewed CLIA certificate.",
                    )
                )
                cleared = False

        if not fac.normal_lab_ranges_current:
            deficiencies.append(
                DeficiencyItem(
                    code="LAB_RANGES_OUTDATED",
                    category=DeficiencyCategory.FACILITY_CERTIFICATION,
                    severity=DeficiencySeverity.MAJOR_WARNING,
                    message="Local laboratory normal reference ranges have not been certified for current protocol.",
                    remediation_action="Verify and submit local lab reference ranges in MDR.",
                )
            )

        if not fac.ip_storage_temperature_calibrated:
            deficiencies.append(
                DeficiencyItem(
                    code="IP_TEMP_UNCALIBRATED",
                    category=DeficiencyCategory.FACILITY_CERTIFICATION,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message="Investigational product storage refrigeration temperature log is not calibrated.",
                    remediation_action="Calibrate temperature monitoring equipment and upload certificate.",
                )
            )
            cleared = False

        if not fac.backup_power_generator_verified:
            deficiencies.append(
                DeficiencyItem(
                    code="BACKUP_POWER_UNVERIFIED",
                    category=DeficiencyCategory.FACILITY_CERTIFICATION,
                    severity=DeficiencySeverity.MAJOR_WARNING,
                    message="Emergency backup power generator status is not verified for IP storage facility.",
                    remediation_action="Document backup generator inspection and annual test.",
                )
            )

        return cleared

    @classmethod
    def _check_ip_depot_release(
        cls,
        criteria: SiteStartupCriteria,
        deficiencies: list[DeficiencyItem],
    ) -> bool:
        """Validate Qualified Person (QP) release and depot inventory authorization."""
        ip_depot = criteria.ip_depot_release
        if not ip_depot:
            return True

        cleared = True
        if not ip_depot.qp_release_authorized:
            deficiencies.append(
                DeficiencyItem(
                    code="QP_RELEASE_PENDING",
                    category=DeficiencyCategory.INVESTIGATIONAL_PRODUCT_RELEASE,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message="Qualified Person (QP) release authorization is pending for clinical batch.",
                    remediation_action="Obtain QP Batch Certificate of Analysis and release sign-off.",
                )
            )
            cleared = False

        if not ip_depot.import_license_approved:
            deficiencies.append(
                DeficiencyItem(
                    code="IMPORT_LICENSE_MISSING",
                    category=DeficiencyCategory.INVESTIGATIONAL_PRODUCT_RELEASE,
                    severity=DeficiencySeverity.CRITICAL_BLOCKER,
                    message="Customs / health authority import license is missing for clinical supplies.",
                    remediation_action="Obtain import permit from destination country health authority.",
                )
            )
            cleared = False

        if not ip_depot.depot_stock_available:
            deficiencies.append(
                DeficiencyItem(
                    code="DEPOT_STOCKOUT",
                    category=DeficiencyCategory.INVESTIGATIONAL_PRODUCT_RELEASE,
                    severity=DeficiencySeverity.MAJOR_WARNING,
                    message="Central depot IP stock is currently depleted.",
                    remediation_action="Manufacture / resupply IP inventory to regional distribution depot.",
                )
            )

        return cleared

    @staticmethod
    def _generate_decision_signature(
        assessment_id: str,
        study_id: str,
        site_id: str,
        status: str,
        deficiency_codes: list[str],
        assessed_at_iso: str,
    ) -> str:
        """Generate deterministic SHA-256 digital signature for 21 CFR Part 11 compliance."""
        payload = {
            "assessment_id": assessment_id,
            "study_id": study_id,
            "site_id": site_id,
            "status": status,
            "deficiencies": sorted(deficiency_codes),
            "assessed_at": assessed_at_iso,
        }
        raw_str = json.dumps(payload, sort_keys=True, separators=(",", ":"))
        return hashlib.sha256(raw_str.encode("utf-8")).hexdigest()
