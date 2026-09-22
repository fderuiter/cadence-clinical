"""FastAPI presentation router for Clinical Visit management and scheduling.

Requirements: PRD-SYS-001, PRD-SYS-004, PRD-SYS-042, PRD-SUB-001, Trace-1, Trace-3
"""

from datetime import datetime

from fastapi import APIRouter, Depends, HTTPException, Request
from pydantic import BaseModel
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    ClinicalSubject,
    ClinicalVisit,
    SubjectConsent,
    SubjectRandomization,
)
from apps.execution.field_masking import apply_rtsm_blinded_filter
from packages.security import enforce_site_isolation
from packages.security.rbac import (
    Principal,
    get_principal,
    verify_not_auditor,
)

router = APIRouter(tags=["Visits"])


class VisitCreate(BaseModel):
    subject_id: str
    visit_name: str
    study_id: str
    visit_date: datetime | None = None
    planned_date: datetime | None = None
    window_start: datetime | None = None
    window_end: datetime | None = None
    window_status: str | None = None


class VisitResponse(BaseModel):
    id: str
    subject_id: str
    visit_name: str
    visit_date: datetime
    study_id: str
    protocol_version_tag: str | None = None
    protocol_version_index: int | None = None
    planned_date: datetime | None = None
    window_start: datetime | None = None
    window_end: datetime | None = None
    window_status: str | None = None


class VisitDetailResponse(BaseModel):
    id: str
    subject_id: str
    visit_name: str
    visit_date: datetime
    study_id: str
    treatment_group: str | None = None
    randomization_seed: str | None = None
    investigational_product_id: str | None = None
    planned_date: datetime | None = None
    window_start: datetime | None = None
    window_end: datetime | None = None
    window_status: str | None = None


@router.post("/api/v1/execution/visits", response_model=VisitResponse)
async def create_visit(
    payload: VisitCreate,
    roles: list[str] = Depends(verify_not_auditor),
) -> VisitResponse:
    async with db_manager.get_session_maker()() as session:
        vdate = payload.visit_date or datetime.now()
        visit = ClinicalVisit(
            subject_id=payload.subject_id,
            visit_name=payload.visit_name,
            visit_date=vdate,
            study_id=payload.study_id,
            planned_date=payload.planned_date,
            window_start=payload.window_start,
            window_end=payload.window_end,
            window_status=payload.window_status,
        )

        stmt_consent = (
            select(SubjectConsent)
            .where(
                SubjectConsent.subject_id == payload.subject_id,
                SubjectConsent.study_id == payload.study_id,
                SubjectConsent.icf_signed.is_(True),
            )
            .order_by(SubjectConsent.version_index.desc())
        )
        res_consent = await session.execute(stmt_consent)
        active_consent = res_consent.scalars().first()
        if active_consent:
            visit.protocol_version_tag = active_consent.version_tag
            visit.protocol_version_index = active_consent.version_index

        session.add(visit)
        await session.commit()

        stmt = select(ClinicalVisit).where(ClinicalVisit.id == visit.id)
        res = await session.execute(stmt)
        visit_db = res.scalar_one()

        return VisitResponse(
            id=visit_db.id,
            subject_id=visit_db.subject_id,
            visit_name=visit_db.visit_name,
            visit_date=visit_db.visit_date,
            study_id=visit_db.study_id,
            protocol_version_tag=visit_db.protocol_version_tag,
            protocol_version_index=visit_db.protocol_version_index,
            planned_date=visit_db.planned_date,
            window_start=visit_db.window_start,
            window_end=visit_db.window_end,
            window_status=visit_db.window_status,
        )


@router.get(
    "/api/v1/execution/visits/{visit_id}",
    response_model=VisitDetailResponse,
)
async def get_visit_detail(
    visit_id: str,
    request: Request,
    principal: Principal = Depends(get_principal),
) -> VisitDetailResponse:
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalVisit).where(ClinicalVisit.id == visit_id)
        result = await session.execute(stmt)
        visit = result.scalars().first()
        if not visit:
            raise HTTPException(status_code=404, detail="Visit not found")

        stmt_subj = select(ClinicalSubject).where(
            ClinicalSubject.subject_id == visit.subject_id
        )
        subj_res = await session.execute(stmt_subj)
        subject = subj_res.scalars().first()
        if not subject:
            raise HTTPException(status_code=404, detail="Associated Subject not found")

        enforce_site_isolation(request, subject.site_id, principal)

        stmt_rand = select(SubjectRandomization).where(
            SubjectRandomization.subject_id == subject.subject_id
        )
        rand_res = await session.execute(stmt_rand)
        rand = rand_res.scalars().first()

        treatment_group = None
        randomization_seed = None
        investigational_product_id = None

        if rand:
            from apps.execution.cryptography import AllocationKeyManager

            key_mgr = AllocationKeyManager()
            await key_mgr.load_from_db(session)
            try:
                decrypted = key_mgr.decrypt(rand.encrypted_allocation)
                treatment_group = decrypted.get("allocation")
            except Exception:
                treatment_group = "Decryption Failed"

            randomization_seed = "12345"
            investigational_product_id = rand.kit_reference

        response_dict = {
            "id": visit.id,
            "subject_id": visit.subject_id,
            "visit_name": visit.visit_name,
            "visit_date": visit.visit_date,
            "study_id": visit.study_id,
            "treatment_group": treatment_group,
            "randomization_seed": randomization_seed,
            "investigational_product_id": investigational_product_id,
            "planned_date": visit.planned_date,
            "window_start": visit.window_start,
            "window_end": visit.window_end,
            "window_status": visit.window_status,
        }

        roles = getattr(request.state, "roles", None)
        if roles is None:
            roles = principal.roles
        masked = apply_rtsm_blinded_filter(response_dict, roles)
        return VisitDetailResponse(**masked)
