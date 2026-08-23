"""RTSM Supply Chain, Multi-Echelon Hierarchy & Predictive Resupply Forecasting.

Requirements: PRD-SCM-001, GMP Annex 13
"""

import math
import uuid
from datetime import UTC, datetime, timedelta

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.context import current_change_reason, current_user_id
from apps.execution.database.models import (
    ClinicalSubject,
    IntegrationOutbox,
    IPKit,
    KitDispensation,
    PackingManifest,
    PredictiveResupplyForecast,
    ResupplyEvent,
    SiteInventory,
    SupplyNode,
    SupplyNodeInventory,
    TransferOrder,
)
from apps.execution.domain.rtsm_models import (
    DailyDemandProjection,
    MultiEchelonRollup,
    PredictiveForecastInput,
    PredictiveForecastResult,
    StockoutRiskLevel,
    SupplyHierarchyNode,
    SupplyNodeType,
)
from apps.execution.trial_lock import TrialLockManager


class SiteInventoryNotFoundError(Exception):
    """Raised when no inventory record exists for the given site and kit combination."""

    pass


class InsufficientStockError(Exception):
    """Raised when the requested dispensation quantity exceeds on-hand site inventory."""

    pass


class QuarantinedKitError(Exception):
    """Raised when attempting to allocate or dispense a quarantined investigational product kit."""

    pass


class ExpiredKitError(Exception):
    """Raised when attempting to allocate or dispense an expired investigational product kit."""

    pass


class SupplyNodeNotFoundError(Exception):
    """Raised when a requested supply hierarchy node cannot be found."""

    pass


class InvalidHierarchyError(Exception):
    """Raised when circular references or invalid echelon parent relationships exist."""

    pass


class TransferOrderNotFoundError(Exception):
    """Raised when a requested transfer order does not exist."""

    pass


class InvalidTransferOrderStateError(Exception):
    """Raised when attempting an invalid status transition on a transfer order."""

    pass


class StorageCapacityExceededError(Exception):
    """Raised when a transfer order or adjustment exceeds destination storage capacity."""

    pass


def evaluate_resupply(on_hand_qty: int, reorder_threshold: int) -> bool:
    """Pure-Python stateless check for inventory threshold breach.

    Returns True when on_hand_qty is at or below the reorder threshold.
    Operates strictly on blinded quantities and does not reference treatment arms.
    """
    return on_hand_qty <= reorder_threshold


# =========================================================================
# Multi-Echelon Supply Hierarchy Pure Logic
# =========================================================================


def build_supply_hierarchy_tree(
    nodes: list[SupplyNode],
    inventories: list[SupplyNodeInventory] | None = None,
) -> MultiEchelonRollup:
    """Constructs a validated multi-echelon supply hierarchy tree and aggregated metrics.

    Tiers: PRIMARY_MANUFACTURER -> CENTRAL_DEPOT -> REGIONAL_DEPOT -> CLINICAL_SITE.
    Validates against cycles and calculates stock rollups per echelon and globally.
    """
    inv_map: dict[str, dict[str, int]] = {}
    if inventories:
        for inv in inventories:
            if inv.node_id not in inv_map:
                inv_map[inv.node_id] = {
                    "on_hand": 0,
                    "reserved": 0,
                    "in_transit": 0,
                }
            inv_map[inv.node_id]["on_hand"] += inv.on_hand_qty
            inv_map[inv.node_id]["reserved"] += inv.reserved_qty
            inv_map[inv.node_id]["in_transit"] += inv.in_transit_qty

    node_dict: dict[str, SupplyHierarchyNode] = {}
    for n in nodes:
        stock = inv_map.get(n.node_id, {"on_hand": 0, "reserved": 0, "in_transit": 0})
        curr_util = n.current_utilization or 0
        cap = n.storage_capacity or 1000
        utilization_pct = round((curr_util / cap) * 100.0, 2) if cap > 0 else 0.0
        node_type_enum = (
            SupplyNodeType(n.node_type)
            if n.node_type in SupplyNodeType.__members__
            else SupplyNodeType.CLINICAL_SITE
        )
        node_dict[n.node_id] = SupplyHierarchyNode(
            node_id=n.node_id,
            study_id=n.study_id,
            node_type=node_type_enum,
            name=n.name,
            country=n.country,
            address=n.address,
            parent_node_id=n.parent_node_id,
            storage_capacity=cap,
            current_utilization=curr_util,
            utilization_percentage=utilization_pct,
            lead_time_days=n.lead_time_days or 3,
            temperature_conditions=n.temperature_conditions or [],
            is_active=True if n.is_active is None else n.is_active,
            total_on_hand=stock["on_hand"],
            total_reserved=stock["reserved"],
            total_in_transit=stock["in_transit"],
            children=[],
        )

    # Check for cycles and build parent-child links
    root_nodes: list[SupplyHierarchyNode] = []
    study_id = nodes[0].study_id if nodes else "UNKNOWN"

    for n_id, h_node in node_dict.items():
        if h_node.parent_node_id and h_node.parent_node_id in node_dict:
            # Check cycle: ensure h_node is not an ancestor of parent
            parent = node_dict[h_node.parent_node_id]
            curr = parent
            visited = {n_id}
            while curr.parent_node_id and curr.parent_node_id in node_dict:
                if curr.parent_node_id in visited:
                    raise InvalidHierarchyError(
                        f"Circular dependency detected between node {n_id} and {curr.parent_node_id}."
                    )
                visited.add(curr.node_id)
                curr = node_dict[curr.parent_node_id]

            parent.children.append(h_node)
        else:
            root_nodes.append(h_node)

    # Rollup counts
    mfg_count = sum(
        1 for n in nodes if n.node_type == SupplyNodeType.PRIMARY_MANUFACTURER
    )
    central_count = sum(1 for n in nodes if n.node_type == SupplyNodeType.CENTRAL_DEPOT)
    regional_count = sum(
        1 for n in nodes if n.node_type == SupplyNodeType.REGIONAL_DEPOT
    )
    site_count = sum(1 for n in nodes if n.node_type == SupplyNodeType.CLINICAL_SITE)

    total_on_hand = sum(s["on_hand"] for s in inv_map.values())
    total_in_transit = sum(s["in_transit"] for s in inv_map.values())
    total_reserved = sum(s["reserved"] for s in inv_map.values())
    total_capacity = sum(n.storage_capacity for n in nodes)
    overall_util_pct = (
        round((sum(n.current_utilization for n in nodes) / total_capacity) * 100.0, 2)
        if total_capacity > 0
        else 0.0
    )

    return MultiEchelonRollup(
        study_id=study_id,
        total_nodes_count=len(nodes),
        manufacturers_count=mfg_count,
        central_depots_count=central_count,
        regional_depots_count=regional_count,
        clinical_sites_count=site_count,
        total_global_on_hand_stock=total_on_hand,
        total_global_in_transit_stock=total_in_transit,
        total_global_reserved_stock=total_reserved,
        total_global_storage_capacity=total_capacity,
        overall_capacity_utilization_pct=overall_util_pct,
        hierarchy_tree=root_nodes,
    )


# =========================================================================
# Predictive Resupply Forecasting Pure Logic
# =========================================================================


def calculate_predictive_forecast(
    input_params: PredictiveForecastInput,
    active_subjects: list[dict] | None = None,
    current_on_hand_qty: int = 0,
    current_reserved_qty: int = 0,
    current_in_transit_qty: int = 0,
    start_date: datetime | None = None,
) -> PredictiveForecastResult:
    """Pure-Python deterministic simulation of clinical kit burn and predictive resupply requirements.

    Simulates day-by-day scheduled visit kit consumptions, screening conversion transitions,
    and rolling enrollment velocity across the forecast horizon.
    """
    now = start_date or datetime.now(UTC)
    horizon = input_params.horizon_days
    active_subjs = active_subjects or []

    # Standard clinical visit schedule intervals (Days: 1, 14, 28, 56, 84, 112, 140, 168, 196, 224, 252, 280, 308, 336)
    standard_visit_intervals = [
        1,
        14,
        28,
        56,
        84,
        112,
        140,
        168,
        196,
        224,
        252,
        280,
        308,
        336,
        364,
    ]

    daily_scheduled_demand = [0] * (horizon + 1)
    daily_new_subject_demand = [0] * (horizon + 1)

    # 1. Map existing active subjects onto scheduled future visit days
    for s in active_subjs:
        days_enrolled = s.get("days_enrolled", 0)
        for visit_day in standard_visit_intervals:
            future_day = visit_day - days_enrolled
            if 1 <= future_day <= horizon:
                daily_scheduled_demand[future_day] += 1

    # 2. Simulate screening pipeline conversions
    converted_pipeline_count = int(
        math.floor(
            input_params.screening_pipeline_count
            * input_params.screening_conversion_rate
        )
    )
    # Stagger screening conversions across days 1 to 14
    for idx in range(converted_pipeline_count):
        conversion_day = (idx % 14) + 1
        if conversion_day <= horizon:
            daily_new_subject_demand[conversion_day] += 1
            for v_offset in [14, 28, 56, 84, 112, 140]:
                subsequent_day = conversion_day + v_offset
                if subsequent_day <= horizon:
                    daily_new_subject_demand[subsequent_day] += 1

    # 3. Simulate ongoing enrollment velocity
    daily_enrollment_rate = input_params.enrollment_velocity_weekly / 7.0
    accumulated_fraction = 0.0
    for day in range(1, horizon + 1):
        accumulated_fraction += daily_enrollment_rate
        if accumulated_fraction >= 1.0:
            new_subjs = int(math.floor(accumulated_fraction))
            accumulated_fraction -= new_subjs
            daily_new_subject_demand[day] += new_subjs
            for v_offset in [14, 28, 56, 84, 112, 140]:
                subsequent_day = day + v_offset
                if subsequent_day <= horizon:
                    daily_new_subject_demand[subsequent_day] += new_subjs

    # 4. Build timeline and compute projected inventory balance trajectory
    effective_available = (
        current_on_hand_qty + current_in_transit_qty - current_reserved_qty
    )
    cumulative_demand = 0
    timeline: list[DailyDemandProjection] = []
    runout_day: int | None = None
    reorder_trigger_day: int | None = None

    lead_plus_buffer = input_params.lead_time_days + input_params.buffer_days

    for day in range(1, horizon + 1):
        cal_date = (now + timedelta(days=day)).strftime("%Y-%m-%d")
        sched = daily_scheduled_demand[day]
        new_sub = daily_new_subject_demand[day]
        total_day_demand = sched + new_sub
        cumulative_demand += total_day_demand
        closing_bal = effective_available - cumulative_demand

        if closing_bal <= 0 and runout_day is None:
            runout_day = day

        # Check if balance breaches safety stock within lead time horizon
        if closing_bal <= input_params.safety_stock_threshold:
            if reorder_trigger_day is None:
                reorder_trigger_day = day

        timeline.append(
            DailyDemandProjection(
                day_index=day,
                date=cal_date,
                scheduled_dispensations=sched,
                projected_new_subject_dispensations=new_sub,
                total_daily_demand=total_day_demand,
                cumulative_demand=cumulative_demand,
                projected_closing_balance=closing_bal,
            )
        )

    # 5. Aggregate calculations
    total_projected_demand = cumulative_demand
    average_daily_burn_rate = (
        round(total_projected_demand / horizon, 4) if horizon > 0 else 0.0
    )

    if average_daily_burn_rate > 0:
        days_of_supply = round(effective_available / average_daily_burn_rate, 2)
    else:
        days_of_supply = 999.0

    projected_runout_date = None
    if runout_day is not None:
        projected_runout_date = (now + timedelta(days=runout_day)).strftime("%Y-%m-%d")

    # 6. Risk Level Determination
    if days_of_supply <= input_params.lead_time_days:
        risk_level = StockoutRiskLevel.CRITICAL
    elif days_of_supply <= lead_plus_buffer:
        risk_level = StockoutRiskLevel.HIGH
    elif days_of_supply <= 2 * lead_plus_buffer:
        risk_level = StockoutRiskLevel.MEDIUM
    else:
        risk_level = StockoutRiskLevel.LOW

    # 7. Recommended Order Quantity
    lead_demand = sum(
        t.total_daily_demand for t in timeline[: min(lead_plus_buffer, len(timeline))]
    )
    target_stock = lead_demand + input_params.safety_stock_threshold
    unbounded_order = max(0, target_stock - effective_available)

    # Bound by storage capacity
    max_allowable_order = max(0, input_params.storage_capacity - current_on_hand_qty)
    recommended_order = min(unbounded_order, max_allowable_order)

    reorder_recommended = recommended_order > 0 or risk_level in (
        StockoutRiskLevel.CRITICAL,
        StockoutRiskLevel.HIGH,
    )

    return PredictiveForecastResult(
        study_id=input_params.study_id,
        site_id=input_params.site_id,
        kit_type=input_params.kit_type,
        forecast_generated_at=now,
        horizon_days=horizon,
        current_on_hand_qty=current_on_hand_qty,
        current_reserved_qty=current_reserved_qty,
        current_in_transit_qty=current_in_transit_qty,
        effective_available_stock=effective_available,
        active_subjects_count=len(active_subjs),
        screening_pipeline_count=input_params.screening_pipeline_count,
        enrollment_velocity_weekly=input_params.enrollment_velocity_weekly,
        total_projected_demand=total_projected_demand,
        average_daily_burn_rate=average_daily_burn_rate,
        days_of_supply_remaining=days_of_supply,
        projected_runout_date=projected_runout_date,
        stockout_risk_level=risk_level,
        recommended_order_qty=recommended_order,
        reorder_recommended=reorder_recommended,
        reorder_trigger_day=reorder_trigger_day,
        timeline=timeline,
    )


# =========================================================================
# Transactional Services
# =========================================================================


async def dispense_kit_transaction(
    session: AsyncSession,
    study_id: str,
    site_id: str,
    subject_id: str,
    visit_id: str,
    kit_id: str,
    quantity: int,
) -> bool:
    """Performs transactional kit dispensation and site inventory updates.

    1. Acquires a pessimistic row lock on the SiteInventory row via select(...).with_for_update().
    2. Validates stock levels, raising InsufficientStockError if quantity exceeds on-hand quantity.
    3. Decrements on-hand inventory and creates a KitDispensation record in the session.
    4. Evaluates resupply conditions via evaluate_resupply.
    5. If resupply is signaled and no PENDING resupply event exists for the (study, site, kit),
       creates a ResupplyEvent with status "PENDING" and flags the inventory.
    6. Returns a boolean indicating whether a new ResupplyEvent was created.
    """
    stmt = (
        select(SiteInventory)
        .where(
            SiteInventory.site_id == site_id,
            SiteInventory.kit_id == kit_id,
            SiteInventory.is_deleted.is_(False),
        )
        .with_for_update()
    )
    result = await session.execute(stmt)
    inventory = result.scalars().first()

    if not inventory:
        raise SiteInventoryNotFoundError(
            f"No inventory record found for site {site_id} and kit {kit_id}."
        )

    # Validate IPKit status gating (quarantine and expiry locks)
    stmt_kit = select(IPKit).where(
        IPKit.study_id == study_id,
        (IPKit.kit_number == kit_id) | (IPKit.id == kit_id),
        IPKit.is_deleted.is_(False),
    )
    res_kit = await session.execute(stmt_kit)
    kit_obj = res_kit.scalars().first()
    if kit_obj:
        if kit_obj.status == "QUARANTINED":
            raise QuarantinedKitError(
                f"Kit {kit_obj.kit_number} is QUARANTINED due to temperature excursion breach and cannot be dispensed."
            )
        exp_date = kit_obj.expiration_date
        if exp_date is not None and exp_date.tzinfo is None:
            exp_date = exp_date.replace(tzinfo=UTC)
        if kit_obj.status == "EXPIRED" or (
            exp_date is not None and exp_date < datetime.now(UTC)
        ):
            raise ExpiredKitError(
                f"Kit {kit_obj.kit_number} is EXPIRED and cannot be dispensed."
            )

    if inventory.on_hand_qty < quantity:
        raise InsufficientStockError(
            f"Insufficient stock for kit {kit_id} at site {site_id}: "
            f"requested {quantity}, on-hand {inventory.on_hand_qty}."
        )

    dispensation = KitDispensation(
        study_id=study_id,
        subject_id=subject_id,
        kit_id=kit_id,
        site_id=site_id,
        visit_id=visit_id,
        quantity=quantity,
    )
    session.add(dispensation)

    inventory.on_hand_qty -= quantity

    new_resupply_created = False
    if evaluate_resupply(inventory.on_hand_qty, inventory.reorder_threshold):
        inventory.resupply_signal = True

        stmt_pending = select(ResupplyEvent).where(
            ResupplyEvent.study_id == study_id,
            ResupplyEvent.site_id == site_id,
            ResupplyEvent.kit_id == kit_id,
            ResupplyEvent.status == "PENDING",
            ResupplyEvent.is_deleted.is_(False),
        )
        res_pending = await session.execute(stmt_pending)
        existing_event = res_pending.scalars().first()

        if not existing_event:
            requested_qty = 20
            new_event = ResupplyEvent(
                study_id=study_id,
                site_id=site_id,
                kit_id=kit_id,
                requested_qty=requested_qty,
                status="PENDING",
            )
            session.add(new_event)
            new_resupply_created = True

    return new_resupply_created


async def run_predictive_forecast_transaction(
    session: AsyncSession,
    input_params: PredictiveForecastInput,
) -> PredictiveForecastResult:
    """Executes a predictive demand forecast for a site, querying DB state and persisting the forecast."""
    # 1. Fetch site node and inventory
    stmt_node = select(SupplyNode).where(
        SupplyNode.study_id == input_params.study_id,
        SupplyNode.node_id == input_params.site_id,
        SupplyNode.is_deleted.is_(False),
    )
    res_node = await session.execute(stmt_node)
    site_node = res_node.scalars().first()

    storage_cap = (
        site_node.storage_capacity if site_node else input_params.storage_capacity
    )
    lead_time = site_node.lead_time_days if site_node else input_params.lead_time_days

    # 2. Fetch inventory (SupplyNodeInventory or fallback to SiteInventory)
    stmt_node_inv = select(SupplyNodeInventory).where(
        SupplyNodeInventory.study_id == input_params.study_id,
        SupplyNodeInventory.node_id == input_params.site_id,
        SupplyNodeInventory.kit_type == input_params.kit_type,
        SupplyNodeInventory.is_deleted.is_(False),
    )
    res_node_inv = await session.execute(stmt_node_inv)
    node_inv = res_node_inv.scalars().first()

    on_hand = 0
    reserved = 0
    in_transit = 0
    safety_stock = input_params.safety_stock_threshold

    if node_inv:
        on_hand = node_inv.on_hand_qty
        reserved = node_inv.reserved_qty
        in_transit = node_inv.in_transit_qty
        safety_stock = node_inv.safety_stock_threshold
    else:
        # Fallback to SiteInventory
        stmt_site_inv = select(SiteInventory).where(
            SiteInventory.study_id == input_params.study_id,
            SiteInventory.site_id == input_params.site_id,
            SiteInventory.is_deleted.is_(False),
        )
        res_site_inv = await session.execute(stmt_site_inv)
        site_inv = res_site_inv.scalars().first()
        if site_inv:
            on_hand = site_inv.on_hand_qty
            safety_stock = site_inv.reorder_threshold

    # 3. Query active subjects at the site
    stmt_subjs = select(ClinicalSubject).where(
        ClinicalSubject.study_id == input_params.study_id,
        ClinicalSubject.site_id == input_params.site_id,
        ClinicalSubject.status.in_(["RANDOMIZED", "ACTIVE", "ENROLLED"]),
        ClinicalSubject.is_deleted.is_(False),
    )
    res_subjs = await session.execute(stmt_subjs)
    active_subjects = res_subjs.scalars().all()

    active_subjs_list = []
    now = datetime.now(UTC)
    for sub in active_subjects:
        created = getattr(sub, "created_at", None) or now
        # make offset-aware comparison
        if created.tzinfo is None:
            created = created.replace(tzinfo=UTC)
        days = max(0, (now - created).days)
        active_subjs_list.append({"subject_id": sub.subject_id, "days_enrolled": days})

    # Query screening subjects count if not overridden in input
    stmt_screening = select(ClinicalSubject).where(
        ClinicalSubject.study_id == input_params.study_id,
        ClinicalSubject.site_id == input_params.site_id,
        ClinicalSubject.status == "SCREENING",
        ClinicalSubject.is_deleted.is_(False),
    )
    res_screening = await session.execute(stmt_screening)
    screening_subjects = res_screening.scalars().all()
    actual_screening_count = (
        len(screening_subjects)
        if input_params.screening_pipeline_count == 0
        else input_params.screening_pipeline_count
    )

    adjusted_input = input_params.model_copy(
        update={
            "storage_capacity": storage_cap,
            "lead_time_days": lead_time,
            "safety_stock_threshold": safety_stock,
            "screening_pipeline_count": actual_screening_count,
        }
    )

    # 4. Compute forecast
    result = calculate_predictive_forecast(
        input_params=adjusted_input,
        active_subjects=active_subjs_list,
        current_on_hand_qty=on_hand,
        current_reserved_qty=reserved,
        current_in_transit_qty=in_transit,
        start_date=now,
    )

    # 5. Persist forecast record
    persisted_forecast = PredictiveResupplyForecast(
        study_id=result.study_id,
        site_id=result.site_id,
        kit_type=result.kit_type,
        forecast_generated_at=result.forecast_generated_at,
        horizon_days=result.horizon_days,
        enrollment_velocity_weekly=result.enrollment_velocity_weekly,
        active_subjects_count=result.active_subjects_count,
        screening_pipeline_count=result.screening_pipeline_count,
        projected_demand_qty=result.total_projected_demand,
        current_on_hand_qty=result.current_on_hand_qty,
        in_transit_qty=result.current_in_transit_qty,
        projected_runout_date=datetime.strptime(
            result.projected_runout_date, "%Y-%m-%d"
        ).replace(tzinfo=UTC)
        if result.projected_runout_date
        else None,
        days_of_supply_remaining=result.days_of_supply_remaining,
        recommended_order_qty=result.recommended_order_qty,
        stockout_risk_level=result.stockout_risk_level.value,
        forecast_details={
            "average_daily_burn_rate": result.average_daily_burn_rate,
            "reorder_recommended": result.reorder_recommended,
            "reorder_trigger_day": result.reorder_trigger_day,
            "timeline": [t.model_dump() for t in result.timeline],
        },
    )
    session.add(persisted_forecast)

    # 6. Flag inventory resupply signal if high/critical risk
    if node_inv and result.stockout_risk_level in (
        StockoutRiskLevel.CRITICAL,
        StockoutRiskLevel.HIGH,
    ):
        node_inv.resupply_signal = True

    return result


async def create_transfer_order_transaction(
    session: AsyncSession,
    study_id: str,
    source_node_id: str,
    destination_node_id: str,
    kit_type: str,
    quantity: int,
    priority: str = "ROUTINE",
    temperature_condition: str = "CONTROLLED_ROOM_TEMP",
    carrier: str | None = None,
    requested_by: str | None = None,
    transfer_order_number: str | None = None,
) -> TransferOrder:
    """Creates a GxP stock transfer order, validates stock, and reserves source inventory."""
    if TrialLockManager.is_site_locked(destination_node_id):
        raise PermissionError(
            f"Destination site/node {destination_node_id} is locked in read-only mode."
        )

    # 1. Verify source node exists and has sufficient on-hand stock
    stmt_src_inv = (
        select(SupplyNodeInventory)
        .where(
            SupplyNodeInventory.study_id == study_id,
            SupplyNodeInventory.node_id == source_node_id,
            SupplyNodeInventory.kit_type == kit_type,
            SupplyNodeInventory.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_src_inv = await session.execute(stmt_src_inv)
    src_inv = res_src_inv.scalars().first()

    if src_inv:
        available_at_source = src_inv.on_hand_qty - src_inv.reserved_qty
        if available_at_source < quantity:
            raise InsufficientStockError(
                f"Source node {source_node_id} has insufficient unreserved stock ({available_at_source}) for requested quantity {quantity}."
            )
        # Reserve stock
        src_inv.reserved_qty += quantity

    # 2. Verify destination storage capacity
    stmt_dst = select(SupplyNode).where(
        SupplyNode.study_id == study_id,
        SupplyNode.node_id == destination_node_id,
        SupplyNode.is_deleted.is_(False),
    )
    res_dst = await session.execute(stmt_dst)
    dst_node = res_dst.scalars().first()
    if dst_node:
        if dst_node.current_utilization + quantity > dst_node.storage_capacity:
            raise StorageCapacityExceededError(
                f"Destination node {destination_node_id} storage capacity ({dst_node.storage_capacity}) exceeded by transfer quantity {quantity} (current utilization: {dst_node.current_utilization})."
            )

    order_num = transfer_order_number or f"TO-{uuid.uuid4().hex[:8].upper()}"

    order = TransferOrder(
        study_id=study_id,
        transfer_order_number=order_num,
        source_node_id=source_node_id,
        destination_node_id=destination_node_id,
        kit_type=kit_type,
        quantity=quantity,
        priority=priority,
        status="REQUESTED",
        temperature_condition=temperature_condition,
        carrier=carrier,
        requested_at=datetime.now(UTC),
        requested_by=requested_by,
    )
    session.add(order)
    return order


async def approve_transfer_order_transaction(
    session: AsyncSession,
    order_id: str,
    approved_by: str,
    change_reason: str,
) -> TransferOrder:
    """Approves a requested transfer order for warehouse picking and packing."""
    stmt = (
        select(TransferOrder)
        .where(TransferOrder.id == order_id, TransferOrder.is_deleted.is_(False))
        .with_for_update()
    )
    res = await session.execute(stmt)
    order = res.scalars().first()

    if not order:
        raise TransferOrderNotFoundError(f"Transfer order {order_id} not found.")

    if order.status not in ("REQUESTED", "DRAFT"):
        raise InvalidTransferOrderStateError(
            f"Cannot approve transfer order in status '{order.status}'."
        )

    order.status = "APPROVED"
    order.approved_at = datetime.now(UTC)
    order.approved_by = approved_by
    order.version += 1

    current_user_id.set(approved_by)
    current_change_reason.set(change_reason)

    return order


async def generate_packing_manifest_transaction(
    session: AsyncSession,
    transfer_order_id: str,
    manifest_number: str,
    serialized_kit_numbers: list[str],
    lot_number: str,
    packed_by: str,
    data_logger_id: str | None = None,
    tamper_seal_id: str | None = None,
    container_serial_number: str | None = None,
    expiration_date: datetime | None = None,
    verified_by: str | None = None,
) -> PackingManifest:
    """Generates a formal GxP packing manifest binding serialized blinded kits to an approved transfer order."""
    stmt_order = (
        select(TransferOrder)
        .where(
            TransferOrder.id == transfer_order_id,
            TransferOrder.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_order = await session.execute(stmt_order)
    order = res_order.scalars().first()

    if not order:
        raise TransferOrderNotFoundError(
            f"Transfer order {transfer_order_id} not found."
        )

    if order.status != "APPROVED":
        raise InvalidTransferOrderStateError(
            f"Packing manifest can only be generated for orders in 'APPROVED' status (current: {order.status})."
        )

    if len(serialized_kit_numbers) != order.quantity:
        raise ValueError(
            f"Kit count mismatch: manifest contains {len(serialized_kit_numbers)} kits, expected order quantity {order.quantity}."
        )

    manifest = PackingManifest(
        study_id=order.study_id,
        transfer_order_id=order.id,
        manifest_number=manifest_number,
        serialized_kit_numbers=serialized_kit_numbers,
        lot_number=lot_number,
        expiration_date=expiration_date,
        container_serial_number=container_serial_number,
        data_logger_id=data_logger_id,
        tamper_seal_id=tamper_seal_id,
        packed_by=packed_by,
        packed_at=datetime.now(UTC),
        verified_by=verified_by,
        verified_at=datetime.now(UTC) if verified_by else None,
    )
    session.add(manifest)

    order.status = "PACKING"
    order.version += 1

    return manifest


async def ship_transfer_order_transaction(
    session: AsyncSession,
    order_id: str,
    shipped_by: str,
    tracking_number: str | None = None,
    carrier: str | None = None,
    estimated_delivery_date: datetime | None = None,
    change_reason: str | None = None,
) -> TransferOrder:
    """Transitions a transfer order to SHIPPED, updates source & destination inventory, and emits outbox event."""
    stmt = (
        select(TransferOrder)
        .where(TransferOrder.id == order_id, TransferOrder.is_deleted.is_(False))
        .with_for_update()
    )
    res = await session.execute(stmt)
    order = res.scalars().first()

    if not order:
        raise TransferOrderNotFoundError(f"Transfer order {order_id} not found.")

    if order.status not in ("PACKING", "APPROVED"):
        raise InvalidTransferOrderStateError(
            f"Cannot ship order in status '{order.status}'."
        )

    # Check manifest exists
    stmt_manifest = select(PackingManifest).where(
        PackingManifest.transfer_order_id == order.id,
        PackingManifest.is_deleted.is_(False),
    )
    res_manifest = await session.execute(stmt_manifest)
    manifest = res_manifest.scalars().first()

    if not manifest:
        raise InvalidTransferOrderStateError(
            f"Cannot ship order {order.transfer_order_number} without a generated packing manifest."
        )

    order.status = "SHIPPED"
    order.shipped_at = datetime.now(UTC)
    order.shipped_by = shipped_by
    if tracking_number:
        order.tracking_number = tracking_number
    if carrier:
        order.carrier = carrier
    if estimated_delivery_date:
        order.estimated_delivery_date = estimated_delivery_date
    order.version += 1

    # Decrement source stock (both on_hand and reserved)
    stmt_src = (
        select(SupplyNodeInventory)
        .where(
            SupplyNodeInventory.study_id == order.study_id,
            SupplyNodeInventory.node_id == order.source_node_id,
            SupplyNodeInventory.kit_type == order.kit_type,
            SupplyNodeInventory.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_src = await session.execute(stmt_src)
    src_inv = res_src.scalars().first()
    if src_inv:
        src_inv.on_hand_qty = max(0, src_inv.on_hand_qty - order.quantity)
        src_inv.reserved_qty = max(0, src_inv.reserved_qty - order.quantity)

    # Increment destination in_transit_qty
    stmt_dst = (
        select(SupplyNodeInventory)
        .where(
            SupplyNodeInventory.study_id == order.study_id,
            SupplyNodeInventory.node_id == order.destination_node_id,
            SupplyNodeInventory.kit_type == order.kit_type,
            SupplyNodeInventory.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_dst = await session.execute(stmt_dst)
    dst_inv = res_dst.scalars().first()
    if dst_inv:
        dst_inv.in_transit_qty += order.quantity
    else:
        # Create destination inventory record if not exists
        new_dst_inv = SupplyNodeInventory(
            study_id=order.study_id,
            node_id=order.destination_node_id,
            kit_type=order.kit_type,
            in_transit_qty=order.quantity,
            on_hand_qty=0,
            reserved_qty=0,
        )
        session.add(new_dst_inv)

    # Emit outbox notification for source and destination depots
    outbox_event = IntegrationOutbox(
        event_type="RTSM_SHIPMENT_DISPATCHED",
        payload={
            "transfer_order_id": order.id,
            "transfer_order_number": order.transfer_order_number,
            "source_node_id": order.source_node_id,
            "destination_node_id": order.destination_node_id,
            "kit_type": order.kit_type,
            "quantity": order.quantity,
            "tracking_number": order.tracking_number,
            "carrier": order.carrier,
            "manifest_number": manifest.manifest_number,
            "data_logger_id": manifest.data_logger_id,
            "shipped_at": order.shipped_at.isoformat(),
        },
        correlation_id=f"DISPATCH_{order.id}",
        created_by=shipped_by,
        reason_for_change=change_reason or "Stock transfer dispatched",
    )
    session.add(outbox_event)

    return order


async def receive_transfer_order_transaction(
    session: AsyncSession,
    order_id: str,
    delivered_by: str,
    change_reason: str | None = None,
) -> TransferOrder:
    """Marks a transfer order DELIVERED at destination, transfers stock from in-transit to on-hand."""
    stmt = (
        select(TransferOrder)
        .where(TransferOrder.id == order_id, TransferOrder.is_deleted.is_(False))
        .with_for_update()
    )
    res = await session.execute(stmt)
    order = res.scalars().first()

    if not order:
        raise TransferOrderNotFoundError(f"Transfer order {order_id} not found.")

    if order.status != "SHIPPED":
        raise InvalidTransferOrderStateError(
            f"Cannot receive order in status '{order.status}' (must be 'SHIPPED')."
        )

    order.status = "DELIVERED"
    order.delivered_at = datetime.now(UTC)
    order.delivered_by = delivered_by
    order.version += 1

    # Update destination node inventory: decrement in_transit, increment on_hand
    stmt_dst = (
        select(SupplyNodeInventory)
        .where(
            SupplyNodeInventory.study_id == order.study_id,
            SupplyNodeInventory.node_id == order.destination_node_id,
            SupplyNodeInventory.kit_type == order.kit_type,
            SupplyNodeInventory.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_dst = await session.execute(stmt_dst)
    dst_inv = res_dst.scalars().first()

    if dst_inv:
        dst_inv.in_transit_qty = max(0, dst_inv.in_transit_qty - order.quantity)
        dst_inv.on_hand_qty += order.quantity
        if dst_inv.on_hand_qty > dst_inv.reorder_point:
            dst_inv.resupply_signal = False
    else:
        new_dst_inv = SupplyNodeInventory(
            study_id=order.study_id,
            node_id=order.destination_node_id,
            kit_type=order.kit_type,
            on_hand_qty=order.quantity,
            in_transit_qty=0,
            reserved_qty=0,
        )
        session.add(new_dst_inv)

    # Also update SiteInventory table if destination is a site
    stmt_site = (
        select(SiteInventory)
        .where(
            SiteInventory.study_id == order.study_id,
            SiteInventory.site_id == order.destination_node_id,
            SiteInventory.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_site = await session.execute(stmt_site)
    site_inv = res_site.scalars().first()
    if site_inv:
        site_inv.on_hand_qty += order.quantity
        if site_inv.on_hand_qty > site_inv.reorder_threshold:
            site_inv.resupply_signal = False

    # Update destination SupplyNode current_utilization
    stmt_node = (
        select(SupplyNode)
        .where(
            SupplyNode.study_id == order.study_id,
            SupplyNode.node_id == order.destination_node_id,
            SupplyNode.is_deleted.is_(False),
        )
        .with_for_update()
    )
    res_node = await session.execute(stmt_node)
    dst_node = res_node.scalars().first()
    if dst_node:
        dst_node.current_utilization = min(
            dst_node.storage_capacity, dst_node.current_utilization + order.quantity
        )

    # Outbox event
    outbox_event = IntegrationOutbox(
        event_type="RTSM_SHIPMENT_DELIVERED",
        payload={
            "transfer_order_id": order.id,
            "transfer_order_number": order.transfer_order_number,
            "destination_node_id": order.destination_node_id,
            "quantity": order.quantity,
            "delivered_at": order.delivered_at.isoformat(),
        },
        correlation_id=f"DELIVER_{order.id}",
        created_by=delivered_by,
        reason_for_change=change_reason or "Stock transfer received at destination",
    )
    session.add(outbox_event)

    return order
