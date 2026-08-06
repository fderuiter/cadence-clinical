"""
FastAPI application entrypoint for the Organization Directory microservice.

Provides REST APIs for Organization, Site, and Personnel (SiteStaff) directory management,
with 21 CFR Part 11 and GxP compliant append-only version history and audit trails.
"""

import os
import uuid
from datetime import UTC, datetime
from typing import Any

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
    PersonnelAssignment,
    Site,
    TrainingLog,
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.delegation import verify_delegation_scope
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import Principal, require_permission
from packages.security.signing import (
    generate_gateway_signature,
    verify_canonical_signature,
)

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
    name: str | None = Field(None, description="Updated name of the organization")
    org_type: OrganizationType | None = Field(
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


class PersonnelAssignmentCreate(BaseModel):
    site_id: str = Field(..., description="The clinical site ID")
    study_id: str = Field(..., description="The clinical study ID")
    is_active: bool = Field(True, description="Whether the assignment is active")
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


class PersonnelAssignmentUpdate(BaseModel):
    site_id: str | None = Field(None, description="Updated clinical site ID")
    study_id: str | None = Field(None, description="Updated clinical study ID")
    is_active: bool | None = Field(None, description="Updated active status")
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


class PersonnelAssignmentResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    personnel_id: str
    site_id: str
    study_id: str
    is_active: bool
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class AssignmentResolutionResponse(BaseModel):
    personnel_id: str
    roles: list[str]
    assigned_sites: list[str]
    assigned_studies: list[str]


class SiteCreate(BaseModel):
    site_id: str = Field(
        ..., description="Unique client-defined identifier for the site"
    )
    name: str = Field(..., description="Name of the site")
    organization_id: str = Field(
        ..., description="Reference to the parent organization ID"
    )
    study_id: str | None = Field(None, description="Optional clinical study ID")
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
    site_id: str | None = Field(None, description="Updated identifier for the site")
    name: str | None = Field(None, description="Updated name of the site")
    organization_id: str | None = Field(
        None, description="Updated reference to parent organization ID"
    )
    study_id: str | None = Field(None, description="Updated clinical study ID")
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
    study_id: str | None = None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class PersonnelCreate(BaseModel):
    keycloak_user_id: str | None = Field(
        None, description="OIDC user ID linked to this staff member"
    )
    first_name: str = Field(..., description="First name")
    last_name: str = Field(..., description="Last name")
    email: str = Field(..., description="Unique email address")
    role: ClinicalStaffRole = Field(..., description="Clinical staff role")
    organization_id: str | None = Field(
        None, description="Reference to parent organization ID"
    )
    site_id: str | None = Field(None, description="Reference to parent site_id")
    study_id: str | None = Field(None, description="Optional clinical study ID")
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
    keycloak_user_id: str | None = Field(
        None, description="OIDC user ID linked to this staff member"
    )
    first_name: str | None = Field(None, description="Updated first name")
    last_name: str | None = Field(None, description="Updated last name")
    email: str | None = Field(None, description="Updated email address")
    role: ClinicalStaffRole | None = Field(
        None, description="Updated clinical staff role"
    )
    organization_id: str | None = Field(
        None, description="Updated reference to parent organization ID"
    )
    site_id: str | None = Field(None, description="Updated reference to parent site_id")
    study_id: str | None = Field(None, description="Updated clinical study ID")
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
    keycloak_user_id: str | None = None
    first_name: str
    last_name: str
    email: str
    role: str
    organization_id: str | None = None
    site_id: str | None = None
    study_id: str | None = None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class TrainingLogCreate(BaseModel):
    personnel_id: str = Field(..., description="The clinical personnel ID")
    site_id: str = Field(..., description="The clinical site ID")
    study_id: str = Field(..., description="The clinical study ID")
    training_topic: str = Field(
        ..., description="The training topic or certification name"
    )
    completion_date: datetime = Field(
        ..., description="Date when training was completed"
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


class TrainingLogUpdate(BaseModel):
    personnel_id: str | None = Field(None, description="Updated personnel ID")
    site_id: str | None = Field(None, description="Updated site ID")
    study_id: str | None = Field(None, description="Updated study ID")
    training_topic: str | None = Field(None, description="Updated training topic")
    completion_date: datetime | None = Field(
        None, description="Updated completion date"
    )
    reason_for_change: str | None = Field(
        None, description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str | None) -> str | None:
        if v is not None and not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class TrainingLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    personnel_id: str
    site_id: str
    study_id: str
    training_topic: str
    completion_date: datetime
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int
    signature_manifestation: dict[str, Any] | None = None
    signer: str | None = None
    signing_timestamp: datetime | None = None


class TrainingLogSignRequest(BaseModel):
    payload: dict[str, Any] = Field(
        ..., description="The canonical training log payload to sign"
    )
    signature: str = Field(
        ..., description="The HMAC-SHA256 signature validating payload integrity"
    )
    reason_for_change: str = Field(
        ..., description="The justification reason for the sign-off action"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


# Retrieve database URL from environment or default to in-memory SQLite
DATABASE_URL = os.getenv("ORG_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


import os
import sys

BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")

def validate_branding_and_domain() -> None:
    app_env = os.getenv("APP_ENV", "").strip().lower()
    is_prod_or_staging = app_env not in ("development", "dev", "test", "")
    if is_prod_or_staging:
        invalid = []
        if not os.getenv("BRAND_NAME") or os.getenv("BRAND_NAME") == "Cadence Clinical":
            invalid.append("BRAND_NAME")
        if not os.getenv("BRAND_DOMAIN") or os.getenv("BRAND_DOMAIN") == "cadenceclinical.com":
            invalid.append("BRAND_DOMAIN")
        if invalid:
            error_msg = f"STARTUP ERROR: Outdated default 'Cadence' branding or missing secure configurations detected in environment '{app_env}' for variables: {', '.join(invalid)}. Halting boot sequence."
            print(error_msg, file=sys.stderr)
            sys.exit(1)

validate_branding_and_domain()


app = FastAPI(
    title=f"{BRAND_NAME} - Organization Directory",
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
    record_id: str | None = None,
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


@app.get("/api/v1/org/organizations", response_model=list[OrganizationResponse])
async def list_organizations(
    request: Request,
    name: str | None = Query(None, description="Filter by partial organization name"),
    org_type: OrganizationType | None = Query(
        None, description="Filter by organization type"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> list[OrganizationResponse]:
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
    version_index: int | None = Query(
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
    "/api/v1/org/organizations/{id}/history", response_model=list[OrganizationResponse]
)
async def get_organization_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[OrganizationResponse]:
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
    duties: list[str] = Field(..., description="List of delegated duties")
    start_date: datetime = Field(
        ..., description="The effective start date of the delegation"
    )
    end_date: datetime | None = Field(
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
    duties: list[str]
    start_date: datetime
    end_date: datetime | None = None
    is_active: bool
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int

    # Cryptographic & Signature fields
    signature: str | None = None
    signed_payload: dict | None = None
    signed_at: datetime | None = None
    signed_by: str | None = None

    # Revocation fields
    revoked_at: datetime | None = None
    revoked_by: str | None = None
    revocation_reason: str | None = None


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


async def archive_signed_doa_to_eisf(
    doa: DelegationOfAuthority,
    change_reason: str,
    request: Request,
) -> None:
    """
    Asynchronously and durably hand off a finalized/signed Delegation of Authority record
    to the eISF service for archiving. Preserves signature, payload, and audit provenance.
    """
    import json
    import logging
    import os
    import time

    import httpx

    from packages.security.signing import generate_gateway_signature

    logger = logging.getLogger("org.archival")

    # Retrieve eISF Service Ingest Endpoint
    # Prioritize EISF_URL environment variable, fallback to default interop port/endpoint
    eisf_url = (
        os.getenv("EISF_URL") or os.getenv("INTEROP_URL") or "http://localhost:8004"
    )
    ingest_endpoint = f"{eisf_url}/api/v1/eisf/ingest"

    # Assemble preserved signed DOA payload contents
    payload_content = {
        "doa_id": doa.id,
        "delegator_id": doa.delegator_id,
        "delegatee_id": doa.delegatee_id,
        "delegated_duties": doa.duties,
        "start_date": doa.start_date.isoformat()
        if isinstance(doa.start_date, datetime)
        else str(doa.start_date),
        "end_date": doa.end_date.isoformat()
        if (doa.end_date and isinstance(doa.end_date, datetime))
        else (str(doa.end_date) if doa.end_date else None),
        "signed_payload": doa.signed_payload,
        "signature": doa.signature,
        "signed_by": doa.signed_by,
        "signed_at": doa.signed_at.isoformat()
        if isinstance(doa.signed_at, datetime)
        else (str(doa.signed_at) if doa.signed_at else None),
        "audit_provenance": {
            "created_by": doa.created_by,
            "created_at": doa.created_at.isoformat()
            if isinstance(doa.created_at, datetime)
            else str(doa.created_at),
            "reason_for_change": doa.reason_for_change,
            "version_index": doa.version_index,
        },
    }

    # Build compliant EISFIngestionRequest payload
    # Standard-versus-Extension Policy uses standard code 05.02.04 for Delegation of Authority Log
    ingest_payload = {
        "study_id": doa.study_id,
        "site_id": doa.site_id,
        "binder_classification": "Delegation of Authority Log",
        "filename": f"signed_doa_{doa.id}.json",
        "content": json.dumps(payload_content, indent=2),
        "mime_type": "application/json",
        "metadata_json": {
            "artifact_type": "Delegation of Authority Log",
            "artifact_code": "05.02.04",
            "doa_id": doa.id,
        },
        "source_system": "Organization Directory",
        "reason_for_change": f"Finalized and signed Delegation of Authority Log archival for DOA {doa.id}",
    }

    # Generate Gateway V2 signature using service token
    user_id = "org_directory_service"
    roles = "admin"
    timestamp = str(time.time())
    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode()
    gateway_reason = "DOA automatic archival to eISF"

    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=gateway_reason,
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": gateway_reason,
    }

    try:
        async with httpx.AsyncClient() as client:
            resp = await client.post(
                ingest_endpoint,
                json=ingest_payload,
                headers=headers,
                timeout=10.0,
            )
            if resp.status_code not in (200, 201):
                logger.warning(
                    f"Failed to archive DOA {doa.id} to eISF. "
                    f"Endpoint returned status code {resp.status_code}: {resp.text}"
                )
            else:
                logger.info(f"Successfully archived signed DOA {doa.id} to eISF.")
    except Exception as e:
        # GxP compliance guidelines specify to log failures without crashing/blocking parent transactions
        logger.error(
            f"Transport failure while archiving signed DOA {doa.id} to eISF: {str(e)}"
        )


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
        signed_at=datetime.now(UTC).replace(tzinfo=None),
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

    # Durable handoff to archive to eISF
    await archive_signed_doa_to_eisf(signed_doa, change_reason, request)

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
        revoked_at=datetime.now(UTC).replace(tzinfo=None),
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


@app.get("/api/v1/org/delegations", response_model=list[DelegationResponse])
async def list_delegations(
    request: Request,
    site_id: str | None = Query(None, description="Filter by site_id"),
    study_id: str | None = Query(None, description="Filter by study_id"),
    delegator_id: str | None = Query(
        None, description="Filter by delegator personnel ID"
    ),
    delegatee_id: str | None = Query(
        None, description="Filter by delegatee personnel ID"
    ),
    is_active: bool | None = Query(None, description="Filter by active status"),
    session: AsyncSession = Depends(get_db_session),
) -> list[DelegationResponse]:
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
    version_index: int | None = Query(
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
    "/api/v1/org/delegations/{id}/history", response_model=list[DelegationResponse]
)
async def get_delegation_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[DelegationResponse]:
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
    record_id: str | None = None
    details: str
    reason_for_change: str


@app.get("/api/v1/org/audit-logs", response_model=list[OrgAuditLogResponse])
async def list_org_audit_logs(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> list[OrgAuditLogResponse]:
    """
    Retrieve organization audit logs in descending chronological order.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(OrgAuditLog).order_by(desc(OrgAuditLog.timestamp))
    res = await session.execute(stmt)
    return res.scalars().all()


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


@app.get("/api/v1/org/sites", response_model=list[SiteResponse])
async def list_sites(
    request: Request,
    site_id: str | None = Query(None, description="Filter by site_id"),
    study_id: str | None = Query(None, description="Filter by study_id"),
    organization_id: str | None = Query(None, description="Filter by organization_id"),
    session: AsyncSession = Depends(get_db_session),
) -> list[SiteResponse]:
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
    version_index: int | None = Query(
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


@app.get("/api/v1/org/sites/{id}/history", response_model=list[SiteResponse])
async def get_site_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[SiteResponse]:
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

    if payload.role.value == "External Monitor":
        if not payload.organization_id:
            raise HTTPException(
                status_code=400,
                detail="External Monitor must be affiliated to a CRO organization.",
            )
        stmt_org = (
            select(Organization)
            .where(Organization.id == payload.organization_id)
            .order_by(desc(Organization.version_index))
        )
        org = (await session.execute(stmt_org)).scalars().first()
        if not org or org.org_type != "CRO":
            raise HTTPException(
                status_code=400,
                detail="External Monitor must be affiliated to a CRO organization.",
            )

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


@app.get("/api/v1/org/personnel", response_model=list[PersonnelResponse])
async def list_personnel(
    request: Request,
    site_id: str | None = Query(None, description="Filter by site_id"),
    study_id: str | None = Query(None, description="Filter by study_id"),
    organization_id: str | None = Query(None, description="Filter by organization_id"),
    role: ClinicalStaffRole | None = Query(
        None, description="Filter by exact staff role"
    ),
    email: str | None = Query(None, description="Filter by exact or partial email"),
    session: AsyncSession = Depends(get_db_session),
) -> list[PersonnelResponse]:
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
    version_index: int | None = Query(
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

    target_role = payload.role.value if payload.role is not None else latest_person.role
    target_org_id = (
        payload.organization_id
        if payload.organization_id is not None
        else latest_person.organization_id
    )
    if target_role == "External Monitor":
        if not target_org_id:
            raise HTTPException(
                status_code=400,
                detail="External Monitor must be affiliated to a CRO organization.",
            )
        stmt_org = (
            select(Organization)
            .where(Organization.id == target_org_id)
            .order_by(desc(Organization.version_index))
        )
        org = (await session.execute(stmt_org)).scalars().first()
        if not org or org.org_type != "CRO":
            raise HTTPException(
                status_code=400,
                detail="External Monitor must be affiliated to a CRO organization.",
            )

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


@app.get("/api/v1/org/personnel/{id}/history", response_model=list[PersonnelResponse])
async def get_personnel_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[PersonnelResponse]:
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


@app.post(
    "/api/v1/org/personnel/{personnel_id}/assignments",
    response_model=PersonnelAssignmentResponse,
    status_code=201,
)
async def create_personnel_assignment(
    request: Request,
    personnel_id: str,
    payload: PersonnelAssignmentCreate,
    session: AsyncSession = Depends(get_db_session),
) -> PersonnelAssignmentResponse:
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    stmt_person = (
        select(Personnel)
        .where(Personnel.id == personnel_id)
        .order_by(desc(Personnel.version_index))
    )
    person = (await session.execute(stmt_person)).scalars().first()
    if not person:
        raise HTTPException(status_code=404, detail="Personnel not found")

    if person.role == "External Monitor":
        if not person.organization_id:
            raise HTTPException(
                status_code=400,
                detail="External Monitor must be affiliated to a CRO organization.",
            )
        stmt_org = (
            select(Organization)
            .where(Organization.id == person.organization_id)
            .order_by(desc(Organization.version_index))
        )
        org = (await session.execute(stmt_org)).scalars().first()
        if not org or org.org_type != "CRO":
            raise HTTPException(
                status_code=400,
                detail="External Monitor must be affiliated to a CRO organization.",
            )

    assignment = PersonnelAssignment(
        id=str(uuid.uuid4()),
        personnel_id=personnel_id,
        site_id=payload.site_id,
        study_id=payload.study_id,
        is_active=payload.is_active,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(assignment)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_ASSIGNMENT_CREATE",
        record_id=assignment.id,
        details=f"Created assignment for personnel ID '{personnel_id}' at site '{payload.site_id}' and study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return assignment


@app.get(
    "/api/v1/org/personnel/{personnel_id}/assignments",
    response_model=list[PersonnelAssignmentResponse],
)
async def list_personnel_assignments(
    request: Request,
    personnel_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[PersonnelAssignmentResponse]:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = (
        select(PersonnelAssignment)
        .where(PersonnelAssignment.personnel_id == personnel_id)
        .order_by(PersonnelAssignment.id, desc(PersonnelAssignment.version_index))
    )
    res = await session.execute(stmt)
    all_assigns = res.scalars().all()

    latest_assigns = {}
    for a in all_assigns:
        if a.id not in latest_assigns:
            latest_assigns[a.id] = a

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_ASSIGNMENT_LIST",
        details=f"Listed assignments for personnel ID '{personnel_id}'.",
        reason_for_change=change_reason or "Standard query access",
    )

    return list(latest_assigns.values())


@app.put(
    "/api/v1/org/personnel/assignments/{id}", response_model=PersonnelAssignmentResponse
)
async def update_personnel_assignment(
    request: Request,
    id: str,
    payload: PersonnelAssignmentUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> PersonnelAssignmentResponse:
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    stmt = (
        select(PersonnelAssignment)
        .where(PersonnelAssignment.id == id)
        .order_by(desc(PersonnelAssignment.version_index))
    )
    latest_assign = (await session.execute(stmt)).scalars().first()
    if not latest_assign:
        raise HTTPException(status_code=404, detail="Personnel assignment not found")

    new_assign = PersonnelAssignment(
        id=id,
        personnel_id=latest_assign.personnel_id,
        site_id=payload.site_id
        if payload.site_id is not None
        else latest_assign.site_id,
        study_id=payload.study_id
        if payload.study_id is not None
        else latest_assign.study_id,
        is_active=payload.is_active
        if payload.is_active is not None
        else latest_assign.is_active,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=latest_assign.version_index + 1,
    )
    session.add(new_assign)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_ASSIGNMENT_UPDATE",
        record_id=id,
        details=f"Updated personnel assignment ID '{id}' to version {new_assign.version_index}.",
        reason_for_change=change_reason,
    )

    return new_assign


@app.get(
    "/api/v1/org/personnel/assignments/{id}/history",
    response_model=list[PersonnelAssignmentResponse],
)
async def get_personnel_assignment_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[PersonnelAssignmentResponse]:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = (
        select(PersonnelAssignment)
        .where(PersonnelAssignment.id == id)
        .order_by(desc(PersonnelAssignment.version_index))
    )
    res = await session.execute(stmt)
    history = res.scalars().all()
    if not history:
        raise HTTPException(
            status_code=404, detail="Personnel assignment history not found"
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_ASSIGNMENT_HISTORY",
        record_id=id,
        details=f"Retrieved history for personnel assignment ID '{id}'.",
        reason_for_change=change_reason or "Standard history query",
    )

    return history


@app.get("/api/v1/org/assignments/resolve", response_model=AssignmentResolutionResponse)
async def resolve_assignments(
    request: Request,
    keycloak_user_id: str = Query(..., description="The Keycloak user ID to resolve"),
    session: AsyncSession = Depends(get_db_session),
) -> AssignmentResolutionResponse:
    """
    Gateway-authenticated service-to-service endpoint to resolve active site and study assignments for personnel.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = (
        select(Personnel)
        .where(Personnel.keycloak_user_id == keycloak_user_id)
        .order_by(desc(Personnel.version_index))
    )
    person = (await session.execute(stmt)).scalars().first()
    if not person:
        raise HTTPException(
            status_code=404, detail="Personnel not found for keycloak_user_id"
        )

    # Fetch all assignments and extract latest active ones
    stmt_assign = (
        select(PersonnelAssignment)
        .where(PersonnelAssignment.personnel_id == person.id)
        .order_by(PersonnelAssignment.id, desc(PersonnelAssignment.version_index))
    )
    res = await session.execute(stmt_assign)
    all_assigns = res.scalars().all()

    latest_assigns = {}
    for a in all_assigns:
        if a.id not in latest_assigns:
            latest_assigns[a.id] = a

    active_assigns = [a for a in latest_assigns.values() if a.is_active]

    assigned_sites = sorted(list(set(a.site_id for a in active_assigns)))
    assigned_studies = sorted(list(set(a.study_id for a in active_assigns)))

    # Also include singular study/site fields if present
    if person.site_id and person.site_id not in assigned_sites:
        assigned_sites.append(person.site_id)
    if person.study_id and person.study_id not in assigned_studies:
        assigned_studies.append(person.study_id)

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="PERSONNEL_ASSIGNMENT_RESOLVE",
        details=f"Resolved assignments for keycloak_user_id '{keycloak_user_id}'. Found {len(assigned_sites)} sites, {len(assigned_studies)} studies.",
        reason_for_change=change_reason or "Internal resolution",
    )

    role_mapping = {
        "Principal Investigator": "investigator",
        "Sub-Investigator": "investigator",
        "CRC": "crc",
        "CRA/Monitor": "cra",
        "External Monitor": "external_monitor",
    }
    resolved_role = role_mapping.get(person.role, person.role.lower().replace(" ", "_"))

    return AssignmentResolutionResponse(
        personnel_id=person.id,
        roles=[resolved_role],
        assigned_sites=assigned_sites,
        assigned_studies=assigned_studies,
    )


# --- Training Log Endpoints ---


async def archive_signed_training_to_eisf(
    training_log: TrainingLog,
    change_reason: str,
    request: Request,
) -> None:
    """Asynchronously and durably archives a signed training log record to the eISF service.

    Args:
        training_log (TrainingLog): The signed training log model.
        change_reason (str): Justification reason for archiving.
        request (Request): The incoming FastAPI request.

    Returns:
        None
    """
    import json
    import logging
    import os
    import time

    import httpx

    logger = logging.getLogger("org.archival")

    # Retrieve eISF Service Ingest Endpoint
    eisf_url = (
        os.getenv("EISF_URL") or os.getenv("INTEROP_URL") or "http://localhost:8004"
    )
    ingest_endpoint = f"{eisf_url}/api/v1/eisf/ingest"

    # Assemble preserved signed training log payload contents
    payload_content = {
        "training_log_id": training_log.id,
        "personnel_id": training_log.personnel_id,
        "site_id": training_log.site_id,
        "study_id": training_log.study_id,
        "training_topic": training_log.training_topic,
        "completion_date": training_log.completion_date.isoformat()
        if isinstance(training_log.completion_date, datetime)
        else str(training_log.completion_date),
        "signature_manifestation": training_log.signature_manifestation,
        "signer": training_log.signer,
        "signing_timestamp": training_log.signing_timestamp.isoformat()
        if isinstance(training_log.signing_timestamp, datetime)
        else (
            str(training_log.signing_timestamp)
            if training_log.signing_timestamp
            else None
        ),
        "audit_provenance": {
            "created_by": training_log.created_by,
            "created_at": training_log.created_at.isoformat()
            if isinstance(training_log.created_at, datetime)
            else str(training_log.created_at),
            "reason_for_change": training_log.reason_for_change,
            "version_index": training_log.version_index,
        },
    }

    # Build compliant EISFIngestionRequest payload
    # Standard code 05.03.01 for Site Training Records
    ingest_payload = {
        "study_id": training_log.study_id,
        "site_id": training_log.site_id,
        "binder_classification": "Site Training Records",
        "filename": f"signed_training_{training_log.id}.json",
        "content": json.dumps(payload_content, indent=2),
        "mime_type": "application/json",
        "metadata_json": {
            "artifact_type": "Site Training Records",
            "artifact_code": "05.03.01",
            "training_log_id": training_log.id,
        },
        "source_system": "Organization Directory",
        "reason_for_change": f"Finalized and signed Site Training Record archival for training log {training_log.id}",
    }

    # Generate Gateway V2 signature using service token
    user_id = "org_directory_service"
    roles = "admin"
    timestamp = str(time.time())
    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode()
    gateway_reason = "Training Log automatic archival to eISF"

    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=gateway_reason,
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": gateway_reason,
    }

    try:
        async with httpx.AsyncClient() as client:
            resp = await client.post(
                ingest_endpoint,
                json=ingest_payload,
                headers=headers,
                timeout=10.0,
            )
            if resp.status_code not in (200, 201):
                logger.warning(
                    f"Failed to archive Training Log {training_log.id} to eISF. "
                    f"Endpoint returned status code {resp.status_code}: {resp.text}"
                )
            else:
                logger.info(
                    f"Successfully archived signed Training Log {training_log.id} to eISF."
                )
    except Exception as e:
        logger.error(
            f"Transport failure while archiving signed Training Log {training_log.id} to eISF: {str(e)}"
        )


@app.post(
    "/api/v1/org/training-logs", response_model=TrainingLogResponse, status_code=201
)
async def create_training_log(
    request: Request,
    payload: TrainingLogCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(require_permission("training_log:create")),
) -> TrainingLogResponse:
    """Creates a new training log record with version history.

    Args:
        request (Request): The incoming request context.
        payload (TrainingLogCreate): Data required to create the training log.
        session (AsyncSession): Relational database session.
        principal (Principal): Security principal.

    Returns:
        TrainingLogResponse: The created training log record.

    Raises:
        HTTPException: If justification is empty.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    training_log = TrainingLog(
        id=str(uuid.uuid4()),
        personnel_id=payload.personnel_id,
        site_id=payload.site_id,
        study_id=payload.study_id,
        training_topic=payload.training_topic,
        completion_date=payload.completion_date,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(training_log)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="TRAINING_LOG_CREATE",
        record_id=training_log.id,
        details=f"Created training log for personnel '{payload.personnel_id}' on topic '{payload.training_topic}'.",
        reason_for_change=change_reason,
    )

    return training_log


@app.get("/api/v1/org/training-logs", response_model=list[TrainingLogResponse])
async def list_training_logs(
    request: Request,
    personnel_id: str | None = Query(None, description="Filter by personnel ID"),
    site_id: str | None = Query(None, description="Filter by site ID"),
    study_id: str | None = Query(None, description="Filter by study ID"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(require_permission("training_log:read")),
) -> list[TrainingLogResponse]:
    """List all training logs, returning the latest version of each.

    Args:
        request (Request): The incoming request context.
        personnel_id (str | None): Optional personnel ID filter.
        site_id (str | None): Optional site ID filter.
        study_id (str | None): Optional study ID filter.
        session (AsyncSession): Relational database session.
        principal (Principal): Security principal.

    Returns:
        list[TrainingLogResponse]: Filtered training log list of latest versions.
    """
    user_id, user_role, change_reason = get_user_context(request)

    # Fetch all records sorted by version desc
    stmt = select(TrainingLog).order_by(TrainingLog.id, desc(TrainingLog.version_index))
    res = await session.execute(stmt)
    all_logs = res.scalars().all()

    # Deduplicate keeping only latest versions
    latest_logs = {}
    for log in all_logs:
        if log.id not in latest_logs:
            latest_logs[log.id] = log

    filtered_logs = list(latest_logs.values())

    # Apply query filters
    if personnel_id is not None:
        filtered_logs = [
            log for log in filtered_logs if log.personnel_id == personnel_id
        ]
    if site_id is not None:
        filtered_logs = [log for log in filtered_logs if log.site_id == site_id]
    if study_id is not None:
        filtered_logs = [log for log in filtered_logs if log.study_id == study_id]

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="TRAINING_LOG_LIST",
        details=f"Listed training logs with filters (personnel_id={personnel_id}, site_id={site_id}, study_id={study_id}).",
        reason_for_change=change_reason or "Standard query access",
    )

    return filtered_logs


@app.get("/api/v1/org/training-logs/{id}", response_model=TrainingLogResponse)
async def get_training_log(
    request: Request,
    id: str,
    version_index: int | None = Query(
        None, description="Optionally retrieve a specific version"
    ),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(require_permission("training_log:read")),
) -> TrainingLogResponse:
    """Retrieve details for a training log by ID.

    Args:
        request (Request): The incoming request context.
        id (str): The training log ID.
        version_index (int | None): Optional version filter.
        session (AsyncSession): Relational database session.
        principal (Principal): Security principal.

    Returns:
        TrainingLogResponse: The specified training log record.

    Raises:
        HTTPException: If training log not found.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(TrainingLog).where(TrainingLog.id == id)
    if version_index is not None:
        stmt = stmt.where(TrainingLog.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(TrainingLog.version_index))

    res = await session.execute(stmt)
    log = res.scalars().first()

    if not log:
        raise HTTPException(status_code=404, detail="Training log not found")

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="TRAINING_LOG_VIEW",
        record_id=log.id,
        details=f"Viewed training log '{log.id}' (version {log.version_index}).",
        reason_for_change=change_reason or "Standard retrieve access",
    )

    return log


@app.get(
    "/api/v1/org/training-logs/{id}/history", response_model=list[TrainingLogResponse]
)
async def get_training_log_history(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(require_permission("training_log:read")),
) -> list[TrainingLogResponse]:
    """Retrieve chronological version history for a training log.

    Args:
        request (Request): The incoming request context.
        id (str): The training log ID.
        session (AsyncSession): Relational database session.
        principal (Principal): Security principal.

    Returns:
        list[TrainingLogResponse]: List of versions for the training log.

    Raises:
        HTTPException: If training log not found.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = (
        select(TrainingLog)
        .where(TrainingLog.id == id)
        .order_by(desc(TrainingLog.version_index))
    )
    res = await session.execute(stmt)
    history = res.scalars().all()

    if not history:
        raise HTTPException(status_code=404, detail="Training log not found")

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="TRAINING_LOG_HISTORY_VIEW",
        record_id=id,
        details=f"Viewed history for training log ID '{id}'.",
        reason_for_change=change_reason or "Standard history access",
    )

    return history


@app.put("/api/v1/org/training-logs/{id}", response_model=TrainingLogResponse)
async def update_training_log(
    request: Request,
    id: str,
    payload: TrainingLogUpdate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(require_permission("training_log:create")),
) -> TrainingLogResponse:
    """Soft-update an existing training log, appending a new version with incremented version_index.

    Args:
        request (Request): The incoming request context.
        id (str): The training log ID to update.
        payload (TrainingLogUpdate): Updated training log values.
        session (AsyncSession): Relational database session.
        principal (Principal): Security principal.

    Returns:
        TrainingLogResponse: The updated/new training log version.

    Raises:
        HTTPException: If training log is signed/locked or justification is empty.
    """
    if not payload.reason_for_change or not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=400,
            detail="Justification parameter (reason_for_change) is required.",
        )

    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    stmt = (
        select(TrainingLog)
        .where(TrainingLog.id == id)
        .order_by(desc(TrainingLog.version_index))
    )
    res = await session.execute(stmt)
    latest_log = res.scalars().first()

    if not latest_log:
        raise HTTPException(status_code=404, detail="Training log not found")

    if latest_log.signature_manifestation is not None:
        raise HTTPException(
            status_code=400, detail="Cannot modify a signed and locked training log"
        )

    new_log = TrainingLog(
        id=id,
        personnel_id=payload.personnel_id
        if payload.personnel_id is not None
        else latest_log.personnel_id,
        site_id=payload.site_id if payload.site_id is not None else latest_log.site_id,
        study_id=payload.study_id
        if payload.study_id is not None
        else latest_log.study_id,
        training_topic=payload.training_topic
        if payload.training_topic is not None
        else latest_log.training_topic,
        completion_date=payload.completion_date
        if payload.completion_date is not None
        else latest_log.completion_date,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=latest_log.version_index + 1,
    )
    session.add(new_log)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="TRAINING_LOG_UPDATE",
        record_id=id,
        details=f"Updated training log ID '{id}' to version {new_log.version_index}.",
        reason_for_change=change_reason,
    )

    return new_log


@app.post("/api/v1/org/training-logs/{id}/sign", response_model=TrainingLogResponse)
async def sign_training_log(
    request: Request,
    id: str,
    payload: TrainingLogSignRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(require_permission("training_log:sign")),
) -> TrainingLogResponse:
    """Approve and electronically sign a Training Log record.

    Args:
        request (Request): The incoming request context.
        id (str): The training log ID to sign.
        payload (TrainingLogSignRequest): Signer credentials/signature and canonical data.
        session (AsyncSession): Relational database session.
        principal (Principal): Security principal.

    Returns:
        TrainingLogResponse: The signed and locked training log version.

    Raises:
        HTTPException: If signature verification fails or data payload mismatch occurs.
    """
    user_id, user_role, change_reason = get_user_context(request)
    change_reason = change_reason or payload.reason_for_change

    # 1. Fetch latest training log record
    stmt = (
        select(TrainingLog)
        .where(TrainingLog.id == id)
        .order_by(desc(TrainingLog.version_index))
    )
    res = await session.execute(stmt)
    latest_log = res.scalars().first()
    if not latest_log:
        raise HTTPException(status_code=404, detail="Training log record not found")

    # 2. Verify canonical signed payload and reject on mismatch or tampering
    is_valid = verify_canonical_signature(
        payload.payload, payload.signature, GATEWAY_SECRET
    )
    if not is_valid:
        raise HTTPException(
            status_code=422,
            detail="Signature verification failed: tampered content or invalid signature.",
        )

    # Treat the canonical signed payload as authoritative: verify that key fields match the record
    signed_data = payload.payload
    if signed_data.get("id") != latest_log.id:
        raise HTTPException(status_code=422, detail="Signed payload 'id' mismatch")
    if signed_data.get("personnel_id") != latest_log.personnel_id:
        raise HTTPException(
            status_code=422, detail="Signed payload 'personnel_id' mismatch"
        )
    if signed_data.get("site_id") != latest_log.site_id:
        raise HTTPException(status_code=422, detail="Signed payload 'site_id' mismatch")
    if signed_data.get("study_id") != latest_log.study_id:
        raise HTTPException(
            status_code=422, detail="Signed payload 'study_id' mismatch"
        )
    if signed_data.get("training_topic") != latest_log.training_topic:
        raise HTTPException(
            status_code=422, detail="Signed payload 'training_topic' mismatch"
        )

    # 3. Create new row version index incremented training record
    signed_log = TrainingLog(
        id=latest_log.id,
        personnel_id=latest_log.personnel_id,
        site_id=latest_log.site_id,
        study_id=latest_log.study_id,
        training_topic=latest_log.training_topic,
        completion_date=latest_log.completion_date,
        signature_manifestation=payload.payload,
        signer=user_id,
        signing_timestamp=datetime.now(UTC).replace(tzinfo=None),
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=latest_log.version_index + 1,
    )
    session.add(signed_log)
    await session.flush()

    # 4. GxP Audit Trail Log Entry
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="TRAINING_LOG_SIGN",
        record_id=signed_log.id,
        details=f"Electronically signed training log record ID '{signed_log.id}' (version {signed_log.version_index}).",
        reason_for_change=change_reason,
    )

    # Durable handoff to archive to eISF
    await archive_signed_training_to_eisf(signed_log, change_reason, request)

    return signed_log
