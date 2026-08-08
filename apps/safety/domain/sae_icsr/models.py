import re
from typing import Any

from pydantic import BaseModel, Field, field_validator, model_validator

DTC_REGEX = re.compile(
    r"^\d{4}(-\d{2}(-\d{2}(T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}(:\d{2})?)?)?)?)?$"
)


def validate_dtc_format(val: str | None) -> str | None:
    if val is None:
        return None
    if not DTC_REGEX.match(val):
        raise ValueError(
            f"Value '{val}' does not conform to CDISC DTC or ISO 8601 date-time format."
        )
    return val


def normalize_severity_val(val: Any) -> str:
    if val is None:
        raise ValueError("Severity value cannot be None")
    cleaned = str(val).strip().upper()
    if cleaned in {"MILD", "GRADE 1", "LOW", "1"}:
        return "MILD"
    if cleaned in {"MODERATE", "GRADE 2", "MEDIUM", "2"}:
        return "MODERATE"
    if cleaned in {"SEVERE", "GRADE 3", "GRADE 4", "GRADE 5", "HIGH", "3", "4", "5"}:
        return "SEVERE"
    raise ValueError(f"Value '{val}' is not a valid or normalizable AE Severity value.")


def normalize_seriousness_val(val: Any) -> str:
    if val is None:
        raise ValueError("Seriousness value cannot be None")
    if isinstance(val, bool):
        return "Y" if val else "N"
    cleaned = str(val).strip().upper()
    if cleaned in {"Y", "YES", "TRUE", "1"}:
        return "Y"
    if cleaned in {"N", "NO", "FALSE", "0"}:
        return "N"
    raise ValueError(
        f"Value '{val}' is not a valid or normalizable seriousness code ('Y' or 'N')."
    )


class VersionedModel(BaseModel):
    """Base class for models supporting follow-up versions with version tracking."""

    version_index: int = Field(1, description="Version of this record (must be >= 1)")
    reason_for_change: str | None = Field(
        None, description="Reason for change in follow-up version"
    )

    @model_validator(mode="after")
    def validate_version_metadata(self) -> VersionedModel:
        if self.version_index < 1:
            raise ValueError("version_index must be greater than or equal to 1")
        if self.version_index > 1:
            if not self.reason_for_change or not self.reason_for_change.strip():
                raise ValueError(
                    "reason_for_change is required and must be non-empty for follow-up versions (version_index > 1)"
                )
        return self


class MedDRACoding(BaseModel):
    """
    Represents MedDRA coding hierarchies consistent with the execution-service coding contract.
    """

    llt_code: str = Field(..., description="Lowest Level Term Code")
    llt_name: str = Field(..., description="Lowest Level Term Name")
    pt_code: str = Field(..., description="Preferred Term Code")
    pt_name: str = Field(..., description="Preferred Term Name")
    hlt_code: str = Field(..., description="High Level Term Code")
    hlt_name: str = Field(..., description="High Level Term Name")
    hlgt_code: str = Field(..., description="High Level Group Term Code")
    hlgt_name: str = Field(..., description="High Level Group Term Name")
    soc_code: str = Field(..., description="System Organ Class Code")
    soc_name: str = Field(..., description="System Organ Class Name")
    primary_soc_flag: str | None = Field(
        None, description="Primary SOC flag ('Y' or 'N')"
    )
    score: float = Field(1.0, description="Match score")

    @field_validator("primary_soc_flag", mode="before")
    @classmethod
    def validate_primary_soc(cls, v: str | None) -> str | None:
        if v is None:
            return None
        cleaned = str(v).strip().upper()
        if cleaned in {"Y", "YES", "TRUE", "1"}:
            return "Y"
        if cleaned in {"N", "NO", "FALSE", "0"}:
            return "N"
        raise ValueError(f"Invalid primary_soc_flag: {v}. Must be 'Y' or 'N'.")


class SeriousAdverseEvent(VersionedModel):
    """
    Model representing SDTM AE/SAE fields with versioning metadata.
    """

    subject_key: str = Field(
        ..., description="Unique subject identifier (e.g., USUBJID)"
    )
    AETERM: str = Field(..., description="Adverse Event Term")
    AESTDTC: str = Field(..., description="Adverse Event Start Date/Time")
    AEENDTC: str | None = Field(None, description="Adverse Event End Date/Time")
    AESEV: str = Field(
        ..., description="Adverse Event Severity (MILD, MODERATE, SEVERE)"
    )
    AESER: str = Field(..., description="Serious Adverse Event Flag ('Y' or 'N')")
    AEREL: str | None = Field(None, description="Causality / Relatedness to study drug")
    AEOUT: str | None = Field(None, description="Outcome of Adverse Event")
    AESEQ: int | None = Field(None, description="Sequence Number (must be >= 1)")
    meddra_coding: MedDRACoding | None = Field(
        None, description="MedDRA coding details"
    )

    @field_validator("AESTDTC", "AEENDTC")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_dtc_format(v)

    @field_validator("AESEV", mode="before")
    @classmethod
    def validate_severity(cls, v: Any) -> str:
        return normalize_severity_val(v)

    @field_validator("AESER", mode="before")
    @classmethod
    def validate_seriousness(cls, v: Any) -> str:
        return normalize_seriousness_val(v)

    @field_validator("AESEQ")
    @classmethod
    def validate_sequence(cls, v: int | None) -> int | None:
        if v is not None and v < 1:
            raise ValueError("AESEQ must be greater than or equal to 1")
        return v

    @model_validator(mode="after")
    def validate_date_sequence(self) -> SeriousAdverseEvent:
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


class ICSRHeader(BaseModel):
    """ICSR transmission header details according to ICH E2B(R3)."""

    sender_organization: str = Field(..., description="Sender organization identifier")
    receiver_organization: str = Field(
        ..., description="Receiver organization identifier"
    )
    message_type: str = Field("ICHICSR", description="Message type")
    transmission_date: str = Field(
        ..., description="Transmission date/time (DTC format)"
    )
    message_id: str = Field(..., description="Unique message ID")

    @field_validator("transmission_date")
    @classmethod
    def validate_transmission_date(cls, v: str) -> str:
        validate_dtc_format(v)
        return v


class ICSRReportIdentifiers(BaseModel):
    """ICSR report-level identification details."""

    worldwide_unique_case_id: str = Field(
        ..., description="Worldwide unique case ID (e.g., ICH worldwide unique ID)"
    )
    local_report_id: str | None = Field(None, description="Local report identifier")
    first_sender_type: str | None = Field(None, description="Type of first sender")


class ICSRPatient(BaseModel):
    """ICSR patient characteristics block."""

    patient_id: str = Field(..., description="Patient identifier / subject key")
    sex: str = Field(..., description="Gender code (normalizes to 'M', 'F', 'U')")
    age: float | None = Field(None, description="Age of the patient")
    age_unit: str | None = Field(None, description="Age unit (e.g. YEAR, MONTH, DAY)")
    birth_date: str | None = Field(None, description="Date of birth")

    @field_validator("sex", mode="before")
    @classmethod
    def validate_sex(cls, v: Any) -> str:
        if v is None:
            raise ValueError("Sex cannot be None")
        cleaned = str(v).strip().upper()
        if cleaned in {"M", "MALE", "1"}:
            return "M"
        if cleaned in {"F", "FEMALE", "2"}:
            return "F"
        if cleaned in {"U", "UNKNOWN", "9", "NOT REPORTED", "NOT_REPORTED"}:
            return "U"
        raise ValueError(
            f"Invalid sex value: {v}. Must be normalizable to 'M', 'F', or 'U'."
        )

    @field_validator("age")
    @classmethod
    def validate_age(cls, v: float | None) -> float | None:
        if v is not None and v < 0:
            raise ValueError("Age cannot be negative")
        return v

    @field_validator("age_unit")
    @classmethod
    def validate_age_unit(cls, v: str | None) -> str | None:
        if v is None:
            return None
        cleaned = v.strip().upper()
        valid_units = {
            "YEAR",
            "YEARS",
            "MONTH",
            "MONTHS",
            "WEEK",
            "WEEKS",
            "DAY",
            "DAYS",
            "DECADE",
            "DECADES",
            "HOUR",
            "HOURS",
        }
        if cleaned not in valid_units:
            raise ValueError(f"Invalid age unit: {v}. Must be one of {valid_units}")
        if cleaned.endswith("S"):
            cleaned = cleaned[:-1]
        return cleaned

    @field_validator("birth_date")
    @classmethod
    def validate_birth_date(cls, v: str | None) -> str | None:
        return validate_dtc_format(v)


class ICSRReactionEvent(BaseModel):
    """ICSR reaction/event block with MedDRA coding."""

    reaction_term: str = Field(..., description="Reaction or event term as reported")
    meddra_coding: MedDRACoding | None = Field(
        None, description="MedDRA coding details"
    )
    start_date: str | None = Field(None, description="Start date of reaction")
    end_date: str | None = Field(None, description="End date of reaction")
    outcome: str | None = Field(
        None, description="Outcome of reaction (e.g. RECOVERED, RESOLVING, FATAL)"
    )
    seriousness_death: str = Field("N", description="Results in death ('Y' or 'N')")
    seriousness_life_threatening: str = Field(
        "N", description="Is life-threatening ('Y' or 'N')"
    )
    seriousness_hospitalization: str = Field(
        "N", description="Requires/prolongs hospitalization ('Y' or 'N')"
    )
    seriousness_disability: str = Field(
        "N", description="Results in persistent/significant disability ('Y' or 'N')"
    )
    seriousness_congenital_anomaly: str = Field(
        "N", description="Results in congenital anomaly/birth defect ('Y' or 'N')"
    )
    seriousness_other_medically_important: str = Field(
        "N", description="Other medically important condition ('Y' or 'N')"
    )

    @field_validator("start_date", "end_date")
    @classmethod
    def validate_dates(cls, v: str | None) -> str | None:
        return validate_dtc_format(v)

    @field_validator(
        "seriousness_death",
        "seriousness_life_threatening",
        "seriousness_hospitalization",
        "seriousness_disability",
        "seriousness_congenital_anomaly",
        "seriousness_other_medically_important",
        mode="before",
    )
    @classmethod
    def validate_seriousness_flags(cls, v: Any) -> str:
        return normalize_seriousness_val(v)


class ICSRSuspectDrug(BaseModel):
    """ICSR suspect drug block."""

    drug_name: str = Field(..., description="Name of suspect drug")
    active_substance_name: str | None = Field(None, description="Active substance name")
    dosage_text: str | None = Field(
        None, description="Dosage and administration text description"
    )
    route_of_administration: str | None = Field(
        None, description="Route of administration"
    )
    action_taken_with_drug: str | None = Field(
        None, description="Action taken with the drug"
    )
    drug_role: str = Field(
        "SUSPECT",
        description="Role of the drug (e.g. SUSPECT, CONCOMITANT, INTERACTING)",
    )

    @field_validator("drug_role")
    @classmethod
    def validate_drug_role(cls, v: str) -> str:
        cleaned = v.strip().upper()
        valid_roles = {"SUSPECT", "CONCOMITANT", "INTERACTING", "DRUG NOT ADMINISTERED"}
        if cleaned not in valid_roles:
            raise ValueError(f"Invalid drug role: {v}. Must be one of {valid_roles}")
        return cleaned


class IndividualCaseSafetyReport(VersionedModel):
    """
    Root Individual Case Safety Report (ICSR) according to ICH E2B(R3).
    """

    header: ICSRHeader = Field(..., description="ICSR message header")
    report_identifiers: ICSRReportIdentifiers = Field(
        ..., description="ICSR report identifiers"
    )
    patient: ICSRPatient = Field(..., description="Patient details")
    reactions: list[ICSRReactionEvent] = Field(
        default_factory=list, description="List of reactions/events"
    )
    suspect_drugs: list[ICSRSuspectDrug] = Field(
        default_factory=list, description="List of suspect drugs"
    )
