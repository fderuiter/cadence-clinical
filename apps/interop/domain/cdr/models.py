"""Pydantic v2 domain models for CDISC SDTM, ADaM, Dataset-JSON 1.1/2.0, and Define-XML 2.1.

Requirements: PRD-CDR-002, PRD-CDR-003, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import re
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, field_validator


def validate_iso_dtc(v: str | None) -> str | None:
    """Validate ISO 8601 DTC format (YYYY, YYYY-MM, YYYY-MM-DD, or YYYY-MM-DDTHH:MM:SS)."""
    if v is None or v == "":
        return None
    iso_pattern = re.compile(
        r"^\d{4}(-(0[1-9]|1[0-2])(-(0[1-9]|[12]\d|3[01])(T([01]\d|2[0-3]):[0-5]\d(:[0-5]\d(\.\d+)?)?(Z|[+-](0\d|1[0-4]):[0-5]\d)?)?)?)?$"
    )
    if not iso_pattern.match(v):
        raise ValueError(
            f"Invalid ISO 8601 DTC date/time format: '{v}'. Must be YYYY, YYYY-MM, YYYY-MM-DD, or full ISO timestamp."
        )
    return v


# =========================================================================
# CDISC SDTM Domain Models (10 Core Domains)
# =========================================================================


class SDTMDM(BaseModel):
    """CDISC SDTM Demographics (DM) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("DM", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    SUBJID: str = Field(..., description="Subject Identifier")
    RFSTDTC: str | None = Field(None, description="Subject Reference Start Date/Time")
    RFENDTC: str | None = Field(None, description="Subject Reference End Date/Time")
    BRTHDTC: str | None = Field(None, description="Date of Birth")
    AGE: int | float | None = Field(None, description="Age")
    AGEU: str | None = Field("YEARS", description="Age Units")
    SEX: str = Field(..., description="Sex (M, F, U)")
    RACE: str = Field(..., description="Race")
    ETHNIC: str | None = Field(None, description="Ethnic Group")
    ARMCD: str | None = Field(None, description="Planned Arm Code")
    ARM: str | None = Field(None, description="Description of Planned Arm")
    ACTARMCD: str | None = Field(None, description="Actual Arm Code")
    ACTARM: str | None = Field(None, description="Description of Actual Arm")
    COUNTRY: str | None = Field("USA", description="Country")
    DMDTC: str | None = Field(None, description="Date/Time of Collection")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "SUBJID", "SEX", "RACE")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("RFSTDTC", "RFENDTC", "BRTHDTC", "DMDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


class SDTMAE(BaseModel):
    """CDISC SDTM Adverse Events (AE) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("AE", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    AESEQ: int = Field(..., ge=1, description="Sequence Number")
    AETERM: str = Field(..., description="Reported Term for Adverse Event")
    AEDECOD: str | None = Field(None, description="Dictionary-Derived Term / PT")
    AEBODSYS: str | None = Field(None, description="Body System or Organ Class / SOC")
    AESEV: str | None = Field(None, description="Severity / Intensity")
    AESER: str = Field(..., description="Serious Event Flag (Y/N)")
    AEREL: str | None = Field(None, description="Causality / Relationship to Treatment")
    AEOUT: str | None = Field(None, description="Outcome of Adverse Event")
    AESTDTC: str | None = Field(None, description="Start Date/Time of Adverse Event")
    AEENDTC: str | None = Field(None, description="End Date/Time of Adverse Event")
    AESDTH: str | None = Field("N", description="Results in Death")
    AESHOSP: str | None = Field("N", description="Requires/Prolongs Hospitalization")
    AESCONG: str | None = Field("N", description="Congenital Anomaly or Birth Defect")
    AESLIFE: str | None = Field("N", description="Is Life Threatening")
    AESDISAB: str | None = Field("N", description="Persistent/Significant Disability")
    AETOXGR: str | None = Field(None, description="Standard Toxicity Grade")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "AETERM", "AESER")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("AESTDTC", "AEENDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


class SDTMCM(BaseModel):
    """CDISC SDTM Concomitant Medications (CM) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("CM", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    CMSEQ: int = Field(..., ge=1, description="Sequence Number")
    CMTRT: str = Field(..., description="Reported Name of Medication")
    CMDECOD: str | None = Field(None, description="Standardized Medication Name")
    CMCLAS: str | None = Field(None, description="Medication Class")
    CMDOSE: float | None = Field(None, description="Dose per Administration")
    CMDOSEU: str | None = Field(None, description="Dose Units")
    CMDOSFRQ: str | None = Field(None, description="Dose Frequency")
    CMROUTE: str | None = Field(None, description="Route of Administration")
    CMSTDTC: str | None = Field(None, description="Start Date/Time of Medication")
    CMENDTC: str | None = Field(None, description="End Date/Time of Medication")
    CMENRF: str | None = Field(
        None, description="End Relative to Reference Period (ONGOING)"
    )
    CMINDC: str | None = Field(None, description="Indication")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "CMTRT")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("CMSTDTC", "CMENDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


class SDTMVS(BaseModel):
    """CDISC SDTM Vital Signs (VS) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("VS", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    VSSEQ: int = Field(..., ge=1, description="Sequence Number")
    VSTESTCD: str = Field(..., description="Vital Signs Test Short Code")
    VSTEST: str = Field(..., description="Vital Signs Test Name")
    VSCAT: str | None = Field("VITAL SIGNS", description="Category for Vital Signs")
    VSORRES: str | float | int | None = Field(None, description="Original Result")
    VSORRESU: str | None = Field(None, description="Original Units")
    VSSTRESC: str | None = Field(
        None, description="Standardized Result in Character Format"
    )
    VSSTRESN: float | None = Field(
        None, description="Standardized Result in Numeric Format"
    )
    VSSTRESU: str | None = Field(None, description="Standardized Units")
    VSPOS: str | None = Field(None, description="Subject Position")
    VSDTC: str | None = Field(None, description="Date/Time of Measurement")
    VSDY: int | None = Field(None, description="Study Day of Vital Signs")
    VISITNUM: float | int | None = Field(None, description="Visit Number")
    VISIT: str | None = Field(None, description="Visit Name")
    VSBLFL: str | None = Field(None, description="Baseline Flag (Y/N)")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "VSTESTCD", "VSTEST")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("VSDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


class SDTMLB(BaseModel):
    """CDISC SDTM Laboratory Findings (LB) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("LB", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    LBSEQ: int = Field(..., ge=1, description="Sequence Number")
    LBTESTCD: str = Field(..., description="Lab Test Short Code")
    LBTEST: str = Field(..., description="Lab Test Name")
    LBCAT: str | None = Field("CHEMISTRY", description="Category for Lab Test")
    LBORRES: str | None = Field(None, description="Original Result")
    LBORRESU: str | None = Field(None, description="Original Units")
    LBSTRESC: str | None = Field(
        None, description="Standardized Result in Character Format"
    )
    LBSTRESN: float | None = Field(
        None, description="Standardized Result in Numeric Format"
    )
    LBSTRESU: str | None = Field(None, description="Standardized Units")
    LBNRIND: str | None = Field(
        None, description="Reference Range Indicator (LOW, NORMAL, HIGH)"
    )
    LBORNRLO: str | None = Field(
        None, description="Reference Range Lower Limit Original"
    )
    LBORNRHI: str | None = Field(
        None, description="Reference Range Upper Limit Original"
    )
    LBSTNRLO: float | None = Field(
        None, description="Reference Range Lower Limit Standard"
    )
    LBSTNRHI: float | None = Field(
        None, description="Reference Range Upper Limit Standard"
    )
    LBDTC: str | None = Field(None, description="Date/Time of Specimen Collection")
    LBDY: int | None = Field(None, description="Study Day of Specimen Collection")
    VISITNUM: float | int | None = Field(None, description="Visit Number")
    VISIT: str | None = Field(None, description="Visit Name")
    LBBLFL: str | None = Field(None, description="Baseline Flag (Y/N)")
    LOINC: str | None = Field(None, description="LOINC Code")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "LBTESTCD", "LBTEST")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("LBDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


class SDTMEX(BaseModel):
    """CDISC SDTM Exposure (EX) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("EX", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    EXSEQ: int = Field(..., ge=1, description="Sequence Number")
    EXTRT: str = Field(..., description="Name of Actual Treatment")
    EXDOSE: float | None = Field(None, description="Dose per Administration")
    EXDOSU: str | None = Field(None, description="Dose Units")
    EXDOSFRM: str | None = Field(None, description="Dose Form")
    EXDOSFRQ: str | None = Field(None, description="Dosing Frequency")
    EXROUTE: str | None = Field(None, description="Route of Administration")
    EXSTDTC: str | None = Field(None, description="Start Date/Time of Treatment")
    EXENDTC: str | None = Field(None, description="End Date/Time of Treatment")
    VISITNUM: float | int | None = Field(None, description="Visit Number")
    VISIT: str | None = Field(None, description="Visit Name")
    EXLOT: str | None = Field(None, description="Lot Number")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "EXTRT")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("EXSTDTC", "EXENDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


class SDTMDS(BaseModel):
    """CDISC SDTM Disposition (DS) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("DS", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    DSSEQ: int = Field(..., ge=1, description="Sequence Number")
    DSTERM: str = Field(..., description="Reported Term for Disposition Event")
    DSDECOD: str = Field(..., description="Standardized Disposition Term")
    DSCAT: str | None = Field(
        "DISPOSITION EVENT", description="Category for Disposition Event"
    )
    DSSCAT: str | None = Field(None, description="Subcategory for Disposition Event")
    DSSTDTC: str | None = Field(
        None, description="Start Date/Time of Disposition Event"
    )
    EPOCH: str | None = Field("TREATMENT", description="Epoch")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "DSTERM", "DSDECOD")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("DSSTDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


class SDTMTU(BaseModel):
    """CDISC SDTM Tumor Identification (TU) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("TU", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    TUSEQ: int = Field(..., ge=1, description="Sequence Number")
    TULNKID: str = Field(..., description="Link ID / Lesion Identifier")
    TUTESTCD: str = Field(
        "TUMIDENT", description="Tumor Identification Test Short Code"
    )
    TUTEST: str = Field(
        "Tumor Identification", description="Tumor Identification Test Name"
    )
    TUORRES: str = Field(
        ..., description="Classification of Tumor (TARGET / NON-TARGET / NEW)"
    )
    TUSTRESC: str = Field(..., description="Standardized Result in Character Format")
    TUMETHOD: str | None = Field("CT SCAN", description="Method of Identification")
    TULOC: str | None = Field(None, description="Anatomical Location of Tumor")
    VISITNUM: float | int | None = Field(None, description="Visit Number")
    VISIT: str | None = Field(None, description="Visit Name")
    TUDTC: str | None = Field(None, description="Date/Time of Assessment")
    TUEVAL: str | None = Field("INDEPENDENT ASSESSOR", description="Evaluator Role")
    TUEVALID: str | None = Field(None, description="Evaluator Identifier")

    @field_validator(
        "STUDYID",
        "DOMAIN",
        "USUBJID",
        "TULNKID",
        "TUTESTCD",
        "TUTEST",
        "TUORRES",
        "TUSTRESC",
    )
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("TUDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


class SDTMTR(BaseModel):
    """CDISC SDTM Tumor Results / Measurements (TR) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("TR", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    TRSEQ: int = Field(..., ge=1, description="Sequence Number")
    TRLNKID: str | None = Field(None, description="Link ID / Lesion Identifier")
    TRTESTCD: str = Field(
        ..., description="Tumor Result Test Short Code (LDIAM, SUMDIR)"
    )
    TRTEST: str = Field(..., description="Tumor Result Test Name")
    TRORRES: str | None = Field(None, description="Original Result")
    TRORRESU: str | None = Field("mm", description="Original Units")
    TRSTRESC: str | None = Field(
        None, description="Standardized Result in Character Format"
    )
    TRSTRESN: float | None = Field(
        None, description="Standardized Result in Numeric Format"
    )
    TRSTRESU: str | None = Field("mm", description="Standardized Units")
    TRMETHOD: str | None = Field("CT SCAN", description="Measurement Method")
    VISITNUM: float | int | None = Field(None, description="Visit Number")
    VISIT: str | None = Field(None, description="Visit Name")
    TRDTC: str | None = Field(None, description="Date/Time of Measurement")
    TREVAL: str | None = Field("INDEPENDENT ASSESSOR", description="Evaluator Role")
    TREVALID: str | None = Field(None, description="Evaluator Identifier")

    @field_validator("STUDYID", "DOMAIN", "USUBJID", "TRTESTCD", "TRTEST")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("TRDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


class SDTMRS(BaseModel):
    """CDISC SDTM Disease Response (RS) domain."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    DOMAIN: str = Field("RS", description="Domain Abbreviation")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    RSSEQ: int = Field(..., ge=1, description="Sequence Number")
    RSTESTCD: str = Field("OVRLRESP", description="Response Assessment Test Short Code")
    RSTEST: str = Field("Overall Response", description="Response Assessment Test Name")
    RSORRES: str = Field(
        ..., description="Overall Response Classification (CR, PR, SD, PD, NE)"
    )
    RSSTRESC: str = Field(..., description="Standardized Result in Character Format")
    RSMETHOD: str | None = Field(
        "RECIST 1.1", description="Assessment Criteria / Method"
    )
    VISITNUM: float | int | None = Field(None, description="Visit Number")
    VISIT: str | None = Field(None, description="Visit Name")
    RSDTC: str | None = Field(None, description="Date/Time of Response Assessment")
    RSEVAL: str | None = Field("INDEPENDENT ASSESSOR", description="Evaluator Role")
    RSEVALID: str | None = Field(None, description="Evaluator Identifier")
    RSEVALSTAT: str | None = Field(None, description="Evaluation Status")

    @field_validator(
        "STUDYID", "DOMAIN", "USUBJID", "RSTESTCD", "RSTEST", "RSORRES", "RSSTRESC"
    )
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()

    @field_validator("RSDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_iso_dtc(v)


# =========================================================================
# CDISC ADaM Dataset Models (4 Core Datasets)
# =========================================================================


class ADaMADSL(BaseModel):
    """CDISC ADaM Subject-Level Analysis Dataset (ADSL)."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    SUBJID: str = Field(..., description="Subject Identifier")
    SITEID: str = Field(..., description="Site Identifier")
    AGE: int | float | None = Field(None, description="Age")
    AGEU: str | None = Field("YEARS", description="Age Units")
    SEX: str = Field(..., description="Sex")
    RACE: str = Field(..., description="Race")
    ETHNIC: str | None = Field(None, description="Ethnic Group")
    ARM: str = Field(..., description="Description of Planned Arm")
    ARMCD: str = Field(..., description="Planned Arm Code")
    ACTARM: str = Field(..., description="Description of Actual Arm")
    ACTARMCD: str = Field(..., description="Actual Arm Code")
    TRT01P: str = Field(..., description="Planned Treatment for Period 01")
    TRT01A: str = Field(..., description="Actual Treatment for Period 01")
    TRTSDT: str | None = Field(None, description="Treatment Start Date (ISO8601)")
    TRTEDT: str | None = Field(None, description="Treatment End Date (ISO8601)")
    RANDT: str | None = Field(None, description="Randomization Date (ISO8601)")
    EOSDT: str | None = Field(None, description="End of Study Date (ISO8601)")
    DTHDT: str | None = Field(None, description="Death Date (ISO8601)")
    DTHFL: str = Field("N", description="Death Flag (Y/N)")
    SAFFL: str = Field("N", description="Safety Population Flag (Y/N)")
    ITTFL: str = Field("N", description="Intent-To-Treat Flag (Y/N)")
    FASFL: str = Field("N", description="Full Analysis Set Flag (Y/N)")
    COMPLFL: str = Field("N", description="Completed Study Flag (Y/N)")
    HEIGHTBL: float | None = Field(None, description="Baseline Height (cm)")
    WEIGHTBL: float | None = Field(None, description="Baseline Weight (kg)")
    BMIBL: float | None = Field(None, description="Baseline BMI (kg/m^2)")

    @field_validator(
        "STUDYID",
        "USUBJID",
        "SUBJID",
        "SITEID",
        "SEX",
        "RACE",
        "ARM",
        "ACTARM",
        "TRT01P",
        "TRT01A",
    )
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()


class ADaMADAE(BaseModel):
    """CDISC ADaM Adverse Events Analysis Dataset (ADAE)."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    SUBJID: str = Field(..., description="Subject Identifier")
    SITEID: str = Field(..., description="Site Identifier")
    ARM: str = Field(..., description="Description of Planned Arm")
    ACTARM: str = Field(..., description="Description of Actual Arm")
    TRT01A: str = Field(..., description="Actual Treatment for Period 01")
    SAFFL: str = Field("Y", description="Safety Population Flag")
    AESEQ: int = Field(..., ge=1, description="Sequence Number")
    AETERM: str = Field(..., description="Reported Term for Adverse Event")
    AEDECOD: str | None = Field(None, description="Dictionary-Derived Term / PT")
    AEBODSYS: str | None = Field(None, description="System Organ Class / SOC")
    AESEV: str | None = Field(None, description="Severity / Intensity")
    AESEVN: int | None = Field(
        None, description="Severity / Intensity (N: 1=Mild, 2=Mod, 3=Sev)"
    )
    AESER: str = Field(..., description="Serious Adverse Event Flag (Y/N)")
    AEREL: str | None = Field(None, description="Causality / Relationship to Treatment")
    AEOUT: str | None = Field(None, description="Outcome of Adverse Event")
    AESTDTC: str | None = Field(None, description="Start Date/Time (SDTM)")
    AEENDTC: str | None = Field(None, description="End Date/Time (SDTM)")
    ASTDT: str | None = Field(None, description="Analysis Start Date (ISO8601)")
    AENDT: str | None = Field(None, description="Analysis End Date (ISO8601)")
    ASTDY: int | None = Field(None, description="Analysis Start Relative Day")
    AENDY: int | None = Field(None, description="Analysis End Relative Day")
    ADURN: int | None = Field(None, description="Analysis Duration (Days)")
    ADURU: str | None = Field("DAYS", description="Analysis Duration Units")
    TRTEMFL: str = Field("N", description="Treatment Emergent Flag (Y/N)")
    PREFL: str = Field("N", description="Pre-treatment Flag (Y/N)")

    @field_validator("STUDYID", "USUBJID", "AETERM", "AESER")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()


class ADaMADTTE(BaseModel):
    """CDISC ADaM Time-to-Event Analysis Dataset (ADTTE)."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    SUBJID: str = Field(..., description="Subject Identifier")
    SITEID: str = Field(..., description="Site Identifier")
    ARM: str = Field(..., description="Description of Planned Arm")
    ACTARM: str = Field(..., description="Description of Actual Arm")
    PARAMCD: str = Field(
        ..., description="Parameter Code (e.g., TTDEATH, TTPROG, TTTOX, TTDISC)"
    )
    PARAM: str = Field(..., description="Parameter Description")
    STARTDT: str = Field(..., description="Time-to-Event Origin Date (ISO8601)")
    ADT: str = Field(..., description="Analysis Date (ISO8601)")
    AVAL: float = Field(
        ..., description="Analysis Value (Time to event/censoring in days)"
    )
    AVALU: str = Field("DAYS", description="Analysis Value Units")
    CNSR: int = Field(
        ..., ge=0, le=1, description="Censoring Flag (0=Event, 1=Censored)"
    )
    EVNTDESC: str = Field(..., description="Event or Censoring Description")
    SAFFL: str = Field("Y", description="Safety Population Flag")
    ITTFL: str = Field("Y", description="Intent-To-Treat Flag")

    @field_validator(
        "STUDYID", "USUBJID", "PARAMCD", "PARAM", "STARTDT", "ADT", "EVNTDESC"
    )
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()


class ADaMBDS(BaseModel):
    """CDISC ADaM Basic Data Structure (BDS) for lab/vitals observations."""

    model_config = ConfigDict(extra="allow")

    STUDYID: str = Field(..., description="Study Identifier")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    SUBJID: str = Field(..., description="Subject Identifier")
    SITEID: str = Field(..., description="Site Identifier")
    ARM: str = Field(..., description="Description of Planned Arm")
    ACTARM: str = Field(..., description="Description of Actual Arm")
    TRTSDT: str | None = Field(None, description="Treatment Start Date (ISO8601)")
    PARAMCD: str = Field(..., description="Parameter Code")
    PARAM: str = Field(..., description="Parameter Description")
    PARAMN: int | None = Field(None, description="Parameter Number")
    AVAL: float | None = Field(None, description="Analysis Value (Numeric)")
    AVALC: str | None = Field(None, description="Analysis Value (Character)")
    BASE: float | None = Field(None, description="Baseline Value")
    CHG: float | None = Field(None, description="Change from Baseline (AVAL - BASE)")
    PCHG: float | None = Field(
        None, description="Percent Change from Baseline ((AVAL - BASE)/BASE * 100)"
    )
    ADTC: str | None = Field(None, description="Analysis Date/Time (ISO8601)")
    ADY: int | None = Field(None, description="Analysis Relative Day")
    AVISIT: str | None = Field(None, description="Analysis Visit Name")
    AVISITN: float | int | None = Field(None, description="Analysis Visit Number")
    ABLFL: str | None = Field(None, description="Analysis Baseline Flag (Y/N)")
    ANRIND: str | None = Field(
        None, description="Analysis Reference Range Indicator (LOW, NORMAL, HIGH)"
    )
    BNRIND: str | None = Field(None, description="Baseline Reference Range Indicator")

    @field_validator("STUDYID", "USUBJID", "PARAMCD", "PARAM")
    @classmethod
    def validate_required_strings(cls, v: str) -> str:
        if not isinstance(v, str) or not v.strip():
            raise ValueError("Field cannot be empty or whitespace.")
        return v.strip()


# =========================================================================
# CDISC Dataset-JSON 1.1 / 2.0 Models
# =========================================================================


class DatasetJSONItem(BaseModel):
    """Metadata for an individual variable in CDISC Dataset-JSON 1.1/2.0."""

    model_config = ConfigDict(extra="allow")

    OID: str | None = Field(None, description="Item OID")
    name: str = Field(..., description="Variable Name (e.g. USUBJID)")
    label: str = Field(..., description="Variable Label")
    type: str = Field(
        ...,
        description="Data Type (string, integer, float, double, decimal, boolean, date, datetime)",
    )
    length: int | None = Field(None, description="Max Length")
    displayFormat: str | None = Field(None, description="Display Format")
    keySequence: int | None = Field(None, description="Key Sequence Number")
    mandatory: bool | str | None = Field(None, description="Mandatory Flag")


class DatasetJSONItemGroup(BaseModel):
    """ItemGroup container in Dataset-JSON clinicalData/referenceData."""

    model_config = ConfigDict(extra="allow")

    itemGroupOID: str = Field(..., description="ItemGroup OID identifier (e.g. IG.DM)")
    name: str = Field(..., description="Dataset Name (e.g. DM)")
    label: str = Field(..., description="Dataset Label")
    records: int = Field(..., ge=0, description="Record Count")
    items: list[DatasetJSONItem] = Field(..., description="List of variable metadata")
    itemData: list[list[Any]] = Field(..., description="2D Matrix of row records")


class DatasetJSONClinicalData(BaseModel):
    """ClinicalData container in Dataset-JSON document."""

    model_config = ConfigDict(extra="allow")

    studyOID: str = Field(..., description="Study OID")
    metaDataVersionOID: str = Field(..., description="Metadata Version OID")
    metaDataRef: str | None = Field(None, description="Reference to Define-XML")
    itemGroupData: dict[str, DatasetJSONItemGroup] = Field(
        ..., description="Dictionary of ItemGroup datasets keyed by ItemGroupOID"
    )


class DatasetJSONDocument(BaseModel):
    """Root CDISC Dataset-JSON 1.1 / 2.0 document envelope."""

    model_config = ConfigDict(extra="allow", populate_by_name=True)

    creationDateTime: str = Field(
        default_factory=lambda: datetime.now(UTC).isoformat().replace("+00:00", "Z"),
        alias="datasetJSONCreationDateTime",
        description="Document Creation Timestamp ISO 8601",
    )
    datasetJSONVersion: str = Field(
        "1.1.0", description="Specification version (1.0.0, 1.1.0, 2.0.0)"
    )
    fileOID: str = Field(..., description="Unique File OID")
    dbLastModifiedDateTime: str | None = Field(
        None, description="Database Last Modified Timestamp"
    )
    originator: str = Field(
        "Cadence Clinical CDR Engine", description="Dataset Originator"
    )
    sourceSystem: str = Field("Cadence Clinical", description="Source System")
    sourceSystemVersion: str = Field("2.1.0", description="Source System Version")
    clinicalData: DatasetJSONClinicalData = Field(
        ..., description="Clinical Data Container"
    )


# =========================================================================
# Define-XML 2.1 Compiler Models
# =========================================================================


class DefineXMLCompilationRequest(BaseModel):
    """Request specification for compiling a CDISC Define-XML v2.1 document."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., min_length=1, description="Study Identifier")
    study_name: str | None = Field(None, description="Human-readable Study Name")
    protocol_name: str | None = Field(None, description="Protocol Name")
    metadata_version_oid: str = Field(
        "MDV.CDISC.001", description="Metadata Version OID"
    )
    standard_name: str = Field(
        "SDTM-IG", description="Standard Name (SDTM-IG / ADaM-IG)"
    )
    standard_version: str = Field(
        "3.4", description="Standard Version (3.4 / 1.3 / 2.1)"
    )
    domains: list[str] = Field(
        default_factory=lambda: [
            "DM",
            "AE",
            "CM",
            "VS",
            "LB",
            "EX",
            "DS",
            "TU",
            "TR",
            "RS",
        ],
        description="SDTM domains to include in Define-XML",
    )
    adam_datasets: list[str] = Field(
        default_factory=lambda: ["ADSL", "ADAE", "ADTTE", "BDS"],
        description="ADaM datasets to include in Define-XML",
    )
    include_vlm: bool = Field(True, description="Include Value Level Metadata (VLM)")
    include_where_clauses: bool = Field(
        True, description="Include WhereClause definitions"
    )
    include_origins: bool = Field(
        True, description="Include detailed Origin annotations"
    )


class DefineXMLValidationResult(BaseModel):
    """Validation outcome for compiled CDISC Define-XML v2.1."""

    model_config = ConfigDict(extra="forbid")

    is_valid: bool = Field(
        ..., description="Whether XML conforms to Define-XML 2.1 schema"
    )
    errors: list[str] = Field(default_factory=list, description="Validation errors")
    warnings: list[str] = Field(default_factory=list, description="Validation warnings")
    item_group_count: int = Field(0, description="Number of ItemGroupDef elements")
    item_def_count: int = Field(0, description="Number of ItemDef elements")
    value_level_metadata_count: int = Field(
        0, description="Number of ValueListDef elements"
    )
    where_clause_count: int = Field(0, description="Number of WhereClauseDef elements")
    codelist_count: int = Field(0, description="Number of CodeList elements")
    method_count: int = Field(0, description="Number of MethodDef derivation elements")


# =========================================================================
# Pipeline Execution & Reporting Models
# =========================================================================


class PipelineExecutionStatus(StrEnum):
    """Execution status for the continuous CDR transformation pipeline."""

    PENDING = "PENDING"
    RUNNING = "RUNNING"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"


class PipelineExecutionResult(BaseModel):
    """Comprehensive execution manifest for a CDISC SDTM/ADaM & Dataset-JSON/Define-XML run."""

    model_config = ConfigDict(extra="forbid")

    execution_id: str = Field(..., description="Unique Pipeline Execution Identifier")
    study_id: str = Field(..., description="Study Identifier")
    snapshot_id: str | None = Field(
        None, description="Linked Snapshot ID if snapshot-pinned"
    )
    status: PipelineExecutionStatus = Field(..., description="Execution Status")
    started_at: datetime = Field(..., description="Execution Start Timestamp")
    completed_at: datetime | None = Field(
        None, description="Execution Completion Timestamp"
    )
    sdtm_counts: dict[str, int] = Field(
        default_factory=dict, description="Record counts per SDTM domain"
    )
    adam_counts: dict[str, int] = Field(
        default_factory=dict, description="Record counts per ADaM dataset"
    )
    total_sdtm_records: int = Field(0, description="Total SDTM records generated")
    total_adam_records: int = Field(0, description="Total ADaM records derived")
    dataset_json_manifest: dict[str, Any] = Field(
        default_factory=dict, description="Dataset-JSON metadata manifest"
    )
    define_xml_summary: dict[str, Any] = Field(
        default_factory=dict, description="Define-XML 2.1 compilation summary"
    )
    validation_summary: dict[str, Any] = Field(
        default_factory=dict, description="Conformance validation results"
    )
    errors: list[str] = Field(
        default_factory=list, description="Execution errors if failed"
    )
