"""Comprehensive unit and integration tests for Participant Financial Wallet and EU CTR Lay Summary Portal.

Requirements:
- ADR-2199: Participant Financial Wallet & EU CTR Lay Summary Architecture
- PRD-SYS-127: Participant Financial Wallet & Plain-Language Trial Results Portal
- PRD-PAT-001: SoA Protocol-Linked Participant Stipends & Digital Wallet
- PRD-PAT-002: Mobile Travel Expense OCR Extraction & Instant Disbursement
- PRD-PAT-003: EU CTR Regulation No 536/2014 Article 37 Lay Summary Portal
"""

import pytest
from httpx import AsyncClient

from apps.payments.tests.conftest import get_auth_headers


@pytest.mark.asyncio
async def test_participant_wallet_provisioning_and_payout_methods(client: AsyncClient):
    """Test participant financial wallet provisioning, query, and updating payout preferences.

    @req:PRD-SYS-127
    @req:PRD-PAT-001
    @req:ADR-2199
    """
    headers = get_auth_headers(roles="Subject", user_id="subject_001")
    study_id = "STUDY-WALLET-01"
    site_id = "SITE-101"
    subject_id = "SUBJ-9001"

    # 1. Provision wallet
    res = await client.post(
        "/api/v1/payments/participant/wallets",
        json={
            "subject_id": subject_id,
            "study_id": study_id,
            "site_id": site_id,
            "currency": "USD",
            "virtual_card_mask": "****-****-****-1234",
            "ach_account_mask": "ACH-***5678",
            "ach_routing_number": "021000021",
            "ach_account_holder": "Participant SUBJ-9001",
        },
        headers=headers,
    )
    assert res.status_code == 201
    wallet = res.json()
    assert wallet["subject_id"] == subject_id
    assert wallet["study_id"] == study_id
    assert wallet["current_balance"] == 0.0
    assert wallet["virtual_card_mask"] == "****-****-****-1234"
    wallet_id = wallet["id"]

    # 2. Get wallet by ID
    res_get = await client.get(
        f"/api/v1/payments/participant/wallets/{wallet_id}",
        headers=headers,
    )
    assert res_get.status_code == 200
    assert res_get.json()["id"] == wallet_id

    # 3. Get wallet by subject & study
    res_subj = await client.get(
        f"/api/v1/payments/participant/wallets/subject/{study_id}/{subject_id}",
        headers=headers,
    )
    assert res_subj.status_code == 200
    assert res_subj.json()["id"] == wallet_id

    # 4. Update payout methods
    res_put = await client.put(
        f"/api/v1/payments/participant/wallets/{wallet_id}/payout-methods",
        json={
            "virtual_card_mask": "****-****-****-9999",
            "virtual_card_expiry": "08/30",
            "ach_account_mask": "ACH-***9999",
        },
        headers=headers,
    )
    assert res_put.status_code == 200
    updated_wallet = res_put.json()
    assert updated_wallet["virtual_card_mask"] == "****-****-****-9999"
    assert updated_wallet["virtual_card_expiry"] == "08/30"
    assert updated_wallet["ach_account_mask"] == "ACH-***9999"


@pytest.mark.asyncio
async def test_soa_stipend_rule_and_visit_verification_trigger(client: AsyncClient):
    """Test SoA protocol stipend rule setup and automatic wallet crediting upon verified visit.

    @req:PRD-SYS-127
    @req:PRD-PAT-001
    @req:ADR-2199
    """
    admin_headers = get_auth_headers(roles="Grants Manager", user_id="grants_lead_01")
    study_id = "STUDY-SOA-01"
    site_id = "SITE-201"
    subject_id = "SUBJ-9002"

    # 1. Create SoA stipend rule for Screening Visit
    r_res = await client.post(
        "/api/v1/payments/participant/stipend-rules",
        json={
            "study_id": study_id,
            "visit_code": "V1_SCREENING",
            "visit_name": "Screening and Consent Visit",
            "procedure_code": "PROC-SCR-01",
            "amount": 85.0,
            "currency": "USD",
            "requires_verification": True,
            "taxable": False,
        },
        headers=admin_headers,
    )
    assert r_res.status_code == 201
    rule = r_res.json()
    assert rule["visit_code"] == "V1_SCREENING"
    assert rule["amount"] == 85.0

    # 2. List stipend rules
    list_res = await client.get(
        f"/api/v1/payments/participant/stipend-rules?study_id={study_id}",
        headers=admin_headers,
    )
    assert list_res.status_code == 200
    assert len(list_res.json()) >= 1

    # 3. Simulate EDC Verified Clinic Visit Completion Event
    event_id = "EDC-EVT-SCR-9002"
    v_res = await client.post(
        "/api/v1/payments/participant/events/visit-verified",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": subject_id,
            "visit_code": "V1_SCREENING",
            "event_id": event_id,
            "verified_by": "crc_alice",
        },
        headers=admin_headers,
    )
    assert v_res.status_code == 200
    evt_data = v_res.json()
    assert evt_data["status"] == "CREDITED"
    assert evt_data["amount"] == 85.0
    assert evt_data["current_balance"] == 85.0
    wallet_id = evt_data["wallet_id"]

    # 4. Check idempotency: trigger same event again
    v_dup_res = await client.post(
        "/api/v1/payments/participant/events/visit-verified",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": subject_id,
            "visit_code": "V1_SCREENING",
            "event_id": event_id,
            "verified_by": "crc_alice",
        },
        headers=admin_headers,
    )
    assert v_dup_res.status_code == 200
    dup_data = v_dup_res.json()
    assert dup_data["status"] == "ALREADY_PROCESSED"
    assert dup_data["current_balance"] == 85.0

    # 5. Verify ledger transactions
    tx_res = await client.get(
        f"/api/v1/payments/participant/wallets/{wallet_id}/transactions",
        headers=admin_headers,
    )
    assert tx_res.status_code == 200
    txs = tx_res.json()
    assert len(txs) == 1
    assert txs[0]["transaction_type"] == "STIPEND_CREDIT"
    assert txs[0]["amount"] == 85.0
    assert txs[0]["balance_after"] == 85.0


@pytest.mark.asyncio
async def test_receipt_ocr_and_travel_expense_approval_workflow(client: AsyncClient):
    """Test receipt OCR extraction, claim submission, 1-click coordinator approval, and rejection.

    @req:PRD-SYS-127
    @req:PRD-PAT-002
    @req:ADR-2199
    """
    subject_headers = get_auth_headers(roles="Subject", user_id="subject_003")
    crc_headers = get_auth_headers(roles="CRC", user_id="crc_coordinator_bob")
    study_id = "STUDY-OCR-01"
    site_id = "SITE-301"
    subject_id = "SUBJ-9003"

    # 1. OCR Extraction Test
    raw_receipt_text = (
        "METRO TRANSIT AUTHORITY\n"
        "Date: 2026-08-23\n"
        "Fare Paid: $24.50\n"
        "Express Bus to Clinical Center\n"
    )
    ocr_res = await client.post(
        "/api/v1/payments/participant/expenses/ocr-extract",
        json={"raw_text": raw_receipt_text, "category_hint": "TRANSIT"},
        headers=subject_headers,
    )
    assert ocr_res.status_code == 200
    ocr_data = ocr_res.json()
    assert ocr_data["gross_amount"] == 24.50
    assert ocr_data["category"] == "TRANSIT"
    assert ocr_data["merchant_name"] == "Metro Transit Authority"
    assert ocr_data["net_amount"] == 24.50

    # 2. Submit Claim
    claim_res = await client.post(
        "/api/v1/payments/participant/expenses",
        json={
            "subject_id": subject_id,
            "study_id": study_id,
            "site_id": site_id,
            "expense_date": "2026-08-23",
            "category": ocr_data["category"],
            "merchant_name": ocr_data["merchant_name"],
            "gross_amount": ocr_data["gross_amount"],
            "tax_deduction": ocr_data["tax_deduction"],
            "net_amount": ocr_data["net_amount"],
            "receipt_image_name": "transit_ticket.jpg",
            "receipt_ocr_data": ocr_data,
        },
        headers=subject_headers,
    )
    assert claim_res.status_code == 201
    claim = claim_res.json()
    claim_id = claim["id"]
    wallet_id = claim["wallet_id"]
    assert claim["status"] == "PENDING_REVIEW"

    # 3. List pending claims
    list_claims_res = await client.get(
        f"/api/v1/payments/participant/expenses?study_id={study_id}&status=PENDING_REVIEW",
        headers=crc_headers,
    )
    assert list_claims_res.status_code == 200
    claims = list_claims_res.json()
    assert any(c["id"] == claim_id for c in claims)

    # 4. 1-Click Coordinator Approval
    review_res = await client.post(
        f"/api/v1/payments/participant/expenses/{claim_id}/review",
        json={
            "status": "APPROVED",
            "review_notes": "Verified public transit ticket for clinic visit.",
        },
        headers=crc_headers,
    )
    assert review_res.status_code == 200
    approved_claim = review_res.json()
    assert approved_claim["status"] == "APPROVED"
    assert approved_claim["reviewed_by"] == "crc_coordinator_bob"

    # 5. Verify Wallet balance credited with travel reimbursement
    w_res = await client.get(
        f"/api/v1/payments/participant/wallets/{wallet_id}",
        headers=subject_headers,
    )
    assert w_res.status_code == 200
    assert w_res.json()["current_balance"] == 24.50
    assert w_res.json()["lifetime_reimbursements"] == 24.50

    # 6. Test Rejection flow on a second claim
    claim2_res = await client.post(
        "/api/v1/payments/participant/expenses",
        json={
            "subject_id": subject_id,
            "study_id": study_id,
            "site_id": site_id,
            "expense_date": "2026-08-23",
            "category": "MEAL",
            "merchant_name": "Luxury Steakhouse",
            "gross_amount": 250.00,
        },
        headers=subject_headers,
    )
    claim2_id = claim2_res.json()["id"]

    reject_res = await client.post(
        f"/api/v1/payments/participant/expenses/{claim2_id}/review",
        json={
            "status": "REJECTED",
            "review_notes": "Exceeds per diem meal allowance threshold.",
        },
        headers=crc_headers,
    )
    assert reject_res.status_code == 200
    assert reject_res.json()["status"] == "REJECTED"


@pytest.mark.asyncio
async def test_automated_digital_disbursements(client: AsyncClient):
    """Test Virtual Card and Direct ACH digital payout dispatch and ledger transactions.

    @req:PRD-SYS-127
    @req:PRD-PAT-001
    @req:PRD-PAT-002
    @req:ADR-2199
    """
    subject_headers = get_auth_headers(roles="Subject", user_id="subject_004")
    admin_headers = get_auth_headers(roles="Grants Manager", user_id="grants_lead_01")
    study_id = "STUDY-DISB-01"
    site_id = "SITE-401"
    subject_id = "SUBJ-9004"

    # Setup rule and credit stipend to fund wallet
    await client.post(
        "/api/v1/payments/participant/stipend-rules",
        json={
            "study_id": study_id,
            "visit_code": "V_TREATMENT",
            "visit_name": "Treatment Visit",
            "amount": 200.0,
        },
        headers=admin_headers,
    )
    v_res = await client.post(
        "/api/v1/payments/participant/events/visit-verified",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": subject_id,
            "visit_code": "V_TREATMENT",
            "event_id": "EVT-DISB-01",
        },
        headers=admin_headers,
    )
    wallet_id = v_res.json()["wallet_id"]

    # 1. Disburse partial amount ($100) via Virtual Card
    disb1_res = await client.post(
        f"/api/v1/payments/participant/wallets/{wallet_id}/disburse",
        json={"disbursement_type": "VIRTUAL_CARD", "amount": 100.0},
        headers=subject_headers,
    )
    assert disb1_res.status_code == 201
    disb1 = disb1_res.json()
    assert disb1["disbursement_type"] == "VIRTUAL_CARD"
    assert disb1["amount"] == 100.0
    assert disb1["status"] == "DISPATCHED"

    # Check remaining balance ($100)
    w_res1 = await client.get(
        f"/api/v1/payments/participant/wallets/{wallet_id}",
        headers=subject_headers,
    )
    assert w_res1.json()["current_balance"] == 100.0

    # 2. Disburse remaining balance via Direct ACH
    disb2_res = await client.post(
        f"/api/v1/payments/participant/wallets/{wallet_id}/disburse",
        json={"disbursement_type": "DIRECT_ACH"},
        headers=subject_headers,
    )
    assert disb2_res.status_code == 201
    disb2 = disb2_res.json()
    assert disb2["disbursement_type"] == "DIRECT_ACH"
    assert disb2["amount"] == 100.0

    # Check zero balance
    w_res2 = await client.get(
        f"/api/v1/payments/participant/wallets/{wallet_id}",
        headers=subject_headers,
    )
    assert w_res2.json()["current_balance"] == 0.0
    assert w_res2.json()["lifetime_disbursements"] == 200.0

    # 3. Test insufficient funds error
    fail_res = await client.post(
        f"/api/v1/payments/participant/wallets/{wallet_id}/disburse",
        json={"disbursement_type": "VIRTUAL_CARD", "amount": 50.0},
        headers=subject_headers,
    )
    assert fail_res.status_code == 400

    # 4. Verify immutable 21 CFR Part 11 ledger transactions
    tx_res = await client.get(
        f"/api/v1/payments/participant/wallets/{wallet_id}/transactions",
        headers=subject_headers,
    )
    assert tx_res.status_code == 200
    txs = tx_res.json()
    assert len(txs) == 3  # 1 STIPEND_CREDIT + 2 DISBURSEMENT_DEBIT
    debit_txs = [t for t in txs if t["transaction_type"] == "DISBURSEMENT_DEBIT"]
    assert len(debit_txs) == 2


@pytest.mark.asyncio
async def test_eu_ctr_lay_summary_portal_and_confirmation_receipt(client: AsyncClient):
    """Test EU CTR Regulation No 536/2014 Article 37 Plain Language Lay Summary publishing and SHA-256 receipt seals.

    @req:PRD-SYS-127
    @req:PRD-PAT-003
    @req:ADR-2199
    """
    sponsor_headers = get_auth_headers(
        roles="Sponsor Designer", user_id="designer_carol"
    )
    subject_headers = get_auth_headers(roles="Subject", user_id="subject_005")
    study_id = "STUDY-EUCTR-01"
    subject_id = "SUBJ-9005"

    # 1. Create and Publish Plain Language Lay Summary
    s_res = await client.post(
        "/api/v1/payments/lay-summaries",
        json={
            "study_id": study_id,
            "protocol_id": "PROTOCOL-EU-01",
            "title": "Plain Language Summary of Study CAD-2026 for Adult Solid Tumors",
            "trial_phase": "Phase III",
            "therapeutic_area": "Oncology",
            "sponsor_name": "Cadence BioSciences",
            "summary_text": "This summary explains trial results for CAD-2026 per EU CTR 536/2014 Article 37.",
            "plain_language_sections": {
                "objective": "To evaluate safety and clinical efficacy.",
                "participants": "450 adult patients across 25 sites.",
                "treatments": "CAD-2026 vs standard comparator.",
                "results": "Statistically significant benefit with 42% risk reduction.",
                "side_effects": "Mild fatigue and headache.",
                "future_plans": "Regulatory approval submissions underway.",
            },
            "irb_approval_number": "IRB-EUCTR-2026-01",
            "irb_approval_date": "2026-08-01",
            "language": "en",
            "status": "PUBLISHED",
        },
        headers=sponsor_headers,
    )
    assert s_res.status_code == 201
    summary = s_res.json()
    summary_id = summary["id"]
    assert summary["status"] == "PUBLISHED"
    assert summary["irb_approval_number"] == "IRB-EUCTR-2026-01"

    # 2. Get Lay Summary details
    get_res = await client.get(
        f"/api/v1/payments/lay-summaries/{summary_id}",
        headers=subject_headers,
    )
    assert get_res.status_code == 200
    assert get_res.json()["title"] == summary["title"]

    # 3. Participant confirms receipt & comprehension rating
    feedback = "Very easy to read and understand the primary clinical outcomes."
    statement = "I confirm receipt and review of this clinical trial lay summary."
    confirm_res = await client.post(
        f"/api/v1/payments/lay-summaries/{summary_id}/confirm-receipt",
        json={
            "subject_id": subject_id,
            "study_id": study_id,
            "comprehension_score": 5,
            "comprehension_feedback": feedback,
            "confirmation_statement": statement,
        },
        headers=subject_headers,
    )
    assert confirm_res.status_code == 201
    receipt = confirm_res.json()
    assert receipt["summary_id"] == summary_id
    assert receipt["subject_id"] == subject_id
    assert receipt["comprehension_score"] == 5
    assert receipt["status"] == "CONFIRMED"
    assert len(receipt["receipt_hash"]) == 64  # Valid SHA-256 hex digest

    # 4. Verify confirmation receipt audit ledger
    receipts_list_res = await client.get(
        f"/api/v1/payments/lay-summaries/{summary_id}/receipts",
        headers=sponsor_headers,
    )
    assert receipts_list_res.status_code == 200
    receipts = receipts_list_res.json()
    assert len(receipts) >= 1
    assert any(r["id"] == receipt["id"] for r in receipts)
