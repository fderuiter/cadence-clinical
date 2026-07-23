import asyncio
import hashlib
import json
import logging
import os
from datetime import datetime, timezone
from typing import Any, Optional

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.trial_lock import TrialLockManager

logger = logging.getLogger("sealer")

_sealer_task: Optional[asyncio.Task] = None
_should_run: bool = False


def clean_json_val(val: Any) -> str:
    """
    Ensure consistent, deterministic serialization of JSON values for hashing.
    Parses and formats dictionaries/lists to remove any whitespace or key order differences.
    """
    if val is None:
        return "null"
    if isinstance(val, (dict, list)):
        return json.dumps(val, sort_keys=True)
    if isinstance(val, str):
        try:
            parsed = json.loads(val)
            return json.dumps(parsed, sort_keys=True)
        except Exception:
            return json.dumps(val)
    return json.dumps(val)


def clean_query(query_str: str, db: AsyncSession) -> str:
    """
    Strips 'audit_schema.' schema prefix from raw SQL queries when running on SQLite
    to maintain dialect-agnostic compatibility in tests.
    """
    if db.bind.dialect.name == "sqlite":
        return query_str.replace("audit_schema.", "")
    return query_str


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
    # 1. Fetch the last valid block hash
    last_block_query = await db.execute(
        text(
            clean_query(
                "SELECT current_block_hash FROM audit_schema.audit_ledger_seals ORDER BY block_index DESC LIMIT 1;",
                db,
            )
        )
    )
    result = last_block_query.fetchone()
    previous_hash = result[0] if result else "0" * 64

    # 2. Fetch all unsealed audit records
    unsealed_query = await db.execute(
        text(
            clean_query(
                "SELECT id, table_name, record_id, action, user_id, timestamp, old_values, new_values, version_index, change_reason "
                "FROM audit_schema.audit_logs WHERE cryptographic_seal IS NULL ORDER BY timestamp ASC, id ASC LIMIT :limit;",
                db,
            )
        ),
        {"limit": limit},
    )
    records = unsealed_query.fetchall()
    if not records:
        return None  # No new logs to seal

    record_hashes = []
    record_ids = []

    for rec in records:
        timestamp_str = (
            rec.timestamp.isoformat()
            if hasattr(rec.timestamp, "isoformat")
            else str(rec.timestamp)
        )

        # Create deterministic payload dict matching the exact schema structure
        record_payload = {
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
        serialized = json.dumps(record_payload, sort_keys=True).encode("utf-8")
        rec_hash = hashlib.sha256(serialized).hexdigest()
        record_hashes.append(rec_hash)
        record_ids.append(rec.id)

    # 3. Calculate Merkle Root of records
    combined_records_payload = "".join(record_hashes).encode("utf-8")
    merkle_root = hashlib.sha256(combined_records_payload).hexdigest()

    # 4. Calculate Block Hash
    block_input = (previous_hash + merkle_root).encode("utf-8")
    current_block_hash = hashlib.sha256(block_input).hexdigest()

    # 5. Insert Ledger Seal Record
    await db.execute(
        text(
            clean_query(
                "INSERT INTO audit_schema.audit_ledger_seals (previous_block_hash, current_block_hash, timestamp, sealed_record_count, merkle_root_hash) "
                "VALUES (:prev, :curr, :timestamp, :count, :merkle);",
                db,
            )
        ),
        {
            "prev": previous_hash,
            "curr": current_block_hash,
            "timestamp": datetime.now(timezone.utc).replace(tzinfo=None),
            "count": len(records),
            "merkle": merkle_root,
        },
    )

    # 6. Apply cryptographic seal to audited records in database
    for rec_id in record_ids:
        await db.execute(
            text(
                clean_query(
                    "UPDATE audit_schema.audit_logs SET cryptographic_seal = :seal WHERE id = :id;",
                    db,
                )
            ),
            {"seal": current_block_hash, "id": rec_id},
        )

    await db.commit()
    return current_block_hash


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
    try:
        # Fetch all seals in order of block_index
        seals_query = await db.execute(
            text(
                clean_query(
                    "SELECT block_index, previous_block_hash, current_block_hash, sealed_record_count, merkle_root_hash "
                    "FROM audit_schema.audit_ledger_seals ORDER BY block_index ASC;",
                    db,
                )
            )
        )
        seals = seals_query.fetchall()

        expected_prev_hash = "0" * 64

        for seal in seals:
            block_idx = seal.block_index
            prev_hash_in_db = seal.previous_block_hash
            curr_hash_in_db = seal.current_block_hash
            record_count_in_db = seal.sealed_record_count
            merkle_root_in_db = seal.merkle_root_hash

            # 1. Chain validation
            if prev_hash_in_db != expected_prev_hash:
                raise ValueError(
                    f"Chain broken at block {block_idx}: expected previous hash '{expected_prev_hash}', got '{prev_hash_in_db}'."
                )

            # 2. Fetch all records associated with this seal
            records_query = await db.execute(
                text(
                    clean_query(
                        "SELECT id, table_name, record_id, action, user_id, timestamp, old_values, new_values, version_index, change_reason "
                        "FROM audit_schema.audit_logs WHERE cryptographic_seal = :seal ORDER BY timestamp ASC, id ASC;",
                        db,
                    )
                ),
                {"seal": curr_hash_in_db},
            )
            records = records_query.fetchall()

            # Verify record count
            if len(records) != record_count_in_db:
                raise ValueError(
                    f"Integrity violation at block {block_idx}: DB has {len(records)} records for seal, but seal expects {record_count_in_db}."
                )

            record_hashes = []
            for rec in records:
                timestamp_str = (
                    rec.timestamp.isoformat()
                    if hasattr(rec.timestamp, "isoformat")
                    else str(rec.timestamp)
                )
                record_payload = {
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
                        str(rec.change_reason)
                        if rec.change_reason is not None
                        else None
                    ),
                }
                serialized = json.dumps(record_payload, sort_keys=True).encode("utf-8")
                rec_hash = hashlib.sha256(serialized).hexdigest()
                record_hashes.append(rec_hash)

            # Recompute Merkle root
            combined_records_payload = "".join(record_hashes).encode("utf-8")
            computed_merkle_root = hashlib.sha256(combined_records_payload).hexdigest()

            if computed_merkle_root != merkle_root_in_db:
                raise ValueError(
                    f"Integrity violation at block {block_idx}: Recomputed Merkle root '{computed_merkle_root}' "
                    f"does not match stored Merkle root '{merkle_root_in_db}'."
                )

            # Recompute Block Hash
            block_input = (expected_prev_hash + computed_merkle_root).encode("utf-8")
            computed_block_hash = hashlib.sha256(block_input).hexdigest()

            if computed_block_hash != curr_hash_in_db:
                raise ValueError(
                    f"Integrity violation at block {block_idx}: Recomputed block hash '{computed_block_hash}' "
                    f"does not match stored current block hash '{curr_hash_in_db}'."
                )

            expected_prev_hash = curr_hash_in_db

        # Verify no orphan seals exist on logs
        all_seals_in_db = {s.current_block_hash for s in seals}
        sealed_records_query = await db.execute(
            text(
                clean_query(
                    "SELECT DISTINCT cryptographic_seal FROM audit_schema.audit_logs WHERE cryptographic_seal IS NOT NULL;",
                    db,
                )
            )
        )
        distinct_seals_on_records = {r[0] for r in sealed_records_query.fetchall()}

        orphan_seals = distinct_seals_on_records - all_seals_in_db
        if orphan_seals:
            raise ValueError(f"Found orphan seals on audit logs: {orphan_seals}")

        return True
    except Exception as e:
        TrialLockManager.lock_trial(reason=f"GxP Core Data Integrity Breach: {str(e)}")
        raise ValueError(f"GxP Core Data Integrity Breach: {str(e)}") from e


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
