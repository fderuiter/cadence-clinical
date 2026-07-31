"""Pydantic data models for Delegation of Authority (DOA) site staffing and task delegation log.

Requirements: PRD-SYS-001
"""

from enum import StrEnum

from pydantic import BaseModel, Field


class DOATaskRoleEnum(StrEnum):
    """Site personnel roles on Delegation of Authority log.

    Requirements: PRD-SYS-001
    """

    PRINCIPAL_INVESTIGATOR = "PRINCIPAL_INVESTIGATOR"
    SUB_INVESTIGATOR = "SUB_INVESTIGATOR"
    CLINICAL_RESEARCH_COORDINATOR = "CLINICAL_RESEARCH_COORDINATOR"
    STUDY_NURSE = "STUDY_NURSE"
    DATA_MANAGER = "DATA_MANAGER"


class DOATaskDelegationEnum(StrEnum):
    """Specific clinical trial study tasks delegated to site personnel.

    Requirements: PRD-SYS-001
    """

    SUBJECT_INFORMED_CONSENT = "SUBJECT_INFORMED_CONSENT"
    PHYSICAL_EXAMINATION = "PHYSICAL_EXAMINATION"
    AE_SAE_REPORTING = "AE_SAE_REPORTING"
    CRF_DATA_ENTRY = "CRF_DATA_ENTRY"
    PI_CASEBOOK_SIGNOFF = "PI_CASEBOOK_SIGNOFF"


class DOAAssignmentRecord(BaseModel):
    """Delegation of Authority (DOA) site personnel assignment log record.

    Requirements: PRD-SYS-001
    """

    record_id: str = Field(..., description="Unique DOA record identifier")
    study_id: str = Field(..., description="Target protocol study ID")
    site_id: str = Field(..., description="Target investigator site ID")
    personnel_name: str = Field(..., description="Full legal name of site personnel")
    personnel_email: str = Field(..., description="Email address of site personnel")
    role: DOATaskRoleEnum = Field(..., description="Site personnel role")
    delegated_tasks: list[DOATaskDelegationEnum] = Field(
        ..., description="List of delegated study tasks"
    )
    start_date: str = Field(..., description="Task delegation start date (YYYY-MM-DD)")
    end_date: str | None = Field(None, description="Optional task delegation end date")
    is_active: bool = Field(True, description="True if assignment is active")
    signed_off: bool = Field(False, description="True if eSignature endorsed by PI")
