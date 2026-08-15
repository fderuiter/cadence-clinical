from fastapi import APIRouter, Depends, HTTPException, status

from apps.ctms.adapters.clients import ETMFClient
from apps.ctms.adapters.repositories import (
    SQLAlchemCTMSDelegationRepository,
    SQLAlchemyIPAccountabilityRepository,
    get_ctms_repository,
    get_ip_accountability_repository,
)
from apps.ctms.application.ip_accountability_service import IPAccountabilityService
from apps.ctms.domain.exceptions import IPKitNotFoundError, IPQuarantineError
from apps.ctms.presentation.dtos import (
    IPDestructionCertificateCreate,
    IPDestructionCertificateResponse,
    IPKitDispenseRequest,
    IPKitReconcileRequest,
    IPKitRecordResponse,
    IPShipmentReceiveRequest,
    IPTemperatureExcursionCreate,
    IPTemperatureExcursionDisposition,
    IPTemperatureExcursionResponse,
)
from packages.security.rbac import Principal, get_principal

router = APIRouter(prefix="/api/v1/ctms/ip", tags=["CTMS IP Accountability"])


def get_ip_service(
    ip_repo: SQLAlchemyIPAccountabilityRepository = Depends(
        get_ip_accountability_repository
    ),
    doa_repo: SQLAlchemCTMSDelegationRepository = Depends(get_ctms_repository),
) -> IPAccountabilityService:
    return IPAccountabilityService(
        ip_repo=ip_repo,
        etmf_client=ETMFClient(),
        doa_repo=doa_repo,
    )


@router.post(
    "/shipments/receive",
    response_model=list[IPKitRecordResponse],
    status_code=status.HTTP_201_CREATED,
)
async def receive_ip_shipment(
    payload: IPShipmentReceiveRequest,
    service: IPAccountabilityService = Depends(get_ip_service),
    principal: Principal = Depends(get_principal),
) -> list[IPKitRecordResponse]:
    entities = await service.receive_shipment_kits(
        study_id=payload.study_id,
        site_id=payload.site_id,
        kit_numbers=payload.kit_numbers,
        lot_number=payload.lot_number,
        kit_type=payload.kit_type,
        shipment_tracking_number=payload.shipment_tracking_number,
        expiration_date=payload.expiration_date,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "IP shipment receipt",
    )
    return [
        IPKitRecordResponse(
            id=e.id or "",
            study_id=e.study_id,
            site_id=e.site_id,
            kit_number=e.kit_number,
            lot_number=e.lot_number,
            kit_type=e.kit_type,
            shipment_tracking_number=e.shipment_tracking_number,
            expiration_date=e.expiration_date,
            status=e.status,
            received_date=e.received_date,
            dispensed_subject_id=e.dispensed_subject_id,
            dispensed_visit_id=e.dispensed_visit_id,
            dispensed_date=e.dispensed_date,
            returned_units_count=e.returned_units_count,
            expected_units_count=e.expected_units_count,
            compliance_percentage=e.compliance_percentage,
            notes=e.notes,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.get("/kits", response_model=list[IPKitRecordResponse])
async def list_ip_kits(
    study_id: str,
    site_id: str | None = None,
    status: str | None = None,
    service: IPAccountabilityService = Depends(get_ip_service),
    principal: Principal = Depends(get_principal),
) -> list[IPKitRecordResponse]:
    entities = await service.list_ip_kits(study_id, site_id, status)
    return [
        IPKitRecordResponse(
            id=e.id or "",
            study_id=e.study_id,
            site_id=e.site_id,
            kit_number=e.kit_number,
            lot_number=e.lot_number,
            kit_type=e.kit_type,
            shipment_tracking_number=e.shipment_tracking_number,
            expiration_date=e.expiration_date,
            status=e.status,
            received_date=e.received_date,
            dispensed_subject_id=e.dispensed_subject_id,
            dispensed_visit_id=e.dispensed_visit_id,
            dispensed_date=e.dispensed_date,
            returned_units_count=e.returned_units_count,
            expected_units_count=e.expected_units_count,
            compliance_percentage=e.compliance_percentage,
            notes=e.notes,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.post("/kits/{kit_id}/dispense", response_model=IPKitRecordResponse)
async def dispense_ip_kit(
    kit_id: str,
    payload: IPKitDispenseRequest,
    service: IPAccountabilityService = Depends(get_ip_service),
    principal: Principal = Depends(get_principal),
) -> IPKitRecordResponse:
    try:
        entity = await service.dispense_kit(
            kit_id=kit_id,
            subject_id=payload.subject_id,
            visit_id=payload.visit_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Subject kit dispensation",
        )
    except IPKitNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except IPQuarantineError as e:
        raise HTTPException(status_code=422, detail=str(e))
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    return IPKitRecordResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        kit_number=entity.kit_number,
        lot_number=entity.lot_number,
        kit_type=entity.kit_type,
        shipment_tracking_number=entity.shipment_tracking_number,
        expiration_date=entity.expiration_date,
        status=entity.status,
        received_date=entity.received_date,
        dispensed_subject_id=entity.dispensed_subject_id,
        dispensed_visit_id=entity.dispensed_visit_id,
        dispensed_date=entity.dispensed_date,
        returned_units_count=entity.returned_units_count,
        expected_units_count=entity.expected_units_count,
        compliance_percentage=entity.compliance_percentage,
        notes=entity.notes,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post("/kits/{kit_id}/reconcile", response_model=IPKitRecordResponse)
async def reconcile_returned_kit(
    kit_id: str,
    payload: IPKitReconcileRequest,
    service: IPAccountabilityService = Depends(get_ip_service),
    principal: Principal = Depends(get_principal),
) -> IPKitRecordResponse:
    try:
        entity = await service.reconcile_returned_kit(
            kit_id=kit_id,
            returned_units_count=payload.returned_units_count,
            expected_units_count=payload.expected_units_count,
            notes=payload.notes,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "Returned kit unit reconciliation",
        )
    except IPKitNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return IPKitRecordResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        kit_number=entity.kit_number,
        lot_number=entity.lot_number,
        kit_type=entity.kit_type,
        shipment_tracking_number=entity.shipment_tracking_number,
        expiration_date=entity.expiration_date,
        status=entity.status,
        received_date=entity.received_date,
        dispensed_subject_id=entity.dispensed_subject_id,
        dispensed_visit_id=entity.dispensed_visit_id,
        dispensed_date=entity.dispensed_date,
        returned_units_count=entity.returned_units_count,
        expected_units_count=entity.expected_units_count,
        compliance_percentage=entity.compliance_percentage,
        notes=entity.notes,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post(
    "/excursions",
    response_model=IPTemperatureExcursionResponse,
    status_code=status.HTTP_201_CREATED,
)
async def log_temperature_excursion(
    payload: IPTemperatureExcursionCreate,
    service: IPAccountabilityService = Depends(get_ip_service),
    principal: Principal = Depends(get_principal),
) -> IPTemperatureExcursionResponse:
    entity = await service.log_temperature_excursion(
        study_id=payload.study_id,
        site_id=payload.site_id,
        kit_ids=payload.kit_ids,
        excursion_type=payload.excursion_type,
        min_temp_celsius=payload.min_temp_celsius,
        max_temp_celsius=payload.max_temp_celsius,
        duration_hours=payload.duration_hours,
        occurred_at=payload.occurred_at,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Temperature excursion logging",
    )
    return IPTemperatureExcursionResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        kit_ids=entity.kit_ids,
        excursion_type=entity.excursion_type,
        min_temp_celsius=entity.min_temp_celsius,
        max_temp_celsius=entity.max_temp_celsius,
        duration_hours=entity.duration_hours,
        occurred_at=entity.occurred_at,
        disposition_status=entity.disposition_status,
        qa_reviewed_by=entity.qa_reviewed_by,
        qa_reviewed_at=entity.qa_reviewed_at,
        qa_rationale=entity.qa_rationale,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post(
    "/excursions/{excursion_id}/disposition",
    response_model=IPTemperatureExcursionResponse,
)
async def disposition_temperature_excursion(
    excursion_id: str,
    payload: IPTemperatureExcursionDisposition,
    service: IPAccountabilityService = Depends(get_ip_service),
    principal: Principal = Depends(get_principal),
) -> IPTemperatureExcursionResponse:
    try:
        entity = await service.disposition_temperature_excursion(
            excursion_id=excursion_id,
            disposition_status=payload.disposition_status,
            qa_rationale=payload.qa_rationale,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "QA temperature excursion disposition",
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return IPTemperatureExcursionResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        kit_ids=entity.kit_ids,
        excursion_type=entity.excursion_type,
        min_temp_celsius=entity.min_temp_celsius,
        max_temp_celsius=entity.max_temp_celsius,
        duration_hours=entity.duration_hours,
        occurred_at=entity.occurred_at,
        disposition_status=entity.disposition_status,
        qa_reviewed_by=entity.qa_reviewed_by,
        qa_reviewed_at=entity.qa_reviewed_at,
        qa_rationale=entity.qa_rationale,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post(
    "/destruction-certificates",
    response_model=IPDestructionCertificateResponse,
    status_code=status.HTTP_201_CREATED,
)
async def generate_destruction_certificate(
    payload: IPDestructionCertificateCreate,
    service: IPAccountabilityService = Depends(get_ip_service),
    principal: Principal = Depends(get_principal),
) -> IPDestructionCertificateResponse:
    entity = await service.generate_destruction_certificate(
        study_id=payload.study_id,
        site_id=payload.site_id,
        kit_ids=payload.kit_ids,
        destruction_method=payload.destruction_method,
        witness_user_id=payload.witness_user_id,
        witness_role=payload.witness_role,
        pi_signature_hash=payload.pi_signature_hash,
        reason_for_destruction=payload.reason_for_destruction,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason
        or "Witnessed destruction certification",
    )
    return IPDestructionCertificateResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        certificate_number=entity.certificate_number,
        kit_ids=entity.kit_ids,
        destruction_method=entity.destruction_method,
        destruction_date=entity.destruction_date,
        witness_user_id=entity.witness_user_id,
        witness_role=entity.witness_role,
        pi_signature_hash=entity.pi_signature_hash,
        pi_signed_at=entity.pi_signed_at,
        reason_for_destruction=entity.reason_for_destruction,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )
