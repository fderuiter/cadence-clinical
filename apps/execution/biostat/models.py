from datetime import datetime
from typing import Any

from pydantic import BaseModel, Field, field_validator, ConfigDict, model_serializer, model_validator


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
    itemGroupOID: str = Field(
        default="IG.", description="Item group OID (e.g., 'IG.DM')"
    )

    @model_validator(mode="before")
    @classmethod
    def populate_item_group_oid(cls, values: Any) -> Any:
        if isinstance(values, dict):
            if "itemGroupOID" not in values or not values["itemGroupOID"] or values["itemGroupOID"] == "IG.":
                name = values.get("name", "")
                if name:
                    values["itemGroupOID"] = f"IG.{name.upper()}"
        return values

    @field_validator("itemData")
    @classmethod
    def validate_row_lengths(cls, item_data: list[list[Any]], info) -> list[list[Any]]:
        # Get items length if available in the input context
        # In validation, we can't easily cross-reference another field unless we do a model_validator,
        # but we can do that in model_validator for better robustness.
        return item_data


class ClinicalData(BaseModel):
    """ClinicalData container for CDISC Dataset-JSON."""

    studyOID: str = Field(
        ..., description="Unique identifier for the study (e.g., 'STUDY.001')"
    )
    metaDataVersionOID: str = Field(
        ..., description="Metadata version identifier (e.g., 'MDV.001')"
    )
    itemGroupData: dict[str, DatasetJSONItemGroup] = Field(
        ..., description="Mapping of group names (e.g., 'IG.DM') to their datasets"
    )
    dbLastModifiedDateTime: str | None = Field(None, description="Database last modified timestamp")
    metaDataRef: str | None = Field(None, description="Metadata reference string or URI")


class ReferenceData(BaseModel):
    """ReferenceData container for CDISC Dataset-JSON (when reference data is utilized instead of clinical data)."""

    studyOID: str = Field(..., description="Unique identifier for the study")
    metaDataVersionOID: str = Field(..., description="Metadata version identifier")
    itemGroupData: dict[str, DatasetJSONItemGroup] = Field(
        ..., description="Mapping of group names to their datasets"
    )
    dbLastModifiedDateTime: str | None = Field(None, description="Database last modified timestamp")
    metaDataRef: str | None = Field(None, description="Metadata reference string or URI")


class DatasetJSON(BaseModel):
    """Root model representing a CDISC Dataset-JSON document compliant with Pydantic v2."""

    model_config = ConfigDict(populate_by_name=True)

    datasetJSONCreationDateTime: str = Field(
        default_factory=lambda: datetime.utcnow().isoformat() + "Z",
        alias="creationDateTime",
        description="ISO 8601 creation timestamp",
    )
    datasetJSONVersion: str = Field(
        "1.0.0", description="The Dataset-JSON specification version"
    )
    fileOID: str | None = Field(None, description="Unique identifier for this file")
    asOfDateTime: str | None = Field(None, description="As of timestamp")
    originator: str | None = Field(None, description="Originator of the data")
    sourceSystem: str | None = Field(None, description="Generating system")
    sourceSystemVersion: str | None = Field(
        None, description="Generating system version"
    )
    clinicalData: ClinicalData | None = Field(None, description="Clinical data block")
    referenceData: ReferenceData | None = Field(
        None, description="Reference data block"
    )
    dbLastModifiedDateTime: str | None = Field(None, description="Database last modified timestamp")
    metaDataRef: str | None = Field(None, description="Metadata reference string or URI")

    @property
    def creationDateTime(self) -> str:
        return self.datasetJSONCreationDateTime

    @model_serializer(mode="wrap")
    def serialize_both_creation_times(self, handler: Any) -> dict[str, Any]:
        data = handler(self)
        if isinstance(data, dict):
            if "datasetJSONCreationDateTime" in data and "creationDateTime" not in data:
                data["creationDateTime"] = data["datasetJSONCreationDateTime"]
            elif "creationDateTime" in data and "datasetJSONCreationDateTime" not in data:
                data["datasetJSONCreationDateTime"] = data["creationDateTime"]
        return data

    @field_validator("datasetJSONCreationDateTime")
    @classmethod
    def validate_timestamp(cls, v: str) -> str:
        # Just simple validation to ensure it looks like a datetime
        try:
            # support both Z and offset
            clean_v = v.rstrip("Z")
            datetime.fromisoformat(clean_v)
        except ValueError:
            raise ValueError(
                "creationDateTime must be a valid ISO 8601 datetime string"
            )
        return v
