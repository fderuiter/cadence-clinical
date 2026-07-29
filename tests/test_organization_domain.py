"""
Unit tests for the shared Organization Directory and Delegation of Authority (DOA) domain vocabulary.
"""

from datetime import datetime, timezone

import pytest
from organization_domain import (
    AuditFields,
    ClinicalStaffRole,
    OrganizationType,
    TrialDuty,
)
from pydantic import ValidationError


def test_organization_type_values():
    """
    Verify that OrganizationType enum contains the expected exact values.
    """
    assert OrganizationType.SPONSOR == "sponsor"
    assert OrganizationType.CRO == "CRO"
    assert OrganizationType.IRB_IEC == "IRB/IEC"
    assert OrganizationType.CENTRAL_LABORATORY == "central laboratory"
    assert OrganizationType.SITE == "site"


def test_clinical_staff_role_values():
    """
    Verify that ClinicalStaffRole enum contains the expected exact values.
    """
    assert ClinicalStaffRole.PRINCIPAL_INVESTIGATOR == "Principal Investigator"
    assert ClinicalStaffRole.SUB_INVESTIGATOR == "Sub-Investigator"
    assert ClinicalStaffRole.CRC == "CRC"
    assert ClinicalStaffRole.CRA_MONITOR == "CRA/Monitor"
    assert ClinicalStaffRole.EXTERNAL_MONITOR == "External Monitor"


def test_trial_duty_values():
    """
    Verify that TrialDuty enum contains the expected delegatable trial-related duties.
    """
    assert TrialDuty.INFORMED_CONSENT == "Informed Consent Process"
    assert (
        TrialDuty.ELIGIBILITY_ASSESSMENT
        == "Subject Recruitment & Eligibility Assessment"
    )
    assert (
        TrialDuty.RANDOMIZATION
        == "Randomization & Interactive Response Technology Management"
    )
    assert (
        TrialDuty.IP_MANAGEMENT
        == "Investigational Product (IP) Management & Dispensation"
    )
    assert TrialDuty.CRF_COMPLETION == "CRF Completion & Data Entry"
    assert TrialDuty.QUERY_RESOLUTION == "Clinical Query & Discrepancy Resolution"
    assert TrialDuty.MEDICAL_ASSESSMENT == "Medical Assessments & Safety Evaluations"
    assert (
        TrialDuty.LAB_SAMPLE_MANAGEMENT
        == "Laboratory Sample Handling, Processing & Shipment"
    )
    assert (
        TrialDuty.SAFETY_REPORTING
        == "Adverse Event (AE) & Serious Adverse Event (SAE) Reporting"
    )
    assert (
        TrialDuty.TRIAL_OVERSIGHT == "Trial Oversight & Principal Investigator Duties"
    )


def test_audit_fields_instantiation():
    """
    Verify that AuditFields can be successfully instantiated with valid fields.
    """
    audit = AuditFields(
        created_by="user_123",
        reason_for_change="Initial setup of the organization profile",
    )
    assert audit.created_by == "user_123"
    assert audit.reason_for_change == "Initial setup of the organization profile"
    assert audit.version_index == 1
    assert isinstance(audit.created_at, datetime)

    # Check that created_at is default-populated to UTC (approx now)
    now = datetime.now(timezone.utc)
    assert abs((audit.created_at - now).total_seconds()) < 5


class SubclassedAuditFields(AuditFields):
    """
    Helper subclass to verify reusability of AuditFields as a model or mixin.
    """

    record_name: str


def test_audit_fields_reusability():
    """
    Verify that AuditFields can be subclassed to form more complex models.
    """
    record = SubclassedAuditFields(
        created_by="admin_user",
        reason_for_change="Created test record",
        record_name="My Clinical Site A",
    )
    assert record.record_name == "My Clinical Site A"
    assert record.created_by == "admin_user"
    assert record.reason_for_change == "Created test record"
    assert record.version_index == 1


@pytest.mark.parametrize(
    "invalid_reason",
    [
        "",
        "   ",
        "\n",
        "\t",
    ],
)
def test_audit_fields_change_reason_validation(invalid_reason):
    """
    Verify that the reason_for_change field validator correctly rejects empty or blank strings.
    """
    with pytest.raises(ValidationError) as excinfo:
        AuditFields(
            created_by="user_abc",
            reason_for_change=invalid_reason,
        )
    assert "Reason for change cannot be empty or consist only of whitespace." in str(
        excinfo.value
    )
