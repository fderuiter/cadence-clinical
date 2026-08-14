# ruff: noqa: N815
from datetime import datetime
from typing import Any

from pydantic import AliasChoices, BaseModel, ConfigDict, Field, field_validator


class VariableMetadata(BaseModel):
    """Metadata representing a single SDTM variable in Dataset-JSON."""

    name: str = Field(..., description="Variable name (e.g., 'USUBJID')")
    label: str = Field(
        ..., description="Variable label (e.g., 'Unique Subject Identifier')"
    )
    type: str = Field(
        ...,
        description="Data type of the variable (e.g., 'string', 'integer', 'float', 'double')",
    )
    length: int | None = Field(None, description="Variable length limit")
    format: str | None = Field(None, description="Display format of the variable")
    keySequence: int | None = Field(
        None, description="Sort order of the key variable if part of a unique key"
    )

    @field_validator("type")
    @classmethod
    def validate_type(cls, v: str) -> str:
        valid_types = {
            "string",
            "integer",
            "float",
            "double",
            "decimal",
            "boolean",
            "date",
            "datetime",
        }
        if v.lower() not in valid_types:
            raise ValueError(
                f"Type '{v}' is not a valid Dataset-JSON type. Must be one of {valid_types}"
            )
        return v.lower()


class SUPPRecord(BaseModel):
    """Represents a Supplemental Qualifier (SUPP--) record as defined in SDTM."""

    STUDYID: str = Field(..., description="Study Identifier")
    RDOMAIN: str = Field(..., description="Related Domain (e.g., 'DM', 'AE')")
    USUBJID: str = Field(..., description="Unique Subject Identifier")
    IDVAR: str = Field(
        ..., description="Identifying Variable (e.g., 'AESEQ', or empty string)"
    )
    IDVARVAL: str = Field(
        ..., description="Identifying Variable Value (e.g., '1', or empty string)"
    )
    QNAM: str = Field(..., description="Qualifier Variable Name (e.g., 'AELOC')")
    QLABEL: str = Field(
        ..., description="Qualifier Variable Label (e.g., 'Anatomical Location')"
    )
    QVAL: str = Field(..., description="Qualifier Value")
    QEVAL: str = Field("", description="Qualifier Evaluator (defaults to empty string)")

    def to_row(self, variable_names: list[str]) -> list[Any]:
        """Converts the SUPPRecord into an ordered list of values based on variable metadata names."""
        record_dict = self.model_dump()
        return [record_dict.get(name, "") for name in variable_names]


class DatasetJSONItemGroup(BaseModel):
    """Represents an itemGroupData object inside CDISC Dataset-JSON clinicalData/referenceData."""

    itemGroupOID: str = Field(..., description="ItemGroup OID identifier")
    records: int = Field(..., description="Number of rows/records in the dataset")
    name: str = Field(..., description="Dataset name (e.g., 'DM')")
    label: str = Field(..., description="Dataset label (e.g., 'Demographics')")
    items: list[VariableMetadata] = Field(
        ..., description="List of ordered variables metadata"
    )
    itemData: list[list[Any]] = Field(
        ...,
        description="List of rows, where each row is an ordered list of values corresponding to the items",
    )

    @field_validator("itemData")
    @classmethod
    def validate_row_lengths(cls, item_data: list[list[Any]], info) -> list[list[Any]]:
        return item_data


class ClinicalData(BaseModel):
    """ClinicalData container for CDISC Dataset-JSON."""

    studyOID: str = Field(
        ..., description="Unique identifier for the study (e.g., 'STUDY.001')"
    )
    metaDataVersionOID: str = Field(
        ..., description="Metadata version identifier (e.g., 'MDV.001')"
    )
    metaDataRef: str | None = Field(
        None, description="External Define-XML metadata reference"
    )
    itemGroupData: dict[str, DatasetJSONItemGroup] = Field(
        ..., description="Mapping of group names (e.g., 'IG.DM') to their datasets"
    )


class ReferenceData(BaseModel):
    """ReferenceData container for CDISC Dataset-JSON (when reference data is utilized instead of clinical data)."""

    studyOID: str = Field(..., description="Unique identifier for the study")
    metaDataVersionOID: str = Field(..., description="Metadata version identifier")
    itemGroupData: dict[str, DatasetJSONItemGroup] = Field(
        ..., description="Mapping of group names to their datasets"
    )


class DatasetJSON(BaseModel):
    """Root model representing a CDISC Dataset-JSON document compliant with Pydantic v2."""

    model_config = ConfigDict(populate_by_name=True)

    datasetJSONCreationDateTime: str = Field(
        default_factory=lambda: datetime.utcnow().isoformat() + "Z",
        serialization_alias="datasetJSONCreationDateTime",
        validation_alias=AliasChoices(
            "datasetJSONCreationDateTime", "creationDateTime"
        ),
        description="ISO 8601 creation timestamp",
    )
    datasetJSONVersion: str = Field(
        "1.0.0", description="The Dataset-JSON specification version"
    )
    fileOID: str | None = Field(None, description="Unique identifier for this file")
    asOfDateTime: str | None = Field(None, description="As of timestamp")
    dbLastModifiedDateTime: str | None = Field(
        None, description="Optional source-data last-modified timestamp header"
    )
    originator: str | None = Field(None, description="Originator of the data")
    sourceSystem: str | None = Field(None, description="Generating system")
    sourceSystemVersion: str | None = Field(
        None, description="Generating system version"
    )
    clinicalData: ClinicalData | None = Field(None, description="Clinical data block")
    referenceData: ReferenceData | None = Field(
        None, description="Reference data block"
    )

    @field_validator("datasetJSONCreationDateTime")
    @classmethod
    def validate_timestamp(cls, v: str) -> str:
        try:
            clean_v = v.rstrip("Z")
            datetime.fromisoformat(clean_v)
        except ValueError:
            raise ValueError(
                "creationDateTime must be a valid ISO 8601 datetime string"
            )
        return v


# --- INPUT JSON DTOS ---


class SubjectDTO(BaseModel):
    id: str
    subject_id: str
    study_id: str
    site_id: str | None = None
    usubjid: str | None = None
    demographics: dict[str, Any] | None = None
    encrypted_demographics: str | None = None
    rfstdtc: str | None = None
    rfendtc: str | None = None
    is_deleted: bool | None = False
    enrollment_index: int | None = None
    randomization_date: Any | None = None
    randt: Any | None = None
    end_of_study_date: Any | None = None
    eosdt: Any | None = None
    death_date: Any | None = None
    dthdtc: Any | None = None
    actarm: str | None = None
    ACTARM: str | None = None


class ObservationDTO(BaseModel):
    id: str
    subject_id: str
    study_id: str
    site_id: str | None = None
    visit_id: str | None = None
    domain: str | None = None
    observation_date: Any | None = None
    test_code: str | None = None
    test_name: str | None = None
    value: Any | None = None
    value_string: str | None = None
    unit: str | None = None
    normalized_value: Any | None = None
    normalized_unit: str | None = None
    is_outlier: bool | None = False
    is_sdv_verified: bool | None = False
    sdv_verified_by: str | None = None
    sdv_verified_at: Any | None = None
    page_id: str | None = None
    lab_source: str | None = None
    lab_site_id: str | None = None
    lab_indicator: str | None = None
    lab_out_of_range: bool | None = False
    matched_normal_bounds: str | None = None
    protocol_version_tag: str | None = None
    protocol_version_index: int | None = None
    provenance: list[dict[str, Any]] | None = None
    is_deleted: bool | None = False


class VisitDTO(BaseModel):
    id: str
    subject_id: str
    study_id: str
    site_id: str | None = None
    visit_name: str | None = None
    visit_date: Any | None = None
    is_deleted: bool | None = False


class MigrationRuleDTO(BaseModel):
    id: str
    study_id: str
    source_version: str
    target_version: str
    rule_type: str
    source_field: str | None = None
    target_field: str | None = None
    default_value_string: str | None = None
    default_value_float: float | None = None
    is_deleted: bool | None = False


class SDTMRequest(BaseModel):
    study_id: str
    subjects: list[SubjectDTO]
    observations: list[ObservationDTO]
    visits: list[VisitDTO] = []
    migration_rules: list[MigrationRuleDTO] = []
    target_version: str = "1.0"


class ADaMRequest(BaseModel):
    study_id: str
    subjects: list[SubjectDTO]
    observations: list[ObservationDTO]
    visits: list[VisitDTO] = []
    migration_rules: list[MigrationRuleDTO] = []
    target_version: str = "1.0"


class BundleRequest(BaseModel):
    study_id: str
    subjects: list[SubjectDTO]
    observations: list[ObservationDTO]
    visits: list[VisitDTO] = []
    migration_rules: list[MigrationRuleDTO] = []
    target_version: str = "1.0"
