"""Pydantic schemas for CDISC and regulatory biostatistical export endpoints."""

from pydantic import BaseModel, Field


class ExportBundleRequest(BaseModel):
    """Pydantic schema representing clinical dataset export request parameters."""

    study_id: str
    format: str = Field(
        "dataset_json",
        description="Target format: dataset_json, xpt_v5, xpt_v8, odm_xml, csv, csv_zip",
    )
    domains: list[str] = Field(
        default_factory=list,
        description="List of SDTM domains to include (e.g. DM, AE, VS, LB, MH, CM)",
    )
    datasets: list[str] = Field(
        default_factory=list,
        description="List of ADaM datasets to include (e.g. ADSL, ADAE, ADVS)",
    )
    site_ids: list[str] = Field(
        default_factory=list,
        description="Optional list of site IDs to filter by",
    )
    cohorts: list[str] = Field(
        default_factory=list,
        description="Optional list of subject cohorts/arms to filter by",
    )
    privacy_profile: str = Field(
        "SAFE_HARBOR",
        description="Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED",
    )
    xpt_version: str = Field(
        "v5",
        description="SAS Transport version: v5 or v8",
    )
    include_audit_trail: bool = Field(
        True,
        description="Whether to include GxP audit records in ODM-XML / CSV",
    )
    metadata_version_oid: str = Field(
        "MDV.001",
        description="ODM / Dataset-JSON metadata version OID",
    )
    salt: str | None = Field(
        None,
        description="Optional HMAC salt override for deterministic de-identification",
    )


class ExportWizardSummaryResponse(BaseModel):
    """Pydantic schema returning export execution summary."""

    study_id: str
    export_format: str
    total_records: int
    domains_included: list[str]
    datasets_included: list[str]
    privacy_profile: str
    status: str
    download_url: str | None = None
    created_at: str
