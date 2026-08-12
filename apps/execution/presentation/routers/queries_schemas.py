from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class QueryHistoryItem(BaseModel):
    """Pydantic schema representing a single audited event in query history."""

    action: str
    user_id: str | None = None
    timestamp: datetime
    old_values: dict[str, Any] | None = None
    new_values: dict[str, Any] | None = None
    change_reason: str | None = None
    version_index: int


class ClinicalQueryResponse(BaseModel):
    """Pydantic schema returning query details and full audit history."""

    id: str
    study_id: str
    subject_id: str
    visit_id: str | None = None
    domain: str | None = None
    test_code: str
    status: str
    explanation: str | None = None
    response: str | None = None
    created_at: datetime
    updated_at: datetime
    history: list[QueryHistoryItem] = []

    observation_id: str | None = None
    field_link: str | None = None
    message: str | None = None
    origin: str | None = None
    priority: str | None = None
    rule_id: str | None = None
    created_by: str | None = None
    responder: str | None = None
    resolver: str | None = None
    resolved_at: datetime | None = None
    cancellation_reason: str | None = None
    escalated_at: datetime | None = None

    form_id: str | None = None
    field_id: str | None = None
    query_type: str | None = None
    action_required: str | None = None


class QueryCreate(BaseModel):
    """Pydantic schema for raising a new query."""

    study_id: str
    subject_id: str
    visit_id: str | None = None
    domain: str | None = None
    test_code: str
    explanation: str
    status: str | None = "OPEN"

    observation_id: str | None = None
    field_link: str | None = None
    message: str | None = None
    origin: str | None = None
    priority: str | None = None
    rule_id: str | None = None
    created_by: str | None = None

    form_id: str | None = None
    field_id: str | None = None
    query_type: str | None = None
    action_required: str | None = None


class QueryReopen(BaseModel):
    """Pydantic schema for reopening a query with a reason."""

    reason: str | None = None


class QueryCancel(BaseModel):
    """Pydantic schema for cancelling a query with a reason."""

    reason: str


class QueryRespond(BaseModel):
    """Pydantic schema for responding to an open query."""

    response: str
    responder: str | None = None


class QueryUpdate(BaseModel):
    """Pydantic schema for general state transitions."""

    status: str
    explanation: str | None = None
    response: str | None = None

    observation_id: str | None = None
    field_link: str | None = None
    message: str | None = None
    origin: str | None = None
    priority: str | None = None
    rule_id: str | None = None
    created_by: str | None = None
    responder: str | None = None
    resolver: str | None = None
    resolved_at: datetime | None = None
    cancellation_reason: str | None = None
    escalated_at: datetime | None = None

    form_id: str | None = None
    field_id: str | None = None
    query_type: str | None = None
    action_required: str | None = None


class SyncBlockQuery(BaseModel):
    """Pydantic schema representing the query details in a local ledger block."""

    model_config = ConfigDict(populate_by_name=True)

    status: str
    message: str | None = None
    created_by: str | None = Field(None, alias="createdBy")
    created_at: str | None = Field(None, alias="createdAt")
    response: str | None = None
    responded_by: str | None = Field(None, alias="respondedBy")
    responded_at: str | None = Field(None, alias="respondedAt")
    closed_by: str | None = Field(None, alias="closedBy")
    closed_at: str | None = Field(None, alias="closedAt")


class SyncBlockDetails(BaseModel):
    """Pydantic schema representing block-specific metadata and clinical coordinates."""

    model_config = ConfigDict(populate_by_name=True)

    field_id: str = Field(..., alias="fieldId")
    study_id: str | None = Field(None, alias="studyId")
    subject_id: str | None = Field(None, alias="subjectId")
    visit_id: str | None = Field(None, alias="visitId")
    domain: str | None = None
    test_code: str | None = Field(None, alias="testCode")
    query: SyncBlockQuery | None = None
    label: str | None = None
    cdash: str | None = None
    old_value: str | None = Field(None, alias="oldValue")
    new_value: str | None = Field(None, alias="newValue")


class LocalLedgerBlock(BaseModel):
    """Pydantic schema representing a cryptographically chained offline ledger block."""

    model_config = ConfigDict(populate_by_name=True)

    index: int
    timestamp: datetime
    action: str
    details: SyncBlockDetails
    reason: str
    prev_hash: str = Field(..., alias="prevHash")
    hash: str


class SyncRequest(BaseModel):
    """Pydantic schema for bulk-synchronizing local client-side ledger updates."""

    blocks: list[LocalLedgerBlock]
