"""Domain Models and Schema Definitions for the FSM DAG Lifecycle Engine.

Provides strongly-typed Pydantic v2 models for state nodes, transition edges,
context payloads, and 21 CFR Part 11 audit envelopes.
"""

from __future__ import annotations

import hashlib
import json
import uuid
from datetime import UTC, datetime
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

from packages.engine_core.rules.ast import ExpressionNode

StateType = Literal["initial", "normal", "terminal", "error"]


class StateNode(BaseModel):
    """Represents a state vertex in the lifecycle graph."""

    model_config = ConfigDict(frozen=True)

    name: str
    state_type: StateType = "normal"
    label: str | None = None
    description: str | None = None
    metadata: dict[str, Any] = Field(default_factory=dict)


class TransitionEdge(BaseModel):
    """Represents a directed transition edge between states."""

    trigger: str
    from_state: str
    to_state: str
    guard: ExpressionNode | None = None
    allowed_roles: list[str] = Field(default_factory=list)
    requires_signature: bool = False
    requires_reason_for_change: bool = True
    min_reason_length: int = 10
    action_hooks: list[str] = Field(default_factory=list)
    description: str | None = None


class Part11AuditEnvelope(BaseModel):
    """Immutable 21 CFR Part 11 cryptographic audit record for state transitions."""

    event_id: str = Field(default_factory=lambda: f"evt_{uuid.uuid4().hex[:12]}")
    machine_id: str
    machine_version: int
    from_state: str
    to_state: str
    trigger: str
    timestamp: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    user_id: str
    user_roles: list[str] = Field(default_factory=list)
    reason_for_change: str
    signature_verified: bool = False
    sha256_digest: str = ""

    @model_validator(mode="after")
    def compute_sha256(self) -> Part11AuditEnvelope:
        """Computes monotonic SHA-256 cryptographic digest of the transition record."""
        if not self.sha256_digest:
            payload = {
                "event_id": self.event_id,
                "machine_id": self.machine_id,
                "machine_version": self.machine_version,
                "from_state": self.from_state,
                "to_state": self.to_state,
                "trigger": self.trigger,
                "timestamp": self.timestamp,
                "user_id": self.user_id,
                "user_roles": sorted(self.user_roles),
                "reason_for_change": self.reason_for_change,
                "signature_verified": self.signature_verified,
            }
            canonical = json.dumps(payload, sort_keys=True)
            self.sha256_digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
        return self


class TransitionContext(BaseModel):
    """Runtime context payload supplied when requesting a state transition."""

    current_state: str
    trigger: str
    user_id: str = "system"
    user_roles: list[str] = Field(default_factory=list)
    reason_for_change: str | None = None
    signature_token: str | None = None
    payload: dict[str, Any] = Field(default_factory=dict)
    as_of: datetime | None = None


class TransitionResult(BaseModel):
    """Outcome of a state transition attempt."""

    success: bool
    from_state: str
    to_state: str
    trigger: str
    error_message: str | None = None
    audit_envelope: Part11AuditEnvelope | None = None
    executed_action_hooks: list[str] = Field(default_factory=list)


class StateMachineDefinition(BaseModel):
    """Declarative specification defining a complete FSM lifecycle graph."""

    machine_id: str
    name: str
    version_index: int = Field(default=1, ge=1)
    states: list[StateNode]
    transitions: list[TransitionEdge]
    initial_state: str
    sha256_checksum: str | None = None
    is_active: bool = True
    is_deleted: bool = False
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    created_by: str = "system"
    reason_for_change: str = "Initial state machine authoring"

    @model_validator(mode="after")
    def validate_and_seal(self) -> StateMachineDefinition:
        """Validates baseline state definitions and generates SHA-256 seal."""
        state_names = {s.name for s in self.states}
        if self.initial_state not in state_names:
            raise ValueError(
                f"Initial state '{self.initial_state}' does not exist in state list"
            )

        if not self.sha256_checksum:
            payload = {
                "machine_id": self.machine_id,
                "name": self.name,
                "version_index": self.version_index,
                "initial_state": self.initial_state,
                "states": [s.model_dump() for s in self.states],
                "transitions": [t.model_dump() for t in self.transitions],
            }
            canonical = json.dumps(payload, sort_keys=True)
            self.sha256_checksum = hashlib.sha256(canonical.encode("utf-8")).hexdigest()

        return self
