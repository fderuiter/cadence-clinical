"""Cryptographic verification of eTMF immutable audit ledger chains and Merkle block seals."""

import hashlib
import json
from collections.abc import Sequence
from typing import Any


def calculate_record_hash(rec: Any) -> str:
    """Calculates deterministic SHA-256 hash of a single audit log record."""
    timestamp_str = str(rec.timestamp)
    record_payload = {
        "id": str(rec.id),
        "timestamp": timestamp_str,
        "user_id": str(rec.user_id),
        "user_role": str(rec.user_role),
        "action": str(rec.action),
        "document_id": str(rec.document_id) if rec.document_id is not None else None,
        "details": str(rec.details),
    }
    if hasattr(rec, "reason_for_change") and rec.reason_for_change is not None:
        record_payload["reason_for_change"] = str(rec.reason_for_change)
    elif (
        hasattr(rec, "_mapping")
        and "reason_for_change" in getattr(rec, "_mapping", {})
        and rec._mapping["reason_for_change"] is not None
    ):
        record_payload["reason_for_change"] = str(rec._mapping["reason_for_change"])

    serialized = json.dumps(record_payload, sort_keys=True).encode("utf-8")
    return hashlib.sha256(serialized).hexdigest()


def compute_merkle_root_from_hashes(record_hashes: Sequence[str]) -> str:
    """Computes the Merkle Root hash from a list of record hashes."""
    combined_records_payload = "".join(record_hashes).encode("utf-8")
    return hashlib.sha256(combined_records_payload).hexdigest()


def calculate_merkle_root_for_records(logs: Sequence[Any]) -> str:
    """Calculates the cryptographic Merkle root hash for a sequential list of audit log records."""
    if not logs:
        return hashlib.sha256(b"EMPTY_BLOCK").hexdigest()

    record_hashes = [calculate_record_hash(rec) for rec in logs]
    return compute_merkle_root_from_hashes(record_hashes)


def verify_etmf_ledger_chain_report(
    seals: Sequence[Any],
    unsealed_logs: Sequence[Any],
    all_logs: Sequence[Any] | None = None,
) -> dict[str, Any]:
    """Cryptographically inspects and verifies the full Merkle block ledger chain for tampering detection."""
    total_sealed_blocks = len(seals)
    unsealed_count = len(unsealed_logs)

    if total_sealed_blocks == 0:
        return {
            "is_valid": True,
            "total_sealed_blocks": 0,
            "total_sealed_records": 0,
            "latest_block_hash": None,
            "genesis_block_hash": None,
            "unsealed_records_count": unsealed_count,
            "tamper_detected": False,
            "details": "Ledger is valid. No sealed Merkle blocks exist yet (all records unsealed).",
        }

    total_records = sum(getattr(s, "sealed_record_count", 0) for s in seals)
    genesis_seal = seals[0]
    latest_seal = seals[-1]

    # Verify genesis block
    if getattr(genesis_seal, "previous_block_hash", None) not in (
        "0" * 64,
        "GENESIS_HASH",
        None,
        "",
    ):
        return {
            "is_valid": False,
            "total_sealed_blocks": total_sealed_blocks,
            "total_sealed_records": total_records,
            "latest_block_hash": getattr(latest_seal, "current_block_hash", None),
            "genesis_block_hash": getattr(genesis_seal, "current_block_hash", None),
            "unsealed_records_count": unsealed_count,
            "tamper_detected": True,
            "details": f"Genesis block '{getattr(genesis_seal, 'block_index', 1)}' has invalid previous_block_hash.",
        }

    # Verify block chaining
    for i in range(1, len(seals)):
        prev = seals[i - 1]
        curr = seals[i]
        if getattr(curr, "previous_block_hash", None) != getattr(
            prev, "current_block_hash", None
        ):
            return {
                "is_valid": False,
                "total_sealed_blocks": total_sealed_blocks,
                "total_sealed_records": total_records,
                "latest_block_hash": getattr(latest_seal, "current_block_hash", None),
                "genesis_block_hash": getattr(genesis_seal, "current_block_hash", None),
                "unsealed_records_count": unsealed_count,
                "tamper_detected": True,
                "details": f"Ledger chain broken between Block {getattr(prev, 'block_index', i)} and Block {getattr(curr, 'block_index', i + 1)}: hash mismatch.",
            }

    # Check for tamper detection in sealed records
    if all_logs is not None:
        for seal in seals:
            block_seal_hash = getattr(seal, "current_block_hash", "")
            sealed_records = [
                log
                for log in all_logs
                if getattr(log, "cryptographic_seal", None) == block_seal_hash
            ]
            expected_count = getattr(seal, "sealed_record_count", 0)
            if len(sealed_records) != expected_count:
                return {
                    "is_valid": False,
                    "total_sealed_blocks": total_sealed_blocks,
                    "total_sealed_records": total_records,
                    "latest_block_hash": getattr(
                        latest_seal, "current_block_hash", None
                    ),
                    "genesis_block_hash": getattr(
                        genesis_seal, "current_block_hash", None
                    ),
                    "unsealed_records_count": unsealed_count,
                    "tamper_detected": True,
                    "details": f"Integrity violation in Block {getattr(seal, 'block_index', 0)}: expected {expected_count} records, found {len(sealed_records)}.",
                }
            rec_hashes = [calculate_record_hash(r) for r in sealed_records]
            computed_merkle = compute_merkle_root_from_hashes(rec_hashes)
            if computed_merkle != getattr(seal, "merkle_root_hash", ""):
                return {
                    "is_valid": False,
                    "total_sealed_blocks": total_sealed_blocks,
                    "total_sealed_records": total_records,
                    "latest_block_hash": getattr(
                        latest_seal, "current_block_hash", None
                    ),
                    "genesis_block_hash": getattr(
                        genesis_seal, "current_block_hash", None
                    ),
                    "unsealed_records_count": unsealed_count,
                    "tamper_detected": True,
                    "details": f"Merkle root mismatch in Block {getattr(seal, 'block_index', 0)}: record data has been tampered with in storage.",
                }

    return {
        "is_valid": True,
        "total_sealed_blocks": total_sealed_blocks,
        "total_sealed_records": total_records,
        "latest_block_hash": getattr(latest_seal, "current_block_hash", None),
        "genesis_block_hash": getattr(genesis_seal, "current_block_hash", None),
        "unsealed_records_count": unsealed_count,
        "tamper_detected": False,
        "details": f"Ledger chain cryptographically intact across all {total_sealed_blocks} sealed Merkle blocks.",
    }
