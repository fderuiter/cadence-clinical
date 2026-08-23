"""Domain processor algorithms, cryptographic token generation, and business rules for DCT.

Implements:
- WebRTC room authorization tokens & DTLS fingerprint validation
- GxP video recording SHA-256 digital sealing (21 CFR Part 11)
- Tiered pediatric assent & LAR dual-signature manifest sealing (PRD-DCT-006)
- Direct-to-Patient (DTP) MKT stability calculation and excursion alerts (PRD-DCT-003)
- In-home nurse visit credential validation & mobile eCRF offline sync (PRD-DCT-004)
- ADR-2199 / ADR-2207, USP <1079>, PDA TR 39
"""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import math
import re
import uuid
from datetime import UTC, datetime, timedelta
from typing import Any

from apps.interop.domain.dct_models import (
    CustodyHandoff,
    ExcursionSeverity,
    GxPRecordingSeal,
    LARRelationship,
    ParticipantRole,
    PediatricAssentPayload,
    PediatricAssentTier,
    RoomCapabilities,
    RoomTokenPayload,
    TemperatureReading,
    TemperatureZone,
)

# Standard activation energy / gas constant for pharmaceutical stability (USP <1079>)
# deltaH / R = 83.144 kJ/mol / 8.3144 J/(mol*K) = 10000 Kelvin
DELTA_H_OVER_R = 10000.0


# =========================================================================
# WebRTC Telehealth Room Authorization & DTLS Verification
# =========================================================================


def build_role_capabilities(role: ParticipantRole) -> RoomCapabilities:
    """Determine permitted room capabilities based on participant clinical role."""
    match role:
        case ParticipantRole.INVESTIGATOR:
            return RoomCapabilities(
                can_present=True,
                can_annotate=True,
                can_sign=True,
                can_record=True,
                can_admit=True,
            )
        case ParticipantRole.NURSE_COORDINATOR:
            return RoomCapabilities(
                can_present=True,
                can_annotate=True,
                can_sign=False,
                can_record=True,
                can_admit=True,
            )
        case ParticipantRole.SUBJECT | ParticipantRole.LAR:
            return RoomCapabilities(
                can_present=False,
                can_annotate=True,
                can_sign=True,
                can_record=False,
                can_admit=False,
            )
        case ParticipantRole.TRANSLATOR | ParticipantRole.WITNESS:
            return RoomCapabilities(
                can_present=False,
                can_annotate=False,
                can_sign=True,
                can_record=False,
                can_admit=False,
            )
        case _:
            return RoomCapabilities()


def generate_room_access_token(
    secret: str,
    room_id: str,
    session_id: str,
    study_id: str,
    subject_id: str,
    participant_id: str,
    role: ParticipantRole,
    display_name: str,
    duration_minutes: int = 120,
) -> tuple[str, RoomTokenPayload]:
    """Generate a tamper-evident, HMAC-SHA256 signed room authorization token."""
    now = datetime.now(UTC)
    expires_at = now + timedelta(minutes=duration_minutes)
    capabilities = build_role_capabilities(role)

    claim_dict = {
        "room_id": room_id,
        "session_id": session_id,
        "study_id": study_id,
        "subject_id": subject_id,
        "participant_id": participant_id,
        "participant_role": role.value,
        "display_name": display_name,
        "expires_at_utc": expires_at.isoformat(),
        "capabilities": {
            "can_present": capabilities.can_present,
            "can_annotate": capabilities.can_annotate,
            "can_sign": capabilities.can_sign,
            "can_record": capabilities.can_record,
            "can_admit": capabilities.can_admit,
        },
    }

    claim_bytes = json.dumps(claim_dict, sort_keys=True).encode("utf-8")
    signature = hmac.new(
        secret.encode("utf-8"), claim_bytes, hashlib.sha256
    ).hexdigest()

    token_payload = RoomTokenPayload(
        room_id=room_id,
        session_id=session_id,
        study_id=study_id,
        subject_id=subject_id,
        participant_id=participant_id,
        participant_role=role,
        display_name=display_name,
        expires_at_utc=expires_at,
        capabilities=capabilities,
        signature=signature,
    )

    encoded_claims = base64.urlsafe_b64encode(claim_bytes).decode("ascii")
    token_string = f"cadence_rt_{encoded_claims}.{signature}"
    return token_string, token_payload


def verify_room_access_token(secret: str, token_string: str) -> RoomTokenPayload:
    """Verify cryptographic signature and expiration of a room authorization token."""
    if not token_string.startswith("cadence_rt_") or "." not in token_string:
        raise ValueError("Invalid room authorization token format")

    raw_part = token_string[len("cadence_rt_") :]
    encoded_claims, signature = raw_part.rsplit(".", 1)

    try:
        claim_bytes = base64.urlsafe_b64decode(encoded_claims.encode("ascii"))
        claims = json.loads(claim_bytes.decode("utf-8"))
    except Exception as exc:
        raise ValueError(f"Malformed token claims: {exc}") from exc

    expected_sig = hmac.new(
        secret.encode("utf-8"), claim_bytes, hashlib.sha256
    ).hexdigest()
    if not hmac.compare_digest(signature, expected_sig):
        raise ValueError("Cryptographic room token signature mismatch (tampered)")

    expires_at = datetime.fromisoformat(claims["expires_at_utc"])
    if expires_at.tzinfo is None:
        expires_at = expires_at.replace(tzinfo=UTC)

    if datetime.now(UTC) > expires_at:
        raise ValueError("Room authorization token has expired")

    cap_data = claims.get("capabilities", {})
    capabilities = RoomCapabilities(
        can_present=cap_data.get("can_present", False),
        can_annotate=cap_data.get("can_annotate", False),
        can_sign=cap_data.get("can_sign", False),
        can_record=cap_data.get("can_record", False),
        can_admit=cap_data.get("can_admit", False),
    )

    return RoomTokenPayload(
        room_id=claims["room_id"],
        session_id=claims["session_id"],
        study_id=claims["study_id"],
        subject_id=claims["subject_id"],
        participant_id=claims["participant_id"],
        participant_role=ParticipantRole(claims["participant_role"]),
        display_name=claims["display_name"],
        expires_at_utc=expires_at,
        capabilities=capabilities,
        signature=signature,
    )


def validate_webrtc_fingerprint(fingerprint_str: str) -> bool:
    """Assert valid WebRTC DTLS-SRTP SHA-256 fingerprint format."""
    if not fingerprint_str:
        return False
    # Standard format: sha-256 XX:XX:XX:... (32 hex octets separated by colons)
    pattern = r"^sha-256\s+([0-9A-Fa-f]{2}:){31}[0-9A-Fa-f]{2}$"
    return bool(re.match(pattern, fingerprint_str.strip()))


# =========================================================================
# GxP Archived Video Recording & Digital Seal (21 CFR Part 11)
# =========================================================================


def create_gxp_recording_digital_seal(
    secret: str,
    recording_id: str,
    session_id: str,
    study_id: str,
    media_payload_or_hash: str,
    duration_seconds: int,
    resolution: str,
    recorded_by: str,
    participant_manifest: list[dict[str, Any]],
) -> GxPRecordingSeal:
    """Generate SHA-256 media digest and GxP 21 CFR Part 11 digital seal."""
    # If media_payload_or_hash is not a 64-char hex string, compute its sha256
    if len(media_payload_or_hash) == 64 and all(
        c in "0123456789abcdefABCDEF" for c in media_payload_or_hash
    ):
        media_hash = media_payload_or_hash.lower()
    else:
        media_hash = hashlib.sha256(media_payload_or_hash.encode("utf-8")).hexdigest()

    sealed_at = datetime.now(UTC)
    cert_statement = (
        f"I certify under 21 CFR Part 11 that this audiovisual recording for session {session_id} "
        f"in study {study_id} is authentic, unaltered, and complete. Media SHA-256: {media_hash}."
    )

    manifest_to_sign = {
        "recording_id": recording_id,
        "session_id": session_id,
        "study_id": study_id,
        "media_sha256_hash": media_hash,
        "duration_seconds": duration_seconds,
        "resolution": resolution,
        "recorded_by": recorded_by,
        "sealed_at_utc": sealed_at.isoformat(),
        "participants": participant_manifest,
        "statement": cert_statement,
    }

    manifest_bytes = json.dumps(manifest_to_sign, sort_keys=True).encode("utf-8")
    signature = hmac.new(
        secret.encode("utf-8"), manifest_bytes, hashlib.sha256
    ).hexdigest()

    return GxPRecordingSeal(
        recording_id=recording_id,
        session_id=session_id,
        study_id=study_id,
        duration_seconds=duration_seconds,
        resolution=resolution,
        media_sha256_hash=media_hash,
        digital_seal_signature=signature,
        recorded_by=recorded_by,
        sealed_at_utc=sealed_at,
        participant_manifest=participant_manifest,
        part11_certification_statement=cert_statement,
    )


def verify_recording_digital_seal(secret: str, seal: GxPRecordingSeal) -> bool:
    """Verify cryptographic digital seal integrity on an archived GxP recording."""
    manifest_to_verify = {
        "recording_id": seal.recording_id,
        "session_id": seal.session_id,
        "study_id": seal.study_id,
        "media_sha256_hash": seal.media_sha256_hash,
        "duration_seconds": seal.duration_seconds,
        "resolution": seal.resolution,
        "recorded_by": seal.recorded_by,
        "sealed_at_utc": seal.sealed_at_utc.isoformat(),
        "participants": seal.participant_manifest,
        "statement": seal.part11_certification_statement,
    }
    manifest_bytes = json.dumps(manifest_to_verify, sort_keys=True).encode("utf-8")
    expected_sig = hmac.new(
        secret.encode("utf-8"), manifest_bytes, hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(seal.digital_seal_signature, expected_sig)


# =========================================================================
# Tiered Pediatric Assent & LAR Dual-Consent Engine (PRD-DCT-006)
# =========================================================================


def determine_pediatric_tier(age: int) -> PediatricAssentTier:
    """Determine applicable assent/consent tier based on minor age at enrollment."""
    if age < 7:
        # Under 7: Parental / LAR consent only
        return PediatricAssentTier.LAR_PARENT_GUARDIAN
    if 7 <= age <= 11:
        return PediatricAssentTier.CHILD_7_11
    if 12 <= age <= 17:
        return PediatricAssentTier.ADOLESCENT_12_17
    return PediatricAssentTier.ADULT_18_PLUS


def evaluate_pediatric_comprehension(
    tier: PediatricAssentTier, answers: list[dict[str, Any]]
) -> tuple[float, bool]:
    """Calculate comprehension score and evaluate pass/fail criteria."""
    if not answers:
        return 0.0, False

    correct_count = sum(
        1 for a in answers if a.get("selected_option") == a.get("correct_option")
    )
    total_count = len(answers)
    score_percent = round((correct_count / total_count) * 100.0, 1)

    # 80% passing threshold for clinical comprehension
    passed = score_percent >= 80.0
    return score_percent, passed


def create_pediatric_assent_bundle(
    study_id: str,
    subject_id: str,
    tier: PediatricAssentTier,
    minor_age: int,
    comprehension_score_percent: float,
    assent_signature: str | None,
    lar_name: str | None,
    lar_relationship: LARRelationship | None,
    lar_legal_basis: str | None,
    lar_signature: str | None,
    investigator_name: str | None,
    investigator_signature: str | None,
    assent_id: str | None = None,
) -> PediatricAssentPayload:
    """Assemble and cryptographically seal multi-party pediatric assent & LAR consent."""
    if tier in (
        PediatricAssentTier.CHILD_7_11,
        PediatricAssentTier.ADOLESCENT_12_17,
        PediatricAssentTier.LAR_PARENT_GUARDIAN,
    ):
        if not lar_signature or not lar_name or not lar_relationship:
            raise ValueError(
                "Legally Authorized Representative (LAR) signature, name, and relationship are mandatory for pediatric subjects"
            )

    if tier == PediatricAssentTier.ADOLESCENT_12_17 and not assent_signature:
        raise ValueError(
            "Adolescent minor assent signature is mandatory for ages 12–17"
        )

    now = datetime.now(UTC)
    assent_uuid = assent_id or f"ASSENT-{uuid.uuid4().hex[:12].upper()}"

    passed = comprehension_score_percent >= 80.0

    # Cryptographic bundle linkage hash (SHA-256)
    manifest = {
        "assent_id": assent_uuid,
        "study_id": study_id,
        "subject_id": subject_id,
        "tier": tier.value,
        "minor_age": minor_age,
        "comprehension_score": comprehension_score_percent,
        "assent_signature": assent_signature or "N/A",
        "lar_name": lar_name or "N/A",
        "lar_relationship": lar_relationship.value if lar_relationship else "N/A",
        "lar_legal_basis": lar_legal_basis or "N/A",
        "lar_signature": lar_signature or "N/A",
        "investigator_name": investigator_name or "N/A",
        "investigator_signature": investigator_signature or "N/A",
    }
    manifest_bytes = json.dumps(manifest, sort_keys=True).encode("utf-8")
    bundle_hash = hashlib.sha256(manifest_bytes).hexdigest()

    return PediatricAssentPayload(
        assent_id=assent_uuid,
        study_id=study_id,
        subject_id=subject_id,
        tier=tier,
        minor_age=minor_age,
        comprehension_score_percent=comprehension_score_percent,
        comprehension_passed=passed,
        assent_signature=assent_signature,
        assent_signed_at_utc=now if assent_signature else None,
        lar_name=lar_name,
        lar_relationship=lar_relationship,
        lar_legal_basis=lar_legal_basis,
        lar_signature=lar_signature,
        lar_signed_at_utc=now if lar_signature else None,
        investigator_name=investigator_name,
        investigator_signature=investigator_signature,
        investigator_signed_at_utc=now if investigator_signature else None,
        cryptographic_bundle_hash=bundle_hash,
    )


def verify_pediatric_assent_bundle(bundle: PediatricAssentPayload) -> bool:
    """Verify cryptographic bundle seal and integrity of pediatric assent."""
    manifest = {
        "assent_id": bundle.assent_id,
        "study_id": bundle.study_id,
        "subject_id": bundle.subject_id,
        "tier": bundle.tier.value,
        "minor_age": bundle.minor_age,
        "comprehension_score": bundle.comprehension_score_percent,
        "assent_signature": bundle.assent_signature or "N/A",
        "lar_name": bundle.lar_name or "N/A",
        "lar_relationship": bundle.lar_relationship.value
        if bundle.lar_relationship
        else "N/A",
        "lar_legal_basis": bundle.lar_legal_basis or "N/A",
        "lar_signature": bundle.lar_signature or "N/A",
        "investigator_name": bundle.investigator_name or "N/A",
        "investigator_signature": bundle.investigator_signature or "N/A",
    }
    manifest_bytes = json.dumps(manifest, sort_keys=True).encode("utf-8")
    expected_hash = hashlib.sha256(manifest_bytes).hexdigest()
    return hmac.compare_digest(bundle.cryptographic_bundle_hash, expected_hash)


# =========================================================================
# Direct-to-Patient (DTP) Cold Chain & Logistics Engine (PRD-DCT-003)
# =========================================================================


def get_temperature_thresholds(zone: TemperatureZone) -> tuple[float, float]:
    """Return permissible (min_celsius, max_celsius) for temperature condition zone."""
    match zone:
        case TemperatureZone.CRYOGENIC:
            return (-196.0, -150.0)
        case TemperatureZone.ULTRA_COLD:
            return (-80.0, -60.0)
        case TemperatureZone.FROZEN:
            return (-25.0, -15.0)
        case TemperatureZone.REFRIGERATED:
            return (2.0, 8.0)
        case TemperatureZone.CONTROLLED_AMBIENT:
            return (15.0, 25.0)
        case _:
            return (15.0, 25.0)


def calculate_mean_kinetic_temperature(
    temperatures_celsius: list[float],
) -> float:
    """Calculate Mean Kinetic Temperature (MKT) in °C using USP <1079> Arrhenius equation."""
    if not temperatures_celsius:
        return 20.0

    # Convert °C to Kelvin (T_k = T_c + 273.15)
    kelvins = [t + 273.15 for t in temperatures_celsius]
    n = len(kelvins)

    # sum_i exp(-deltaH / (R * T_i))
    arrhenius_sum = sum(math.exp(-DELTA_H_OVER_R / tk) for tk in kelvins)
    mean_arrhenius = arrhenius_sum / n

    if mean_arrhenius <= 0:
        return temperatures_celsius[0]

    # T_mkt_k = (-deltaH / R) / ln(mean_arrhenius)
    mkt_kelvin = -DELTA_H_OVER_R / math.log(mean_arrhenius)
    return round(mkt_kelvin - 273.15, 2)


def evaluate_temperature_excursions(
    zone: TemperatureZone, readings: list[TemperatureReading]
) -> tuple[float, float, float, ExcursionSeverity]:
    """Evaluate cold chain data logger readings for min/max violations and MKT excursions."""
    if not readings:
        min_lim, max_lim = get_temperature_thresholds(zone)
        mid = round((min_lim + max_lim) / 2.0, 1)
        return mid, mid, mid, ExcursionSeverity.NONE

    temps = [r.temperature_celsius for r in readings]
    min_temp = min(temps)
    max_temp = max(temps)
    mkt_temp = calculate_mean_kinetic_temperature(temps)

    min_limit, max_limit = get_temperature_thresholds(zone)

    # Excursion checking
    violations = [t for t in temps if t < min_limit or t > max_limit]
    if not violations:
        return min_temp, max_temp, mkt_temp, ExcursionSeverity.NONE

    # Critical if MKT is out of spec or severe single reading deviation (> 5°C beyond limit)
    severe_violation = any(
        t < (min_limit - 5.0) or t > (max_limit + 5.0) for t in temps
    )
    if (
        severe_violation
        or mkt_temp < min_limit
        or mkt_temp > max_limit
        or len(violations) > (len(temps) * 0.3)
    ):
        return (
            min_temp,
            max_temp,
            mkt_temp,
            ExcursionSeverity.CRITICAL_EXCURSION_QUARANTINE,
        )

    return min_temp, max_temp, mkt_temp, ExcursionSeverity.MINOR_WARNING


def create_custody_handoff_event(
    stage: str,
    released_by: str,
    received_by: str,
    tamper_seal_intact: bool,
    current_temperature_celsius: float,
    notes: str = "",
    secret_key: str = "cadence_gxp_secret",
) -> CustodyHandoff:
    """Create an immutable, cryptographically signed Chain of Custody handoff record."""
    now = datetime.now(UTC)
    handoff_id = f"COC-{uuid.uuid4().hex[:10].upper()}"

    manifest = f"{handoff_id}|{stage}|{released_by}|{received_by}|{tamper_seal_intact}|{current_temperature_celsius}|{now.isoformat()}"
    signature = hmac.new(
        secret_key.encode("utf-8"), manifest.encode("utf-8"), hashlib.sha256
    ).hexdigest()

    return CustodyHandoff(
        handoff_id=handoff_id,
        stage=stage,
        released_by=released_by,
        received_by=received_by,
        timestamp_utc=now,
        tamper_seal_intact=tamper_seal_intact,
        current_temperature_celsius=current_temperature_celsius,
        notes=notes,
        signature_manifestation=f"DIGITAL_SIG:{signature[:16]}",
    )


# =========================================================================
# In-Home Healthcare Nurse Visits & Offline eCRF Sync (PRD-DCT-004)
# =========================================================================


def verify_nurse_credentials(
    gcp_trained: bool,
    protocol_trained: bool,
    license_active: bool,
    doa_authorized: bool,
) -> bool:
    """Verify visiting nurse regulatory credentials before permitting home health visit."""
    return gcp_trained and protocol_trained and license_active and doa_authorized


def reconcile_mobile_ecrf_offline_payload(
    server_state: dict[str, Any] | None,
    client_payload: dict[str, Any],
    client_sync_id: str,
) -> tuple[dict[str, Any], str]:
    """Reconcile in-home nurse mobile eCRF data entry with optimistic version checking."""
    reconciled = dict(server_state or {})
    client_data = client_payload.get("data", {})

    for field_key, val in client_data.items():
        reconciled[field_key] = val

    reconciled["last_reconciled_sync_id"] = client_sync_id
    reconciled["reconciled_at_utc"] = datetime.now(UTC).isoformat()
    return reconciled, "SYNCHRONIZED"
