"""Cell & Gene Therapy (CGT) Vein-to-Vein Chain of Custody / Identity (COC/COI) & Drug Destruction Engine.

Requirements: PRD-SCM-005, PRD-SCM-006, ADR-2199, ISBT 128
"""

import base64
import hashlib
import hmac
import json
import os
import uuid
from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import (
    CGTBedsideVerification,
    CGTChainOfCustody,
    CGTDestructionCertificate,
    CGTMilestoneEvent,
    CGTVialReturnReconciliation,
    IntegrationOutbox,
    IPKit,
)
from apps.execution.domain.rtsm_models import (
    CGTCustodyStatus,
    CGTStage,
    CryogenicTempStatus,
    TamperEvidentBarcode,
    VialDisposition,
)
from apps.execution.trial_lock import TrialLockManager

# Standard secret for HMAC signatures in CGT barcode & unlock token generation
CGT_CRYPTO_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-cgt-crypto-secret-key-12345"
).encode("utf-8")


def _assert_unlocked(study_id: str | None = None, site_id: str | None = None) -> None:
    """Ensures trial and site are not locked against data mutation."""
    from fastapi import HTTPException

    if TrialLockManager.is_locked():
        raise HTTPException(
            status_code=423,
            detail="Locked: Trial system is currently locked against changes.",
        )
    if site_id and TrialLockManager.is_site_locked(site_id):
        raise HTTPException(
            status_code=423,
            detail=f"Locked: Site {site_id} is currently locked against changes.",
        )


# =========================================================================
# Custom Exceptions
# =========================================================================


class CGTChainOfCustodyNotFoundError(Exception):
    """Raised when a Cell & Gene Therapy Chain of Custody record is not found."""

    pass


class InvalidDINError(Exception):
    """Raised when an ISBT 128 Donation Identification Number fails structural or checksum validation."""

    pass


class BarcodeTamperError(Exception):
    """Raised when a cryptographic barcode payload fails HMAC integrity verification."""

    pass


class BedsideVerificationMismatchError(Exception):
    """Raised when bedside scanning detects identity mismatch between wristband, bag barcode, or batch."""

    pass


class DuplicateWitnessError(Exception):
    """Raised when primary and secondary e-signatures are attempted by the same individual."""

    pass


class InfusionLockedError(Exception):
    """Raised when attempting drug infusion without valid two-person bedside verification token."""

    pass


class CryogenicExcursionError(Exception):
    """Raised when attempting cell release or infusion under unresolved cryogenic thermal breach."""

    pass


class DestructionCertificateNotFoundError(Exception):
    """Raised when a Certificate of Destruction cannot be found."""

    pass


class ReconciliationNotFoundError(Exception):
    """Raised when a vial return reconciliation record cannot be found."""

    pass


# =========================================================================
# ISBT 128 Donation Identification Number (DIN) Algorithmic Engine
# =========================================================================

# ISO 7064 Mod 37-2 character set for ISBT 128 check character calculations
ISO7064_CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ*"


def calculate_iso7064_mod37_2(data: str) -> str:
    """Calculates ISO 7064 Mod 37-2 check character for ISBT 128 DIN validation.

    Args:
        data: Alphanumeric input string (e.g. facility code + year + sequence).

    Returns:
        Single check character from ISO 7064 Mod 37-2 character set.
    """
    clean = data.upper().replace("=", "").replace(" ", "").replace("-", "")
    p = 36
    for char in clean:
        val = ISO7064_CHARSET.find(char)
        if val == -1:
            val = 0
        s = (p + val) % 36
        if s == 0:
            s = 36
        p = (s * 2) % 37
    check_val = (38 - p) % 37
    if check_val < len(ISO7064_CHARSET):
        return ISO7064_CHARSET[check_val]
    return "0"


def generate_isbt128_din(
    facility_code: str = "W1234",
    year: int | None = None,
    sequence_num: int | None = None,
) -> str:
    """Generates an ISBT 128 Donation Identification Number (DIN) with ISO 7064 checksum.

    Format: [Facility: 5 chars][Year: 2 digits][Sequence: 6 digits][Check: 2 chars]
    Example: W12342600000100

    Args:
        facility_code: 5-character facility identifier (e.g. W1234, A9999).
        year: 2-digit collection year (defaults to current year % 100).
        sequence_num: Sequential integer donation number (1..999999).

    Returns:
        Formatted 15-character ISBT 128 DIN string.
    """
    fac = facility_code.upper().replace("=", "").strip()
    if len(fac) < 5:
        fac = fac.ljust(5, "0")
    elif len(fac) > 5:
        fac = fac[:5]

    current_yr = (
        year
        if (year is not None and 0 <= year <= 99)
        else (datetime.now(UTC).year % 100)
    )
    seq = (
        sequence_num
        if (sequence_num is not None and sequence_num > 0)
        else (uuid.uuid4().int % 900000 + 100000)
    )
    seq_str = f"{seq % 1000000:06d}"
    core_din = f"{fac}{current_yr:02d}{seq_str}"
    chk1 = calculate_iso7064_mod37_2(core_din)
    chk2 = calculate_iso7064_mod37_2(core_din + chk1)
    return f"{core_din}{chk1}{chk2}"


def validate_isbt128_din(din: str) -> bool:
    """Validates structural correctness and ISO 7064 checksum of an ISBT 128 DIN.

    Args:
        din: Raw or formatted DIN string.

    Returns:
        True if DIN is structurally valid and check characters match, False otherwise.
    """
    if not din:
        return False
    clean = din.upper().replace("=", "").replace(" ", "").replace("-", "").strip()
    if len(clean) < 13:
        return False
    # Check facility code, year, sequence format
    if len(clean) >= 15:
        core_din = clean[:13]
        expected_chk1 = calculate_iso7064_mod37_2(core_din)
        expected_chk2 = calculate_iso7064_mod37_2(core_din + expected_chk1)
        actual_chk = clean[13:15]
        return actual_chk == f"{expected_chk1}{expected_chk2}"
    # If 13 chars provided without checksum, core structure must be alphanumeric
    return clean.isalnum()


# =========================================================================
# Cryptographic Tamper-Evident Barcode Generation & Verification
# =========================================================================


def generate_crypto_barcode(
    study_id: str,
    site_id: str,
    subject_id: str,
    din: str,
    batch_id: str | None = None,
    secret_key: bytes = CGT_CRYPTO_SECRET,
) -> TamperEvidentBarcode:
    """Generates a cryptographic barcode binding subject UUID, ISBT 128 DIN, and batch.

    Args:
        study_id: Clinical trial identifier.
        site_id: Clinical site identifier.
        subject_id: Primary subject identifier / UUID.
        din: Donation Identification Number.
        batch_id: Optional cell manufacturing lot/batch number.
        secret_key: Secret HMAC key.

    Returns:
        TamperEvidentBarcode with HMAC-SHA256 signature and serialized payload.
    """
    barcode_id = f"BAR-{uuid.uuid4().hex[:12].upper()}"
    created_at = datetime.now(UTC)
    ts_str = created_at.isoformat()

    # Canonical message representation
    canonical_msg = (
        f"BARCODE_ID:{barcode_id}|STUDY:{study_id}|SITE:{site_id}|"
        f"SUBJ:{subject_id}|DIN:{din}|BATCH:{batch_id or ''}|TIME:{ts_str}"
    )

    crypto_hash = hmac.new(
        secret_key, canonical_msg.encode("utf-8"), hashlib.sha256
    ).hexdigest()

    payload_dict = {
        "barcode_id": barcode_id,
        "study_id": study_id,
        "site_id": site_id,
        "subject_id": subject_id,
        "din": din,
        "batch_id": batch_id,
        "created_at": ts_str,
        "crypto_hash": crypto_hash,
    }
    encoded_payload = base64.urlsafe_b64encode(
        json.dumps(payload_dict, sort_keys=True).encode("utf-8")
    ).decode("utf-8")

    return TamperEvidentBarcode(
        barcode_id=barcode_id,
        study_id=study_id,
        site_id=site_id,
        subject_id=subject_id,
        din=din,
        batch_id=batch_id,
        created_at=created_at,
        crypto_hash=crypto_hash,
        barcode_payload=encoded_payload,
    )


def verify_crypto_barcode(
    barcode_payload: str,
    expected_subject_id: str | None = None,
    expected_din: str | None = None,
    expected_batch_id: str | None = None,
    secret_key: bytes = CGT_CRYPTO_SECRET,
) -> tuple[bool, str | None, dict]:
    """Verifies HMAC-SHA256 cryptographic seal and entity bindings on a scanned barcode.

    Args:
        barcode_payload: Base64-encoded barcode payload or JSON string.
        expected_subject_id: Optional expected subject UUID for verification.
        expected_din: Optional expected DIN.
        expected_batch_id: Optional expected manufacturing batch ID.
        secret_key: Secret HMAC key.

    Returns:
        Tuple of (is_valid: bool, failure_reason: str | None, decoded_data: dict).
    """
    try:
        if barcode_payload.startswith("{"):
            data = json.loads(barcode_payload)
        else:
            decoded_bytes = base64.urlsafe_b64decode(barcode_payload.encode("utf-8"))
            data = json.loads(decoded_bytes.decode("utf-8"))
    except Exception as exc:
        return False, f"Barcode payload corrupted or not decodable: {exc}", {}

    required_fields = [
        "barcode_id",
        "study_id",
        "site_id",
        "subject_id",
        "din",
        "crypto_hash",
    ]
    for field in required_fields:
        if field not in data:
            return False, f"Missing required cryptographic barcode field: {field}", data

    # Reconstruct canonical message and recompute HMAC
    barcode_id = data["barcode_id"]
    study_id = data["study_id"]
    site_id = data["site_id"]
    subject_id = data["subject_id"]
    din = data["din"]
    batch_id = data.get("batch_id")
    ts_str = data.get("created_at", "")
    provided_hash = data["crypto_hash"]

    canonical_msg = (
        f"BARCODE_ID:{barcode_id}|STUDY:{study_id}|SITE:{site_id}|"
        f"SUBJ:{subject_id}|DIN:{din}|BATCH:{batch_id or ''}|TIME:{ts_str}"
    )
    expected_hash = hmac.new(
        secret_key, canonical_msg.encode("utf-8"), hashlib.sha256
    ).hexdigest()

    if not hmac.compare_digest(provided_hash, expected_hash):
        return (
            False,
            "Cryptographic seal mismatch: barcode has been tampered with.",
            data,
        )

    if expected_subject_id and subject_id.strip() != expected_subject_id.strip():
        return (
            False,
            f"Subject mismatch: barcode binds to {subject_id}, expected {expected_subject_id}",
            data,
        )

    if expected_din and din.strip() != expected_din.strip():
        return (
            False,
            f"DIN mismatch: barcode binds to {din}, expected {expected_din}",
            data,
        )

    if expected_batch_id and batch_id and batch_id.strip() != expected_batch_id.strip():
        return (
            False,
            f"Batch mismatch: barcode binds to {batch_id}, expected {expected_batch_id}",
            data,
        )

    return True, None, data


# =========================================================================
# Vein-to-Vein Chain of Custody (COC) & Lifecycle Milestone Transactions
# =========================================================================


async def initialize_apheresis_coc_transaction(
    session: AsyncSession,
    study_id: str,
    site_id: str,
    subject_id: str,
    collector_id: str,
    collector_name: str,
    collector_role: str,
    initial_temp_celsius: float = -196.0,
    din: str | None = None,
    dry_shipper_id: str | None = None,
    seal_id: str | None = None,
    notes: str | None = None,
) -> CGTChainOfCustody:
    """Registers an initial apheresis collection and initializes the Vein-to-Vein COC record.

    Conforms to PRD-SCM-005.
    """
    _assert_unlocked(study_id, site_id)

    # Validate or generate ISBT 128 DIN
    assigned_din = (
        din
        if (din and validate_isbt128_din(din))
        else generate_isbt128_din(facility_code=site_id[:5])
    )

    # Check if DIN already exists
    stmt_check = select(CGTChainOfCustody).where(
        CGTChainOfCustody.study_id == study_id,
        CGTChainOfCustody.din == assigned_din,
    )
    res_check = await session.execute(stmt_check)
    if res_check.scalar_one_or_none():
        raise InvalidDINError(
            f"Chain of Custody record for DIN {assigned_din} already exists in study {study_id}."
        )

    # Generate initial tamper-evident barcode
    barcode = generate_crypto_barcode(
        study_id=study_id,
        site_id=site_id,
        subject_id=subject_id,
        din=assigned_din,
        batch_id=None,
    )

    now = datetime.now(UTC)
    coc = CGTChainOfCustody(
        study_id=study_id,
        subject_id=subject_id,
        din=assigned_din,
        batch_id=None,
        current_stage=CGTStage.APHERESIS_COLLECTION,
        status=CGTCustodyStatus.COLLECTED,
        tamper_evident_barcode=barcode.barcode_payload,
        barcode_signature=barcode.crypto_hash,
        collection_site_id=site_id,
        collection_timestamp=now,
        manufacturing_facility_id=None,
        cryo_temp_excursion_count=0,
        lowest_temp_celsius=initial_temp_celsius,
        highest_temp_celsius=initial_temp_celsius,
        infusion_unlocked=False,
        infusion_unlock_token=None,
    )
    session.add(coc)
    await session.flush()

    # Log initial milestone
    milestone = CGTMilestoneEvent(
        study_id=study_id,
        chain_of_custody_id=str(coc.id),
        stage=CGTStage.APHERESIS_COLLECTION,
        milestone_type="APHERESIS_COLLECTION",
        timestamp=now,
        location_name=f"Site {site_id} Apheresis Collection Center",
        facility_id=site_id,
        latitude=None,
        longitude=None,
        temperature_celsius=initial_temp_celsius,
        temperature_status=CryogenicTempStatus.OPTIMAL
        if initial_temp_celsius <= -150.0
        else CryogenicTempStatus.WARNING,
        custodian_id=collector_id,
        custodian_name=collector_name,
        custodian_role=collector_role,
        carrier=None,
        tracking_number=None,
        seal_intact=True,
        seal_id=seal_id,
        dry_shipper_id=dry_shipper_id,
        electronic_signature={
            "signed_by": collector_id,
            "role": collector_role,
            "timestamp": now.isoformat(),
        },
        notes=notes or "Initial apheresis cellular harvest successfully completed.",
    )
    session.add(milestone)

    # Dispatch outbox event
    outbox = IntegrationOutbox(
        event_type="rtsm.cgt.apheresis_collected.v1",
        payload={
            "coc_id": str(coc.id),
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": subject_id,
            "din": assigned_din,
            "barcode_id": barcode.barcode_id,
            "timestamp": now.isoformat(),
        },
        correlation_id=f"cgt-apheresis-{coc.id}",
        created_by=collector_id or "system",
        reason_for_change=notes or "Apheresis collection recorded",
    )
    session.add(outbox)

    return coc


async def record_cryogenic_milestone_transaction(
    session: AsyncSession,
    coc_id: str,
    stage: str,
    location_name: str,
    temperature_celsius: float,
    custodian_id: str,
    custodian_name: str,
    custodian_role: str,
    facility_id: str | None = None,
    latitude: float | None = None,
    longitude: float | None = None,
    carrier: str | None = None,
    tracking_number: str | None = None,
    seal_intact: bool = True,
    seal_id: str | None = None,
    dry_shipper_id: str | None = None,
    signature_token: str | None = None,
    notes: str | None = None,
) -> CGTMilestoneEvent:
    """Records a cryogenic transit milestone with GPS coordinates and temperature logging.

    Conforms to PRD-SCM-005.
    """
    stmt = select(CGTChainOfCustody).where(CGTChainOfCustody.id == coc_id)
    res = await session.execute(stmt)
    coc = res.scalar_one_or_none()
    if not coc:
        raise CGTChainOfCustodyNotFoundError(
            f"Chain of Custody record {coc_id} not found."
        )

    _assert_unlocked(coc.study_id, coc.collection_site_id or "SITE-01")

    # Evaluate cryogenic temperature integrity
    # For vapor phase LN2: <= -150.0 is OPTIMAL; -150 to -135 is WARNING; > -135 is EXCURSION
    # For dry ice / frozen cell transit: <= -70.0 is OPTIMAL
    if temperature_celsius <= -150.0:
        temp_status = CryogenicTempStatus.OPTIMAL
    elif -150.0 < temperature_celsius <= -135.0:
        temp_status = CryogenicTempStatus.WARNING
    else:
        temp_status = CryogenicTempStatus.EXCURSION

    # Update temperature extremes and excursion counters on the master COC record
    if coc.lowest_temp_celsius is None or temperature_celsius < coc.lowest_temp_celsius:
        coc.lowest_temp_celsius = temperature_celsius
    if (
        coc.highest_temp_celsius is None
        or temperature_celsius > coc.highest_temp_celsius
    ):
        coc.highest_temp_celsius = temperature_celsius

    if temp_status == CryogenicTempStatus.EXCURSION:
        coc.cryo_temp_excursion_count += 1
        # Thermal breach triggers automated quarantine
        if temperature_celsius > -100.0 or not seal_intact:
            coc.status = CGTCustodyStatus.QUARANTINED

    # Advance stage and status
    coc.current_stage = stage
    if coc.status != CGTCustodyStatus.QUARANTINED:
        if stage in (
            CGTStage.COURIER_TRANSIT_OUTBOUND,
            CGTStage.COURIER_TRANSIT_RETURN,
        ):
            coc.status = CGTCustodyStatus.IN_TRANSIT
        elif stage == CGTStage.SITE_RECEIPT_AND_STORAGE:
            coc.status = CGTCustodyStatus.RETURNED_TO_SITE

    now = datetime.now(UTC)
    milestone = CGTMilestoneEvent(
        study_id=coc.study_id,
        chain_of_custody_id=str(coc.id),
        stage=stage,
        milestone_type=f"MILESTONE_{stage}",
        timestamp=now,
        location_name=location_name,
        facility_id=facility_id,
        latitude=latitude,
        longitude=longitude,
        temperature_celsius=temperature_celsius,
        temperature_status=temp_status,
        custodian_id=custodian_id,
        custodian_name=custodian_name,
        custodian_role=custodian_role,
        carrier=carrier,
        tracking_number=tracking_number,
        seal_intact=seal_intact,
        seal_id=seal_id,
        dry_shipper_id=dry_shipper_id,
        electronic_signature={
            "signed_by": custodian_id,
            "role": custodian_role,
            "timestamp": now.isoformat(),
        }
        if signature_token
        else None,
        notes=notes,
    )
    session.add(milestone)

    # Dispatch outbox event
    outbox = IntegrationOutbox(
        event_type="rtsm.cgt.milestone_recorded.v1",
        payload={
            "coc_id": str(coc.id),
            "study_id": coc.study_id,
            "stage": stage,
            "temperature_celsius": temperature_celsius,
            "temperature_status": temp_status,
            "status": coc.status,
            "timestamp": now.isoformat(),
        },
        correlation_id=f"cgt-milestone-{milestone.id}",
        created_by=custodian_id or "system",
        reason_for_change=notes or f"Cryogenic milestone {stage} recorded",
    )
    session.add(outbox)

    return milestone


async def bind_manufacturing_batch_transaction(
    session: AsyncSession,
    coc_id: str,
    batch_id: str,
    manufacturing_facility_id: str,
    operator_id: str,
    operator_name: str,
    operator_role: str = "Manufacturing Specialist",
    temperature_celsius: float = -196.0,
    notes: str | None = None,
) -> CGTChainOfCustody:
    """Binds manufacturing batch record to original subject identity and regenerates cryptographic barcode.

    Conforms to PRD-SCM-005.
    """
    stmt = select(CGTChainOfCustody).where(CGTChainOfCustody.id == coc_id)
    res = await session.execute(stmt)
    coc = res.scalar_one_or_none()
    if not coc:
        raise CGTChainOfCustodyNotFoundError(
            f"Chain of Custody record {coc_id} not found."
        )

    _assert_unlocked(coc.study_id, coc.collection_site_id or "SITE-01")

    # Generate updated tamper-evident barcode now binding the cell manufacturing batch
    new_barcode = generate_crypto_barcode(
        study_id=coc.study_id,
        site_id=coc.collection_site_id or "SITE-01",
        subject_id=coc.subject_id,
        din=coc.din,
        batch_id=batch_id,
    )

    coc.batch_id = batch_id
    coc.manufacturing_facility_id = manufacturing_facility_id
    coc.current_stage = CGTStage.MANUFACTURING_PROCESSING
    coc.status = CGTCustodyStatus.MANUFACTURED
    coc.tamper_evident_barcode = new_barcode.barcode_payload
    coc.barcode_signature = new_barcode.crypto_hash

    now = datetime.now(UTC)
    milestone = CGTMilestoneEvent(
        study_id=coc.study_id,
        chain_of_custody_id=str(coc.id),
        stage=CGTStage.MANUFACTURING_PROCESSING,
        milestone_type="MANUFACTURING_BATCH_BOUND",
        timestamp=now,
        location_name=f"CMO Facility: {manufacturing_facility_id}",
        facility_id=manufacturing_facility_id,
        latitude=None,
        longitude=None,
        temperature_celsius=temperature_celsius,
        temperature_status=CryogenicTempStatus.OPTIMAL,
        custodian_id=operator_id,
        custodian_name=operator_name,
        custodian_role=operator_role,
        carrier=None,
        tracking_number=None,
        seal_intact=True,
        seal_id=None,
        dry_shipper_id=None,
        electronic_signature={
            "signed_by": operator_id,
            "role": operator_role,
            "batch_id": batch_id,
            "timestamp": now.isoformat(),
        },
        notes=notes
        or f"Autologous CAR-T cell engineering batch {batch_id} released and bound to subject {coc.subject_id}.",
    )
    session.add(milestone)

    outbox = IntegrationOutbox(
        event_type="rtsm.cgt.batch_bound.v1",
        payload={
            "coc_id": str(coc.id),
            "study_id": coc.study_id,
            "subject_id": coc.subject_id,
            "din": coc.din,
            "batch_id": batch_id,
            "barcode_id": new_barcode.barcode_id,
            "timestamp": now.isoformat(),
        },
        correlation_id=f"cgt-batch-bound-{coc.id}",
        created_by=operator_id or "system",
        reason_for_change=notes or f"Manufacturing batch {batch_id} bound",
    )
    session.add(outbox)

    return coc


# =========================================================================
# Mandatory Bedside Two-Person Electronic Verification (Dual e-Signature Step-Up)
# =========================================================================


async def verify_bedside_dual_signatures_transaction(
    session: AsyncSession,
    coc_id: str,
    scanned_wristband_subject_id: str,
    scanned_bag_barcode: str,
    primary_verifier_id: str,
    primary_verifier_name: str,
    primary_verifier_role: str,
    primary_signature_token: str,
    secondary_verifier_id: str,
    secondary_verifier_name: str,
    secondary_verifier_role: str,
    secondary_signature_token: str,
    notes: str | None = None,
) -> CGTBedsideVerification:
    """Executes mandatory two-person bedside verification before unlocking the drug infusion eCRF.

    Conforms to PRD-SCM-005:
    - Primary Verifier (e.g. Bedside Nurse)
    - Secondary Verifier (e.g. Principal Investigator / Co-Investigator)
    - Verifies patient wristband UUID against COI subject UUID
    - Cryptographically validates cell bag barcode against original DIN & batch record
    - Validates cold chain integrity (no unresolved temperature quarantine)
    - Generates one-time unlock token for EDC drug infusion eCRF

    Raises:
        DuplicateWitnessError: If primary and secondary verifiers are the same person.
        CGTChainOfCustodyNotFoundError: If COC record does not exist.
        BedsideVerificationMismatchError: If wristband or bag barcode does not match.
        BarcodeTamperError: If bag barcode fails HMAC cryptographic verification.
        CryogenicExcursionError: If product is quarantined or thermally compromised.
    """
    # 1. Enforce distinct two-person dual verification
    if primary_verifier_id.strip().lower() == secondary_verifier_id.strip().lower():
        raise DuplicateWitnessError(
            "GxP Compliance Violation: Bedside two-person verification requires two distinct authorized staff members. "
            f"Primary ({primary_verifier_id}) and Secondary ({secondary_verifier_id}) cannot be identical."
        )

    if not primary_signature_token or not secondary_signature_token:
        raise DuplicateWitnessError(
            "Both primary and secondary electronic signature step-up tokens are required."
        )

    # 2. Retrieve COC record
    stmt = select(CGTChainOfCustody).where(CGTChainOfCustody.id == coc_id)
    res = await session.execute(stmt)
    coc = res.scalar_one_or_none()
    if not coc:
        raise CGTChainOfCustodyNotFoundError(
            f"Chain of Custody record {coc_id} not found."
        )

    _assert_unlocked(coc.study_id, coc.collection_site_id or "SITE-01")

    # 3. Check for thermal quarantine or excursion status
    if coc.status == CGTCustodyStatus.QUARANTINED:
        raise CryogenicExcursionError(
            f"Infusion Blocked: Cell product for subject {coc.subject_id} (DIN: {coc.din}) is in QUARANTINED status "
            f"due to {coc.cryo_temp_excursion_count} cold chain temperature excursions."
        )

    # 4. Verify patient wristband scan against COI Subject UUID
    wristband_matched = scanned_wristband_subject_id.strip() == coc.subject_id.strip()
    if not wristband_matched:
        # Record failed attempt
        failed_ver = CGTBedsideVerification(
            study_id=coc.study_id,
            site_id=coc.collection_site_id or "SITE-01",
            chain_of_custody_id=str(coc.id),
            subject_id=coc.subject_id,
            din=coc.din,
            batch_id=coc.batch_id or "UNKNOWN",
            scanned_wristband_subject_id=scanned_wristband_subject_id,
            scanned_bag_barcode=scanned_bag_barcode,
            wristband_matched=False,
            bag_matched=False,
            temperature_chain_verified=False,
            primary_verifier_id=primary_verifier_id,
            primary_verifier_name=primary_verifier_name,
            primary_verifier_role=primary_verifier_role,
            primary_signature_token=primary_signature_token,
            primary_signed_at=datetime.now(UTC),
            secondary_verifier_id=secondary_verifier_id,
            secondary_verifier_name=secondary_verifier_name,
            secondary_verifier_role=secondary_verifier_role,
            secondary_signature_token=secondary_signature_token,
            secondary_signed_at=datetime.now(UTC),
            status="FAILED",
            unlock_token="NONE",
            verification_notes=f"CRITICAL MISMATCH: Scanned wristband subject '{scanned_wristband_subject_id}' does not match expected subject '{coc.subject_id}'.",
        )
        session.add(failed_ver)
        await session.flush()
        raise BedsideVerificationMismatchError(
            f"Bedside Verification Mismatch: Scanned wristband ({scanned_wristband_subject_id}) "
            f"does not match COI subject identifier ({coc.subject_id})."
        )

    # 5. Cryptographically decode and verify scanned cell bag barcode
    is_valid_barcode, fail_reason, decoded_data = verify_crypto_barcode(
        barcode_payload=scanned_bag_barcode,
        expected_subject_id=coc.subject_id,
        expected_din=coc.din,
        expected_batch_id=coc.batch_id,
    )
    if not is_valid_barcode:
        if "tampered" in (fail_reason or "").lower():
            raise BarcodeTamperError(f"Bedside Infusion Blocked: {fail_reason}")
        raise BedsideVerificationMismatchError(
            f"Bedside Infusion Blocked: {fail_reason}"
        )

    now = datetime.now(UTC)
    # Generate cryptographic one-time unlock token for EDC eCRF
    unlock_token_msg = (
        f"UNLOCK|COC:{coc.id}|SUBJ:{coc.subject_id}|DIN:{coc.din}|BATCH:{coc.batch_id}|"
        f"VER1:{primary_verifier_id}|VER2:{secondary_verifier_id}|TIME:{now.isoformat()}"
    )
    unlock_token = hmac.new(
        CGT_CRYPTO_SECRET, unlock_token_msg.encode("utf-8"), hashlib.sha256
    ).hexdigest()

    # 6. Record successful dual-person verification
    verification = CGTBedsideVerification(
        study_id=coc.study_id,
        site_id=coc.collection_site_id or "SITE-01",
        chain_of_custody_id=str(coc.id),
        subject_id=coc.subject_id,
        din=coc.din,
        batch_id=coc.batch_id or "BATCH-01",
        scanned_wristband_subject_id=scanned_wristband_subject_id,
        scanned_bag_barcode=scanned_bag_barcode,
        wristband_matched=True,
        bag_matched=True,
        temperature_chain_verified=True,
        primary_verifier_id=primary_verifier_id,
        primary_verifier_name=primary_verifier_name,
        primary_verifier_role=primary_verifier_role,
        primary_signature_token=primary_signature_token,
        primary_signed_at=now,
        secondary_verifier_id=secondary_verifier_id,
        secondary_verifier_name=secondary_verifier_name,
        secondary_verifier_role=secondary_verifier_role,
        secondary_signature_token=secondary_signature_token,
        secondary_signed_at=now,
        status="VERIFIED",
        unlock_token=unlock_token,
        verification_notes=notes
        or "Two-person bedside verification passed. Infusion eCRF unlocked.",
    )
    session.add(verification)

    # 7. Update COC state to BEDSIDE_VERIFIED & unlock infusion
    coc.infusion_unlocked = True
    coc.infusion_unlock_token = unlock_token
    coc.current_stage = CGTStage.BEDSIDE_VERIFICATION
    coc.status = CGTCustodyStatus.BEDSIDE_VERIFIED

    # Add milestone
    milestone = CGTMilestoneEvent(
        study_id=coc.study_id,
        chain_of_custody_id=str(coc.id),
        stage=CGTStage.BEDSIDE_VERIFICATION,
        milestone_type="BEDSIDE_DUAL_VERIFICATION",
        timestamp=now,
        location_name=f"Site {coc.collection_site_id or 'SITE-01'} Bedside Infusion Unit",
        facility_id=coc.collection_site_id,
        latitude=None,
        longitude=None,
        temperature_celsius=20.0,
        temperature_status=CryogenicTempStatus.OPTIMAL,
        custodian_id=primary_verifier_id,
        custodian_name=primary_verifier_name,
        custodian_role=primary_verifier_role,
        carrier=None,
        tracking_number=None,
        seal_intact=True,
        seal_id=None,
        dry_shipper_id=None,
        electronic_signature={
            "primary": {
                "user_id": primary_verifier_id,
                "name": primary_verifier_name,
                "role": primary_verifier_role,
                "token": primary_signature_token,
            },
            "secondary": {
                "user_id": secondary_verifier_id,
                "name": secondary_verifier_name,
                "role": secondary_verifier_role,
                "token": secondary_signature_token,
            },
            "timestamp": now.isoformat(),
        },
        notes="Bedside dual-person verification completed. Infusion eCRF form unlocked.",
    )
    session.add(milestone)

    # Outbox event
    outbox = IntegrationOutbox(
        event_type="rtsm.cgt.bedside_verified.v1",
        payload={
            "coc_id": str(coc.id),
            "study_id": coc.study_id,
            "subject_id": coc.subject_id,
            "din": coc.din,
            "batch_id": coc.batch_id,
            "unlock_token": unlock_token,
            "primary_verifier_id": primary_verifier_id,
            "secondary_verifier_id": secondary_verifier_id,
            "timestamp": now.isoformat(),
        },
        correlation_id=f"cgt-bedside-{verification.id}",
        created_by=primary_verifier_id or "system",
        reason_for_change="Bedside dual-person verification completed",
    )
    session.add(outbox)

    return verification


async def execute_infusion_administration_transaction(
    session: AsyncSession,
    coc_id: str,
    unlock_token: str,
    administrator_id: str,
    administrator_name: str,
    administrator_role: str = "Infusion Specialist",
    volume_infused_ml: float = 50.0,
    vital_signs_pre_temp_celsius: float = 37.0,
    vital_signs_post_temp_celsius: float = 37.0,
    notes: str | None = None,
) -> CGTChainOfCustody:
    """Records cellular drug infusion administration, gated by the bedside verification unlock token.

    Conforms to PRD-SCM-005.
    """
    stmt = select(CGTChainOfCustody).where(CGTChainOfCustody.id == coc_id)
    res = await session.execute(stmt)
    coc = res.scalar_one_or_none()
    if not coc:
        raise CGTChainOfCustodyNotFoundError(
            f"Chain of Custody record {coc_id} not found."
        )

    _assert_unlocked(coc.study_id, coc.collection_site_id or "SITE-01")

    # Strict unlock token check
    if not coc.infusion_unlocked or not coc.infusion_unlock_token:
        raise InfusionLockedError(
            "Infusion Gating Error: Infusion eCRF is locked. Bedside two-person electronic verification must be completed first."
        )

    if not hmac.compare_digest(coc.infusion_unlock_token, unlock_token):
        raise InfusionLockedError(
            "Infusion Gating Error: Invalid or expired bedside unlock token."
        )

    now = datetime.now(UTC)
    coc.current_stage = CGTStage.INFUSION_ADMINISTRATION
    coc.status = CGTCustodyStatus.INFUSED
    coc.infused_at = now

    milestone = CGTMilestoneEvent(
        study_id=coc.study_id,
        chain_of_custody_id=str(coc.id),
        stage=CGTStage.INFUSION_ADMINISTRATION,
        milestone_type="INFUSION_ADMINISTERED",
        timestamp=now,
        location_name=f"Site {coc.collection_site_id or 'SITE-01'} Clinical Infusion Suite",
        facility_id=coc.collection_site_id,
        latitude=None,
        longitude=None,
        temperature_celsius=vital_signs_post_temp_celsius,
        temperature_status=CryogenicTempStatus.OPTIMAL,
        custodian_id=administrator_id,
        custodian_name=administrator_name,
        custodian_role=administrator_role,
        carrier=None,
        tracking_number=None,
        seal_intact=True,
        seal_id=None,
        dry_shipper_id=None,
        electronic_signature={
            "signed_by": administrator_id,
            "role": administrator_role,
            "timestamp": now.isoformat(),
        },
        notes=notes
        or f"Autologous cellular product infused. Volume: {volume_infused_ml} mL. Pre/Post vitals normal.",
    )
    session.add(milestone)

    outbox = IntegrationOutbox(
        event_type="rtsm.cgt.infusion_administered.v1",
        payload={
            "coc_id": str(coc.id),
            "study_id": coc.study_id,
            "subject_id": coc.subject_id,
            "din": coc.din,
            "batch_id": coc.batch_id,
            "volume_infused_ml": volume_infused_ml,
            "administered_by": administrator_id,
            "timestamp": now.isoformat(),
        },
        correlation_id=f"cgt-infusion-{coc.id}",
        created_by=administrator_id or "system",
        reason_for_change=notes
        or f"Autologous cellular product infused ({volume_infused_ml} mL)",
    )
    session.add(outbox)

    return coc


# =========================================================================
# IP Returns, Accountability & Reconciliation Destruction Engine
# =========================================================================


async def log_vial_return_reconciliation_transaction(
    session: AsyncSession,
    study_id: str,
    site_id: str,
    subject_id: str,
    kit_number: str,
    vial_identifier: str,
    lot_number: str,
    expected_quantity: int = 1,
    returned_quantity: int = 1,
    intact_vials: int = 0,
    used_vials: int = 1,
    residual_volume_ml: float = 0.0,
    din: str | None = None,
    logged_by: str = "site_coordinator",
    disposition: str = VialDisposition.RECONCILED_FOR_DESTRUCTION,
) -> CGTVialReturnReconciliation:
    """Logs returned kit/vial accountability and calculates patient adherence compliance.

    Conforms to PRD-SCM-006.
    """
    _assert_unlocked(study_id, site_id)

    # Adherence formula: % of prescribed units consumed
    consumed = expected_quantity - intact_vials
    adherence_pct = (
        100.0
        if expected_quantity <= 0
        else max(0.0, min(100.0, (consumed / expected_quantity) * 100.0))
    )

    now = datetime.now(UTC)
    recon = CGTVialReturnReconciliation(
        study_id=study_id,
        site_id=site_id,
        subject_id=subject_id,
        kit_number=kit_number,
        vial_identifier=vial_identifier,
        lot_number=lot_number,
        din=din,
        expected_quantity=expected_quantity,
        returned_quantity=returned_quantity,
        intact_vials=intact_vials,
        used_vials=used_vials,
        residual_volume_ml=residual_volume_ml,
        adherence_percentage=round(adherence_pct, 2),
        disposition=disposition,
        logged_by=logged_by,
        logged_at=now,
        certificate_id=None,
    )
    session.add(recon)
    await session.flush()

    outbox = IntegrationOutbox(
        event_type="rtsm.cgt.vial_return_reconciled.v1",
        payload={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": subject_id,
            "kit_number": kit_number,
            "vial_identifier": vial_identifier,
            "adherence_percentage": adherence_pct,
            "logged_by": logged_by,
            "timestamp": now.isoformat(),
        },
        correlation_id=f"cgt-return-{recon.id}",
        created_by=logged_by or "system",
        reason_for_change=f"Vial return reconciliation logged for kit {kit_number}",
    )
    session.add(outbox)

    return recon


async def generate_certificate_of_destruction_transaction(
    session: AsyncSession,
    study_id: str,
    site_id: str,
    lot_number: str,
    kit_numbers: list[str],
    vial_identifiers: list[str],
    destruction_reason: str,
    destruction_method: str,
    destruction_facility: str,
    witness_1_id: str,
    witness_1_name: str,
    witness_1_role: str,
    witness_1_signature_token: str,
    witness_2_id: str,
    witness_2_name: str,
    witness_2_role: str,
    witness_2_signature_token: str,
    quantity_destroyed: int = 1,
    residual_volume_ml: float | None = None,
    subject_id: str | None = None,
    din: str | None = None,
    coc_id: str | None = None,
    notes: str | None = None,
) -> CGTDestructionCertificate:
    """Generates formal Certificate of Destruction with dual witness electronic signatures.

    Conforms to PRD-SCM-006 & 21 CFR Part 11:
    - Enforces two distinct witness signatures (witness_1 != witness_2)
    - Computes cryptographic SHA-256 seal
    - Updates IPKit status to DESTROYED
    - Updates COC record to DESTROYED if applicable

    Raises:
        DuplicateWitnessError: If witness 1 and witness 2 are the same individual.
    """
    _assert_unlocked(study_id, site_id)

    # 1. Enforce distinct witnesses
    if witness_1_id.strip().lower() == witness_2_id.strip().lower():
        raise DuplicateWitnessError(
            "GxP Compliance Violation: Certificate of Destruction requires dual witness signatures from two distinct staff members. "
            f"Witness 1 ({witness_1_id}) and Witness 2 ({witness_2_id}) cannot be the same user."
        )

    if not witness_1_signature_token or not witness_2_signature_token:
        raise DuplicateWitnessError(
            "Both witness 1 and witness 2 electronic signatures are mandatory."
        )

    now = datetime.now(UTC)
    cert_num = f"COD-{now.year}-{uuid.uuid4().hex[:8].upper()}"

    # 2. Compute SHA-256 seal
    seal_data = {
        "certificate_number": cert_num,
        "study_id": study_id,
        "site_id": site_id,
        "lot_number": lot_number,
        "kit_numbers": sorted(kit_numbers),
        "vial_identifiers": sorted(vial_identifiers),
        "quantity_destroyed": quantity_destroyed,
        "destruction_method": destruction_method,
        "destruction_facility": destruction_facility,
        "witness_1": witness_1_id,
        "witness_2": witness_2_id,
        "timestamp": now.isoformat(),
    }
    cryptographic_seal = hashlib.sha256(
        json.dumps(seal_data, sort_keys=True).encode("utf-8")
    ).hexdigest()

    # 3. Create Certificate of Destruction
    cert = CGTDestructionCertificate(
        study_id=study_id,
        site_id=site_id,
        certificate_number=cert_num,
        chain_of_custody_id=coc_id,
        subject_id=subject_id,
        din=din,
        lot_number=lot_number,
        kit_numbers=kit_numbers,
        vial_identifiers=vial_identifiers,
        quantity_destroyed=quantity_destroyed,
        unit_of_measure="VIALS",
        residual_volume_ml=residual_volume_ml,
        destruction_reason=destruction_reason,
        destruction_method=destruction_method,
        destruction_facility=destruction_facility,
        destruction_timestamp=now,
        witness_1_id=witness_1_id,
        witness_1_name=witness_1_name,
        witness_1_role=witness_1_role,
        witness_1_signature_token=witness_1_signature_token,
        witness_1_signed_at=now,
        witness_2_id=witness_2_id,
        witness_2_name=witness_2_name,
        witness_2_role=witness_2_role,
        witness_2_signature_token=witness_2_signature_token,
        witness_2_signed_at=now,
        cryptographic_seal=cryptographic_seal,
        status="COMPLETED",
        reconciliation_notes=notes,
    )
    session.add(cert)
    await session.flush()

    # 4. Transition affected IP kits to DESTROYED status
    if kit_numbers:
        stmt_kits = select(IPKit).where(
            IPKit.study_id == study_id,
            IPKit.kit_number.in_(kit_numbers),
        )
        res_kits = await session.execute(stmt_kits)
        for kit in res_kits.scalars().all():
            kit.status = "DESTROYED"

    # 5. Update COC record if provided
    if coc_id:
        stmt_coc = select(CGTChainOfCustody).where(CGTChainOfCustody.id == coc_id)
        res_coc = await session.execute(stmt_coc)
        coc = res_coc.scalar_one_or_none()
        if coc:
            coc.status = CGTCustodyStatus.DESTROYED
            coc.current_stage = CGTStage.DESTRUCTION
            coc.reconciled_at = now

            # Log destruction milestone
            milestone = CGTMilestoneEvent(
                study_id=study_id,
                chain_of_custody_id=coc_id,
                stage=CGTStage.DESTRUCTION,
                milestone_type="CERTIFICATE_OF_DESTRUCTION_ISSUED",
                timestamp=now,
                location_name=destruction_facility,
                facility_id=site_id,
                latitude=None,
                longitude=None,
                temperature_celsius=20.0,
                temperature_status=CryogenicTempStatus.OPTIMAL,
                custodian_id=witness_1_id,
                custodian_name=witness_1_name,
                custodian_role=witness_1_role,
                carrier=None,
                tracking_number=None,
                seal_intact=True,
                seal_id=None,
                dry_shipper_id=None,
                electronic_signature={
                    "witness_1": witness_1_id,
                    "witness_2": witness_2_id,
                    "certificate_number": cert_num,
                    "timestamp": now.isoformat(),
                },
                notes=f"Certificate of Destruction {cert_num} executed. Seal: {cryptographic_seal[:16]}...",
            )
            session.add(milestone)

    # 6. Bind to reconciliation records
    if vial_identifiers:
        stmt_recons = select(CGTVialReturnReconciliation).where(
            CGTVialReturnReconciliation.study_id == study_id,
            CGTVialReturnReconciliation.vial_identifier.in_(vial_identifiers),
        )
        res_recons = await session.execute(stmt_recons)
        for recon in res_recons.scalars().all():
            recon.certificate_id = str(cert.id)
            recon.disposition = VialDisposition.DESTROYED

    # 7. Outbox notification
    outbox = IntegrationOutbox(
        event_type="rtsm.cgt.destruction_certified.v1",
        payload={
            "certificate_id": str(cert.id),
            "certificate_number": cert_num,
            "study_id": study_id,
            "site_id": site_id,
            "lot_number": lot_number,
            "quantity_destroyed": quantity_destroyed,
            "witness_1_id": witness_1_id,
            "witness_2_id": witness_2_id,
            "cryptographic_seal": cryptographic_seal,
            "timestamp": now.isoformat(),
        },
        correlation_id=f"cgt-destruction-{cert.id}",
        created_by=witness_1_id or "system",
        reason_for_change=destruction_reason
        or f"Certified destruction of lot {lot_number}",
    )
    session.add(outbox)

    return cert
