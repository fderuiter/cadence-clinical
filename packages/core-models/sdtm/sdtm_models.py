"""
SDTM Core Domain Pydantic v2 Models for mapped records.

Defines schemas with GxP audit metadata (inheriting from AuditableModel)
for storing transformed SDTM domain data.
"""

from typing import Optional, Union

from pydantic import Field, field_validator

from sdtm.models import AuditableModel, validate_dtc_format


class SDTMRecordDM(AuditableModel):
    """
    Mapped SDTM Record Demographics (DM) domain schema.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("DM", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    SUBJID: Optional[str] = Field(None, description="Subject Identifier (Expected)")
    RFSTDTC: Optional[str] = Field(
        None, description="Subject Reference Start Date/Time (Expected)"
    )
    BRTHDTC: Optional[str] = Field(None, description="Date of Birth (Permissible)")
    AGE: Optional[Union[int, float]] = Field(None, description="Age (Expected)")
    AGEU: Optional[str] = Field(None, description="Age Units (Expected)")
    SEX: Optional[str] = Field(
        None, description="Sex (Required, normalizes to 'M', 'F', 'U')"
    )
    RACE: Optional[str] = Field(
        None, description="Race (Required, normalizes to CDISC RACE CT)"
    )
    ETHNIC: Optional[str] = Field(None, description="Ethnic Group")

    @field_validator("STUDYID", "DOMAIN", "USUBJID")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v

    @field_validator("RFSTDTC", "BRTHDTC")
    @classmethod
    def validate_dates(cls, v: Optional[str]) -> Optional[str]:
        return validate_dtc_format(v)


class SDTMRecordAE(AuditableModel):
    """
    Mapped SDTM Record Adverse Events (AE) domain schema.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("AE", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    AESEQ: int = Field(..., description="Sequence Number (Required)")
    AETERM: str = Field(
        ..., description="Reported Term for the Adverse Event (Required)"
    )
    AEDECOD: Optional[str] = Field(
        None, description="Dictionary-Derived Term (Expected)"
    )
    AESEV: Optional[str] = Field(None, description="Severity/Intensity (Permissible)")
    AESER: str = Field(..., description="Serious Adverse Event Flag (Required)")
    AESTDTC: Optional[str] = Field(
        None, description="Start Date/Time of Adverse Event (Expected)"
    )
    AEENDTC: Optional[str] = Field(
        None, description="End Date/Time of Adverse Event (Expected)"
    )

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "AETERM", "AESER")
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

    @field_validator("AESTDTC", "AEENDTC")
    @classmethod
    def validate_dates(cls, v: Optional[str]) -> Optional[str]:
        return validate_dtc_format(v)


class SDTMRecordVS(AuditableModel):
    """
    Mapped SDTM Record Vital Signs (VS) domain schema.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("VS", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    VSSEQ: int = Field(..., description="Sequence Number (Required)")
    VSTESTCD: str = Field(..., description="Vital Signs Test Short Code (Required)")
    VSTEST: str = Field(..., description="Vital Signs Test Name (Required)")
    VSORRES: Optional[Union[int, float]] = Field(
        None, description="Original Result (Expected)"
    )
    VSORRESU: Optional[str] = Field(None, description="Original Result Unit (Expected)")
    VSSTRESC: Optional[str] = Field(
        None, description="Standardized Result in Character Format (Expected)"
    )
    VSSTRESN: Optional[float] = Field(
        None, description="Standardized Result in Numeric Format (Expected)"
    )
    VSSTRESU: Optional[str] = Field(
        None, description="Standardized Result Unit (Expected)"
    )
    VSDTC: Optional[str] = Field(
        None, description="Date/Time of Vital Signs Measurement (Expected)"
    )
    VSDY: Optional[int] = Field(
        None, description="Study Day of Vital Signs Measurement"
    )

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
    def validate_dates(cls, v: Optional[str]) -> Optional[str]:
        return validate_dtc_format(v)


class SDTMRecordLB(AuditableModel):
    """
    Mapped SDTM Record Laboratory Findings (LB) domain schema.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("LB", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    LBSEQ: int = Field(..., description="Sequence Number (Required)")
    LBTESTCD: str = Field(..., description="Lab Test Short Code (Required)")
    LBTEST: str = Field(..., description="Lab Test Name (Required)")
    LBORRES: Optional[str] = Field(None, description="Original Result (Expected)")
    LBORRESU: Optional[str] = Field(None, description="Original Result Unit (Expected)")
    LBSTRESC: Optional[str] = Field(
        None, description="Standardized Result in Character Format (Expected)"
    )
    LBSTRESN: Optional[float] = Field(
        None, description="Standardized Result in Numeric Format (Expected)"
    )
    LBSTRESU: Optional[str] = Field(
        None, description="Standardized Result Unit (Expected)"
    )
    LBDTC: Optional[str] = Field(
        None, description="Date/Time of Specimen Collection (Expected)"
    )
    LBDY: Optional[int] = Field(None, description="Study Day of Specimen Collection")

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
    def validate_dates(cls, v: Optional[str]) -> Optional[str]:
        return validate_dtc_format(v)


class SDTMRecordSV(AuditableModel):
    """
    Mapped SDTM Record Subject Visits (SV) domain schema.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("SV", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    SVSEQ: int = Field(..., description="Sequence Number (Required)")
    VISIT: str = Field(..., description="Visit Name (Required)")
    SVSTDTC: Optional[str] = Field(
        None, description="Start Date/Time of Visit (Required)"
    )
    SVENDTC: Optional[str] = Field(
        None, description="End Date/Time of Visit (Permissible)"
    )
    SVDY: Optional[int] = Field(None, description="Study Day of Visit")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "VISIT")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v

    @field_validator("SVSEQ")
    @classmethod
    def validate_sequence(cls, v: int) -> int:
        if v < 1:
            raise ValueError("SVSEQ must be greater than or equal to 1")
        return v

    @field_validator("SVSTDTC", "SVENDTC")
    @classmethod
    def validate_dates(cls, v: Optional[str]) -> Optional[str]:
        return validate_dtc_format(v)


class SDTMRecordCM(AuditableModel):
    """
    Mapped SDTM Record Concomitant Medications (CM) domain schema.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("CM", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    CMSEQ: int = Field(..., description="Sequence Number (Required)")
    CMTRT: str = Field(..., description="Reported Name of Medication (Required)")
    CMDECOD: Optional[str] = Field(None, description="Standardized Medication Name")
    CMSTDTC: Optional[str] = Field(None, description="Start Date/Time of Medication")
    CMENDTC: Optional[str] = Field(None, description="End Date/Time of Medication")

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
    def validate_dates(cls, v: Optional[str]) -> Optional[str]:
        return validate_dtc_format(v)


class SDTMRecordDS(AuditableModel):
    """
    Mapped SDTM Record Disposition (DS) domain schema.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("DS", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    DSSEQ: int = Field(..., description="Sequence Number (Required)")
    DSTERM: str = Field(..., description="Reported Term for Disposition Event")
    DSDECOD: str = Field(..., description="Standardized Disposition Term")
    DSCAT: Optional[str] = Field(None, description="Category of Disposition Event")
    DSSTDTC: Optional[str] = Field(
        None, description="Start Date/Time of Disposition Event"
    )

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "DSTERM", "DSDECOD")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v

    @field_validator("DSSEQ")
    @classmethod
    def validate_sequence(cls, v: int) -> int:
        if v < 1:
            raise ValueError("DSSEQ must be greater than or equal to 1")
        return v

    @field_validator("DSSTDTC")
    @classmethod
    def validate_dates(cls, v: Optional[str]) -> Optional[str]:
        return validate_dtc_format(v)


class SDTMRecordMH(AuditableModel):
    """
    Mapped SDTM Record Medical History (MH) domain schema.
    """

    STUDYID: str = Field(..., description="Study Identifier (Required)")
    DOMAIN: str = Field("MH", description="Domain Abbreviation (Required)")
    USUBJID: str = Field(..., description="Unique Subject Identifier (Required)")
    MHSEQ: int = Field(..., description="Sequence Number (Required)")
    MHTERM: str = Field(..., description="Reported Term for Medical History")
    MHDECOD: Optional[str] = Field(
        None, description="Standardized Medical History Term"
    )
    MHCAT: Optional[str] = Field(None, description="Category of Medical History")
    MHDTC: Optional[str] = Field(None, description="Date/Time of History")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "MHTERM")
    @classmethod
    def validate_non_empty_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or consist only of whitespace.")
        return v

    @field_validator("MHSEQ")
    @classmethod
    def validate_sequence(cls, v: int) -> int:
        if v < 1:
            raise ValueError("MHSEQ must be greater than or equal to 1")
        return v

    @field_validator("MHDTC")
    @classmethod
    def validate_dates(cls, v: Optional[str]) -> Optional[str]:
        return validate_dtc_format(v)
