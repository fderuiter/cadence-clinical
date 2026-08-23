"""FastAPI router for Multi-Echelon RTSM Supply Hierarchy, Predictive Resupply & Transfer Orders.

Requirements: PRD-SCM-001, GMP Annex 13
"""

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.database.models import (
    SupplyNode,
    SupplyNodeInventory,
    TransferOrder,
)
from apps.execution.dependencies import verify_change_justification
from apps.execution.domain.rtsm_models import (
    MultiEchelonRollup,
    PredictiveForecastInput,
)
from apps.execution.presentation.routers.rtsm_schemas import (
    PackingManifestCreateRequest,
    PackingManifestResponse,
    PredictiveForecastRequest,
    PredictiveForecastResponse,
    SupplyNodeCreateRequest,
    SupplyNodeInventoryAdjustRequest,
    SupplyNodeInventoryResponse,
    SupplyNodeResponse,
    TransferOrderApprovalRequest,
    TransferOrderCreateRequest,
    TransferOrderReceiveRequest,
    TransferOrderResponse,
    TransferOrderShipRequest,
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
