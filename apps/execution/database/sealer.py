import asyncio
import logging
import os
from typing import Any, Optional

from sqlalchemy.ext.asyncio import AsyncSession

from packages.security.signing import (
    clean_json_val,
    generic_execute_audit_sealing_cycle,
    generic_validate_ledger_integrity,
)

logger = logging.getLogger("sealer")

_sealer_task: Optional[asyncio.Task] = None
_should_run: bool = False


def clean_query(query_str: str, db: AsyncSession) -> str:
    """
    Strips 'audit_schema.' schema prefix from raw SQL queries when running on SQLite
    to maintain dialect-agnostic compatibility in tests.
    """
    if db.bind.dialect.name == "sqlite":
        return query_str.replace("audit_schema.", "")
    return query_str


def execution_payload_builder(rec: Any) -> dict:
    """Helper to construct deterministic payload for execution core audit log record."""
    timestamp_str = (
        rec.timestamp.isoformat()
        if hasattr(rec.timestamp, "isoformat")
        else str(rec.timestamp)
    )
    return {
        "id": str(rec.id),
        "table_name": str(rec.table_name),
        "record_id": str(rec.record_id),
        "action": str(rec.action),
        "user_id": str(rec.user_id) if rec.user_id is not None else None,
        "timestamp": timestamp_str,
        "old_values": clean_json_val(rec.old_values),
        "new_values": clean_json_val(rec.new_values),
        "version_index": int(rec.version_index),
        "change_reason": (
            str(rec.change_reason) if rec.change_reason is not None else None
        ),
    }


async def execute_audit_sealing_cycle(
    db: AsyncSession, limit: int = 100
) -> Optional[str]:
    """
    Compiles chronological batches of unsealed audit events and hashes them using SHA-256
    with sequential block-level chaining to create cryptographic seals.

    Args:
        db (AsyncSession): The active database session.
        limit (int): Maximum number of unsealed logs to process in one block.

    Returns:
        Optional[str]: The hash of the newly created block, or None if no new records were sealed.
    """
    return await generic_execute_audit_sealing_cycle(
        db=db,
        seals_table=clean_query("audit_schema.audit_ledger_seals", db),
        logs_table=clean_query("audit_schema.audit_logs", db),
        log_columns=[
            "id",
            "table_name",
            "record_id",
            "action",
            "user_id",
            "timestamp",
            "old_values",
            "new_values",
            "version_index",
            "change_reason",
        ],
        payload_builder=execution_payload_builder,
        limit=limit,
    )


async def validate_ledger_integrity(db: AsyncSession) -> bool:
    """
    Validates the entire cryptographic ledger chain, rebuilding hashes sequentially.
    If any tampering is detected:
      1. Terminates validation.
      2. Locks the trial using TrialLockManager.
      3. Raises a ValueError alert.

    Returns:
        bool: True if ledger integrity is successfully verified.
    """
    return await generic_validate_ledger_integrity(
        db=db,
        seals_table=clean_query("audit_schema.audit_ledger_seals", db),
        logs_table=clean_query("audit_schema.audit_logs", db),
        log_columns=[
            "id",
            "table_name",
            "record_id",
            "action",
            "user_id",
            "timestamp",
            "old_values",
            "new_values",
            "version_index",
            "change_reason",
        ],
        payload_builder=execution_payload_builder,
        trial_lock_reason_prefix="GxP Core Data Integrity Breach",
    )


async def start_background_sealer(
    session_maker: Any, interval: Optional[float] = None
) -> None:
    """
    Start the asynchronous background ledger sealer thread.
    """
    global _sealer_task, _should_run
    if interval is None:
        interval = float(os.getenv("SEALER_INTERVAL_SECONDS", "60.0"))
    _should_run = True

    async def sealer_loop():
        logger.info(
            "Background ledger sealer started with interval %s seconds.", interval
        )
        while _should_run:
            try:
                async with session_maker() as db:
                    block_hash = await execute_audit_sealing_cycle(db)
                    if block_hash:
                        logger.info(
                            "Successfully sealed block with hash: %s", block_hash
                        )
            except Exception as e:
                logger.error("Error in audit sealing cycle: %s", e, exc_info=True)

            for _ in range(int(interval * 10)):
                if not _should_run:
                    break
                await asyncio.sleep(0.1)

    _sealer_task = asyncio.create_task(sealer_loop())


async def stop_background_sealer() -> None:
    """
    Stop the asynchronous background ledger sealer thread.
    """
    global _sealer_task, _should_run
    _should_run = False
    if _sealer_task:
        try:
            await _sealer_task
        except asyncio.CancelledError:
            pass
        _sealer_task = None
    logger.info("Background ledger sealer stopped.")
