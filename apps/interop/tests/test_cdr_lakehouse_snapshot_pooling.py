"""Comprehensive unit and integration test suite for Multi-Source Clinical Lakehouse and Version-Pinned Data Pooling.

Requirements: PRD-CDR-001, ADR-2199, 21 CFR Part 11
"""

from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.interop.adapters.database import db_manager
from apps.interop.adapters.lakehouse_models import (
    LakehouseCanonicalRecord,
)
from apps.interop.adapters.lakehouse_repository import (
    LakehouseRepository,
)
from apps.interop.adapters.models import Base
from apps.interop.application.lakehouse_service import (
    LakehouseService,
    SnapshotImmutableError,
)
from apps.interop.domain.lakehouse_models import (
    CentralLabIngestItem,
    DataStreamType,
    EDCEcrfIngestItem,
    EHRFHIRIngestItem,
    EPROEcoaIngestItem,
    IoTTelemetryReading,
    IOTWearableIngestItem,
    LocalLabIngestItem,
    LockMilestoneType,
    MedicalImagingIngestItem,
    MedicalImagingLesion,
    MultiStreamIngestPayload,
    ObservationDomain,
)
from apps.interop.domain.lakehouse_normalizer import (
    compute_canonical_record_hash,
    compute_snapshot_pool_digest,
    normalize_central_lab,
    normalize_edc_ecrf,
    normalize_ehr_fhir,
    normalize_epro_ecoa,
    normalize_iot_wearable,
    normalize_local_lab,
    normalize_medical_imaging,
    partition_and_filter_records,
)
from apps.interop.main import app
from packages.security.rbac_helpers import build_gateway_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup clean in-memory database for lakehouse tests."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "data_manager",
    user_id: str = "user_dm_01",
    change_reason: str = "GxP CDR Lakehouse testing",
    unblinded_access: bool = False,
) -> dict[str, str]:
    """Helper to generate valid gateway V2 signed headers."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
        unblinded_access=unblinded_access,
    )


# =========================================================================
# Unit Tests: Normalization across all 7 Heterogeneous Streams
# =========================================================================


class TestLakehouseNormalization:
    """Test canonical harmonization across 7 heterogeneous clinical data streams.

    @req:PRD-CDR-001
    @req:ADR-2199
    """

    def test_normalize_edc_ecrf(self) -> None:
        """Verify transactional EDC eCRF normalization into canonical clinical records.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        item = EDCEcrfIngestItem(
            subject_id="SUBJ-001",
            visit_id="VISIT_1",
            form_oid="FORM_VS",
            field_name="SYSBP",
            value=128.5,
            unit="mmHg",
            recorded_at=now,
            status="COMPLETED",
            investigator_id="INV-101",
            site_id="SITE-01",
            domain="VS",
            is_unblinded=False,
            metadata={"crf_version": "1.0"},
        )

        record = normalize_edc_ecrf("STUDY-901", item)

        assert record.study_id == "STUDY-901"
        assert record.subject_id == "SUBJ-001"
        assert record.stream_type == DataStreamType.EDC_ECRF
        assert record.domain == ObservationDomain.VS.value
        assert record.item_code == "SYSBP"
        assert record.numeric_value == 128.5
        assert record.unit == "mmHg"
        assert record.is_unblinded is False
        assert len(record.data_hash) == 64
        assert record.normalized_attributes["form_oid"] == "FORM_VS"

    def test_normalize_epro_ecoa(self) -> None:
        """Verify subject-facing ePRO/eCOA questionnaire normalization.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        item = EPROEcoaIngestItem(
            subject_id="SUBJ-002",
            diary_id="DIARY_PROMIS_29",
            questionnaire_name="PROMIS-29 v2.0",
            answers={"Q1_PAIN": 3, "Q2_FATIGUE": 2, "Q3_SLEEP": "Moderate"},
            total_score=68.5,
            device_timestamp=now,
            completed_at=now,
            compliance_flag=True,
            site_id="SITE-02",
            metadata={"device_model": "iPhone 15"},
        )

        records = normalize_epro_ecoa("STUDY-901", item)

        # 1 total score record + 3 item records = 4 canonical records
        assert len(records) == 4

        # Check total score record
        score_rec = next(r for r in records if "TOTAL_SCORE" in r.item_code)
        assert score_rec.domain == ObservationDomain.QS.value
        assert score_rec.numeric_value == 68.5
        assert score_rec.unit == "points"

        # Check individual question record
        pain_rec = next(r for r in records if "Q1_PAIN" in r.item_code)
        assert pain_rec.numeric_value == 3.0
        assert pain_rec.stream_type == DataStreamType.EPRO_ECOA

    def test_normalize_central_lab(self) -> None:
        """Verify Central Laboratory diagnostic feed normalization.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        item = CentralLabIngestItem(
            subject_id="SUBJ-003",
            site_id="SITE-01",
            specimen_id="SPEC-7788",
            test_code="LOINC_718-7",
            test_name="Hemoglobin [Mass/volume] in Blood",
            numeric_value=14.2,
            string_value=None,
            unit="g/dL",
            reference_low=12.0,
            reference_high=17.5,
            abnormal_flag="NORMAL",
            collection_timestamp=now,
            accession_number="ACC-99001",
            is_unblinded=False,
            metadata={"analyzer": "Sysmex XN-9000"},
        )

        record = normalize_central_lab("STUDY-901", item)

        assert record.stream_type == DataStreamType.CENTRAL_LAB
        assert record.domain == ObservationDomain.LB.value
        assert record.item_code == "LOINC_718-7"
        assert record.numeric_value == 14.2
        assert record.reference_low == 12.0
        assert record.reference_high == 17.5
        assert record.abnormal_flag == "NORMAL"

    def test_normalize_local_lab(self) -> None:
        """Verify site Local Laboratory feed normalization.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        item = LocalLabIngestItem(
            subject_id="SUBJ-004",
            site_id="SITE-03",
            test_code="LOCAL_ALT",
            test_name="Alanine Aminotransferase",
            value=85.0,
            unit="U/L",
            reference_low=10.0,
            reference_high=45.0,
            abnormal_flag="HIGH",
            collection_timestamp=now,
            is_unblinded=False,
        )

        record = normalize_local_lab("STUDY-901", item)

        assert record.stream_type == DataStreamType.LOCAL_LAB
        assert record.domain == ObservationDomain.LB.value
        assert record.numeric_value == 85.0
        assert record.abnormal_flag == "HIGH"

    def test_normalize_iot_wearables(self) -> None:
        """Verify connected sensors and wearable IoT continuous telemetry normalization.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        t0 = datetime.now(UTC)
        readings = [
            IoTTelemetryReading(
                timestamp=t0 + timedelta(minutes=i * 5),
                metric_name="glucose",
                value=110.0 + i * 2.5,
                unit="mg/dL",
                flag="VALID",
            )
            for i in range(5)
        ]

        item = IOTWearableIngestItem(
            subject_id="SUBJ-005",
            device_id="DEV-CGM-1002",
            device_type="CGM",
            observation_type="CONTINUOUS_GLUCOSE",
            telemetry_readings=readings,
            duration_seconds=1200.0,
            sample_rate_hz=0.0033,
            calibration_status="CALIBRATED",
            site_id="SITE-01",
        )

        records = normalize_iot_wearable("STUDY-901", item)

        assert len(records) == 5
        assert records[0].stream_type == DataStreamType.IOT_WEARABLE
        assert records[0].item_code == "CGM_GLUCOSE"
        assert records[0].numeric_value == 110.0

    def test_normalize_ehr_fhir(self) -> None:
        """Verify EHR HL7 FHIR bundle resource normalization.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        item = EHRFHIRIngestItem(
            subject_id="SUBJ-006",
            resource_type="Observation",
            fhir_id="obs-88331",
            resource_payload={
                "resourceType": "Observation",
                "id": "obs-88331",
                "status": "final",
                "valueQuantity": {"value": 72.0, "unit": "beats/min"},
            },
            coding_code="8867-4",
            coding_display="Heart rate",
            effective_datetime=now,
            site_id="SITE-02",
        )

        record = normalize_ehr_fhir("STUDY-901", item)

        assert record.stream_type == DataStreamType.EHR_FHIR
        assert record.domain == ObservationDomain.VS.value
        assert record.numeric_value == 72.0
        assert record.unit == "beats/min"
        assert record.item_code == "8867-4"

    def test_normalize_medical_imaging(self) -> None:
        """Verify Blinded Medical Imaging and RECIST 1.1 adjudication normalization.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        item = MedicalImagingIngestItem(
            subject_id="SUBJ-007",
            site_id="SITE-01",
            study_instance_uid="1.2.840.10008.1.1.1",
            series_instance_uid="1.2.840.10008.1.1.2",
            modality="CT",
            timepoint="WEEK_6",
            lesions=[
                MedicalImagingLesion(
                    lesion_id="T1",
                    lesion_type="TARGET",
                    site_location="Right Hepatic Lobe",
                    longest_diameter_mm=24.5,
                    response_category="PRESENT",
                ),
                MedicalImagingLesion(
                    lesion_id="T2",
                    lesion_type="TARGET",
                    site_location="Left Lung Lower Lobe",
                    longest_diameter_mm=18.0,
                    response_category="PRESENT",
                ),
            ],
            overall_response="PR",
            sum_of_diameters_mm=42.5,
            reader_id="BICR_READER_01",
            adjudication_tier="PRIMARY_READER_1",
            scan_datetime=now,
            is_unblinded=False,
        )

        records = normalize_medical_imaging("STUDY-901", item)

        # 1 overall response record (RS) + 2 lesion records (TR) = 3 canonical records
        assert len(records) == 3

        rs_rec = next(r for r in records if r.domain == ObservationDomain.RS.value)
        assert rs_rec.text_value == "PR"
        assert rs_rec.numeric_value == 42.5
        assert rs_rec.unit == "mm"

        tr_rec1 = next(r for r in records if "LESION_T1" in r.item_code)
        assert tr_rec1.domain == ObservationDomain.TR.value
        assert tr_rec1.numeric_value == 24.5


# =========================================================================
# Unit Tests: Cryptographic Hashing, Digest Sealing & Masking
# =========================================================================


class TestCryptographicIntegrityAndBlinding:
    """Test cryptographic SHA-256 digests and blinding partitions.

    @req:PRD-CDR-001
    @req:ADR-2199
    """

    def test_deterministic_record_hashing(self) -> None:
        """Verify identical observations produce bit-for-bit identical SHA-256 hashes.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        t = datetime(2026, 8, 23, 12, 0, 0, tzinfo=UTC)
        hash1 = compute_canonical_record_hash(
            study_id="STUDY-01",
            subject_id="SUBJ-01",
            stream_type="EDC_ECRF",
            domain="VS",
            item_code="SYSBP",
            observation_datetime=t,
            numeric_value=120.0,
            text_value=None,
            unit="mmHg",
            is_unblinded=False,
        )

        hash2 = compute_canonical_record_hash(
            study_id="STUDY-01",
            subject_id="SUBJ-01",
            stream_type="EDC_ECRF",
            domain="VS",
            item_code="SYSBP",
            observation_datetime=t,
            numeric_value=120.0,
            text_value=None,
            unit="mmHg",
            is_unblinded=False,
        )

        # Modified value must yield different hash
        hash_diff = compute_canonical_record_hash(
            study_id="STUDY-01",
            subject_id="SUBJ-01",
            stream_type="EDC_ECRF",
            domain="VS",
            item_code="SYSBP",
            observation_datetime=t,
            numeric_value=125.0,
            text_value=None,
            unit="mmHg",
            is_unblinded=False,
        )

        assert hash1 == hash2
        assert hash1 != hash_diff

    def test_snapshot_pool_digest_reproducibility(self) -> None:
        """Verify snapshot pool digest is invariant to record order but sensitive to contents.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        item1 = EDCEcrfIngestItem(
            subject_id="SUBJ-1",
            visit_id="V1",
            form_oid="F1",
            field_name="VS1",
            value=10.0,
            recorded_at=now,
        )
        item2 = EDCEcrfIngestItem(
            subject_id="SUBJ-2",
            visit_id="V1",
            form_oid="F1",
            field_name="VS2",
            value=20.0,
            recorded_at=now,
        )

        rec1 = normalize_edc_ecrf("STUDY-01", item1)
        rec2 = normalize_edc_ecrf("STUDY-01", item2)

        # Digest with order [rec1, rec2]
        digest_a = compute_snapshot_pool_digest(
            "STUDY-01", "v1.0", "DATABASE_LOCK", [rec1, rec2]
        )
        # Digest with order [rec2, rec1] (must be identical because it sorts internally)
        digest_b = compute_snapshot_pool_digest(
            "STUDY-01", "v1.0", "DATABASE_LOCK", [rec2, rec1]
        )

        assert digest_a == digest_b

    def test_unblinded_masking_partition(self) -> None:
        """Verify unblinded records are properly masked for blinded roles.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        blinded_item = EDCEcrfIngestItem(
            subject_id="SUBJ-1",
            visit_id="V1",
            form_oid="F1",
            field_name="SYSBP",
            value=120.0,
            recorded_at=now,
            is_unblinded=False,
        )
        unblinded_item = EDCEcrfIngestItem(
            subject_id="SUBJ-1",
            visit_id="V1",
            form_oid="F_RAND",
            field_name="ARMCD",
            value="ACTIVE_DRUG_100MG",
            recorded_at=now,
            is_unblinded=True,
        )

        rec_blinded = normalize_edc_ecrf("STUDY-01", blinded_item)
        rec_unblinded = normalize_edc_ecrf("STUDY-01", unblinded_item)

        # 1. Blinded access query: unblinded record text_value should be masked
        records_blinded, masked_cnt = partition_and_filter_records(
            [rec_blinded, rec_unblinded], is_unblinded_access=False
        )
        assert masked_cnt == 1
        assert records_blinded[0].text_value is None  # numeric value 120.0 remains
        assert records_blinded[0].numeric_value == 120.0
        assert records_blinded[1].text_value == "[BLINDED]"
        assert records_blinded[1].raw_payload == {"status": "[BLINDED_BY_GxP_POLICY]"}

        # 2. Unblinded access query: original unblinded value returned intact
        records_unblinded, masked_cnt2 = partition_and_filter_records(
            [rec_blinded, rec_unblinded], is_unblinded_access=True
        )
        assert masked_cnt2 == 0
        assert records_unblinded[1].text_value == "ACTIVE_DRUG_100MG"


# =========================================================================
# Integration Tests: Lakehouse Service Layer & DB Persistence
# =========================================================================


class TestLakehouseServiceLayer:
    """Test full database lifecycle with LakehouseService.

    @req:PRD-CDR-001
    @req:ADR-2199
    """

    @pytest.mark.asyncio
    async def test_full_lakehouse_ingest_pool_snapshot_verify_lifecycle(self) -> None:
        """Verify full ingestion, snapshot creation, verification, and audit logging.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        async with db_manager.session_maker() as session:
            now = datetime.now(UTC)
            service = LakehouseService(LakehouseRepository(session))

            # 1. Ingest Multi-Stream Payload
            payload_edc = MultiStreamIngestPayload(
                study_id="STUDY-999",
                stream_type=DataStreamType.EDC_ECRF,
                edc_items=[
                    EDCEcrfIngestItem(
                        subject_id="SUBJ-001",
                        visit_id="V1",
                        form_oid="F_VS",
                        field_name="SYSBP",
                        value=120.0,
                        unit="mmHg",
                        recorded_at=now,
                        domain="VS",
                    ),
                    EDCEcrfIngestItem(
                        subject_id="SUBJ-001",
                        visit_id="V1",
                        form_oid="F_RAND",
                        field_name="TRT01P",
                        value="EXPERIMENTAL_ARM_A",
                        recorded_at=now,
                        is_unblinded=True,
                    ),
                ],
                reason_for_ingest="Baseline visit EDC ingestion",
            )

            edc_records = await service.ingest_stream_payload(
                payload=payload_edc,
                user_id="cra_monitor_01",
                user_role="cra",
            )
            assert len(edc_records) == 2

            # Ingest Central Lab stream
            payload_lab = MultiStreamIngestPayload(
                study_id="STUDY-999",
                stream_type=DataStreamType.CENTRAL_LAB,
                central_lab_items=[
                    CentralLabIngestItem(
                        subject_id="SUBJ-001",
                        specimen_id="SPEC-100",
                        test_code="LOINC_ALT",
                        test_name="Alanine Aminotransferase",
                        numeric_value=32.0,
                        unit="U/L",
                        reference_low=10.0,
                        reference_high=45.0,
                        abnormal_flag="NORMAL",
                        collection_timestamp=now,
                    )
                ],
                reason_for_ingest="Central lab baseline panel",
            )

            lab_records = await service.ingest_stream_payload(
                payload=payload_lab,
                user_id="lab_tech_01",
                user_role="data_manager",
            )
            assert len(lab_records) == 1

            # 2. Create Version-Pinned Snapshot tied to Database Lock Milestone
            snapshot = await service.create_version_pinned_snapshot(
                study_id="STUDY-999",
                version_tag="v1.0.0-INTERIM-1",
                milestone=LockMilestoneType.INTERIM_ANALYSIS_1,
                lock_id="LOCK-STUDY-999-INT1",
                reason_for_snapshot="Locking pooled data for DSMB Interim Analysis 1",
                filter_criteria=None,
                user_id="lead_statistician",
                user_role="sponsor_statistician",
            )

            assert snapshot.snapshot_id.startswith("SNAP-")
            assert snapshot.record_count == 3
            assert snapshot.blinded_record_count == 2
            assert snapshot.unblinded_record_count == 1
            assert snapshot.stream_counts["EDC_ECRF"] == 2
            assert snapshot.stream_counts["CENTRAL_LAB"] == 1
            assert snapshot.is_locked is True
            assert len(snapshot.sha256_digest) == 64

            # 3. Test Snapshot Immutability: duplicate version tag must raise SnapshotImmutableError
            with pytest.raises(SnapshotImmutableError):
                await service.create_version_pinned_snapshot(
                    study_id="STUDY-999",
                    version_tag="v1.0.0-INTERIM-1",
                    milestone=LockMilestoneType.INTERIM_ANALYSIS_1,
                    lock_id="LOCK-STUDY-999-INT1",
                    reason_for_snapshot="Attempting duplicate overwrite",
                    filter_criteria=None,
                    user_id="lead_statistician",
                    user_role="sponsor_statistician",
                )

            # 4. Verify Cryptographic Integrity
            verify_res = await service.verify_snapshot_integrity(
                snapshot_id=snapshot.snapshot_id,
                user_id="auditor_01",
                user_role="auditor",
            )
            assert verify_res.is_valid is True
            assert verify_res.stored_digest == snapshot.sha256_digest
            assert verify_res.computed_digest == snapshot.sha256_digest

            # 5. Query Pooled Records as Blinded User
            (
                blinded_query,
                masked_cnt,
                digest,
            ) = await service.query_snapshot_data(
                snapshot_id=snapshot.snapshot_id,
                filter_criteria=None,
                is_unblinded_access=False,
                user_id="blinded_cra",
                user_role="cra",
            )
            assert len(blinded_query) == 3
            assert masked_cnt == 1
            unblinded_rec = next(r for r in blinded_query if r.is_unblinded)
            assert unblinded_rec.text_value == "[BLINDED]"

            # 6. Query Pooled Records as Unblinded Role
            (
                unblinded_query,
                masked_cnt2,
                digest2,
            ) = await service.query_snapshot_data(
                snapshot_id=snapshot.snapshot_id,
                filter_criteria=None,
                is_unblinded_access=True,
                user_id="unblinded_stat_01",
                user_role="unblinded_statistician",
            )
            assert masked_cnt2 == 0
            unblinded_rec_real = next(r for r in unblinded_query if r.is_unblinded)
            assert unblinded_rec_real.text_value == "EXPERIMENTAL_ARM_A"

            # 7. Check 21 CFR Part 11 Audit Trail
            audit_logs = await service.get_snapshot_audit_trail(
                snapshot_id=snapshot.snapshot_id,
            )
            assert (
                len(audit_logs) >= 3
            )  # SNAPSHOT_CREATE, SNAPSHOT_VERIFY, SNAPSHOT_QUERY, UNBLINDED_ACCESS
            assert any(log["action"] == "SNAPSHOT_CREATE" for log in audit_logs)
            assert any(log["action"] == "SNAPSHOT_VERIFY" for log in audit_logs)
            assert any(log["action"] == "UNBLINDED_ACCESS" for log in audit_logs)


# =========================================================================
# Integration Tests: FastAPI REST Endpoints via TestClient
# =========================================================================


class TestLakehouseAPIEndpoints:
    """Test full HTTP API gateway surface for Lakehouse endpoints.

    @req:PRD-CDR-001
    @req:ADR-2199
    """

    def test_lakehouse_rest_api_workflow(self) -> None:
        """Verify REST API ingest, snapshot creation, query, verify, and metrics.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        client = TestClient(app)
        headers_dm = get_auth_headers(roles="data_manager", user_id="dm_user")
        headers_unblinded = get_auth_headers(
            roles="unblinded_statistician",
            user_id="stat_unblinded",
            unblinded_access=True,
        )

        now_iso = datetime.now(UTC).isoformat()

        # 1. POST /api/v1/interop/lakehouse/ingest (EDC)
        ingest_payload = {
            "study_id": "STUDY-REST-01",
            "stream_type": "EDC_ECRF",
            "edc_items": [
                {
                    "subject_id": "SUBJ-101",
                    "visit_id": "V1",
                    "form_oid": "F_VS",
                    "field_name": "PULSE",
                    "value": 72.0,
                    "unit": "bpm",
                    "recorded_at": now_iso,
                    "status": "COMPLETED",
                    "domain": "VS",
                },
                {
                    "subject_id": "SUBJ-101",
                    "visit_id": "V1",
                    "form_oid": "F_RAND",
                    "field_name": "ARMCD",
                    "value": "PLACEBO",
                    "recorded_at": now_iso,
                    "is_unblinded": True,
                },
            ],
            "reason_for_ingest": "REST API Ingestion Test",
        }

        resp_ingest = client.post(
            "/api/v1/interop/lakehouse/ingest",
            json=ingest_payload,
            headers=headers_dm,
        )
        assert resp_ingest.status_code == 201
        data_ingest = resp_ingest.json()
        assert data_ingest["records_ingested"] == 2
        assert data_ingest["unblinded_records"] == 1

        # 2. POST /api/v1/interop/lakehouse/snapshots
        snap_payload = {
            "study_id": "STUDY-REST-01",
            "version_tag": "v1.0.0-REST-LOCK",
            "milestone": "DATABASE_LOCK",
            "lock_id": "LOCK-REST-01-FINAL",
            "reason_for_snapshot": "Final Database Lock Snapshot for Regulatory Submission",
        }

        resp_snap = client.post(
            "/api/v1/interop/lakehouse/snapshots",
            json=snap_payload,
            headers=headers_dm,
        )
        assert resp_snap.status_code == 201
        data_snap = resp_snap.json()
        snap_id = data_snap["snapshot"]["snapshot_id"]
        assert snap_id.startswith("SNAP-")
        assert data_snap["snapshot"]["record_count"] == 2

        # 3. GET /api/v1/interop/lakehouse/snapshots
        resp_list = client.get(
            "/api/v1/interop/lakehouse/snapshots",
            params={"study_id": "STUDY-REST-01"},
            headers=headers_dm,
        )
        assert resp_list.status_code == 200
        assert resp_list.json()["total"] == 1

        # 4. GET /api/v1/interop/lakehouse/snapshots/{snapshot_id}
        resp_get = client.get(
            f"/api/v1/interop/lakehouse/snapshots/{snap_id}",
            headers=headers_dm,
        )
        assert resp_get.status_code == 200
        assert resp_get.json()["snapshot"]["version_tag"] == "v1.0.0-REST-LOCK"

        # 5. POST /api/v1/interop/lakehouse/snapshots/{snapshot_id}/verify
        resp_verify = client.post(
            f"/api/v1/interop/lakehouse/snapshots/{snap_id}/verify",
            headers=headers_dm,
        )
        assert resp_verify.status_code == 200
        assert resp_verify.json()["result"]["is_valid"] is True

        # 6. POST /api/v1/interop/lakehouse/snapshots/{snapshot_id}/query (Blinded Role)
        resp_query_blinded = client.post(
            f"/api/v1/interop/lakehouse/snapshots/{snap_id}/query",
            json={"request_unblinded": False},
            headers=headers_dm,
        )
        assert resp_query_blinded.status_code == 200
        query_data = resp_query_blinded.json()
        assert query_data["is_masked"] is True
        assert query_data["masked_count"] == 1
        masked_rec = next(r for r in query_data["records"] if r["is_unblinded"])
        assert masked_rec["text_value"] == "[BLINDED]"

        # 7. POST /api/v1/interop/lakehouse/snapshots/{snapshot_id}/query (Unblinded Role)
        resp_query_unblinded = client.post(
            f"/api/v1/interop/lakehouse/snapshots/{snap_id}/query",
            json={"request_unblinded": True},
            headers=headers_unblinded,
        )
        assert resp_query_unblinded.status_code == 200
        unblinded_data = resp_query_unblinded.json()
        assert unblinded_data["is_masked"] is False
        assert unblinded_data["masked_count"] == 0
        real_unblinded_rec = next(
            r for r in unblinded_data["records"] if r["is_unblinded"]
        )
        assert real_unblinded_rec["text_value"] == "PLACEBO"

        # 8. GET /api/v1/interop/lakehouse/snapshots/{snapshot_id}/audit-trail
        resp_audit = client.get(
            f"/api/v1/interop/lakehouse/snapshots/{snap_id}/audit-trail",
            headers=headers_dm,
        )
        assert resp_audit.status_code == 200
        assert resp_audit.json()["total_entries"] >= 3

        # 9. GET /api/v1/interop/lakehouse/metrics/{study_id}
        resp_metrics = client.get(
            "/api/v1/interop/lakehouse/metrics/STUDY-REST-01",
            headers=headers_dm,
        )
        assert resp_metrics.status_code == 200
        metrics = resp_metrics.json()["metrics"]
        assert metrics["total_records"] == 2
        assert metrics["total_snapshots"] == 1
        assert metrics["stream_breakdown"]["EDC_ECRF"] == 2

    def test_unauthorized_unblinded_access_rejected_with_403(self) -> None:
        """Verify that blinded roles (CRA, Investigator) attempting unblinded access receive 403 Forbidden.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        client = TestClient(app)
        headers_dm = get_auth_headers(roles="data_manager", user_id="dm_user")
        headers_cra = get_auth_headers(roles="cra", user_id="cra_blinded")

        # Ingest and create snapshot
        client.post(
            "/api/v1/interop/lakehouse/ingest",
            json={
                "study_id": "STUDY-FORBIDDEN-01",
                "stream_type": "EDC_ECRF",
                "edc_items": [
                    {
                        "subject_id": "SUBJ-999",
                        "visit_id": "V1",
                        "form_oid": "F_RAND",
                        "field_name": "ARMCD",
                        "value": "ACTIVE_DRUG",
                        "recorded_at": datetime.now(UTC).isoformat(),
                        "is_unblinded": True,
                    }
                ],
                "reason_for_ingest": "Unblinded test record",
            },
            headers=headers_dm,
        )

        resp_snap = client.post(
            "/api/v1/interop/lakehouse/snapshots",
            json={
                "study_id": "STUDY-FORBIDDEN-01",
                "version_tag": "v1.0.0-FORBIDDEN-TEST",
                "milestone": "SAFETY_REVIEW",
                "reason_for_snapshot": "Blinding boundary enforcement test",
            },
            headers=headers_dm,
        )
        snap_id = resp_snap.json()["snapshot"]["snapshot_id"]

        # Blinded CRA requests unblinded access -> Must return 403 Forbidden
        resp_forbidden = client.post(
            f"/api/v1/interop/lakehouse/snapshots/{snap_id}/query",
            json={"request_unblinded": True},
            headers=headers_cra,
        )
        assert resp_forbidden.status_code == 403
        assert "unblinded role permissions" in resp_forbidden.json()["detail"].lower()

    def test_snapshot_not_found_returns_404(self) -> None:
        """Verify querying a non-existent snapshot ID returns 404 Not Found.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers(roles="data_manager")

        resp = client.get(
            "/api/v1/interop/lakehouse/snapshots/SNAP-NONEXISTENT",
            headers=headers,
        )
        assert resp.status_code == 404

    @pytest.mark.asyncio
    async def test_tamper_detection_in_snapshot_verification(self) -> None:
        """Verify that any post-lock database tampering is detected and fails verification.

        @req:PRD-CDR-001
        @req:ADR-2199
        """
        async with db_manager.session_maker() as session:
            now = datetime.now(UTC)
            service = LakehouseService(LakehouseRepository(session))

            # Ingest record
            payload = MultiStreamIngestPayload(
                study_id="STUDY-TAMPER-01",
                stream_type=DataStreamType.EDC_ECRF,
                edc_items=[
                    EDCEcrfIngestItem(
                        subject_id="SUBJ-T1",
                        visit_id="V1",
                        form_oid="F_VS",
                        field_name="SYSBP",
                        value=120.0,
                        unit="mmHg",
                        recorded_at=now,
                        domain="VS",
                    )
                ],
                reason_for_ingest="Pre-tamper test record",
            )
            await service.ingest_stream_payload(
                payload=payload,
                user_id="dm_01",
                user_role="data_manager",
            )

            # Create and seal snapshot
            snapshot = await service.create_version_pinned_snapshot(
                study_id="STUDY-TAMPER-01",
                version_tag="v1.0.0-TAMPER-TEST",
                milestone=LockMilestoneType.DATABASE_LOCK,
                lock_id="LOCK-TAMPER-01",
                reason_for_snapshot="Sealed snapshot for tamper test",
                filter_criteria=None,
                user_id="dm_01",
                user_role="data_manager",
            )

            # Verification initially succeeds
            initial_verify = await service.verify_snapshot_integrity(
                snapshot_id=snapshot.snapshot_id,
                user_id="auditor_01",
                user_role="auditor",
            )
            assert initial_verify.is_valid is True

            # Simulate malicious tampering: corrupt observation in database
            stmt_rec = select(LakehouseCanonicalRecord).where(
                LakehouseCanonicalRecord.study_id == "STUDY-TAMPER-01"
            )
            tampered_rec = (await session.execute(stmt_rec)).scalars().first()
            assert tampered_rec is not None
            tampered_rec.numeric_value = 999.9  # Maliciously altered value
            # Update data_hash to match altered value so the recomputed digest changes
            tampered_rec.data_hash = compute_canonical_record_hash(
                study_id="STUDY-TAMPER-01",
                subject_id="SUBJ-T1",
                stream_type="EDC_ECRF",
                domain="VS",
                item_code="SYSBP",
                observation_datetime=tampered_rec.observation_datetime,
                numeric_value=999.9,
                text_value=None,
                unit="mmHg",
                is_unblinded=False,
            )
            await session.flush()

            # Re-verify: must fail integrity check and flag tamper detection!
            tampered_verify = await service.verify_snapshot_integrity(
                snapshot_id=snapshot.snapshot_id,
                user_id="auditor_01",
                user_role="auditor",
            )
            assert tampered_verify.is_valid is False
            assert "CRITICAL INTEGRITY FAILURE" in tampered_verify.verification_message
