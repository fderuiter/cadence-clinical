"""Pydantic v2 Models for Site Startup (SSU) & Regulatory Greenlight Engine.

Defines schemas for regulatory clearances, institutional review board approvals,
executed trial agreements, staff GCP qualifications, delegation of authority,
and investigational product release decision trees (21 CFR Part 11 compliant).
"""

from __future__ import annotations

from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field


class GreenlightStatus(StrEnum):
    """Multi-stage greenlight decision status."""

    BLOCKED = "BLOCKED"
    CONDITIONAL_RELEASE = "CONDITIONAL_RELEASE"
    GREENLIGHT_CLEARED = "GREENLIGHT_CLEARED"


class DeficiencySeverity(StrEnum):
    """Severity classification of greenlight deficiencies."""

    CRITICAL_BLOCKER = "CRITICAL_BLOCKER"
    MAJOR_WARNING = "MAJOR_WARNING"
    INFORMATIONAL = "INFORMATIONAL"


class DeficiencyCategory(StrEnum):
    """Categorical classification of regulatory gating requirements."""

    REGULATORY_IRB = "REGULATORY_IRB"
    CONTRACT_CTA = "CONTRACT_CTA"
    ESSENTIAL_DOCUMENTS = "ESSENTIAL_DOCUMENTS"
    TRAINING_COMPLIANCE = "TRAINING_COMPLIANCE"
    DELEGATION_OF_AUTHORITY = "DELEGATION_OF_AUTHORITY"
    FACILITY_CERTIFICATION = "FACILITY_CERTIFICATION"
    INVESTIGATIONAL_PRODUCT_RELEASE = "INVESTIGATIONAL_PRODUCT_RELEASE"


class DeficiencyItem(BaseModel):
    """Granular actionable diagnostic for unmet startup criteria."""

    code: str
    category: DeficiencyCategory
    severity: DeficiencySeverity
    message: str
    remediation_action: str
    required_role: str | None = None
    document_reference: str | None = None


class IRBApprovalRecord(BaseModel):
    """Institutional Review Board / Independent Ethics Committee record."""

    irb_name: str
    approval_number: str
    protocol_version: str
    approval_date: datetime
    expiration_date: datetime
    is_full_board_approval: bool = True
    safety_reporting_acknowledged: bool = True


class ContractRecord(BaseModel):
    """Clinical trial legal agreement or financial contract."""

    contract_id: str
    contract_type: str = "CTA"
    is_fully_executed: bool
    executed_date: datetime | None = None
    sponsor_signed: bool = True
    institution_signed: bool = True
    pi_signed: bool = True


class StaffRole(StrEnum):
    """Clinical site staff role taxonomy."""

    PRINCIPAL_INVESTIGATOR = "PRINCIPAL_INVESTIGATOR"
    SUB_INVESTIGATOR = "SUB_INVESTIGATOR"
    CLINICAL_RESEARCH_COORDINATOR = "CLINICAL_RESEARCH_COORDINATOR"
    STUDY_NURSE = "STUDY_NURSE"
    PHARMACIST = "PHARMACIST"
    LAB_TECHNICIAN = "LAB_TECHNICIAN"


class StaffQualificationRecord(BaseModel):
    """Individual investigator or site staff qualification & training checklist."""

    staff_id: str
    staff_name: str
    role: StaffRole
    is_cv_current: bool = True
    is_medical_license_active: bool = True
    is_gcp_certified: bool = True
    financial_disclosure_signed: bool = True
    protocol_training_completed: bool = True
    trained_protocol_version: str = "1.0"
    edc_training_completed: bool = True
    doa_signed: bool = True
    doa_start_date: datetime | None = None


class SiteFacilityRecord(BaseModel):
    """Site facility, lab accreditation, and cold chain readiness record."""

    site_id: str
    clia_cap_certified: bool = True
    clia_expiration: datetime | None = None
    normal_lab_ranges_current: bool = True
    ip_storage_temperature_calibrated: bool = True
    backup_power_generator_verified: bool = True


class IPDepotReleaseRecord(BaseModel):
    """Investigational Product depot distribution and QP authorization record."""

    study_id: str
    site_id: str
    qp_release_authorized: bool = True
    import_license_approved: bool = True
    depot_stock_available: bool = True


class SiteStartupCriteria(BaseModel):
    """Comprehensive site startup evaluation dossier."""

    study_id: str
    site_id: str
    target_protocol_version: str = "1.0"
    irb_approval: IRBApprovalRecord | None = None
    contracts: list[ContractRecord] = Field(default_factory=list)
    staff_qualifications: list[StaffQualificationRecord] = Field(default_factory=list)
    facility: SiteFacilityRecord | None = None
    ip_depot_release: IPDepotReleaseRecord | None = None
    fda_1572_signed: bool = False
    site_initiation_visit_completed: bool = False
    metadata: dict[str, Any] = Field(default_factory=dict)


class GreenlightDecision(BaseModel):
    """Multi-criteria regulatory clearance decision with 21 CFR Part 11 digital seal."""

    assessment_id: str
    study_id: str
    site_id: str
    status: GreenlightStatus
    can_ship_ip: bool
    can_screen_subjects: bool
    can_enroll_subjects: bool
    irb_cleared: bool
    contracts_cleared: bool
    documents_cleared: bool
    staff_cleared: bool
    facility_cleared: bool
    ip_depot_cleared: bool
    deficiencies: list[DeficiencyItem]
    assessed_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    digital_signature_hash: str
