"""FastAPI router for offline batch delta ingestion and synchronization.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import uuid
from datetime import datetime, timezone
from typing import Any, Dict, List, Type

from execution.offline_models import (
    OfflineBatchSyncRequest,
    OfflineBatchSyncResponse,
    OfflineDeltaItem,
)
from fastapi import APIRouter, Depends, Request, status
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    ClinicalObservation,
    ClinicalQuery,
    FormSubmission,
    SyncedBatchIdempotencyKey,
)
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/offline", tags=["Offline"])


class OfflineSyncEngine:
    """Reconciles and processes a batch of offline transaction deltas inside an active transaction.

    Requirements: PRD-SYS-001
    """

    @staticmethod
    async def process_delta_batch(
        session,
        deltas: List[OfflineDeltaItem],
    ) -> tuple[int, List[Dict[str, Any]]]:
        """Process a list of offline delta items.

        Requirements: PRD-SYS-001
        """
        entity_map: Dict[str, Type] = {
            "form_submission": FormSubmission,
            "form_submissions": FormSubmission,
            "clinical_observation": ClinicalObservation,
            "clinical_observations": ClinicalObservation,
            "clinical_query": ClinicalQuery,
            "clinical_queries": ClinicalQuery,
        }

        processed_count = 0
        conflicts = []

        for delta in deltas:
            entity_type_lower = delta.entity_type.lower()
            model_class = entity_map.get(entity_type_lower)
            if not model_class:
                conflicts.append(
                    {
                        "delta_id": delta.delta_id,
                        "entity_id": delta.entity_id,
                        "error": f"Unknown entity type: '{delta.entity_type}'",
                    }
                )
                continue

            # Query existing record where is_deleted is False (E712 compliance)
            stmt = select(model_class).where(
                model_class.id == delta.entity_id,
                model_class.is_deleted.is_(False),
            )
            res = await session.execute(stmt)
            existing_record = res.scalar_one_or_none()

            if delta.action == "CREATE":
                if existing_record is not None:
                    conflicts.append(
                        {
                            "delta_id": delta.delta_id,
                            "entity_id": delta.entity_id,
                            "error": "Entity already exists on CREATE action",
                        }
                    )
                else:
                    try:
                        payload_data = dict(delta.payload)
                        payload_data["id"] = delta.entity_id
                        new_record = model_class(**payload_data)
                        if hasattr(new_record, "version"):
                            new_record.version = 1
                        session.add(new_record)
                        processed_count += 1
                    except Exception as e:
                        conflicts.append(
                            {
                                "delta_id": delta.delta_id,
                                "entity_id": delta.entity_id,
                                "error": f"Failed to instantiate model: {str(e)}",
                            }
                        )

            elif delta.action in ("UPDATE", "SUBMIT"):
                if existing_record is None:
                    conflicts.append(
                        {
                            "delta_id": delta.delta_id,
                            "entity_id": delta.entity_id,
                            "error": f"Entity not found on {delta.action} action",
                        }
                    )
                else:
                    try:
                        for key, val in delta.payload.items():
                            if hasattr(existing_record, key):
                                setattr(existing_record, key, val)
                        if hasattr(existing_record, "version"):
                            existing_record.version = (existing_record.version or 1) + 1
                        processed_count += 1
                    except Exception as e:
                        conflicts.append(
                            {
                                "delta_id": delta.delta_id,
                                "entity_id": delta.entity_id,
                                "error": f"Failed to update model: {str(e)}",
                            }
                        )

        return processed_count, conflicts


@router.post(
    "/sync-batch",
    response_model=OfflineBatchSyncResponse,
    status_code=status.HTTP_200_OK,
)
async def sync_offline_batch(
    payload: OfflineBatchSyncRequest,
    request: Request,
    user: dict = Depends(get_current_user),
) -> OfflineBatchSyncResponse:
    """Ingest batch of queued offline eCRF/ePRO deltas idempotently.

    Requirements: PRD-SYS-001
    """
    async with db_manager.get_session_maker()() as session:
        # Check idempotency first outside the write transaction to allow instant return
        stmt = select(SyncedBatchIdempotencyKey).where(
            SyncedBatchIdempotencyKey.client_batch_id == payload.client_batch_id
        )
        res = await session.execute(stmt)
        existing_key = res.scalar_one_or_none()

        if existing_key is not None:
            return OfflineBatchSyncResponse(
                client_batch_id=payload.client_batch_id,
                status="ALREADY_PROCESSED",
                processed_count=existing_key.processed_count,
                conflicts=[],
            )

        # To start a new explicit write transaction, we commit/rollback any active implicit transaction
        await session.rollback()

        # Open transactional block
        async with session.begin():
            user_val = user.get("sub") or "system"
            reason_val = (
                getattr(request.state, "change_reason", None)
                or "Offline batch synchronization"
            )

            await session.execute(
                text("SELECT set_config('cadence.current_user_id', :user_id, true);"),
                {"user_id": user_val},
            )
            await session.execute(
                text(
                    "SELECT set_config('cadence.current_change_reason', :reason, true);"
                ),
                {"reason": reason_val},
            )
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', true);")
            )

            processed_count, conflicts = await OfflineSyncEngine.process_delta_batch(
                session, payload.deltas
            )

            status_str = "SUCCESS" if not conflicts else "PARTIAL_SUCCESS"

            # Record OFFLINE_SYNC_BATCH audit event
            audit_log = AuditLog(
                id=str(uuid.uuid4()),
                table_name="synced_batch_idempotency_keys",
                record_id=payload.client_batch_id,
                action="OFFLINE_SYNC_BATCH",
                user_id=user_val,
                ip_address=getattr(request.client, "host", None) or "127.0.0.1"
                if request.client
                else "127.0.0.1",
                timestamp=datetime.now(timezone.utc).replace(tzinfo=None),
                old_values={},
                new_values={
                    "device_id": payload.device_id,
                    "deltas_count": len(payload.deltas),
                    "processed_count": processed_count,
                    "client_batch_id": payload.client_batch_id,
                    "status": status_str,
                },
                version_index=1,
                change_reason=reason_val,
            )
            session.add(audit_log)

            # Persist idempotency key record
            idempotency_key_record = SyncedBatchIdempotencyKey(
                client_batch_id=payload.client_batch_id,
                device_id=payload.device_id,
                processed_count=processed_count,
                processed_at=datetime.now(timezone.utc).replace(tzinfo=None),
            )
            session.add(idempotency_key_record)

        return OfflineBatchSyncResponse(
            client_batch_id=payload.client_batch_id,
            status=status_str,
            processed_count=processed_count,
            conflicts=conflicts,
        )
