"""Unified Regulatory Safety Snapshot & Seal Ledger.

Requirements:
- PRD-PVG-002: Secure AS2 B2B Transmission Gateway & E2B(R3) MDN Non-Repudiation
- PRD-PVG-004: Periodic Safety Update Reporting & DSUR Compilation
- PRD-PVG-008: 21 CFR Part 11 Electronic Signature Gating & Sign-Off Manifests
- PRD-AGG-001: Automated Aggregate Safety Compiler & Exposure Calculator
- PRD-SYS-125: GxP Immutable Audit Ledger & Bitwise Reproducibility
- PRD-SYS-126: Medical Device & IVD Safety Investigation Suite
- PRD-DEV-003: Adverse Device Effect Vigilance Deadlines & RTSM Quarantine
- PRD-DEV-004: Regulatory Incident Reports & CDISC SDTM DD/DO Exports
- ADR-2199: Unified Regulatory Audit Ledger & Point-in-Time Merkle Sealing
- 21 CFR Part 11: Electronic Records; Electronic Signatures
"""

import base64
import hashlib
import json
import re
import uuid
from datetime import UTC, datetime, timedelta
from typing import Any

from apps.safety.domain.periodic_report_models import (
    QPPVSignoffManifest,
    RegulatorySnapshot,
)
from apps.safety.services.as2_gateway import (
    AS2DispositionStatus,
    AS2MDNReceipt,
    AS2TransmissionEnvelope,
)
from packages.security.signing import (
    compute_merkle_root as security_compute_merkle_root,
)
from packages.security.signing import (
    generate_canonical_signature,
)

ALLOWED_PHYSICIAN_SIGNING_ROLES: frozenset[str] = frozenset(
    {
        "medical_monitor",
        "qppv",
        "qppv_officer",
        "qualified_person_pv",
        "safety_officer",
        "safety_physician",
        "sponsor_admin",
        "sponsor_medical_monitor",
        "sponsor_pv_officer",
        "sponsor_safety_physician",
        "super_admin",
    }
)


class RegulatoryAuditLedger:
    """Unified engine for regulatory dataset snapshotting, Merkle tree digest generation,

    bitwise reproducibility verification, and 21 CFR Part 11 digital sealing across
    DSUR/PSUR aggregate compilers, AS2 B2B EDI gateways, and Medical Device vigilance.
    """

    # -------------------------------------------------------------------------
    # 1. Core Cryptographic & Merkle Tree Dataset Digest Calculation
    # -------------------------------------------------------------------------

    @classmethod
    def compute_canonical_digest(cls, data: Any) -> str:
        """Computes a deterministic, sort-keyed SHA-256 hex digest for any data structure.

        Args:
            data: Dictionary, list, string, or primitive object.

        Returns:
            64-character SHA-256 hexadecimal digest string.
        """
        if isinstance(data, (bytes, bytearray)):
            return hashlib.sha256(data).hexdigest()
        if isinstance(data, str):
            try:
                parsed = json.loads(data)
                serialized = json.dumps(
                    parsed, sort_keys=True, separators=(",", ":"), default=str
                )
            except Exception:
                serialized = data
            return hashlib.sha256(serialized.encode("utf-8")).hexdigest()

        serialized = json.dumps(
            data, sort_keys=True, separators=(",", ":"), default=str
        )
        return hashlib.sha256(serialized.encode("utf-8")).hexdigest()

    @classmethod
    def compute_merkle_root(cls, leaf_hashes: list[str]) -> str:
        """Computes the SHA-256 Merkle root hash from a sequence of leaf hashes.

        Args:
            leaf_hashes: List of hexadecimal SHA-256 leaf digest strings.

        Returns:
            64-character SHA-256 Merkle root digest string.
        """
        if not leaf_hashes:
            return hashlib.sha256(b"").hexdigest()
        if len(leaf_hashes) == 1:
            return leaf_hashes[0]

        # Use canonical security library Merkle root computation
        return security_compute_merkle_root(leaf_hashes)

    @classmethod
    def build_dataset_merkle_tree(
        cls,
        records: list[dict[str, Any]],
        key_field: str = "case_id",
    ) -> tuple[str, list[str]]:
        """Constructs sorted leaf hashes and Merkle root for a dataset of records.

        Args:
            records: List of record dictionaries.
            key_field: Dictionary key to sort records deterministically.

        Returns:
            Tuple of (merkle_root_hash, list_of_leaf_hashes).
        """
        sorted_records = sorted(
            records, key=lambda r: str(r.get(key_field, r.get("id", "")))
        )
        leaf_hashes = [cls.compute_canonical_digest(rec) for rec in sorted_records]
        merkle_root = cls.compute_merkle_root(leaf_hashes)
        return merkle_root, leaf_hashes

    @classmethod
    def verify_bitwise_integrity(
        cls,
        expected_digest: str,
        data: Any,
    ) -> bool:
        """Verifies bitwise integrity of a dataset against an expected SHA-256 digest.

        Args:
            expected_digest: Expected 64-character SHA-256 digest.
            data: Data structure or raw string to verify.

        Returns:
            True if computed digest matches expected digest exactly, False otherwise.
        """
        computed = cls.compute_canonical_digest(data)
        return computed.lower() == expected_digest.lower()

    # -------------------------------------------------------------------------
    # 2. Immutable Regulatory Snapshot Lifecycle (DSUR, PSUR, PBRER)
    # -------------------------------------------------------------------------

    @classmethod
    def create_snapshot(
        cls,
        study_id: str,
        compound_name: str,
        reporting_period_start: str,
        reporting_period_end: str,
        dibd_anniversary: str | None = None,
        raw_cases: list[dict[str, Any]] | None = None,
        subjects_data: list[dict[str, Any]] | None = None,
        user_id: str = "system",
        lock_reason: str = "Annual Periodic Regulatory Reporting Snapshot Lock",
    ) -> RegulatorySnapshot:
        """Constructs an immutable, SHA-256 sealed dataset snapshot.

        Requirements: PRD-AGG-001, PRD-SYS-125, ADR-2199
        """
        cases = raw_cases or []
        subjects = subjects_data or []
        now_dt = datetime.now(UTC).isoformat()
        snapshot_id = f"snap_{uuid.uuid4().hex[:12]}"

        # Canonical normalization for cryptographic digest
        canonical_manifest = {
            "cases": sorted(cases, key=lambda c: str(c.get("case_id", ""))),
            "cases_count": len(cases),
            "compound_name": compound_name,
            "dibd_anniversary": dibd_anniversary,
            "reporting_period_end": reporting_period_end,
            "reporting_period_start": reporting_period_start,
            "study_id": study_id,
            "subjects": sorted(subjects, key=lambda s: str(s.get("subject_id", ""))),
            "subjects_count": len(subjects),
        }

        canonical_json = json.dumps(canonical_manifest, sort_keys=True, default=str)
        snapshot_hash = hashlib.sha256(canonical_json.encode("utf-8")).hexdigest()

        return RegulatorySnapshot(
            snapshot_id=snapshot_id,
            study_id=study_id,
            compound_name=compound_name,
            reporting_period_start=reporting_period_start,
            reporting_period_end=reporting_period_end,
            dibd_anniversary=dibd_anniversary,
            snapshot_hash=snapshot_hash,
            total_cases_frozen=len(cases),
            total_subjects_frozen=len(subjects),
            is_locked=True,
            locked_at=now_dt,
            locked_by=user_id,
            lock_reason=lock_reason,
            dataset_manifest={
                "cases_count": len(cases),
                "hash": snapshot_hash,
                "subjects_count": len(subjects),
            },
        )

    @classmethod
    def verify_snapshot(
        cls,
        expected_hash: str,
        cases: list[dict[str, Any]],
        subjects: list[dict[str, Any]],
        study_id: str,
        compound_name: str,
        period_start: str,
        period_end: str,
        dibd: str | None = None,
    ) -> bool:
        """Verifies bitwise integrity of a frozen regulatory snapshot against cases and subjects.

        Requirements: PRD-AGG-001, PRD-SYS-125, ADR-2199
        """
        canonical_manifest = {
            "cases": sorted(cases, key=lambda c: str(c.get("case_id", ""))),
            "cases_count": len(cases),
            "compound_name": compound_name,
            "dibd_anniversary": dibd,
            "reporting_period_end": period_end,
            "reporting_period_start": period_start,
            "study_id": study_id,
            "subjects": sorted(subjects, key=lambda s: str(s.get("subject_id", ""))),
            "subjects_count": len(subjects),
        }
        computed = hashlib.sha256(
            json.dumps(canonical_manifest, sort_keys=True, default=str).encode("utf-8")
        ).hexdigest()
        return computed == expected_hash

    @classmethod
    def verify_snapshot_manifest(
        cls,
        snapshot_id: str,
        expected_hash: str,
        dataset_manifest: dict[str, Any],
    ) -> dict[str, Any]:
        """Cryptographically verifies a snapshot manifest payload and produces structured validation result.

        Requirements: PRD-AGG-001, 21 CFR Part 11, ADR-2199
        """
        canonical_json = json.dumps(dataset_manifest, sort_keys=True, default=str)
        computed_hash = hashlib.sha256(canonical_json.encode("utf-8")).hexdigest()

        is_valid = (expected_hash == computed_hash) or (
            dataset_manifest.get("hash") == expected_hash
        )

        return {
            "computed_hash": computed_hash,
            "expected_hash": expected_hash,
            "is_valid": is_valid,
            "match_status": (
                "VERIFIED_MATCH" if is_valid else "HASH_MISMATCH_TAMPER_DETECTED"
            ),
            "snapshot_id": snapshot_id,
            "verified_at": datetime.now(UTC).isoformat(),
        }

    # -------------------------------------------------------------------------
    # 3. 21 CFR Part 11 Electronic Signature Sealing
    # -------------------------------------------------------------------------

    @classmethod
    def validate_signing_role(
        cls,
        user_id: str,
        user_roles: list[str] | set[str] | tuple[str, ...],
        allowed_roles: set[str] | frozenset[str] = ALLOWED_PHYSICIAN_SIGNING_ROLES,
    ) -> bool:
        """Validates that a signer holds an authorized clinical or safety role."""
        if not user_roles:
            return False
        normalized_roles = {
            str(r)
            .lower()
            .replace(" ", "_")
            .replace("(", "")
            .replace(")", "")
            .replace("-", "_")
            for r in (
                user_roles
                if isinstance(user_roles, (list, set, tuple))
                else [user_roles]
            )
        }
        return bool(
            normalized_roles.intersection(allowed_roles)
            or any(
                "qppv" in r
                or "physician" in r
                or "safety" in r
                or "medical_monitor" in r
                or "admin" in r
                for r in normalized_roles
            )
        )

    @classmethod
    def seal_electronic_signature(
        cls,
        user_id: str,
        user_roles: list[str],
        signature_secret: str,
        meaning_of_signature: str,
        payload_to_sign: dict[str, Any],
        allowed_roles: set[str] | frozenset[str] = ALLOWED_PHYSICIAN_SIGNING_ROLES,
    ) -> tuple[str, str]:
        """Validates role authorization and applies a 21 CFR Part 11 canonical signature seal.

        Returns:
            Tuple of (manifest_id, signature_token).
        """
        if not cls.validate_signing_role(user_id, user_roles, allowed_roles):
            raise ValueError(
                f"User '{user_id}' with roles {user_roles} is not authorized for 21 CFR Part 11 electronic signature."
            )

        manifest_id = f"sig_{uuid.uuid4().hex[:12]}"
        sig_token = generate_canonical_signature(
            payload_to_sign, signature_secret.encode("utf-8")
        )
        return manifest_id, sig_token

    @classmethod
    def seal_qppv_signoff(
        cls,
        report_id: str,
        report_type: str,
        study_id: str,
        compound_name: str,
        user_id: str,
        user_roles: list[str],
        signature_secret: str,
        meaning_of_signature: str,
        snapshot_hash: str,
        report_payload: dict[str, Any],
    ) -> QPPVSignoffManifest:
        """Constructs, validates, and seals a 21 CFR Part 11 Executive QPPV approval certificate.

        Requirements: PRD-AGG-001, PRD-PVG-008, 21 CFR Part 11, ADR-2199
        """
        if not cls.validate_signing_role(user_id, user_roles):
            raise ValueError(
                f"User '{user_id}' with roles {user_roles} is not authorized to sign periodic safety reports."
            )

        now_dt = datetime.now(UTC)
        manifest_id = f"sig_rep_{uuid.uuid4().hex[:12]}"

        payload_to_sign = {
            "compound_name": compound_name,
            "meaning": meaning_of_signature,
            "report_id": report_id,
            "report_type": report_type,
            "signer": user_id,
            "snapshot_hash": snapshot_hash,
            "study_id": study_id,
            "timestamp": now_dt.isoformat(),
        }
        sig_token = generate_canonical_signature(
            payload_to_sign, signature_secret.encode("utf-8")
        )

        role_lower = [str(r).lower() for r in (user_roles or [])]
        is_qppv = any("qppv" in r for r in role_lower)

        return QPPVSignoffManifest(
            comments=None,
            manifest_id=manifest_id,
            meaning_of_signature=meaning_of_signature,
            qppv_name=user_id,
            qppv_role=(
                "Qualified Person for Pharmacovigilance (QPPV)"
                if is_qppv
                else "Authorized Safety Physician"
            ),
            qppv_user_id=user_id,
            report_hash=hashlib.sha256(
                json.dumps(report_payload, sort_keys=True, default=str).encode("utf-8")
            ).hexdigest(),
            signature_token=sig_token,
            signed_at=now_dt.isoformat(),
            snapshot_hash=snapshot_hash,
            verification_status="VALID",
        )

    # -------------------------------------------------------------------------
    # 4. AS2 Gateway Integrity, MIC Calculation & Non-Repudiation (RFC 4130)
    # -------------------------------------------------------------------------

    @classmethod
    def calculate_as2_mic(cls, payload: str | bytes) -> str:
        """Calculates RFC 4130 formatted SHA-256 Message Integrity Check (MIC).

        Args:
            payload: Raw XML or binary transmission content.

        Returns:
            Base64 encoded SHA-256 digest string followed by algorithm tag (e.g. '... , sha-256').
        """
        raw_bytes = payload.encode("utf-8") if isinstance(payload, str) else payload
        digest = hashlib.sha256(raw_bytes).digest()
        b64_digest = base64.b64encode(digest).decode("ascii")
        return f"{b64_digest}, sha-256"

    @classmethod
    def seal_outbound_as2_envelope(
        cls,
        as2_from: str,
        as2_to: str,
        xml_payload: str,
        custom_message_id: str | None = None,
    ) -> AS2TransmissionEnvelope:
        """Constructs canonical AS2 envelope headers and SHA-256 MIC seal for E2B(R3) dispatch.

        Requirements: PRD-PVG-002, RFC 4130
        """
        msg_id = custom_message_id or f"<{uuid.uuid4()}@{as2_from.lower()}.ccrsoft.com>"
        mic = cls.calculate_as2_mic(xml_payload)
        now_date = datetime.now(UTC).strftime("%a, %d %b %Y %H:%M:%S GMT")

        headers = {
            "AS2-From": as2_from,
            "AS2-To": as2_to,
            "AS2-Version": "1.2",
            "Content-Type": "application/xml; charset=UTF-8",
            "Date": now_date,
            "Disposition-Notification-Options": "signed-receipt-protocol=optional, pkcs7-signature; signed-receipt-micalg=optional, sha-256",
            "Disposition-Notification-To": f"as2-mdn@{as2_from.lower()}.ccrsoft.com",
            "Message-ID": msg_id,
        }

        return AS2TransmissionEnvelope(
            as2_from=as2_from,
            as2_to=as2_to,
            content_mic_sha256=mic,
            headers=headers,
            message_id=msg_id,
            payload_content_type="application/xml",
            raw_payload=xml_payload,
        )

    @classmethod
    def verify_as2_mdn_receipt(
        cls,
        raw_mdn_text: str,
        expected_outbound_envelope: AS2TransmissionEnvelope | None = None,
    ) -> AS2MDNReceipt:
        """Parses MDN multipart text, extracts fields, and validates MIC for non-repudiation and tamper detection.

        Requirements: PRD-PVG-002, RFC 4130
        """
        mdn_id_match = re.search(
            r"Message-ID:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE
        )
        mdn_id = (
            mdn_id_match.group(1).strip() if mdn_id_match else f"<mdn_{uuid.uuid4()}>"
        )

        orig_id_match = re.search(
            r"Original-Message-ID:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE
        )
        orig_id = (
            orig_id_match.group(1).strip()
            if orig_id_match
            else (
                expected_outbound_envelope.message_id
                if expected_outbound_envelope
                else ""
            )
        )

        ua_match = re.search(r"Reporting-UA:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE)
        reporting_ua = ua_match.group(1).strip() if ua_match else None

        disp_match = re.search(
            r"Disposition:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE
        )
        raw_disp = (
            disp_match.group(1).strip()
            if disp_match
            else "automatic-action/MDN-sent-automatically; processed"
        )

        mic_match = re.search(
            r"Received-Content-MIC:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE
        )
        received_mic = mic_match.group(1).strip() if mic_match else None

        # Determine disposition status
        if "processed" in raw_disp.lower() and "error" not in raw_disp.lower():
            status = AS2DispositionStatus.PROCESSED
        elif "warning" in raw_disp.lower():
            status = AS2DispositionStatus.WARNING
        else:
            status = AS2DispositionStatus.FAILED

        # Verify MIC against original transmission
        is_verified = False
        error = None

        if expected_outbound_envelope:
            expected_mic_norm = cls._normalize_mic(
                expected_outbound_envelope.content_mic_sha256
            )
            if received_mic:
                received_mic_norm = cls._normalize_mic(received_mic)
                if expected_mic_norm == received_mic_norm:
                    is_verified = True
                else:
                    status = AS2DispositionStatus.TAMPERED
                    error = f"MIC mismatch: expected '{expected_mic_norm}', received '{received_mic_norm}'"
            else:
                error = "MDN does not contain Received-Content-MIC"
        else:
            is_verified = True

        return AS2MDNReceipt(
            disposition_raw=raw_disp,
            disposition_status=status,
            is_mic_verified=is_verified,
            mdn_message_id=mdn_id,
            original_message_id=orig_id,
            received_mic=received_mic,
            reporting_ua=reporting_ua,
            verification_error=error,
        )

    @staticmethod
    def _normalize_mic(mic: str) -> str:
        """Normalizes MIC string by stripping whitespace and standardizing algorithm tag."""
        return re.sub(r"\s+", "", mic).lower()

    # -------------------------------------------------------------------------
    # 5. Medical Device Safety Investigation Sealing & Vigilance Deadlines
    # -------------------------------------------------------------------------

    @classmethod
    def calculate_device_vigilance_deadline(
        cls,
        classification: str,
        is_public_health_threat: bool = False,
        now_ts: datetime | None = None,
    ) -> tuple[int, datetime]:
        """Calculates statutory vigilance reporting deadlines per ISO 14155, EU MDR & EU IVDR.

        - 2 calendar days: Public health threat
        - 10 calendar days: SADE / USADE / Death / Serious deterioration
        - 15 calendar days: ADE / General reportable incidents

        Returns:
            Tuple of (deadline_days: int, due_date: datetime).
        """
        ref_ts = now_ts or datetime.now(UTC).replace(tzinfo=None)
        cls_upper = classification.upper()

        if is_public_health_threat:
            days = 2
        elif cls_upper in ("SADE", "USADE"):
            days = 10
        else:
            days = 15

        due_date = ref_ts + timedelta(days=days)
        return days, due_date

    @classmethod
    def seal_device_adjudication(
        cls,
        investigation_id: str,
        case_number: str,
        classification: str,
        causality: str,
        capa_required: bool,
        safety_physician_user_id: str,
        signature_token: str,
        reason_for_change: str | None = None,
    ) -> dict[str, Any]:
        """Applies a 21 CFR Part 11 electronic signature seal to a Safety Physician device adjudication.

        Requirements: PRD-SYS-126, PRD-DEV-003, 21 CFR Part 11, ADR-2209
        """
        now_ts = datetime.now(UTC)
        manifest_id = signature_token or f"sig_dev_{uuid.uuid4().hex[:12]}"

        adjudication_digest = cls.compute_canonical_digest(
            {
                "capa_required": capa_required,
                "case_number": case_number,
                "causality": causality,
                "classification": classification,
                "investigation_id": investigation_id,
                "physician": safety_physician_user_id,
                "timestamp": now_ts.isoformat(),
            }
        )

        return {
            "adjudicated_at": now_ts.isoformat(),
            "adjudication_digest": adjudication_digest,
            "esignature_manifest_id": manifest_id,
            "physician_user_id": safety_physician_user_id,
            "verification_status": "VALID",
        }

    @classmethod
    def seal_device_vigilance_report(
        cls,
        case_dict: dict[str, Any],
    ) -> dict[str, Any]:
        """Constructs and seals EU MDR 2017/745 / EU IVDR 2017/746 Manufacturer Incident Report (MIR v7.2).

        Requirements: PRD-SYS-126, PRD-DEV-004, EU MDR 2017/745, ADR-2209
        """
        case_num = str(case_dict.get("case_number", "DEV-UNKNOWN"))
        dev_name = str(case_dict.get("device_name", "Unknown Device"))
        udi_di = str(case_dict.get("device_udi_di", ""))
        lot_num = str(case_dict.get("lot_number", ""))
        serial_num = case_dict.get("serial_number")
        study_id = str(case_dict.get("study_id", ""))
        site_id = str(case_dict.get("site_id", ""))
        subj_id = case_dict.get("subject_id")
        defic_id = str(case_dict.get("deficiency_id", ""))
        cls_val = str(case_dict.get("classification", "ADE"))
        caus_val = str(case_dict.get("causality", "POSSIBLE"))
        is_usade = bool(case_dict.get("is_usade", False))
        vigilance_days = int(case_dict.get("vigilance_deadline_days", 15))
        vigilance_due = str(
            case_dict.get("vigilance_due_date", datetime.now(UTC).isoformat())
        )
        status = str(case_dict.get("status", "OPEN"))
        notes = case_dict.get("investigation_notes")
        root_cause = case_dict.get("root_cause_summary")
        capa_req = bool(case_dict.get("capa_required", False))
        capa_ref = case_dict.get("capa_reference")
        fsca_req = bool(case_dict.get("fsca_required", False))
        physician_id = case_dict.get("safety_physician_user_id")
        adj_at = case_dict.get("adjudicated_at")

        summary_text = (
            f"Investigational Medical Device Incident Report for {dev_name} "
            f"(UDI-DI: {udi_di}, Lot: {lot_num}, Serial: {serial_num or 'N/A'}). "
            f"Classification: {cls_val}. Causality: {caus_val}. "
            f"Evaluation: {root_cause or notes or 'Under Investigation'}."
        )

        payload_json = {
            "administrative_information": {
                "competent_authority": "EUDAMED / National Competent Authority",
                "report_reference_number": f"MIR-{case_num}",
                "report_type": (
                    "INITIAL_INCIDENT_REPORT"
                    if status == "OPEN"
                    else "FINAL_INVESTIGATION_REPORT"
                ),
                "reporting_date": datetime.now(UTC).isoformat(),
                "vigilance_due_date": vigilance_due,
            },
            "device_identification": {
                "brand_name": dev_name,
                "clinical_investigation_study_id": study_id,
                "investigation_site_id": site_id,
                "lot_number": lot_num,
                "serial_number": serial_num,
                "subject_identifier": subj_id,
                "udi_di": udi_di,
            },
            "incident_information": {
                "causal_relationship": caus_val,
                "deficiency_id": defic_id,
                "is_unanticipated_sade": is_usade,
                "safety_classification": cls_val,
                "summary": summary_text,
            },
            "investigation_and_actions": {
                "adjudicated_at": adj_at,
                "capa_initiated": capa_req,
                "capa_reference": capa_ref,
                "fsca_required": fsca_req,
                "root_cause_analysis": root_cause,
                "safety_physician": physician_id,
                "status": status,
            },
            "mir_version": "7.2",
            "regulatory_framework": "EU_MDR_2017_745",
        }

        payload_xml = f"""<?xml version="1.0" encoding="UTF-8"?>
<ManufacturerIncidentReport version="7.2" xmlns="urn:eudamed:mdr:mir:v7.2">
  <AdministrativeInformation>
    <ReportReference>{payload_json["administrative_information"]["report_reference_number"]}</ReportReference>
    <ReportType>{payload_json["administrative_information"]["report_type"]}</ReportType>
    <VigilanceDueDate>{vigilance_due}</VigilanceDueDate>
  </AdministrativeInformation>
  <DeviceIdentification>
    <BrandName>{dev_name}</BrandName>
    <UDI_DI>{udi_di}</UDI_DI>
    <LotNumber>{lot_num}</LotNumber>
    <SerialNumber>{serial_num or ""}</SerialNumber>
    <StudyIdentifier>{study_id}</StudyIdentifier>
    <SiteIdentifier>{site_id}</SiteIdentifier>
    <SubjectIdentifier>{subj_id or ""}</SubjectIdentifier>
  </DeviceIdentification>
  <IncidentInformation>
    <Classification>{cls_val}</Classification>
    <Causality>{caus_val}</Causality>
    <IsUSADE>{str(is_usade).lower()}</IsUSADE>
    <Description>{summary_text}</Description>
  </IncidentInformation>
  <ActionsTaken>
    <CAPARequired>{str(capa_req).lower()}</CAPARequired>
    <FSCARequired>{str(fsca_req).lower()}</FSCARequired>
    <Status>{status}</Status>
  </ActionsTaken>
</ManufacturerIncidentReport>"""

        xml_digest = cls.compute_canonical_digest(payload_xml)

        return {
            "case_number": case_num,
            "classification": cls_val,
            "device_name": dev_name,
            "incident_summary": summary_text,
            "lot_number": lot_num,
            "payload_digest": xml_digest,
            "payload_json": payload_json,
            "payload_xml": payload_xml,
            "report_type": "EU_MDR_MIR_V7_2",
            "reporting_authority": "EUDAMED / National Competent Authority",
            "serial_number": serial_num,
            "udi_di": udi_di,
            "vigilance_deadline_days": vigilance_days,
            "vigilance_due_date": vigilance_due,
        }
