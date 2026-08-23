"""Integration and unit tests for Self-Billing Invoicing, Remittance Advice Generator, and ERP Sync.

Requirements: ADR-2199, PRD-PAY-004, PRD-PAY-006
"""

import pytest
from httpx import AsyncClient

from apps.payments.domain.models import InvoiceStatus, PayableStatus
from apps.payments.services.pdf_remittance_generator import (
    RemittanceAdvicePdfGenerator,
)
from apps.payments.tests.conftest import get_auth_headers


@pytest.mark.asyncio
async def test_compile_monthly_billing_cycle_and_invoice_aggregation(
    client: AsyncClient,
):
    """Validate compilation of monthly billing cycle aggregating approved payables into self-billing invoice draft.

    @req:ADR-2199
    @req:PRD-PAY-004
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-INV-001"
    site_id = "SITE-INV-101"

    # Step 1: Create budget contract
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Invoicing Test Contract",
            "overhead_percentage": 20.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    assert c_res.status_code == 201
    contract_id = c_res.json()["id"]

    # Step 2: Configure fee schedule
    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "V1",
            "form_id": "FORM-DEMO",
            "procedure_code": "PROC-DEMO",
            "procedure_name": "Demographics Evaluation",
            "base_amount": 500.0,
            "patient_stipend": 50.0,
            "travel_reimbursement": 25.0,
            "requires_sdv": False,
        },
        headers=headers,
    )

    # Step 3: Trigger 2 eCRF submissions to create payables
    p1_res = await client.post(
        "/api/v1/payments/events/ecrf-submission",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUBJ-001",
            "visit_code": "V1",
            "form_id": "FORM-DEMO",
            "event_id": "EVT-INV-01",
        },
        headers=headers,
    )
    assert p1_res.status_code == 201
    payable_id_1 = p1_res.json()[0]["id"]

    p2_res = await client.post(
        "/api/v1/payments/events/ecrf-submission",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUBJ-002",
            "visit_code": "V1",
            "form_id": "FORM-DEMO",
            "event_id": "EVT-INV-02",
        },
        headers=headers,
    )
    assert p2_res.status_code == 201
    payable_id_2 = p2_res.json()[0]["id"]

    # Step 4: Approve the payables with valid 21 CFR Part 11 e-sig tokens
    app1_path = f"/api/v1/payments/payables/{payable_id_1}/approve"
    app1_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Approve payable 1",
        action=app1_path,
    )
    appr1 = await client.post(app1_path, headers=app1_headers)
    assert appr1.status_code == 200
    assert appr1.json()["status"] == "APPROVED"

    app2_path = f"/api/v1/payments/payables/{payable_id_2}/approve"
    app2_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Approve payable 2",
        action=app2_path,
    )
    appr2 = await client.post(app2_path, headers=app2_headers)
    assert appr2.status_code == 200
    assert appr2.json()["status"] == "APPROVED"

    # Step 5: Compile Monthly Billing Cycle
    comp_res = await client.post(
        "/api/v1/payments/invoices/compile",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "billing_cycle": "MONTHLY",
            "period_start": "2026-08-01",
            "period_end": "2026-08-31",
            "auto_approve": False,
        },
        headers=headers,
    )
    assert comp_res.status_code == 201
    invoice = comp_res.json()

    assert invoice["study_id"] == study_id
    assert invoice["site_id"] == site_id
    assert invoice["billing_cycle"] == "MONTHLY"
    assert invoice["status"] == "DRAFT"
    assert invoice["payable_count"] == 2
    assert len(invoice["line_items"]) == 2

    # Financial Aggregation Assertions:
    # Per payable: base=500, overhead=100 (20%), stipend=50, travel=25 -> gross=675
    # withholding = 67.5 (10%), net = 607.5
    # For 2 payables: total_gross = 1350.0, total_holdback = 135.0, total_net = 1215.0
    assert invoice["total_base_amount"] == 1000.0
    assert invoice["total_overhead_amount"] == 200.0
    assert invoice["total_patient_stipend"] == 100.0
    assert invoice["total_travel_reimbursement"] == 50.0
    assert invoice["total_gross_amount"] == 1350.0
    assert invoice["total_holdback_amount"] == 135.0
    assert invoice["total_net_amount"] == 1215.0

    # Step 6: Verify Payables transitioned to INVOICED
    pay_check = await client.get(
        f"/api/v1/payments/payables/{payable_id_1}", headers=headers
    )
    assert pay_check.json()["status"] == PayableStatus.INVOICED.value

    # Step 7: Verify Remittance Advice record created and sealed with SHA-256
    remittance = invoice["remittance_advice"]
    assert remittance is not None
    assert remittance["gross_amount"] == 1350.0
    assert remittance["holdback_amount"] == 135.0
    assert remittance["net_amount"] == 1215.0
    assert len(remittance["pdf_sha256_hash"]) == 64
    assert remittance["is_sealed"] is True
    assert remittance["is_signed"] is False


@pytest.mark.asyncio
async def test_remittance_advice_pdf_download_and_tamper_detection(
    client: AsyncClient,
):
    """Validate remittance advice PDF download, cryptographic signing, and tamper detection.

    @req:ADR-2199
    @req:PRD-PAY-004
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-PDF-002"
    site_id = "SITE-PDF-202"

    # Setup contract and payables
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Remittance PDF Contract",
            "overhead_percentage": 15.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "SCREENING",
            "form_id": "FORM-SCR",
            "procedure_code": "PROC-LABS",
            "procedure_name": "Safety Blood Panel",
            "base_amount": 1000.0,
            "requires_sdv": False,
        },
        headers=headers,
    )

    p_res = await client.post(
        "/api/v1/payments/events/ecrf-submission",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUBJ-99",
            "visit_code": "SCREENING",
            "form_id": "FORM-SCR",
            "event_id": "EVT-PDF-01",
        },
        headers=headers,
    )
    payable_id = p_res.json()[0]["id"]
    app_path = f"/api/v1/payments/payables/{payable_id}/approve"
    app_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Approve payable",
        action=app_path,
    )
    await client.post(app_path, headers=app_headers)

    # Compile billing cycle
    comp_res = await client.post(
        "/api/v1/payments/invoices/compile",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "billing_cycle": "QUARTERLY",
            "period_start": "2026-07-01",
            "period_end": "2026-09-30",
            "auto_approve": True,
        },
        headers=headers,
    )
    invoice = comp_res.json()
    invoice_id = invoice["id"]

    # Step 1: Download binary PDF before signing
    pdf_res = await client.get(
        f"/api/v1/payments/invoices/{invoice_id}/remittance-advice/pdf",
        headers=headers,
    )
    assert pdf_res.status_code == 200
    assert pdf_res.headers["content-type"] == "application/pdf"
    assert pdf_res.content.startswith(b"%PDF-")
    assert len(pdf_res.headers["X-Document-SHA256"]) == 64

    # Step 2: Attempt dispatch before signing (must fail)
    dispatch_fail = await client.post(
        f"/api/v1/payments/invoices/{invoice_id}/remittance-advice/dispatch",
        json={"recipient_email": "finance@trial-site.org"},
        headers=headers,
    )
    assert dispatch_fail.status_code == 400
    assert (
        "Cannot dispatch unsigned remittance advice" in dispatch_fail.json()["detail"]
    )

    # Step 3: Cryptographically sign remittance advice statement with 21 CFR Part 11 manifest
    sign_path = f"/api/v1/payments/invoices/{invoice_id}/remittance-advice/sign"
    sign_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Authorized Self-Billing Remittance Disbursement Statement",
        action=sign_path,
    )
    sign_res = await client.post(
        sign_path,
        json={
            "signer_name": "Dr. Eleanor Vance, Grants Controller",
            "signer_role": "Director of Clinical Grants",
            "signature_reason": "Authorized Self-Billing Remittance Disbursement Statement",
        },
        headers=sign_headers,
    )
    assert sign_res.status_code == 200
    signed_remittance = sign_res.json()
    assert signed_remittance["is_signed"] is True
    assert signed_remittance["signed_by"] == "Dr. Eleanor Vance, Grants Controller"
    assert signed_remittance["signature_manifest"]["is_valid"] is True

    # Step 4: Verify cryptographic signature endpoint
    verify_res = await client.post(
        f"/api/v1/payments/invoices/{invoice_id}/remittance-advice/verify-signature",
        headers=headers,
    )
    assert verify_res.status_code == 200
    verify_data = verify_res.json()
    assert verify_data["is_valid"] is True
    assert verify_data["status"] == "VALID"
    assert verify_data["signed_by"] == "Dr. Eleanor Vance, Grants Controller"

    # Step 5: Validate Tamper Detection on Altered PDF Bytes
    signed_pdf_download = await client.get(
        f"/api/v1/payments/invoices/{invoice_id}/remittance-advice/pdf",
        headers=headers,
    )
    signed_bytes = signed_pdf_download.content
    tampered_bytes = signed_bytes[:50] + b"X" + signed_bytes[51:]

    # Verification must flag tamper detection
    from packages.compliance.services.esignature_verifier import (
        TamperDetectedError,
    )

    with pytest.raises(TamperDetectedError):
        RemittanceAdvicePdfGenerator.verify_remittance_pdf(tampered_bytes)

    # Step 6: Dispatch signed remittance advice statement
    dispatch_ok = await client.post(
        f"/api/v1/payments/invoices/{invoice_id}/remittance-advice/dispatch",
        json={"recipient_email": "finance@trial-site.org"},
        headers=headers,
    )
    assert dispatch_ok.status_code == 200
    assert dispatch_ok.json()["dispatch_status"] == "DISPATCHED"
    assert dispatch_ok.json()["dispatched_to"] == "finance@trial-site.org"


@pytest.mark.asyncio
async def test_erp_synchronization_and_webhook_reconciliation(
    client: AsyncClient,
):
    """Validate enterprise ERP export, webhook synchronization, and payment reconciliation.

    @req:ADR-2199
    @req:PRD-PAY-004
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-ERP-003"
    site_id = "SITE-ERP-303"

    # Setup contract with multi-party split payees (70% Hospital, 30% PI Practice)
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "ERP Sync Contract",
            "overhead_percentage": 10.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/split-payees",
        json={
            "payees": [
                {
                    "payee_id": "PAYEE-HOSP",
                    "payee_name": "University Hospital Foundation",
                    "payee_role": "HOSPITAL",
                    "allocation_percentage": 70.0,
                    "bank_account_reference": "BANK-HOSP-987",
                    "tax_identifier": "EIN-12-3456789",
                },
                {
                    "payee_id": "PAYEE-PI",
                    "payee_name": "Principal Investigator LLC",
                    "payee_role": "PI_PRACTICE",
                    "allocation_percentage": 30.0,
                    "bank_account_reference": "BANK-PI-456",
                    "tax_identifier": "EIN-98-7654321",
                },
            ]
        },
        headers=headers,
    )

    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "V1",
            "form_id": "FORM-CT",
            "procedure_code": "PROC-CT-SCAN",
            "procedure_name": "Computed Tomography Scan",
            "base_amount": 2000.0,
            "requires_sdv": False,
        },
        headers=headers,
    )

    p_res = await client.post(
        "/api/v1/payments/events/ecrf-submission",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUBJ-ERP-01",
            "visit_code": "V1",
            "form_id": "FORM-CT",
            "event_id": "EVT-ERP-01",
        },
        headers=headers,
    )
    payable_id = p_res.json()[0]["id"]
    app_path = f"/api/v1/payments/payables/{payable_id}/approve"
    app_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Approve payable",
        action=app_path,
    )
    await client.post(app_path, headers=app_headers)

    # Compile invoice
    comp_res = await client.post(
        "/api/v1/payments/invoices/compile",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "billing_cycle": "MONTHLY",
            "period_start": "2026-08-01",
            "period_end": "2026-08-31",
            "auto_approve": True,
        },
        headers=headers,
    )
    invoice = comp_res.json()
    invoice_id = invoice["id"]
    invoice_number = invoice["invoice_number"]

    # Step 1: Export to SAP ERP
    sap_export = await client.post(
        f"/api/v1/payments/invoices/{invoice_id}/erp-export?erp_system=SAP",
        headers=headers,
    )
    assert sap_export.status_code == 200
    export_data = sap_export.json()
    assert export_data["erp_system"] == "SAP"
    assert export_data["invoice_number"] == invoice_number
    assert len(export_data["payload"]["payee_distributions"]) == 2

    # Step 2: Inbound ERP Webhook: Status = PAID from SAP
    webhook_headers = get_auth_headers(roles="Grants Manager")
    sap_webhook = await client.post(
        "/api/v1/payments/erp/webhook",
        json={
            "erp_system": "SAP",
            "invoice_id": invoice_id,
            "invoice_number": invoice_number,
            "erp_reference_id": "SAP-DOC-2026-908123",
            "status": "PAID",
            "amount_paid": 1980.0,
            "payment_date": "2026-08-25",
            "bank_reference": "WIRE-FED-8891230",
            "notes": "Automated SAP batch clearing run",
        },
        headers=webhook_headers,
    )
    assert sap_webhook.status_code == 200
    webhook_data = sap_webhook.json()
    assert webhook_data["new_status"] == "PAID"
    assert webhook_data["erp_reference_id"] == "SAP-DOC-2026-908123"

    # Step 3: Verify invoice status updated to PAID
    inv_check = await client.get(
        f"/api/v1/payments/invoices/{invoice_id}", headers=headers
    )
    assert inv_check.json()["status"] == InvoiceStatus.PAID.value

    # Step 4: Final Payment Reconciliation
    reconcile_res = await client.post(
        f"/api/v1/payments/invoices/{invoice_id}/reconcile",
        json={
            "payment_reference": "WIRE-FED-8891230",
            "erp_system": "SAP",
            "payment_date": "2026-08-25",
            "amount_paid": 1980.0,
            "notes": "Matched against bank settlement journal",
        },
        headers=headers,
    )
    assert reconcile_res.status_code == 200
    recon_data = reconcile_res.json()
    assert recon_data["status"] == "RECONCILED"
    assert recon_data["erp_sync_status"] == "RECONCILED"
    assert recon_data["reconciliation_reference"] == "WIRE-FED-8891230"
    assert recon_data["reconciled_payables_count"] == 1

    # Step 5: Check ERP Sync Logs
    logs_res = await client.get(
        f"/api/v1/payments/invoices/{invoice_id}/erp-logs", headers=headers
    )
    assert logs_res.status_code == 200
    erp_logs = logs_res.json()
    assert len(erp_logs) >= 3  # OUTBOUND_EXPORT, INBOUND_WEBHOOK, RECONCILIATION

    # Step 6: Verify Immutable 21 CFR Part 11 Audit Trail
    audit_res = await client.get(
        f"/api/v1/payments/audit-logs?entity_type=INVOICE&entity_id={invoice_id}",
        headers=headers,
    )
    assert audit_res.status_code == 200
    audit_logs = audit_res.json()
    actions = [a["action"] for a in audit_logs]
    assert "INVOICE_COMPILED" in actions
    assert "ERP_PAYMENT_SYNCED" in actions
    assert "INVOICE_RECONCILED" in actions


@pytest.mark.asyncio
async def test_netsuite_export_and_invoices_filtering(client: AsyncClient):
    """Validate NetSuite ERP export format and invoice query filtering.

    @req:ADR-2199
    @req:PRD-PAY-004
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-NET-004"
    site_id = "SITE-NET-404"

    # Setup contract and payable
    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "NetSuite Contract",
            "overhead_percentage": 25.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "V1",
            "form_id": "FORM-NS",
            "procedure_code": "PROC-NS",
            "procedure_name": "NetSuite Test Procedure",
            "base_amount": 1500.0,
            "requires_sdv": False,
        },
        headers=headers,
    )

    p_res = await client.post(
        "/api/v1/payments/events/ecrf-submission",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUBJ-NS-01",
            "visit_code": "V1",
            "form_id": "FORM-NS",
            "event_id": "EVT-NS-01",
        },
        headers=headers,
    )
    payable_id = p_res.json()[0]["id"]
    app_path = f"/api/v1/payments/payables/{payable_id}/approve"
    app_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Approve payable",
        action=app_path,
    )
    await client.post(app_path, headers=app_headers)

    # Compile invoice
    comp_res = await client.post(
        "/api/v1/payments/invoices/compile",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "billing_cycle": "MANUAL",
            "period_start": "2026-08-01",
            "period_end": "2026-08-15",
            "auto_approve": True,
        },
        headers=headers,
    )
    invoice = comp_res.json()
    invoice_id = invoice["id"]

    # Export to NetSuite
    ns_export = await client.post(
        f"/api/v1/payments/invoices/{invoice_id}/erp-export?erp_system=NETSUITE",
        headers=headers,
    )
    assert ns_export.status_code == 200
    ns_data = ns_export.json()
    assert ns_data["erp_system"] == "NETSUITE"
    assert ns_data["document_type"] == "SELF_BILLING_INVOICE"
    assert ns_data["payload"]["currency"] == "USD"
    assert ns_data["payload"]["net_amount"] == invoice["total_net_amount"]

    # Filter invoices query
    list_res = await client.get(
        f"/api/v1/payments/invoices?study_id={study_id}&billing_cycle=MANUAL&erp_sync_status=SYNCED",
        headers=headers,
    )
    assert list_res.status_code == 200
    matching = list_res.json()
    assert len(matching) >= 1
    assert matching[0]["id"] == invoice_id


@pytest.mark.asyncio
async def test_unsigned_remittance_advice_verification(client: AsyncClient):
    """Validate verification status of an unsigned remittance advice statement.

    @req:ADR-2199
    @req:PRD-PAY-004
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-UNSIG-005"
    site_id = "SITE-UNSIG-505"

    c_res = await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Unsigned Test Contract",
            "overhead_percentage": 10.0,
            "withholding_percentage": 10.0,
        },
        headers=headers,
    )
    contract_id = c_res.json()["id"]

    await client.post(
        f"/api/v1/payments/contracts/{contract_id}/fee-schedules",
        json={
            "visit_code": "V1",
            "form_id": "FORM-U",
            "procedure_code": "PROC-01",
            "procedure_name": "Test Procedure",
            "base_amount": 500.0,
            "requires_sdv": False,
        },
        headers=headers,
    )

    p_res = await client.post(
        "/api/v1/payments/events/ecrf-submission",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUBJ-U-01",
            "visit_code": "V1",
            "form_id": "FORM-U",
            "event_id": "EVT-U-01",
        },
        headers=headers,
    )
    payable_id = p_res.json()[0]["id"]
    app_path = f"/api/v1/payments/payables/{payable_id}/approve"
    app_headers = get_auth_headers(
        roles="Grants Manager",
        change_reason="Approve payable",
        action=app_path,
    )
    await client.post(app_path, headers=app_headers)

    comp_res = await client.post(
        "/api/v1/payments/invoices/compile",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "billing_cycle": "MONTHLY",
            "period_start": "2026-08-01",
            "period_end": "2026-08-31",
            "auto_approve": False,
        },
        headers=headers,
    )
    invoice_id = comp_res.json()["id"]

    verify_res = await client.post(
        f"/api/v1/payments/invoices/{invoice_id}/remittance-advice/verify-signature",
        headers=headers,
    )
    assert verify_res.status_code == 200
    res_data = verify_res.json()
    assert res_data["is_valid"] is False
    assert res_data["status"] == "UNSIGNED"
    assert "has not been electronically signed yet" in res_data["failure_reason"]


@pytest.mark.asyncio
async def test_compilation_with_no_payables_fails_gracefully(client: AsyncClient):
    """Validate that attempting to compile a billing cycle with zero approved payables returns 400 Bad Request.

    @req:ADR-2199
    @req:PRD-PAY-004
    @req:PRD-PAY-006
    """
    headers = get_auth_headers(roles="Grants Manager")
    study_id = "STUDY-EMPTY-006"
    site_id = "SITE-EMPTY-606"

    await client.post(
        "/api/v1/payments/contracts",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "contract_name": "Empty Test Contract",
        },
        headers=headers,
    )

    comp_res = await client.post(
        "/api/v1/payments/invoices/compile",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "billing_cycle": "MONTHLY",
            "period_start": "2026-08-01",
            "period_end": "2026-08-31",
        },
        headers=headers,
    )
    assert comp_res.status_code == 400
    assert "No approved payable line items available" in comp_res.json()["detail"]


@pytest.mark.asyncio
async def test_rbac_permissions_for_invoicing(client: AsyncClient):
    """Validate Role-Based Access Control (RBAC) on financial invoicing endpoints.

    @req:ADR-2199
    @req:PRD-PAY-004
    @req:PRD-PAY-006
    """
    read_headers = get_auth_headers(roles="Auditor")

    # Read-only user cannot compile invoices
    compile_res = await client.post(
        "/api/v1/payments/invoices/compile",
        json={
            "study_id": "STUDY-RBAC",
            "site_id": "SITE-RBAC",
            "billing_cycle": "MONTHLY",
            "period_start": "2026-08-01",
            "period_end": "2026-08-31",
        },
        headers=read_headers,
    )
    assert compile_res.status_code == 403

    # Read-only user can list invoices
    list_res = await client.get("/api/v1/payments/invoices", headers=read_headers)
    assert list_res.status_code == 200
