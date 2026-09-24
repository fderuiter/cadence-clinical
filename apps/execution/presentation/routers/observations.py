"""FastAPI presentation router for Clinical Observations, unit conversions, and outlier calculations.

Requirements: PRD-SYS-001, PRD-SYS-004, PRD-SYS-042, PRD-SYS-121, PRD-SUB-001, Trace-1, Trace-15
"""

import json
from datetime import UTC, datetime

from fastapi import (
    APIRouter,
    BackgroundTasks,
    Depends,
    HTTPException,
)
from pydantic import BaseModel, model_validator
from sqlalchemy import select

from apps.execution.coding import match_verbatim_term
from apps.execution.database.context import (
    current_change_reason,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    CodingState,
    DictionaryImportJob,
    ImportState,
    SubjectConsent,
)
from apps.execution.database.models import (
    DictionaryType as DBDictionaryType,
)
from apps.execution.demographics import get_safe_demographics
from apps.execution.edit_checks import (
    handle_cascading_nullification,
    run_asynchronous_edit_checks,
    run_synchronous_edit_checks,
)
from apps.execution.lab_range_cache import get_active_lab_ranges, lab_range_cache
from apps.execution.lab_ranges import evaluate_lab_value, select_reference_range
from apps.execution.outliers import recalculate_cohort_outliers
from apps.execution.site_execution_gatekeeper import (
    require_site_mutation_authorization,
)
from apps.execution.ucum import convert_unit, get_normalized_representation
from packages.security.rbac import verify_not_auditor

router = APIRouter(tags=["Observations"])


class ObservationCreate(BaseModel):
    """Pydantic schema for creating a clinical observation."""

    subject_id: str
    study_id: str | None = None
    site_id: str | None = None
    visit_id: str | None = None
    domain: str
    test_code: str
    test_name: str
    value: float | None = None
    value_string: str | None = None
    unit: str | None = None
    observation_date: datetime | None = None
    lab_source: str | None = None
    lab_site_id: str | None = None


class ObservationResponse(BaseModel):
    """Pydantic schema returning observation details."""

    id: str
    subject_id: str
    study_id: str
    visit_id: str | None = None
    domain: str
    observation_date: datetime
    test_code: str
    test_name: str
    value: float | None = None
    value_string: str | None = None
    unit: str | None = None
    normalized_value: float | None = None
    normalized_unit: str | None = None
    is_outlier: bool
    lab_source: str | None = None
    lab_site_id: str | None = None
    lab_indicator: str | None = None
    lab_out_of_range: bool | None = None
    matched_normal_bounds: str | None = None
    range_indicator: str | None = None
    is_out_of_range: bool | None = None
    reference_range_low: float | None = None
    reference_range_high: float | None = None
    protocol_version_tag: str | None = None
    protocol_version_index: int | None = None

    @model_validator(mode="after")
    def populate_range_fields(self) -> ObservationResponse:
        if self.range_indicator is None and self.lab_indicator is not None:
            self.range_indicator = self.lab_indicator
        if self.is_out_of_range is None and self.lab_out_of_range is not None:
            self.is_out_of_range = self.lab_out_of_range
        if self.reference_range_low is None or self.reference_range_high is None:
            if self.matched_normal_bounds:
                try:
                    bounds = json.loads(self.matched_normal_bounds)
                    if self.reference_range_low is None:
                        self.reference_range_low = bounds.get("low")
                    if self.reference_range_high is None:
                        self.reference_range_high = bounds.get("high")
                except Exception:
                    pass
        return self


class UnitConversionRequest(BaseModel):
    """Pydantic schema for unit conversion requests."""

    value: float
    from_unit: str
    to_unit: str


class UnitConversionResponse(BaseModel):
    """Pydantic schema returning converted values."""

    value: float
    from_unit: str
    to_unit: str
    converted_value: float


class OutlierRecalculateRequest(BaseModel):
    """Pydantic schema for triggering outlier calculations."""

    study_id: str
    test_code: str


class OutlierRecalculateResponse(BaseModel):
    """Pydantic schema returning recalculation status."""

    status: str
    study_id: str
    test_code: str
    outliers_found: int


async def perform_unit_conversion(
    payload: UnitConversionRequest,
) -> UnitConversionResponse:
    """Helper method executing unit conversion logic."""
    try:
        conv = convert_unit(payload.value, payload.from_unit, payload.to_unit)
        return UnitConversionResponse(
            value=payload.value,
            from_unit=payload.from_unit,
            to_unit=payload.to_unit,
            converted_value=conv,
        )
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))


@router.post("/api/v1/execution/observations", response_model=ObservationResponse)
async def create_observation(
    payload: ObservationCreate,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(verify_not_auditor),
) -> ObservationResponse:
    """CREATE a new clinical observation, performing unit normalization and outlier checks."""
    norm_val, norm_unit = get_normalized_representation(payload.value, payload.unit)

    async with db_manager.get_session_maker()() as session:
        stmt_subj = select(ClinicalSubject).where(
            ClinicalSubject.subject_id == payload.subject_id
        )
        res_subj = await session.execute(stmt_subj)
        subj_db = res_subj.scalars().first()

        study_id = payload.study_id
        if not study_id:
            if not subj_db:
                raise HTTPException(
                    status_code=400,
                    detail="Subject not registered; cannot infer study_id",
                )
            study_id = subj_db.study_id

        u_id = current_user_id.get() or "system"
        proto_tag = getattr(subj_db, "active_protocol_version", None) or "1.0.0"
        s_id = getattr(payload, "site_id", None) or (
            getattr(subj_db, "site_id", None) if subj_db else None
        )
        if s_id:
            await require_site_mutation_authorization(
                site_id=s_id,
                user_id=u_id,
                operation_type="OBSERVATION_CREATION",
                study_id=study_id,
                protocol_version=proto_tag,
            )

        obs_date = payload.observation_date or datetime.now()

        gender = "U"
        age = None
        if subj_db:
            demo = get_safe_demographics(subj_db, obs_date, preserve_custom=True)
            gender = demo.get("gender")
            age = demo.get("age")

        ranges = await get_active_lab_ranges(
            lab_range_cache, session, study_id, payload.test_code
        )

        matched_range = select_reference_range(
            ranges=ranges,
            study_id=study_id,
            test_code=payload.test_code,
            normalized_unit=norm_unit,
            lab_source=payload.lab_source or "CENTRAL",
            sex=gender,
            age=age,
            site_id=payload.lab_site_id,
        )

        indicator, out_of_range, matched_bounds = evaluate_lab_value(
            norm_val, matched_range
        )

        protocol_version_tag = None
        protocol_version_index = None
        stmt_consent = (
            select(SubjectConsent)
            .where(
                SubjectConsent.subject_id == payload.subject_id,
                SubjectConsent.study_id == study_id,
                SubjectConsent.icf_signed.is_(True),
            )
            .order_by(SubjectConsent.version_index.desc())
        )
        res_consent = await session.execute(stmt_consent)
        active_consent = res_consent.scalars().first()
        if active_consent:
            protocol_version_tag = active_consent.version_tag
            protocol_version_index = active_consent.version_index

        obs = ClinicalObservation(
            subject_id=payload.subject_id,
            study_id=study_id,
            visit_id=payload.visit_id,
            domain=payload.domain,
            observation_date=obs_date,
            test_code=payload.test_code,
            test_name=payload.test_name,
            value=payload.value,
            value_string=payload.value_string,
            unit=payload.unit,
            normalized_value=norm_val,
            normalized_unit=norm_unit,
            is_outlier=False,
            lab_source=payload.lab_source or "CENTRAL",
            lab_site_id=payload.lab_site_id,
            lab_indicator=indicator,
            lab_out_of_range=out_of_range,
            matched_normal_bounds=matched_bounds,
            protocol_version_tag=protocol_version_tag,
            protocol_version_index=protocol_version_index,
        )
        session.add(obs)

        domain_upper = payload.domain.upper()
        if domain_upper in {"AE", "MH", "CM"}:
            verbatim = payload.value_string
            if verbatim and verbatim.strip():
                if domain_upper in {"AE", "MH"}:
                    dict_type = DBDictionaryType.MEDDRA
                    stmt_latest = (
                        select(DictionaryImportJob)
                        .where(
                            DictionaryImportJob.dictionary_type
                            == DBDictionaryType.MEDDRA,
                            DictionaryImportJob.status == ImportState.COMPLETED,
                        )
                        .order_by(DictionaryImportJob.completed_at.desc())
                    )
                    res_latest = await session.execute(stmt_latest)
                    latest_job = res_latest.scalars().first()
                    version = latest_job.dictionary_version if latest_job else "26.0"
                else:
                    dict_type = DBDictionaryType.WHODRUG
                    stmt_latest = (
                        select(DictionaryImportJob)
                        .where(
                            DictionaryImportJob.dictionary_type
                            == DBDictionaryType.WHODRUG,
                            DictionaryImportJob.status == ImportState.COMPLETED,
                        )
                        .order_by(DictionaryImportJob.completed_at.desc())
                    )
                    res_latest = await session.execute(stmt_latest)
                    latest_job = res_latest.scalars().first()
                    version = latest_job.dictionary_version if latest_job else "2024-03"

                try:
                    match_res = await match_verbatim_term(
                        session=session,
                        verbatim=verbatim.strip(),
                        dictionary_type=dict_type.value,
                        version=version,
                    )
                except Exception:
                    match_res = {
                        "status": "UNCODABLE",
                        "match": None,
                        "suggestions": [],
                    }

                status = CodingState.UNCODED
                coded_code = None
                coded_term = None
                score = None
                hierarchy = None
                suggestions = None

                match_status = match_res.get("status")
                if match_status == "AUTO-CODED" and match_res.get("match"):
                    m = match_res["match"]
                    status = CodingState.AUTO_CODED
                    coded_code = m.get("code") or m.get("drug_code")
                    coded_term = m.get("term_name") or m.get("preferred_name")
                    score = m.get("score")
                    if dict_type == DBDictionaryType.MEDDRA:
                        hierarchy = m.get("hierarchies")
                    else:
                        hierarchy = {
                            "atc_context": m.get("atc_context", []),
                            "ingredients": m.get("ingredients", []),
                        }
                elif match_status == "SUGGESTIONS":
                    status = CodingState.SUGGESTED
                    suggestions = match_res.get("suggestions")
                elif match_status == "UNCODABLE":
                    status = CodingState.QUERY_PENDING

                assignment = ClinicalCodingAssignment(
                    verbatim_text=verbatim.strip(),
                    source_field=f"{payload.domain}.{payload.test_code}",
                    observation_id=obs.id,
                    dictionary_type=dict_type,
                    dictionary_version=version,
                    coded_code=coded_code,
                    coded_term=coded_term,
                    status=status,
                    score=score,
                    hierarchy=hierarchy,
                    suggestions=suggestions,
                    domain=domain_upper,
                    assigned_by="system",
                    assigned_at=datetime.now(UTC),
                )
                session.add(assignment)

                if status == CodingState.QUERY_PENDING:
                    stmt_q_exist = select(ClinicalQuery).where(
                        ClinicalQuery.study_id == study_id,
                        ClinicalQuery.subject_id == payload.subject_id,
                        ClinicalQuery.visit_id == payload.visit_id,
                        ClinicalQuery.domain == payload.domain,
                        ClinicalQuery.test_code == payload.test_code,
                        ClinicalQuery.status.in_(
                            ["CANDIDATE", "OPEN", "ANSWERED", "REOPENED"]
                        ),
                        ClinicalQuery.is_deleted.is_(False),
                    )
                    res_q_exist = await session.execute(stmt_q_exist)
                    existing_q = res_q_exist.scalars().first()

                    if not existing_q:
                        q_explanation = f"The verbatim term '{verbatim.strip()}' in field {payload.test_code} is uncodable. Please split into individual events or clarify spelling."
                        query = ClinicalQuery(
                            study_id=study_id,
                            subject_id=payload.subject_id,
                            visit_id=payload.visit_id,
                            domain=payload.domain,
                            test_code=payload.test_code,
                            status="OPEN",
                            explanation=q_explanation,
                            message=q_explanation,
                            observation_id=obs.id,
                            origin="SYSTEM_CODING",
                            query_type="SYSTEM_CODING",
                            form_id=f"{payload.domain.upper()}_FORM",
                            field_id=payload.test_code,
                            action_required="RE-ENTER_VERBATIM",
                            created_by="system",
                        )
                        session.add(query)

                if status == CodingState.AUTO_CODED:
                    ledger = ClinicalCodingLedger(
                        assignment_id=assignment.id,
                        verbatim_text=verbatim.strip(),
                        observation_id=obs.id,
                        dictionary_type=dict_type,
                        old_dictionary_version=None,
                        old_coded_code=None,
                        old_coded_term=None,
                        new_dictionary_version=version,
                        new_coded_code=coded_code,
                        new_coded_term=coded_term,
                        recoding_reason="Auto-coded by Medical Coding Engine",
                        decision_by="system",
                        decision_at=datetime.now(UTC),
                    )
                    session.add(ledger)

        await session.commit()

        await recalculate_cohort_outliers(session, study_id, payload.test_code)

        stmt_obs = select(ClinicalObservation).where(ClinicalObservation.id == obs.id)
        res_obs = await session.execute(stmt_obs)
        obs_db = res_obs.scalar_one()

        await handle_cascading_nullification(session, obs_db)
        await run_synchronous_edit_checks(session, obs_db)
        await session.commit()

        user_id = current_user_id.get()
        change_reason = current_change_reason.get()

        if obs_db.lab_indicator in ("LOW LOW", "HIGH HIGH"):
            from apps.execution.notification_events import (
                dispatch_critical_lab_alerts,
            )

            dispatch_critical_lab_alerts(
                background_tasks,
                obs_db,
                obs_db.lab_indicator,
                user_id,
                change_reason,
            )

        background_tasks.add_task(
            run_asynchronous_edit_checks,
            db_manager.get_session_maker(),
            obs_db.id,
            user_id=user_id,
            change_reason=change_reason,
        )

        return ObservationResponse(
            id=obs_db.id,
            subject_id=obs_db.subject_id,
            study_id=obs_db.study_id,
            visit_id=obs_db.visit_id,
            domain=obs_db.domain,
            observation_date=obs_db.observation_date,
            test_code=obs_db.test_code,
            test_name=obs_db.test_name,
            value=obs_db.value,
            value_string=obs_db.value_string,
            unit=obs_db.unit,
            normalized_value=obs_db.normalized_value,
            normalized_unit=obs_db.normalized_unit,
            is_outlier=obs_db.is_outlier,
            lab_source=obs_db.lab_source,
            lab_site_id=obs_db.lab_site_id,
            lab_indicator=obs_db.lab_indicator,
            lab_out_of_range=obs_db.lab_out_of_range,
            matched_normal_bounds=obs_db.matched_normal_bounds,
            range_indicator=obs_db.lab_indicator,
            is_out_of_range=obs_db.lab_out_of_range,
            reference_range_low=obs_db.reference_range_low,
            reference_range_high=obs_db.reference_range_high,
            protocol_version_tag=obs_db.protocol_version_tag,
            protocol_version_index=obs_db.protocol_version_index,
        )


@router.post("/unit-conversion", response_model=UnitConversionResponse)
async def post_unit_conversion_execution(
    payload: UnitConversionRequest,
) -> UnitConversionResponse:
    """Translate incoming values using UCUM mapping rules (Execution API)."""
    return await perform_unit_conversion(payload)


@router.get("/unit-conversion", response_model=UnitConversionResponse)
async def get_unit_conversion_execution(
    value: float, from_unit: str, to_unit: str
) -> UnitConversionResponse:
    """Translate incoming values using UCUM mapping rules via GET (Execution API)."""
    return await perform_unit_conversion(
        UnitConversionRequest(value=value, from_unit=from_unit, to_unit=to_unit)
    )


@router.post(
    "/api/v1/execution/outliers/recalculate", response_model=OutlierRecalculateResponse
)
async def trigger_outlier_recalculation(
    payload: OutlierRecalculateRequest,
) -> OutlierRecalculateResponse:
    """Trigger cohort-wide outlier recalculation on-demand."""
    async with db_manager.get_session_maker()() as session:
        count = await recalculate_cohort_outliers(
            session, payload.study_id, payload.test_code
        )
        return OutlierRecalculateResponse(
            status="success",
            study_id=payload.study_id,
            test_code=payload.test_code,
            outliers_found=count,
        )
