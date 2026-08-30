"""Unit Tests for Site Startup (SSU) & Regulatory Greenlight Engine.

Verifies multi-criteria clearance gating, IRB/EC expiration checks, CTA execution,
essential document verification (Form FDA 1572), staff GCP qualifications,
DOA logs, and investigational product release decision trees (21 CFR Part 11 compliant).

@req:PRD-SYS-050
@req:PRD-SSU-001
@req:PRD-CTMS-004
@req:PRD-SYS-003
@req:Trace-1
"""

from __future__ import annotations

from datetime import UTC, datetime, timedelta

from packages.engine_core.operations import (
    ContractRecord,
    GreenlightStatus,
    IPDepotReleaseRecord,
    IRBApprovalRecord,
    SiteFacilityRecord,
    SiteGreenlightEngine,
    SiteStartupCriteria,
    SiteStartupGreenlightEngine,
    StaffQualificationRecord,
    StaffRole,
)


def _build_compliant_criteria() -> SiteStartupCriteria:
    """Build a fully compliant site startup criteria dossier."""
    now = datetime(2026, 3, 1, 12, 0, tzinfo=UTC)
    return SiteStartupCriteria(
        study_id="STUDY-IMMUNO-01",
        site_id="SITE-501",
        target_protocol_version="2.0",
        irb_approval=IRBApprovalRecord(
            irb_name="Western Institutional Review Board",
            approval_number="WIRB-2026-981",
            protocol_version="2.0",
            approval_date=now - timedelta(days=30),
            expiration_date=now + timedelta(days=335),
            is_full_board_approval=True,
            safety_reporting_acknowledged=True,
        ),
        contracts=[
            ContractRecord(
                contract_id="CTA-501",
                contract_type="CTA",
                is_fully_executed=True,
                executed_date=now - timedelta(days=20),
                sponsor_signed=True,
                institution_signed=True,
                pi_signed=True,
            ),
        ],
        fda_1572_signed=True,
        site_initiation_visit_completed=True,
        staff_qualifications=[
            StaffQualificationRecord(
                staff_id="STAFF-PI",
                staff_name="Dr. Jane Doe, MD",
                role=StaffRole.PRINCIPAL_INVESTIGATOR,
                is_cv_current=True,
                is_medical_license_active=True,
                is_gcp_certified=True,
                financial_disclosure_signed=True,
                protocol_training_completed=True,
                trained_protocol_version="2.0",
                edc_training_completed=True,
                doa_signed=True,
            ),
            StaffQualificationRecord(
                staff_id="STAFF-CRC",
                staff_name="John Coordinator, BSN",
                role=StaffRole.CLINICAL_RESEARCH_COORDINATOR,
                is_cv_current=True,
                is_gcp_certified=True,
                financial_disclosure_signed=True,
                protocol_training_completed=True,
                trained_protocol_version="2.0",
                edc_training_completed=True,
                doa_signed=True,
            ),
        ],
        facility=SiteFacilityRecord(
            site_id="SITE-501",
            clia_cap_certified=True,
            clia_expiration=now + timedelta(days=400),
            normal_lab_ranges_current=True,
            ip_storage_temperature_calibrated=True,
            backup_power_generator_verified=True,
        ),
        ip_depot_release=IPDepotReleaseRecord(
            study_id="STUDY-IMMUNO-01",
            site_id="SITE-501",
            qp_release_authorized=True,
            import_license_approved=True,
            depot_stock_available=True,
        ),
    )


def test_site_greenlight_fully_compliant_cleared():
    """Verify fully compliant site startup dossier produces GREENLIGHT_CLEARED status.

    @req:PRD-SYS-050
    @req:PRD-SSU-001
    @req:PRD-SYS-003
    """
    criteria = _build_compliant_criteria()
    decision = SiteStartupGreenlightEngine.evaluate(
        criteria, as_of_date=datetime(2026, 3, 1, 12, 0, tzinfo=UTC)
    )

    assert decision.status == GreenlightStatus.GREENLIGHT_CLEARED
    assert decision.can_ship_ip is True
    assert decision.can_screen_subjects is True
    assert decision.can_enroll_subjects is True
    assert decision.irb_cleared is True
    assert decision.contracts_cleared is True
    assert decision.documents_cleared is True
    assert decision.staff_cleared is True
    assert decision.facility_cleared is True
    assert decision.ip_depot_cleared is True
    assert len(decision.deficiencies) == 0
    assert decision.digital_signature_hash != ""


def test_site_greenlight_irb_expired_and_version_mismatch_blocks():
    """Verify expired IRB approval or protocol version mismatch blocks greenlight.

    @req:PRD-SYS-050
    @req:PRD-SSU-001
    @req:PRD-CTMS-004
    """
    now = datetime(2026, 3, 1, 12, 0, tzinfo=UTC)
    criteria = _build_compliant_criteria()
    # Expired IRB
    criteria.irb_approval.expiration_date = now - timedelta(days=5)
    # Version mismatch
    criteria.irb_approval.protocol_version = "1.0"

    decision = SiteGreenlightEngine.evaluate_readiness(criteria, as_of_date=now)

    assert decision.status == GreenlightStatus.BLOCKED
    assert decision.can_ship_ip is False
    assert decision.can_enroll_subjects is False
    assert decision.irb_cleared is False
    codes = [d.code for d in decision.deficiencies]
    assert "IRB_EXPIRED" in codes
    assert "IRB_VERSION_MISMATCH" in codes


def test_site_greenlight_unexecuted_cta_blocks():
    """Verify unexecuted Clinical Trial Agreement blocks IP release and enrollment.

    @req:PRD-SYS-050
    @req:PRD-SSU-001
    """
    criteria = _build_compliant_criteria()
    criteria.contracts[0].is_fully_executed = False
    criteria.contracts[0].pi_signed = False

    decision = SiteGreenlightEngine.evaluate_readiness(criteria)

    assert decision.status == GreenlightStatus.BLOCKED
    assert decision.can_ship_ip is False
    assert decision.contracts_cleared is False
    codes = [d.code for d in decision.deficiencies]
    assert "CTA_UNEXECUTED" in codes
    assert "CTA_PI_SIGNATURE_MISSING" in codes


def test_site_greenlight_missing_fda_1572_blocks():
    """Verify missing Form FDA 1572 blocks site greenlight.

    @req:PRD-SYS-050
    @req:PRD-SSU-001
    @req:PRD-SYS-003
    """
    criteria = _build_compliant_criteria()
    criteria.fda_1572_signed = False

    decision = SiteGreenlightEngine.evaluate_readiness(criteria)

    assert decision.status == GreenlightStatus.BLOCKED
    assert decision.documents_cleared is False
    codes = [d.code for d in decision.deficiencies]
    assert "FDA_1572_MISSING" in codes


def test_site_greenlight_staff_deficiencies_diagnostics():
    """Verify granular diagnostic detection of staff license, GCP, and training issues.

    @req:PRD-SYS-050
    @req:PRD-SSU-001
    @req:PRD-CTMS-004
    """
    criteria = _build_compliant_criteria()
    pi = criteria.staff_qualifications[0]
    crc = criteria.staff_qualifications[1]

    # Invalidate PI license and GCP
    pi.is_medical_license_active = False
    pi.is_gcp_certified = False
    # CRC training mismatch
    crc.trained_protocol_version = "1.0"
    crc.doa_signed = False

    decision = SiteGreenlightEngine.evaluate_readiness(criteria)

    assert decision.status == GreenlightStatus.BLOCKED
    assert decision.staff_cleared is False
    codes = [d.code for d in decision.deficiencies]
    assert "STAFF_LICENSE_INACTIVE" in codes
    assert "STAFF_GCP_EXPIRED" in codes
    assert "STAFF_TRAINING_MISMATCH" in codes
    assert "STAFF_DOA_MISSING" in codes


def test_site_greenlight_conditional_release_for_pending_siv():
    """Verify pending Site Initiation Visit grants CONDITIONAL_RELEASE (allows IP staging, blocks enrollment).

    @req:PRD-SYS-050
    @req:PRD-SSU-001
    """
    criteria = _build_compliant_criteria()
    # All criteria met except SIV is scheduled/pending
    criteria.site_initiation_visit_completed = False

    decision = SiteGreenlightEngine.evaluate_readiness(criteria)

    assert decision.status == GreenlightStatus.CONDITIONAL_RELEASE
    assert decision.can_ship_ip is True  # Conditional pre-shipping permitted
    assert decision.can_screen_subjects is True
    assert decision.can_enroll_subjects is False  # Cannot enroll before SIV
    codes = [d.code for d in decision.deficiencies]
    assert "SIV_INCOMPLETE" in codes


def test_site_greenlight_facility_and_ip_depot_failures():
    """Verify uncalibrated IP storage temperature and pending QP release block clearance.

    @req:PRD-SYS-050
    @req:PRD-SSU-001
    @req:PRD-SYS-003
    """
    criteria = _build_compliant_criteria()
    criteria.facility.ip_storage_temperature_calibrated = False
    criteria.ip_depot_release.qp_release_authorized = False

    decision = SiteGreenlightEngine.evaluate_readiness(criteria)

    assert decision.status == GreenlightStatus.BLOCKED
    assert decision.facility_cleared is False
    assert decision.ip_depot_cleared is False
    codes = [d.code for d in decision.deficiencies]
    assert "IP_TEMP_UNCALIBRATED" in codes
    assert "QP_RELEASE_PENDING" in codes
