import asyncio
import logging
import os
from typing import Any, Optional

from sqlalchemy.ext.asyncio import AsyncSession

from packages.security.signing import (
    generic_execute_audit_sealing_cycle,
    generic_validate_ledger_integrity,
)

logger = logging.getLogger("quality-sealer")

_sealer_task: Optional[asyncio.Task] = None
_should_run: bool = False


def quality_payload_builder(rec: Any) -> dict:
    """Helper to construct deterministic payload for Quality audit log record."""
    timestamp_str = (
        rec.timestamp.isoformat()
        if hasattr(rec.timestamp, "isoformat")
        else str(rec.timestamp)
    )
    return {
        "id": str(rec.id),
        "timestamp": timestamp_str,
        "user_id": str(rec.user_id),
        "user_role": str(rec.user_role),
        "action": str(rec.action),
        "details": str(rec.details),
        "record_id": str(rec.record_id) if rec.record_id is not None else None,
        "change_reason": str(rec.change_reason)
        if rec.change_reason is not None
        else None,
    }


async def execute_quality_audit_sealing_cycle(
    db: AsyncSession, limit: int = 100
) -> Optional[str]:
    """
    Compiles chronological batches of unsealed Quality audit logs and hashes them using SHA-256
    with sequential block-level chaining to create cryptographic seals.
    """
    return await generic_execute_audit_sealing_cycle(
        db=db,
        seals_table="quality_audit_ledger_seals",
        logs_table="quality_audit_logs",
        log_columns=[
            "id",
            "timestamp",
            "user_id",
            "user_role",
            "action",
            "details",
            "record_id",
            "change_reason",
        ],
        payload_builder=quality_payload_builder,
        limit=limit,
    )


async def validate_quality_ledger_integrity(db: AsyncSession) -> bool:
    """
    Validates the entire Quality cryptographic ledger chain, rebuilding hashes sequentially.
    """
    return await generic_validate_ledger_integrity(
        db=db,
        seals_table="quality_audit_ledger_seals",
        logs_table="quality_audit_logs",
        log_columns=[
            "id",
            "timestamp",
            "user_id",
            "user_role",
            "action",
            "details",
            "record_id",
            "change_reason",
        ],
        payload_builder=quality_payload_builder,
        trial_lock_reason_prefix="Quality GxP Data Integrity Breach",
    )


async def start_background_quality_sealer(
    session_maker: Any, interval: Optional[float] = None
) -> None:
    """
    Start the asynchronous background Quality ledger sealer thread.
    """
    global _sealer_task, _should_run
    if interval is None:
        interval = float(os.getenv("QUALITY_SEALER_INTERVAL_SECONDS", "60.0"))
    _should_run = True

    async def sealer_loop():
        logger.info(
            "Background Quality ledger sealer started with interval %s seconds.",
            interval,
        )
        while _should_run:
            try:
                async with session_maker() as db:
                    block_hash = await execute_quality_audit_sealing_cycle(db)
                    if block_hash:
                        logger.info(
                            "Successfully sealed Quality block with hash: %s",
                            block_hash,
                        )
                    # Periodic chain verification check to detect database modifications
                    await validate_quality_ledger_integrity(db)
            except Exception as e:
                logger.error(
                    "Error in Quality audit sealing/verification cycle: %s",
                    e,
                    exc_info=True,
                )

            for _ in range(int(interval * 10)):
                if not _should_run:
                    break
                await asyncio.sleep(0.1)

    _sealer_task = asyncio.create_task(sealer_loop())


async def stop_background_quality_sealer() -> None:
    """
    Stop the asynchronous background Quality ledger sealer thread.
    """
    global _sealer_task, _should_run
    _should_run = False
    if _sealer_task:
        try:
            await _sealer_task
        except asyncio.CancelledError:
            pass
        _sealer_task = None
    logger.info("Background Quality ledger sealer stopped.")
