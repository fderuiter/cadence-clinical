"""Unit tests for CTMS Self-Billing Invoice & Remittance Engine.

@req:PRD-PAY-003
@req:PRD-SYS-001
"""

from apps.ctms.services.self_billing_engine import (
    SelfBillingEngine,
)


def test_tax_treatment_domestic_us():
    """Verify US domestic invoicing has 0% tax."""
    treatment, rate = SelfBillingEngine.determine_tax_treatment("US", "US")
    assert treatment == "STANDARD"
    assert rate == 0.0


def test_tax_treatment_domestic_uk():
    """Verify UK domestic invoicing applies standard 20% VAT."""
    treatment, rate = SelfBillingEngine.determine_tax_treatment("GB", "GB")
    assert treatment == "STANDARD"
    assert rate == 0.20


def test_tax_treatment_cross_border_eu_reverse_charge():
    """Verify EU cross-border B2B with VAT number applies reverse-charge treatment."""
    treatment, rate = SelfBillingEngine.determine_tax_treatment(
        sponsor_country="DE",
        site_country="FR",
        site_vat_id="FR12345678901",
    )
    assert treatment == "REVERSE_CHARGE"
    assert rate == 0.0


def test_tax_treatment_cross_border_non_eu_exempt():
    """Verify cross-border clinical services outside EU are export exempt."""
    treatment, rate = SelfBillingEngine.determine_tax_treatment(
        sponsor_country="US",
        site_country="JP",
    )
    assert treatment == "EXEMPT"
    assert rate == 0.0


def test_generate_self_billing_invoice_calculation():
    """Validate invoice aggregation, withholding reserve calculation, and cryptographic digest."""
    payables = [
        {
            "id": "pay_001",
            "payable_type": "PROCEDURE",
            "description": "Baseline MRI Scan",
            "gross_amount": 1000.0,
            "overhead_amount": 250.0,  # 25% overhead
            "withholding_amount": 100.0,  # 10% withholding reserve
        },
        {
            "id": "pay_002",
            "payable_type": "PROCEDURE",
            "description": "Pharmacokinetic Blood Draw",
            "gross_amount": 500.0,
            "overhead_amount": 125.0,
            "withholding_amount": 50.0,
        },
    ]

    invoice = SelfBillingEngine.generate_invoice(
        study_id="STUDY-101",
        site_id="SITE-001",
        grant_id="GRANT-999",
        payee_name="University Hospital Clinical Research Ltd",
        payee_tax_id="GB999888777",
        sponsor_country="GB",
        site_country="GB",
        payables=payables,
        currency="GBP",
        sponsor_currency="GBP",
        exchange_rate=1.0,
    )

    assert invoice.invoice_number.startswith("SBI-STUDY--SITE-0")
    assert invoice.total_gross_amount == 1500.0
    assert invoice.total_overhead_amount == 375.0
    assert invoice.total_withholding_reserve == 150.0

    # Taxable base: 1875.0 * 20% VAT = 375.0
    assert invoice.total_tax_amount == 375.0

    # Net payable: (1875.0 + 375.0) - 150.0 = 2100.0
    assert invoice.total_net_payable == 2100.0
    assert len(invoice.line_items) == 2
    assert invoice.digital_digest != ""
    assert invoice.version_index == 1
