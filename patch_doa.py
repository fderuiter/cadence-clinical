with open("apps/execution/routers/doa.py") as f:
    lines = f.readlines()

imports = """
from datetime import datetime, UTC
import hashlib
from sqlalchemy import select
from apps.execution.database.core import db_manager
from apps.execution.database.models import DOADelegationRecord, DOAAuditLog, SiteStaffMember

class DelegateTaskRequest(BaseModel):
    site_id: str
    staff_user_id: str
    task_code: str
    pi_user_id: str
    reason_for_change: str

class ApproveDelegationRequest(BaseModel):
    delegation_id: str
    password: str
    totp_code: str | None = None
    pi_user_id: str

class ApproveTaskDelegationRequest(BaseModel):
    delegation_id: str
    signature_hash: str
    reason_for_change: str
    pi_user_id: str

class RevokeDelegationRequest(BaseModel):
    delegation_id: str
    end_date: datetime
    reason_for_change: str

class SiteStaffMemberRequest(BaseModel):
    staff_user_id: str
    site_id: str
    name: str
    email: str
    has_gcp_training: bool

class DOADelegationRecordResponse(BaseModel):
    id: str
    pass

class SiteStaffMemberResponse(BaseModel):
    pass

class DOAAuditLogResponse(BaseModel):
    pass
"""
# Insert after BaseModel, Field import
idx = 0
for i, line in enumerate(lines):
    if "from pydantic import BaseModel, Field" in line:
        idx = i + 1
        break

lines.insert(idx, imports)

with open("apps/execution/routers/doa.py", "w") as f:
    f.writelines(lines)
