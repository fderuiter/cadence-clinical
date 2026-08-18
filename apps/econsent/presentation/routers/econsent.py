"""FastAPI Router for eConsent microservice."""

import json
import os
import uuid
from datetime import UTC, datetime

import redis
from fastapi import APIRouter, Depends, HTTPException, Request
from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.infrastructure.cache import (
    ApprovedTranslationCache,
    get_approved_template_translation,
)
from apps.econsent.infrastructure.database import db_manager
from apps.econsent.infrastructure.models import (
    ComprehensionCheck,
    ComprehensionResult,
    ConsentAuditLog,
    ConsentClause,
    ConsentDocument,
    ConsentSignature,
    ConsentTemplate,
    ConsentTranslation,
    EtmfArchivalDelivery,
    SubjectConsent,
)
from apps.econsent.infrastructure.services import submit_comprehension_answers
from apps.econsent.presentation.dtos import (
    ArchivalDeliveryResponse,
    ClauseDiffDTO,
    ComposedClauseResponse,
    ComposedTemplateResponse,
    ComprehensionCheckCreate,
    ComprehensionCheckResponse,
    ComprehensionSubmissionRequest,
    ComprehensionSubmissionResponse,
    ConsentClauseCreate,
    ConsentClauseResponse,
    ConsentClauseUpdate,
    ConsentDocumentCreate,
    ConsentDocumentResponse,
    ConsentSignatureRequest,
    ConsentSignatureResponse,
    ConsentTemplateCreate,
    ConsentTemplateResponse,
    ConsentTemplateUpdate,
    ConsentTranslationCreate,
    ConsentTranslationResponse,
    ConsentTranslationUpdate,
    SubjectConsentCaptureRequest,
    SubjectConsentResponse,
    SubjectConsentStatusResponse,
    TemplateDiffResponse,
    TranslationTransitionRequest,
)
from packages.database import DatabaseSessionDependency
from packages.security.rbac import verify_not_auditor

router = APIRouter()
get_db_session = DatabaseSessionDependency(db_manager)
approved_translation_cache = ApprovedTranslationCache()


async def write_audit_log(
    session: AsyncSession,
    actor_id: str,
    actor_role: str,
    action: str,
    document_id: str | None,
    details: str,
    reason_for_change: str,
) -> None:
    """Appends an entry to the 21 CFR Part 11 compliant ConsentAuditLog."""
    log_entry = ConsentAuditLog(
        actor_id=actor_id,
        actor_role=actor_role,
        action=action,
        document_id=document_id,
        details=details,
        reason_for_change=reason_for_change,
    )
    session.add(log_entry)
    await session.flush()


def publish_consent_completed_event(
    subject_id: str,
    study_id: str,
    site_id: str | None,
    version_tag: str = "1.0",
    version_index: int = 1,
) -> None:
    """Publishes a consent completed event to a Redis Pub/Sub channel.

    Args:
        subject_id: The pseudonym identifier of the subject.
        study_id: The study identifier.
        site_id: Optional site identifier.
        version_tag: Optional version tag of the consent template.
        version_index: Optional version index of the consent template.

    Returns:
        None
    """
    redis_host = os.getenv("REDIS_HOST")
    if not redis_host:
        return
    try:
        redis_port = int(os.getenv("REDIS_PORT", "6379"))
        redis_password = os.getenv("REDIS_PASSWORD") or None
        redis_channel = os.getenv("REDIS_CHANNEL_CONSENT", "econsent_consent_completed")

        r = redis.Redis(
            host=redis_host,
            port=redis_port,
            password=redis_password,
            decode_responses=True,
            socket_timeout=5,
        )
        payload = {
            "action": "consent_completed",
            "subject_id": subject_id,
            "study_id": study_id,
            "site_id": site_id,
            "version_tag": version_tag,
            "version_index": version_index,
        }
        r.publish(redis_channel, json.dumps(payload))
    except Exception as e:
        import logging

        logging.getLogger("econsent-publisher").warning(
            f"Failed to publish consent completed event to Redis: {e}"
        )


def map_document_to_response(doc: ConsentDocument) -> ConsentDocumentResponse:
    return ConsentDocumentResponse(
        id=doc.id,
        study_id=doc.study_id,
        site_id=doc.site_id,
        document_name=doc.document_name,
        content=doc.content,
        created_at=doc.created_at,
        created_by=doc.created_by,
        reason_for_change=doc.reason_for_change,
        version_index=doc.version_index,
    )


@router.post(
    "/api/v1/econsent/documents",
    response_model=ConsentDocumentResponse,
    status_code=201,
)
async def create_consent_document(
    request: Request,
    payload: ConsentDocumentCreate,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentDocumentResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    doc = ConsentDocument(
        study_id=payload.study_id,
        site_id=payload.site_id,
        document_name=payload.document_name,
        content=payload.content,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=payload.version_index,
    )
    session.add(doc)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="CREATE_DOCUMENT",
        document_id=doc.id,
        details=f"Created consent document '{payload.document_name}' for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return map_document_to_response(doc)


@router.get("/api/v1/econsent/documents/{id}", response_model=ConsentDocumentResponse)
async def get_consent_document(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentDocumentResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Standard document access")

    stmt = select(ConsentDocument).where(ConsentDocument.id == id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=404, detail=f"Consent document with ID '{id}' not found."
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="VIEW_DOCUMENT",
        document_id=doc.id,
        details=f"Viewed consent document '{doc.document_name}' (ID: {doc.id}).",
        reason_for_change=change_reason,
    )

    return map_document_to_response(doc)


@router.post(
    "/api/v1/econsent/clauses",
    response_model=ConsentClauseResponse,
    status_code=201,
)
async def create_consent_clause(
    request: Request,
    payload: ConsentClauseCreate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentClauseResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    clause_id = payload.clause_id or str(uuid.uuid4())

    clause = ConsentClause(
        clause_id=clause_id,
        study_id=payload.study_id,
        title=payload.title,
        text=payload.text,
        version_index=1,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(clause)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="INGEST",
        document_id=clause.id,
        details=f"Ingested clause '{clause_id}' version 1 for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return clause


@router.put(
    "/api/v1/econsent/clauses/{clause_id}",
    response_model=ConsentClauseResponse,
)
async def update_consent_clause(
    request: Request,
    clause_id: str,
    payload: ConsentClauseUpdate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentClauseResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    stmt = (
        select(ConsentClause)
        .where(
            ConsentClause.clause_id == clause_id,
            ConsentClause.study_id == payload.study_id,
        )
        .order_by(desc(ConsentClause.version_index))
    )
    result = await session.execute(stmt)
    existing = result.scalars().all()

    if not existing:
        raise HTTPException(
            status_code=404,
            detail=f"Clause with ID '{clause_id}' not found for study '{payload.study_id}'.",
        )

    next_version = existing[0].version_index + 1

    clause = ConsentClause(
        clause_id=clause_id,
        study_id=payload.study_id,
        title=payload.title,
        text=payload.text,
        version_index=next_version,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(clause)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="UPDATE",
        document_id=clause.id,
        details=f"Updated clause '{clause_id}' to version {next_version} for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return clause


@router.get(
    "/api/v1/econsent/clauses",
    response_model=list[ConsentClauseResponse],
)
async def list_consent_clauses(
    request: Request,
    study_id: str | None = None,
    clause_id: str | None = None,
    all_versions: bool = False,
    session: AsyncSession = Depends(get_db_session),
) -> list[ConsentClauseResponse]:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "List clauses")

    stmt = select(ConsentClause)
    if study_id:
        stmt = stmt.where(ConsentClause.study_id == study_id)
    if clause_id:
        stmt = stmt.where(ConsentClause.clause_id == clause_id)
    stmt = stmt.order_by(ConsentClause.clause_id, desc(ConsentClause.version_index))

    result = await session.execute(stmt)
    clauses_list = result.scalars().all()

    if not all_versions:
        seen = set()
        latest_clauses = []
        for c in clauses_list:
            if c.clause_id not in seen:
                seen.add(c.clause_id)
                latest_clauses.append(c)
        clauses_list = latest_clauses

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="LIST",
        document_id=None,
        details=f"Listed clauses (study_id: {study_id}, clause_id: {clause_id}, all_versions: {all_versions}).",
        reason_for_change=change_reason,
    )

    return clauses_list


@router.get(
    "/api/v1/econsent/clauses/{clause_id}",
    response_model=ConsentClauseResponse,
)
async def get_consent_clause(
    request: Request,
    clause_id: str,
    version_index: int | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentClauseResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Retrieve clause")

    stmt = select(ConsentClause).where(ConsentClause.clause_id == clause_id)
    if version_index is not None:
        stmt = stmt.where(ConsentClause.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(ConsentClause.version_index))

    result = await session.execute(stmt)
    clause = result.scalars().first()

    if not clause:
        raise HTTPException(
            status_code=404,
            detail=f"Clause '{clause_id}' not found.",
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW",
        document_id=clause.id,
        details=f"Viewed clause '{clause_id}' version {clause.version_index}.",
        reason_for_change=change_reason,
    )

    return clause


@router.post(
    "/api/v1/econsent/templates",
    response_model=ConsentTemplateResponse,
    status_code=201,
)
async def create_consent_template(
    request: Request,
    payload: ConsentTemplateCreate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTemplateResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    template_id = payload.template_id or str(uuid.uuid4())

    template = ConsentTemplate(
        template_id=template_id,
        study_id=payload.study_id,
        template_name=payload.template_name,
        protocol_version=payload.protocol_version,
        requires_reconsent=payload.requires_reconsent,
        clauses=payload.clauses,
        workflow_steps=payload.workflow_steps,
        is_published=False,
        version_index=1,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(template)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="INGEST",
        document_id=template.id,
        details=f"Ingested template '{template_id}' version 1 for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return template


@router.get(
    "/api/v1/econsent/archival-status/{correlation_id}",
    response_model=ArchivalDeliveryResponse,
)
async def get_archival_status_endpoint(
    correlation_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> ArchivalDeliveryResponse:
    stmt = select(EtmfArchivalDelivery).where(
        EtmfArchivalDelivery.correlation_id == correlation_id
    )
    result = await session.execute(stmt)
    delivery = result.scalars().first()

    if not delivery:
        raise HTTPException(
            status_code=404,
            detail=f"Archival delivery record for correlation ID '{correlation_id}' not found.",
        )

    return delivery


@router.get(
    "/api/v1/econsent/archival-status",
    response_model=ArchivalDeliveryResponse,
)
async def get_archival_status_query_endpoint(
    correlation_id: str | None = None,
    template_id: str | None = None,
    version_index: int | None = None,
    subject_pseudonym: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> ArchivalDeliveryResponse:
    if correlation_id:
        stmt = select(EtmfArchivalDelivery).where(
            EtmfArchivalDelivery.correlation_id == correlation_id
        )
    elif template_id and version_index is not None and subject_pseudonym:
        stmt = select(EtmfArchivalDelivery).where(
            EtmfArchivalDelivery.template_id == template_id,
            EtmfArchivalDelivery.version_index == version_index,
            EtmfArchivalDelivery.subject_pseudonym == subject_pseudonym,
        )
    else:
        raise HTTPException(
            status_code=400,
            detail="Must provide either correlation_id or all of (template_id, version_index, subject_pseudonym)",
        )

    result = await session.execute(stmt)
    delivery = result.scalars().first()

    if not delivery:
        raise HTTPException(
            status_code=404,
            detail="Archival delivery record not found.",
        )

    return delivery


@router.post(
    "/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent",
    response_model=SubjectConsentResponse,
    status_code=201,
)
async def capture_subject_consent(
    request: Request,
    template_id: str,
    version_index: int,
    payload: SubjectConsentCaptureRequest,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> SubjectConsentResponse:
    user_id = getattr(request.state, "user_id", "patient")
    user_role = getattr(request.state, "roles", "patient")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    stmt_tpl = select(ConsentTemplate).where(
        ConsentTemplate.template_id == template_id,
        ConsentTemplate.version_index == version_index,
    )
    res_tpl = await session.execute(stmt_tpl)
    template = res_tpl.scalars().first()
    if not template or not template.is_published:
        raise HTTPException(
            status_code=400,
            detail=f"Template '{template_id}' version {version_index} does not exist or is not published.",
        )

    stmt_result = select(ComprehensionResult).where(
        ComprehensionResult.template_id == template_id,
        ComprehensionResult.version_index == version_index,
        ComprehensionResult.subject_pseudonym == payload.subject_pseudonym,
        ComprehensionResult.passed.is_(True),
    )
    res_result = await session.execute(stmt_result)
    passing_result = res_result.scalars().first()
    if not passing_result:
        raise HTTPException(
            status_code=400,
            detail="Cannot capture consent. Comprehension checks have not been completed or passed for this template version.",
        )

    sig_token = request.headers.get("X-Sig-Token")
    from packages.security.sig_token_verifier import verify_and_consume_sig_token

    sig_payload = verify_and_consume_sig_token(sig_token, user_id)
    secret = os.getenv(
        "GATEWAY_SECRET", default="internal-gateway-secret-12345"
    ).encode()

    bound_action = sig_payload.get("action", "")
    request_path = request.url.path
    if (
        "capture-consent" not in bound_action
        and request_path != bound_action
        and bound_action not in request_path
        and request_path not in bound_action
    ):
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    from packages.security.signing import (
        canonical_serialize,
        compute_sha256_hash,
        generate_canonical_signature,
    )

    server_timestamp = datetime.now(UTC)

    def normalize_timestamp_str(dt) -> str | None:
        if not dt:
            return None
        if isinstance(dt, datetime):
            return dt.strftime("%Y-%m-%dT%H:%M:%S")
        if isinstance(dt, str):
            try:
                from dateutil.parser import parse

                return parse(dt).strftime("%Y-%m-%dT%H:%M:%S")
            except Exception:
                return dt
        return str(dt)

    canonical_payload = {
        "subject_pseudonym": payload.subject_pseudonym,
        "study_id": template.study_id,
        "site_id": payload.site_id,
        "template_id": template_id,
        "version_index": version_index,
        "protocol_version": template.protocol_version,
        "source_content_identity": payload.source_content_identity,
        "server_timestamp": normalize_timestamp_str(server_timestamp),
        "device_timestamp": normalize_timestamp_str(payload.device_timestamp),
    }

    canonical_bytes = canonical_serialize(canonical_payload)
    canonical_hash = compute_sha256_hash(canonical_bytes)
    hmac_sig = generate_canonical_signature(canonical_payload, secret)

    from packages.security.signature import SignatureManifestation, SigningReason

    manifest = SignatureManifestation(
        signer_id=user_id,
        timestamp=server_timestamp,
        signing_reason=SigningReason.APPROVAL,
        ip_address=request.client.host if request.client else "127.0.0.1",
        user_agent=request.headers.get("user-agent") or "Unknown",
        sha256_hash=canonical_hash,
    )

    signature_manifest_data = {
        "signature_manifestation": manifest.model_dump(mode="json"),
        "canonical_signature": hmac_sig,
        "canonical_payload_hash": canonical_hash,
    }

    sc = SubjectConsent(
        subject_pseudonym=payload.subject_pseudonym,
        study_id=template.study_id,
        site_id=payload.site_id,
        template_id=template_id,
        version_index=version_index,
        protocol_version=template.protocol_version,
        source_content_identity=payload.source_content_identity,
        server_timestamp=server_timestamp,
        device_timestamp=payload.device_timestamp,
        signature_manifest=signature_manifest_data,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(sc)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="CAPTURE_CONSENT",
        document_id=sc.id,
        details=f"Canonically captured signed subject consent for template '{template_id}' version {version_index}.",
        reason_for_change=change_reason,
    )

    publish_consent_completed_event(
        sc.subject_pseudonym,
        sc.study_id,
        sc.site_id,
        sc.protocol_version or "1.0",
        sc.version_index,
    )

    return SubjectConsentResponse(
        id=sc.id,
        subject_pseudonym=sc.subject_pseudonym,
        study_id=sc.study_id,
        site_id=sc.site_id,
        template_id=sc.template_id,
        version_index=sc.version_index,
        protocol_version=sc.protocol_version,
        source_content_identity=sc.source_content_identity,
        server_timestamp=sc.server_timestamp,
        device_timestamp=sc.device_timestamp,
        signature_manifest=sc.signature_manifest,
        created_at=sc.created_at,
        created_by=sc.created_by,
        reason_for_change=sc.reason_for_change,
    )


@router.get(
    "/api/v1/econsent/subjects/{subject_pseudonym}/consent-status",
    response_model=SubjectConsentStatusResponse,
)
async def get_subject_consent_status_endpoint(
    subject_pseudonym: str,
    study_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> SubjectConsentStatusResponse:
    stmt = select(SubjectConsent).where(
        SubjectConsent.subject_pseudonym == subject_pseudonym
    )
    if study_id:
        stmt = stmt.where(SubjectConsent.study_id == study_id)
    stmt = stmt.order_by(desc(SubjectConsent.version_index))
    result = await session.execute(stmt)
    sc = result.scalars().first()

    if not sc:
        raise HTTPException(
            status_code=404,
            detail=f"No signed consent found for subject '{subject_pseudonym}'.",
        )

    requires_reconsent = False
    stmt_tpl = select(ConsentTemplate).where(
        ConsentTemplate.study_id == sc.study_id,
        ConsentTemplate.is_published.is_(True),
        ConsentTemplate.version_index > sc.version_index,
        ConsentTemplate.requires_reconsent.is_(True),
    )
    res_tpl = await session.execute(stmt_tpl)
    higher_tpl = res_tpl.scalars().first()
    if higher_tpl:
        requires_reconsent = True

    return SubjectConsentStatusResponse(
        subject_pseudonym=sc.subject_pseudonym,
        study_id=sc.study_id,
        site_id=sc.site_id,
        template_id=sc.template_id,
        version_index=sc.version_index,
        protocol_version=sc.protocol_version,
        signed=True,
        comprehension_passed=True,
        requires_reconsent=requires_reconsent,
    )


@router.post(
    "/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks",
    response_model=ComprehensionCheckResponse,
    status_code=201,
)
async def create_or_update_comprehension_check(
    request: Request,
    template_id: str,
    version_index: int,
    payload: ComprehensionCheckCreate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ComprehensionCheckResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    stmt_tpl = select(ConsentTemplate).where(
        ConsentTemplate.template_id == template_id,
        ConsentTemplate.version_index == version_index,
    )
    res_tpl = await session.execute(stmt_tpl)
    template = res_tpl.scalars().first()
    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Consent template '{template_id}' version {version_index} not found.",
        )

    stmt_check = select(ComprehensionCheck).where(
        ComprehensionCheck.template_id == template_id,
        ComprehensionCheck.version_index == version_index,
    )
    res_check = await session.execute(stmt_check)
    check = res_check.scalars().first()

    if check:
        check.questions = payload.questions
        check.expected_answers = payload.expected_answers
        check.threshold_policy = payload.threshold_policy
        check.created_by = user_id
        check.reason_for_change = change_reason
    else:
        check = ComprehensionCheck(
            template_id=template_id,
            version_index=version_index,
            questions=payload.questions,
            expected_answers=payload.expected_answers,
            threshold_policy=payload.threshold_policy,
            created_by=user_id,
            reason_for_change=change_reason,
        )
        session.add(check)

    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="DEFINE_COMPREHENSION_CHECK",
        document_id=check.id,
        details=f"Defined comprehension check for template '{template_id}' version {version_index}.",
        reason_for_change=change_reason,
    )

    return ComprehensionCheckResponse(
        id=check.id,
        template_id=check.template_id,
        version_index=check.version_index,
        questions=check.questions,
        expected_answers=check.expected_answers,
        threshold_policy=check.threshold_policy,
        created_at=check.created_at,
        created_by=check.created_by,
        reason_for_change=check.reason_for_change,
    )


@router.post(
    "/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers",
    response_model=ComprehensionSubmissionResponse,
)
async def submit_comprehension_answers_endpoint(
    request: Request,
    template_id: str,
    version_index: int,
    payload: ComprehensionSubmissionRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ComprehensionSubmissionResponse:
    user_id = getattr(request.state, "user_id", "patient")
    user_role = getattr(request.state, "roles", "patient")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    stmt_tpl = select(ConsentTemplate).where(
        ConsentTemplate.template_id == template_id,
        ConsentTemplate.version_index == version_index,
    )
    res_tpl = await session.execute(stmt_tpl)
    template = res_tpl.scalars().first()
    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Consent template '{template_id}' version {version_index} not found.",
        )

    try:
        result = await submit_comprehension_answers(
            session=session,
            template_id=template_id,
            version_index=version_index,
            subject_pseudonym=payload.subject_pseudonym,
            submitted_answers=payload.submitted_answers,
            created_by=user_id,
            reason_for_change=change_reason,
        )
    except ValueError as e:
        raise HTTPException(
            status_code=400,
            detail=str(e),
        )

    total_questions = len(result.expected_answers)
    correct_count = 0
    for q_id, expected_val in result.expected_answers.items():
        sub_val = result.submitted_answers.get(q_id)
        if (
            sub_val is not None
            and str(sub_val).strip().lower() == str(expected_val).strip().lower()
        ):
            correct_count += 1

    import math

    if "min_correct" in result.threshold_policy:
        min_required = int(result.threshold_policy["min_correct"])
    elif "passing_percentage" in result.threshold_policy:
        min_required = math.ceil(
            (float(result.threshold_policy["passing_percentage"]) / 100.0)
            * total_questions
        )
    else:
        min_required = total_questions

    if result.passed:
        next_step = "sign_consent"
        message = "Congratulations! You have passed the comprehension check and can proceed to sign the consent form."
    else:
        next_step = "retry_checks"
        message = "You did not meet the passing threshold. Please review the material and try again."

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="COMPREHENSION_EVALUATION",
        document_id=result.id,
        details=f"Evaluated comprehension answers for template '{template_id}' version {version_index}, subject '{payload.subject_pseudonym}'. Score: {result.score}%. Passed: {result.passed}.",
        reason_for_change=change_reason,
    )

    return ComprehensionSubmissionResponse(
        passed=result.passed,
        score=result.score,
        total_questions=total_questions,
        correct_count=correct_count,
        min_required=min_required,
        next_step=next_step,
        message=message,
    )


@router.post(
    "/api/v1/econsent/templates/{template_id}/versions/{version_index}/sign",
    response_model=ConsentSignatureResponse,
)
async def sign_consent_template_endpoint(
    request: Request,
    template_id: str,
    version_index: int,
    payload: ConsentSignatureRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentSignatureResponse:
    user_id = getattr(request.state, "user_id", "patient")
    user_role = getattr(request.state, "roles", "patient")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    stmt_tpl = select(ConsentTemplate).where(
        ConsentTemplate.template_id == template_id,
        ConsentTemplate.version_index == version_index,
    )
    res_tpl = await session.execute(stmt_tpl)
    template = res_tpl.scalars().first()
    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Consent template '{template_id}' version {version_index} not found.",
        )

    stmt_check = select(ComprehensionCheck).where(
        ComprehensionCheck.template_id == template_id,
        ComprehensionCheck.version_index == version_index,
    )
    res_check = await session.execute(stmt_check)
    check = res_check.scalars().first()

    has_comprehension_step = any(
        step.get("type")
        in ("comprehension_check", "comprehension-check", "comprehension")
        or step.get("step_type")
        in ("comprehension_check", "comprehension-check", "comprehension")
        for step in template.workflow_steps
    )

    if check or has_comprehension_step:
        stmt_result = select(ComprehensionResult).where(
            ComprehensionResult.template_id == template_id,
            ComprehensionResult.version_index == version_index,
            ComprehensionResult.subject_pseudonym == payload.subject_pseudonym,
            ComprehensionResult.passed.is_(True),
        )
        res_result = await session.execute(stmt_result)
        passing_result = res_result.scalars().first()

        if not passing_result:
            raise HTTPException(
                status_code=400,
                detail="Cannot sign consent. Comprehension checks have not been completed or passed for this template version.",
            )

    sig = ConsentSignature(
        template_id=template_id,
        version_index=version_index,
        subject_pseudonym=payload.subject_pseudonym,
        signature_data=payload.signature_data,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(sig)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="SIGN_CONSENT",
        document_id=sig.id,
        details=f"Subject '{payload.subject_pseudonym}' signed consent template '{template_id}' version {version_index}.",
        reason_for_change=change_reason,
    )

    import json

    stmt_trans = (
        select(ConsentTranslation.language_code)
        .where(
            ConsentTranslation.source_id == template_id,
            ConsentTranslation.source_type == "template",
            ConsentTranslation.source_version_index == version_index,
            ConsentTranslation.status == "APPROVED",
        )
        .order_by(desc(ConsentTranslation.version_index))
    )
    res_trans = await session.execute(stmt_trans)
    trans_lang = res_trans.scalars().first()
    lang_code = trans_lang or "en"

    try:
        composed_data = await fetch_composed_translation_from_db(
            template_id, version_index, lang_code, session
        )
    except Exception:
        composed_clauses = []
        for clause_id in template.clauses:
            clause_stmt = (
                select(ConsentClause)
                .where(
                    ConsentClause.clause_id == clause_id,
                    ConsentClause.study_id == template.study_id,
                )
                .order_by(desc(ConsentClause.version_index))
            )
            clause_res = await session.execute(clause_stmt)
            clause = clause_res.scalars().first()
            if clause:
                composed_clauses.append(
                    {
                        "clause_id": clause.clause_id,
                        "title": clause.title,
                        "text": clause.text,
                        "version_index": clause.version_index,
                    }
                )
        composed_data = {
            "id": template.id,
            "template_id": template.template_id,
            "study_id": template.study_id,
            "template_name": template.template_name,
            "protocol_version": template.protocol_version,
            "language_code": lang_code,
            "is_published": template.is_published,
            "requires_reconsent": template.requires_reconsent,
            "version_index": template.version_index,
            "clauses": composed_clauses,
            "workflow_steps": template.workflow_steps,
        }

    manifest_and_sig = {
        "manifest": composed_data,
        "signature_metadata": {
            "subject_pseudonym": payload.subject_pseudonym,
            "signed_at": sig.signed_at.isoformat()
            if sig.signed_at
            else datetime.now(UTC).isoformat(),
            "created_by": user_id,
            "signature_data": payload.signature_data,
        },
    }
    artifact_content = json.dumps(manifest_and_sig)
    correlation_id = f"{template_id}:{version_index}:{payload.subject_pseudonym}"

    delivery = EtmfArchivalDelivery(
        status="PENDING",
        attempts=0,
        correlation_id=correlation_id,
        template_id=template_id,
        version_index=version_index,
        subject_pseudonym=payload.subject_pseudonym,
        study_id=template.study_id,
        site_id=payload.site_id,
        artifact_content=artifact_content,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(delivery)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="ARCHIVAL_QUEUED",
        document_id=delivery.id,
        details=f"Queued ICF archival delivery for template '{template_id}' version {version_index}, subject '{payload.subject_pseudonym}'. Correlation ID: {correlation_id}",
        reason_for_change=change_reason,
    )

    publish_consent_completed_event(
        sig.subject_pseudonym,
        template.study_id,
        payload.site_id,
        template.protocol_version or "1.0",
        sig.version_index,
    )

    return ConsentSignatureResponse(
        id=sig.id,
        template_id=sig.template_id,
        version_index=sig.version_index,
        subject_pseudonym=sig.subject_pseudonym,
        signature_data=sig.signature_data,
        signed_at=sig.signed_at,
        created_by=sig.created_by,
        reason_for_change=sig.reason_for_change,
    )


@router.get(
    "/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks",
    response_model=ComprehensionCheckResponse,
)
async def get_comprehension_check(
    request: Request,
    template_id: str,
    version_index: int,
    session: AsyncSession = Depends(get_db_session),
) -> ComprehensionCheckResponse:
    stmt = select(ComprehensionCheck).where(
        ComprehensionCheck.template_id == template_id,
        ComprehensionCheck.version_index == version_index,
    )
    res = await session.execute(stmt)
    check = res.scalars().first()

    if not check:
        raise HTTPException(
            status_code=404,
            detail=f"Comprehension check for template '{template_id}' version {version_index} not found.",
        )

    return ComprehensionCheckResponse(
        id=check.id,
        template_id=check.template_id,
        version_index=check.version_index,
        questions=check.questions,
        expected_answers=check.expected_answers,
        threshold_policy=check.threshold_policy,
        created_at=check.created_at,
        created_by=check.created_by,
        reason_for_change=check.reason_for_change,
    )


@router.post(
    "/api/v1/econsent/translations",
    response_model=ConsentTranslationResponse,
    status_code=201,
)
async def create_consent_translation(
    request: Request,
    payload: ConsentTranslationCreate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTranslationResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    if payload.source_type == "clause":
        stmt = select(ConsentClause).where(
            ConsentClause.clause_id == payload.source_id,
            ConsentClause.version_index == payload.source_version_index,
        )
        res = await session.execute(stmt)
        if not res.scalars().first():
            raise HTTPException(
                status_code=400,
                detail=f"Source clause '{payload.source_id}' version {payload.source_version_index} not found.",
            )
    elif payload.source_type == "template":
        stmt = select(ConsentTemplate).where(
            ConsentTemplate.template_id == payload.source_id,
            ConsentTemplate.version_index == payload.source_version_index,
        )
        res = await session.execute(stmt)
        if not res.scalars().first():
            raise HTTPException(
                status_code=400,
                detail=f"Source template '{payload.source_id}' version {payload.source_version_index} not found.",
            )

    translation_id = payload.translation_id or str(uuid.uuid4())

    translation = ConsentTranslation(
        translation_id=translation_id,
        source_id=payload.source_id,
        source_type=payload.source_type,
        source_version_index=payload.source_version_index,
        language_code=payload.language_code,
        translated_title=payload.translated_title,
        translated_text=payload.translated_text,
        status="DRAFT",
        version_index=1,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(translation)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="CREATE_TRANSLATION",
        document_id=translation.id,
        details=f"Created translation '{translation_id}' draft version 1 for {payload.source_type} '{payload.source_id}' in '{payload.language_code}'.",
        reason_for_change=change_reason,
    )

    return translation


@router.put(
    "/api/v1/econsent/translations/{translation_id}",
    response_model=ConsentTranslationResponse,
)
async def update_consent_translation(
    request: Request,
    translation_id: str,
    payload: ConsentTranslationUpdate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTranslationResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    stmt = (
        select(ConsentTranslation)
        .where(ConsentTranslation.translation_id == translation_id)
        .order_by(desc(ConsentTranslation.version_index))
    )
    result = await session.execute(stmt)
    existing = result.scalars().all()

    if not existing:
        raise HTTPException(
            status_code=404,
            detail=f"Translation with ID '{translation_id}' not found.",
        )

    next_version = existing[0].version_index + 1

    translation = ConsentTranslation(
        translation_id=translation_id,
        source_id=payload.source_id,
        source_type=payload.source_type,
        source_version_index=payload.source_version_index,
        language_code=payload.language_code,
        translated_title=payload.translated_title,
        translated_text=payload.translated_text,
        status="DRAFT",
        version_index=next_version,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(translation)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="UPDATE_TRANSLATION",
        document_id=translation.id,
        details=f"Updated translation '{translation_id}' to version {next_version} for {payload.source_type} '{payload.source_id}' in '{payload.language_code}'.",
        reason_for_change=change_reason,
    )

    return translation


@router.get(
    "/api/v1/econsent/translations",
    response_model=list[ConsentTranslationResponse],
)
async def list_consent_translations(
    request: Request,
    source_id: str | None = None,
    source_type: str | None = None,
    language_code: str | None = None,
    status: str | None = None,
    all_versions: bool = False,
    session: AsyncSession = Depends(get_db_session),
) -> list[ConsentTranslationResponse]:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "List translations")

    stmt = select(ConsentTranslation)
    if source_id:
        stmt = stmt.where(ConsentTranslation.source_id == source_id)
    if source_type:
        stmt = stmt.where(ConsentTranslation.source_type == source_type)
    if language_code:
        stmt = stmt.where(ConsentTranslation.language_code == language_code)
    if status:
        stmt = stmt.where(ConsentTranslation.status == status)

    stmt = stmt.order_by(
        ConsentTranslation.translation_id, desc(ConsentTranslation.version_index)
    )

    result = await session.execute(stmt)
    translations_list = result.scalars().all()

    if not all_versions:
        seen = set()
        latest_translations = []
        for t in translations_list:
            if t.translation_id not in seen:
                seen.add(t.translation_id)
                latest_translations.append(t)
        translations_list = latest_translations

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="LIST_TRANSLATIONS",
        document_id=None,
        details=f"Listed translations (source_id: {source_id}, language_code: {language_code}, all_versions: {all_versions}).",
        reason_for_change=change_reason,
    )

    return translations_list


@router.get(
    "/api/v1/econsent/translations/{translation_id}",
    response_model=ConsentTranslationResponse,
)
async def get_consent_translation(
    request: Request,
    translation_id: str,
    version_index: int | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTranslationResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Retrieve translation")

    stmt = select(ConsentTranslation).where(
        ConsentTranslation.translation_id == translation_id
    )
    if version_index is not None:
        stmt = stmt.where(ConsentTranslation.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(ConsentTranslation.version_index))

    result = await session.execute(stmt)
    translation = result.scalars().first()

    if not translation:
        raise HTTPException(
            status_code=404,
            detail=f"Translation '{translation_id}' not found.",
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW_TRANSLATION",
        document_id=translation.id,
        details=f"Viewed translation '{translation_id}' version {translation.version_index}.",
        reason_for_change=change_reason,
    )

    return translation


@router.post(
    "/api/v1/econsent/translations/{translation_id}/transition",
    response_model=ConsentTranslationResponse,
)
async def transition_consent_translation(
    request: Request,
    translation_id: str,
    payload: TranslationTransitionRequest,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTranslationResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    stmt = (
        select(ConsentTranslation)
        .where(ConsentTranslation.translation_id == translation_id)
        .order_by(desc(ConsentTranslation.version_index))
    )
    result = await session.execute(stmt)
    translation = result.scalars().first()

    if not translation:
        raise HTTPException(
            status_code=404,
            detail=f"Translation '{translation_id}' not found.",
        )

    current_status = translation.status
    target_status = payload.status

    if current_status == target_status:
        return translation

    allowed = False
    if (
        current_status == "DRAFT"
        and target_status == "IN_REVIEW"
        or current_status == "IN_REVIEW"
        and target_status in ("APPROVED", "DRAFT")
    ):
        allowed = True

    if not allowed:
        raise HTTPException(
            status_code=400,
            detail=f"Invalid translation status transition from '{current_status}' to '{target_status}'.",
        )

    translation.status = target_status
    translation.reason_for_change = change_reason
    translation.created_by = user_id
    session.add(translation)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="TRANSITION_TRANSLATION",
        document_id=translation.id,
        details=f"Transitioned translation '{translation_id}' from '{current_status}' to '{target_status}'.",
        reason_for_change=change_reason,
    )

    if target_status == "APPROVED":
        if translation.source_type == "template":
            approved_translation_cache.invalidate(
                translation.source_id,
                translation.source_version_index,
                translation.language_code,
            )
        elif translation.source_type == "clause":
            approved_translation_cache.clear()

    return translation


async def fetch_composed_translation_from_db(
    template_id: str,
    version_index: int,
    language_code: str,
    session: AsyncSession,
) -> dict:
    stmt = select(ConsentTemplate).where(
        ConsentTemplate.template_id == template_id,
        ConsentTemplate.version_index == version_index,
    )
    result = await session.execute(stmt)
    template = result.scalars().first()
    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Consent template '{template_id}' version {version_index} not found.",
        )

    tpl_trans_stmt = (
        select(ConsentTranslation)
        .where(
            ConsentTranslation.source_id == template_id,
            ConsentTranslation.source_type == "template",
            ConsentTranslation.source_version_index == version_index,
            ConsentTranslation.language_code == language_code,
            ConsentTranslation.status == "APPROVED",
        )
        .order_by(desc(ConsentTranslation.version_index))
    )
    tpl_trans_res = await session.execute(tpl_trans_stmt)
    tpl_translation = tpl_trans_res.scalars().first()

    if not tpl_translation:
        raise HTTPException(
            status_code=404,
            detail=f"Approved template translation for '{template_id}' (version {version_index}) in '{language_code}' not found.",
        )

    composed_clauses = []
    for clause_id in template.clauses:
        clause_stmt = (
            select(ConsentClause)
            .where(
                ConsentClause.clause_id == clause_id,
                ConsentClause.study_id == template.study_id,
            )
            .order_by(desc(ConsentClause.version_index))
        )
        clause_res = await session.execute(clause_stmt)
        clause = clause_res.scalars().first()

        if not clause:
            raise HTTPException(
                status_code=404,
                detail=f"Referenced clause '{clause_id}' not found.",
            )

        clause_trans_stmt = (
            select(ConsentTranslation)
            .where(
                ConsentTranslation.source_id == clause_id,
                ConsentTranslation.source_type == "clause",
                ConsentTranslation.source_version_index == clause.version_index,
                ConsentTranslation.language_code == language_code,
                ConsentTranslation.status == "APPROVED",
            )
            .order_by(desc(ConsentTranslation.version_index))
        )
        clause_trans_res = await session.execute(clause_trans_stmt)
        clause_translation = clause_trans_res.scalars().first()

        if not clause_translation:
            raise HTTPException(
                status_code=404,
                detail=f"Approved clause translation for '{clause_id}' (version {clause.version_index}) in '{language_code}' not found.",
            )

        composed_clauses.append(
            {
                "clause_id": clause.clause_id,
                "title": clause_translation.translated_title,
                "text": clause_translation.translated_text,
                "version_index": clause.version_index,
            }
        )

    return {
        "id": template.id,
        "template_id": template.template_id,
        "study_id": template.study_id,
        "template_name": tpl_translation.translated_title,
        "protocol_version": template.protocol_version,
        "language_code": language_code,
        "is_published": template.is_published,
        "requires_reconsent": template.requires_reconsent,
        "version_index": template.version_index,
        "clauses": composed_clauses,
        "workflow_steps": template.workflow_steps,
    }


@router.get(
    "/api/v1/econsent/templates/{template_id}/approved-content",
)
async def get_approved_composed_template(
    request: Request,
    template_id: str,
    language_code: str,
    version_index: int | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> dict:
    if version_index is None:
        stmt = (
            select(ConsentTemplate)
            .where(ConsentTemplate.template_id == template_id)
            .order_by(desc(ConsentTemplate.version_index))
        )
        res = await session.execute(stmt)
        templates = res.scalars().all()
        if not templates:
            raise HTTPException(
                status_code=404,
                detail=f"Template '{template_id}' not found.",
            )
        published_tpls = [t for t in templates if t.is_published]
        if published_tpls:
            version_index = published_tpls[0].version_index
        else:
            version_index = templates[0].version_index

    from apps.econsent.domain.localization.models import validate_language_code

    try:
        language_code = validate_language_code(language_code)
    except ValueError as e:
        raise HTTPException(
            status_code=400,
            detail=str(e),
        )

    async def fetch_db_fn(tid: str, vidx: int, lang: str) -> dict:
        import apps.econsent.main as main_module

        return await main_module.fetch_composed_translation_from_db(
            tid, vidx, lang, session
        )

    try:
        composed_data = await get_approved_template_translation(
            approved_translation_cache,
            template_id,
            version_index,
            language_code,
            fetch_db_fn,
        )
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(
            status_code=500,
            detail=f"Database retrieval error: {str(e)}",
        )

    user_id = getattr(request.state, "user_id", "patient")
    user_role = getattr(request.state, "roles", "patient")
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW_APPROVED_TRANSLATION",
        document_id=composed_data.get("id"),
        details=f"Viewed approved composed template translation for '{template_id}' version {version_index} in '{language_code}'.",
        reason_for_change="Standard translation retrieval",
    )

    return composed_data


@router.put(
    "/api/v1/econsent/templates/{template_id}",
    response_model=ConsentTemplateResponse,
)
async def update_consent_template(
    request: Request,
    template_id: str,
    payload: ConsentTemplateUpdate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTemplateResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    stmt = (
        select(ConsentTemplate)
        .where(
            ConsentTemplate.template_id == template_id,
            ConsentTemplate.study_id == payload.study_id,
        )
        .order_by(desc(ConsentTemplate.version_index))
    )
    result = await session.execute(stmt)
    existing = result.scalars().all()

    if not existing:
        raise HTTPException(
            status_code=404,
            detail=f"Template with ID '{template_id}' not found for study '{payload.study_id}'.",
        )

    next_version = existing[0].version_index + 1

    template = ConsentTemplate(
        template_id=template_id,
        study_id=payload.study_id,
        template_name=payload.template_name,
        protocol_version=payload.protocol_version,
        requires_reconsent=payload.requires_reconsent,
        clauses=payload.clauses,
        workflow_steps=payload.workflow_steps,
        is_published=False,
        version_index=next_version,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(template)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="UPDATE",
        document_id=template.id,
        details=f"Updated template '{template_id}' to version {next_version} for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return template


@router.get(
    "/api/v1/econsent/templates",
    response_model=list[ConsentTemplateResponse],
)
async def list_consent_templates(
    request: Request,
    study_id: str | None = None,
    template_id: str | None = None,
    all_versions: bool = False,
    session: AsyncSession = Depends(get_db_session),
) -> list[ConsentTemplateResponse]:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "List templates")

    stmt = select(ConsentTemplate)
    if study_id:
        stmt = stmt.where(ConsentTemplate.study_id == study_id)
    if template_id:
        stmt = stmt.where(ConsentTemplate.template_id == template_id)
    stmt = stmt.order_by(
        ConsentTemplate.template_id, desc(ConsentTemplate.version_index)
    )

    result = await session.execute(stmt)
    templates_list = result.scalars().all()

    if not all_versions:
        seen = set()
        latest_templates = []
        for t in templates_list:
            if t.template_id not in seen:
                seen.add(t.template_id)
                latest_templates.append(t)
        templates_list = latest_templates

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="LIST",
        document_id=None,
        details=f"Listed templates (study_id: {study_id}, template_id: {template_id}, all_versions: {all_versions}).",
        reason_for_change=change_reason,
    )

    return templates_list


@router.get(
    "/api/v1/econsent/templates/{template_id}",
    response_model=ConsentTemplateResponse,
)
async def get_consent_template(
    request: Request,
    template_id: str,
    version_index: int | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTemplateResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Retrieve template")

    stmt = select(ConsentTemplate).where(ConsentTemplate.template_id == template_id)
    if version_index is not None:
        stmt = stmt.where(ConsentTemplate.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(ConsentTemplate.version_index))

    result = await session.execute(stmt)
    template = result.scalars().first()

    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Template '{template_id}' not found.",
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW",
        document_id=template.id,
        details=f"Viewed template '{template_id}' version {template.version_index}.",
        reason_for_change=change_reason,
    )

    return template


@router.get(
    "/api/v1/econsent/templates/{template_id}/compose",
    response_model=ComposedTemplateResponse,
)
async def compose_consent_template(
    request: Request,
    template_id: str,
    version_index: int | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> ComposedTemplateResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Compose template")

    stmt = select(ConsentTemplate).where(ConsentTemplate.template_id == template_id)
    if version_index is not None:
        stmt = stmt.where(ConsentTemplate.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(ConsentTemplate.version_index))

    result = await session.execute(stmt)
    template = result.scalars().first()

    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Template '{template_id}' not found.",
        )

    composed_clauses = []
    for clause_id in template.clauses:
        clause_stmt = (
            select(ConsentClause)
            .where(
                ConsentClause.clause_id == clause_id,
                ConsentClause.study_id == template.study_id,
            )
            .order_by(desc(ConsentClause.version_index))
        )
        clause_res = await session.execute(clause_stmt)
        clause = clause_res.scalars().first()

        if not clause:
            raise HTTPException(
                status_code=404,
                detail=f"Referenced clause '{clause_id}' not found for study '{template.study_id}'.",
            )

        composed_clauses.append(
            ComposedClauseResponse(
                clause_id=clause.clause_id,
                title=clause.title,
                text=clause.text,
                version_index=clause.version_index,
            )
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW",
        document_id=template.id,
        details=f"Composed template '{template_id}' version {template.version_index}.",
        reason_for_change=change_reason,
    )

    return ComposedTemplateResponse(
        id=template.id,
        template_id=template.template_id,
        study_id=template.study_id,
        template_name=template.template_name,
        protocol_version=template.protocol_version,
        is_published=template.is_published,
        requires_reconsent=template.requires_reconsent,
        version_index=template.version_index,
        clauses=composed_clauses,
        workflow_steps=template.workflow_steps,
        created_at=template.created_at,
        created_by=template.created_by,
        reason_for_change=template.reason_for_change,
    )


@router.post(
    "/api/v1/econsent/templates/{template_id}/publish",
    response_model=ConsentTemplateResponse,
)
async def publish_consent_template(
    request: Request,
    template_id: str,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTemplateResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Publish template")

    stmt = (
        select(ConsentTemplate)
        .where(ConsentTemplate.template_id == template_id)
        .order_by(desc(ConsentTemplate.version_index))
    )
    result = await session.execute(stmt)
    template = result.scalars().first()

    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Template '{template_id}' not found.",
        )

    if template.is_published:
        raise HTTPException(
            status_code=400,
            detail=f"Template '{template_id}' is already published.",
        )

    for clause_id in template.clauses:
        clause_stmt = select(ConsentClause).where(
            ConsentClause.clause_id == clause_id,
            ConsentClause.study_id == template.study_id,
        )
        clause_res = await session.execute(clause_stmt)
        if not clause_res.scalars().first():
            raise HTTPException(
                status_code=400,
                detail=f"Validation failed: Referenced clause '{clause_id}' does not exist for study '{template.study_id}'.",
            )

    has_comprehension = any(
        step.get("type")
        in ("comprehension_check", "comprehension-check", "comprehension")
        or step.get("step_type")
        in ("comprehension_check", "comprehension-check", "comprehension")
        for step in template.workflow_steps
    )
    has_signature = any(
        step.get("type")
        in ("signature_placeholder", "signature-placeholder", "signature")
        or step.get("step_type")
        in ("signature_placeholder", "signature-placeholder", "signature")
        for step in template.workflow_steps
    )

    if not has_comprehension:
        raise HTTPException(
            status_code=400,
            detail="Validation failed: Template must include a comprehension-check workflow step.",
        )
    if not has_signature:
        raise HTTPException(
            status_code=400,
            detail="Validation failed: Template must include a signature placeholder workflow step.",
        )

    template.is_published = True
    template.reason_for_change = change_reason
    template.created_by = user_id
    session.add(template)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="UPDATE",
        document_id=template.id,
        details=f"Published template '{template_id}' version {template.version_index}.",
        reason_for_change=change_reason,
    )

    return template


@router.get(
    "/api/v1/econsent/templates/{template_id}/diff/{base_version}/{target_version}",
    response_model=TemplateDiffResponse,
)
async def diff_template_versions(
    template_id: str,
    base_version: int,
    target_version: int,
    session: AsyncSession = Depends(get_db_session),
) -> TemplateDiffResponse:
    """Computes semantic delta and substantive change analysis between two template versions."""
    from apps.econsent.adapters.repositories import (
        SQLConsentAuditRepository,
        SQLConsentClauseRepository,
        SQLConsentTemplateRepository,
    )
    from apps.econsent.application.use_cases import TemplateAuthoringService

    svc = TemplateAuthoringService(
        template_repo=SQLConsentTemplateRepository(session),
        clause_repo=SQLConsentClauseRepository(session),
        audit_repo=SQLConsentAuditRepository(session),
    )
    report = await svc.diff_template_versions(
        template_id=template_id,
        base_version_index=base_version,
        target_version_index=target_version,
    )
    return TemplateDiffResponse(
        template_id=report.template_id,
        base_version_index=report.base_version_index,
        target_version_index=report.target_version_index,
        total_added=report.total_added,
        total_removed=report.total_removed,
        total_modified=report.total_modified,
        total_unchanged=report.total_unchanged,
        requires_reconsent=report.requires_reconsent,
        substantive_summary=report.substantive_summary,
        clause_diffs=[
            ClauseDiffDTO(
                clause_id=d.clause_id,
                change_type=d.change_type,
                old_title=d.old_title,
                new_title=d.new_title,
                old_text=d.old_text,
                new_text=d.new_text,
                text_diff=d.text_diff,
                is_substantive=d.is_substantive,
            )
            for d in report.clause_diffs
        ],
    )
