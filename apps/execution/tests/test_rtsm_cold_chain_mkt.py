"""Unit and Integration Tests for RTSM Cold Chain Ingestion, Arrhenius MKT Excursion Engine & Lot Re-Labeling.

Requirements: PRD-SCM-002, PRD-SCM-003, ADR-2199, USP <1079>, PDA TR 39
"""

import os
import time
from datetime import UTC, datetime, timedelta

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    DrugStabilityBudget,
    IntegrationOutbox,
    IPKit,
    PackingManifest,
    SiteInventory,
    TransferOrder,
)
from apps.execution.domain.rtsm_models import (
    ExcursionEvaluationStatus,
    KitStatus,
    LoggerEntityType,
    RawTemperaturePoint,
    StabilityBudgetConfig,
)
from apps.execution.main import app
from apps.execution.rtsm_cold_chain import (
    InvalidLoggerFileError,
    calculate_mkt,
    evaluate_temperature_stability_budget,
    parse_logger_csv,
    parse_logger_json,
    parse_logger_xml,
)
from apps.execution.trial_lock import TrialLockManager
from packages.security.signing import generate_gateway_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Initializes clean test database."""
    from apps.execution.database.migrate import deploy_database_triggers

    TrialLockManager.reset()
    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_gateway_headers(
    user_id: str = "test_qa_lead",
    roles: str = "sponsor_admin",
    change_reason: str = "Cold chain validation test",
    site_id: str | None = None,
    include_sig_token: bool = True,
) -> dict:
    """Generate signed gateway V2 headers for authenticated test requests."""
    from jose import jwt

    timestamp = str(time.time())
    sig_token = None
    if include_sig_token:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        sig_token = jwt.encode(
            sig_payload, b"valid-test-gateway-secret-key-2026", algorithm="HS256"
        )

    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=b"valid-test-gateway-secret-key-2026",  # pragma: allowlist secret
        change_reason=change_reason,
        site_id=site_id,
        sig_token=sig_token,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    if sig_token:
        headers["X-Sig-Token"] = sig_token
    return headers


# =========================================================================
# 1. Pure Arrhenius MKT Mathematical Tests
# =========================================================================


def test_arrhenius_mkt_uniform_temperature():
    """Verify MKT of a constant isothermal temperature profile equals that temperature.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    constant_temps = [5.0] * 100
    mkt = calculate_mkt(constant_temps, activation_energy_over_r=10000.0)
    assert abs(mkt - 5.0) < 0.001


def test_arrhenius_mkt_fluctuating_temperature_non_linear():
    """Verify Arrhenius kinetic rate acceleration yields MKT > arithmetic mean for fluctuating temperatures.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    # Fluctuation between 2.0°C and 8.0°C. Arithmetic mean is 5.0°C.
    fluctuating_temps = [2.0, 8.0]
    mkt = calculate_mkt(fluctuating_temps, activation_energy_over_r=10000.0)
    arithmetic_mean = sum(fluctuating_temps) / len(fluctuating_temps)

    assert arithmetic_mean == 5.0
    # Arrhenius equation weights higher temperatures more heavily:
    assert mkt > arithmetic_mean
    assert round(mkt, 2) == 5.54


def test_arrhenius_mkt_spike_sensitivity():
    """Verify high temperature spikes disproportionately accelerate thermal kinetic degradation.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    # 95 readings at 4°C, 5 transient readings spiking at 25°C
    profile = [4.0] * 95 + [25.0] * 5
    arithmetic_mean = sum(profile) / len(profile)  # 5.05°C
    mkt = calculate_mkt(profile, activation_energy_over_r=10000.0)

    # MKT should significantly exceed arithmetic mean due to the 25°C spikes
    assert mkt > arithmetic_mean
    assert mkt > 7.0  # Demonstrates Arrhenius exponential penalty (~7.58°C vs 5.05°C)


def test_arrhenius_mkt_boundary_and_singularity_rejection():
    """Verify edge cases: single temperature, empty input, and physical impossible temperatures.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    # Single reading
    assert calculate_mkt([4.2]) == 4.2

    # Empty list
    with pytest.raises(ValueError, match="At least one temperature reading"):
        calculate_mkt([])

    # Below absolute zero
    with pytest.raises(ValueError, match="below absolute zero"):
        calculate_mkt([-275.0])


# =========================================================================
# 2. Digital Temperature Logger Parser Tests (CSV, XML, JSON)
# =========================================================================


def test_parse_logger_csv_standard_and_semicolon():
    """Verify CSV parser correctly extracts timestamps and temperatures across standard delimiters.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    csv_standard = (
        "timestamp,temperature_celsius\n"
        "2026-08-23T08:00:00Z,4.5\n"
        "2026-08-23T08:15:00Z,5.0\n"
        "2026-08-23T08:30:00Z,4.8\n"
    )
    points = parse_logger_csv(csv_standard)
    assert len(points) == 3
    assert points[0].temperature_celsius == 4.5
    assert points[1].temperature_celsius == 5.0

    # Semicolon and comma decimals (European logger export)
    csv_european = (
        "DateTime;Temperature\n2026-08-23 09:00:00;4,2\n2026-08-23 09:15:00;4,9\n"
    )
    points_eu = parse_logger_csv(csv_european)
    assert len(points_eu) == 2
    assert points_eu[0].temperature_celsius == 4.2
    assert points_eu[1].temperature_celsius == 4.9


def test_parse_logger_xml_sensitech_and_temptale():
    """Verify XML parser extracts readings from standard medical temperature logger formats.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    xml_data = """<?xml version="1.0" encoding="UTF-8"?>
    <TemperatureLoggerReport loggerId="LOG-999">
        <Readings>
            <Reading>
                <Timestamp>2026-08-23T10:00:00Z</Timestamp>
                <Temperature>3.8</Temperature>
            </Reading>
            <Reading>
                <Timestamp>2026-08-23T10:15:00Z</Timestamp>
                <Temperature>4.1</Temperature>
            </Reading>
        </Readings>
    </TemperatureLoggerReport>
    """
    points = parse_logger_xml(xml_data)
    assert len(points) == 2
    assert points[0].temperature_celsius == 3.8
    assert points[1].temperature_celsius == 4.1


def test_parse_logger_json_payload():
    """Verify JSON parser handles arrays and nested data structures.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    json_data = """{
        "logger_id": "LOG-JSON-01",
        "readings": [
            {"timestamp": "2026-08-23T11:00:00Z", "temperature_celsius": 4.0},
            {"timestamp": "2026-08-23T11:15:00Z", "temperature_celsius": 4.4}
        ]
    }"""
    points = parse_logger_json(json_data)
    assert len(points) == 2
    assert points[0].temperature_celsius == 4.0
    assert points[1].temperature_celsius == 4.4


def test_parse_logger_corrupt_file_raises():
    """Verify malformed logger contents raise InvalidLoggerFileError.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    with pytest.raises(InvalidLoggerFileError):
        parse_logger_csv("corrupted,binary\ndata,bad_temp_abc")

    with pytest.raises(InvalidLoggerFileError):
        parse_logger_xml("<invalid><xml></unclosed>")

    with pytest.raises(InvalidLoggerFileError):
        parse_logger_json("{bad_json: missing_quotes}")


# =========================================================================
# 3. Stability Budget Evaluation Pure Unit Tests
# =========================================================================


def test_stability_budget_evaluation_compliant():
    """Verify temperature series strictly within bounds evaluates to COMPLIANT.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    base_time = datetime(2026, 8, 23, 8, 0, tzinfo=UTC)
    readings = [
        RawTemperaturePoint(
            timestamp=base_time + timedelta(minutes=15 * i), temperature_celsius=5.0
        )
        for i in range(20)
    ]
    budget = StabilityBudgetConfig(
        study_id="STUDY-001",
        kit_type="VIAL_A",
        min_temp_celsius=2.0,
        max_temp_celsius=8.0,
        max_mkt_celsius=8.0,
        max_cumulative_hours_above_upper=24.0,
        max_cumulative_hours_below_lower=0.0,
    )
    result = evaluate_temperature_stability_budget(
        study_id="STUDY-001",
        logger_id="LOG-001",
        entity_type=LoggerEntityType.SHIPMENT,
        entity_id="TO-100",
        readings=readings,
        budget=budget,
    )
    assert result.evaluation_status == ExcursionEvaluationStatus.COMPLIANT
    assert result.quarantine_triggered is False
    assert result.cumulative_hours_above_upper == 0.0
    assert result.cumulative_hours_below_lower == 0.0
    assert abs(result.mean_kinetic_temperature_celsius - 5.0) < 0.01


def test_stability_budget_evaluation_acceptable_excursion():
    """Verify minor excursion within cumulative allowable budget evaluates to EXCURSION_ACCEPTABLE.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    base_time = datetime(2026, 8, 23, 8, 0, tzinfo=UTC)
    # 20 readings at 5.0°C (5 hours) + 4 readings at 9.0°C (1 hour excursion)
    readings = [
        RawTemperaturePoint(
            timestamp=base_time + timedelta(minutes=15 * i), temperature_celsius=5.0
        )
        for i in range(20)
    ] + [
        RawTemperaturePoint(
            timestamp=base_time + timedelta(minutes=15 * (20 + i)),
            temperature_celsius=9.0,
        )
        for i in range(4)
    ]
    budget = StabilityBudgetConfig(
        study_id="STUDY-001",
        kit_type="VIAL_A",
        min_temp_celsius=2.0,
        max_temp_celsius=8.0,
        max_mkt_celsius=8.0,
        max_cumulative_hours_above_upper=24.0,  # 24 hours allowed, actual is 1 hour
        max_cumulative_hours_below_lower=0.0,
    )
    result = evaluate_temperature_stability_budget(
        study_id="STUDY-001",
        logger_id="LOG-001",
        entity_type=LoggerEntityType.SHIPMENT,
        entity_id="TO-100",
        readings=readings,
        budget=budget,
    )
    assert result.evaluation_status == ExcursionEvaluationStatus.EXCURSION_ACCEPTABLE
    assert result.quarantine_triggered is False
    assert result.cumulative_hours_above_upper > 0.0
    assert result.cumulative_hours_above_upper <= 24.0
    assert len(result.excursions) == 1
    assert result.excursions[0].excursion_type == "HIGH_TEMPERATURE"


def test_stability_budget_evaluation_breach_and_hard_boundary():
    """Verify critical heat / freezing excursion triggers EXCURSION_BREACH and quarantine flag.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    base_time = datetime(2026, 8, 23, 8, 0, tzinfo=UTC)
    # Spike to 28°C breaching hard max limit of 25°C
    readings = [
        RawTemperaturePoint(
            timestamp=base_time + timedelta(minutes=15 * i), temperature_celsius=5.0
        )
        for i in range(10)
    ] + [
        RawTemperaturePoint(
            timestamp=base_time + timedelta(minutes=15 * (10 + i)),
            temperature_celsius=28.0,
        )
        for i in range(4)
    ]
    budget = StabilityBudgetConfig(
        study_id="STUDY-001",
        kit_type="VIAL_A",
        min_temp_celsius=2.0,
        max_temp_celsius=8.0,
        max_mkt_celsius=8.0,
        max_cumulative_hours_above_upper=24.0,
        hard_max_temp_celsius=25.0,
    )
    result = evaluate_temperature_stability_budget(
        study_id="STUDY-001",
        logger_id="LOG-001",
        entity_type=LoggerEntityType.SHIPMENT,
        entity_id="TO-100",
        readings=readings,
        budget=budget,
    )
    assert result.evaluation_status == ExcursionEvaluationStatus.EXCURSION_BREACH
    assert result.quarantine_triggered is True
    assert result.hard_boundary_breached is True


# =========================================================================
# 4. Integration Tests: Shipment Ingestion, MKT & Automated Kit Quarantine
# =========================================================================


@pytest.mark.asyncio
async def test_shipment_temperature_excursion_quarantines_kits_and_outbox():
    """Verify shipment data logger breach automatically transitions kits to QUARANTINED and emits outbox event.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    study_id = "STUDY-COLD-101"
    transfer_order_num = "TO-SHIP-999"
    logger_id = "LOGGER-TEMP-001"

    async with db_manager.get_session_maker()() as session:
        # 1. Setup Transfer Order and Packing Manifest with serialized kits
        order = TransferOrder(
            study_id=study_id,
            transfer_order_number=transfer_order_num,
            source_node_id="DEPOT-CENTRAL",
            destination_node_id="SITE-101",
            kit_type="BIOLOGIC_VIAL",
            quantity=3,
            status="SHIPPED",
            temperature_condition="REFRIGERATED_2_TO_8",
        )
        session.add(order)
        await session.flush()

        manifest = PackingManifest(
            study_id=study_id,
            transfer_order_id=order.id,
            manifest_number="MAN-999",
            serialized_kit_numbers=["KIT-1001", "KIT-1002", "KIT-1003"],
            lot_number="LOT-2026-A",
            data_logger_id=logger_id,
            packed_by="depot_pharmacist",
        )
        session.add(manifest)

        # 2. Add IPKits in catalog
        for knum in ["KIT-1001", "KIT-1002", "KIT-1003"]:
            kit = IPKit(
                study_id=study_id,
                kit_number=knum,
                kit_type="BIOLOGIC_VIAL",
                status=KitStatus.AVAILABLE,
                lot_number="LOT-2026-A",
                node_id="SITE-101",
            )
            session.add(kit)

        # 3. Add Drug Stability Budget
        budget = DrugStabilityBudget(
            study_id=study_id,
            kit_type="BIOLOGIC_VIAL",
            storage_condition="REFRIGERATED_2_TO_8",
            min_temp_celsius=2.0,
            max_temp_celsius=8.0,
            max_mkt_celsius=8.0,
            max_cumulative_hours_above_upper=2.0,  # Strict: max 2 hours above 8°C
            hard_max_temp_celsius=20.0,
        )
        session.add(budget)
        await session.commit()

    # 4. Ingest CSV logger file showing continuous 30°C temperature breach
    csv_payload = (
        "timestamp,temperature_celsius\n"
        "2026-08-23T08:00:00Z,5.0\n"
        "2026-08-23T09:00:00Z,15.0\n"
        "2026-08-23T10:00:00Z,22.0\n"
        "2026-08-23T11:00:00Z,30.0\n"
        "2026-08-23T12:00:00Z,30.0\n"
    )

    headers = get_gateway_headers(
        user_id="cra_monitor", roles="cra", change_reason="Ingest arrival logger"
    )
    req_body = {
        "study_id": study_id,
        "logger_id": logger_id,
        "file_format": "CSV",
        "raw_payload": csv_payload,
        "filename": "logger_to_999.csv",
        "entity_type": "SHIPMENT",
        "entity_id": transfer_order_num,
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://testserver"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/rtsm/cold-chain/ingest",
            json=req_body,
            headers=headers,
        )
        assert resp.status_code == 201
        data = resp.json()
        assert data["evaluation_status"] == "EXCURSION_BREACH"
        assert data["quarantine_triggered"] is True
        assert data["quarantined_kit_count"] == 3

    # 5. Verify in DB: Kits are QUARANTINED and Outbox event was created
    async with db_manager.get_session_maker()() as session:
        stmt_kits = select(IPKit).where(IPKit.study_id == study_id)
        res_kits = await session.execute(stmt_kits)
        kits = list(res_kits.scalars().all())
        assert len(kits) == 3
        for k in kits:
            assert k.status == KitStatus.QUARANTINED
            assert "Cold Chain Excursion Breach" in k.quarantine_reason
            assert k.quarantined_at is not None

        stmt_outbox = select(IntegrationOutbox).where(
            IntegrationOutbox.event_type
            == "COLD_CHAIN_TEMPERATURE_EXCURSION_QUARANTINE"
        )
        res_outbox = await session.execute(stmt_outbox)
        outbox_entry = res_outbox.scalars().first()
        assert outbox_entry is not None
        assert outbox_entry.payload["quarantined_kit_count"] == 3


# =========================================================================
# 5. Integration Tests: Programmatic Dispensation Gating in EDC / RTSM
# =========================================================================


@pytest.mark.asyncio
async def test_dispensation_gating_blocks_quarantined_and_expired_kits():
    """Verify RTSM/EDC dispensation endpoint rejects QUARANTINED and EXPIRED kits with HTTP 422.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    study_id = "STUDY-GATING-202"
    site_id = "SITE-202"

    async with db_manager.get_session_maker()() as session:
        # 1. Setup Site Inventory
        inv = SiteInventory(
            study_id=study_id,
            site_id=site_id,
            kit_id="KIT-QUARANTINED",
            on_hand_qty=10,
            reorder_threshold=2,
        )
        inv2 = SiteInventory(
            study_id=study_id,
            site_id=site_id,
            kit_id="KIT-EXPIRED",
            on_hand_qty=10,
            reorder_threshold=2,
        )
        inv3 = SiteInventory(
            study_id=study_id,
            site_id=site_id,
            kit_id="KIT-AVAILABLE",
            on_hand_qty=10,
            reorder_threshold=2,
        )
        session.add_all([inv, inv2, inv3])

        # 2. Add IPKit Catalog records with different statuses
        kit_q = IPKit(
            study_id=study_id,
            kit_number="KIT-QUARANTINED",
            kit_type="VIAL",
            status=KitStatus.QUARANTINED,
            quarantine_reason="Temperature excursion breach: MKT > 15°C",
            node_id=site_id,
        )
        kit_exp = IPKit(
            study_id=study_id,
            kit_number="KIT-EXPIRED",
            kit_type="VIAL",
            status=KitStatus.AVAILABLE,
            expiration_date=datetime(2025, 1, 1, tzinfo=UTC),  # Past expiry date
            node_id=site_id,
        )
        kit_ok = IPKit(
            study_id=study_id,
            kit_number="KIT-AVAILABLE",
            kit_type="VIAL",
            status=KitStatus.AVAILABLE,
            expiration_date=datetime(2027, 1, 1, tzinfo=UTC),
            node_id=site_id,
        )
        session.add_all([kit_q, kit_exp, kit_ok])
        await session.commit()

    headers = get_gateway_headers(
        user_id="crc_user",
        roles="crc",
        change_reason="Subject Visit Dispensation",
        site_id=site_id,
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://testserver"
    ) as client:
        # A. Attempt to dispense Quarantined Kit -> BLOCKED (HTTP 422)
        payload_q = {
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUBJ-001",
            "visit_id": "VISIT-V1",
            "kit_id": "KIT-QUARANTINED",
            "quantity": 1,
        }
        res_q = await client.post(
            "/api/v1/execution/rtsm/dispense",
            json=payload_q,
            headers=headers,
        )
        assert res_q.status_code == 422
        assert "QUARANTINED" in res_q.json()["detail"]

        # B. Attempt to dispense Expired Kit -> BLOCKED (HTTP 422)
        payload_exp = {
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUBJ-001",
            "visit_id": "VISIT-V1",
            "kit_id": "KIT-EXPIRED",
            "quantity": 1,
        }
        res_exp = await client.post(
            "/api/v1/execution/rtsm/dispense",
            json=payload_exp,
            headers=headers,
        )
        assert res_exp.status_code == 422
        assert "EXPIRED" in res_exp.json()["detail"]

        # C. Dispense compliant available kit -> SUCCESS (HTTP 201)
        payload_ok = {
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUBJ-001",
            "visit_id": "VISIT-V1",
            "kit_id": "KIT-AVAILABLE",
            "quantity": 1,
        }
        res_ok = await client.post(
            "/api/v1/execution/rtsm/dispense",
            json=payload_ok,
            headers=headers,
        )
        assert res_ok.status_code == 201
        assert res_ok.json()["status"] == "success"


# =========================================================================
# 6. Integration Tests: Manual Quarantine & QA Disposition Release
# =========================================================================


@pytest.mark.asyncio
async def test_manual_quarantine_and_qa_disposition_release_workflow():
    """Verify manual quarantine placement and QA disposition release workflows.

    @req:PRD-SYS-184
    @req:PRD-SCM-002
    @req:ADR-2199
    """
    study_id = "STUDY-DISPO-303"
    site_id = "SITE-303"

    async with db_manager.get_session_maker()() as session:
        kit = IPKit(
            study_id=study_id,
            kit_number="KIT-DISPO-01",
            kit_type="ORAL_TAB",
            status=KitStatus.AVAILABLE,
            node_id=site_id,
        )
        session.add(kit)
        await session.commit()

    headers_cra = get_gateway_headers(
        user_id="cra_audit",
        roles="cra",
        change_reason="Investigator flagged physical box damage",
    )
    headers_qa = get_gateway_headers(
        user_id="qa_director",
        roles="sponsor_admin",
        change_reason="QA inspection determined secondary seal intact",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://testserver"
    ) as client:
        # 1. Manual Quarantine
        q_res = await client.post(
            "/api/v1/execution/rtsm/kits/quarantine",
            json={
                "study_id": study_id,
                "kit_numbers": ["KIT-DISPO-01"],
                "quarantine_reason": "Damaged outer packaging reported by CRC",
            },
            headers=headers_cra,
        )
        assert q_res.status_code == 200
        assert q_res.json()["quarantined_kit_count"] == 1

        # Verify kit is quarantined
        k_res = await client.get(
            f"/api/v1/execution/rtsm/kits?study_id={study_id}&status=QUARANTINED",
            headers=headers_cra,
        )
        assert k_res.status_code == 200
        assert len(k_res.json()) == 1
        assert k_res.json()[0]["kit_number"] == "KIT-DISPO-01"

        # 2. QA Disposition Release
        rel_res = await client.post(
            "/api/v1/execution/rtsm/kits/release-quarantine",
            json={
                "study_id": study_id,
                "kit_numbers": ["KIT-DISPO-01"],
                "qa_release_justification": "QA inspected physical seal; internal blister pack undamaged.",
            },
            headers=headers_qa,
        )
        assert rel_res.status_code == 200
        assert rel_res.json()["released_kit_count"] == 1

        # Verify kit is AVAILABLE again
        k_avail = await client.get(
            f"/api/v1/execution/rtsm/kits?study_id={study_id}&status=AVAILABLE",
            headers=headers_qa,
        )
        assert k_avail.status_code == 200
        assert len(k_avail.json()) == 1
        assert k_avail.json()[0]["status"] == "AVAILABLE"


# =========================================================================
# 7. Integration Tests: Lot Expiry Extension & Automated Re-Labeling (PRD-SCM-003)
# =========================================================================


@pytest.mark.asyncio
async def test_lot_expiry_extension_and_relabeling_task_lifecycle():
    """Verify lot expiry extension updates kits across facilities and generates verifiable re-labeling tasks.

    @req:PRD-SYS-184
    @req:PRD-SCM-003
    @req:ADR-2199
    """
    study_id = "STUDY-LOT-EXT-404"
    lot_number = "LOT-STAB-2026-X"

    initial_exp = datetime(2026, 12, 31, tzinfo=UTC)
    extended_exp = datetime(2027, 6, 30, tzinfo=UTC)

    async with db_manager.get_session_maker()() as session:
        # Setup kits distributed across Central Depot, Regional Depot, and Site 401
        kits = [
            IPKit(
                study_id=study_id,
                kit_number=f"KIT-L-{i}",
                kit_type="LYOPHILIZED_VIAL",
                lot_number=lot_number,
                expiration_date=initial_exp,
                node_id="CENTRAL_DEPOT" if i < 3 else "SITE_401",
                status=KitStatus.AVAILABLE,
            )
            for i in range(6)
        ]
        session.add_all(kits)
        await session.commit()

    headers_admin = get_gateway_headers(
        user_id="qa_supply_admin",
        roles="sponsor_admin",
        change_reason="Lot shelf-life extension approved per Stability Study Protocol STAB-2026-09",
    )
    headers_site = get_gateway_headers(
        user_id="site_pharmacist_401",
        roles="crc",
        change_reason="Applied physical extension label to kit boxes",
    )
    headers_cra = get_gateway_headers(
        user_id="cra_monitor",
        roles="cra",
        change_reason="Verified relabeling barcode seals",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://testserver"
    ) as client:
        # 1. Trigger Automated Lot Expiration Extension
        ext_payload = {
            "study_id": study_id,
            "lot_number": lot_number,
            "kit_type": "LYOPHILIZED_VIAL",
            "extended_expiry_date": extended_exp.isoformat(),
            "extension_reason": "12-month real-time stability testing data confirms potency stability.",
            "stability_protocol_reference": "STAB-PROTOCOL-2026-09",
        }
        res_ext = await client.post(
            "/api/v1/execution/rtsm/lots/extend-expiry",
            json=ext_payload,
            headers=headers_admin,
        )
        assert res_ext.status_code == 201
        data_ext = res_ext.json()
        assert data_ext["affected_kits_count"] == 6
        assert (
            data_ext["relabeling_tasks_generated"] == 2
        )  # 1 for CENTRAL_DEPOT, 1 for SITE_401

        # 2. Verify all kits have their expiration date updated
        res_kits = await client.get(
            f"/api/v1/execution/rtsm/kits?study_id={study_id}&lot_number={lot_number}",
            headers=headers_admin,
        )
        assert res_kits.status_code == 200
        for k in res_kits.json():
            assert "2027-06-30" in k["expiration_date"]

        # 3. Query generated Re-Labeling Tasks
        res_tasks = await client.get(
            f"/api/v1/execution/rtsm/relabeling-tasks?study_id={study_id}&lot_number={lot_number}",
            headers=headers_admin,
        )
        assert res_tasks.status_code == 200
        tasks = res_tasks.json()
        assert len(tasks) == 2
        site_task = next(t for t in tasks if t["node_id"] == "SITE_401")
        assert site_task["status"] == "PENDING_RELABEL"
        assert site_task["affected_kit_count"] == 3

        # 4. Complete Re-Labeling Task at Site 401
        task_id = site_task["id"]
        res_comp = await client.post(
            f"/api/v1/execution/rtsm/relabeling-tasks/{task_id}/complete",
            json={
                "notes": "All 3 kits relabeled with batch label token EXP-2027-06-30."
            },
            headers=headers_site,
        )
        assert res_comp.status_code == 200
        assert res_comp.json()["status"] == "COMPLETED"
        assert res_comp.json()["completed_by"] == "site_pharmacist_401"

        # 5. Dual QA Verification of Re-Labeling Task
        res_ver = await client.post(
            f"/api/v1/execution/rtsm/relabeling-tasks/{task_id}/verify",
            json={
                "label_barcode_hash": "SHA256:a1b2c3d4e5f67890abcdef1234567890",
                "notes": "Verified expiration date barcode and tamper-evident overlay.",
            },
            headers=headers_cra,
        )
        assert res_ver.status_code == 200
        assert res_ver.json()["status"] == "VERIFIED"
        assert res_ver.json()["verified_by"] == "cra_monitor"
        assert res_ver.json()["label_barcode_hash"] is not None
