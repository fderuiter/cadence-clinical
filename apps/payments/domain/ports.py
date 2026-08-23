"""Domain port interfaces for Site Payments microservice.

Defines protocol contracts for:
1. SitePaymentLedgerPort
2. BillingAndErpGatewayPort
3. ParticipantCompensationPort

Requirements: ADR-2199, PRD-SYS-001, PRD-PAY-002, PRD-PAY-004, PRD-PAY-006
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Any

from apps.payments.domain.models import (
    ErpExportEnvelope,
    ReceiptOcrResult,
)


class SitePaymentLedgerPort(ABC):
    """Port interface for site budget contracts, fee schedules, triggers, and holdbacks."""

    @abstractmethod
    async def create_budget_contract(
        self,
        study_id: str,
        site_id: str,
        contract_name: str,
        tier_level: str = "TIER_1_ACADEMIC",
        currency: str = "USD",
        total_budget: float = 0.0,
        overhead_percentage: float = 25.0,
        withholding_percentage: float = 10.0,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> Any: ...

    @abstractmethod
    async def process_ecrf_submission_event(
        self,
        study_id: str,
        site_id: str,
        subject_id: str,
        visit_code: str,
        form_id: str,
        event_id: str,
        timestamp: Any | None = None,
        actor_id: str = "edc_system",
        user_id: str = "system",
        user_roles: str = "EDC System",
        reason_for_change: str | None = None,
    ) -> list[Any]: ...


class BillingAndErpGatewayPort(ABC):
    """Port interface for self-billing cycles, PDF remittance statements, and ERP sync."""

    @abstractmethod
    async def compile_billing_cycle(
        self,
        study_id: str,
        site_id: str,
        billing_cycle: str = "MONTHLY",
        period_start: str = "2026-08-01",
        period_end: str = "2026-08-31",
        auto_approve: bool = False,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> Any: ...

    @abstractmethod
    async def export_invoice_to_erp(
        self,
        invoice_id: str,
        erp_system: str = "SAP",
        user_id: str = "system",
        user_roles: str = "Grants Manager",
    ) -> ErpExportEnvelope: ...

    @abstractmethod
    async def process_erp_webhook(
        self,
        payload: Any,
        user_id: str = "system",
        user_roles: str = "ERP Connector",
    ) -> dict[str, Any]: ...


class ParticipantCompensationPort(ABC):
    """Port interface for participant wallets, stipends, expenses, and lay summaries."""

    @abstractmethod
    async def get_or_create_wallet(
        self,
        subject_id: str,
        study_id: str,
        site_id: str,
        currency: str = "USD",
        virtual_card_token: str | None = None,
        virtual_card_mask: str | None = None,
        virtual_card_expiry: str | None = None,
        ach_routing_number: str | None = None,
        ach_account_mask: str | None = None,
        ach_account_holder: str | None = None,
        user_id: str = "system",
        user_roles: str = "Subject",
    ) -> Any: ...

    @abstractmethod
    def extract_receipt_ocr(
        self,
        image_base64: str | None = None,
        raw_text: str | None = None,
        category_hint: str | None = None,
        filename: str | None = None,
    ) -> ReceiptOcrResult: ...

    @abstractmethod
    async def process_visit_verified_stipend(
        self,
        study_id: str,
        site_id: str,
        subject_id: str,
        visit_code: str,
        event_id: str,
        verified_by: str = "crc_coordinator",
        user_roles: str = "CRC",
        reason_for_change: str | None = None,
    ) -> dict[str, Any]: ...


__all__ = [
    "BillingAndErpGatewayPort",
    "ParticipantCompensationPort",
    "SitePaymentLedgerPort",
]
