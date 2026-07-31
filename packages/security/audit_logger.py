"""21 CFR Part 11 Immutable Audit Logger and SHA-256 Digest Chain Engine.

Provides centralized, tamper-evident audit logging with SHA-256 digest chaining
and mandatory GxP audit fields (event_id, action_type, user_id, reason_for_change, etc.).

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

import datetime
import hashlib
import hmac
import uuid
from typing import Any, Dict, List, Optional

from pydantic import BaseModel, Field


class AuditLogPayload(BaseModel):
    """Pydantic v2 payload schema for creating an auditable event log entry."""

    service_name: str = Field(
        ..., min_length=1, description="Source microservice identifier"
    )
    action_type: str = Field(
        ..., description="Auditable operation: CREATE, UPDATE, LOCK, SIGN, VIEW, EXPORT"
    )
    entity_name: str = Field(
        ...,
        description="Target domain entity type (e.g. ClinicalObservation, FormSubmission)",
    )
    entity_id: str = Field(..., description="Target domain entity unique identifier")
    user_id: str = Field(..., description="Authenticated user Keycloak subject ID")
    tenant_id: str = Field(
        default="tenant_default", description="Sponsor tenant identifier"
    )
    reason_for_change: str = Field(
        ..., min_length=1, description="21 CFR Part 11 required change justification"
    )
    details: Optional[Dict[str, Any]] = Field(
        default_factory=dict, description="Structured contextual event details"
    )


class AuditLogRecord(AuditLogPayload):
    """Immutable audit log record schema containing cryptographic digest chain metadata."""

    event_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    timestamp: datetime.datetime = Field(
        default_factory=lambda: datetime.datetime.now(datetime.timezone.utc).replace(
            tzinfo=None
        )
    )
    previous_digest: str = Field(
        default="GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
    )
    sha256_digest: str = Field(
        ..., description="SHA-256 HMAC digest binding event fields to chain"
    )


def compute_audit_digest(
    event_id: str,
    service_name: str,
    action_type: str,
    entity_name: str,
    entity_id: str,
    user_id: str,
    tenant_id: str,
    reason_for_change: str,
    timestamp: str,
    previous_digest: str,
    secret_key: str = "gxp-audit-secret-key-cadence-2026",
) -> str:
    """Compute deterministic SHA-256 HMAC digest binding audit log payload fields.

    Args:
        event_id: Unique audit event identifier.
        service_name: Microservice name emitting event.
        action_type: Operation type.
        entity_name: Entity name.
        entity_id: Entity ID.
        user_id: User ID.
        tenant_id: Tenant ID.
        reason_for_change: Change reason.
        timestamp: ISO 8601 UTC timestamp string.
        previous_digest: SHA-256 digest of previous record in audit chain.
        secret_key: HMAC secret key.

    Returns:
        Hex-encoded SHA-256 HMAC digest string.
    """
    canonical_payload = (
        f"{event_id}|{service_name}|{action_type}|{entity_name}|{entity_id}|"
        f"{user_id}|{tenant_id}|{reason_for_change}|{timestamp}|{previous_digest}"
    )
    return hmac.new(
        secret_key.encode("utf-8"),
        canonical_payload.encode("utf-8"),
        hashlib.sha256,
    ).hexdigest()


class AuditLoggerEngine:
    """In-memory and durable audit logging engine maintaining SHA-256 chain integrity."""

    def __init__(self, secret_key: str = "gxp-audit-secret-key-cadence-2026") -> None:
        self.secret_key = secret_key
        self._chain: List[AuditLogRecord] = []

    @property
    def last_digest(self) -> str:
        """Retrieve latest SHA-256 digest in audit chain or genesis block hash."""
        if not self._chain:
            return "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
        return self._chain[-1].sha256_digest

    def log_event(self, payload: AuditLogPayload) -> AuditLogRecord:
        """Create and append a tamper-evident audit log record to chain.

        Args:
            payload: Validated AuditLogPayload model instance.

        Returns:
            Appended AuditLogRecord with computed SHA-256 digest.
        """
        event_id = str(uuid.uuid4())
        timestamp = datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None)
        timestamp_str = timestamp.isoformat()
        prev_digest = self.last_digest

        digest = compute_audit_digest(
            event_id=event_id,
            service_name=payload.service_name,
            action_type=payload.action_type,
            entity_name=payload.entity_name,
            entity_id=payload.entity_id,
            user_id=payload.user_id,
            tenant_id=payload.tenant_id,
            reason_for_change=payload.reason_for_change,
            timestamp=timestamp_str,
            previous_digest=prev_digest,
            secret_key=self.secret_key,
        )

        record = AuditLogRecord(
            event_id=event_id,
            service_name=payload.service_name,
            action_type=payload.action_type,
            entity_name=payload.entity_name,
            entity_id=payload.entity_id,
            user_id=payload.user_id,
            tenant_id=payload.tenant_id,
            reason_for_change=payload.reason_for_change,
            details=payload.details,
            timestamp=timestamp,
            previous_digest=prev_digest,
            sha256_digest=digest,
        )

        self._chain.append(record)
        return record

    def verify_chain_integrity(self) -> bool:
        """Verify unbroken cryptographic SHA-256 digest chain integrity across all records.

        Returns:
            True if all record digests and links match expected values, False if tampered.
        """
        expected_prev = "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"

        for record in self._chain:
            if record.previous_digest != expected_prev:
                return False

            recalculated_digest = compute_audit_digest(
                event_id=record.event_id,
                service_name=record.service_name,
                action_type=record.action_type,
                entity_name=record.entity_name,
                entity_id=record.entity_id,
                user_id=record.user_id,
                tenant_id=record.tenant_id,
                reason_for_change=record.reason_for_change,
                timestamp=record.timestamp.isoformat(),
                previous_digest=record.previous_digest,
                secret_key=self.secret_key,
            )

            if record.sha256_digest != recalculated_digest:
                return False

            expected_prev = record.sha256_digest

        return True


# Global default audit logger engine instance
audit_logger_engine = AuditLoggerEngine()


class CentralAuditLogger:
    """Centralized audit logging facade for clinical and eConsent workflow events."""

    @staticmethod
    def log_event(
        service_name: str,
        action_type: str,
        entity_name: str,
        entity_id: str,
        user_id: str,
        reason_for_change: str,
        details: Optional[Dict[str, Any]] = None,
    ) -> AuditLogRecord:
        """Create and append an audit event log to the SHA-256 chain."""
        payload = AuditLogPayload(
            service_name=service_name,
            action_type=action_type,
            entity_name=entity_name,
            entity_id=entity_id,
            user_id=user_id,
            reason_for_change=reason_for_change,
            details=details or {},
        )
        return audit_logger_engine.log_event(payload)
