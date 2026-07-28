"""
FastAPI application entrypoint for the Organization Directory microservice.

Provides REST APIs for Organization, Site, and Personnel (SiteStaff) directory management,
with 21 CFR Part 11 and GxP compliant append-only version history and audit trails.
"""

import os
import uuid
from datetime import datetime, timezone
from typing import List, Optional

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from organization_domain import ClinicalStaffRole, OrganizationType
from pydantic import BaseModel, ConfigDict, Field, field_validator
from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.org.database import db_manager
from apps.org.models import (
    Base,
    DelegationOfAuthority,
    Organization,
    OrgAuditLog,
    Personnel,
    Site,
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.delegation import verify_delegation_scope
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.signing import verify_canonical_signature

# Retrieve gateway secret for canonical signatures verification
GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode()

# --- Pydantic Request/Response Schemas ---


class OrganizationCreate(BaseModel):
    name: str = Field(..., description="Name of the organization")
    org_type: OrganizationType = Field(..., description="Type of the organization")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class OrganizationUpdate(BaseModel):
    name: Optional[str] = Field(None, description="Updated name of the organization")
    org_type: Optional[OrganizationType] = Field(
        None, description="Updated type of the organization"
    )
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class OrganizationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    name: str
    org_type: str
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class SiteCreate(BaseModel):
    site_id: str = Field(
        ..., description="Unique client-defined identifier for the site"
    )
    name: str = Field(..., description="Name of the site")
    organization_id: str = Field(
        ..., description="Reference to the parent organization ID"
    )
    study_id: Optional[str] = Field(None, description="Optional clinical study ID")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class SiteUpdate(BaseModel):
    site_id: Optional[str] = Field(None, description="Updated identifier for the site")
    name: Optional[str] = Field(None, description="Updated name of the site")
    organization_id: Optional[str] = Field(
        None, description="Updated reference to parent organization ID"
    )
    study_id: Optional[str] = Field(None, description="Updated clinical study ID")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class SiteResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    site_id: str
    name: str
    organization_id: str
    study_id: Optional[str] = None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class PersonnelCreate(BaseModel):
    keycloak_user_id: Optional[str] = Field(
        None, description="OIDC user ID linked to this staff member"
    )
    first_name: str = Field(..., description="First name")
    last_name: str = Field(..., description="Last name")
    email: str = Field(..., description="Unique email address")
    role: ClinicalStaffRole = Field(..., description="Clinical staff role")
    organization_id: Optional[str] = Field(
        None, description="Reference to parent organization ID"
    )
    site_id: Optional[str] = Field(None, description="Reference to parent site_id")
    study_id: Optional[str] = Field(None, description="Optional clinical study ID")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class PersonnelUpdate(BaseModel):
    keycloak_user_id: Optional[str] = Field(
        None, description="OIDC user ID linked to this staff member"
    )
    first_name: Optional[str] = Field(None, description="Updated first name")
    last_name: Optional[str] = Field(None, description="Updated last name")
    email: Optional[str] = Field(None, description="Updated email address")
    role: Optional[ClinicalStaffRole] = Field(
        None, description="Updated clinical staff role"
    )
    organization_id: Optional[str] = Field(
        None, description="Updated reference to parent organization ID"
    )
    site_id: Optional[str] = Field(
        None, description="Updated reference to parent site_id"
    )
    study_id: Optional[str] = Field(None, description="Updated clinical study ID")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class PersonnelResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    keycloak_user_id: Optional[str] = None
    first_name: str
    last_name: str
    email: str
    role: str
    organization_id: Optional[str] = None
    site_id: Optional[str] = None
    study_id: Optional[str] = None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


# Retrieve database URL from environment or default to in-memory SQLite
DATABASE_URL = os.getenv("ORG_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


app = FastAPI(
    title="Cadence Clinical - Organization Directory",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Register internal gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Standardized DB session dependency
get_db_session = DatabaseSessionDependency(db_manager)


# --- Helper Functions ---


async def write_audit_log(
    session: AsyncSession,
    actor_id: str,
    actor_role: str,
    action: str,
    record_id: Optional[str] = None,
    details: str = "",
    reason_for_change: str = "Standard Access",
) -> None:
    """
    Appends an entry to the 21 CFR Part 11 compliant OrgAuditLog.
    """
    log_entry = OrgAuditLog(
        actor_id=actor_id,
        actor_role=actor_role,
        action=action,
        record_id=record_id,
        details=details,
        reason_for_change=reason_for_change,
    )
    session.add(log_entry)
    await session.flush()


def get_user_context(request: Request):
    """
    Helper to extract user identity claims propagated by GatewayAuthMiddleware.
    """
    user_id = getattr(request.state, "user_id", "system")
    roles = getattr(request.state, "roles", [])
    user_role = ",".join(roles) if isinstance(roles, list) else str(roles)
    if not user_role:
        user_role = "system"

    change_reason = (
        getattr(request.state, "change_reason", None)
        or request.headers.get("X-Change-Reason")
        or request.headers.get("x-change-reason")
    )
    return user_id, user_role, change_reason


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Health check endpoint to verify microservice availability.

    Bypasses standard API Gateway headers validation.
    """
    return {"status": "ok", "service": "org"}


# --- Organization Endpoints ---


@app.post(
    "/api/v1/org/organizations", response_model=OrganizationResponse, status_code=201
)
async def create_organization(
    request: Request,
    payload: OrganizationCreate,
    session: AsyncSession = Depends(get_db_session),
) -> OrganizationResponse:
    """
    Create a new organization record.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    org = Organization(
        id=str(uuid.uuid4()),
        name=payload.name,
        org_type=payload.org_type.value,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(org)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="ORGANIZATION_CREATE",
        record_id=org.id,
        details=f"Created organization '{payload.name}' with type '{payload.org_type}'.",
        reason_for_change=change_reason,
    )

    return org


@app.get("/api/v1/org/organizations", response_model=List[OrganizationResponse])
async def list_organizations(
    request: Request,
    name: Optional[str] = Query(
        None, description="Filter by partial organization name"
    ),
    org_type: Optional[OrganizationType] = Query(
        None, description="Filter by organization type"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> List[OrganizationResponse]:
    """
    List all organizations. Applies filters on partial name or exact type and returns the latest version of each.
    """
    user_id, user_role, change_reason = get_user_context(request)

    # Fetch all records first to determine latest versions in-memory
    stmt = select(Organization).order_by(
        Organization.id, desc(Organization.version_index)
    )
    res = await session.execute(stmt)
    all_orgs = res.scalars().all()

    # Deduplicate keeping only latest versions
    latest_orgs = {}
    for org in all_orgs:
        if org.id not in latest_orgs:
            latest_orgs[org.id] = org

    filtered_orgs = list(latest_orgs.values())

    # Apply query filters
    if name is not None:
        name_lower = name.lower()
        filtered_orgs = [o for o in filtered_orgs if name_lower in o.name.lower()]
    if org_type is not None:
        filtered_orgs = [o for o in filtered_orgs if o.org_type == org_type.value]

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="ORGANIZATION_LIST",
        details=f"Listed organizations matching filters (name={name}, org_type={org_type}).",
        reason_for_change=change_reason or "Standard query access",
    )

    return filtered_orgs


@app.get("/api/v1/org/organizations/{id}", response_model=OrganizationResponse)
async def get_organization(
    request: Request,
    id: str,
    version_index: Optional[int] = Query(
        None, description="Optionally retrieve a specific version"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> OrganizationResponse:
    """
    Retrieve details for an organization by ID. Returns the latest version by default or a specific historical version.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Organization).where(Organization.id == id)
    if version_index is not None:
        stmt = stmt.where(Organization.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(Organization.version_index))

    res = await session.execute(stmt)
    org = res.scalars().first()

    if not org:
        raise HTTPException(status_code=404, detail="Organization not found")

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="ORGANIZATION_VIEW",
        record_id=org.id,
        details=f"Viewed organization '{org.name}' (version {org.version_index}).",
        reason_for_change=change_reason or "Standard retrieve access",
    )

    return org


@app.put("/api/v1/org/organizations/{id}", response_model=OrganizationResponse)
async def update_organization(
    request: Request,
    id: str,
    payload: OrganizationUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> OrganizationResponse:
    """
    Soft-update an existing organization, appending a new version with incremented version_index.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    stmt = (
        select(Organization)
        .where(Organization.id == id)
        .order_by(desc(Organization.version_index))
    )
    res = await session.execute(stmt)
    latest_org = res.scalars().first()

    if not latest_org:
        raise HTTPException(status_code=404, detail="Organization not found")

    new_org = Organization(
        id=id,
        name=payload.name if payload.name is not None else latest_org.name,
        org_type=payload.org_type.value
        if payload.org_type is not None
        else latest_org.org_type,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=latest_org.version_index + 1,
    )
    session.add(new_org)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="ORGANIZATION_UPDATE",
        record_id=id,
        details=f"Updated organization ID '{id}' to version {new_org.version_index}.",
        reason_for_change=change_reason,
    )

    return new_org


@app.get(
    "/api/v1/org/organizations/{id}/history", response_model=List[OrganizationResponse]
)
async def get_organization_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> List[OrganizationResponse]:
    """
    Retrieve chronological version history for an organization.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = (
        select(Organization)
        .where(Organization.id == id)
        .order_by(desc(Organization.version_index))
    )
    res = await session.execute(stmt)
    history = res.scalars().all()

    if not history:
        raise HTTPException(status_code=404, detail="Organization history not found")

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="ORGANIZATION_HISTORY",
        record_id=id,
        details=f"Retrieved history for organization ID '{id}'.",
        reason_for_change=change_reason or "Standard history query",
    )

    return history


# --- Delegation of Authority (DOA) API Models ---


class DelegationCreate(BaseModel):
    delegator_id: str = Field(
        ..., description="The Personnel ID of the delegator (typically the PI)"
    )
    delegatee_id: str = Field(..., description="The Personnel ID of the delegatee")
    site_id: str = Field(
        ..., description="The site ID where authority is being delegated"
    )
    study_id: str = Field(..., description="The study ID")
    duties: List[str] = Field(..., description="List of delegated duties")
    start_date: datetime = Field(
        ..., description="The effective start date of the delegation"
    )
    end_date: Optional[datetime] = Field(
        None, description="Optional effective end date of the delegation"
    )
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class DelegationSign(BaseModel):
    payload: dict = Field(
        ..., description="The canonical delegation payload to sign and verify"
    )
    signature: str = Field(
        ..., description="The symmetric HMAC canonical signature of the payload"
    )
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class DelegationRevoke(BaseModel):
    reason_for_change: str = Field(
        ..., description="The revocation reason / Part 11 justification"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class DelegationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    delegator_id: str
    delegatee_id: str
    site_id: str
    study_id: str
    duties: List[str]
    start_date: datetime
    end_date: Optional[datetime] = None
    is_active: bool
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int

    # Cryptographic & Signature fields
    signature: Optional[str] = None
    signed_payload: Optional[dict] = None
    signed_at: Optional[datetime] = None
    signed_by: Optional[str] = None

    # Revocation fields
    revoked_at: Optional[datetime] = None
    revoked_by: Optional[str] = None
    revocation_reason: Optional[str] = None


# --- Delegation of Authority (DOA) REST Endpoints ---


@app.post("/api/v1/org/delegations", response_model=DelegationResponse, status_code=201)
async def create_delegation(
    request: Request,
    payload: DelegationCreate,
    session: AsyncSession = Depends(get_db_session),
) -> DelegationResponse:
    """
    Grant delegation of authority. Only a Principal Investigator at the matching site may perform this operation.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    # 1. Scope/Role authorization checks using verify_delegation_scope
    verify_delegation_scope(
        request=request,
        target_site_id=payload.site_id,
        target_sponsor_id=getattr(request.state, "sponsor_id", None),
        enforce_pi=True,
    )

    # 2. Database existence and role verification for delegator and delegatee
    stmt_delegator = (
        select(Personnel)
        .where(Personnel.id == payload.delegator_id)
        .order_by(desc(Personnel.version_index))
    )
    res_delegator = await session.execute(stmt_delegator)
    delegator_p = res_delegator.scalars().first()
    if not delegator_p:
        raise HTTPException(status_code=404, detail="Delegator personnel not found")

    if delegator_p.role != "Principal Investigator":
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Delegator must be a Principal Investigator.",
        )

    stmt_delegatee = (
        select(Personnel)
        .where(Personnel.id == payload.delegatee_id)
        .order_by(desc(Personnel.version_index))
    )
    res_delegatee = await session.execute(stmt_delegatee)
    delegatee_p = res_delegatee.scalars().first()
    if not delegatee_p:
        raise HTTPException(status_code=404, detail="Delegatee personnel not found")

    # 3. Create the initial Delegation of Authority record (version 1)
    doa = DelegationOfAuthority(
        id=str(uuid.uuid4()),
        delegator_id=payload.delegator_id,
        delegatee_id=payload.delegatee_id,
        site_id=payload.site_id,
        study_id=payload.study_id,
        duties=payload.duties,
        start_date=payload.start_date,
        end_date=payload.end_date,
        is_active=True,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(doa)
    await session.flush()

    # 4. GxP Audit Trail Log Entry
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="DELEGATION_GRANT",
        record_id=doa.id,
        details=f"Granted delegation of authority (ID: {doa.id}) from delegator {delegator_p.first_name} {delegator_p.last_name} to delegatee {delegatee_p.first_name} {delegatee_p.last_name} at site {payload.site_id}.",
        reason_for_change=change_reason,
    )

    return doa


@app.post("/api/v1/org/delegations/{id}/sign-off", response_model=DelegationResponse)
async def sign_delegation(
    request: Request,
    id: str,
    payload: DelegationSign,
    session: AsyncSession = Depends(get_db_session),
) -> DelegationResponse:
    """
    Approve and electronically sign a Delegation of Authority (DOA) record.
    Requires Part 11 re-authentication (enforced via X-Sig-Token header check in gateway middleware).
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    # 1. Fetch latest delegation record
    stmt = (
        select(DelegationOfAuthority)
        .where(DelegationOfAuthority.id == id)
        .order_by(desc(DelegationOfAuthority.version_index))
    )
    res = await session.execute(stmt)
    latest_doa = res.scalars().first()
    if not latest_doa:
        raise HTTPException(
            status_code=404, detail="Delegation of Authority record not found"
        )

    # 2. Scope/Role authorization verification
    verify_delegation_scope(
        request=request,
        target_site_id=latest_doa.site_id,
        target_sponsor_id=getattr(request.state, "sponsor_id", None),
        enforce_pi=True,
    )

    # 3. Verify canonical signed payload and reject on mismatch or tampering
    is_valid = verify_canonical_signature(
        payload.payload, payload.signature, GATEWAY_SECRET
    )
    if not is_valid:
        raise HTTPException(
            status_code=400,
            detail="Signature verification failed: tampered content or invalid signature.",
        )

    # Treat the canonical signed payload as authoritative: verify that key fields match the record
    signed_data = payload.payload
    if signed_data.get("id") != latest_doa.id:
        raise HTTPException(status_code=400, detail="Signed payload 'id' mismatch")
    if signed_data.get("delegator_id") != latest_doa.delegator_id:
        raise HTTPException(
            status_code=400, detail="Signed payload 'delegator_id' mismatch"
        )
    if signed_data.get("delegatee_id") != latest_doa.delegatee_id:
        raise HTTPException(
            status_code=400, detail="Signed payload 'delegatee_id' mismatch"
        )
    if signed_data.get("site_id") != latest_doa.site_id:
        raise HTTPException(status_code=400, detail="Signed payload 'site_id' mismatch")
    if signed_data.get("study_id") != latest_doa.study_id:
        raise HTTPException(
            status_code=400, detail="Signed payload 'study_id' mismatch"
        )

    signed_duties = signed_data.get("duties") or []
    if sorted(signed_duties) != sorted(latest_doa.duties):
        raise HTTPException(status_code=400, detail="Signed payload 'duties' mismatch")

    # 4. Create new row version index incremented delegation record
    signed_doa = DelegationOfAuthority(
        id=latest_doa.id,
        delegator_id=latest_doa.delegator_id,
        delegatee_id=latest_doa.delegatee_id,
        site_id=latest_doa.site_id,
        study_id=latest_doa.study_id,
        duties=latest_doa.duties,
        start_date=latest_doa.start_date,
        end_date=latest_doa.end_date,
        is_active=latest_doa.is_active,
        signature=payload.signature,
        signed_payload=payload.payload,
        signed_at=datetime.now(timezone.utc).replace(tzinfo=None),
        signed_by=user_id,
        revoked_at=latest_doa.revoked_at,
        revoked_by=latest_doa.revoked_by,
        revocation_reason=latest_doa.revocation_reason,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=latest_doa.version_index + 1,
    )
    session.add(signed_doa)
    await session.flush()

    # 5. GxP Audit Trail Log Entry
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="DELEGATION_SIGN",
        record_id=signed_doa.id,
        details=f"Electronically signed delegation of authority record ID '{signed_doa.id}' (version {signed_doa.version_index}).",
        reason_for_change=change_reason,
    )

    return signed_doa


@app.post("/api/v1/org/delegations/{id}/revoke", response_model=DelegationResponse)
async def revoke_delegation(
    request: Request,
    id: str,
    payload: DelegationRevoke,
    session: AsyncSession = Depends(get_db_session),
) -> DelegationResponse:
    """
    Revoke an existing Delegation of Authority (DOA) record.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    # 1. Fetch latest delegation record
    stmt = (
        select(DelegationOfAuthority)
        .where(DelegationOfAuthority.id == id)
        .order_by(desc(DelegationOfAuthority.version_index))
    )
    res = await session.execute(stmt)
    latest_doa = res.scalars().first()
    if not latest_doa:
        raise HTTPException(
            status_code=404, detail="Delegation of Authority record not found"
        )

    # 2. Scope/Role authorization verification
    verify_delegation_scope(
        request=request,
        target_site_id=latest_doa.site_id,
        target_sponsor_id=getattr(request.state, "sponsor_id", None),
        enforce_pi=True,
    )

    # 3. Create new row version index incremented delegation record with is_active=False and revocation details
    revoked_doa = DelegationOfAuthority(
        id=latest_doa.id,
        delegator_id=latest_doa.delegator_id,
        delegatee_id=latest_doa.delegatee_id,
        site_id=latest_doa.site_id,
        study_id=latest_doa.study_id,
        duties=latest_doa.duties,
        start_date=latest_doa.start_date,
        end_date=latest_doa.end_date,
        is_active=False,
        signature=latest_doa.signature,
        signed_payload=latest_doa.signed_payload,
        signed_at=latest_doa.signed_at,
        signed_by=latest_doa.signed_by,
        revoked_at=datetime.now(timezone.utc).replace(tzinfo=None),
        revoked_by=user_id,
        revocation_reason=payload.reason_for_change,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=latest_doa.version_index + 1,
    )
    session.add(revoked_doa)
    await session.flush()

    # 4. GxP Audit Trail Log Entry
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="DELEGATION_REVOKE",
        record_id=revoked_doa.id,
        details=f"Revoked delegation of authority record ID '{revoked_doa.id}' (version {revoked_doa.version_index}). Reason: '{payload.reason_for_change}'",
        reason_for_change=change_reason,
    )

    return revoked_doa


@app.get("/api/v1/org/delegations", response_model=List[DelegationResponse])
async def list_delegations(
    request: Request,
    site_id: Optional[str] = Query(None, description="Filter by site_id"),
    study_id: Optional[str] = Query(None, description="Filter by study_id"),
    delegator_id: Optional[str] = Query(
        None, description="Filter by delegator personnel ID"
    ),
    delegatee_id: Optional[str] = Query(
        None, description="Filter by delegatee personnel ID"
    ),
    is_active: Optional[bool] = Query(None, description="Filter by active status"),
    session: AsyncSession = Depends(get_db_session),
) -> List[DelegationResponse]:
    """
    List all delegation records, returning only the latest version of each unique record.
    """
    user_id, user_role, change_reason = get_user_context(request)

    # Retrieve all rows ordered by version_index to extract latest in-memory
    stmt = select(DelegationOfAuthority).order_by(
        DelegationOfAuthority.id, desc(DelegationOfAuthority.version_index)
    )
    res = await session.execute(stmt)
    all_doas = res.scalars().all()

    latest_doas = {}
    for d in all_doas:
        if d.id not in latest_doas:
            latest_doas[d.id] = d

    filtered_doas = list(latest_doas.values())

    # Apply optional query filters
    if site_id is not None:
        filtered_doas = [d for d in filtered_doas if d.site_id == site_id]
    if study_id is not None:
        filtered_doas = [d for d in filtered_doas if d.study_id == study_id]
    if delegator_id is not None:
        filtered_doas = [d for d in filtered_doas if d.delegator_id == delegator_id]
    if delegatee_id is not None:
        filtered_doas = [d for d in filtered_doas if d.delegatee_id == delegatee_id]
    if is_active is not None:
        filtered_doas = [d for d in filtered_doas if d.is_active == is_active]

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="DELEGATION_LIST",
        details="Listed delegations of authority matching filters.",
        reason_for_change=change_reason or "Standard query access",
    )

    return filtered_doas


@app.get("/api/v1/org/delegations/{id}", response_model=DelegationResponse)
async def get_delegation(
    request: Request,
    id: str,
    version_index: Optional[int] = Query(
        None, description="Optionally retrieve a specific version"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> DelegationResponse:
    """
    Retrieve details of a specific Delegation of Authority record by ID (latest by default or specific historical version).
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(DelegationOfAuthority).where(DelegationOfAuthority.id == id)
    if version_index is not None:
        stmt = stmt.where(DelegationOfAuthority.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(DelegationOfAuthority.version_index))

    res = await session.execute(stmt)
    doa = res.scalars().first()
    if not doa:
        raise HTTPException(
            status_code=404, detail="Delegation of Authority record not found"
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="DELEGATION_VIEW",
        record_id=doa.id,
        details=f"Viewed delegation of authority ID '{doa.id}' (version {doa.version_index}).",
        reason_for_change=change_reason or "Standard retrieve access",
    )

    return doa


@app.get(
    "/api/v1/org/delegations/{id}/history", response_model=List[DelegationResponse]
)
async def get_delegation_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> List[DelegationResponse]:
    """
    Retrieve chronological version history for a specific Delegation of Authority record.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = (
        select(DelegationOfAuthority)
        .where(DelegationOfAuthority.id == id)
        .order_by(desc(DelegationOfAuthority.version_index))
    )
    res = await session.execute(stmt)
    history = res.scalars().all()
    if not history:
        raise HTTPException(status_code=404, detail="Delegation history not found")

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="DELEGATION_HISTORY",
        record_id=id,
        details=f"Retrieved history for delegation of authority ID '{id}'.",
        reason_for_change=change_reason or "Standard history query",
    )

    return history


class OrgAuditLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    timestamp: datetime
    actor_id: str
    actor_role: str
    action: str
    record_id: Optional[str] = None
    details: str
    reason_for_change: str


@app.get("/api/v1/org/audit-logs", response_model=List[OrgAuditLogResponse])
async def list_org_audit_logs(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[OrgAuditLogResponse]:
    """
    Retrieve organization audit logs in descending chronological order.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(OrgAuditLog).order_by(desc(OrgAuditLog.timestamp))
    res = await session.execute(stmt)
    logs = res.scalars().all()

    return logs


# --- Site Endpoints ---


@app.post("/api/v1/org/sites", response_model=SiteResponse, status_code=201)
async def create_site(
    request: Request,
    payload: SiteCreate,
    session: AsyncSession = Depends(get_db_session),
) -> SiteResponse:
    """
    Create a new site record.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    site = Site(
        id=str(uuid.uuid4()),
        site_id=payload.site_id,
        name=payload.name,
        organization_id=payload.organization_id,
        study_id=payload.study_id,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(site)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="SITE_CREATE",
        record_id=site.id,
        details=f"Created site '{payload.name}' with site_id '{payload.site_id}'.",
        reason_for_change=change_reason,
    )

    return site


@app.get("/api/v1/org/sites", response_model=List[SiteResponse])
async def list_sites(
    request: Request,
    site_id: Optional[str] = Query(None, description="Filter by site_id"),
    study_id: Optional[str] = Query(None, description="Filter by study_id"),
    organization_id: Optional[str] = Query(
        None, description="Filter by organization_id"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> List[SiteResponse]:
    """
    List all sites. Applies filters and returns the latest version of each unique site.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Site).order_by(Site.id, desc(Site.version_index))
    res = await session.execute(stmt)
    all_sites = res.scalars().all()

    # Deduplicate to latest
    latest_sites = {}
    for site in all_sites:
        if site.id not in latest_sites:
            latest_sites[site.id] = site

    filtered_sites = list(latest_sites.values())

    # Apply filters
    if site_id is not None:
        filtered_sites = [
            s for s in filtered_sites if site_id.lower() in s.site_id.lower()
        ]
    if study_id is not None:
        filtered_sites = [
            s
            for s in filtered_sites
            if s.study_id and study_id.lower() in s.study_id.lower()
        ]
    if organization_id is not None:
        filtered_sites = [
            s for s in filtered_sites if s.organization_id == organization_id
        ]

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="SITE_LIST",
        details="Listed sites matching filters.",
        reason_for_change=change_reason or "Standard query access",
    )

    return filtered_sites


@app.get("/api/v1/org/sites/{id}", response_model=SiteResponse)
async def get_site(
    request: Request,
    id: str,
    version_index: Optional[int] = Query(
        None, description="Optionally retrieve a specific version"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> SiteResponse:
    """
    Retrieve details for a site by ID. Returns latest version or specific historical version.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Site).where(Site.id == id)
    if version_index is not None:
        stmt = stmt.where(Site.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(Site.version_index))

    res = await session.execute(stmt)
    site = res.scalars().first()

    if not site:
        raise HTTPException(status_code=404, detail="Site not found")

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="SITE_VIEW",
        record_id=site.id,
        details=f"Viewed site '{site.name}' (version {site.version_index}).",
        reason_for_change=change_reason or "Standard retrieve access",
    )

    return site


@app.put("/api/v1/org/sites/{id}", response_model=SiteResponse)
async def update_site(
    request: Request,
    id: str,
    payload: SiteUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> SiteResponse:
    """
    Soft-update an existing site, appending a new version with incremented version_index.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    stmt = select(Site).where(Site.id == id).order_by(desc(Site.version_index))
    res = await session.execute(stmt)
    latest_site = res.scalars().first()

    if not latest_site:
        raise HTTPException(status_code=404, detail="Site not found")

    new_site = Site(
        id=id,
        site_id=payload.site_id if payload.site_id is not None else latest_site.site_id,
        name=payload.name if payload.name is not None else latest_site.name,
        organization_id=payload.organization_id
        if payload.organization_id is not None
        else latest_site.organization_id,
        study_id=payload.study_id
        if payload.study_id is not None
        else latest_site.study_id,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=latest_site.version_index + 1,
    )
    session.add(new_site)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="SITE_UPDATE",
        record_id=id,
        details=f"Updated site ID '{id}' to version {new_site.version_index}.",
        reason_for_change=change_reason,
    )

    return new_site


@app.get("/api/v1/org/sites/{id}/history", response_model=List[SiteResponse])
async def get_site_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> List[SiteResponse]:
    """
    Retrieve chronological version history for a site.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Site).where(Site.id == id).order_by(desc(Site.version_index))
    res = await session.execute(stmt)
    history = res.scalars().all()

    if not history:
        raise HTTPException(status_code=404, detail="Site history not found")

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="SITE_HISTORY",
        record_id=id,
        details=f"Retrieved history for site ID '{id}'.",
        reason_for_change=change_reason or "Standard history query",
    )

    return history


# --- Personnel Endpoints ---


@app.post("/api/v1/org/personnel", response_model=PersonnelResponse, status_code=201)
async def create_personnel(
    request: Request,
    payload: PersonnelCreate,
    session: AsyncSession = Depends(get_db_session),
) -> PersonnelResponse:
    """
    Create a new personnel record.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    person = Personnel(
        id=str(uuid.uuid4()),
        keycloak_user_id=payload.keycloak_user_id,
        first_name=payload.first_name,
        last_name=payload.last_name,
        email=payload.email,
        role=payload.role.value,
        organization_id=payload.organization_id,
        site_id=payload.site_id,
        study_id=payload.study_id,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(person)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_CREATE",
        record_id=person.id,
        details=f"Created personnel '{payload.first_name} {payload.last_name}' with email '{payload.email}'.",
        reason_for_change=change_reason,
    )

    return person


@app.get("/api/v1/org/personnel", response_model=List[PersonnelResponse])
async def list_personnel(
    request: Request,
    site_id: Optional[str] = Query(None, description="Filter by site_id"),
    study_id: Optional[str] = Query(None, description="Filter by study_id"),
    organization_id: Optional[str] = Query(
        None, description="Filter by organization_id"
    ),
    role: Optional[ClinicalStaffRole] = Query(
        None, description="Filter by exact staff role"
    ),
    email: Optional[str] = Query(None, description="Filter by exact or partial email"),
    session: AsyncSession = Depends(get_db_session),
) -> List[PersonnelResponse]:
    """
    List all personnel. Applies filters and returns the latest version of each unique staff member.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Personnel).order_by(Personnel.id, desc(Personnel.version_index))
    res = await session.execute(stmt)
    all_personnel = res.scalars().all()

    # Deduplicate to latest
    latest_personnel = {}
    for p in all_personnel:
        if p.id not in latest_personnel:
            latest_personnel[p.id] = p

    filtered_personnel = list(latest_personnel.values())

    # Apply filters
    if site_id is not None:
        filtered_personnel = [
            p
            for p in filtered_personnel
            if p.site_id and site_id.lower() in p.site_id.lower()
        ]
    if study_id is not None:
        filtered_personnel = [
            p
            for p in filtered_personnel
            if p.study_id and study_id.lower() in p.study_id.lower()
        ]
    if organization_id is not None:
        filtered_personnel = [
            p for p in filtered_personnel if p.organization_id == organization_id
        ]
    if role is not None:
        filtered_personnel = [p for p in filtered_personnel if p.role == role.value]
    if email is not None:
        email_lower = email.lower()
        filtered_personnel = [
            p for p in filtered_personnel if email_lower in p.email.lower()
        ]

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_LIST",
        details="Listed personnel matching filters.",
        reason_for_change=change_reason or "Standard query access",
    )

    return filtered_personnel


@app.get("/api/v1/org/personnel/{id}", response_model=PersonnelResponse)
async def get_personnel(
    request: Request,
    id: str,
    version_index: Optional[int] = Query(
        None, description="Optionally retrieve a specific version"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> PersonnelResponse:
    """
    Retrieve details for personnel by ID. Returns latest version or specific historical version.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Personnel).where(Personnel.id == id)
    if version_index is not None:
        stmt = stmt.where(Personnel.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(Personnel.version_index))

    res = await session.execute(stmt)
    person = res.scalars().first()

    if not person:
        raise HTTPException(status_code=404, detail="Personnel not found")

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_VIEW",
        record_id=person.id,
        details=f"Viewed personnel '{person.first_name} {person.last_name}' (version {person.version_index}).",
        reason_for_change=change_reason or "Standard retrieve access",
    )

    return person


@app.put("/api/v1/org/personnel/{id}", response_model=PersonnelResponse)
async def update_personnel(
    request: Request,
    id: str,
    payload: PersonnelUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> PersonnelResponse:
    """
    Soft-update an existing personnel record, appending a new version with incremented version_index.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    stmt = (
        select(Personnel)
        .where(Personnel.id == id)
        .order_by(desc(Personnel.version_index))
    )
    res = await session.execute(stmt)
    latest_person = res.scalars().first()

    if not latest_person:
        raise HTTPException(status_code=404, detail="Personnel not found")

    new_person = Personnel(
        id=id,
        keycloak_user_id=payload.keycloak_user_id
        if payload.keycloak_user_id is not None
        else latest_person.keycloak_user_id,
        first_name=payload.first_name
        if payload.first_name is not None
        else latest_person.first_name,
        last_name=payload.last_name
        if payload.last_name is not None
        else latest_person.last_name,
        email=payload.email if payload.email is not None else latest_person.email,
        role=payload.role.value if payload.role is not None else latest_person.role,
        organization_id=payload.organization_id
        if payload.organization_id is not None
        else latest_person.organization_id,
        site_id=payload.site_id
        if payload.site_id is not None
        else latest_person.site_id,
        study_id=payload.study_id
        if payload.study_id is not None
        else latest_person.study_id,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=latest_person.version_index + 1,
    )
    session.add(new_person)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_UPDATE",
        record_id=id,
        details=f"Updated personnel ID '{id}' to version {new_person.version_index}.",
        reason_for_change=change_reason,
    )

    return new_person


@app.get("/api/v1/org/personnel/{id}/history", response_model=List[PersonnelResponse])
async def get_personnel_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> List[PersonnelResponse]:
    """
    Retrieve chronological version history for personnel.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = (
        select(Personnel)
        .where(Personnel.id == id)
        .order_by(desc(Personnel.version_index))
    )
    res = await session.execute(stmt)
    history = res.scalars().all()

    if not history:
        raise HTTPException(status_code=404, detail="Personnel history not found")

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_HISTORY",
        record_id=id,
        details=f"Retrieved history for personnel ID '{id}'.",
        reason_for_change=change_reason or "Standard history query",
    )

    return history
