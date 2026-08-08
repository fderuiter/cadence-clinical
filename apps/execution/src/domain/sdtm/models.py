"""
SDTM Core Domain Pydantic v2 Models.

This module provides strongly-typed, validated Pydantic v2 models for
SDTM DM, AE, VS, LB, CM, and generic SUPPQUAL records.
These models integrate CDISC controlled terminology normalizations and
comply with FDA 21 CFR Part 11 / GxP audit guidelines.
"""

import re
from datetime import UTC, datetime

from pydantic import BaseModel, Field, field_validator, model_validator

from packages.database.datetime_helpers import AwareDatetime

from .enums import (
    AEOutcome,
    AERelationship,
    AESeriousness,
    AESeverity,
    Race,
    Sex,
)
from .terminology import (
    normalize_race,
    normalize_seriousness,
    normalize_severity,
    normalize_sex,
)

# DTC (Date/Time) validation regex supporting partial dates (e.g. YYYY, YYYY-MM, YYYY-MM-DD, or full ISO 8601 with timezone)
DTC_REGEX = re.compile(
    r"^\d{4}(-\d{2}(-\d{2}(T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}(:\d{2})?)?)?)?)?$"
)


def validate_dtc_format(val: str | None) -> str | None:
    """
    Validates that a string complies with CDISC DTC or ISO 8601 date-time format.
    Allows partial dates (YYYY, YYYY-MM, YYYY-MM-DD) and full ISO 8601 timestamps.
    """
    if val is None:
        return None
    if not isinstance(val, str) or not DTC_REGEX.match(val):
        raise ValueError(
            f"Value '{val}' does not conform to CDISC DTC or ISO 8601 date-time format."
        )
    return val


class AuditableModel(BaseModel):
    """
    A Pydantic v2 model mixin containing standard 21 CFR Part 11
    and GxP compliant audit and metadata fields.
    """

    created_at: AwareDatetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Chronological UTC timestamp when the record was created.",
    )
    created_by: str = Field(
        ...,
        description="Unique identifier (e.g. username/OIDC user_id) of the user who created the record.",
    )
    reason_for_change: str = Field(
        ...,
        description="Mandatory explanation or audit justification for creating or mutating this record.",
    )
    version_index: int = Field(
        default=1,
        description="Optimistic locking or row version counter, initialized to 1.",
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason_for_change(cls, v: str) -> str:
        """
        Validate that the reason_for_change is a non-empty, non-blank string.
        """
        if not isinstance(v, str) or not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v

    @field_validator("version_index")
    @classmethod
    def validate_version_index(cls, v: int) -> int:
        """
        Validate that version_index is greater than or equal to 1.
        """
        if v < 1:
            raise ValueError("version_index must be greater than or equal to 1")
        return v


class DM(AuditableModel):
    """
    Demographics (DM) SDTM Domain model representing Blueprint §2.2 mapping variables.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("DM", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    SUBJID: str | None = Field(None, description="Subject Identifier (Expected)")
    RFSTDTC: str | None = Field(
        None, description="Subject Reference Start Date/Time (Expected)"
    )
    RFENDTC: str | None = Field(
        None, description="Subject Reference End Date/Time (Expected)"
    )
    BRTHDTC: str | None = Field(None, description="Date of Birth (Permissible)")
    AGE: int | float | None = Field(None, description="Age (Expected)")
    AGEU: str | None = Field(None, description="Age Units (Expected)")
    SEX: Sex = Field(..., description="Sex (Required, normalizes to 'M', 'F', 'U')")
    RACE: Race = Field(..., description="Race (Required, normalizes to CDISC RACE CT)")
    ARM: str = Field(..., description="Description of Planned Arm (Required)")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "ARM")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v

    @field_validator("RFSTDTC", "RFENDTC", "BRTHDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_dtc_format(v)

    @field_validator("SEX", mode="before")
    @classmethod
    def validate_sex(cls, v: str | None) -> str | None:
        if v is None:
            return None
        return normalize_sex(v)

    @field_validator("RACE", mode="before")
    @classmethod
    def validate_race(cls, v: str | list | None) -> str | None:
        if v is None:
            return None
        return normalize_race(v)


class AE(AuditableModel):
    """
    Adverse Events (AE) SDTM Domain model representing Blueprint §2.2 mapping variables.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("AE", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    AESEQ: int = Field(..., description="Sequence Number (Required)")
    AETERM: str = Field(
        ..., description="Reported Term for the Adverse Event (Required)"
    )
    AELOC: str | None = Field(None, description="Anatomical Location (Permissible)")
    AELDTC: str | None = Field(
        None, description="Date/Time of Local Adverse Event Onset (Permissible)"
    )
    AESTDTC: str | None = Field(
        None, description="Start Date/Time of Adverse Event (Expected)"
    )
    AEENDTC: str | None = Field(
        None, description="End Date/Time of Adverse Event (Expected)"
    )
    AESEV: AESeverity | None = Field(
        None, description="Severity/Intensity (Permissible)"
    )
    AESER: AESeriousness = Field(
        ..., description="Serious Adverse Event Flag (Required)"
    )
    AEREL: AERelationship | None = Field(
        None, description="Causality / Relationship to treatment (Permissible)"
    )
    AEOUT: AEOutcome | None = Field(None, description="Outcome (Permissible)")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "AETERM")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v

    @field_validator("AESEQ")
    @classmethod
    def validate_sequence(cls, v: int) -> int:
        if v < 1:
            raise ValueError("AESEQ must be greater than or equal to 1")
        return v

    @field_validator("AELDTC", "AESTDTC", "AEENDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_dtc_format(v)

    @field_validator("AESEV", mode="before")
    @classmethod
    def validate_severity(cls, v: str | None) -> str | None:
        if v is None:
            return None
        return normalize_severity(v)

    @field_validator("AESER", mode="before")
    @classmethod
    def validate_seriousness(cls, v: str | bool | None) -> str | None:
        if v is None:
            return None
        return normalize_seriousness(v)

    @model_validator(mode="after")
    def validate_ae_dates(self) -> AE:
        if self.AESTDTC and self.AEENDTC:
            s_clean = re.sub(r"[^\d]", "", self.AESTDTC)
            e_clean = re.sub(r"[^\d]", "", self.AEENDTC)
            min_len = min(len(s_clean), len(e_clean))
            if min_len > 0:
                if e_clean[:min_len] < s_clean[:min_len]:
                    raise ValueError(
                        f"AEENDTC ({self.AEENDTC}) cannot be earlier than AESTDTC ({self.AESTDTC})"
                    )
        return self


class VS(AuditableModel):
    """
    Vital Signs (VS) SDTM Domain model representing Blueprint §2.2 mapping variables.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("VS", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    VSSEQ: int = Field(..., description="Sequence Number (Required)")
    VSTESTCD: str = Field(..., description="Vital Signs Test Short Code (Required)")
    VSTEST: str = Field(..., description="Vital Signs Test Name (Required)")
    VSORRES: int | float | None = Field(None, description="Original Result (Expected)")
    VSORRESU: str | None = Field(None, description="Original Result Unit (Expected)")
    VSSTRESC: str | None = Field(
        None, description="Standardized Result in Character Format (Expected)"
    )
    VSSTRESN: float | None = Field(
        None, description="Standardized Result in Numeric Format (Expected)"
    )
    VSSTRESU: str | None = Field(
        None, description="Standardized Result Unit (Expected)"
    )
    VSPOS: str | None = Field(None, description="Subject Position (Permissible)")
    VSDTC: str | None = Field(
        None, description="Date/Time of Vital Signs Measurement (Expected)"
    )
    VSBLFL: str | None = Field(None, description="Baseline Flag (Permissible)")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "VSTESTCD", "VSTEST")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v

    @field_validator("VSSEQ")
    @classmethod
    def validate_sequence(cls, v: int) -> int:
        if v < 1:
            raise ValueError("VSSEQ must be greater than or equal to 1")
        return v

    @field_validator("VSDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_dtc_format(v)


class LB(AuditableModel):
    """
    Laboratory Findings (LB) SDTM Domain model representing Blueprint §2.2 mapping variables.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("LB", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    LBSEQ: int = Field(..., description="Sequence Number (Required)")
    LBTESTCD: str = Field(..., description="Lab Test Short Code (Required)")
    LBTEST: str = Field(..., description="Lab Test Name (Required)")
    LBORRES: str | None = Field(None, description="Original Result (Expected)")
    LBORRESU: str | None = Field(None, description="Original Result Unit (Expected)")
    LBSTRESC: str | None = Field(
        None, description="Standardized Result in Character Format (Expected)"
    )
    LBSTRESN: float | None = Field(
        None, description="Standardized Result in Numeric Format (Expected)"
    )
    LBSTRESU: str | None = Field(
        None, description="Standardized Result Unit (Expected)"
    )
    LBNRIND: str | None = Field(
        None, description="Normal Range Reference Indicator (Permissible)"
    )
    LBDTC: str | None = Field(
        None, description="Date/Time of Specimen Collection (Expected)"
    )
    LBLOINC: str | None = Field(None, description="LOINC Code (Permissible)")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "LBTESTCD", "LBTEST")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v

    @field_validator("LBSEQ")
    @classmethod
    def validate_sequence(cls, v: int) -> int:
        if v < 1:
            raise ValueError("LBSEQ must be greater than or equal to 1")
        return v

    @field_validator("LBDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_dtc_format(v)


class CM(AuditableModel):
    """
    Concomitant Medications (CM) SDTM Domain model.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("CM", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    CMSEQ: int = Field(..., description="Sequence Number (Required)")
    CMTRT: str = Field(..., description="Reported Name of Medication (Required)")
    CMDECOD: str | None = Field(
        None, description="Standardized Medication Name (Expected)"
    )
    CMCLAS: str | None = Field(None, description="Medication Class (Permissible)")
    CMDOSE: float | None = Field(None, description="Dose per Administration (Expected)")
    CMDOSEU: str | None = Field(None, description="Dose Units (Expected)")
    CMDOSFRQ: str | None = Field(None, description="Dose Frequency (Expected)")
    CMROUTE: str | None = Field(
        None, description="Route of Administration (Permissible)"
    )
    CMSTDTC: str | None = Field(
        None, description="Start Date/Time of Medication (Expected)"
    )
    CMENDTC: str | None = Field(
        None, description="End Date/Time of Medication (Expected)"
    )

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "CMTRT")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v

    @field_validator("CMSEQ")
    @classmethod
    def validate_sequence(cls, v: int) -> int:
        if v < 1:
            raise ValueError("CMSEQ must be greater than or equal to 1")
        return v

    @field_validator("CMSTDTC", "CMENDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_dtc_format(v)

    @model_validator(mode="after")
    def validate_cm_dates(self) -> CM:
        if self.CMSTDTC and self.CMENDTC:
            s_clean = re.sub(r"[^\d]", "", self.CMSTDTC)
            e_clean = re.sub(r"[^\d]", "", self.CMENDTC)
            min_len = min(len(s_clean), len(e_clean))
            if min_len > 0:
                if e_clean[:min_len] < s_clean[:min_len]:
                    raise ValueError(
                        f"CMENDTC ({self.CMENDTC}) cannot be earlier than CMSTDTC ({self.CMSTDTC})"
                    )
        return self


class SUPPQUAL(AuditableModel):
    """
    Supplemental Qualifiers (SUPPQUAL) SDTM Generic Domain model.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    RDOMAIN: str = Field(..., description="Related Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    IDVAR: str | None = Field("", description="Identifying Variable (Expected)")
    IDVARVAL: str | None = Field(
        "", description="Identifying Variable Value (Expected)"
    )
    QNAM: str = Field(..., description="Qualifier Variable Name (Required)")
    QLABEL: str = Field(..., description="Qualifier Variable Label (Required)")
    QVAL: str = Field(..., description="Qualifier Value (Required)")
    QEVAL: str | None = Field("", description="Qualifier Evaluator (Expected)")

    @field_validator("STUDYID", "RDOMAIN", "USUBJID", "QNAM", "QLABEL", "QVAL")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v


# Expose descriptive names as aliases
Demographics = DM
AdverseEvent = AE
VitalSign = VS
Laboratory = LB
ConcomitantMedication = CM
SUPPQUALRecord = SUPPQUAL
