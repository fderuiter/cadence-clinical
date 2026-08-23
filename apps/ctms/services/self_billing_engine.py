"""Automated Self-Billing Invoicing, Tax Calculation, and Remittance Advice Engine for CTMS Site Payments.

Requirements: PRD-PAY-003, PRD-SYS-001 (21 CFR Part 11)
"""

import hashlib
import json
from datetime import UTC, datetime
from typing import Any

from pydantic import BaseModel, Field


class TaxRule(BaseModel):
    """Tax calculation rule for site jurisdiction and trial location."""

    country_code: str
    standard_vat_rate: float = 0.0  # e.g., 0.20 for 20%
    is_eu_member: bool = False
    reverse_charge_applicable: bool = False


class RemittanceLineItem(BaseModel):
    """Single payable line item in a remittance advice document."""

    payable_id: str
    payable_type: str
    description: str
    gross_amount: float
    overhead_amount: float
    withholding_reserve_amount: float
    tax_amount: float
    net_payable_amount: float
    currency: str


class SelfBillingInvoice(BaseModel):
    """Self-billing tax invoice produced automatically on behalf of clinical site."""

    invoice_number: str
    study_id: str
    site_id: str
    grant_id: str
    payee_name: str
    payee_tax_id: str | None = None
    billing_period_start: str
    billing_period_end: str
    currency: str
    exchange_rate_to_sponsor: float = 1.0
    sponsor_currency: str = "USD"
    total_gross_amount: float = 0.0
    total_overhead_amount: float = 0.0
    total_withholding_reserve: float = 0.0
    total_tax_amount: float = 0.0
    total_net_payable: float = 0.0
    tax_treatment: str = "STANDARD"  # STANDARD, EXEMPT, REVERSE_CHARGE
    line_items: list[RemittanceLineItem] = Field(default_factory=list)
    digital_digest: str = ""
    status: str = "GENERATED"  # GENERATED, APPROVED, DISBURSED, CANCELLED
    created_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    created_by: str = "system_self_billing_engine"
    reason_for_change: str = "Automated Monthly Self-Billing Invoicing Run"
    version_index: int = 1


class SelfBillingEngine:
    """Core domain engine for generating Part 11 compliant self-billing invoices and remittance advices."""

    DEFAULT_TAX_RULES: dict[str, TaxRule] = {
        "US": TaxRule(country_code="US", standard_vat_rate=0.0, is_eu_member=False),
        "GB": TaxRule(country_code="GB", standard_vat_rate=0.20, is_eu_member=False),
        "DE": TaxRule(
            country_code="DE",
            standard_vat_rate=0.19,
            is_eu_member=True,
            reverse_charge_applicable=True,
        ),
        "FR": TaxRule(
            country_code="FR",
            standard_vat_rate=0.20,
            is_eu_member=True,
            reverse_charge_applicable=True,
        ),
        "NL": TaxRule(
            country_code="NL",
            standard_vat_rate=0.21,
            is_eu_member=True,
            reverse_charge_applicable=True,
        ),
        "JP": TaxRule(country_code="JP", standard_vat_rate=0.10, is_eu_member=False),
    }

    @classmethod
    def determine_tax_treatment(
        cls,
        sponsor_country: str,
        site_country: str,
        site_vat_id: str | None = None,
    ) -> tuple[str, float]:
        """Calculates tax rate and VAT treatment code based on jurisdiction and tax registration.

        Args:
            sponsor_country: ISO 2-letter country code of sponsor entity.
            site_country: ISO 2-letter country code of clinical site institution.
            site_vat_id: Optional registered VAT/tax identification number.

        Returns:
            Tuple of (tax_treatment_code, effective_tax_rate).
        """
        sponsor_rule = cls.DEFAULT_TAX_RULES.get(
            sponsor_country.upper(),
            TaxRule(country_code=sponsor_country, standard_vat_rate=0.0),
        )
        site_rule = cls.DEFAULT_TAX_RULES.get(
            site_country.upper(),
            TaxRule(country_code=site_country, standard_vat_rate=0.0),
        )

        # Cross-border EU B2B with valid VAT ID -> Reverse Charge (0% at billing, self-accounted)
        if (
            sponsor_rule.is_eu_member
            and site_rule.is_eu_member
            and sponsor_country.upper() != site_country.upper()
            and site_vat_id
        ):
            return "REVERSE_CHARGE", 0.0

        # Domestic invoicing
        if sponsor_country.upper() == site_country.upper():
            return "STANDARD", site_rule.standard_vat_rate

        # Non-EU cross-border export of clinical services -> EXEMPT
        if sponsor_country.upper() != site_country.upper():
            return "EXEMPT", 0.0

        return "STANDARD", site_rule.standard_vat_rate

    @classmethod
    def generate_invoice(
        cls,
        study_id: str,
        site_id: str,
        grant_id: str,
        payee_name: str,
        payee_tax_id: str | None,
        sponsor_country: str,
        site_country: str,
        payables: list[dict[str, Any]],
        currency: str = "USD",
        sponsor_currency: str = "USD",
        exchange_rate: float = 1.0,
        billing_period_start: str | None = None,
        billing_period_end: str | None = None,
        created_by: str = "system_self_billing_engine",
        reason_for_change: str = "Automated Monthly Self-Billing Invoicing Run",
    ) -> SelfBillingInvoice:
        """Assembles verified payable records into a consolidated self-billing tax invoice.

        Args:
            study_id: Unique trial identifier.
            site_id: Clinical site code.
            grant_id: Executed investigator grant contract ID.
            payee_name: Legal name of institution or payee entity.
            payee_tax_id: Registered VAT or TIN identifier.
            sponsor_country: Country of contracting sponsor.
            site_country: Country of site.
            payables: List of verified payable dictionary records.
            currency: Contract operational currency.
            sponsor_currency: Consolidated reporting currency.
            exchange_rate: Conversion rate from contract currency to sponsor currency.
            billing_period_start: ISO start date for billing cycle.
            billing_period_end: ISO end date for billing cycle.
            created_by: Actor initiating invoice generation.
            reason_for_change: 21 CFR Part 11 audit rationale.

        Returns:
            Fully populated, cryptographically digested SelfBillingInvoice entity.
        """
        treatment, tax_rate = cls.determine_tax_treatment(
            sponsor_country=sponsor_country,
            site_country=site_country,
            site_vat_id=payee_tax_id,
        )

        now_str = datetime.now(UTC).isoformat()
        period_start = billing_period_start or now_str[:10]
        period_end = billing_period_end or now_str[:10]

        invoice_seq = (
            hashlib.sha256(f"{study_id}:{site_id}:{period_start}:{period_end}".encode())
            .hexdigest()[:8]
            .upper()
        )
        invoice_number = (
            f"SBI-{study_id[:6].upper()}-{site_id[:6].upper()}-{invoice_seq}"
        )

        line_items: list[RemittanceLineItem] = []
        tot_gross = 0.0
        tot_overhead = 0.0
        tot_withholding = 0.0
        tot_tax = 0.0
        tot_net = 0.0

        for p in payables:
            gross = float(p.get("gross_amount", 0.0) or p.get("base_amount", 0.0))
            overhead = float(p.get("overhead_amount", 0.0))
            withholding = float(p.get("withholding_amount", 0.0))

            taxable_base = gross + overhead
            tax_amt = round(taxable_base * tax_rate, 2)
            net_amt = round(taxable_base + tax_amt - withholding, 2)

            item = RemittanceLineItem(
                payable_id=str(p.get("id", f"pay_{len(line_items) + 1}")),
                payable_type=str(p.get("payable_type", "PROCEDURE")),
                description=str(p.get("description", "Clinical procedure fee")),
                gross_amount=round(gross, 2),
                overhead_amount=round(overhead, 2),
                withholding_reserve_amount=round(withholding, 2),
                tax_amount=tax_amt,
                net_payable_amount=net_amt,
                currency=currency,
            )
            line_items.append(item)

            tot_gross += gross
            tot_overhead += overhead
            tot_withholding += withholding
            tot_tax += tax_amt
            tot_net += net_amt

        # Compute SHA-256 cryptographic digest for Part 11 non-repudiation
        digest_manifest = {
            "invoice_number": invoice_number,
            "study_id": study_id,
            "site_id": site_id,
            "grant_id": grant_id,
            "total_net_payable": round(tot_net, 2),
            "line_item_count": len(line_items),
        }
        digest = hashlib.sha256(
            json.dumps(digest_manifest, sort_keys=True).encode()
        ).hexdigest()

        return SelfBillingInvoice(
            invoice_number=invoice_number,
            study_id=study_id,
            site_id=site_id,
            grant_id=grant_id,
            payee_name=payee_name,
            payee_tax_id=payee_tax_id,
            billing_period_start=period_start,
            billing_period_end=period_end,
            currency=currency,
            exchange_rate_to_sponsor=exchange_rate,
            sponsor_currency=sponsor_currency,
            total_gross_amount=round(tot_gross, 2),
            total_overhead_amount=round(tot_overhead, 2),
            total_withholding_reserve=round(tot_withholding, 2),
            total_tax_amount=round(tot_tax, 2),
            total_net_payable=round(tot_net, 2),
            tax_treatment=treatment,
            line_items=line_items,
            digital_digest=digest,
            status="GENERATED",
            created_at=now_str,
            created_by=created_by,
            reason_for_change=reason_for_change,
            version_index=1,
        )
