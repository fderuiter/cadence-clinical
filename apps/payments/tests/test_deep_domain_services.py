"""Direct unit tests for the three decomposed Payments Deep Domain Modules:
1. SitePaymentLedgerEngine
2. BillingAndErpGateway
3. ParticipantCompensationService

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-004, PRD-PAY-006, PRD-SYS-161, PRD-SYS-162, PRD-SYS-163
"""

import pytest
from sqlalchemy.ext.asyncio import AsyncSession

from apps.payments.presentation.dtos import (
    ErpWebhookPayload,
    SplitPayeeConfigItem,
)
from apps.payments.services.audit import list_payment_audit_logs
from apps.payments.services.billing_and_erp_gateway import (
    BillingAndErpGateway,
)
from apps.payments.services.participant_compensation_service import (
    ParticipantCompensationService,
)
from apps.payments.services.site_payment_ledger_engine import (
    SitePaymentLedgerEngine,
)


@pytest.mark.asyncio
async def test_site_payment_ledger_engine_direct(db_session: AsyncSession):
    """Directly test SitePaymentLedgerEngine domain methods without HTTP layer.

    @req:PRD-SYS-161
    @req:PRD-SYS-162
    @req:ADR-2199
    @req:PRD-PAY-002
    @req:PRD-PAY-003
    """
    engine = SitePaymentLedgerEngine(session=db_session)

    # 1. Create contract
    contract = await engine.create_budget_contract(
        study_id="STUDY-DIRECT-01",
        site_id="SITE-DIRECT-01",
        contract_name="Direct Ledger Contract",
        total_budget=50000.0,
        overhead_percentage=20.0,
        withholding_percentage=10.0,
        user_id="user_admin",
    )
    assert contract.id is not None
    assert contract.overhead_percentage == 20.0

    # 2. Add split payee
    payees = await engine.configure_split_payees(
        contract_id=contract.id,
        payees=[
            SplitPayeeConfigItem(
                payee_id="PAYEE-MAIN",
                payee_name="Main Hospital",
                payee_role="HOSPITAL",
                allocation_percentage=100.0,
            )
        ],
        user_id="user_admin",
    )
    assert len(payees) == 1
    assert payees[0].allocation_percentage == 100.0

    # 3. Add fee schedule
    fee = await engine.add_fee_schedule(
        contract_id=contract.id,
        visit_code="V1",
        procedure_code="PROC-01",
        procedure_name="Initial Physical",
        base_amount=300.0,
        requires_sdv=False,
        user_id="user_admin",
    )
    assert fee.base_amount == 300.0

    # 4. Trigger payable via eCRF submission
    payables = await engine.process_ecrf_submission_event(
        study_id="STUDY-DIRECT-01",
        site_id="SITE-DIRECT-01",
        subject_id="SUBJ-001",
        visit_code="V1",
        form_id="FORM-01",
        event_id="EVT-DIRECT-01",
    )
    assert len(payables) == 1
    payable = payables[0]
    assert payable.base_amount == 300.0
    assert payable.overhead_amount == 60.0  # 20% of 300
    assert payable.gross_amount == 360.0
    assert payable.withholding_amount == 36.0  # 10% of 360
    assert payable.net_amount == 324.0
    assert (
        payable.status == "TRIGGERED"
    )  # requires_sdv=False -> TRIGGERED (ready for billing or manual approval)

    # 5. Check audit logs
    logs = await list_payment_audit_logs(
        session=db_session, entity_type="PAYABLE", entity_id=payable.id
    )
    assert len(logs) >= 1
    assert logs[0].action == "PAYABLE_TRIGGERED_ECRF"


@pytest.mark.asyncio
async def test_billing_and_erp_gateway_direct(db_session: AsyncSession):
    """Directly test BillingAndErpGateway domain methods for invoicing and reconciliation.

    @req:PRD-SYS-163
    @req:ADR-2199
    @req:PRD-PAY-004
    @req:PRD-PAY-006
    """
    ledger = SitePaymentLedgerEngine(session=db_session)
    gateway = BillingAndErpGateway(session=db_session)

    # Setup contract, fee schedule, and approved payable
    contract = await ledger.create_budget_contract(
        study_id="STUDY-BILL-01",
        site_id="SITE-BILL-01",
        contract_name="Billing Test Contract",
        overhead_percentage=15.0,
        withholding_percentage=10.0,
    )
    await ledger.add_fee_schedule(
        contract_id=contract.id,
        visit_code="VISIT_1",
        procedure_code="PROC_LAB",
        procedure_name="Blood Panel",
        base_amount=500.0,
        requires_sdv=False,
    )
    payables = await ledger.process_ecrf_submission_event(
        study_id="STUDY-BILL-01",
        site_id="SITE-BILL-01",
        subject_id="SUBJ-100",
        visit_code="VISIT_1",
        form_id="FORM_LAB",
        event_id="EVT-BILL-01",
    )
    assert len(payables) == 1
    payable = payables[0]
    assert payable.status == "TRIGGERED"

    # 1. Compile billing cycle into Self-Billing Invoice
    invoice = await gateway.compile_billing_cycle(
        study_id="STUDY-BILL-01",
        site_id="SITE-BILL-01",
        billing_cycle="MONTHLY",
        auto_approve=True,
    )
    assert invoice.status == "APPROVED"
    assert invoice.payable_count == 1
    assert invoice.total_base_amount == 500.0
    assert invoice.remittance_advice is not None
    assert invoice.remittance_advice.is_sealed is True

    # 2. Cryptographically sign remittance advice
    signed_remittance = await gateway.sign_and_seal_remittance_advice(
        invoice_id=invoice.id,
        signer_name="Dr. Financial Lead",
        signer_role="Grants Officer",
        signature_reason="Authorized Remittance Statement",
    )
    assert signed_remittance.is_signed is True
    assert signed_remittance.signed_by == "Dr. Financial Lead"

    # 3. Verify digital signature
    verify_res = await gateway.verify_remittance_advice_signature(invoice_id=invoice.id)
    assert verify_res["is_valid"] is True
    assert verify_res["status"] == "VALID"

    # 4. Export invoice to ERP
    envelope = await gateway.export_invoice_to_erp(
        invoice_id=invoice.id, erp_system="SAP"
    )
    assert envelope.erp_system == "SAP"
    assert envelope.invoice_number == invoice.invoice_number

    # 5. Process inbound ERP webhook
    webhook_res = await gateway.process_erp_webhook(
        payload=ErpWebhookPayload(
            erp_system="SAP",
            invoice_id=invoice.id,
            erp_reference_id="SAP-DOC-987654",
            status="RECONCILED",
            bank_reference="WIRE-ACH-9999",
            notes="Direct settlement received",
        )
    )
    assert webhook_res["status"] == "success"
    assert webhook_res["new_status"] == "RECONCILED"
    assert webhook_res["reconciled"] is True


@pytest.mark.asyncio
async def test_participant_compensation_service_direct(db_session: AsyncSession):
    """Directly test ParticipantCompensationService for wallets, OCR, and lay summaries.

    @req:PRD-SYS-182
    @req:PRD-SYS-161
    @req:ADR-2199
    @req:PRD-PAY-002
    @req:PRD-PAY-004
    @req:PRD-PAY-006
    """
    comp_service = ParticipantCompensationService(session=db_session)

    # 1. Provision participant wallet
    wallet = await comp_service.get_or_create_wallet(
        subject_id="PATIENT-888",
        study_id="STUDY-PATIENT-01",
        site_id="SITE-PATIENT-01",
        currency="USD",
    )
    assert wallet.current_balance == 0.0
    assert wallet.status == "ACTIVE"

    # 2. Configure stipend rule and process visit verification
    await comp_service.create_stipend_rule(
        study_id="STUDY-PATIENT-01",
        visit_code="CYCLE_1_DAY_1",
        visit_name="Cycle 1 Day 1 Visit",
        amount=75.0,
    )
    stipend_res = await comp_service.process_visit_verified_stipend(
        study_id="STUDY-PATIENT-01",
        site_id="SITE-PATIENT-01",
        subject_id="PATIENT-888",
        visit_code="CYCLE_1_DAY_1",
        event_id="VISIT-EVT-888-01",
    )
    assert stipend_res["status"] == "CREDITED"
    assert stipend_res["amount"] == 75.0
    assert stipend_res["current_balance"] == 75.0

    # 3. Test OCR extraction on travel receipt
    ocr_result = comp_service.extract_receipt_ocr(
        raw_text="UBER TRIP\nDate: 2026-08-23\nTotal: $32.40\nRide to Clinic"
    )
    assert ocr_result.category == "TRANSIT"
    assert ocr_result.gross_amount == 32.40
    assert ocr_result.net_amount == 32.40

    # 4. Submit travel expense claim and approve it
    claim = await comp_service.submit_expense_claim(
        subject_id="PATIENT-888",
        study_id="STUDY-PATIENT-01",
        site_id="SITE-PATIENT-01",
        expense_date="2026-08-23",
        category=ocr_result.category,
        merchant_name=ocr_result.merchant_name,
        gross_amount=ocr_result.gross_amount,
        wallet_id=wallet.id,
    )
    assert claim.status == "PENDING_REVIEW"

    reviewed_claim = await comp_service.review_expense_claim(
        claim_id=claim.id,
        review_status="APPROVED",
        reviewer_id="crc_jane",
    )
    assert reviewed_claim.status == "APPROVED"

    # Verify wallet received the reimbursement: 75.0 + 32.40 = 107.40
    refreshed_wallet = await comp_service.get_wallet_by_id(wallet.id)
    assert refreshed_wallet.current_balance == 107.40

    # 5. Disburse funds
    disbursement = await comp_service.disburse_funds(
        wallet_id=wallet.id,
        disbursement_type="VIRTUAL_CARD",
        amount=50.0,
    )
    assert disbursement.status == "DISPATCHED"
    assert disbursement.amount == 50.0

    # Verify balance reduced: 107.40 - 50.0 = 57.40
    final_wallet = await comp_service.get_wallet_by_id(wallet.id)
    assert final_wallet.current_balance == 57.40

    # 6. EU CTR Lay Summary lifecycle
    lay_summary = await comp_service.create_or_update_lay_summary(
        study_id="STUDY-PATIENT-01",
        title="Phase 3 Study Results Summary",
        therapeutic_area="Oncology",
        sponsor_name="Cadence Clinical",
        summary_text="Plain language clinical results.",
        irb_approval_number="IRB-2026-9911",
        irb_approval_date="2026-08-20",
    )
    assert lay_summary.status == "PUBLISHED"

    receipt = await comp_service.confirm_lay_summary_receipt(
        summary_id=lay_summary.id,
        subject_id="PATIENT-888",
        study_id="STUDY-PATIENT-01",
        comprehension_score=5,
    )
    assert receipt.receipt_hash is not None
    assert receipt.comprehension_score == 5
