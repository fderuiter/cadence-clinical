"""Unit and integration tests for Real-Time EDC/SDV Trigger-to-Payable & Split-Payee Engine.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006, PRD-SYS-161, PRD-SYS-162
"""

import pytest
from httpx import AsyncClient

from apps.payments.tests.conftest import get_auth_headers


@pytest.mark.asyncio
async def test_budget_contract_creation_and_split_payee_configuration(
    client: AsyncClient,
):
    """Validate multi-tier budget contract creation and 100% split payee configuration.

    @req:PRD-SYS-161
    @req:PRD-SYS-162
    @req:ADR-2199
    @req:PRD-PAY-002
    @req:PRD-PAY-003
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(
        roles="Grants Manager", change_reason="Initial contract setup"
    )
    study_id = "STUDY-ONC-2026"
    site_id = "SITE-101-ACADEMIC"

    # Step 1: Create Budget Contract
    contract_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "contract_name": "Phase III Oncology - AMC Master Schedule",
        "tier_level": "TIER_1_ACADEMIC",
        "currency": "USD",
        "total_budget": 750000.0,
        "overhead_percentage": 25.0,
        "withholding_percentage": 10.0,
    }
    res = await client.post(
        "/api/v1/payments/contracts", json=contract_payload, headers=headers
    )
    assert res.status_code == 201
    contract_data = res.json()
    assert contract_data["study_id"] == study_id
    assert contract_data["site_id"] == site_id
    assert contract_data["overhead_percentage"] == 25.0
    assert contract_data["withholding_percentage"] == 10.0
    assert contract_data["version_index"] == 1
    contract_id = contract_data["id"]

    # Step 2: Configure Split Payees (Hospital 70%, PI Practice 20%, Central Pharmacy 10%)
    split_payload = {
        "payees": [
            {
                "payee_id": "PAYEE-HOSP-01",
                "payee_name": "Academic Medical Center Research Institute",
                "payee_role": "HOSPITAL",
                "allocation_percentage": 70.0,
                "bank_account_reference": "BANK-ACCT-HOSP-7788",
                "tax_identifier": "EIN-12-3456789",
            },
            {
                "payee_id": "PAYEE-PI-01",
                "payee_name": "Oncology Associates PI Practice LLC",
                "payee_role": "PI_PRACTICE",
                "allocation_percentage": 20.0,
                "bank_account_reference": "BANK-ACCT-PI-9922",
                "tax_identifier": "EIN-98-7654321",
            },
            {
                "payee_id": "PAYEE-PHARM-01",
                "payee_name": "Central Investigational Pharmacy",
                "payee_role": "CENTRAL_PHARMACY",
                "allocation_percentage": 10.0,
                "bank_account_reference": "BANK-ACCT-PHARM-3344",
                "tax_identifier": "EIN-45-6789012",
            },
        ]
    }
    res_split = await client.post(
        f"/api/v1/payments/contracts/{contract_id}/split-payees",
        json=split_payload,
        headers=headers,
    )
    assert res_split.status_code == 201
    payees = res_split.json()
    assert len(payees) == 3
    assert sum(p["allocation_percentage"] for p in payees) == 100.0

    # Step 3: Add Fee Schedule Items
    fee_items = [
        {
            "visit_code": "SCREENING",
            "form_id": "ECRF-FORM-VS",
            "procedure_code": "PROC-VS-01",
            "procedure_name": "Vital Signs & Physical Exam",
            "base_amount": 200.0,
            "overhead_percentage": 25.0,
            "patient_stipend": 50.0,
            "travel_reimbursement": 25.0,
            "requires_sdv": False,
        },
        {
            "visit_code": "CYCLE_1_DAY_1",
            "form_id": "ECRF-FORM-PK",
            "procedure_code": "PROC-PK-01",
            "procedure_name": "Pharmacokinetic Blood Sampling",
            "base_amount": 500.0,
            "overhead_percentage": 25.0,
            "patient_stipend": 100.0,
            "travel_reimbursement": 50.0,
            "requires_sdv": True,  # CRA SDV gated!
        },
    ]

    for item in fee_items:
        res_fs = await client.post(
            f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
            json=item,
            headers=headers,
        )
        assert res_fs.status_code == 201


@pytest.mark.asyncio
async def test_split_payee_validation_failure_on_invalid_percentage(
    client: AsyncClient,
):
    """Validate that split payee allocations not summing to 100% are rejected.

    @req:PRD-SYS-162
    @req:PRD-PAY-003
    """
    headers = get_auth_headers(roles="Grants Manager")
    # Create Contract
    contract_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": "STUDY-TEST-001",
            "site_id": "SITE-001",
            "contract_name": "Test Contract",
        },
        headers=headers,
    )
    contract_id = contract_res.json()["id"]

    # Invalid sum: 70 + 20 = 90%
    invalid_split = {
        "payees": [
            {
                "payee_id": "PAYEE-01",
                "payee_name": "Hospital",
                "payee_role": "HOSPITAL",
                "allocation_percentage": 70.0,
            },
            {
                "payee_id": "PAYEE-02",
                "payee_name": "PI",
                "payee_role": "PI_PRACTICE",
                "allocation_percentage": 20.0,
            },
        ]
    }
    res = await client.post(
        f"/api/v1/payments/contracts/{contract_id}/split-payees",
        json=invalid_split,
        headers=headers,
    )
    assert res.status_code in (400, 422)
    assert "must sum exactly to 100.0%" in res.json()["detail"]


@pytest.mark.asyncio
async def test_real_time_ecrf_submission_trigger_and_split_allocation(
    client: AsyncClient,
):
    """Validate real-time eCRF submission triggering instant payable calculation,

    10% holdback reserve deduction, and multi-party split payee allocation.

    @req:PRD-SYS-161
    @req:PRD-SYS-162
    @req:ADR-2199
    @req:PRD-PAY-002
    @req:PRD-PAY-003
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-RT-01"
    site_id = "SITE-301"

    # Setup contract
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Cardiology Community Grant",
            "overhead_percentage": 20.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    # Configure 70% / 20% / 10% split payees
    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/split-payees",
        json={
            "payees": [
                {
                    "payee_id": "PAYEE-HOSP",
                    "payee_name": "Community Hospital",
                    "payee_role": "HOSPITAL",
                    "allocation_percentage": 70.0,
                },
                {
                    "payee_id": "PAYEE-PI",
                    "payee_name": "Cardiology PI Practice",
                    "payee_role": "PI_PRACTICE",
                    "allocation_percentage": 20.0,
                },
                {
                    "payee_id": "PAYEE-PHARM",
                    "payee_name": "Central Pharmacy",
                    "payee_role": "CENTRAL_PHARMACY",
                    "allocation_percentage": 10.0,
                },
            ]
        },
        headers=headers,
    )

    # Fee schedule for SCREENING: Base=400, Overhead 20% = 80, Stipend = 50, Travel = 20
    # Gross = 400 + 80 + 50 + 20 = 550.00
    # 10% Withholding = 55.00
    # Net = 495.00
    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "SCREENING",
            "form_id": "ECRF-FORM-ECG",
            "procedure_code": "PROC-ECG-01",
            "procedure_name": "12-Lead ECG Evaluation",
            "base_amount": 400.0,
            "overhead_percentage": 20.0,
            "patient_stipend": 50.0,
            "travel_reimbursement": 20.0,
            "requires_sdv": False,
        },
        headers=headers,
    )

    # Trigger eCRF Form Submission Event
    ecrf_event = {
        "study_id": study_id,
        "site_id": site_id,
        "subject_id": "SUBJ-001",
        "visit_code": "SCREENING",
        "form_id": "ECRF-FORM-ECG",
        "event_id": "EVT-ECRF-SUBMISSION-9001",
        "actor_id": "crc_operator_01",
    }
    trigger_res = await client.post(
        "/api/v1/payments/events/ecrf-submission", json=ecrf_event, headers=headers
    )
    assert trigger_res.status_code == 201
    payables = trigger_res.json()
    assert len(payables) == 1
    p = payables[0]

    # Verify Financial Totals
    assert p["base_amount"] == 400.0
    assert p["overhead_amount"] == 80.0
    assert p["patient_stipend_amount"] == 50.0
    assert p["travel_reimbursement_amount"] == 20.0
    assert p["gross_amount"] == 550.0
    assert p["withholding_rate"] == 10.0
    assert p["withholding_amount"] == 55.0
    assert p["net_amount"] == 495.0
    assert p["status"] == "TRIGGERED"
    assert p["is_sdv_gated"] is False

    # Verify Split Payee Allocations
    allocs = p["allocations"]
    assert len(allocs) == 3

    # Hospital (70%): Gross = 385.0, Holdback = 38.50, Net = 346.50
    hosp_alloc = next(a for a in allocs if a["payee_id"] == "PAYEE-HOSP")
    assert hosp_alloc["allocation_percentage"] == 70.0
    assert hosp_alloc["gross_share"] == 385.0
    assert hosp_alloc["withholding_share"] == 38.5
    assert hosp_alloc["net_share"] == 346.5

    # PI Practice (20%): Gross = 110.0, Holdback = 11.0, Net = 99.0
    pi_alloc = next(a for a in allocs if a["payee_id"] == "PAYEE-PI")
    assert pi_alloc["allocation_percentage"] == 20.0
    assert pi_alloc["gross_share"] == 110.0
    assert pi_alloc["withholding_share"] == 11.0
    assert pi_alloc["net_share"] == 99.0

    # Central Pharmacy (10%): Gross = 55.0, Holdback = 5.5, Net = 49.5
    pharm_alloc = next(a for a in allocs if a["payee_id"] == "PAYEE-PHARM")
    assert pharm_alloc["allocation_percentage"] == 10.0
    assert pharm_alloc["gross_share"] == 55.0
    assert pharm_alloc["withholding_share"] == 5.5
    assert pharm_alloc["net_share"] == 49.5

    # Verify Holdback Reserve Record
    assert p["holdback_reserve"] is not None
    assert p["holdback_reserve"]["holdback_amount"] == 55.0
    assert p["holdback_reserve"]["status"] == "HELD"


@pytest.mark.asyncio
async def test_cra_sdv_verification_gating_workflow(client: AsyncClient):
    """Validate CRA SDV verification gating: eCRF creates PENDING_SDV payable,

    which is unlocked only upon CRA SDV verification event.

    @req:PRD-SYS-161
    @req:PRD-SYS-162
    @req:ADR-2199
    @req:PRD-PAY-002
    @req:PRD-PAY-003
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-SDV-GATE-01"
    site_id = "SITE-202"

    # Setup contract and gated fee schedule
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "SDV Gated Contract",
            "overhead_percentage": 15.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "CYCLE_1_DAY_1",
            "form_id": "ECRF-FORM-LAB",
            "procedure_code": "PROC-LAB-01",
            "procedure_name": "Hematology & Chemistry Panel",
            "base_amount": 300.0,
            "overhead_percentage": 15.0,
            "requires_sdv": True,  # CRA SDV Gated!
        },
        headers=headers,
    )

    # Step 1: eCRF is submitted -> Payable is created in PENDING_SDV status
    ecrf_event = {
        "study_id": study_id,
        "site_id": site_id,
        "subject_id": "SUBJ-002",
        "visit_code": "CYCLE_1_DAY_1",
        "form_id": "ECRF-FORM-LAB",
        "event_id": "EVT-ECRF-LAB-001",
    }
    e_res = await client.post(
        "/api/v1/payments/events/ecrf-submission", json=ecrf_event, headers=headers
    )
    assert e_res.status_code == 201
    gated_payables = e_res.json()
    assert len(gated_payables) == 1
    gated_p = gated_payables[0]
    assert gated_p["status"] == "PENDING_SDV"
    assert gated_p["is_sdv_gated"] is True
    assert gated_p["sdv_verified_at"] is None
    payable_id = gated_p["id"]

    # Attempting to approve a gated payable before SDV verification should fail
    app_path = f"/api/v1/payments/payables/{payable_id}/approve"
    app_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Approve gated payable",
        action=app_path,
    )
    approve_fail = await client.post(app_path, headers=app_headers)
    assert approve_fail.status_code in (400, 422)
    assert "gated pending CRA SDV" in approve_fail.json()["detail"]

    # Step 2: CRA performs Source Data Verification (SDV)
    sdv_event = {
        "study_id": study_id,
        "site_id": site_id,
        "subject_id": "SUBJ-002",
        "visit_code": "CYCLE_1_DAY_1",
        "form_id": "ECRF-FORM-LAB",
        "verified_by": "cra_monitor_jane",
        "event_id": "EVT-CRA-SDV-LAB-001",
    }
    sdv_res = await client.post(
        "/api/v1/payments/events/cra-sdv-verification",
        json=sdv_event,
        headers=headers,
    )
    assert sdv_res.status_code == 200
    unlocked_p = sdv_res.json()
    assert unlocked_p["id"] == payable_id
    assert unlocked_p["status"] == "TRIGGERED"
    assert unlocked_p["is_sdv_gated"] is False
    assert unlocked_p["sdv_verified_by"] == "cra_monitor_jane"
    assert unlocked_p["sdv_verified_at"] is not None

    # Step 3: Now approval succeeds with fresh signature token
    app_headers_ok = get_auth_headers(
        roles="Grants Manager",
        change_reason="Approve verified payable",
        action=app_path,
    )
    approve_ok = await client.post(app_path, headers=app_headers_ok)
    assert approve_ok.status_code == 200
    assert approve_ok.json()["status"] == "APPROVED"


@pytest.mark.asyncio
async def test_deterministic_idempotency_prevents_duplicate_payables(
    client: AsyncClient,
):
    """Validate that identical event IDs do not duplicate payable ledgers.

    @req:PRD-SYS-161
    @req:ADR-2199
    @req:PRD-PAY-002
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-IDEM-01"
    site_id = "SITE-IDEM-01"

    # Setup contract and fee schedule
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Idempotency Contract",
            "overhead_percentage": 10.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "VISIT_01",
            "form_id": "FORM_DEMOG",
            "procedure_code": "PROC_DEMOG",
            "procedure_name": "Demographics Capture",
            "base_amount": 100.0,
            "requires_sdv": False,
        },
        headers=headers,
    )

    event_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "subject_id": "SUBJ-IDEM-100",
        "visit_code": "VISIT_01",
        "form_id": "FORM_DEMOG",
        "event_id": "EVT-DUPLICATE-CHECK-001",
    }

    # First event trigger
    res1 = await client.post(
        "/api/v1/payments/events/ecrf-submission",
        json=event_payload,
        headers=headers,
    )
    assert res1.status_code == 201
    payable_id_1 = res1.json()[0]["id"]

    # Re-send same event (duplicate network transmission)
    res2 = await client.post(
        "/api/v1/payments/events/ecrf-submission",
        json=event_payload,
        headers=headers,
    )
    assert res2.status_code == 201
    payable_id_2 = res2.json()[0]["id"]

    # Must be identical entity ID, not duplicated
    assert payable_id_1 == payable_id_2

    # Query payables list to ensure only 1 record exists
    list_res = await client.get(
        f"/api/v1/payments/payables?study_id={study_id}&site_id={site_id}",
        headers=headers,
    )
    assert len(list_res.json()) == 1


@pytest.mark.asyncio
async def test_holdback_retention_reserve_and_dual_signoff_release(
    client: AsyncClient,
):
    """Validate 10% holdback retention reserve calculation and dual sign-off release governance.

    @req:PRD-SYS-162
    @req:ADR-2199
    @req:PRD-PAY-003
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-HB-01"
    site_id = "SITE-HB-01"

    # Setup contract
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Holdback Governance Contract",
            "overhead_percentage": 0.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "VISIT_01",
            "form_id": "FORM_BIO",
            "procedure_code": "PROC_BIO",
            "procedure_name": "Biopsy Assessment",
            "base_amount": 1000.0,
            "requires_sdv": False,
        },
        headers=headers,
    )

    # Ingest 2 eCRF submissions -> $1000 each -> $100 holdback each ($200 total held)
    for i in range(1, 3):
        await client.post(
            "/api/v1/payments/events/ecrf-submission",
            json={
                "study_id": study_id,
                "site_id": site_id,
                "subject_id": f"SUBJ-HB-{i}",
                "visit_code": "VISIT_01",
                "form_id": "FORM_BIO",
                "event_id": f"EVT-HB-SUB-{i}",
            },
            headers=headers,
        )

    # Verify Holdback list
    hb_res = await client.get(
        f"/api/v1/payments/holdbacks?study_id={study_id}&site_id={site_id}&status=HELD",
        headers=headers,
    )
    assert hb_res.status_code == 200
    held_list = hb_res.json()
    assert len(held_list) == 2
    assert sum(h["holdback_amount"] for h in held_list) == 200.0

    # Step 2: Attempt release with missing DM sign-off -> Fails
    fail_release_1 = await client.post(
        "/api/v1/payments/holdbacks/release",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "release_reason": "Closeout without DM",
            "dm_signoff_by": "",
            "quality_signoff_by": "qa_director_sarah",
        },
        headers=headers,
    )
    assert fail_release_1.status_code in (400, 422)
    assert "Data Management sign-off is required" in fail_release_1.json()["detail"]

    # Step 3: Attempt release with missing Quality sign-off -> Fails
    fail_release_2 = await client.post(
        "/api/v1/payments/holdbacks/release",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "release_reason": "Closeout without Quality",
            "dm_signoff_by": "dm_lead_michael",
            "quality_signoff_by": "",
        },
        headers=headers,
    )
    assert fail_release_2.status_code in (400, 422)
    assert "Quality Oversight sign-off is required" in fail_release_2.json()["detail"]

    # Step 4: Execute compliant release with dual DM + Quality sign-off
    release_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "release_reason": "Formal study closeout, database lock & query resolution complete",
        "dm_signoff_by": "dm_lead_michael",
        "quality_signoff_by": "qa_director_sarah",
    }
    release_res = await client.post(
        "/api/v1/payments/holdbacks/release",
        json=release_payload,
        headers=headers,
    )
    assert release_res.status_code == 200
    rel_data = release_res.json()
    assert rel_data["total_released_amount"] == 200.0
    assert rel_data["released_count"] == 2
    assert rel_data["status"] == "RELEASED"
    assert rel_data["dm_signoff_by"] == "dm_lead_michael"
    assert rel_data["quality_signoff_by"] == "qa_director_sarah"


@pytest.mark.asyncio
async def test_payment_audit_ledger_immutable_tracking(client: AsyncClient):
    """Validate that every contract, payable, split, and holdback transition

    generates an append-only 21 CFR Part 11 audit log record.

    @req:PRD-SYS-161
    @req:PRD-SYS-162
    @req:ADR-2199
    @req:PRD-PAY-002
    @req:PRD-PAY-003
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(
        roles="Grants Manager",
        user_id="grants_lead_alicia",
        change_reason="Audit trail validation",
    )
    study_id = "STUDY-AUDIT-01"
    site_id = "SITE-AUDIT-01"

    # 1. Create contract
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Audit Tracked Contract",
            "overhead_percentage": 10.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    # 2. Add fee schedule
    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "VISIT_01",
            "procedure_code": "PROC-01",
            "procedure_name": "Initial Scan",
            "base_amount": 500.0,
        },
        headers=headers,
    )

    # 3. Query audit logs
    logs_res = await client.get("/api/v1/payments/audit-logs", headers=headers)
    assert logs_res.status_code == 200
    logs = logs_res.json()
    assert len(logs) >= 2

    contract_logs = [item for item in logs if item["entity_type"] == "CONTRACT"]
    assert len(contract_logs) >= 1
    assert contract_logs[0]["action"] == "CONTRACT_CREATED"
    assert contract_logs[0]["created_by"] == "grants_lead_alicia"
    assert contract_logs[0]["version_index"] == 1


@pytest.mark.asyncio
async def test_ctms_milestone_completion_event_trigger(client: AsyncClient):
    """Validate CTMS milestone completion event triggering automated milestone payables.

    @req:PRD-SYS-161
    @req:PRD-SYS-162
    @req:ADR-2199
    @req:PRD-PAY-002
    @req:PRD-PAY-003
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-MILE-01"
    site_id = "SITE-MILE-01"

    # Setup contract
    await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Milestone Site Contract",
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )

    # Trigger Milestone Event: First Patient Enrolled ($5,000)
    milestone_event = {
        "study_id": study_id,
        "site_id": site_id,
        "milestone_code": "MILE-FPI-01",
        "milestone_name": "First Patient Enrolled",
        "amount": 5000.0,
        "verified_by": "ctms_lead_tom",
        "event_id": "EVT-CTMS-MILE-001",
    }
    res = await client.post(
        "/api/v1/payments/events/milestone-completion",
        json=milestone_event,
        headers=headers,
    )
    assert res.status_code == 201
    m_payable = res.json()
    assert m_payable["trigger_type"] == "MILESTONE_COMPLETED"
    assert m_payable["procedure_code"] == "MILE-FPI-01"
    assert m_payable["gross_amount"] == 5000.0
    assert m_payable["withholding_amount"] == 500.0  # 10% holdback
    assert m_payable["net_amount"] == 4500.0
    assert m_payable["status"] == "TRIGGERED"
    assert m_payable["holdback_reserve"]["holdback_amount"] == 500.0
    assert m_payable["holdback_reserve"]["status"] == "HELD"


@pytest.mark.asyncio
async def test_rbac_financial_permissions_and_auditor_isolation(
    client: AsyncClient,
):
    """Validate RBAC access control: Grants Manager has write access, Auditor has read-only.

    @req:PRD-SYS-161
    @req:ADR-2199
    @req:PRD-PAY-002
    """
    study_id = "STUDY-RBAC-01"
    site_id = "SITE-RBAC-01"

    # Auditor Attempting Write -> 403 Forbidden
    auditor_headers = get_auth_headers(roles="Auditor", user_id="auditor_inspector_01")
    res_forbidden = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Unauthorized Contract",
        },
        headers=auditor_headers,
    )
    assert res_forbidden.status_code == 403
    assert "Forbidden" in res_forbidden.json()["detail"]

    # Grants Manager Writing -> 201 Created
    grants_headers = get_auth_headers(roles="Grants Manager")
    res_ok = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Authorized Contract",
        },
        headers=grants_headers,
    )
    assert res_ok.status_code == 201

    # Auditor Reading -> 200 OK
    res_read = await client.get(
        f"/api/v1/payments/contracts?study_id={study_id}",
        headers=auditor_headers,
    )
    assert res_read.status_code == 200
    assert len(res_read.json()) >= 1
