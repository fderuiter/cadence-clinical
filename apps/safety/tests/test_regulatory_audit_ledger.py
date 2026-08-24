"""Comprehensive Test Suite for RegulatoryAuditLedger, Snapshotting, and Digital Sealing.

Requirements:
@req:PRD-PVG-002
@req:PRD-PVG-004
@req:PRD-PVG-008
@req:PRD-AGG-001
@req:PRD-AGG-002
@req:PRD-AGG-003
@req:PRD-SYS-125
@req:PRD-SYS-126
@req:PRD-DEV-003
@req:PRD-DEV-004
@req:ADR-2199
"""

import uuid
from datetime import datetime, timedelta

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.safety.database import db_manager
from apps.safety.infrastructure.models import (
    Base,
)
from apps.safety.main import app
from apps.safety.presentation.dtos import (
    DeviceSafetyInvestigationAdjudicateDTO,
    DeviceSafetyInvestigationCreateDTO,
)
from apps.safety.services.as2_gateway import (
    AS2DispositionStatus,
    AS2GatewayService,
)
from apps.safety.services.device_safety_service import (
    DeviceSafetyService,
)
from apps.safety.services.periodic_report_compiler import (
    RegulatorySnapshotManager,
)
from apps.safety.services.regulatory_audit_ledger import (
    RegulatoryAuditLedger,
)

pytestmark = pytest.mark.xdist_group("regulatory_audit_ledger")


@pytest_asyncio.fixture(autouse=True)
async def setup_ledger_db():
    """Initializes in-memory database for regulatory audit ledger tests."""
    db_uri = f"sqlite+aiosqlite:///file:memdb_ledger_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
    db_manager.init_db(db_uri, echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


@pytest.fixture
def client():
    return TestClient(app)


# =========================================================================
# 1. Merkle Tree & Canonical Dataset Digest Calculations
# =========================================================================


def test_canonical_digest_determinism_and_key_sorting():
    """Validates that compute_canonical_digest produces identical SHA-256 digests regardless of key order.

    @req:PRD-SYS-125
    @req:PRD-AGG-001
    @req:ADR-2199
    """
    dict_a = {"study_id": "STUDY-001", "cases": 10, "is_locked": True}
    dict_b = {"is_locked": True, "cases": 10, "study_id": "STUDY-001"}

    digest_a = RegulatoryAuditLedger.compute_canonical_digest(dict_a)
    digest_b = RegulatoryAuditLedger.compute_canonical_digest(dict_b)

    assert digest_a == digest_b
    assert len(digest_a) == 64
    assert RegulatoryAuditLedger.verify_bitwise_integrity(digest_a, dict_b) is True


def test_merkle_tree_dataset_digest_calculation():
    """Validates Merkle root generation and leaf hashing over clinical dataset records.

    @req:PRD-SYS-125
    @req:PRD-AGG-001
    @req:ADR-2199
    """
    records = [
        {"case_id": "CASE-002", "term": "Headache", "severity": 1},
        {"case_id": "CASE-001", "term": "Nausea", "severity": 2},
        {"case_id": "CASE-003", "term": "Pyrexia", "severity": 3},
    ]

    merkle_root, leaf_hashes = RegulatoryAuditLedger.build_dataset_merkle_tree(
        records=records,
        key_field="case_id",
    )

    assert len(leaf_hashes) == 3
    assert len(merkle_root) == 64

    # Bitwise reproducibility: recomputing on reverse order input yields identical Merkle root
    reversed_records = list(reversed(records))
    merkle_root_2, leaf_hashes_2 = RegulatoryAuditLedger.build_dataset_merkle_tree(
        records=reversed_records,
        key_field="case_id",
    )
    assert merkle_root == merkle_root_2
    assert leaf_hashes == leaf_hashes_2


def test_merkle_tree_edge_cases_empty_and_single_leaf():
    """Validates Merkle root computation on edge cases (empty and single leaf).

    @req:PRD-SYS-125
    @req:PRD-AGG-001
    @req:ADR-2199
    """
    empty_root = RegulatoryAuditLedger.compute_merkle_root([])
    assert len(empty_root) == 64

    single_leaf = RegulatoryAuditLedger.compute_canonical_digest({"test": "value"})
    single_root = RegulatoryAuditLedger.compute_merkle_root([single_leaf])
    assert single_root == single_leaf


# =========================================================================
# 2. Point-in-Time Regulatory Snapshot Management & Tamper Detection
# =========================================================================


def test_regulatory_snapshot_creation_and_reproducibility():
    """Validates that RegulatoryAuditLedger creates locked snapshot with bitwise reproducibility.

    @req:PRD-AGG-001
    @req:PRD-SYS-125
    @req:ADR-2199
    """
    cases = [
        {"case_id": "C-1", "meddra_pt": "Rash", "onset_date": "2026-01-10"},
        {"case_id": "C-2", "meddra_pt": "Dizziness", "onset_date": "2026-02-15"},
    ]
    subjects = [
        {"subject_id": "SUB-1", "treatment_arm": "Active"},
        {"subject_id": "SUB-2", "treatment_arm": "Placebo"},
    ]

    snap1 = RegulatoryAuditLedger.create_snapshot(
        study_id="STUDY-MERKLE-01",
        compound_name="CADENCE-DRUG",
        reporting_period_start="2026-01-01",
        reporting_period_end="2026-12-31",
        dibd_anniversary="2026-06-01",
        raw_cases=cases,
        subjects_data=subjects,
        user_id="lead.qppv",
        lock_reason="Annual Periodic Safety Update Report Snapshot",
    )

    assert snap1.is_locked is True
    assert snap1.snapshot_hash is not None
    assert len(snap1.snapshot_hash) == 64
    assert snap1.total_cases_frozen == 2
    assert snap1.total_subjects_frozen == 2

    # Verification on valid data
    is_valid = RegulatoryAuditLedger.verify_snapshot(
        expected_hash=snap1.snapshot_hash,
        cases=cases,
        subjects=subjects,
        study_id="STUDY-MERKLE-01",
        compound_name="CADENCE-DRUG",
        period_start="2026-01-01",
        period_end="2026-12-31",
        dibd="2026-06-01",
    )
    assert is_valid is True


def test_regulatory_snapshot_tamper_detection():
    """Validates tamper detection on mutated, added, or deleted cases and demographics.

    @req:PRD-AGG-001
    @req:PRD-SYS-125
    @req:ADR-2199
    """
    cases = [{"case_id": "C-1", "meddra_pt": "Rash", "onset_date": "2026-01-10"}]
    subjects = [{"subject_id": "SUB-1", "treatment_arm": "Active"}]

    snap = RegulatoryAuditLedger.create_snapshot(
        study_id="STUDY-TAMPER",
        compound_name="CADENCE-DRUG",
        reporting_period_start="2026-01-01",
        reporting_period_end="2026-12-31",
        raw_cases=cases,
        subjects_data=subjects,
    )

    # 1. Mutate field
    tampered_cases = [
        {"case_id": "C-1", "meddra_pt": "Severe Rash", "onset_date": "2026-01-10"}
    ]
    assert (
        RegulatoryAuditLedger.verify_snapshot(
            expected_hash=snap.snapshot_hash,
            cases=tampered_cases,
            subjects=subjects,
            study_id="STUDY-TAMPER",
            compound_name="CADENCE-DRUG",
            period_start="2026-01-01",
            period_end="2026-12-31",
        )
        is False
    )

    # 2. Add extra record
    extra_cases = [
        {"case_id": "C-1", "meddra_pt": "Rash", "onset_date": "2026-01-10"},
        {"case_id": "C-2", "meddra_pt": "Nausea", "onset_date": "2026-01-12"},
    ]
    assert (
        RegulatoryAuditLedger.verify_snapshot(
            expected_hash=snap.snapshot_hash,
            cases=extra_cases,
            subjects=subjects,
            study_id="STUDY-TAMPER",
            compound_name="CADENCE-DRUG",
            period_start="2026-01-01",
            period_end="2026-12-31",
        )
        is False
    )


def test_verify_snapshot_manifest_structured_response():
    """Validates verify_snapshot_manifest returns structured validation dictionary.

    @req:PRD-AGG-001
    @req:21 CFR Part 11
    @req:ADR-2199
    """
    manifest = {"cases_count": 5, "subjects_count": 10, "hash": "abcd" * 16}
    expected = "abcd" * 16

    res = RegulatoryAuditLedger.verify_snapshot_manifest(
        snapshot_id="snap_test_01",
        expected_hash=expected,
        dataset_manifest=manifest,
    )

    assert res["is_valid"] is True
    assert res["match_status"] == "VERIFIED_MATCH"
    assert res["snapshot_id"] == "snap_test_01"
    assert "verified_at" in res

    # Tampered manifest
    bad_manifest = {"cases_count": 999, "subjects_count": 0}
    bad_res = RegulatoryAuditLedger.verify_snapshot_manifest(
        snapshot_id="snap_test_02",
        expected_hash="1234" * 16,
        dataset_manifest=bad_manifest,
    )
    assert bad_res["is_valid"] is False
    assert bad_res["match_status"] == "HASH_MISMATCH_TAMPER_DETECTED"


# =========================================================================
# 3. 21 CFR Part 11 Electronic Signature Sealing
# =========================================================================


def test_seal_qppv_signoff_authorization_and_manifest():
    """Validates 21 CFR Part 11 QPPV electronic signature seal generation and role gating.

    @req:PRD-AGG-003
    @req:PRD-PVG-008
    @req:21 CFR Part 11
    @req:ADR-2199
    """
    report_payload = {
        "report_id": "RPT-001",
        "study_id": "STUDY-001",
        "total_saes": 3,
    }

    # Negative test: unauthorized role
    with pytest.raises(ValueError, match="is not authorized to sign"):
        RegulatoryAuditLedger.seal_qppv_signoff(
            report_id="RPT-001",
            report_type="DSUR",
            study_id="STUDY-001",
            compound_name="CADENCE-X",
            user_id="crc_user",
            user_roles=["site_crc"],
            signature_secret="secret123",
            meaning_of_signature="Approval",
            snapshot_hash="a" * 64,
            report_payload=report_payload,
        )

    # Positive test: authorized QPPV role
    qppv_manifest = RegulatoryAuditLedger.seal_qppv_signoff(
        report_id="RPT-001",
        report_type="DSUR",
        study_id="STUDY-001",
        compound_name="CADENCE-X",
        user_id="lead_qppv_officer",
        user_roles=["qppv", "safety_physician"],
        signature_secret="qppv-secret-xyz",
        meaning_of_signature="Executive QPPV Approval for EMA/FDA Submission",
        snapshot_hash="a" * 64,
        report_payload=report_payload,
    )

    assert qppv_manifest.manifest_id.startswith("sig_rep_")
    assert qppv_manifest.verification_status == "VALID"
    assert qppv_manifest.qppv_user_id == "lead_qppv_officer"
    assert qppv_manifest.qppv_role == "Qualified Person for Pharmacovigilance (QPPV)"
    assert len(qppv_manifest.report_hash) == 64


def test_seal_electronic_signature_generic_and_role_gating():
    """Validates generic 21 CFR Part 11 electronic signature sealing.

    @req:PRD-PVG-008
    @req:21 CFR Part 11
    @req:ADR-2199
    """
    payload_to_sign = {
        "entity_id": "ALERT-001",
        "action": "CONFIRM_SIGNAL",
    }

    # Unauthorized role rejected
    with pytest.raises(ValueError, match="is not authorized for 21 CFR Part 11"):
        RegulatoryAuditLedger.seal_electronic_signature(
            user_id="unauth_user",
            user_roles=["site_coordinator"],
            signature_secret="sec123",
            meaning_of_signature="Sign Alert",
            payload_to_sign=payload_to_sign,
        )

    # Authorized physician
    manifest_id, sig_token = RegulatoryAuditLedger.seal_electronic_signature(
        user_id="dr_smith",
        user_roles=["safety_physician"],
        signature_secret="sec123",
        meaning_of_signature="Safety Physician Triage Sign-off",
        payload_to_sign=payload_to_sign,
    )

    assert manifest_id.startswith("sig_")
    assert len(sig_token) == 64


# =========================================================================
# 4. AS2 B2B EDI Gateway MIC Calculation & MDN Verification (RFC 4130)
# =========================================================================


def test_as2_mic_calculation_and_envelope_sealing():
    """Validates RFC 4130 Message Integrity Check (MIC) formatting and outbound envelope sealing.

    @req:PRD-PVG-002
    @req:PRD-SYS-001
    @req:ADR-2199
    """
    xml_content = "<?xml version='1.0'?><icsr><id>E2B-001</id></icsr>"
    mic = RegulatoryAuditLedger.calculate_as2_mic(xml_content)

    assert mic.endswith(", sha-256")
    b64_part = mic.split(",")[0].strip()
    assert len(b64_part) > 20

    envelope = RegulatoryAuditLedger.seal_outbound_as2_envelope(
        as2_from="CADENCE-SPONSOR",
        as2_to="FDA-ESG-GATEWAY",
        xml_payload=xml_content,
    )

    assert envelope.as2_from == "CADENCE-SPONSOR"
    assert envelope.as2_to == "FDA-ESG-GATEWAY"
    assert envelope.content_mic_sha256 == mic
    assert envelope.headers["AS2-Version"] == "1.2"
    assert envelope.headers["Message-ID"] == envelope.message_id


def test_as2_mdn_receipt_verification_and_tamper_detection():
    """Validates MDN non-repudiation parsing and MIC tamper detection.

    @req:PRD-PVG-002
    @req:PRD-SYS-001
    @req:ADR-2199
    """
    xml_content = "<?xml version='1.0'?><test>payload</test>"
    envelope = RegulatoryAuditLedger.seal_outbound_as2_envelope(
        as2_from="CADENCE-SPONSOR",
        as2_to="EMA-GATEWAY",
        xml_payload=xml_content,
    )

    # 1. Valid MDN matching MIC
    valid_mdn_text = f"""
Message-ID: <mdn_test_123@ema.europa.eu>
Original-Message-ID: {envelope.message_id}
Reporting-UA: EudraVigilance Gateway 2.0
Disposition: automatic-action/MDN-sent-automatically; processed
Received-Content-MIC: {envelope.content_mic_sha256}
"""
    receipt = RegulatoryAuditLedger.verify_as2_mdn_receipt(
        raw_mdn_text=valid_mdn_text,
        expected_outbound_envelope=envelope,
    )
    assert receipt.disposition_status == AS2DispositionStatus.PROCESSED
    assert receipt.is_mic_verified is True
    assert receipt.verification_error is None

    # 2. Tampered MDN with mismatched MIC
    tampered_mdn_text = f"""
Message-ID: <mdn_test_tampered@ema.europa.eu>
Original-Message-ID: {envelope.message_id}
Disposition: automatic-action/MDN-sent-automatically; processed
Received-Content-MIC: corrupted_mic_hash_xyz==, sha-256
"""
    tampered_receipt = RegulatoryAuditLedger.verify_as2_mdn_receipt(
        raw_mdn_text=tampered_mdn_text,
        expected_outbound_envelope=envelope,
    )
    assert tampered_receipt.disposition_status == AS2DispositionStatus.TAMPERED
    assert tampered_receipt.is_mic_verified is False
    assert "MIC mismatch" in str(tampered_receipt.verification_error)


# =========================================================================
# 5. Medical Device Safety Investigations & MIR Vigilance Sealing
# =========================================================================


def test_device_vigilance_deadline_calculation():
    """Validates statutory vigilance reporting deadlines per ISO 14155 / EU MDR / IVDR.

    @req:PRD-SYS-126
    @req:PRD-DEV-003
    @req:ADR-2209
    """
    ref_ts = datetime(2026, 8, 1, 12, 0, 0)

    # 2 calendar days for public health threat
    days_pht, due_pht = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="SADE",
        is_public_health_threat=True,
        now_ts=ref_ts,
    )
    assert days_pht == 2
    assert due_pht == ref_ts + timedelta(days=2)

    # 10 calendar days for SADE / USADE
    days_sade, due_sade = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="SADE",
        now_ts=ref_ts,
    )
    assert days_sade == 10
    assert due_sade == ref_ts + timedelta(days=10)

    days_usade, due_usade = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="USADE",
        now_ts=ref_ts,
    )
    assert days_usade == 10

    # 15 calendar days for general ADE / Device Deficiency
    days_ade, due_ade = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="ADE",
        now_ts=ref_ts,
    )
    assert days_ade == 15
    assert due_ade == ref_ts + timedelta(days=15)


def test_device_adjudication_and_mir_report_sealing():
    """Validates Safety Physician device adjudication sealing and EU MDR MIR v7.2 XML/JSON generation.

    @req:PRD-SYS-126
    @req:PRD-DEV-004
    @req:21 CFR Part 11
    @req:ADR-2209
    """
    # 1. Seal adjudication
    adj_seal = RegulatoryAuditLedger.seal_device_adjudication(
        investigation_id="INV-DEV-001",
        case_number="DEV-STUDY-001",
        classification="SADE",
        causality="PROBABLE",
        capa_required=True,
        safety_physician_user_id="physician_01",
        signature_token="valid_part11_physician_token_123",
        reason_for_change="Formal Safety Physician Adjudication",
    )
    assert adj_seal["verification_status"] == "VALID"
    assert adj_seal["esignature_manifest_id"] == "valid_part11_physician_token_123"
    assert len(adj_seal["adjudication_digest"]) == 64

    # 2. Seal MIR report
    case_dict = {
        "case_number": "DEV-STUDY-001",
        "device_name": "CardioLead Electrode",
        "device_udi_di": "00840123456789",
        "lot_number": "LOT-ELECTRODE-99",
        "serial_number": "SN-001",
        "study_id": "STUDY-DEV-101",
        "site_id": "SITE-101",
        "classification": "SADE",
        "causality": "PROBABLE",
        "is_usade": False,
        "vigilance_deadline_days": 10,
        "vigilance_due_date": "2026-08-11T12:00:00",
        "status": "RESOLVED",
        "capa_required": True,
        "capa_reference": "CAPA-2026-DEV-01",
        "fsca_required": False,
        "safety_physician_user_id": "physician_01",
        "adjudicated_at": "2026-08-01T12:00:00",
    }
    report = RegulatoryAuditLedger.seal_device_vigilance_report(case_dict)

    assert report["report_type"] == "EU_MDR_MIR_V7_2"
    assert "ManufacturerIncidentReport" in report["payload_xml"]
    assert report["payload_json"]["device_identification"]["udi_di"] == "00840123456789"
    assert len(report["payload_digest"]) == 64


# =========================================================================
# 6. Service Integration & Caller Verification
# =========================================================================


@pytest.mark.asyncio
async def test_device_safety_service_end_to_end_lifecycle():
    """Validates DeviceSafetyService operations (open, list, get, adjudicate, vigilance report).

    @req:PRD-SYS-126
    @req:PRD-DEV-003
    @req:PRD-DEV-004
    @req:21 CFR Part 11
    @req:ADR-2209
    """
    async with db_manager.session_maker() as session:
        # 1. Open investigation
        create_dto = DeviceSafetyInvestigationCreateDTO(
            study_id="STUDY-SVC-DEV",
            site_id="SITE-01",
            deficiency_id="DEFIC-01",
            device_name="Smart Infusion Pump",
            device_udi_di="00840000000001",
            lot_number="LOT-01",
            classification="SADE",
            reason_for_change="Initiate SADE inquiry",
        )
        inv = await DeviceSafetyService.open_investigation(
            session=session,
            payload=create_dto,
            user_id="safety_officer_01",
        )
        assert inv.status == "OPEN"
        assert inv.vigilance_deadline_days == 10

        # 2. Query
        cases = await DeviceSafetyService.list_investigations(
            session=session,
            study_id="STUDY-SVC-DEV",
            classification="SADE",
        )
        assert len(cases) == 1

        # 3. Adjudicate with Part 11 signature
        adj_dto = DeviceSafetyInvestigationAdjudicateDTO(
            classification="SADE",
            causality="PROBABLE",
            is_usade=False,
            capa_required=True,
            capa_reference="CAPA-DEV-123",
            fsca_required=False,
            signature_token="sig_physician_token_abc_123",
            reason_for_change="Safety Physician Adjudication",
        )
        adjudicated = await DeviceSafetyService.adjudicate_investigation(
            session=session,
            investigation_id=inv.id,
            payload=adj_dto,
            user_id="safety_physician_01",
        )
        assert adjudicated.status == "RESOLVED"
        assert adjudicated.causality == "PROBABLE"
        assert adjudicated.safety_physician_user_id == "safety_physician_01"

        # 4. Generate vigilance report
        report = await DeviceSafetyService.generate_vigilance_report(
            session=session,
            investigation_id=inv.id,
        )
        assert report.report_type == "EU_MDR_MIR_V7_2"
        assert "<ManufacturerIncidentReport" in report.payload_xml


def test_as2_gateway_delegates_to_regulatory_audit_ledger():
    """Validates AS2GatewayService delegation to RegulatoryAuditLedger.

    @req:PRD-PVG-002
    @req:ADR-2199
    """
    xml_data = "<icsr>test</icsr>"
    mic1 = AS2GatewayService.calculate_mic(xml_data)
    mic2 = RegulatoryAuditLedger.calculate_as2_mic(xml_data)
    assert mic1 == mic2

    env = AS2GatewayService.create_outbound_envelope(
        as2_from="SENDER",
        as2_to="RECEIVER",
        xml_payload=xml_data,
    )
    assert env.content_mic_sha256 == mic1


def test_regulatory_snapshot_manager_delegates_to_regulatory_audit_ledger():
    """Validates RegulatorySnapshotManager delegation to RegulatoryAuditLedger.

    @req:PRD-AGG-001
    @req:ADR-2199
    """
    cases = [{"case_id": "C-1", "meddra_pt": "Nausea"}]
    subjects = [{"subject_id": "S-1", "treatment_arm": "Active"}]

    snap1 = RegulatorySnapshotManager.create_snapshot(
        study_id="STUDY-DEL-01",
        compound_name="CADENCE",
        reporting_period_start="2026-01-01",
        reporting_period_end="2026-12-31",
        raw_cases=cases,
        subjects_data=subjects,
    )
    snap2 = RegulatoryAuditLedger.create_snapshot(
        study_id="STUDY-DEL-01",
        compound_name="CADENCE",
        reporting_period_start="2026-01-01",
        reporting_period_end="2026-12-31",
        raw_cases=cases,
        subjects_data=subjects,
    )
    assert snap1.snapshot_hash == snap2.snapshot_hash
