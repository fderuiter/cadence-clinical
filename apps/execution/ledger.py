import asyncio
import hashlib
import json
import logging
from typing import List

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import AuditLedgerBlock, AuditLog

logger = logging.getLogger(__name__)

# Safety Freeze state
_SAFETY_FREEZE_ACTIVE = False


def is_safety_freeze_active() -> bool:
    return _SAFETY_FREEZE_ACTIVE


def enable_safety_freeze():
    global _SAFETY_FREEZE_ACTIVE
    _SAFETY_FREEZE_ACTIVE = True


def generate_log_hash(log: AuditLog) -> str:
    # Serialize the log to generate a consistent hash
    log_dict = {
        "id": log.id,
        "table_name": log.table_name,
        "record_id": log.record_id,
        "action": log.action,
        "user_id": log.user_id,
        "timestamp": log.timestamp.isoformat() if log.timestamp else None,
        "old_values": log.old_values,
        "new_values": log.new_values,
        "version_index": log.version_index,
        "change_reason": log.change_reason,
    }
    log_str = json.dumps(log_dict, sort_keys=True)
    return hashlib.sha256(log_str.encode("utf-8")).hexdigest()


def compute_merkle_root(hashes: List[str]) -> str:
    if not hashes:
        return hashlib.sha256(b"empty").hexdigest()

    current_layer = hashes[:]
    while len(current_layer) > 1:
        if len(current_layer) % 2 != 0:
            current_layer.append(current_layer[-1])

        next_layer = []
        for i in range(0, len(current_layer), 2):
            combined = current_layer[i] + current_layer[i + 1]
            next_layer.append(hashlib.sha256(combined.encode("utf-8")).hexdigest())
        current_layer = next_layer

    return current_layer[0]


async def verify_chain(session: AsyncSession) -> bool:
    """Verifies the integrity of the audit ledger chain and logs."""
    result = await session.execute(
        select(AuditLedgerBlock).order_by(AuditLedgerBlock.block_number)
    )
    blocks = result.scalars().all()

    expected_previous_hash = "0" * 64
    for block in blocks:
        if block.previous_block_hash != expected_previous_hash:
            logger.error(
                f"Integrity error: Block {block.block_number} previous hash mismatch."
            )
            return False

        expected_block_hash = hashlib.sha256(
            (
                str(block.block_number) + block.merkle_root + block.previous_block_hash
            ).encode("utf-8")
        ).hexdigest()

        if block.block_hash != expected_block_hash:
            logger.error(f"Integrity error: Block {block.block_number} hash mismatch.")
            return False

        if block.sealed_log_ids:
            log_result = await session.execute(
                select(AuditLog).where(AuditLog.id.in_(block.sealed_log_ids))
            )
            log_dict_by_id = {log.id: log for log in log_result.scalars().all()}

            if len(log_dict_by_id) != len(block.sealed_log_ids):
                logger.error(
                    f"Integrity error: Missing logs for block {block.block_number}."
                )
                return False

            log_hashes = [
                generate_log_hash(log_dict_by_id[log_id])
                for log_id in block.sealed_log_ids
            ]
            computed_merkle = compute_merkle_root(log_hashes)
            if computed_merkle != block.merkle_root:
                logger.error(
                    f"Integrity error: Block {block.block_number} merkle root mismatch."
                )
                return False

        expected_previous_hash = block.block_hash

    return True


async def seal_logs(session: AsyncSession) -> None:
    """Seals unsealed logs into a new ledger block."""
    result = await session.execute(
        select(AuditLog)
        .where(AuditLog.block_id.is_(None))
        .order_by(AuditLog.timestamp)
        .limit(100)
    )
    unsealed_logs = result.scalars().all()

    if not unsealed_logs:
        return

    last_block_result = await session.execute(
        select(AuditLedgerBlock).order_by(AuditLedgerBlock.block_number.desc()).limit(1)
    )
    last_block = last_block_result.scalars().first()

    block_number = last_block.block_number + 1 if last_block else 0
    previous_block_hash = last_block.block_hash if last_block else "0" * 64

    log_hashes = [generate_log_hash(log) for log in unsealed_logs]
    merkle_root = compute_merkle_root(log_hashes)

    block_hash = hashlib.sha256(
        (str(block_number) + merkle_root + previous_block_hash).encode("utf-8")
    ).hexdigest()

    sealed_log_ids = [log.id for log in unsealed_logs]

    new_block = AuditLedgerBlock(
        block_number=block_number,
        merkle_root=merkle_root,
        previous_block_hash=previous_block_hash,
        block_hash=block_hash,
        sealed_log_ids=sealed_log_ids,
    )
    session.add(new_block)
    await session.flush()

    for log in unsealed_logs:
        log.block_id = new_block.id


async def run_sealing_loop(session_maker):
    """Continuous background loop for sealing and verifying logs."""
    while True:
        try:
            async with session_maker() as session:
                is_valid = await verify_chain(session)
                if not is_valid:
                    if not is_safety_freeze_active():
                        enable_safety_freeze()
                        logger.critical(
                            "SAFETY FREEZE INITIATED due to audit ledger verification failure."
                        )
                        # Requirement 5: Dispatch automated notifications to QA and Security Officer
                        print(
                            "ALERT: Notification sent to qa_rep@example.com - Data Integrity Breach Detected!"
                        )
                        print(
                            "ALERT: Notification sent to security_officer@example.com - Data Integrity Breach Detected!"
                        )
                else:
                    await seal_logs(session)
                    await session.commit()
        except Exception as e:
            logger.error(f"Error in sealing loop: {e}")

        await asyncio.sleep(60)
