"""
Organization Directory and Delegation of Authority (DOA) domain models.

This module provides the shared Pydantic v2 domain models and controlled
vocabularies (enums) for organization types, clinical staff roles, and
delegatable significant trial-related duties in compliance with 21 CFR Part 11,
EU Annex 11, and ICH E6(R2).
"""

from enum import Enum

from audit import AuditFields  # noqa: F401


class OrganizationType(str, Enum):
    """
    Standard organization types involved in clinical trials.
    """

    SPONSOR = "sponsor"
    CRO = "CRO"
    IRB_IEC = "IRB/IEC"
    CENTRAL_LAB = "central laboratory"
    CENTRAL_LABORATORY = "central laboratory"
    SITE = "site"


class ClinicalStaffRole(str, Enum):
    """
    Standard clinical staff role vocabulary from docs/SDLC/05_Security_Compliance_Audit_Spec.md,
    reused across organization directory and delegation of authority records. Includes the
    External Monitor persona aligned to CRO affiliation.
    """

    PRINCIPAL_INVESTIGATOR = "Principal Investigator"
    SUB_INVESTIGATOR = "Sub-Investigator"
    CRC = "CRC"
    CRA_MONITOR = "CRA/Monitor"
    EXTERNAL_MONITOR = "External Monitor"


class StaffRole(str, Enum):
    """
    Shared Clinical & Site Staff roles vocabulary, derived from SDLC Security Compliance & Audit Spec.
    """

    PI = "Principal Investigator"
    PRINCIPAL_INVESTIGATOR = "Principal Investigator"
    SUB_INVESTIGATOR = "Sub-Investigator"
    CRC = "CRC"
    CRA_MONITOR = "CRA/Monitor"
    EXTERNAL_MONITOR = "External Monitor"


class TrialDuty(str, Enum):
    """
    Controlled vocabulary of delegatable significant trial-related duties aligned with ICH E6(R2).
    """

    INFORMED_CONSENT = "Informed Consent Process"
    ELIGIBILITY_ASSESSMENT = "Subject Recruitment & Eligibility Assessment"
    RANDOMIZATION = "Randomization & Interactive Response Technology Management"
    IP_MANAGEMENT = "Investigational Product (IP) Management & Dispensation"
    CRF_COMPLETION = "CRF Completion & Data Entry"
    QUERY_RESOLUTION = "Clinical Query & Discrepancy Resolution"
    MEDICAL_ASSESSMENT = "Medical Assessments & Safety Evaluations"
    LAB_SAMPLE_MANAGEMENT = "Laboratory Sample Handling, Processing & Shipment"
    SAFETY_REPORTING = "Adverse Event (AE) & Serious Adverse Event (SAE) Reporting"
    TRIAL_OVERSIGHT = "Trial Oversight & Principal Investigator Duties"
