"""FastAPI router for Multi-Echelon RTSM Supply Hierarchy, Predictive Resupply & Transfer Orders.

Requirements: PRD-SCM-001, GMP Annex 13
"""

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.database.models import (
    DrugStabilityBudget,
    ExcursionEvaluation,
    IPKit,
    LotExpiryExtension,
    ReLabelingTask,
    SupplyNode,
    SupplyNodeInventory,
    TemperatureLogger,
    TransferOrder,
)
from apps.execution.dependencies import verify_change_justification
from apps.execution.domain.rtsm_models import (
    MultiEchelonRollup,
    PredictiveForecastInput,
    StabilityBudgetConfig,
)
from apps.execution.presentation.routers.rtsm_schemas import (
    DiscreteExcursionResponse,
    ExcursionEvaluationResponse,
    IPKitCreateRequest,
    IPKitResponse,
    KitQuarantineRequest,
    KitReleaseQuarantineRequest,
    LoggerIngestionRequest,
    LotExpiryExtensionRequest,
    LotExpiryExtensionResponse,
    MktCalculationDirectRequest,
    MktCalculationDirectResponse,
    PackingManifestCreateRequest,
    PackingManifestResponse,
    PredictiveForecastRequest,
    PredictiveForecastResponse,
    ReLabelingTaskCompleteRequest,
    ReLabelingTaskResponse,
    ReLabelingTaskVerifyRequest,
    StabilityBudgetCreateRequest,
    StabilityBudgetResponse,
    SupplyNodeCreateRequest,
    SupplyNodeInventoryAdjustRequest,
    SupplyNodeInventoryResponse,
    SupplyNodeResponse,
    TemperatureLoggerRegisterRequest,
    TemperatureLoggerResponse,
    TransferOrderApprovalRequest,
    TransferOrderCreateRequest,
    TransferOrderReceiveRequest,
    TransferOrderResponse,
    TransferOrderShipRequest,
)
from apps.execution.rtsm_cold_chain import (
    InvalidLoggerFileError,
    ReLabelingTaskNotFoundError,
    calculate_mkt,
    complete_relabeling_task_transaction,
    create_or_update_stability_budget_transaction,
    extend_lot_expiration_transaction,
    ingest_and_evaluate_logger_transaction,
    quarantine_kits_transaction,
    release_quarantined_kits_transaction,
    verify_relabeling_task_transaction,
)
from apps.execution.rtsm_supply import (
    InsufficientStockError,
    InvalidHierarchyError,
    InvalidTransferOrderStateError,
    StorageCapacityExceededError,
    TransferOrderNotFoundError,
    approve_transfer_order_transaction,
    build_supply_hierarchy_tree,
    create_transfer_order_transaction,
    generate_packing_manifest_transaction,
    receive_transfer_order_transaction,
    run_predictive_forecast_transaction,
    ship_transfer_order_transaction,
)
from apps.execution.trial_lock import TrialLockManager
from packages.security import (
    ROLE_CRA,
    ROLE_CRC,
    ROLE_INVESTIGATOR,
    ROLE_SITE_INVESTIGATOR,
    ROLE_SPONSOR_ADMIN,
    ROLE_SPONSOR_DM,
    ROLE_SYSADMIN,
    Principal,
    can_access_site,
    can_access_study,
    get_principal,
    require_roles,
)

router = APIRouter(prefix="/api/v1/execution/rtsm", tags=["RTSM Multi-Echelon Supply"])


# =========================================================================
# Multi-Echelon Supply Hierarchy Endpoints
# =========================================================================


@router.get("/hierarchy", response_model=MultiEchelonRollup)
async def get_supply_hierarchy_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> MultiEchelonRollup:
    """Returns the complete multi-echelon supply hierarchy tree and stock rollup for a study."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail=f"Forbidden: Access denied to study {study_id}."
        )

    stmt_nodes = (
        select(SupplyNode)
        .where(SupplyNode.study_id == study_id, SupplyNode.is_deleted.is_(False))
        .order_by(SupplyNode.node_type)
    )
    res_nodes = await session.execute(stmt_nodes)
    nodes = list(res_nodes.scalars().all())

    stmt_inv = select(SupplyNodeInventory).where(
        SupplyNodeInventory.study_id == study_id,
        SupplyNodeInventory.is_deleted.is_(False),
    )
    res_inv = await session.execute(stmt_inv)
    inventories = list(res_inv.scalars().all())

    try:
        return build_supply_hierarchy_tree(nodes, inventories)
    except InvalidHierarchyError as e:
        raise HTTPException(status_code=400, detail=str(e))


@router.post("/nodes", response_model=SupplyNodeResponse, status_code=201)
async def create_supply_node_endpoint(
    payload: SupplyNodeCreateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> SupplyNodeResponse:
    """Registers or updates a supply node in the clinical supply hierarchy."""
    verify_change_justification(request)

    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )

    stmt = select(SupplyNode).where(
        SupplyNode.study_id == payload.study_id,
        SupplyNode.node_id == payload.node_id,
        SupplyNode.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    existing = res.scalars().first()

    if existing:
        existing.node_type = str(payload.node_type)
        existing.name = payload.name
        existing.country = payload.country
        existing.address = payload.address
        existing.parent_node_id = payload.parent_node_id
        existing.storage_capacity = payload.storage_capacity
        existing.lead_time_days = payload.lead_time_days
        existing.temperature_conditions = payload.temperature_conditions
        existing.version += 1
        node = existing
    else:
        node = SupplyNode(
            study_id=payload.study_id,
            node_id=payload.node_id,
            node_type=str(payload.node_type),
            name=payload.name,
            country=payload.country,
            address=payload.address,
            parent_node_id=payload.parent_node_id,
            storage_capacity=payload.storage_capacity,
            lead_time_days=payload.lead_time_days,
            temperature_conditions=payload.temperature_conditions,
            current_utilization=0,
            is_active=True,
        )
        session.add(node)

    await session.commit()
    await session.refresh(node)

    return SupplyNodeResponse(
        id=node.id,
        study_id=node.study_id,
        node_id=node.node_id,
        node_type=node.node_type,
        name=node.name,
        country=node.country,
        address=node.address,
        parent_node_id=node.parent_node_id,
        storage_capacity=node.storage_capacity,
        current_utilization=node.current_utilization,
        lead_time_days=node.lead_time_days,
        temperature_conditions=node.temperature_conditions or [],
        is_active=node.is_active,
    )


@router.get(
    "/nodes/{node_id}/inventory", response_model=list[SupplyNodeInventoryResponse]
)
async def get_node_inventory_endpoint(
    node_id: str,
    study_id: str = Query(..., description="Clinical study identifier"),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[SupplyNodeInventoryResponse]:
    """Lists inventory records and stock levels for a specific supply node."""
    if not (
        can_access_study(principal, study_id) and can_access_site(principal, node_id)
    ):
        raise HTTPException(
            status_code=403, detail="Forbidden: Access denied to study or node."
        )

    stmt = select(SupplyNodeInventory).where(
        SupplyNodeInventory.study_id == study_id,
        SupplyNodeInventory.node_id == node_id,
        SupplyNodeInventory.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    records = list(res.scalars().all())

    return [
        SupplyNodeInventoryResponse(
            id=r.id,
            study_id=r.study_id,
            node_id=r.node_id,
            kit_type=r.kit_type,
            lot_number=r.lot_number,
            on_hand_qty=r.on_hand_qty,
            reserved_qty=r.reserved_qty,
            in_transit_qty=r.in_transit_qty,
            safety_stock_threshold=r.safety_stock_threshold,
            reorder_point=r.reorder_point,
            max_capacity=r.max_capacity,
            resupply_signal=r.resupply_signal,
        )
        for r in records
    ]


@router.post(
    "/nodes/{node_id}/inventory/adjust",
    response_model=SupplyNodeInventoryResponse,
)
async def adjust_node_inventory_endpoint(
    node_id: str,
    payload: SupplyNodeInventoryAdjustRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> SupplyNodeInventoryResponse:
    """Adjusts or initializes stock levels for a supply node."""
    verify_change_justification(request)

    if TrialLockManager.is_site_locked(node_id):
        raise HTTPException(
            status_code=423,
            detail=f"Node {node_id} is currently locked in read-only state.",
        )

    stmt = select(SupplyNodeInventory).where(
        SupplyNodeInventory.study_id == payload.study_id,
        SupplyNodeInventory.node_id == node_id,
        SupplyNodeInventory.kit_type == payload.kit_type,
        SupplyNodeInventory.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    existing = res.scalars().first()

    if existing:
        existing.on_hand_qty = payload.on_hand_qty
        existing.lot_number = payload.lot_number
        existing.safety_stock_threshold = payload.safety_stock_threshold
        existing.reorder_point = payload.reorder_point
        existing.max_capacity = payload.max_capacity
        existing.resupply_signal = existing.on_hand_qty <= existing.reorder_point
        existing.version += 1
        inv = existing
    else:
        inv = SupplyNodeInventory(
            study_id=payload.study_id,
            node_id=node_id,
            kit_type=payload.kit_type,
            lot_number=payload.lot_number,
            on_hand_qty=payload.on_hand_qty,
            reserved_qty=0,
            in_transit_qty=0,
            safety_stock_threshold=payload.safety_stock_threshold,
            reorder_point=payload.reorder_point,
            max_capacity=payload.max_capacity,
            resupply_signal=payload.on_hand_qty <= payload.reorder_point,
        )
        session.add(inv)

    # Update node current utilization
    stmt_node = select(SupplyNode).where(
        SupplyNode.study_id == payload.study_id,
        SupplyNode.node_id == node_id,
        SupplyNode.is_deleted.is_(False),
    )
    res_node = await session.execute(stmt_node)
    node = res_node.scalars().first()
    if node:
        node.current_utilization = payload.on_hand_qty
        node.version += 1

    await session.commit()
    await session.refresh(inv)

    return SupplyNodeInventoryResponse(
        id=inv.id,
        study_id=inv.study_id,
        node_id=inv.node_id,
        kit_type=inv.kit_type,
        lot_number=inv.lot_number,
        on_hand_qty=inv.on_hand_qty,
        reserved_qty=inv.reserved_qty,
        in_transit_qty=inv.in_transit_qty,
        safety_stock_threshold=inv.safety_stock_threshold,
        reorder_point=inv.reorder_point,
        max_capacity=inv.max_capacity,
        resupply_signal=inv.resupply_signal,
    )


# =========================================================================
# Predictive Resupply Forecasting Endpoints
# =========================================================================


@router.post("/forecast/site", response_model=PredictiveForecastResponse)
async def forecast_site_resupply_endpoint(
    payload: PredictiveForecastRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_INVESTIGATOR,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
            "crc",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> PredictiveForecastResponse:
    """Executes a predictive demand and resupply forecast for a clinical site."""
    if not (
        can_access_study(principal, payload.study_id)
        and can_access_site(principal, payload.site_id)
    ):
        raise HTTPException(
            status_code=403, detail="Forbidden: Access denied to study or site."
        )

    domain_input = PredictiveForecastInput(
        study_id=payload.study_id,
        site_id=payload.site_id,
        kit_type=payload.kit_type,
        horizon_days=payload.horizon_days,
        enrollment_velocity_weekly=payload.enrollment_velocity_weekly,
        screening_pipeline_count=payload.screening_pipeline_count,
        screening_conversion_rate=payload.screening_conversion_rate,
        lead_time_days=payload.lead_time_days,
        buffer_days=payload.buffer_days,
        safety_stock_threshold=payload.safety_stock_threshold,
        storage_capacity=payload.storage_capacity,
    )

    try:
        result = await run_predictive_forecast_transaction(session, domain_input)
        await session.commit()
    except Exception as e:
        await session.rollback()
        raise HTTPException(status_code=500, detail=f"Forecast error: {str(e)}")

    return PredictiveForecastResponse(
        study_id=result.study_id,
        site_id=result.site_id,
        kit_type=result.kit_type,
        forecast_generated_at=result.forecast_generated_at,
        horizon_days=result.horizon_days,
        current_on_hand_qty=result.current_on_hand_qty,
        current_reserved_qty=result.current_reserved_qty,
        current_in_transit_qty=result.current_in_transit_qty,
        effective_available_stock=result.effective_available_stock,
        active_subjects_count=result.active_subjects_count,
        screening_pipeline_count=result.screening_pipeline_count,
        enrollment_velocity_weekly=result.enrollment_velocity_weekly,
        total_projected_demand=result.total_projected_demand,
        average_daily_burn_rate=result.average_daily_burn_rate,
        days_of_supply_remaining=result.days_of_supply_remaining,
        projected_runout_date=result.projected_runout_date,
        stockout_risk_level=result.stockout_risk_level.value,
        recommended_order_qty=result.recommended_order_qty,
        reorder_recommended=result.reorder_recommended,
        reorder_trigger_day=result.reorder_trigger_day,
        timeline=result.timeline,
    )


@router.post("/forecast/study", response_model=list[PredictiveForecastResponse])
async def forecast_study_resupply_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    horizon_days: int = Query(default=60, ge=7, le=365),
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[PredictiveForecastResponse]:
    """Generates concurrent predictive forecasts across all active clinical sites in a study."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail=f"Forbidden: Access denied to study {study_id}."
        )

    stmt_sites = select(SupplyNode).where(
        SupplyNode.study_id == study_id,
        SupplyNode.node_type == "CLINICAL_SITE",
        SupplyNode.is_deleted.is_(False),
    )
    res_sites = await session.execute(stmt_sites)
    sites = list(res_sites.scalars().all())

    results: list[PredictiveForecastResponse] = []
    for site in sites:
        if not can_access_site(principal, site.node_id):
            continue
        domain_input = PredictiveForecastInput(
            study_id=study_id,
            site_id=site.node_id,
            horizon_days=horizon_days,
            lead_time_days=site.lead_time_days,
            storage_capacity=site.storage_capacity,
        )
        res = await run_predictive_forecast_transaction(session, domain_input)
        results.append(
            PredictiveForecastResponse(
                study_id=res.study_id,
                site_id=res.site_id,
                kit_type=res.kit_type,
                forecast_generated_at=res.forecast_generated_at,
                horizon_days=res.horizon_days,
                current_on_hand_qty=res.current_on_hand_qty,
                current_reserved_qty=res.current_reserved_qty,
                current_in_transit_qty=res.current_in_transit_qty,
                effective_available_stock=res.effective_available_stock,
                active_subjects_count=res.active_subjects_count,
                screening_pipeline_count=res.screening_pipeline_count,
                enrollment_velocity_weekly=res.enrollment_velocity_weekly,
                total_projected_demand=res.total_projected_demand,
                average_daily_burn_rate=res.average_daily_burn_rate,
                days_of_supply_remaining=res.days_of_supply_remaining,
                projected_runout_date=res.projected_runout_date,
                stockout_risk_level=res.stockout_risk_level.value,
                recommended_order_qty=res.recommended_order_qty,
                reorder_recommended=res.reorder_recommended,
                reorder_trigger_day=res.reorder_trigger_day,
                timeline=res.timeline,
            )
        )

    await session.commit()
    return results


# =========================================================================
# Transfer Orders & GxP Packing Manifest Endpoints
# =========================================================================


@router.post("/transfer-orders", response_model=TransferOrderResponse, status_code=201)
async def create_transfer_order_endpoint(
    payload: TransferOrderCreateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> TransferOrderResponse:
    """Creates a stock transfer order between supply nodes."""
    verify_change_justification(request)

    if not (
        can_access_study(principal, payload.study_id)
        and can_access_site(principal, payload.destination_node_id)
    ):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access denied to study or destination node.",
        )

    try:
        order = await create_transfer_order_transaction(
            session=session,
            study_id=payload.study_id,
            source_node_id=payload.source_node_id,
            destination_node_id=payload.destination_node_id,
            kit_type=payload.kit_type,
            quantity=payload.quantity,
            priority=str(payload.priority),
            temperature_condition=str(payload.temperature_condition),
            carrier=payload.carrier,
            requested_by=principal.user_id,
            transfer_order_number=payload.transfer_order_number,
        )
        await session.commit()
        await session.refresh(order)
    except InsufficientStockError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except StorageCapacityExceededError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except PermissionError as e:
        raise HTTPException(status_code=423, detail=str(e))

    return TransferOrderResponse(
        id=order.id,
        study_id=order.study_id,
        transfer_order_number=order.transfer_order_number,
        source_node_id=order.source_node_id,
        destination_node_id=order.destination_node_id,
        kit_type=order.kit_type,
        quantity=order.quantity,
        priority=order.priority,
        status=order.status,
        temperature_condition=order.temperature_condition,
        carrier=order.carrier,
        tracking_number=order.tracking_number,
        estimated_delivery_date=order.estimated_delivery_date,
        requested_at=order.requested_at,
        requested_by=order.requested_by,
        approved_at=order.approved_at,
        approved_by=order.approved_by,
        shipped_at=order.shipped_at,
        shipped_by=order.shipped_by,
        delivered_at=order.delivered_at,
        delivered_by=order.delivered_by,
        rejection_reason=order.rejection_reason,
    )


@router.get("/transfer-orders", response_model=list[TransferOrderResponse])
async def list_transfer_orders_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    source_node_id: str | None = None,
    destination_node_id: str | None = None,
    status: str | None = None,
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[TransferOrderResponse]:
    """Lists active transfer orders with optional filtering."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail=f"Forbidden: Access denied to study {study_id}."
        )

    stmt = select(TransferOrder).where(
        TransferOrder.study_id == study_id, TransferOrder.is_deleted.is_(False)
    )
    if source_node_id:
        stmt = stmt.where(TransferOrder.source_node_id == source_node_id)
    if destination_node_id:
        stmt = stmt.where(TransferOrder.destination_node_id == destination_node_id)
    if status:
        stmt = stmt.where(TransferOrder.status == status)

    stmt = stmt.order_by(TransferOrder.requested_at.desc())
    res = await session.execute(stmt)
    orders = list(res.scalars().all())

    filtered = [
        o
        for o in orders
        if can_access_site(principal, o.destination_node_id)
        or can_access_site(principal, o.source_node_id)
    ]

    return [
        TransferOrderResponse(
            id=o.id,
            study_id=o.study_id,
            transfer_order_number=o.transfer_order_number,
            source_node_id=o.source_node_id,
            destination_node_id=o.destination_node_id,
            kit_type=o.kit_type,
            quantity=o.quantity,
            priority=o.priority,
            status=o.status,
            temperature_condition=o.temperature_condition,
            carrier=o.carrier,
            tracking_number=o.tracking_number,
            estimated_delivery_date=o.estimated_delivery_date,
            requested_at=o.requested_at,
            requested_by=o.requested_by,
            approved_at=o.approved_at,
            approved_by=o.approved_by,
            shipped_at=o.shipped_at,
            shipped_by=o.shipped_by,
            delivered_at=o.delivered_at,
            delivered_by=o.delivered_by,
            rejection_reason=o.rejection_reason,
        )
        for o in filtered
    ]


@router.post(
    "/transfer-orders/{order_id}/approve",
    response_model=TransferOrderResponse,
)
async def approve_transfer_order_endpoint(
    order_id: str,
    payload: TransferOrderApprovalRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> TransferOrderResponse:
    """Approves a transfer order for warehouse packing."""
    verify_change_justification(request)

    if not payload.change_justification or not payload.change_justification.strip():
        raise HTTPException(
            status_code=400, detail="Change justification must not be empty."
        )

    try:
        order = await approve_transfer_order_transaction(
            session=session,
            order_id=order_id,
            approved_by=principal.user_id,
            change_reason=payload.change_justification,
        )
        await session.commit()
        await session.refresh(order)
    except TransferOrderNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except InvalidTransferOrderStateError as e:
        raise HTTPException(status_code=400, detail=str(e))

    return TransferOrderResponse(
        id=order.id,
        study_id=order.study_id,
        transfer_order_number=order.transfer_order_number,
        source_node_id=order.source_node_id,
        destination_node_id=order.destination_node_id,
        kit_type=order.kit_type,
        quantity=order.quantity,
        priority=order.priority,
        status=order.status,
        temperature_condition=order.temperature_condition,
        carrier=order.carrier,
        tracking_number=order.tracking_number,
        estimated_delivery_date=order.estimated_delivery_date,
        requested_at=order.requested_at,
        requested_by=order.requested_by,
        approved_at=order.approved_at,
        approved_by=order.approved_by,
        shipped_at=order.shipped_at,
        shipped_by=order.shipped_by,
        delivered_at=order.delivered_at,
        delivered_by=order.delivered_by,
        rejection_reason=order.rejection_reason,
    )


@router.post(
    "/transfer-orders/{order_id}/manifest",
    response_model=PackingManifestResponse,
    status_code=201,
)
async def generate_packing_manifest_endpoint(
    order_id: str,
    payload: PackingManifestCreateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> PackingManifestResponse:
    """Generates a formal GxP packing manifest binding serialized kits to an approved transfer order."""
    verify_change_justification(request)

    try:
        manifest = await generate_packing_manifest_transaction(
            session=session,
            transfer_order_id=order_id,
            manifest_number=payload.manifest_number,
            serialized_kit_numbers=payload.serialized_kit_numbers,
            lot_number=payload.lot_number,
            expiration_date=payload.expiration_date,
            container_serial_number=payload.container_serial_number,
            data_logger_id=payload.data_logger_id,
            tamper_seal_id=payload.tamper_seal_id,
            packed_by=principal.user_id,
        )
        await session.commit()
        await session.refresh(manifest)
    except TransferOrderNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except InvalidTransferOrderStateError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    return PackingManifestResponse(
        id=manifest.id,
        study_id=manifest.study_id,
        transfer_order_id=manifest.transfer_order_id,
        manifest_number=manifest.manifest_number,
        serialized_kit_numbers=manifest.serialized_kit_numbers,
        lot_number=manifest.lot_number,
        expiration_date=manifest.expiration_date,
        container_serial_number=manifest.container_serial_number,
        data_logger_id=manifest.data_logger_id,
        tamper_seal_id=manifest.tamper_seal_id,
        packed_by=manifest.packed_by,
        packed_at=manifest.packed_at,
        verified_by=manifest.verified_by,
        verified_at=manifest.verified_at,
    )


@router.post(
    "/transfer-orders/{order_id}/ship",
    response_model=TransferOrderResponse,
)
async def ship_transfer_order_endpoint(
    order_id: str,
    payload: TransferOrderShipRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> TransferOrderResponse:
    """Marks a transfer order shipped, moves inventory to in-transit, and dispatches depot notification."""
    verify_change_justification(request)

    try:
        order = await ship_transfer_order_transaction(
            session=session,
            order_id=order_id,
            shipped_by=principal.user_id,
            tracking_number=payload.tracking_number,
            carrier=payload.carrier,
            estimated_delivery_date=payload.estimated_delivery_date,
            change_reason=payload.change_justification,
        )
        await session.commit()
        await session.refresh(order)
    except TransferOrderNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except InvalidTransferOrderStateError as e:
        raise HTTPException(status_code=400, detail=str(e))

    return TransferOrderResponse(
        id=order.id,
        study_id=order.study_id,
        transfer_order_number=order.transfer_order_number,
        source_node_id=order.source_node_id,
        destination_node_id=order.destination_node_id,
        kit_type=order.kit_type,
        quantity=order.quantity,
        priority=order.priority,
        status=order.status,
        temperature_condition=order.temperature_condition,
        carrier=order.carrier,
        tracking_number=order.tracking_number,
        estimated_delivery_date=order.estimated_delivery_date,
        requested_at=order.requested_at,
        requested_by=order.requested_by,
        approved_at=order.approved_at,
        approved_by=order.approved_by,
        shipped_at=order.shipped_at,
        shipped_by=order.shipped_by,
        delivered_at=order.delivered_at,
        delivered_by=order.delivered_by,
        rejection_reason=order.rejection_reason,
    )


@router.post(
    "/transfer-orders/{order_id}/receive",
    response_model=TransferOrderResponse,
)
async def receive_transfer_order_endpoint(
    order_id: str,
    payload: TransferOrderReceiveRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_INVESTIGATOR,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
            "crc",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> TransferOrderResponse:
    """Marks a transfer order delivered/received at destination node, transferring in-transit stock to on-hand."""
    verify_change_justification(request)

    try:
        order = await receive_transfer_order_transaction(
            session=session,
            order_id=order_id,
            delivered_by=principal.user_id,
            change_reason=payload.change_justification,
        )
        await session.commit()
        await session.refresh(order)
    except TransferOrderNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except InvalidTransferOrderStateError as e:
        raise HTTPException(status_code=400, detail=str(e))

    return TransferOrderResponse(
        id=order.id,
        study_id=order.study_id,
        transfer_order_number=order.transfer_order_number,
        source_node_id=order.source_node_id,
        destination_node_id=order.destination_node_id,
        kit_type=order.kit_type,
        quantity=order.quantity,
        priority=order.priority,
        status=order.status,
        temperature_condition=order.temperature_condition,
        carrier=order.carrier,
        tracking_number=order.tracking_number,
        estimated_delivery_date=order.estimated_delivery_date,
        requested_at=order.requested_at,
        requested_by=order.requested_by,
        approved_at=order.approved_at,
        approved_by=order.approved_by,
        shipped_at=order.shipped_at,
        shipped_by=order.shipped_by,
        delivered_at=order.delivered_at,
        delivered_by=order.delivered_by,
        rejection_reason=order.rejection_reason,
    )


# =========================================================================
# Stability Budget Endpoints (PRD-SCM-002, USP <1079>)
# =========================================================================


@router.post(
    "/stability-budgets", response_model=StabilityBudgetResponse, status_code=201
)
async def create_stability_budget_endpoint(
    request: Request,
    payload: StabilityBudgetCreateRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "dm",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> StabilityBudgetResponse:
    """Configures allowable thermal excursion limits and Arrhenius activation parameters for a kit type or lot."""
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )
    verify_change_justification(request)

    config = StabilityBudgetConfig(
        study_id=payload.study_id,
        kit_type=payload.kit_type,
        lot_number=payload.lot_number,
        storage_condition=payload.storage_condition,
        min_temp_celsius=payload.min_temp_celsius,
        max_temp_celsius=payload.max_temp_celsius,
        max_mkt_celsius=payload.max_mkt_celsius,
        max_cumulative_hours_above_upper=payload.max_cumulative_hours_above_upper,
        max_cumulative_hours_below_lower=payload.max_cumulative_hours_below_lower,
        activation_energy_over_r=payload.activation_energy_over_r,
        hard_min_temp_celsius=payload.hard_min_temp_celsius,
        hard_max_temp_celsius=payload.hard_max_temp_celsius,
    )

    budget = await create_or_update_stability_budget_transaction(session, config)
    await session.commit()
    await session.refresh(budget)

    return StabilityBudgetResponse(
        id=budget.id,
        study_id=budget.study_id,
        kit_type=budget.kit_type,
        lot_number=budget.lot_number,
        storage_condition=budget.storage_condition,
        min_temp_celsius=budget.min_temp_celsius,
        max_temp_celsius=budget.max_temp_celsius,
        max_mkt_celsius=budget.max_mkt_celsius,
        max_cumulative_hours_above_upper=budget.max_cumulative_hours_above_upper,
        max_cumulative_hours_below_lower=budget.max_cumulative_hours_below_lower,
        activation_energy_over_r=budget.activation_energy_over_r,
        hard_min_temp_celsius=budget.hard_min_temp_celsius,
        hard_max_temp_celsius=budget.hard_max_temp_celsius,
    )


@router.get("/stability-budgets", response_model=list[StabilityBudgetResponse])
async def list_stability_budgets_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[StabilityBudgetResponse]:
    """Lists defined drug stability budgets for a clinical study."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail=f"Forbidden: Access denied to study {study_id}."
        )

    stmt = select(DrugStabilityBudget).where(
        DrugStabilityBudget.study_id == study_id,
        DrugStabilityBudget.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    budgets = list(res.scalars().all())

    return [
        StabilityBudgetResponse(
            id=b.id,
            study_id=b.study_id,
            kit_type=b.kit_type,
            lot_number=b.lot_number,
            storage_condition=b.storage_condition,
            min_temp_celsius=b.min_temp_celsius,
            max_temp_celsius=b.max_temp_celsius,
            max_mkt_celsius=b.max_mkt_celsius,
            max_cumulative_hours_above_upper=b.max_cumulative_hours_above_upper,
            max_cumulative_hours_below_lower=b.max_cumulative_hours_below_lower,
            activation_energy_over_r=b.activation_energy_over_r,
            hard_min_temp_celsius=b.hard_min_temp_celsius,
            hard_max_temp_celsius=b.hard_max_temp_celsius,
        )
        for b in budgets
    ]


# =========================================================================
# Cold Chain Hardware Logger Registration & Ingestion Endpoints (PRD-SCM-002)
# =========================================================================


@router.post(
    "/cold-chain/loggers", response_model=TemperatureLoggerResponse, status_code=201
)
async def register_temperature_logger_endpoint(
    request: Request,
    payload: TemperatureLoggerRegisterRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
            "crc",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> TemperatureLoggerResponse:
    """Registers a temperature data logger device bound to a shipment or storage facility."""
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )
    verify_change_justification(request)

    stmt = select(TemperatureLogger).where(
        TemperatureLogger.study_id == payload.study_id,
        TemperatureLogger.logger_id == payload.logger_id,
        TemperatureLogger.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    existing = res.scalars().first()
    if existing:
        existing.device_model = payload.device_model
        existing.serial_number = payload.serial_number
        existing.logger_type = payload.logger_type
        existing.assigned_transfer_order_id = payload.assigned_transfer_order_id
        existing.assigned_node_id = payload.assigned_node_id
        existing.assigned_container_id = payload.assigned_container_id
        existing.calibration_expiry_date = payload.calibration_expiry_date
        logger_obj = existing
    else:
        logger_obj = TemperatureLogger(
            study_id=payload.study_id,
            logger_id=payload.logger_id,
            device_model=payload.device_model,
            serial_number=payload.serial_number,
            logger_type=payload.logger_type,
            assigned_transfer_order_id=payload.assigned_transfer_order_id,
            assigned_node_id=payload.assigned_node_id,
            assigned_container_id=payload.assigned_container_id,
            calibration_expiry_date=payload.calibration_expiry_date,
            status="ACTIVE",
        )
        session.add(logger_obj)

    await session.commit()
    await session.refresh(logger_obj)

    return TemperatureLoggerResponse(
        id=logger_obj.id,
        study_id=logger_obj.study_id,
        logger_id=logger_obj.logger_id,
        device_model=logger_obj.device_model,
        serial_number=logger_obj.serial_number,
        logger_type=logger_obj.logger_type,
        assigned_transfer_order_id=logger_obj.assigned_transfer_order_id,
        assigned_node_id=logger_obj.assigned_node_id,
        assigned_container_id=logger_obj.assigned_container_id,
        calibration_expiry_date=logger_obj.calibration_expiry_date,
        status=logger_obj.status,
    )


@router.get("/cold-chain/loggers", response_model=list[TemperatureLoggerResponse])
async def list_temperature_loggers_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[TemperatureLoggerResponse]:
    """Lists registered temperature loggers for a study."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail=f"Forbidden: Access denied to study {study_id}."
        )

    stmt = select(TemperatureLogger).where(
        TemperatureLogger.study_id == study_id,
        TemperatureLogger.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    loggers = list(res.scalars().all())

    return [
        TemperatureLoggerResponse(
            id=lg.id,
            study_id=lg.study_id,
            logger_id=lg.logger_id,
            device_model=lg.device_model,
            serial_number=lg.serial_number,
            logger_type=lg.logger_type,
            assigned_transfer_order_id=lg.assigned_transfer_order_id,
            assigned_node_id=lg.assigned_node_id,
            assigned_container_id=lg.assigned_container_id,
            calibration_expiry_date=lg.calibration_expiry_date,
            status=lg.status,
        )
        for lg in loggers
    ]


@router.post(
    "/cold-chain/ingest", response_model=ExcursionEvaluationResponse, status_code=201
)
async def ingest_temperature_logger_file_endpoint(
    request: Request,
    payload: LoggerIngestionRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
            "crc",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> ExcursionEvaluationResponse:
    """Ingests a digital logger file (CSV, XML, JSON), calculates Arrhenius MKT, and enforces automated kit quarantine."""
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )
    verify_change_justification(request)

    override_budget = None
    if payload.stability_budget_override:
        override_budget = StabilityBudgetConfig(
            study_id=payload.stability_budget_override.study_id,
            kit_type=payload.stability_budget_override.kit_type,
            lot_number=payload.stability_budget_override.lot_number,
            storage_condition=payload.stability_budget_override.storage_condition,
            min_temp_celsius=payload.stability_budget_override.min_temp_celsius,
            max_temp_celsius=payload.stability_budget_override.max_temp_celsius,
            max_mkt_celsius=payload.stability_budget_override.max_mkt_celsius,
            max_cumulative_hours_above_upper=payload.stability_budget_override.max_cumulative_hours_above_upper,
            max_cumulative_hours_below_lower=payload.stability_budget_override.max_cumulative_hours_below_lower,
            activation_energy_over_r=payload.stability_budget_override.activation_energy_over_r,
            hard_min_temp_celsius=payload.stability_budget_override.hard_min_temp_celsius,
            hard_max_temp_celsius=payload.stability_budget_override.hard_max_temp_celsius,
        )

    try:
        result = await ingest_and_evaluate_logger_transaction(
            session=session,
            study_id=payload.study_id,
            logger_id=payload.logger_id,
            file_format=payload.file_format,
            raw_payload=payload.raw_payload,
            filename=payload.filename,
            entity_type=payload.entity_type,
            entity_id=payload.entity_id,
            stability_budget_override=override_budget,
        )
        await session.commit()
    except InvalidLoggerFileError as e:
        raise HTTPException(status_code=400, detail=str(e))

    return ExcursionEvaluationResponse(
        study_id=result.study_id,
        logger_id=result.logger_id,
        entity_type=str(result.entity_type),
        entity_id=result.entity_id,
        evaluation_status=str(result.evaluation_status),
        mean_kinetic_temperature_celsius=result.mean_kinetic_temperature_celsius,
        arithmetic_mean_celsius=result.arithmetic_mean_celsius,
        min_temperature_celsius=result.min_temperature_celsius,
        max_temperature_celsius=result.max_temperature_celsius,
        cumulative_hours_above_upper=result.cumulative_hours_above_upper,
        cumulative_hours_below_lower=result.cumulative_hours_below_lower,
        total_duration_hours=result.total_duration_hours,
        readings_count=result.readings_count,
        hard_boundary_breached=result.hard_boundary_breached,
        mkt_breached=result.mkt_breached,
        cumulative_time_breached=result.cumulative_time_breached,
        quarantine_triggered=result.quarantine_triggered,
        quarantined_kit_count=result.quarantined_kit_count,
        excursions=[
            DiscreteExcursionResponse(
                start_time=e.start_time,
                end_time=e.end_time,
                duration_hours=e.duration_hours,
                excursion_type=e.excursion_type,
                peak_or_nadir_temp_celsius=e.peak_or_nadir_temp_celsius,
                threshold_celsius=e.threshold_celsius,
            )
            for e in result.excursions
        ],
        evaluated_at=result.evaluated_at,
        evaluated_by=result.evaluated_by,
        summary_notes=result.summary_notes,
    )


@router.post("/cold-chain/evaluate-mkt", response_model=MktCalculationDirectResponse)
async def evaluate_mkt_direct_endpoint(
    payload: MktCalculationDirectRequest,
    principal: Principal = Depends(get_principal),
) -> MktCalculationDirectResponse:
    """Direct Arrhenius Mean Kinetic Temperature (MKT) calculation per USP <1079> / PDA TR 39."""
    try:
        mkt = calculate_mkt(
            payload.temperatures_celsius,
            activation_energy_over_r=payload.activation_energy_over_r,
        )
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    arithmetic_mean = round(
        sum(payload.temperatures_celsius) / len(payload.temperatures_celsius), 4
    )
    min_temp = round(min(payload.temperatures_celsius), 2)
    max_temp = round(max(payload.temperatures_celsius), 2)

    return MktCalculationDirectResponse(
        mean_kinetic_temperature_celsius=mkt,
        arithmetic_mean_celsius=arithmetic_mean,
        min_temperature_celsius=min_temp,
        max_temperature_celsius=max_temp,
        readings_count=len(payload.temperatures_celsius),
    )


@router.get("/cold-chain/evaluations", response_model=list[ExcursionEvaluationResponse])
async def list_excursion_evaluations_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    entity_id: str | None = Query(
        None, description="Optional filter by transfer order or node ID"
    ),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[ExcursionEvaluationResponse]:
    """Lists cold chain excursion evaluation audit records."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail=f"Forbidden: Access denied to study {study_id}."
        )

    stmt = select(ExcursionEvaluation).where(
        ExcursionEvaluation.study_id == study_id,
        ExcursionEvaluation.is_deleted.is_(False),
    )
    if entity_id:
        stmt = stmt.where(ExcursionEvaluation.entity_id == entity_id)

    stmt = stmt.order_by(ExcursionEvaluation.evaluated_at.desc())
    res = await session.execute(stmt)
    records = list(res.scalars().all())

    return [
        ExcursionEvaluationResponse(
            id=r.id,
            study_id=r.study_id,
            logger_id=r.logger_id,
            entity_type=r.entity_type,
            entity_id=r.entity_id,
            evaluation_status=r.evaluation_status,
            mean_kinetic_temperature_celsius=r.mean_kinetic_temperature_celsius,
            arithmetic_mean_celsius=r.arithmetic_mean_celsius,
            min_temperature_celsius=r.min_temperature_celsius,
            max_temperature_celsius=r.max_temperature_celsius,
            cumulative_hours_above_upper=r.cumulative_hours_above_upper,
            cumulative_hours_below_lower=r.cumulative_hours_below_lower,
            total_duration_hours=0.0,
            readings_count=0,
            hard_boundary_breached=r.hard_boundary_breached,
            mkt_breached=r.mkt_breached,
            cumulative_time_breached=r.cumulative_time_breached,
            quarantine_triggered=r.quarantine_triggered,
            quarantined_kit_count=r.quarantined_kit_count,
            excursions=[],
            evaluated_at=r.evaluated_at,
            evaluated_by=r.evaluated_by,
            summary_notes=r.summary_notes,
        )
        for r in records
    ]


# =========================================================================
# Investigational Product Kit Management & Quarantine Disposition
# =========================================================================


@router.post("/kits", response_model=IPKitResponse, status_code=201)
async def create_kit_endpoint(
    request: Request,
    payload: IPKitCreateRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "dm",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> IPKitResponse:
    """Registers an investigational product kit into the blinded RTSM inventory catalog."""
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )
    verify_change_justification(request)

    kit = IPKit(
        study_id=payload.study_id,
        kit_number=payload.kit_number,
        kit_type=payload.kit_type,
        description=payload.description,
        lot_number=payload.lot_number,
        expiration_date=payload.expiration_date,
        node_id=payload.node_id,
        status="AVAILABLE",
    )
    session.add(kit)
    await session.commit()
    await session.refresh(kit)

    return IPKitResponse(
        id=kit.id,
        study_id=kit.study_id,
        kit_number=kit.kit_number,
        kit_type=kit.kit_type,
        description=kit.description,
        status=kit.status,
        lot_number=kit.lot_number,
        expiration_date=kit.expiration_date,
        node_id=kit.node_id,
        quarantine_reason=kit.quarantine_reason,
        quarantined_at=kit.quarantined_at,
        quarantined_by=kit.quarantined_by,
    )


@router.get("/kits", response_model=list[IPKitResponse])
async def list_kits_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    node_id: str | None = Query(None, description="Optional node/site ID filter"),
    lot_number: str | None = Query(None, description="Optional lot number filter"),
    status: str | None = Query(
        None, description="Optional status filter (e.g. AVAILABLE, QUARANTINED)"
    ),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[IPKitResponse]:
    """Lists investigational product kits."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail=f"Forbidden: Access denied to study {study_id}."
        )

    stmt = select(IPKit).where(
        IPKit.study_id == study_id,
        IPKit.is_deleted.is_(False),
    )
    if node_id:
        stmt = stmt.where(IPKit.node_id == node_id)
    if lot_number:
        stmt = stmt.where(IPKit.lot_number == lot_number)
    if status:
        stmt = stmt.where(IPKit.status == status)

    res = await session.execute(stmt)
    kits = list(res.scalars().all())

    return [
        IPKitResponse(
            id=k.id,
            study_id=k.study_id,
            kit_number=k.kit_number,
            kit_type=k.kit_type,
            description=k.description,
            status=k.status,
            lot_number=k.lot_number,
            expiration_date=k.expiration_date,
            node_id=k.node_id,
            quarantine_reason=k.quarantine_reason,
            quarantined_at=k.quarantined_at,
            quarantined_by=k.quarantined_by,
        )
        for k in kits
    ]


@router.post("/kits/quarantine", status_code=200)
async def quarantine_kits_endpoint(
    request: Request,
    payload: KitQuarantineRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SPONSOR_DM,
            ROLE_CRA,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> dict[str, str | int]:
    """Manually places investigational product kits in QUARANTINED status with Part 11 electronic audit trail."""
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )
    verify_change_justification(request)

    count = await quarantine_kits_transaction(
        session=session,
        study_id=payload.study_id,
        kit_numbers=payload.kit_numbers,
        reason=payload.quarantine_reason,
    )
    await session.commit()
    return {"status": "success", "quarantined_kit_count": count}


@router.post("/kits/release-quarantine", status_code=200)
async def release_quarantine_kits_endpoint(
    request: Request,
    payload: KitReleaseQuarantineRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SYSADMIN,
            "sponsor_admin",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> dict[str, str | int]:
    """Authorizes QA disposition release of quarantined kits back into available inventory."""
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )
    verify_change_justification(request)

    count = await release_quarantined_kits_transaction(
        session=session,
        study_id=payload.study_id,
        kit_numbers=payload.kit_numbers,
        qa_release_justification=payload.qa_release_justification,
    )
    await session.commit()
    return {"status": "success", "released_kit_count": count}


# =========================================================================
# Lot Expiration Extension & Re-Labeling Workflows (PRD-SCM-003)
# =========================================================================


@router.post(
    "/lots/extend-expiry", response_model=LotExpiryExtensionResponse, status_code=201
)
async def extend_lot_expiry_endpoint(
    request: Request,
    payload: LotExpiryExtensionRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SYSADMIN,
            "sponsor_admin",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LotExpiryExtensionResponse:
    """Executes automated lot expiration extension workflow, updating kits across all sites and depots."""
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )
    verify_change_justification(request)

    ext_rec, _ = await extend_lot_expiration_transaction(
        session=session,
        study_id=payload.study_id,
        lot_number=payload.lot_number,
        kit_type=payload.kit_type,
        extended_expiry_date=payload.extended_expiry_date,
        extension_reason=payload.extension_reason,
        stability_protocol_reference=payload.stability_protocol_reference,
    )
    await session.commit()
    await session.refresh(ext_rec)

    return LotExpiryExtensionResponse(
        id=ext_rec.id,
        study_id=ext_rec.study_id,
        lot_number=ext_rec.lot_number,
        kit_type=ext_rec.kit_type,
        initial_expiry_date=ext_rec.initial_expiry_date,
        extended_expiry_date=ext_rec.extended_expiry_date,
        extension_reason=ext_rec.extension_reason,
        stability_protocol_reference=ext_rec.stability_protocol_reference,
        authorized_by=ext_rec.authorized_by,
        approval_status=ext_rec.approval_status,
        affected_kits_count=ext_rec.affected_kits_count,
        relabeling_tasks_generated=ext_rec.relabeling_tasks_generated,
        created_at=ext_rec.created_at,
    )


@router.get("/lots/extensions", response_model=list[LotExpiryExtensionResponse])
async def list_lot_expiry_extensions_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    lot_number: str | None = Query(None, description="Optional lot number filter"),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[LotExpiryExtensionResponse]:
    """Lists lot expiration extension audit records."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail=f"Forbidden: Access denied to study {study_id}."
        )

    stmt = select(LotExpiryExtension).where(
        LotExpiryExtension.study_id == study_id,
        LotExpiryExtension.is_deleted.is_(False),
    )
    if lot_number:
        stmt = stmt.where(LotExpiryExtension.lot_number == lot_number)

    stmt = stmt.order_by(LotExpiryExtension.created_at.desc())
    res = await session.execute(stmt)
    records = list(res.scalars().all())

    return [
        LotExpiryExtensionResponse(
            id=r.id,
            study_id=r.study_id,
            lot_number=r.lot_number,
            kit_type=r.kit_type,
            initial_expiry_date=r.initial_expiry_date,
            extended_expiry_date=r.extended_expiry_date,
            extension_reason=r.extension_reason,
            stability_protocol_reference=r.stability_protocol_reference,
            authorized_by=r.authorized_by,
            approval_status=r.approval_status,
            affected_kits_count=r.affected_kits_count,
            relabeling_tasks_generated=r.relabeling_tasks_generated,
            created_at=r.created_at,
        )
        for r in records
    ]


@router.get("/relabeling-tasks", response_model=list[ReLabelingTaskResponse])
async def list_relabeling_tasks_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    node_id: str | None = Query(None, description="Optional node/site ID filter"),
    lot_number: str | None = Query(None, description="Optional lot number filter"),
    status: str | None = Query(None, description="Optional status filter"),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[ReLabelingTaskResponse]:
    """Lists physical re-labeling action tasks across facilities."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail=f"Forbidden: Access denied to study {study_id}."
        )

    stmt = select(ReLabelingTask).where(
        ReLabelingTask.study_id == study_id,
        ReLabelingTask.is_deleted.is_(False),
    )
    if node_id:
        stmt = stmt.where(ReLabelingTask.node_id == node_id)
    if lot_number:
        stmt = stmt.where(ReLabelingTask.lot_number == lot_number)
    if status:
        stmt = stmt.where(ReLabelingTask.status == status)

    res = await session.execute(stmt)
    tasks = list(res.scalars().all())

    return [
        ReLabelingTaskResponse(
            id=t.id,
            study_id=t.study_id,
            task_number=t.task_number,
            lot_number=t.lot_number,
            node_id=t.node_id,
            affected_kit_count=t.affected_kit_count,
            old_expiry_date=t.old_expiry_date,
            new_expiry_date=t.new_expiry_date,
            status=t.status,
            assigned_to=t.assigned_to,
            completed_at=t.completed_at,
            completed_by=t.completed_by,
            verified_at=t.verified_at,
            verified_by=t.verified_by,
            label_barcode_hash=t.label_barcode_hash,
            relabeling_notes=t.relabeling_notes,
        )
        for t in tasks
    ]


@router.post(
    "/relabeling-tasks/{task_id}/complete", response_model=ReLabelingTaskResponse
)
async def complete_relabeling_task_endpoint(
    task_id: str,
    request: Request,
    payload: ReLabelingTaskCompleteRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_INVESTIGATOR,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
            "crc",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> ReLabelingTaskResponse:
    """Records completion of a physical kit re-labeling task with electronic signature justification."""
    verify_change_justification(request)

    try:
        task = await complete_relabeling_task_transaction(
            session=session,
            task_id=task_id,
            notes=payload.notes,
        )
        await session.commit()
        await session.refresh(task)
    except ReLabelingTaskNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return ReLabelingTaskResponse(
        id=task.id,
        study_id=task.study_id,
        task_number=task.task_number,
        lot_number=task.lot_number,
        node_id=task.node_id,
        affected_kit_count=task.affected_kit_count,
        old_expiry_date=task.old_expiry_date,
        new_expiry_date=task.new_expiry_date,
        status=task.status,
        assigned_to=task.assigned_to,
        completed_at=task.completed_at,
        completed_by=task.completed_by,
        verified_at=task.verified_at,
        verified_by=task.verified_by,
        label_barcode_hash=task.label_barcode_hash,
        relabeling_notes=task.relabeling_notes,
    )


@router.post(
    "/relabeling-tasks/{task_id}/verify", response_model=ReLabelingTaskResponse
)
async def verify_relabeling_task_endpoint(
    task_id: str,
    request: Request,
    payload: ReLabelingTaskVerifyRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_CRA,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> ReLabelingTaskResponse:
    """Dual QA verification of completed re-labeling task with barcode verification hash."""
    verify_change_justification(request)

    try:
        task = await verify_relabeling_task_transaction(
            session=session,
            task_id=task_id,
            label_barcode_hash=payload.label_barcode_hash,
            notes=payload.notes,
        )
        await session.commit()
        await session.refresh(task)
    except ReLabelingTaskNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return ReLabelingTaskResponse(
        id=task.id,
        study_id=task.study_id,
        task_number=task.task_number,
        lot_number=task.lot_number,
        node_id=task.node_id,
        affected_kit_count=task.affected_kit_count,
        old_expiry_date=task.old_expiry_date,
        new_expiry_date=task.new_expiry_date,
        status=task.status,
        assigned_to=task.assigned_to,
        completed_at=task.completed_at,
        completed_by=task.completed_by,
        verified_at=task.verified_at,
        verified_by=task.verified_by,
        label_barcode_hash=task.label_barcode_hash,
        relabeling_notes=task.relabeling_notes,
    )
