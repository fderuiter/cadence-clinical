"""Request and Response DTOs for the CDISC SDTM/ADaM Pipeline & Dataset-JSON/Define-XML APIs.

Requirements: PRD-CDR-002, PRD-CDR-003, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class RunContinuousPipelineRequest(BaseModel):
    """Request payload to trigger end-to-end continuous CDISC SDTM/ADaM and Dataset-JSON/Define-XML pipeline."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(
        ..., min_length=1, description="Unique Study Identifier (e.g. STUDY-001)"
    )
    snapshot_id: str | None = Field(
        None, description="Optional version-pinned snapshot ID to execute over"
    )
    dataset_json_version: str = Field(
        "1.1.0", description="Target Dataset-JSON version ('1.0.0', '1.1.0', '2.0.0')"
    )
    reason_for_change: str = Field(
        "Automated CDISC SDTM/ADaM pipeline continuous derivation run",
        min_length=3,
        description="21 CFR Part 11 required reason for change",
    )


class RunSDTMPipelineRequest(BaseModel):
    """Request payload to execute SDTM mapping pipeline."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., min_length=1, description="Study Identifier")
    snapshot_id: str | None = Field(None, description="Optional snapshot ID")
    domains: list[str] | None = Field(
        None,
        description="Optional list of specific SDTM domains (DM, AE, CM, VS, LB, EX, DS, TU, TR, RS)",
    )
    reason_for_change: str = Field("SDTM domain mapping execution", min_length=3)


class RunADaMPipelineRequest(BaseModel):
    """Request payload to execute ADaM derivation pipeline."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., min_length=1, description="Study Identifier")
    snapshot_id: str | None = Field(None, description="Optional snapshot ID")
    datasets: list[str] | None = Field(
        None,
        description="Optional list of specific ADaM datasets (ADSL, ADAE, ADTTE, BDS)",
    )
    reason_for_change: str = Field("ADaM dataset derivation execution", min_length=3)


class ExportDatasetJSONRequest(BaseModel):
    """Request payload for exporting datasets in CDISC Dataset-JSON 1.1 / 2.0."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., min_length=1, description="Study Identifier")
    snapshot_id: str | None = Field(None, description="Optional snapshot ID")
    datasets: list[str] | None = Field(
        None,
        description="List of domains/datasets to include (e.g. ['DM', 'AE', 'ADSL', 'ADAE'])",
    )
    dataset_json_version: str = Field(
        "1.1.0",
        description="Dataset-JSON specification version ('1.0.0', '1.1.0', '2.0.0')",
    )
    reason_for_change: str = Field("Dataset-JSON biostatistical export", min_length=3)


class ExportDefineXMLRequest(BaseModel):
    """Request payload for compiling and exporting CDISC Define-XML v2.1."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., min_length=1, description="Study Identifier")
    study_name: str | None = Field(None, description="Study Name")
    protocol_name: str | None = Field(None, description="Protocol Name")
    metadata_version_oid: str = Field(
        "MDV.CDISC.001", description="Metadata Version OID"
    )
    standard_name: str = Field("SDTM-IG", description="Standard Name")
    standard_version: str = Field("3.4", description="Standard Version")
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
        description="SDTM domains to document",
    )
    adam_datasets: list[str] = Field(
        default_factory=lambda: ["ADSL", "ADAE", "ADTTE", "BDS"],
        description="ADaM datasets to document",
    )
    include_vlm: bool = Field(True, description="Include Value Level Metadata")
    include_where_clauses: bool = Field(True, description="Include WhereClauses")
    include_origins: bool = Field(True, description="Include Origin annotations")
    reason_for_change: str = Field("Define-XML v2.1 compilation export", min_length=3)


class ValidateDatasetJSONRequest(BaseModel):
    """Request payload for validating Dataset-JSON."""

    model_config = ConfigDict(extra="allow")

    payload: dict[str, Any] | str = Field(
        ..., description="Dataset-JSON document or dictionary"
    )


class ValidateDatasetJSONResponse(BaseModel):
    """Validation response for Dataset-JSON."""

    model_config = ConfigDict(extra="forbid")

    is_valid: bool
    errors: list[str] = Field(default_factory=list)
    warnings: list[str] = Field(default_factory=list)
    dataset_count: int = 0
    record_count: int = 0


class ValidateDefineXMLRequest(BaseModel):
    """Request payload for validating Define-XML 2.1."""

    model_config = ConfigDict(extra="forbid")

    xml_content: str = Field(
        ..., min_length=10, description="Define-XML document string"
    )


class SDTMPipelineResponse(BaseModel):
    """Response containing mapped SDTM domain records."""

    model_config = ConfigDict(extra="forbid")

    study_id: str
    snapshot_id: str | None
    domain_counts: dict[str, int]
    total_records: int
    domains: dict[str, list[dict[str, Any]]]


class ADaMPipelineResponse(BaseModel):
    """Response containing derived ADaM dataset records."""

    model_config = ConfigDict(extra="forbid")

    study_id: str
    snapshot_id: str | None
    dataset_counts: dict[str, int]
    total_records: int
    datasets: dict[str, list[dict[str, Any]]]


class DomainCatalogResponse(BaseModel):
    """Catalog of supported SDTM & ADaM domains, variables, and VLM definitions."""

    model_config = ConfigDict(extra="forbid")

    sdtm_domains: list[str]
    adam_datasets: list[str]
    domain_metadata: dict[str, Any]
    vlm_catalog: dict[str, Any]
    codelist_catalog: list[str]
