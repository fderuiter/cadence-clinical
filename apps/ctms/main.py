import os
from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator, Dict, List, Optional

from fastapi import Depends, FastAPI, HTTPException, Request
from pydantic import BaseModel, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.ctms.database import db_manager
from apps.ctms.models import Base, CTMSAuditLog, CTMSStudy, write_audit_log
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("CTMS_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """
    Handle the lifespan events for the CTMS application.

    Initializes the database session manager on startup and securely
    cleans up connections on shutdown. Creates all tables if sqlite is used.
    """
    db_manager.init_db(DATABASE_URL)

    # Automatically create tables for sqlite in-memory/file databases
    if DATABASE_URL.startswith("sqlite"):
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

    yield

    await db_manager.close()


app = FastAPI(
    title="Cadence Clinical - CTMS",
    version="0.1.0",
    lifespan=lifespan,
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


# Dependable to obtain database session
async def get_db_session() -> AsyncGenerator[AsyncSession, None]:
    """
    Dependency to yield an asynchronous database session.
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        try:
            yield session
            await session.commit()
        except Exception:
            await session.rollback()
            raise


# Helper to check roles case-insensitively
def check_roles(roles_str: str, allowed_roles: List[str]) -> None:
    roles_list = [r.strip().lower() for r in roles_str.split(",") if r.strip()]
    if not any(r in allowed_roles for r in roles_list):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied for roles: {roles_str}.",
        )


# Pydantic models for CTMS
class CTMSStudyCreate(BaseModel):
    study_id: str = Field(..., description="Unique clinical study ID")
    name: str = Field(..., description="Descriptive name of the clinical study")
    status: Optional[str] = Field("ACTIVE", description="Initial status of the study")


class CTMSStudyResponse(BaseModel):
    id: str
    study_id: str
    name: str
    status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class CTMSAuditLogResponse(BaseModel):
    id: str
    timestamp: str
    user_id: str
    user_role: str
    action: str
    details: str


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "ctms"}


@app.post("/api/v1/ctms/studies", response_model=CTMSStudyResponse, status_code=201)
async def create_study(
    request: Request,
    payload: CTMSStudyCreate,
    session: AsyncSession = Depends(get_db_session),
) -> CTMSStudyResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    # RBAC: Only write-privileged roles can manage CTMS studies
    check_roles(
        user_roles,
        ["monitor", "grants manager", "cra", "sponsor admin", "admin", "system"],
    )

    study = CTMSStudy(
        study_id=payload.study_id,
        name=payload.name,
        status=payload.status or "ACTIVE",
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(study)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_STUDY",
        details=f"Created CTMS study '{payload.study_id}' with name '{payload.name}'. Reason: {change_reason}",
    )

    return CTMSStudyResponse(
        id=study.id,
        study_id=study.study_id,
        name=study.name,
        status=study.status,
        created_at=study.created_at.isoformat(),
        created_by=study.created_by,
        reason_for_change=study.reason_for_change,
        version_index=study.version_index,
    )


@app.get("/api/v1/ctms/studies", response_model=List[CTMSStudyResponse])
async def list_studies(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[CTMSStudyResponse]:
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        [
            "monitor",
            "grants manager",
            "cra",
            "site investigator",
            "sponsor admin",
            "admin",
            "auditor",
            "anonymous",
            "system",
        ],
    )

    stmt = select(CTMSStudy).order_by(CTMSStudy.created_at.desc())
    result = await session.execute(stmt)
    studies = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_STUDIES",
        details="Listed all CTMS studies.",
    )

    return [
        CTMSStudyResponse(
            id=s.id,
            study_id=s.study_id,
            name=s.name,
            status=s.status,
            created_at=s.created_at.isoformat(),
            created_by=s.created_by,
            reason_for_change=s.reason_for_change,
            version_index=s.version_index,
        )
        for s in studies
    ]


@app.get("/api/v1/ctms/audit-logs", response_model=List[CTMSAuditLogResponse])
async def get_audit_trail(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[CTMSAuditLogResponse]:
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        ["auditor", "sponsor admin", "admin", "monitor", "grants manager", "system"],
    )

    # Log view action first
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="VIEW_AUDIT_LOGS",
        details="Accessed CTMS audit logs.",
    )

    stmt = select(CTMSAuditLog).order_by(CTMSAuditLog.timestamp.desc())
    result = await session.execute(stmt)
    logs = result.scalars().all()

    return [
        CTMSAuditLogResponse(
            id=l.id,
            timestamp=l.timestamp.isoformat(),
            user_id=l.user_id,
            user_role=l.user_role,
            action=l.action,
            details=l.details,
        )
        for l in logs
    ]
