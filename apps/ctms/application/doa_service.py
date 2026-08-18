"""Delegation of Authority (DOA) log sign-off and task delegation service.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import os
from datetime import UTC, datetime
from typing import Any

from packages.security.gateway_client import (
    GatewayBaseClient,
    create_service_auth_headers,
)

EXECUTION_URL = (os.getenv("EXECUTION_URL") or "http://localhost:8002").rstrip("/")


def _get_auth_headers() -> dict[str, str]:
    return create_service_auth_headers(user_id="ctms-service")


class DOADelegationRecordObj:
    def __init__(self, data: dict):
        self.id = data.get("id")
        self.site_id = data.get("site_id")
        self.staff_user_id = data.get("staff_user_id")
        self.task_code = data.get("task_code")
        self.status = data.get("status")
        self.pi_user_id = data.get("pi_user_id")
        self.reason_for_change = data.get("reason_for_change")
        self.is_active = data.get("is_active")

        pi_approved_at_raw = data.get("pi_approved_at")
        if pi_approved_at_raw:
            if isinstance(pi_approved_at_raw, str):
                val = datetime.fromisoformat(pi_approved_at_raw.replace("Z", "+00:00"))
                if val.tzinfo is None:
                    val = val.replace(tzinfo=UTC)
                self.pi_approved_at = val
            else:
                self.pi_approved_at = pi_approved_at_raw
        else:
            self.pi_approved_at = None

        self.pi_signature_hash = data.get("pi_signature_hash")

        end_date_raw = data.get("end_date")
        if end_date_raw:
            if isinstance(end_date_raw, str):
                val = datetime.fromisoformat(end_date_raw.replace("Z", "+00:00"))
                if val.tzinfo is None:
                    val = val.replace(tzinfo=UTC)
                self.end_date = val
            else:
                self.end_date = end_date_raw
        else:
            self.end_date = None


async def delegate_task(
    session: Any,
    site_id: str,
    staff_user_id: str,
    task_code: str,
    pi_user_id: str,
    reason_for_change: str,
) -> DOADelegationRecordObj:
    """Delegate a clinical trial task to a staff member.

    Verifies the staff member is trained and creates a pending record.
    """
    client = GatewayBaseClient(base_url=EXECUTION_URL)
    response = await client.request(
        method="POST",
        path="/api/v1/execution/doa/delegate",
        user_id="ctms-service",
        roles="system",
        change_reason=reason_for_change,
        json={
            "site_id": site_id,
            "staff_user_id": staff_user_id,
            "task_code": task_code,
            "pi_user_id": pi_user_id,
            "reason_for_change": reason_for_change,
        },
    )
    if response.status_code >= 400:
        try:
            detail = response.json().get("detail", "Error")
        except Exception:
            detail = response.text or "Error"
        raise ValueError(detail)

    return DOADelegationRecordObj(response.json())


async def approve_delegation_with_esignature(
    session: Any,
    delegation_id: str,
    pi_user_id: str,
    password: str,
    totp_code: str | None = None,
) -> DOADelegationRecordObj:
    """Approve a pending task delegation with PI 21 CFR Part 11 eSignature."""
    client = GatewayBaseClient(base_url=EXECUTION_URL)
    response = await client.request(
        method="POST",
        path="/api/v1/execution/doa/endorse",
        user_id="ctms-service",
        roles="system",
        change_reason="PI Delegation Approval",
        json={
            "delegation_id": delegation_id,
            "pi_user_id": pi_user_id,
            "password": password,
            "totp_code": totp_code,
        },
    )
    if response.status_code >= 400:
        try:
            detail = response.json().get("detail", "Error")
        except Exception:
            detail = response.text or "Error"
        raise ValueError(detail)

    return DOADelegationRecordObj(response.json())


async def revoke_delegation(
    session: Any,
    delegation_id: str,
    end_date: datetime,
    reason_for_change: str,
) -> DOADelegationRecordObj:
    """Revoke a task delegation record and mark its end date."""
    client = GatewayBaseClient(base_url=EXECUTION_URL)
    response = await client.request(
        method="POST",
        path="/api/v1/execution/doa/revoke",
        user_id="ctms-service",
        roles="system",
        change_reason=reason_for_change,
        json={
            "delegation_id": delegation_id,
            "end_date": end_date.isoformat(),
            "reason_for_change": reason_for_change,
        },
    )
    if response.status_code >= 400:
        try:
            detail = response.json().get("detail", "Error")
        except Exception:
            detail = response.text or "Error"
        raise ValueError(detail)

    return DOADelegationRecordObj(response.json())


class DOAManagerService:
    """DOAManagerService provides class-based interface to DOA task delegation.

    Requirements: PRD-SYS-001
    """

    def __init__(self, session: Any):
        """Initialize service with an active AsyncSession."""
        self.session = session

    async def delegate_task(
        self,
        site_id: str,
        staff_user_id: str,
        task_code: str,
        pi_user_id: str,
        reason_for_change: str,
    ) -> DOADelegationRecordObj:
        """Delegate a task using this service's session."""
        return await delegate_task(
            session=self.session,
            site_id=site_id,
            staff_user_id=staff_user_id,
            task_code=task_code,
            pi_user_id=pi_user_id,
            reason_for_change=reason_for_change,
        )

    async def approve_delegation_with_esignature(
        self,
        delegation_id: str,
        pi_user_id: str,
        password: str,
        totp_code: str | None = None,
    ) -> DOADelegationRecordObj:
        """Approve a delegation with eSignature using this service's session."""
        return await approve_delegation_with_esignature(
            session=self.session,
            delegation_id=delegation_id,
            pi_user_id=pi_user_id,
            password=password,
            totp_code=totp_code,
        )

    async def approve_task_delegation(
        self,
        delegation_id: str,
        pi_user_id: str,
        signature_hash: str,
        reason_for_change: str,
    ) -> DOADelegationRecordObj:
        """Approve site staff task delegation via 21 CFR Part 11 electronic signature.

        Requirements: PRD-SYS-001
        """
        client = GatewayBaseClient(base_url=EXECUTION_URL)
        response = await client.request(
            method="POST",
            path="/api/v1/execution/doa/endorse_task",
            user_id="ctms-service",
            roles="system",
            change_reason=reason_for_change,
            json={
                "delegation_id": delegation_id,
                "pi_user_id": pi_user_id,
                "signature_hash": signature_hash,
                "reason_for_change": reason_for_change,
            },
        )
        if response.status_code >= 400:
            try:
                detail = response.json().get("detail", "Error")
            except Exception:
                detail = response.text or "Error"
            raise ValueError(detail)

        return DOADelegationRecordObj(response.json())

    async def revoke_delegation(
        self,
        delegation_id: str,
        end_date: datetime,
        reason_for_change: str,
    ) -> DOADelegationRecordObj:
        """Revoke a task delegation using this service's session."""
        return await revoke_delegation(
            session=self.session,
            delegation_id=delegation_id,
            end_date=end_date,
            reason_for_change=reason_for_change,
        )
