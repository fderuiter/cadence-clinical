"""Domain port interfaces for Site Payments.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

from abc import ABC, abstractmethod
from typing import Any


class IPaymentsRepository(ABC):
    """Port interface defining persistence operations for Site Payments."""

    @abstractmethod
    async def get_active_contract(self, study_id: str, site_id: str) -> Any | None: ...

    @abstractmethod
    async def get_contract_by_id(self, contract_id: str) -> Any | None: ...

    @abstractmethod
    async def save_contract(self, contract: Any) -> Any: ...

    @abstractmethod
    async def list_contracts(
        self, study_id: str | None = None, site_id: str | None = None
    ) -> list[Any]: ...

    @abstractmethod
    async def save_fee_schedule(self, fee_schedule: Any) -> Any: ...

    @abstractmethod
    async def list_fee_schedules(
        self,
        contract_id: str,
        visit_code: str | None = None,
        form_id: str | None = None,
    ) -> list[Any]: ...

    @abstractmethod
    async def save_split_payees(self, split_payees: list[Any]) -> list[Any]: ...

    @abstractmethod
    async def list_split_payees(self, contract_id: str) -> list[Any]: ...

    @abstractmethod
    async def save_payable(self, payable: Any) -> Any: ...

    @abstractmethod
    async def get_payable_by_id(self, payable_id: str) -> Any | None: ...

    @abstractmethod
    async def get_payable_by_event_id(self, event_id: str) -> Any | None: ...

    @abstractmethod
    async def list_payables(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        subject_id: str | None = None,
        visit_code: str | None = None,
        status: str | None = None,
        is_sdv_gated: bool | None = None,
    ) -> list[Any]: ...

    @abstractmethod
    async def save_payable_allocations(self, allocations: list[Any]) -> list[Any]: ...

    @abstractmethod
    async def save_holdback_reserve(self, holdback: Any) -> Any: ...

    @abstractmethod
    async def list_holdback_reserves(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        status: str | None = None,
    ) -> list[Any]: ...

    @abstractmethod
    async def record_audit_log(
        self,
        entity_type: str,
        entity_id: str,
        action: str,
        user_id: str,
        user_roles: str,
        previous_state: dict[str, Any] | None = None,
        new_state: dict[str, Any] | None = None,
        details: str | None = None,
        reason_for_change: str | None = None,
        version_index: int = 1,
    ) -> Any: ...

    @abstractmethod
    async def list_audit_logs(
        self,
        entity_type: str | None = None,
        entity_id: str | None = None,
    ) -> list[Any]: ...
