"""FastAPI Router for Medical Device & In-Vitro Diagnostic (IVD) Clinical Investigation Suite.

Requirements:
- ADR-2209 / ADR-2199
- PRD-SYS-126 (Medical Device & IVD Investigation Suite)
- PRD-DEV-001 (UDI & Serialized Inventory Accountability)
- PRD-DEV-002 (Device Deficiency & Malfunction eCRF Capture)
- PRD-DEV-003 (Automated Adverse Device Effect & RTSM Quarantine Triggers)
- PRD-DEV-004 (Regulatory Incident Reports & CDISC SDTM DD/DO Exports)
- ISO 14155:2020 / EU MDR 2017/745 / EU IVDR 2017/746 / 21 CFR Part 11
"""

from fastapi import (
    APIRouter,
    Depends,
    HTTPException,
    Query,
    Request,
)
from sqlalchemy import (
    case,
    func,
    select,
)
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.database.context import (
    audit_context,
    current_change_reason,
    current_user_id,
)
from apps.execution.database.models import (
    DeviceDeficiencyReport,
    DeviceQuarantineRecord,
    InvestigationalDeviceInventory,
)
from apps.execution.device_investigation import (
    _map_deficiency_to_response,
    _map_inventory_to_response,
    _map_quarantine_to_response,
    dispense_device,
    initiate_device_quarantine,
    parse_udi_barcode,
    register_device_inventory,
    release_device_quarantine,
    report_device_deficiency,
)
from apps.execution.domain.device_models import (
    DeviceDeficiencyCreateRequest,
    DeviceDeficiencyResponse,
    DeviceDispensationRequest,
    DeviceDispensationResponse,
    DeviceInventoryCreateRequest,
    DeviceInventoryResponse,
    DeviceInvestigationSummary,
    DeviceQuarantineCreateRequest,
    DeviceQuarantineReleaseRequest,
    DeviceQuarantineResponse,
    ParsedUDI,
    UDIParseRequest,
)
from packages.security.rbac import (
    ROLE_AUDITOR,
    ROLE_CRA,
    ROLE_CRC,
    ROLE_DATA_MANAGER,
    ROLE_PRINCIPAL_INVESTIGATOR,
    ROLE_SITE_INVESTIGATOR,
    ROLE_SPONSOR_ADMIN,
    ROLE_SPONSOR_DESIGNER,
    Principal,
    can_access_study,
    get_principal,
    require_roles,
)

router = APIRouter(
    prefix="/api/v1/execution/devices", tags=["Medical Device & IVD Suite"]
)


def _get_audit_meta(request: Request, principal: Principal) -> tuple[str, str]:
    """Helper to extract user_id and change_reason from request context."""
    user_id = (
        request.headers.get("X-User-Id")
        or principal.user_id
        or current_user_id.get()
        or "system"
    )
    change_reason = (
        request.headers.get("X-Change-Reason")
        or current_change_reason.get()
        or "Device investigation operation"
    )
    return user_id, change_reason


@router.post(
    "/parse-udi",
    response_model=ParsedUDI,
    status_code=200,
)
async def parse_udi(
    payload: UDIParseRequest,
    principal: Principal = Depends(get_principal),
) -> ParsedUDI:
    """Parses and validates a Unique Device Identifier (UDI) barcode string.

    Supports GS1-128, GS1-DataMatrix (AIs 01, 10, 11, 17, 21, 8012), and HIBCC formats.

    @req:PRD-DEV-001, @req:PRD-SYS-126, @req:ADR-2209
    """
    return parse_udi_barcode(payload.raw_barcode)


@router.post(
    "/inventory",
    response_model=list[DeviceInventoryResponse],
    status_code=201,
)
async def register_inventory(
    request: Request,
    payload: DeviceInventoryCreateRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[DeviceInventoryResponse]:
    """Registers serialized investigational medical device and IVD inventory items in RTSM.

    @req:PRD-DEV-001, @req:PRD-SYS-126, @req:ADR-2209
    """
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {payload.study_id}",
        )

    user_id, change_reason = _get_audit_meta(request, principal)

    with audit_context(
        user_id=user_id,
        change_reason=payload.reason_for_change or change_reason,
    ):
        return await register_device_inventory(
            session=session,
            request=payload,
            user_id=user_id,
            change_reason=payload.reason_for_change or change_reason,
        )


@router.get(
    "/inventory",
    response_model=list[DeviceInventoryResponse],
    status_code=200,
)
async def list_inventory(
    study_id: str = Query(..., description="Study Identifier"),
    site_id: str | None = Query(None, description="Optional Site Identifier"),
    status: str | None = Query(None, description="Optional Status filter"),
    udi_di: str | None = Query(None, description="Optional UDI-DI filter"),
    lot_number: str | None = Query(None, description="Optional Lot filter"),
    serial_number: str | None = Query(None, description="Optional Serial filter"),
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[DeviceInventoryResponse]:
    """Retrieves investigational medical device inventory matching query filters.

    @req:PRD-DEV-001, @req:PRD-SYS-126, @req:ADR-2209
    """
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {study_id}",
        )

    query = select(InvestigationalDeviceInventory).where(
        InvestigationalDeviceInventory.study_id == study_id,
        InvestigationalDeviceInventory.is_deleted.is_(False),
    )

    if site_id:
        query = query.where(InvestigationalDeviceInventory.site_id == site_id)
    if status:
        query = query.where(InvestigationalDeviceInventory.status == status)
    if udi_di:
        query = query.where(InvestigationalDeviceInventory.udi_di == udi_di)
    if lot_number:
        query = query.where(InvestigationalDeviceInventory.lot_number == lot_number)
    if serial_number:
        query = query.where(
            InvestigationalDeviceInventory.serial_number == serial_number
        )

    res = await session.execute(query)
    items = res.scalars().all()

    return [
        _map_inventory_to_response(item, principal.user_id, "Query inventory")
        for item in items
    ]


@router.post(
    "/dispense",
    response_model=DeviceDispensationResponse,
    status_code=200,
)
async def dispense(
    request: Request,
    payload: DeviceDispensationRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> DeviceDispensationResponse:
    """Dispenses an investigational medical device to a study participant.

    Enforces strict quarantine and expiration blocking rules.

    @req:PRD-DEV-001, @req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209
    """
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {payload.study_id}",
        )

    user_id, change_reason = _get_audit_meta(request, principal)

    with audit_context(
        user_id=user_id,
        change_reason=payload.reason_for_change or change_reason,
    ):
        return await dispense_device(
            session=session,
            request=payload,
            user_id=user_id,
            change_reason=payload.reason_for_change or change_reason,
        )


@router.post(
    "/deficiencies",
    response_model=DeviceDeficiencyResponse,
    status_code=201,
)
async def log_deficiency(
    request: Request,
    payload: DeviceDeficiencyCreateRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> DeviceDeficiencyResponse:
    """Logs a device deficiency/malfunction and triggers automated quarantine + safety cases.

    @req:PRD-DEV-002, @req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209
    """
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {payload.study_id}",
        )

    user_id, change_reason = _get_audit_meta(request, principal)

    with audit_context(
        user_id=user_id,
        change_reason=payload.reason_for_change or change_reason,
    ):
        return await report_device_deficiency(
            session=session,
            request=payload,
            user_id=user_id,
            change_reason=payload.reason_for_change or change_reason,
        )


@router.get(
    "/deficiencies",
    response_model=list[DeviceDeficiencyResponse],
    status_code=200,
)
async def list_deficiencies(
    study_id: str = Query(..., description="Study Identifier"),
    site_id: str | None = Query(None, description="Optional Site Identifier"),
    status: str | None = Query(None, description="Optional Status filter"),
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[DeviceDeficiencyResponse]:
    """Lists reported medical device deficiencies and malfunctions for a study.

    @req:PRD-DEV-002, @req:PRD-SYS-126, @req:ADR-2209
    """
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {study_id}",
        )

    query = select(DeviceDeficiencyReport).where(
        DeviceDeficiencyReport.study_id == study_id,
        DeviceDeficiencyReport.is_deleted.is_(False),
    )

    if site_id:
        query = query.where(DeviceDeficiencyReport.site_id == site_id)
    if status:
        query = query.where(DeviceDeficiencyReport.investigation_status == status)

    res = await session.execute(query)
    items = res.scalars().all()

    return [
        _map_deficiency_to_response(
            item, principal.user_id, "Query device deficiencies"
        )
        for item in items
    ]


@router.get(
    "/deficiencies/{deficiency_id}",
    response_model=DeviceDeficiencyResponse,
    status_code=200,
)
async def get_deficiency(
    deficiency_id: str,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> DeviceDeficiencyResponse:
    """Retrieves a single device deficiency report by ID.

    @req:PRD-DEV-002, @req:PRD-SYS-126, @req:ADR-2209
    """
    stmt = select(DeviceDeficiencyReport).where(
        DeviceDeficiencyReport.id == deficiency_id,
        DeviceDeficiencyReport.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    item = res.scalars().first()

    if not item:
        raise HTTPException(
            status_code=404, detail=f"Device deficiency {deficiency_id} not found."
        )

    if not can_access_study(principal, item.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {item.study_id}",
        )

    return _map_deficiency_to_response(
        item, principal.user_id, "Retrieve device deficiency"
    )


@router.post(
    "/quarantine",
    response_model=DeviceQuarantineResponse,
    status_code=201,
)
async def initiate_quarantine(
    request: Request,
    payload: DeviceQuarantineCreateRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> DeviceQuarantineResponse:
    """Manually places a device serial or lot into QUARANTINED status with dispensation blocking.

    @req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209
    """
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {payload.study_id}",
        )

    user_id, change_reason = _get_audit_meta(request, principal)

    with audit_context(
        user_id=user_id,
        change_reason=payload.reason_for_change or change_reason,
    ):
        return await initiate_device_quarantine(
            session=session,
            request=payload,
            user_id=user_id,
            change_reason=payload.reason_for_change or change_reason,
        )


@router.post(
    "/quarantine/release",
    response_model=DeviceQuarantineResponse,
    status_code=200,
)
async def release_quarantine(
    request: Request,
    payload: DeviceQuarantineReleaseRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_PRINCIPAL_INVESTIGATOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> DeviceQuarantineResponse:
    """Releases a device quarantine record under 21 CFR Part 11 electronic signature.

    @req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209
    """
    user_id, change_reason = _get_audit_meta(request, principal)

    with audit_context(
        user_id=user_id,
        change_reason=payload.reason_for_change or change_reason,
    ):
        return await release_device_quarantine(
            session=session,
            request=payload,
            user_id=user_id,
            change_reason=payload.reason_for_change or change_reason,
        )


@router.get(
    "/quarantine",
    response_model=list[DeviceQuarantineResponse],
    status_code=200,
)
async def list_quarantines(
    study_id: str = Query(..., description="Study Identifier"),
    site_id: str | None = Query(None, description="Optional Site Identifier"),
    status: str | None = Query(None, description="Optional Status filter"),
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[DeviceQuarantineResponse]:
    """Lists device quarantine records for a clinical trial study.

    @req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209
    """
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {study_id}",
        )

    query = select(DeviceQuarantineRecord).where(
        DeviceQuarantineRecord.study_id == study_id,
        DeviceQuarantineRecord.is_deleted.is_(False),
    )

    if site_id:
        query = query.where(DeviceQuarantineRecord.site_id == site_id)
    if status:
        query = query.where(DeviceQuarantineRecord.status == status)

    res = await session.execute(query)
    items = res.scalars().all()

    return [
        _map_quarantine_to_response(item, principal.user_id, "Query device quarantines")
        for item in items
    ]


@router.get(
    "/summary",
    response_model=DeviceInvestigationSummary,
    status_code=200,
)
async def get_device_summary(
    study_id: str = Query(..., description="Study Identifier"),
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> DeviceInvestigationSummary:
    """Retrieves operational metrics for the Medical Device & IVD Investigation console.

    @req:PRD-DEV-001, @req:PRD-DEV-002, @req:PRD-DEV-003, @req:PRD-SYS-126, @req:ADR-2209
    """
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {study_id}",
        )

    # Count inventory
    inv_res = await session.execute(
        select(
            func.count(InvestigationalDeviceInventory.id),
            func.sum(
                case((InvestigationalDeviceInventory.status == "AVAILABLE", 1), else_=0)
            ),
            func.sum(
                case((InvestigationalDeviceInventory.status == "DISPENSED", 1), else_=0)
            ),
            func.sum(
                case(
                    (InvestigationalDeviceInventory.status == "QUARANTINED", 1), else_=0
                )
            ),
        ).where(
            InvestigationalDeviceInventory.study_id == study_id,
            InvestigationalDeviceInventory.is_deleted.is_(False),
        )
    )
    total_inv, avail_inv, disp_inv, quar_inv = inv_res.first() or (0, 0, 0, 0)

    # Count deficiencies
    defic_res = await session.execute(
        select(
            func.count(DeviceDeficiencyReport.id),
            func.sum(
                case(
                    (DeviceDeficiencyReport.safety_classification == "SADE", 1),
                    (DeviceDeficiencyReport.safety_classification == "USADE", 1),
                    else_=0,
                )
            ),
        ).where(
            DeviceDeficiencyReport.study_id == study_id,
            DeviceDeficiencyReport.is_deleted.is_(False),
        )
    )
    total_defic, total_sade = defic_res.first() or (0, 0)

    # Count active quarantines
    quar_res = await session.execute(
        select(func.count(DeviceQuarantineRecord.id)).where(
            DeviceQuarantineRecord.study_id == study_id,
            DeviceQuarantineRecord.status == "ACTIVE",
            DeviceQuarantineRecord.is_deleted.is_(False),
        )
    )
    active_quar = quar_res.scalar_one() or 0

    return DeviceInvestigationSummary(
        study_id=study_id,
        total_devices=int(total_inv or 0),
        available_devices=int(avail_inv or 0),
        dispensed_devices=int(disp_inv or 0),
        quarantined_devices=int(quar_inv or 0),
        total_deficiencies=int(total_defic or 0),
        open_safety_cases=int(total_defic or 0),
        sade_count=int(total_sade or 0),
        active_quarantines=int(active_quar or 0),
    )
