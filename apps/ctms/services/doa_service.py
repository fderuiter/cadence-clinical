"""Delegation of Authority (DOA) log sign-off and task delegation service.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import os
from datetime import datetime

import httpx

from packages.security.gateway_client import create_service_auth_headers

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
        self.pi_approved_at = (
            datetime.fromisoformat(pi_approved_at_raw.replace("Z", "+00:00"))
            if pi_approved_at_raw
            else None
        )

        self.pi_signature_hash = data.get("pi_signature_hash")

        end_date_raw = data.get("end_date")
        self.end_date = (
            datetime.fromisoformat(end_date_raw.replace("Z", "+00:00"))
            if end_date_raw
            else None
        )


async def delegate_task(
    session,
    site_id: str,
    staff_user_id: str,
    task_code: str,
    pi_user_id: str,
    reason_for_change: str,
) -> DOADelegationRecordObj:
    """Delegate a clinical trial task to a staff member.

    Verifies the staff member is trained and creates a pending record.
    """
    headers = _get_auth_headers()
    payload = {
        "site_id": site_id,
        "staff_user_id": staff_user_id,
        "task_code": task_code,
        "pi_user_id": pi_user_id,
        "reason_for_change": reason_for_change,
    }
    async with httpx.AsyncClient() as client:
        response = await client.post(
            f"{EXECUTION_URL}/api/v1/execution/doa/delegate",
            headers=headers,
            json=payload,
        )
        if response.status_code == 400:
            raise ValueError(response.json().get("detail", "Error delegating task"))
        if response.status_code != 200:
            raise ValueError(f"HTTP error {response.status_code}: {response.text}")
        return DOADelegationRecordObj(response.json())


async def approve_delegation_with_esignature(
    session,
    delegation_id: str,
    pi_user_id: str,
    password: str,
    totp_code: str | None = None,
) -> DOADelegationRecordObj:
    """Approve a pending task delegation with PI 21 CFR Part 11 eSignature."""
    headers = _get_auth_headers()
    payload = {
        "delegation_id": delegation_id,
        "pi_user_id": pi_user_id,
        "password": password,
        "totp_code": totp_code,
    }
    async with httpx.AsyncClient() as client:
        response = await client.post(
            f"{EXECUTION_URL}/api/v1/execution/doa/endorse",
            headers=headers,
            json=payload,
        )
        if response.status_code == 400:
            raise ValueError(
                response.json().get("detail", "Error approving delegation")
            )
        if response.status_code != 200:
            raise ValueError(f"HTTP error {response.status_code}: {response.text}")
        return DOADelegationRecordObj(response.json())


async def revoke_delegation(
    session,
    delegation_id: str,
    end_date: datetime,
    reason_for_change: str,
) -> DOADelegationRecordObj:
    """Revoke a task delegation record and mark its end date."""
    headers = _get_auth_headers()
    payload = {
        "delegation_id": delegation_id,
        "end_date": end_date.isoformat(),
        "reason_for_change": reason_for_change,
    }
    async with httpx.AsyncClient() as client:
        response = await client.post(
            f"{EXECUTION_URL}/api/v1/execution/doa/revoke",
            headers=headers,
            json=payload,
        )
        if response.status_code == 400:
            raise ValueError(response.json().get("detail", "Error revoking delegation"))
        if response.status_code != 200:
            raise ValueError(f"HTTP error {response.status_code}: {response.text}")
        return DOADelegationRecordObj(response.json())


class DOAManagerService:
    """DOAManagerService provides class-based interface to DOA task delegation.

    Requirements: PRD-SYS-001
    """

    def __init__(self, session):
        """Initialize service with session."""
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
        headers = _get_auth_headers()
        payload = {
            "delegation_id": delegation_id,
            "pi_user_id": pi_user_id,
            "signature_hash": signature_hash,
            "reason_for_change": reason_for_change,
        }
        async with httpx.AsyncClient() as client:
            response = await client.post(
                f"{EXECUTION_URL}/api/v1/execution/doa/endorse_task",
                headers=headers,
                json=payload,
            )
            if response.status_code == 400:
                raise ValueError(
                    response.json().get("detail", "Error approving task delegation")
                )
            if response.status_code != 200:
                raise ValueError(f"HTTP error {response.status_code}: {response.text}")
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
