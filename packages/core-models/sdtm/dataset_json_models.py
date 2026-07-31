"""
Pydantic models for CDISC Dataset-JSON v1.0 standard representation.

Requirements Traceability: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

from typing import Any, List, Literal, Optional

from pydantic import BaseModel, Field


class DatasetJsonItemDef(BaseModel):
    """Dataset-JSON variable/item definition."""

    name: str = Field(..., description="The variable name (e.g., 'USUBJID')")
    label: str = Field(..., description="The label describing the variable")
    type: Literal["string", "integer", "float", "date", "datetime"] = Field(
        ..., description="Standard clinical data type"
    )
    length: Optional[int] = Field(None, description="Optional length limitation")


class DatasetJsonItemGroup(BaseModel):
    """Dataset-JSON itemGroupData representation holding metadata and tabular records."""

    records: int = Field(..., description="The number of records/rows in the dataset")
    name: str = Field(..., description="The dataset or domain name")
    label: str = Field(..., description="A descriptive label for the dataset")
    items: List[DatasetJsonItemDef] = Field(
        ..., description="Ordered list of variable/item definitions"
    )
    itemData: List[List[Any]] = Field(
        ...,
        description="The grid data, as an ordered list of lists matching the items metadata schema",
    )


class DatasetJsonPayload(BaseModel):
    """The root envelope for CDISC Dataset-JSON compliant documents."""

    creationDateTime: str = Field(..., description="ISO 8601 creation timestamp in UTC")
    datasetJSONVersion: str = Field(
        "1.0.0", description="The Dataset-JSON specification version"
    )
    fileOID: str = Field(
        ..., description="File OID identifying the resource or file location"
    )
    clinicalData: dict = Field(
        ...,
        description="Clinical data metadata container containing studyOID, metaDataVersionOID, and itemGroupData",
    )
