"""FastAPI router for Multi-Echelon RTSM Supply Hierarchy, Predictive Resupply & Transfer Orders.

Requirements: PRD-SCM-001, GMP Annex 13
"""

from typing import Any

from fastapi import (
    APIRouter,
    BackgroundTasks,
    Depends,
    HTTPException,
    Query,
    Request,
)
from pydantic import BaseModel, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.adaptive_math import (
    CrmDoseEscalationEngine as CrmDoseEscalationEngine,
)
from apps.execution.adaptive_math import (
    calculate_boin_boundaries as calculate_boin_boundaries,
)
from apps.execution.database.context import (
    current_change_reason,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    CGTBedsideVerification,
    CGTChainOfCustody,
    CGTDestructionCertificate,
    CGTMilestoneEvent,
    CGTVialReturnReconciliation,
    DrugStabilityBudget,
    ExcursionEvaluation,
    IPKit,
    LotExpiryExtension,
    ReLabelingTask,
    ResupplyEvent,
    SupplyNode,
    SupplyNodeInventory,
    TemperatureLogger,
    TransferOrder,
)
from apps.execution.dependencies import verify_change_justification
from apps.execution.domain.adaptive_models import (
    AdaptiveTrialConfigDTO as AdaptiveTrialConfigDTO,
)
from apps.execution.domain.adaptive_models import (
    BoinBoundariesCalculationRequest as BoinBoundariesCalculationRequest,
)
from apps.execution.domain.adaptive_models import (
    BoinBoundariesCalculationResponse as BoinBoundariesCalculationResponse,
)
from apps.execution.domain.adaptive_models import (
    BoinConfig as BoinConfig,
)
from apps.execution.domain.adaptive_models import (
    CohortEvaluationInput as CohortEvaluationInput,
)
from apps.execution.domain.adaptive_models import (
    CohortEvaluationResult as CohortEvaluationResult,
)
from apps.execution.domain.adaptive_models import (
    CohortRecommendationAction as CohortRecommendationAction,
)
from apps.execution.domain.adaptive_models import (
    CohortTransitionLogResponse as CohortTransitionLogResponse,
)
from apps.execution.domain.adaptive_models import (
    CohortTransitionRequest as CohortTransitionRequest,
)
from apps.execution.domain.adaptive_models import (
    CrmConfig as CrmConfig,
)
from apps.execution.domain.adaptive_models import (
    CrmPosteriorCalculationRequest as CrmPosteriorCalculationRequest,
)
from apps.execution.domain.adaptive_models import (
    CrmPosteriorCalculationResponse as CrmPosteriorCalculationResponse,
)
from apps.execution.domain.adaptive_models import (
    RAREvaluationResult as RAREvaluationResult,
)
from apps.execution.domain.adaptive_models import (
    RARExpandOrTerminateArmRequest as RARExpandOrTerminateArmRequest,
)
from apps.execution.domain.rtsm_models import (
    MultiEchelonRollup,
    PredictiveForecastInput,
    StabilityBudgetConfig,
)
from apps.execution.presentation.routers.rtsm_schemas import (
    CGTApheresisCreateRequest,
    CGTBarcodeVerifyRequest,
    CGTBarcodeVerifyResponse,
    CGTBedsideVerificationRequest,
    CGTBedsideVerificationResponse,
    CGTChainOfCustodyResponse,
    CGTDestructionCertificateCreateRequest,
    CGTDestructionCertificateResponse,
    CGTFullTraceabilityResponse,
    CGTInfusionExecuteRequest,
    CGTManufacturingBindRequest,
    CGTMilestoneCreateRequest,
    CGTMilestoneResponse,
    CGTVialReturnReconciliationRequest,
    CGTVialReturnReconciliationResponse,
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
from apps.execution.rtsm_adaptive import (
    AdaptiveTrialConfigNotFoundError,
    CohortTransitionError,
    InvalidDoseLevelError,
    RARArmNotFoundError,
    add_or_terminate_rar_arm_transaction,
    advance_cohort_decision_transaction,
    create_or_update_adaptive_config_transaction,
    evaluate_and_update_rar_transaction,
    evaluate_cohort_outcomes_transaction,
    get_adaptive_study_state_transaction,
)
from apps.execution.rtsm_cgt import (
    BarcodeTamperError,
    BedsideVerificationMismatchError,
    CGTChainOfCustodyNotFoundError,
    CryogenicExcursionError,
    DuplicateWitnessError,
    InfusionLockedError,
    InvalidDINError,
    bind_manufacturing_batch_transaction,
    execute_infusion_administration_transaction,
    generate_certificate_of_destruction_transaction,
    initialize_apheresis_coc_transaction,
    log_vial_return_reconciliation_transaction,
    record_cryogenic_milestone_transaction,
    verify_bedside_dual_signatures_transaction,
    verify_crypto_barcode,
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
    ExpiredKitError,
    InsufficientStockError,
    InvalidHierarchyError,
    InvalidTransferOrderStateError,
    QuarantinedKitError,
    SiteInventoryNotFoundError,
    StorageCapacityExceededError,
    TransferOrderNotFoundError,
    approve_transfer_order_transaction,
    build_supply_hierarchy_tree,
    create_transfer_order_transaction,
    dispense_kit_transaction,
    generate_packing_manifest_transaction,
    receive_transfer_order_transaction,
    run_predictive_forecast_transaction,
    ship_transfer_order_transaction,
)
from apps.execution.trial_lock import TrialLockManager
from packages.security import (
    ROLE_CRA,
    ROLE_CRC,
    ROLE_DATA_MANAGER,
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

    # Enforce sovereign site execution gate on destination site (PRD-SYS-121, PRD-SSU-003, ADR-2202)
    from apps.execution.site_execution_gatekeeper import (
        require_site_mutation_authorization,
    )

    if payload.study_id and payload.destination_node_id:
        await require_site_mutation_authorization(
            site_id=payload.destination_node_id,
            user_id=principal.user_id,
            operation_type="TRANSFER_ORDER",
            study_id=payload.study_id,
            token=request.headers.get("X-Site-Greenlight-Token"),
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


# =========================================================================
# Cell & Gene Therapy (CGT) Vein-to-Vein COC/COI & Destruction Endpoints
# Requirements: PRD-SCM-005, PRD-SCM-006, ADR-2199
# =========================================================================


@router.post(
    "/cgt/apheresis", response_model=CGTChainOfCustodyResponse, status_code=201
)
async def initialize_apheresis_endpoint(
    payload: CGTApheresisCreateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
            "crc",
            "investigator",
            "site_investigator",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CGTChainOfCustodyResponse:
    """Initializes Vein-to-Vein tracking upon initial apheresis cellular harvest."""
    if not can_access_study(principal, payload.study_id) or not can_access_site(
        principal, payload.site_id
    ):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access to requested study or site denied.",
        )
    if TrialLockManager.is_site_locked(payload.site_id):
        raise HTTPException(
            status_code=423,
            detail=f"Locked: Site {payload.site_id} is currently locked against changes.",
        )
    verify_change_justification(request)

    try:
        coc = await initialize_apheresis_coc_transaction(
            session=session,
            study_id=payload.study_id,
            site_id=payload.site_id,
            subject_id=payload.subject_id,
            collector_id=payload.collector_id,
            collector_name=payload.collector_name,
            collector_role=payload.collector_role,
            initial_temp_celsius=payload.initial_temp_celsius,
            din=payload.din,
            dry_shipper_id=payload.dry_shipper_id,
            seal_id=payload.seal_id,
            notes=payload.notes,
        )
        await session.commit()
        await session.refresh(coc)
    except InvalidDINError as e:
        raise HTTPException(status_code=400, detail=str(e))

    return CGTChainOfCustodyResponse(
        id=str(coc.id),
        study_id=coc.study_id,
        subject_id=coc.subject_id,
        din=coc.din,
        batch_id=coc.batch_id,
        current_stage=coc.current_stage,
        status=coc.status,
        tamper_evident_barcode=coc.tamper_evident_barcode,
        barcode_signature=coc.barcode_signature,
        collection_site_id=coc.collection_site_id,
        collection_timestamp=coc.collection_timestamp,
        manufacturing_facility_id=coc.manufacturing_facility_id,
        cryo_temp_excursion_count=coc.cryo_temp_excursion_count,
        lowest_temp_celsius=coc.lowest_temp_celsius,
        highest_temp_celsius=coc.highest_temp_celsius,
        infusion_unlocked=coc.infusion_unlocked,
        infused_at=coc.infused_at,
        reconciled_at=coc.reconciled_at,
    )


@router.post("/cgt/milestone", response_model=CGTMilestoneResponse, status_code=201)
async def record_cgt_milestone_endpoint(
    payload: CGTMilestoneCreateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "cra",
            "crc",
            "investigator",
            "site_investigator",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CGTMilestoneResponse:
    """Records a cryogenic transit milestone with GPS coordinates and temperature logging."""
    verify_change_justification(request)

    try:
        milestone = await record_cryogenic_milestone_transaction(
            session=session,
            coc_id=payload.coc_id,
            stage=payload.stage,
            location_name=payload.location_name,
            temperature_celsius=payload.temperature_celsius,
            custodian_id=payload.custodian_id,
            custodian_name=payload.custodian_name,
            custodian_role=payload.custodian_role,
            facility_id=payload.facility_id,
            latitude=payload.latitude,
            longitude=payload.longitude,
            carrier=payload.carrier,
            tracking_number=payload.tracking_number,
            seal_intact=payload.seal_intact,
            seal_id=payload.seal_id,
            dry_shipper_id=payload.dry_shipper_id,
            signature_token=payload.signature_token,
            notes=payload.notes,
        )
        await session.commit()
        await session.refresh(milestone)
    except CGTChainOfCustodyNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return CGTMilestoneResponse(
        id=str(milestone.id),
        study_id=milestone.study_id,
        chain_of_custody_id=milestone.chain_of_custody_id,
        stage=milestone.stage,
        milestone_type=milestone.milestone_type,
        timestamp=milestone.timestamp,
        location_name=milestone.location_name,
        facility_id=milestone.facility_id,
        latitude=milestone.latitude,
        longitude=milestone.longitude,
        temperature_celsius=milestone.temperature_celsius,
        temperature_status=milestone.temperature_status,
        custodian_id=milestone.custodian_id,
        custodian_name=milestone.custodian_name,
        custodian_role=milestone.custodian_role,
        carrier=milestone.carrier,
        tracking_number=milestone.tracking_number,
        seal_intact=milestone.seal_intact,
        seal_id=milestone.seal_id,
        dry_shipper_id=milestone.dry_shipper_id,
        electronic_signature=milestone.electronic_signature,
        notes=milestone.notes,
    )


@router.post("/cgt/manufacturing-bind", response_model=CGTChainOfCustodyResponse)
async def bind_manufacturing_batch_endpoint(
    payload: CGTManufacturingBindRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_CRA,
            ROLE_SYSADMIN,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            "sponsor_admin",
            "cra",
            "investigator",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CGTChainOfCustodyResponse:
    """Binds manufacturing batch record to original subject identity and regenerates barcode."""
    verify_change_justification(request)

    try:
        coc = await bind_manufacturing_batch_transaction(
            session=session,
            coc_id=payload.coc_id,
            batch_id=payload.batch_id,
            manufacturing_facility_id=payload.manufacturing_facility_id,
            operator_id=payload.operator_id,
            operator_name=payload.operator_name,
            operator_role=payload.operator_role,
            temperature_celsius=payload.temperature_celsius,
            notes=payload.notes,
        )
        await session.commit()
        await session.refresh(coc)
    except CGTChainOfCustodyNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return CGTChainOfCustodyResponse(
        id=str(coc.id),
        study_id=coc.study_id,
        subject_id=coc.subject_id,
        din=coc.din,
        batch_id=coc.batch_id,
        current_stage=coc.current_stage,
        status=coc.status,
        tamper_evident_barcode=coc.tamper_evident_barcode,
        barcode_signature=coc.barcode_signature,
        collection_site_id=coc.collection_site_id,
        collection_timestamp=coc.collection_timestamp,
        manufacturing_facility_id=coc.manufacturing_facility_id,
        cryo_temp_excursion_count=coc.cryo_temp_excursion_count,
        lowest_temp_celsius=coc.lowest_temp_celsius,
        highest_temp_celsius=coc.highest_temp_celsius,
        infusion_unlocked=coc.infusion_unlocked,
        infused_at=coc.infused_at,
        reconciled_at=coc.reconciled_at,
    )


@router.post("/cgt/bedside-verify", response_model=CGTBedsideVerificationResponse)
async def bedside_dual_verification_endpoint(
    payload: CGTBedsideVerificationRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRC,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_SPONSOR_ADMIN,
            ROLE_SYSADMIN,
            "crc",
            "investigator",
            "site_investigator",
            "sponsor_admin",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CGTBedsideVerificationResponse:
    """Executes mandatory two-person electronic bedside verification, unlocking the drug infusion eCRF."""
    verify_change_justification(request)

    try:
        verification = await verify_bedside_dual_signatures_transaction(
            session=session,
            coc_id=payload.coc_id,
            scanned_wristband_subject_id=payload.scanned_wristband_subject_id,
            scanned_bag_barcode=payload.scanned_bag_barcode,
            primary_verifier_id=payload.primary_verifier_id,
            primary_verifier_name=payload.primary_verifier_name,
            primary_verifier_role=payload.primary_verifier_role,
            primary_signature_token=payload.primary_signature_token,
            secondary_verifier_id=payload.secondary_verifier_id,
            secondary_verifier_name=payload.secondary_verifier_name,
            secondary_verifier_role=payload.secondary_verifier_role,
            secondary_signature_token=payload.secondary_signature_token,
            notes=payload.notes,
        )
        await session.commit()
        await session.refresh(verification)
    except CGTChainOfCustodyNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except (
        DuplicateWitnessError,
        BedsideVerificationMismatchError,
        BarcodeTamperError,
        CryogenicExcursionError,
    ) as e:
        raise HTTPException(status_code=400, detail=str(e))

    return CGTBedsideVerificationResponse(
        id=str(verification.id),
        study_id=verification.study_id,
        site_id=verification.site_id,
        chain_of_custody_id=verification.chain_of_custody_id,
        subject_id=verification.subject_id,
        din=verification.din,
        batch_id=verification.batch_id,
        wristband_matched=verification.wristband_matched,
        bag_matched=verification.bag_matched,
        temperature_chain_verified=verification.temperature_chain_verified,
        primary_verifier_id=verification.primary_verifier_id,
        primary_verifier_name=verification.primary_verifier_name,
        primary_verifier_role=verification.primary_verifier_role,
        primary_signed_at=verification.primary_signed_at,
        secondary_verifier_id=verification.secondary_verifier_id,
        secondary_verifier_name=verification.secondary_verifier_name,
        secondary_verifier_role=verification.secondary_verifier_role,
        secondary_signed_at=verification.secondary_signed_at,
        status=verification.status,
        unlock_token=verification.unlock_token,
        verification_notes=verification.verification_notes,
    )


@router.post("/cgt/infuse", response_model=CGTChainOfCustodyResponse)
async def execute_infusion_endpoint(
    payload: CGTInfusionExecuteRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRC,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_SPONSOR_ADMIN,
            ROLE_SYSADMIN,
            "crc",
            "investigator",
            "site_investigator",
            "sponsor_admin",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CGTChainOfCustodyResponse:
    """Records cellular drug infusion administration, gated by the bedside verification unlock token."""
    verify_change_justification(request)

    try:
        coc = await execute_infusion_administration_transaction(
            session=session,
            coc_id=payload.coc_id,
            unlock_token=payload.unlock_token,
            administrator_id=payload.administrator_id,
            administrator_name=payload.administrator_name,
            administrator_role=payload.administrator_role,
            volume_infused_ml=payload.volume_infused_ml,
            vital_signs_pre_temp_celsius=payload.vital_signs_pre_temp_celsius,
            vital_signs_post_temp_celsius=payload.vital_signs_post_temp_celsius,
            notes=payload.notes,
        )
        await session.commit()
        await session.refresh(coc)
    except CGTChainOfCustodyNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except InfusionLockedError as e:
        raise HTTPException(status_code=403, detail=str(e))

    return CGTChainOfCustodyResponse(
        id=str(coc.id),
        study_id=coc.study_id,
        subject_id=coc.subject_id,
        din=coc.din,
        batch_id=coc.batch_id,
        current_stage=coc.current_stage,
        status=coc.status,
        tamper_evident_barcode=coc.tamper_evident_barcode,
        barcode_signature=coc.barcode_signature,
        collection_site_id=coc.collection_site_id,
        collection_timestamp=coc.collection_timestamp,
        manufacturing_facility_id=coc.manufacturing_facility_id,
        cryo_temp_excursion_count=coc.cryo_temp_excursion_count,
        lowest_temp_celsius=coc.lowest_temp_celsius,
        highest_temp_celsius=coc.highest_temp_celsius,
        infusion_unlocked=coc.infusion_unlocked,
        infused_at=coc.infused_at,
        reconciled_at=coc.reconciled_at,
    )


@router.post(
    "/cgt/returns", response_model=CGTVialReturnReconciliationResponse, status_code=201
)
async def log_vial_return_endpoint(
    payload: CGTVialReturnReconciliationRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRC,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_CRA,
            ROLE_SPONSOR_ADMIN,
            ROLE_SYSADMIN,
            "crc",
            "investigator",
            "site_investigator",
            "cra",
            "sponsor_admin",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CGTVialReturnReconciliationResponse:
    """Logs returned kit/vial accountability and calculates patient adherence compliance."""
    if not can_access_study(principal, payload.study_id) or not can_access_site(
        principal, payload.site_id
    ):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access to requested study or site denied.",
        )
    if TrialLockManager.is_site_locked(payload.site_id):
        raise HTTPException(
            status_code=423,
            detail=f"Locked: Site {payload.site_id} is currently locked against changes.",
        )
    verify_change_justification(request)

    recon = await log_vial_return_reconciliation_transaction(
        session=session,
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_id=payload.subject_id,
        kit_number=payload.kit_number,
        vial_identifier=payload.vial_identifier,
        lot_number=payload.lot_number,
        expected_quantity=payload.expected_quantity,
        returned_quantity=payload.returned_quantity,
        intact_vials=payload.intact_vials,
        used_vials=payload.used_vials,
        residual_volume_ml=payload.residual_volume_ml,
        din=payload.din,
        logged_by=payload.logged_by,
        disposition=payload.disposition,
    )
    await session.commit()
    await session.refresh(recon)

    return CGTVialReturnReconciliationResponse(
        id=str(recon.id),
        study_id=recon.study_id,
        site_id=recon.site_id,
        subject_id=recon.subject_id,
        kit_number=recon.kit_number,
        vial_identifier=recon.vial_identifier,
        lot_number=recon.lot_number,
        din=recon.din,
        expected_quantity=recon.expected_quantity,
        returned_quantity=recon.returned_quantity,
        intact_vials=recon.intact_vials,
        used_vials=recon.used_vials,
        residual_volume_ml=recon.residual_volume_ml,
        adherence_percentage=recon.adherence_percentage,
        disposition=recon.disposition,
        logged_by=recon.logged_by,
        logged_at=recon.logged_at,
        certificate_id=recon.certificate_id,
    )


@router.post(
    "/cgt/destruction-certificate",
    response_model=CGTDestructionCertificateResponse,
    status_code=201,
)
async def generate_destruction_certificate_endpoint(
    payload: CGTDestructionCertificateCreateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_CRC,
            ROLE_CRA,
            ROLE_SPONSOR_ADMIN,
            ROLE_SYSADMIN,
            "investigator",
            "site_investigator",
            "crc",
            "cra",
            "sponsor_admin",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CGTDestructionCertificateResponse:
    """Generates a formal Certificate of Destruction with dual witness electronic signatures."""
    if not can_access_study(principal, payload.study_id) or not can_access_site(
        principal, payload.site_id
    ):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access to requested study or site denied.",
        )
    if TrialLockManager.is_site_locked(payload.site_id):
        raise HTTPException(
            status_code=423,
            detail=f"Locked: Site {payload.site_id} is currently locked against changes.",
        )
    verify_change_justification(request)

    try:
        cert = await generate_certificate_of_destruction_transaction(
            session=session,
            study_id=payload.study_id,
            site_id=payload.site_id,
            lot_number=payload.lot_number,
            kit_numbers=payload.kit_numbers,
            vial_identifiers=payload.vial_identifiers,
            destruction_reason=payload.destruction_reason,
            destruction_method=payload.destruction_method,
            destruction_facility=payload.destruction_facility,
            witness_1_id=payload.witness_1_id,
            witness_1_name=payload.witness_1_name,
            witness_1_role=payload.witness_1_role,
            witness_1_signature_token=payload.witness_1_signature_token,
            witness_2_id=payload.witness_2_id,
            witness_2_name=payload.witness_2_name,
            witness_2_role=payload.witness_2_role,
            witness_2_signature_token=payload.witness_2_signature_token,
            quantity_destroyed=payload.quantity_destroyed,
            residual_volume_ml=payload.residual_volume_ml,
            subject_id=payload.subject_id,
            din=payload.din,
            coc_id=payload.coc_id,
            notes=payload.notes,
        )
        await session.commit()
        await session.refresh(cert)
    except DuplicateWitnessError as e:
        raise HTTPException(status_code=400, detail=str(e))

    return CGTDestructionCertificateResponse(
        id=str(cert.id),
        study_id=cert.study_id,
        site_id=cert.site_id,
        certificate_number=cert.certificate_number,
        chain_of_custody_id=cert.chain_of_custody_id,
        subject_id=cert.subject_id,
        din=cert.din,
        lot_number=cert.lot_number,
        kit_numbers=cert.kit_numbers,
        vial_identifiers=cert.vial_identifiers,
        quantity_destroyed=cert.quantity_destroyed,
        unit_of_measure=cert.unit_of_measure,
        residual_volume_ml=cert.residual_volume_ml,
        destruction_reason=cert.destruction_reason,
        destruction_method=cert.destruction_method,
        destruction_facility=cert.destruction_facility,
        destruction_timestamp=cert.destruction_timestamp,
        witness_1_id=cert.witness_1_id,
        witness_1_name=cert.witness_1_name,
        witness_1_role=cert.witness_1_role,
        witness_1_signed_at=cert.witness_1_signed_at,
        witness_2_id=cert.witness_2_id,
        witness_2_name=cert.witness_2_name,
        witness_2_role=cert.witness_2_role,
        witness_2_signed_at=cert.witness_2_signed_at,
        cryptographic_seal=cert.cryptographic_seal,
        status=cert.status,
        reconciliation_notes=cert.reconciliation_notes,
    )


@router.post("/cgt/verify-barcode", response_model=CGTBarcodeVerifyResponse)
async def verify_cgt_barcode_endpoint(
    payload: CGTBarcodeVerifyRequest,
    principal: Principal = Depends(get_principal),
) -> CGTBarcodeVerifyResponse:
    """Verifies cryptographic authenticity and entity bindings for a scanned barcode."""
    is_valid, failure_reason, data = verify_crypto_barcode(
        barcode_payload=payload.barcode_payload,
        expected_subject_id=payload.expected_subject_id,
        expected_din=payload.expected_din,
        expected_batch_id=payload.expected_batch_id,
    )

    return CGTBarcodeVerifyResponse(
        is_valid=is_valid,
        failure_reason=failure_reason,
        barcode_id=data.get("barcode_id"),
        study_id=data.get("study_id"),
        site_id=data.get("site_id"),
        subject_id=data.get("subject_id"),
        din=data.get("din"),
        batch_id=data.get("batch_id"),
        crypto_hash=data.get("crypto_hash"),
    )


@router.get(
    "/cgt/chain-of-custody/{study_id}/{subject_id}",
    response_model=CGTFullTraceabilityResponse,
)
async def get_subject_cgt_traceability_endpoint(
    study_id: str,
    subject_id: str,
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CGTFullTraceabilityResponse:
    """Retrieves full Vein-to-Vein traceability ledger for a subject."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail="Forbidden: Access to requested study denied."
        )

    stmt_coc = select(CGTChainOfCustody).where(
        CGTChainOfCustody.study_id == study_id,
        CGTChainOfCustody.subject_id == subject_id,
    )
    res_coc = await session.execute(stmt_coc)
    coc = res_coc.scalar_one_or_none()
    if not coc:
        raise HTTPException(
            status_code=404,
            detail=f"No Chain of Custody record found for subject {subject_id} in study {study_id}.",
        )

    # Milestones
    stmt_ms = (
        select(CGTMilestoneEvent)
        .where(CGTMilestoneEvent.chain_of_custody_id == str(coc.id))
        .order_by(CGTMilestoneEvent.timestamp.asc())
    )
    res_ms = await session.execute(stmt_ms)
    milestones = res_ms.scalars().all()

    # Bedside verification
    stmt_bv = select(CGTBedsideVerification).where(
        CGTBedsideVerification.chain_of_custody_id == str(coc.id),
        CGTBedsideVerification.status == "VERIFIED",
    )
    res_bv = await session.execute(stmt_bv)
    bv = res_bv.scalar_one_or_none()

    # Destruction cert
    stmt_dc = select(CGTDestructionCertificate).where(
        CGTDestructionCertificate.chain_of_custody_id == str(coc.id)
    )
    res_dc = await session.execute(stmt_dc)
    dc = res_dc.scalar_one_or_none()

    coc_resp = CGTChainOfCustodyResponse(
        id=str(coc.id),
        study_id=coc.study_id,
        subject_id=coc.subject_id,
        din=coc.din,
        batch_id=coc.batch_id,
        current_stage=coc.current_stage,
        status=coc.status,
        tamper_evident_barcode=coc.tamper_evident_barcode,
        barcode_signature=coc.barcode_signature,
        collection_site_id=coc.collection_site_id,
        collection_timestamp=coc.collection_timestamp,
        manufacturing_facility_id=coc.manufacturing_facility_id,
        cryo_temp_excursion_count=coc.cryo_temp_excursion_count,
        lowest_temp_celsius=coc.lowest_temp_celsius,
        highest_temp_celsius=coc.highest_temp_celsius,
        infusion_unlocked=coc.infusion_unlocked,
        infused_at=coc.infused_at,
        reconciled_at=coc.reconciled_at,
    )

    ms_resp = [
        CGTMilestoneResponse(
            id=str(m.id),
            study_id=m.study_id,
            chain_of_custody_id=m.chain_of_custody_id,
            stage=m.stage,
            milestone_type=m.milestone_type,
            timestamp=m.timestamp,
            location_name=m.location_name,
            facility_id=m.facility_id,
            latitude=m.latitude,
            longitude=m.longitude,
            temperature_celsius=m.temperature_celsius,
            temperature_status=m.temperature_status,
            custodian_id=m.custodian_id,
            custodian_name=m.custodian_name,
            custodian_role=m.custodian_role,
            carrier=m.carrier,
            tracking_number=m.tracking_number,
            seal_intact=m.seal_intact,
            seal_id=m.seal_id,
            dry_shipper_id=m.dry_shipper_id,
            electronic_signature=m.electronic_signature,
            notes=m.notes,
        )
        for m in milestones
    ]

    bv_resp = (
        CGTBedsideVerificationResponse(
            id=str(bv.id),
            study_id=bv.study_id,
            site_id=bv.site_id,
            chain_of_custody_id=bv.chain_of_custody_id,
            subject_id=bv.subject_id,
            din=bv.din,
            batch_id=bv.batch_id,
            wristband_matched=bv.wristband_matched,
            bag_matched=bv.bag_matched,
            temperature_chain_verified=bv.temperature_chain_verified,
            primary_verifier_id=bv.primary_verifier_id,
            primary_verifier_name=bv.primary_verifier_name,
            primary_verifier_role=bv.primary_verifier_role,
            primary_signed_at=bv.primary_signed_at,
            secondary_verifier_id=bv.secondary_verifier_id,
            secondary_verifier_name=bv.secondary_verifier_name,
            secondary_verifier_role=bv.secondary_verifier_role,
            secondary_signed_at=bv.secondary_signed_at,
            status=bv.status,
            unlock_token=bv.unlock_token,
            verification_notes=bv.verification_notes,
        )
        if bv
        else None
    )

    dc_resp = (
        CGTDestructionCertificateResponse(
            id=str(dc.id),
            study_id=dc.study_id,
            site_id=dc.site_id,
            certificate_number=dc.certificate_number,
            chain_of_custody_id=dc.chain_of_custody_id,
            subject_id=dc.subject_id,
            din=dc.din,
            lot_number=dc.lot_number,
            kit_numbers=dc.kit_numbers,
            vial_identifiers=dc.vial_identifiers,
            quantity_destroyed=dc.quantity_destroyed,
            unit_of_measure=dc.unit_of_measure,
            residual_volume_ml=dc.residual_volume_ml,
            destruction_reason=dc.destruction_reason,
            destruction_method=dc.destruction_method,
            destruction_facility=dc.destruction_facility,
            destruction_timestamp=dc.destruction_timestamp,
            witness_1_id=dc.witness_1_id,
            witness_1_name=dc.witness_1_name,
            witness_1_role=dc.witness_1_role,
            witness_1_signed_at=dc.witness_1_signed_at,
            witness_2_id=dc.witness_2_id,
            witness_2_name=dc.witness_2_name,
            witness_2_role=dc.witness_2_role,
            witness_2_signed_at=dc.witness_2_signed_at,
            cryptographic_seal=dc.cryptographic_seal,
            status=dc.status,
            reconciliation_notes=dc.reconciliation_notes,
        )
        if dc
        else None
    )

    return CGTFullTraceabilityResponse(
        chain_of_custody=coc_resp,
        milestones=ms_resp,
        bedside_verification=bv_resp,
        destruction_certificate=dc_resp,
    )


@router.get(
    "/cgt/certificates/{study_id}",
    response_model=list[CGTDestructionCertificateResponse],
)
async def list_destruction_certificates_endpoint(
    study_id: str,
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[CGTDestructionCertificateResponse]:
    """Lists all Certificates of Destruction for a study."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail="Forbidden: Access to requested study denied."
        )

    stmt = (
        select(CGTDestructionCertificate)
        .where(CGTDestructionCertificate.study_id == study_id)
        .order_by(CGTDestructionCertificate.destruction_timestamp.desc())
    )
    res = await session.execute(stmt)
    certs = res.scalars().all()

    return [
        CGTDestructionCertificateResponse(
            id=str(c.id),
            study_id=c.study_id,
            site_id=c.site_id,
            certificate_number=c.certificate_number,
            chain_of_custody_id=c.chain_of_custody_id,
            subject_id=c.subject_id,
            din=c.din,
            lot_number=c.lot_number,
            kit_numbers=c.kit_numbers,
            vial_identifiers=c.vial_identifiers,
            quantity_destroyed=c.quantity_destroyed,
            unit_of_measure=c.unit_of_measure,
            residual_volume_ml=c.residual_volume_ml,
            destruction_reason=c.destruction_reason,
            destruction_method=c.destruction_method,
            destruction_facility=c.destruction_facility,
            destruction_timestamp=c.destruction_timestamp,
            witness_1_id=c.witness_1_id,
            witness_1_name=c.witness_1_name,
            witness_1_role=c.witness_1_role,
            witness_1_signed_at=c.witness_1_signed_at,
            witness_2_id=c.witness_2_id,
            witness_2_name=c.witness_2_name,
            witness_2_role=c.witness_2_role,
            witness_2_signed_at=c.witness_2_signed_at,
            cryptographic_seal=c.cryptographic_seal,
            status=c.status,
            reconciliation_notes=c.reconciliation_notes,
        )
        for c in certs
    ]


@router.get(
    "/cgt/reconciliations/{study_id}",
    response_model=list[CGTVialReturnReconciliationResponse],
)
async def list_vial_reconciliations_endpoint(
    study_id: str,
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[CGTVialReturnReconciliationResponse]:
    """Lists all product return and accountability reconciliation records for a study."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail="Forbidden: Access to requested study denied."
        )

    stmt = (
        select(CGTVialReturnReconciliation)
        .where(CGTVialReturnReconciliation.study_id == study_id)
        .order_by(CGTVialReturnReconciliation.logged_at.desc())
    )
    res = await session.execute(stmt)
    recons = res.scalars().all()

    return [
        CGTVialReturnReconciliationResponse(
            id=str(r.id),
            study_id=r.study_id,
            site_id=r.site_id,
            subject_id=r.subject_id,
            kit_number=r.kit_number,
            vial_identifier=r.vial_identifier,
            lot_number=r.lot_number,
            din=r.din,
            expected_quantity=r.expected_quantity,
            returned_quantity=r.returned_quantity,
            intact_vials=r.intact_vials,
            used_vials=r.used_vials,
            residual_volume_ml=r.residual_volume_ml,
            adherence_percentage=r.adherence_percentage,
            disposition=r.disposition,
            logged_by=r.logged_by,
            logged_at=r.logged_at,
            certificate_id=r.certificate_id,
        )
        for r in recons
    ]


# =========================================================================
# Adaptive Oncology Trial Dose Escalation (BOIN / CRM / 3+3) & RAR Endpoints
# Requirements: PRD-SCM-004, ADR-2199
# =========================================================================


@router.post("/adaptive/config", response_model=dict[str, Any], status_code=201)
async def create_or_update_adaptive_config_endpoint(
    payload: AdaptiveTrialConfigDTO,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_CRC,
            ROLE_SYSADMIN,
            ROLE_DATA_MANAGER,
            "sponsor_admin",
            "investigator",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> dict[str, Any]:
    """Registers or updates adaptive oncology dose-escalation or RAR trial design parameters."""
    verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason")

    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )

    record = await create_or_update_adaptive_config_transaction(
        session=session,
        config=payload,
        user_id=principal.user_id,
        change_reason=change_reason,
    )
    await session.commit()
    await session.refresh(record)

    return {
        "id": record.id,
        "study_id": record.study_id,
        "design_type": record.design_type,
        "target_toxicity_rate": record.target_toxicity_rate,
        "cohort_size": record.cohort_size,
        "is_active": record.is_active,
    }


@router.get("/adaptive/config", response_model=dict[str, Any])
async def get_adaptive_config_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> dict[str, Any]:
    """Retrieves current adaptive dose-escalation trial configuration, active cohort state, and history."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {study_id}.",
        )

    state = await get_adaptive_study_state_transaction(
        session=session, study_id=study_id
    )
    if not state.get("config"):
        raise HTTPException(
            status_code=404,
            detail=f"No adaptive configuration found for study {study_id}.",
        )

    cfg = state["config"]
    active_cohort = state["active_cohort"]
    return {
        "study_id": study_id,
        "design_type": cfg.design_type,
        "target_toxicity_rate": cfg.target_toxicity_rate,
        "dose_levels": cfg.dose_levels,
        "boin_config": cfg.boin_config,
        "crm_config": cfg.crm_config,
        "three_plus_three_config": cfg.three_plus_three_config,
        "rar_config": cfg.rar_config,
        "cohort_size": cfg.cohort_size,
        "is_active": cfg.is_active,
        "active_cohort": (
            {
                "cohort_number": active_cohort.cohort_number,
                "current_dose_level_index": active_cohort.current_dose_level_index,
                "current_dose_name": active_cohort.current_dose_name,
                "enrolled_count": active_cohort.enrolled_count,
                "dlt_count": active_cohort.dlt_count,
                "recommendation_action": active_cohort.recommendation_action,
                "recommended_dose_level_index": active_cohort.recommended_dose_level_index,
                "recommended_dose_name": active_cohort.recommended_dose_name,
                "status": active_cohort.status,
                "rationale": active_cohort.rationale,
            }
            if active_cohort
            else None
        ),
    }


@router.post("/adaptive/cohorts/evaluate", response_model=CohortEvaluationResult)
async def evaluate_adaptive_cohort_endpoint(
    payload: CohortEvaluationInput,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_CRC,
            ROLE_SYSADMIN,
            ROLE_DATA_MANAGER,
            "sponsor_admin",
            "investigator",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CohortEvaluationResult:
    """Evaluates safety cohort DLTs and computes real-time recommendation flags (BOIN, CRM, 3+3)."""
    verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason")

    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )

    try:
        result = await evaluate_cohort_outcomes_transaction(
            session=session,
            payload=payload,
            user_id=principal.user_id,
            change_reason=change_reason,
        )
        await session.commit()
        return result
    except AdaptiveTrialConfigNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except (InvalidDoseLevelError, CohortTransitionError, ValueError) as e:
        raise HTTPException(status_code=400, detail=str(e))


@router.post("/adaptive/cohorts/transition", response_model=CohortTransitionLogResponse)
async def transition_adaptive_cohort_endpoint(
    payload: CohortTransitionRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_SYSADMIN,
            "sponsor_admin",
            "investigator",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CohortTransitionLogResponse:
    """Advances or overrides a dose escalation cohort with 21 CFR Part 11 audit logging."""
    verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason")

    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )

    try:
        log_res = await advance_cohort_decision_transaction(
            session=session,
            payload=payload,
            user_id=principal.user_id,
            change_reason=change_reason,
        )
        await session.commit()
        return log_res
    except AdaptiveTrialConfigNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except (CohortTransitionError, InvalidDoseLevelError, ValueError) as e:
        raise HTTPException(status_code=400, detail=str(e))


@router.get(
    "/adaptive/cohorts/history", response_model=list[CohortTransitionLogResponse]
)
async def get_adaptive_cohorts_history_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[CohortTransitionLogResponse]:
    """Returns the immutable 21 CFR Part 11 audited transition log history for a study."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {study_id}.",
        )

    state = await get_adaptive_study_state_transaction(
        session=session, study_id=study_id
    )
    logs = state.get("transition_logs", [])

    return [
        CohortTransitionLogResponse(
            id=log.id,
            study_id=log.study_id,
            cohort_number=log.cohort_number,
            from_dose_level=log.from_dose_level,
            to_dose_level=log.to_dose_level,
            action_taken=log.action_taken,
            algorithm_recommendation=log.algorithm_recommendation,
            override_occurred=log.override_occurred,
            override_reason=log.override_reason,
            evaluated_dlt_rate=log.evaluated_dlt_rate,
            posterior_toxicity_estimates=log.posterior_toxicity_estimates,
            authorized_by=log.authorized_by,
            transitioned_at=log.transitioned_at,
            signature_token=log.signature_token,
            cryptographic_checksum=log.cryptographic_checksum,
        )
        for log in logs
    ]


@router.post("/adaptive/rar/evaluate", response_model=RAREvaluationResult)
async def evaluate_rar_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    request: Request = None,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_INVESTIGATOR,
            ROLE_SITE_INVESTIGATOR,
            ROLE_SYSADMIN,
            ROLE_DATA_MANAGER,
            "sponsor_admin",
            "investigator",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> RAREvaluationResult:
    """Calculates dynamic Response-Adaptive Randomization allocation weights across active arms."""
    if request:
        verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason") if request else None

    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {study_id}.",
        )

    try:
        rar_res = await evaluate_and_update_rar_transaction(
            session=session,
            study_id=study_id,
            user_id=principal.user_id,
            change_reason=change_reason,
        )
        await session.commit()
        return rar_res
    except (AdaptiveTrialConfigNotFoundError, RARArmNotFoundError) as e:
        raise HTTPException(status_code=404, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))


@router.post("/adaptive/rar/arms", response_model=RAREvaluationResult)
async def expand_or_terminate_rar_arm_endpoint(
    payload: RARExpandOrTerminateArmRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_SYSADMIN,
            ROLE_INVESTIGATOR,
            "sponsor_admin",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> RAREvaluationResult:
    """Seamlessly adds or terminates a treatment arm in RAR with zero study downtime."""
    verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason")

    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {payload.study_id}.",
        )

    try:
        rar_res = await add_or_terminate_rar_arm_transaction(
            session=session,
            payload=payload,
            user_id=principal.user_id,
            change_reason=change_reason,
        )
        await session.commit()
        return rar_res
    except (AdaptiveTrialConfigNotFoundError, RARArmNotFoundError) as e:
        raise HTTPException(status_code=404, detail=str(e))
    except (CohortTransitionError, ValueError) as e:
        raise HTTPException(status_code=400, detail=str(e))


@router.get("/adaptive/rar/state", response_model=dict[str, Any])
async def get_rar_state_endpoint(
    study_id: str = Query(..., description="Clinical study identifier"),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_execution_db_session),
) -> dict[str, Any]:
    """Retrieves current Response-Adaptive Randomization arm allocation weights and posterior states."""
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied to study {study_id}.",
        )

    state = await get_adaptive_study_state_transaction(
        session=session, study_id=study_id
    )
    rar_arms = state.get("rar_arms", [])
    if not rar_arms:
        raise HTTPException(
            status_code=404, detail=f"No RAR arms found for study {study_id}."
        )

    return {
        "study_id": study_id,
        "arms": [
            {
                "arm_id": a.arm_id,
                "arm_name": a.arm_name,
                "is_control": a.is_control,
                "status": a.status,
                "enrolled_count": a.enrolled_count,
                "response_count": a.response_count,
                "posterior_mean": a.posterior_mean,
                "allocation_probability": a.allocation_probability,
                "superiority_vs_control_prob": a.superiority_vs_control_prob,
            }
            for a in rar_arms
        ],
        "active_probabilities": {
            a.arm_id: a.allocation_probability for a in rar_arms if a.status == "ACTIVE"
        },
    }


# =========================================================================
# Pure Statistical Calculation / Protocol Designer Endpoints
# =========================================================================


@router.post(
    "/adaptive/boin/calculate-boundaries",
    response_model=BoinBoundariesCalculationResponse,
)
def calculate_boin_boundaries_endpoint(
    payload: BoinBoundariesCalculationRequest,
) -> BoinBoundariesCalculationResponse:
    """Calculates theoretical BOIN escalation and de-escalation toxicity boundaries (lambda_e, lambda_d)."""
    l_e, l_d, p1, p2 = calculate_boin_boundaries(
        payload.target_toxicity_rate,
        payload.factor_e,
        payload.factor_d,
    )
    return BoinBoundariesCalculationResponse(
        target_toxicity_rate=payload.target_toxicity_rate,
        phi_1=p1,
        phi_2=p2,
        lambda_e=l_e,
        lambda_d=l_d,
        description=(
            f"BOIN design boundaries for target DLT rate {payload.target_toxicity_rate}: "
            f"Escalate if p_hat <= {l_e}; De-escalate if p_hat >= {l_d}; Stay if {l_e} < p_hat < {l_d}."
        ),
    )


@router.post(
    "/adaptive/crm/posterior",
    response_model=CrmPosteriorCalculationResponse,
)
def calculate_crm_posterior_endpoint(
    payload: CrmPosteriorCalculationRequest,
) -> CrmPosteriorCalculationResponse:
    """Calculates Bayesian posterior probabilities and recommended next dose under Continual Reassessment Method."""
    crm_cfg = CrmConfig(
        skeleton_probs=payload.skeleton_probs,
        target_toxicity_rate=payload.target_toxicity_rate,
        prior_std=payload.prior_std,
    )
    engine = CrmDoseEscalationEngine(crm_cfg)

    dose_stats_dict = {
        d["dose_index"]: {"enrolled": d.get("enrolled", 0), "dlts": d.get("dlts", 0)}
        for d in payload.dose_evaluations
    }

    action, rec_dose, post_means, od_1, rat, safety_stop = engine.evaluate_cohort(
        current_dose_index=payload.current_dose_index,
        dose_stats=dose_stats_dict,
    )

    return CrmPosteriorCalculationResponse(
        target_toxicity_rate=payload.target_toxicity_rate,
        current_dose_index=payload.current_dose_index,
        recommended_dose_index=rec_dose or payload.current_dose_index,
        recommendation_action=action,
        posterior_mean_probabilities=post_means,
        overdose_probability_dose_1=od_1,
        is_safety_stop_triggered=safety_stop,
        rationale=rat,
    )


# ==========================================
# RTSM Supply Chain & Inventory Management API
# ==========================================


class DispenseRequest(BaseModel):
    study_id: str
    site_id: str
    subject_id: str
    visit_id: str
    kit_id: str
    quantity: int = Field(default=1, ge=1)


class DispenseResponse(BaseModel):
    status: str
    message: str
    resupply_triggered: bool


class ResupplyEventResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    kit_id: str
    requested_qty: int
    status: str
    triggered_at: Any


class ResupplyEventApprovalRequest(BaseModel):
    change_justification: str


@router.post(
    "/dispense",
    response_model=DispenseResponse,
    status_code=201,
)
async def dispense_kit_endpoint(
    request: Request,
    payload: DispenseRequest,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRC,
            ROLE_INVESTIGATOR,
            ROLE_CRA,
            detail="Forbidden: User role is not authorized for RTSM supply dispensation.",
        )
    ),
) -> DispenseResponse:
    """End-point to dispense investigational product (IP) kits against site inventory."""
    if TrialLockManager.is_site_locked(payload.site_id):
        raise HTTPException(
            status_code=423,
            detail=f"Site {payload.site_id} is currently locked in a read-only state.",
        )

    async with db_manager.get_session_maker()() as session:
        try:
            resupply_triggered = await dispense_kit_transaction(
                session=session,
                study_id=payload.study_id,
                site_id=payload.site_id,
                subject_id=payload.subject_id,
                visit_id=payload.visit_id,
                kit_id=payload.kit_id,
                quantity=payload.quantity,
            )
            await session.commit()
        except (QuarantinedKitError, ExpiredKitError) as e:
            raise HTTPException(status_code=422, detail=str(e))
        except SiteInventoryNotFoundError as e:
            raise HTTPException(status_code=404, detail=str(e))
        except InsufficientStockError as e:
            raise HTTPException(status_code=400, detail=str(e))
        except PermissionError as e:
            raise HTTPException(status_code=423, detail=str(e))
        except Exception as e:
            await session.rollback()
            raise HTTPException(
                status_code=500, detail=f"Internal database error: {str(e)}"
            )

    if resupply_triggered:

        def dispatch_resupply_notification(
            site_id: str, kit_id: str, requested_qty: int
        ):
            from apps.execution.trial_lock import NotificationRouter

            router = NotificationRouter()
            payload_notif = {
                "message": f"Resupply triggered for site {site_id}, kit {kit_id}. Requested quantity: {requested_qty}",
                "site_id": site_id,
                "kit_id": kit_id,
                "requested_qty": requested_qty,
                "related_entity_type": "site-inventory",
                "related_entity_id": f"{site_id}:{kit_id}",
            }
            router.send_dashboard_notification(["supply_manager"], payload_notif)

        background_tasks.add_task(
            dispatch_resupply_notification,
            payload.site_id,
            payload.kit_id,
            20,
        )

    return DispenseResponse(
        status="success",
        message=f"Successfully dispensed {payload.quantity} of kit {payload.kit_id} to subject {payload.subject_id}.",
        resupply_triggered=resupply_triggered,
    )


@router.get(
    "/resupply-events",
    response_model=list[ResupplyEventResponse],
)
async def list_resupply_events_endpoint(
    study_id: str | None = None,
    site_id: str | None = None,
    status: str | None = None,
    principal: Principal = Depends(get_principal),
) -> list[ResupplyEventResponse]:
    """List resupply events filtered by role and site permissions."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(ResupplyEvent).where(ResupplyEvent.is_deleted.is_(False))
        if study_id:
            stmt = stmt.where(ResupplyEvent.study_id == study_id)
        if site_id:
            stmt = stmt.where(ResupplyEvent.site_id == site_id)
        if status:
            stmt = stmt.where(ResupplyEvent.status == status)

        stmt = stmt.order_by(ResupplyEvent.triggered_at.desc())
        result = await session.execute(stmt)
        events = result.scalars().all()

        filtered = []
        for ev in events:
            if can_access_site(principal, ev.site_id) and can_access_study(
                principal, ev.study_id
            ):
                filtered.append(ev)

        return [
            ResupplyEventResponse(
                id=ev.id,
                study_id=ev.study_id,
                site_id=ev.site_id,
                kit_id=ev.kit_id,
                requested_qty=ev.requested_qty,
                status=ev.status,
                triggered_at=ev.triggered_at,
            )
            for ev in filtered
        ]


@router.post(
    "/resupply-events/{event_id}/confirm",
    response_model=ResupplyEventResponse,
)
async def approve_resupply_event_endpoint(
    event_id: str,
    request: Request,
    payload: ResupplyEventApprovalRequest,
    principal: Principal = Depends(get_principal),
) -> ResupplyEventResponse:
    """Approve a resupply event to trigger shipment generation."""
    verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason")

    if not payload.change_justification or not payload.change_justification.strip():
        raise HTTPException(
            status_code=400, detail="Change justification must not be empty."
        )

    async with db_manager.get_session_maker()() as session:
        stmt = select(ResupplyEvent).where(
            ResupplyEvent.id == event_id, ResupplyEvent.is_deleted.is_(False)
        )
        result = await session.execute(stmt)
        event = result.scalars().first()
        if not event:
            raise HTTPException(status_code=404, detail="Resupply event not found")

        if not (
            can_access_site(principal, event.site_id)
            and can_access_study(principal, event.study_id)
        ):
            raise HTTPException(
                status_code=403, detail="Forbidden: Access denied to site or study."
            )

        def is_manager(p: Principal) -> bool:
            manager_roles = {"sponsor_dm", "admin", "sysadmin", "cra", "monitor"}
            if any(r in manager_roles for r in p.roles):
                return True
            raw_manager_roles = {
                "cra",
                "monitor",
                "clinical_research_associate",
                "clinicalresearchassociate",
                "sponsor_admin",
                "sponsoradmin",
                "admin",
                "sysadmin",
                "system_admin",
                "systemadmin",
            }
            for r in p.raw_roles:
                norm_r = r.strip().lower().replace(" ", "_")
                if norm_r in raw_manager_roles:
                    return True
            return False

        if not is_manager(principal):
            raise HTTPException(
                status_code=403,
                detail="Forbidden: User does not have manager-level permissions.",
            )

        if TrialLockManager.is_site_locked(event.site_id):
            raise HTTPException(
                status_code=423,
                detail=f"Site {event.site_id} is currently locked in a read-only state.",
            )

        event.status = "APPROVED"
        event.version += 1
        current_user_id.set(principal.user_id)
        current_change_reason.set(change_reason)

        session.add(event)
        await session.commit()

        stmt = select(ResupplyEvent).where(ResupplyEvent.id == event_id)
        result = await session.execute(stmt)
        event = result.scalars().first()

        return ResupplyEventResponse(
            id=event.id,
            study_id=event.study_id,
            site_id=event.site_id,
            kit_id=event.kit_id,
            requested_qty=event.requested_qty,
            status=event.status,
            triggered_at=event.triggered_at,
        )


@router.post(
    "/resupply-events/{event_id}/reject",
    response_model=ResupplyEventResponse,
)
async def reject_resupply_event_endpoint(
    event_id: str,
    request: Request,
    payload: ResupplyEventApprovalRequest,
    principal: Principal = Depends(get_principal),
) -> ResupplyEventResponse:
    """Reject a resupply event."""
    verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason")

    if not payload.change_justification or not payload.change_justification.strip():
        raise HTTPException(
            status_code=400, detail="Change justification must not be empty."
        )

    async with db_manager.get_session_maker()() as session:
        stmt = select(ResupplyEvent).where(
            ResupplyEvent.id == event_id, ResupplyEvent.is_deleted.is_(False)
        )
        result = await session.execute(stmt)
        event = result.scalars().first()
        if not event:
            raise HTTPException(status_code=404, detail="Resupply event not found")

        if not (
            can_access_site(principal, event.site_id)
            and can_access_study(principal, event.study_id)
        ):
            raise HTTPException(
                status_code=403, detail="Forbidden: Access denied to site or study."
            )

        def is_manager(p: Principal) -> bool:
            manager_roles = {"sponsor_dm", "admin", "sysadmin", "cra", "monitor"}
            if any(r in manager_roles for r in p.roles):
                return True
            raw_manager_roles = {
                "cra",
                "monitor",
                "clinical_research_associate",
                "clinicalresearchassociate",
                "sponsor_admin",
                "sponsoradmin",
                "admin",
                "sysadmin",
                "system_admin",
                "systemadmin",
            }
            for r in p.raw_roles:
                norm_r = r.strip().lower().replace(" ", "_")
                if norm_r in raw_manager_roles:
                    return True
            return False

        if not is_manager(principal):
            raise HTTPException(
                status_code=403,
                detail="Forbidden: User does not have manager-level permissions.",
            )

        if TrialLockManager.is_site_locked(event.site_id):
            raise HTTPException(
                status_code=423,
                detail=f"Site {event.site_id} is currently locked in a read-only state.",
            )

        event.status = "REJECTED"
        event.version += 1
        current_user_id.set(principal.user_id)
        current_change_reason.set(change_reason)

        session.add(event)
        await session.commit()

        stmt = select(ResupplyEvent).where(ResupplyEvent.id == event_id)
        result = await session.execute(stmt)
        event = result.scalars().first()

        return ResupplyEventResponse(
            id=event.id,
            study_id=event.study_id,
            site_id=event.site_id,
            kit_id=event.kit_id,
            requested_qty=event.requested_qty,
            status=event.status,
            triggered_at=event.triggered_at,
        )
