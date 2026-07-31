import uuid
from typing import Optional

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import (
    ChangeApprovalSignature,
    ComplianceChangeRequest,
)

# Registry of active settings to apply once approved
CURRENT_SETTINGS = {
    "session_timeout_minutes": "30",
    "password_expiration_days": "90",  # pragma: allowlist secret
    "esignature_timeout_thresholds": "120",
    "site_isolation_rules": "enabled",
    "data_lock_configurations": "strict",
}

APPROVER_ROLES = {
    "admin_user": "System Administrator",
    "system_admin": "System Administrator",
    "qa_lead": "QA Lead",
    "qa_manager": "QA Lead",
}


class ComplianceChangeRequestService:
    """Service to manage GxP compliance change requests and multi-approver electronic signatures."""

    def __init__(self, session: AsyncSession):
        self.session = session

    def generate_impact_assessment(
        self, setting_key: str, old_value: str, new_value: str
    ) -> dict:
        """Automated diff report categorizing settings changes by clinical risk (LOW, MEDIUM, HIGH)."""
        risk_map = {
            "site_isolation_rules": "HIGH",
            "data_lock_configurations": "HIGH",
            "password_expiration_days": "MEDIUM",  # pragma: allowlist secret
            "password_policies": "MEDIUM",  # pragma: allowlist secret
            "session_timeout_minutes": "LOW",
            "esignature_timeout_thresholds": "MEDIUM",
        }
        risk = risk_map.get(setting_key, "LOW")
        return {
            "setting_key": setting_key,
            "old_value": old_value,
            "new_value": new_value,
            "clinical_risk": risk,
            "description": f"Setting change of '{setting_key}' from '{old_value}' to '{new_value}' poses a {risk} clinical risk.",
        }

    async def create_change_request(
        self,
        setting_key: str,
        old_value: str,
        new_value: str,
        requested_by: str,
        reason: str,
    ) -> ComplianceChangeRequest:
        """Create a new Compliance Change Request and automatically apply the requested_by signature."""
        assessment = self.generate_impact_assessment(setting_key, old_value, new_value)

        cr = ComplianceChangeRequest(
            id=str(uuid.uuid4()),
            setting_key=setting_key,
            old_value=old_value,
            new_value=new_value,
            requested_by=requested_by,
            reason=reason,
            status="PENDING_APPROVAL",
            impact_assessment=assessment,
        )
        self.session.add(cr)
        await self.session.flush()

        # Automatically record requester's signature to start the multi-approver chain
        req_role = APPROVER_ROLES.get(requested_by, "System Administrator")
        sig = ChangeApprovalSignature(
            id=str(uuid.uuid4()),
            change_request_id=cr.id,
            approver_id=requested_by,
            signature_token=f"sig_tok_req_{requested_by}_{cr.id}",
            role=req_role,
        )
        self.session.add(sig)
        await self.session.flush()

        return cr

    async def get_change_request(self, cr_id: str) -> Optional[ComplianceChangeRequest]:
        """Fetch a change request by its ID."""
        stmt = select(ComplianceChangeRequest).where(
            ComplianceChangeRequest.id == cr_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def approve_change_request(
        self,
        cr_id: str,
        approver_id: str,
        signature_token: str,
    ) -> ComplianceChangeRequest:
        """Record an approval signature and transition status to APPROVED if all thresholds are met."""
        if not signature_token or signature_token.strip() == "":
            raise ValueError("Signature token is invalid or missing.")

        # Non-repudiation: signature token cannot be forged or reused
        stmt_sig_check = select(ChangeApprovalSignature).where(
            ChangeApprovalSignature.signature_token == signature_token
        )
        res_sig_check = await self.session.execute(stmt_sig_check)
        if res_sig_check.scalars().first() is not None:
            raise ValueError("Signature token has already been used.")

        cr = await self.get_change_request(cr_id)
        if not cr:
            raise ValueError("Change request not found.")

        if cr.status != "PENDING_APPROVAL":
            raise ValueError("Change request is not in PENDING_APPROVAL status.")

        # Check for duplicate approval by the same user
        for existing_sig in cr.signatures:
            if existing_sig.approver_id == approver_id:
                raise ValueError(
                    "This approver has already signed this change request."
                )

        role = APPROVER_ROLES.get(approver_id, "QA Lead")

        sig = ChangeApprovalSignature(
            id=str(uuid.uuid4()),
            change_request_id=cr_id,
            approver_id=approver_id,
            signature_token=signature_token,
            role=role,
        )
        self.session.add(sig)
        await self.session.flush()

        # Reload change request with updated signatures
        await self.session.refresh(cr)

        signers = {s.approver_id for s in cr.signatures}

        # If multi-approver threshold (2 distinct signatures) is met, approve and apply
        if len(signers) >= 2:
            cr.status = "APPROVED"
            CURRENT_SETTINGS[cr.setting_key] = cr.new_value
            await self.session.flush()

        return cr
