"""Pydantic transport schemas for eISF service presentation layer."""

from datetime import date, datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, model_validator

from apps.eisf.domain.eisf_transport_models import (
    EISFDocumentDetail,
    EISFDocumentUploadRequest,
    EISFFolderNode,
)


class DocumentCreate(BaseModel):
    study_id: str = Field(..., description="The clinical study ID")
    site_id: str = Field(..., description="The clinical site ID")
    binder_classification: str = Field(..., description="Binder classification")
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Base64 or raw content")
    mime_type: str = Field(..., description="MIME type")
    metadata_json: dict[str, Any] | None = Field(None, description="Metadata JSON")
    correlation_key: str | None = Field(None, description="Correlation key")
    content_checksum: str | None = Field(None, description="Content checksum")
    source_system: str = Field("eISF", description="Source system name")
    reason_for_change: str = Field(
        ..., min_length=10, max_length=1000, description="Part 11 reason for change"
    )
    issue_date: date | None = Field(None, description="Optional document issue date")
    expiration_date: date | None = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: str | None = Field(
        None, description="Optional document owner ID"
    )

    @model_validator(mode="after")
    def validate_dates(self) -> DocumentCreate:
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class DocumentUpdate(BaseModel):
    study_id: str = Field(..., description="The clinical study ID")
    site_id: str = Field(..., description="The clinical site ID")
    binder_classification: str = Field(..., description="Binder classification")
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Base64 or raw content")
    mime_type: str = Field(..., description="MIME type")
    metadata_json: dict[str, Any] | None = Field(None, description="Metadata JSON")
    correlation_key: str | None = Field(None, description="Correlation key")
    content_checksum: str | None = Field(None, description="Content checksum")
    source_system: str = Field("eISF", description="Source system name")
    reason_for_change: str = Field(
        ..., min_length=10, max_length=1000, description="Part 11 reason for change"
    )
    issue_date: date | None = Field(None, description="Optional document issue date")
    expiration_date: date | None = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: str | None = Field(
        None, description="Optional document owner ID"
    )

    @model_validator(mode="after")
    def validate_dates(self) -> DocumentUpdate:
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class EISFIngestionRequest(BaseModel):
    study_id: str = Field(..., description="The clinical study ID")
    site_id: str = Field(..., description="The clinical site ID")
    binder_classification: str | None = Field(None, description="Binder classification")
    artifact_type: str | None = Field(
        None,
        description="Artifact classification metadata alias for binder_classification",
    )
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Base64 or raw content")
    mime_type: str = Field(..., description="MIME type")
    metadata_json: dict[str, Any] | None = Field(None, description="Metadata JSON")
    correlation_key: str | None = Field(None, description="Correlation key")
    content_checksum: str | None = Field(None, description="Content checksum")
    source_system: str = Field("eISF", description="Source system name")
    reason_for_change: str | None = Field(
        None, min_length=10, max_length=1000, description="Part 11 reason for change"
    )
    issue_date: date | None = Field(None, description="Optional document issue date")
    expiration_date: date | None = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: str | None = Field(
        None, description="Optional document owner ID"
    )

    @classmethod
    @model_validator(mode="before")
    def resolve_binder_class(cls, data: Any) -> Any:
        if isinstance(data, dict):
            bc = data.get("binder_classification")
            at = data.get("artifact_type")
            if not bc and not at:
                raise ValueError(
                    "Either binder_classification or artifact_type must be provided"
                )
            if not bc:
                data["binder_classification"] = at
        return data

    @model_validator(mode="after")
    def validate_dates(self) -> EISFIngestionRequest:
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class EISFSyncItem(BaseModel):
    id: str | None = None
    study_id: str = Field(..., description="The clinical study ID")
    site_id: str = Field(..., description="The clinical site ID")
    binder_classification: str = Field(..., description="Binder classification")
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Base64 or raw content")
    mime_type: str = Field(..., description="MIME type")
    version_index: int | None = Field(None, description="Optional version index")
    metadata_json: dict[str, Any] | None = Field(None, description="Metadata JSON")
    correlation_key: str | None = Field(None, description="Correlation key")
    content_checksum: str | None = Field(None, description="Content checksum")
    source_system: str = Field("eISF", description="Source system name")
    sync_status: str = Field("PENDING", description="Sync status")
    conflict_policy: str = Field(
        "CLIENT_WINS", description="CLIENT_WINS, SERVER_WINS, or MERGE"
    )
    issue_date: date | None = Field(None, description="Optional document issue date")
    expiration_date: date | None = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: str | None = Field(
        None, description="Optional document owner ID"
    )

    @classmethod
    @model_validator(mode="before")
    def resolve_conflict_policy(cls, data: Any) -> Any:
        if isinstance(data, dict):
            cp = data.get("conflict_policy")
            cs = data.get("conflict_strategy")
            if not cp and cs:
                data["conflict_policy"] = cs
        return data

    @model_validator(mode="after")
    def validate_dates(self) -> EISFSyncItem:
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class EISFSyncRequest(BaseModel):
    submissions: list[EISFSyncItem] = Field(..., description="List of sync items")


class EISFSyncResponse(BaseModel):
    status: str = "success"
    processed_count: int
    created_count: int
    updated_count: int
    ignored_count: int


class DocumentResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    binder_classification: str
    filename: str
    content: str
    mime_type: str
    version_index: int
    created_at: datetime
    created_by: str
    metadata_json: dict[str, Any] | None = None
    correlation_key: str | None = None
    content_checksum: str | None = None
    sync_status: str
    source_system: str
    issue_date: date | None = None
    expiration_date: date | None = None
    document_owner_id: str | None = None

    model_config = ConfigDict(from_attributes=True)


class BinderSectionStatus(BaseModel):
    section_name: str
    required_artifacts: list[str]
    present: list[str]
    missing: list[str]


class BinderCompletenessResponse(BaseModel):
    site_id: str
    is_complete: bool
    sections: list[BinderSectionStatus]


__all__ = [
    "BinderCompletenessResponse",
    "BinderSectionStatus",
    "DocumentCreate",
    "DocumentResponse",
    "DocumentUpdate",
    "EISFDocumentDetail",
    "EISFDocumentUploadRequest",
    "EISFFolderNode",
    "EISFIngestionRequest",
    "EISFSyncItem",
    "EISFSyncRequest",
    "EISFSyncResponse",
]
