from pydantic import BaseModel


class CTMSDelegationEntity(BaseModel):
    id: str | None = None
    site_id: str
    staff_user_id: str
    task_codes: list[str]
    start_date: str
    end_date: str | None = None
    is_active: bool = False
    signed_off: bool = False
    created_by: str
    reason_for_change: str
    version_index: int = 1


class CTMSAuditLogEntity(BaseModel):
    id: str | None = None
    user_id: str
    user_role: str
    action: str
    details: str
    timestamp: str  # ISO string
