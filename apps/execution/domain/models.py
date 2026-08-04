from datetime import datetime

from pydantic import BaseModel


class ExecutionStaffEntity(BaseModel):
    id: str | None = None
    site_id: str
    staff_user_id: str
    name: str
    email: str
    has_gcp_training: bool


class ExecutionDelegationEntity(BaseModel):
    id: str | None = None
    site_id: str
    staff_user_id: str
    task_code: str
    pi_user_id: str | None = None
    status: str
    pi_signature_hash: str | None = None
    pi_approved_at: datetime | None = None
    end_date: datetime | None = None
    reason_for_change: str | None = None
    is_active: bool


class ExecutionAuditLogEntity(BaseModel):
    id: str | None = None
    user_id: str | None = None
    action: str
    details: str
    timestamp: datetime
