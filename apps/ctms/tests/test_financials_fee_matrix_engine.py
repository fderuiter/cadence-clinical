import os
import time

import pytest_asyncio
from fastapi.testclient import TestClient
from jose import jwt
from sqlalchemy import event

from apps.ctms.adapters.database import db_manager
from apps.ctms.adapters.models import Base
from apps.ctms.main import app
from packages.testing.security import generate_signature

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", default="internal-gateway-secret-12345"
).encode("utf-8")


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup in-memory CTMS database with attached audit schema."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)

    @event.listens_for(db_manager.engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_connection, connection_record):
        cursor = dbapi_connection.cursor()
        cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        cursor.close()

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "Grants Manager",
    change_reason: str = "Financial operations",
    action: str | None = None,
    user_id: str = "grants_lead_01",
) -> dict:
    timestamp = str(time.time())
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    if action:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": action,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        headers["X-Sig-Token"] = jwt.encode(
            sig_payload, "internal-gateway-secret-12345", algorithm="HS256"
        )
    return headers


def test_multi_tier_budget_templates():
    """Validate multi-tier budget template creation, schema properties, and retrieval.

    @req:ADR-2200, PRD-PAY-001, PRD-PAY-005
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Grants Manager", change_reason="Create Tier 1 Academic template"
    )

    template_payload = {
        "template_name": "Oncology Phase III - Academic Medical Center",
        "study_id": "STUDY-ONC-2026",
        "tier_level": "TIER_1_ACADEMIC",
        "currency": "USD",
        "overhead_percentage": 28.5,
        "withholding_percentage": 10.0,
        "fixed_startup_fee": 15000.0,
        "fixed_closeout_fee": 5000.0,
        "irb_fee": 3500.0,
        "patient_stipend_amount": 75.0,
        "travel_reimbursement_rate": 50.0,
        "fixed_fees": [
            {"milestone": "Site Initiation", "amount": 7500.0},
            {"milestone": "First Subject Screened", "amount": 2500.0},
        ],
        "procedure_items": [
            {
                "visit_name": "SCREENING",
                "activity_id": "ACT-ECG-01",
                "form_id": "FORM-ECG-V1",
                "procedure_code": "CPT-93000",
                "procedure_name": "Electrocardiogram 12-lead",
                "base_amount": 250.0,
                "overhead_percentage": 28.5,
                "withholding_percentage": 10.0,
                "patient_stipend": 75.0,
                "travel_reimbursement": 50.0,
            },
            {
                "visit_name": "SCREENING",
                "activity_id": "ACT-LAB-01",
                "form_id": "FORM-LAB-V1",
                "procedure_code": "CPT-80053",
                "procedure_name": "Comprehensive Metabolic Panel",
                "base_amount": 180.0,
                "overhead_percentage": 28.5,
                "withholding_percentage": 10.0,
                "patient_stipend": 0.0,
                "travel_reimbursement": 0.0,
            },
            {
                "visit_name": "WEEK_4",
                "activity_id": "ACT-PHYS-01",
                "form_id": "FORM-PE-V1",
                "procedure_code": "CPT-99214",
                "procedure_name": "Detailed Physical Examination",
                "base_amount": 320.0,
                "overhead_percentage": 28.5,
                "withholding_percentage": 10.0,
                "patient_stipend": 75.0,
                "travel_reimbursement": 50.0,
            },
        ],
    }

    res = client.post(
        "/api/v1/ctms/financials/templates", json=template_payload, headers=headers
    )
    assert res.status_code == 201
    created_tmpl = res.json()
    assert (
        created_tmpl["template_name"] == "Oncology Phase III - Academic Medical Center"
    )
    assert created_tmpl["tier_level"] == "TIER_1_ACADEMIC"
    assert created_tmpl["overhead_percentage"] == 28.5
    assert created_tmpl["fixed_startup_fee"] == 15000.0
    assert len(created_tmpl["procedure_items"]) == 3
    assert created_tmpl["version_index"] == 1
    assert created_tmpl["created_by"] == "grants_lead_01"

    # List templates
    res_list = client.get("/api/v1/ctms/financials/templates", headers=headers)
    assert res_list.status_code == 200
    assert len(res_list.json()) >= 1

    # Get single template
    res_get = client.get(
        f"/api/v1/ctms/financials/templates/{created_tmpl['id']}", headers=headers
    )
    assert res_get.status_code == 200
    assert res_get.json()["id"] == created_tmpl["id"]


def test_template_application_and_fee_matrix_grid():
    """Validate applying budget template to investigator grant and procedure linkages.

    @req:ADR-2200, PRD-PAY-001, PRD-PAY-005
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Grants Manager", change_reason="Apply budget template"
    )

    # Step 1: Create Budget Template
    template_payload = {
        "template_name": "Cardiology Community Clinic Tier 2",
        "study_id": "STUDY-CARD-101",
        "tier_level": "TIER_2_COMMUNITY",
        "currency": "USD",
        "overhead_percentage": 18.0,
        "withholding_percentage": 10.0,
        "fixed_startup_fee": 8000.0,
        "fixed_closeout_fee": 3000.0,
        "irb_fee": 2000.0,
        "patient_stipend_amount": 50.0,
        "travel_reimbursement_rate": 25.0,
        "fixed_fees": [],
        "procedure_items": [
            {
                "visit_name": "SCREENING",
                "activity_id": "USDM-ACT-001",
                "form_id": "ECRF-FORM-VS",
                "procedure_code": "PROC-VS-01",
                "procedure_name": "Vital Signs & Anthropometry",
                "base_amount": 100.0,
                "overhead_percentage": 18.0,
                "withholding_percentage": 10.0,
                "patient_stipend": 50.0,
                "travel_reimbursement": 25.0,
            },
            {
                "visit_name": "SCREENING",
                "activity_id": "USDM-ACT-002",
                "form_id": "ECRF-FORM-ECG",
                "procedure_code": "PROC-ECG-01",
                "procedure_name": "12-Lead ECG",
                "base_amount": 200.0,
                "overhead_percentage": 18.0,
                "withholding_percentage": 10.0,
                "patient_stipend": 0.0,
                "travel_reimbursement": 0.0,
            },
        ],
    }
    tmpl_res = client.post(
        "/api/v1/ctms/financials/templates", json=template_payload, headers=headers
    )
    template_id = tmpl_res.json()["id"]

    # Step 2: Create Investigator Grant
    grant_payload = {
        "study_id": "STUDY-CARD-101",
        "site_id": "SITE-COMM-404",
        "total_budget": 250000.0,
        "currency": "USD",
    }
    grant_res = client.post("/api/v1/ctms/grants", json=grant_payload, headers=headers)
    grant_id = grant_res.json()["id"]

    # Step 3: Apply Template to Grant
    apply_res = client.post(
        f"/api/v1/ctms/financials/grants/{grant_id}/apply-template",
        json={"template_id": template_id},
        headers=headers,
    )
    assert apply_res.status_code == 201
    applied_grids = apply_res.json()
    assert len(applied_grids) == 2
    assert applied_grids[0]["activity_id"] == "USDM-ACT-001"
    assert applied_grids[0]["form_id"] == "ECRF-FORM-VS"
    assert applied_grids[0]["overhead_percentage"] == 18.0

    # Step 4: Add direct Procedure Payment Grid item with specific eCRF linkage
    custom_procedure = {
        "grant_id": grant_id,
        "visit_name": "WEEK_4",
        "procedure_code": "PROC-ECHO-01",
        "procedure_name": "Echocardiogram 2D Doppler",
        "base_amount": 600.0,
        "overhead_percentage": 18.0,
        "withholding_percentage": 10.0,
        "activity_id": "USDM-ACT-ECHO",
        "form_id": "ECRF-FORM-ECHO",
        "patient_stipend": 50.0,
        "travel_reimbursement": 25.0,
    }
    custom_res = client.post(
        "/api/v1/ctms/financials/procedure-grids",
        json=custom_procedure,
        headers=headers,
    )
    assert custom_res.status_code == 201
    assert custom_res.json()["activity_id"] == "USDM-ACT-ECHO"
    assert custom_res.json()["form_id"] == "ECRF-FORM-ECHO"

    # Step 5: List procedure grids
    list_res = client.get(
        f"/api/v1/ctms/financials/procedure-grids?grant_id={grant_id}", headers=headers
    )
    assert list_res.status_code == 200
    assert len(list_res.json()) == 3


def test_visit_payable_calculation_with_overhead_and_stipends():
    """Validate visit payable calculation with base fees, institutional overhead, patient stipends, and withholdings.

    @req:ADR-2200, PRD-PAY-001, PRD-PAY-005
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Grants Manager", change_reason="Calculate visit payables"
    )

    grant_payload = {
        "study_id": "STUDY-CALC-001",
        "site_id": "SITE-CALC-001",
        "total_budget": 100000.0,
        "currency": "USD",
    }
    grant_res = client.post("/api/v1/ctms/grants", json=grant_payload, headers=headers)
    grant_id = grant_res.json()["id"]

    # Add 2 procedures for SCREENING visit:
    # Proc 1: Base = $300, Overhead = 25% ($75), Stipend = $50, Travel = $25, Withholding = 10% on ($300+$75)=$37.50
    # Proc 2: Base = $200, Overhead = 25% ($50), Stipend = $0, Travel = $0, Withholding = 10% on ($200+$50)=$25.00
    # Expected Base = $500.00
    # Expected Overhead = $125.00
    # Expected Stipend = $50.00
    # Expected Travel = $25.00
    # Expected Gross = $500 + $125 + $50 + $25 = $700.00
    # Expected Withholding = $37.50 + $25.00 = $62.50
    # Expected Net = $700.00 - $62.50 = $637.50

    p1 = {
        "grant_id": grant_id,
        "visit_name": "SCREENING",
        "procedure_code": "PROC-P1",
        "procedure_name": "Procedure One",
        "base_amount": 300.0,
        "overhead_percentage": 25.0,
        "withholding_percentage": 10.0,
        "patient_stipend": 50.0,
        "travel_reimbursement": 25.0,
    }
    p2 = {
        "grant_id": grant_id,
        "visit_name": "SCREENING",
        "procedure_code": "PROC-P2",
        "procedure_name": "Procedure Two",
        "base_amount": 200.0,
        "overhead_percentage": 25.0,
        "withholding_percentage": 10.0,
        "patient_stipend": 0.0,
        "travel_reimbursement": 0.0,
    }
    client.post("/api/v1/ctms/financials/procedure-grids", json=p1, headers=headers)
    client.post("/api/v1/ctms/financials/procedure-grids", json=p2, headers=headers)

    calc_res = client.get(
        f"/api/v1/ctms/financials/grants/{grant_id}/calculate-visit?visit_name=SCREENING",
        headers=headers,
    )
    assert calc_res.status_code == 200
    calc = calc_res.json()
    assert calc["base_amount"] == 500.0
    assert calc["overhead_amount"] == 125.0
    assert calc["patient_stipend_total"] == 50.0
    assert calc["travel_reimbursement_total"] == 25.0
    assert calc["gross_amount"] == 700.0
    assert calc["withholding_amount"] == 62.5
    assert calc["net_amount"] == 637.5


def test_contract_amendments_and_historical_fee_schedule_preservation():
    """Validate contract amendments incrementing version_index and preserving historical fee schedules.

    @req:ADR-2200, PRD-PAY-001, PRD-PAY-005
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Grants Manager", change_reason="Initial grant setup"
    )

    # Step 1: Initial Grant
    grant_payload = {
        "study_id": "STUDY-AMEND-999",
        "site_id": "SITE-AMEND-101",
        "total_budget": 500000.0,
        "currency": "USD",
    }
    grant_res = client.post("/api/v1/ctms/grants", json=grant_payload, headers=headers)
    grant_id = grant_res.json()["id"]

    # Initial procedure under version_index = 1
    p_v1 = {
        "grant_id": grant_id,
        "visit_name": "WEEK_12",
        "procedure_code": "PROC-MRI-01",
        "procedure_name": "Brain MRI",
        "base_amount": 1000.0,
        "overhead_percentage": 20.0,
        "withholding_percentage": 10.0,
    }
    client.post("/api/v1/ctms/financials/procedure-grids", json=p_v1, headers=headers)

    # Calculate payable under version 1
    calc_v1 = client.get(
        f"/api/v1/ctms/financials/grants/{grant_id}/calculate-visit?visit_name=WEEK_12&version_index=1",
        headers=headers,
    ).json()
    assert calc_v1["base_amount"] == 1000.0
    assert calc_v1["net_amount"] == 1080.0  # ($1000 + $200 overhead) - $120 withholding

    # Step 2: Execute Contract Amendment #1 (Increases total budget to $600,000, updates MRI fee to $1200)
    amendment_payload = {
        "summary_of_changes": "Protocol Amendment 2: Expanded MRI sequence requirements and adjusted inflation compensation.",
        "revised_budget": 600000.0,
        "signature_manifestation": {
            "signer_name": "Dr. Sarah Jenkins",
            "signer_role": "Principal Investigator",
            "signed_at": "2026-08-22T20:00:00Z",
            "meaning": "Contract Amendment Execution",
        },
        "updated_procedures": [
            {
                "visit_name": "WEEK_12",
                "activity_id": "ACT-MRI-NEW",
                "form_id": "FORM-MRI-NEW",
                "procedure_code": "PROC-MRI-01",
                "procedure_name": "Brain MRI with Contrast",
                "base_amount": 1200.0,
                "overhead_percentage": 20.0,
                "withholding_percentage": 10.0,
                "patient_stipend": 100.0,
                "travel_reimbursement": 50.0,
            }
        ],
    }
    amend_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Protocol amendment rate adjustment",
        action="CONTRACT_AMENDMENT_EXECUTION",
    )
    amend_res = client.post(
        f"/api/v1/ctms/financials/grants/{grant_id}/amend",
        json=amendment_payload,
        headers=amend_headers,
    )
    assert amend_res.status_code == 201
    amendment = amend_res.json()
    assert amendment["amendment_number"] == 1
    assert amendment["previous_budget"] == 500000.0
    assert amendment["revised_budget"] == 600000.0
    assert amendment["previous_version_index"] == 1
    assert amendment["new_version_index"] == 2
    assert amendment["signature_manifestation"]["signer_name"] == "Dr. Sarah Jenkins"

    # Step 3: Verify Historical vs Amended Procedure Grids
    grids_v1 = client.get(
        f"/api/v1/ctms/financials/procedure-grids?grant_id={grant_id}&version_index=1",
        headers=headers,
    ).json()
    assert len(grids_v1) == 1
    assert grids_v1[0]["base_amount"] == 1000.0

    grids_v2 = client.get(
        f"/api/v1/ctms/financials/procedure-grids?grant_id={grant_id}&version_index=2",
        headers=headers,
    ).json()
    assert len(grids_v2) == 1
    assert grids_v2[0]["base_amount"] == 1200.0
    assert grids_v2[0]["patient_stipend"] == 100.0

    # Step 4: Calculate visit payables for amended version
    calc_v2 = client.get(
        f"/api/v1/ctms/financials/grants/{grant_id}/calculate-visit?visit_name=WEEK_12&version_index=2",
        headers=headers,
    ).json()
    assert calc_v2["base_amount"] == 1200.0
    assert calc_v2["patient_stipend_total"] == 100.0
    assert calc_v2["travel_reimbursement_total"] == 50.0

    # Step 5: List amendments
    list_amends = client.get(
        f"/api/v1/ctms/financials/grants/{grant_id}/amendments",
        headers=headers,
    ).json()
    assert len(list_amends) == 1


def test_multi_currency_exchange_rates_and_conversion():
    """Validate foreign exchange rate table setup, currency conversion, and visit payable conversion.

    @req:ADR-2200, PRD-PAY-001, PRD-PAY-005
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="Grants Manager", change_reason="Set FX Rates")

    # Step 1: Configure FX rate: 1 EUR = 1.08 USD
    fx_payload = {
        "base_currency": "EUR",
        "target_currency": "USD",
        "rate": 1.08,
    }
    fx_res = client.post(
        "/api/v1/ctms/financials/exchange-rates", json=fx_payload, headers=headers
    )
    assert fx_res.status_code == 201
    assert fx_res.json()["base_currency"] == "EUR"
    assert fx_res.json()["rate"] == 1.08

    # Step 2: Convert currency directly
    conv_payload = {
        "amount": 1000.0,
        "from_currency": "EUR",
        "to_currency": "USD",
    }
    conv_res = client.post(
        "/api/v1/ctms/financials/convert", json=conv_payload, headers=headers
    )
    assert conv_res.status_code == 200
    assert conv_res.json()["converted_amount"] == 1080.0

    # Step 3: Calculate visit payable for European Site (EUR) with conversion to USD (Sponsor base)
    grant_payload = {
        "study_id": "STUDY-GLOBAL-EUR",
        "site_id": "SITE-PARIS-01",
        "total_budget": 400000.0,
        "currency": "EUR",
    }
    grant_res = client.post("/api/v1/ctms/grants", json=grant_payload, headers=headers)
    grant_id = grant_res.json()["id"]

    grid_item = {
        "grant_id": grant_id,
        "visit_name": "VISIT_1",
        "procedure_code": "PROC-BIO-01",
        "procedure_name": "Biopsy Sampling",
        "base_amount": 1000.0,
        "overhead_percentage": 0.0,
        "withholding_percentage": 0.0,
    }
    client.post(
        "/api/v1/ctms/financials/procedure-grids", json=grid_item, headers=headers
    )

    calc_res = client.get(
        f"/api/v1/ctms/financials/grants/{grant_id}/calculate-visit?visit_name=VISIT_1&currency=EUR&target_currency=USD",
        headers=headers,
    )
    assert calc_res.status_code == 200
    res_data = calc_res.json()
    assert res_data["currency"] == "EUR"
    assert res_data["target_currency"] == "USD"
    assert res_data["net_amount"] == 1000.0
    assert res_data["exchange_rate"] == 1.08
    assert res_data["converted_net_amount"] == 1080.0


def test_budget_adjustments_passthrough_and_penalties():
    """Validate passthrough expense reimbursements, deviation penalties, and approval workflow.

    @req:ADR-2200, PRD-PAY-001, PRD-PAY-005
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Grants Manager", change_reason="Manage budget adjustments"
    )

    study_id = "STUDY-ADJ-101"
    site_id = "SITE-ADJ-501"

    # Create investigator grant first
    grant_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "total_budget": 150000.0,
        "currency": "USD",
    }
    grant_res = client.post("/api/v1/ctms/grants", json=grant_payload, headers=headers)
    grant_id = grant_res.json()["id"]

    # Step 1: Create Passthrough Expense Reimbursement (Positive adjustment)
    passthrough = {
        "grant_id": grant_id,
        "site_id": site_id,
        "study_id": study_id,
        "adjustment_type": "PASSTHROUGH_REIMBURSEMENT",
        "description": "Special dry-ice courier shipment for PK/PD samples",
        "amount": 450.0,
        "currency": "USD",
    }
    pt_res = client.post(
        "/api/v1/ctms/financials/adjustments", json=passthrough, headers=headers
    )
    assert pt_res.status_code == 201
    pt_data = pt_res.json()
    assert pt_data["status"] == "PENDING"
    assert pt_data["amount"] == 450.0

    # Step 2: Create Protocol Deviation Penalty (Negative adjustment)
    penalty = {
        "grant_id": grant_id,
        "site_id": site_id,
        "study_id": study_id,
        "adjustment_type": "PROTOCOL_DEVIATION_PENALTY",
        "description": "Unscheduled out-of-window lab draw without sponsor pre-authorization",
        "amount": -250.0,
        "currency": "USD",
    }
    pen_res = client.post(
        "/api/v1/ctms/financials/adjustments", json=penalty, headers=headers
    )
    assert pen_res.status_code == 201
    pen_data = pen_res.json()
    assert pen_data["amount"] == -250.0

    # Step 3: Approve adjustment
    app_path = f"/api/v1/ctms/financials/adjustments/{pt_data['id']}/approve"
    app_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Approve adjustment",
        action=app_path,
    )
    app_res = client.post(
        app_path,
        headers=app_headers,
    )
    assert app_res.status_code == 200
    assert app_res.json()["status"] == "APPROVED"
    assert app_res.json()["approved_by"] == "grants_lead_01"

    # Step 4: List adjustments for study
    adj_list = client.get(
        f"/api/v1/ctms/financials/adjustments?study_id={study_id}",
        headers=headers,
    ).json()
    assert len(adj_list) == 2


def test_rbac_access_control():
    """Validate role-based access control denies unauthorized write operations.

    @req:ADR-2200, PRD-PAY-001, PRD-PAY-005
    """
    client = TestClient(app)
    unauthorized_headers = get_auth_headers(
        roles="Site CRC", change_reason="Attempt unauthorized write"
    )

    # Attempt to create template as Site CRC
    res = client.post(
        "/api/v1/ctms/financials/templates",
        json={
            "template_name": "Unauthorized",
            "tier_level": "TIER_1_ACADEMIC",
            "currency": "USD",
        },
        headers=unauthorized_headers,
    )
    assert res.status_code == 403

    # Attempt to execute contract amendment as Site CRC
    res_amend = client.post(
        "/api/v1/ctms/financials/grants/G1/amend",
        json={"summary_of_changes": "Unauthorized", "revised_budget": 999999.0},
        headers=unauthorized_headers,
    )
    assert res_amend.status_code == 403

    # Attempt to create adjustment as Site CRC
    res_adj = client.post(
        "/api/v1/ctms/financials/adjustments",
        json={
            "grant_id": "G1",
            "site_id": "S1",
            "study_id": "ST1",
            "adjustment_type": "SITE_BONUS",
            "description": "Unauthorized bonus",
            "amount": 1000.0,
        },
        headers=unauthorized_headers,
    )
    assert res_adj.status_code == 403
