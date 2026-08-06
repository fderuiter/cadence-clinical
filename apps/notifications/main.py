import asyncio
import contextlib
import os
from datetime import datetime, timedelta

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy import and_, or_, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.notifications.database import db_manager
from apps.notifications.models import (
    Base,
    Notification,
    NotificationAuditLog,
    NotificationCategory,
    NotificationDelivery,
    NotificationPriority,
    NotificationStatus,
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware
from packages.security import assert_secure_secrets
from packages.security.rbac import get_normalized_roles


# Pydantic Schemas for Request/Response Validation
class NotificationCreate(BaseModel):
    recipient_user_id: str | None = Field(None, description="Optional target user ID")
    recipient_role: str | None = Field(None, description="Optional target role")
    category: NotificationCategory = Field(
        ..., description="Category: ALERTS, SYSTEM, ACTION_ITEMS"
    )
    priority: NotificationPriority = Field(
        ..., description="Priority: LOW, MEDIUM, HIGH, CRITICAL"
    )
    channels: str = Field(
        "IN_APP", description="Comma-separated delivery channels (e.g. 'IN_APP,EMAIL')"
    )
    message_content: str = Field(..., description="Message content")
    related_entity_id: str | None = Field(
        None, description="Optional related entity ID"
    )
    related_entity_type: str | None = Field(
        None, description="Optional related entity type"
    )


class NotificationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    recipient_user_id: str | None = None
    recipient_role: str | None = None
    category: NotificationCategory
    priority: NotificationPriority
    channels: str
    message_content: str
    related_entity_id: str | None = None
    related_entity_type: str | None = None
    status: NotificationStatus
    delivery_state: str
    retries: int
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


DATABASE_URL = os.getenv("NOTIFICATIONS_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets("notifications", {"WEBHOOK_SIGNING_SECRET": os.getenv("WEBHOOK_SIGNING_SECRET")})


# Global set to track active deliveries in memory
active_deliveries = set()
active_tasks = set()


async def poll_and_dispatch() -> None:
    """
    Polls the database for due notification deliveries and spawns concurrent tasks to process them.
    """
    if not db_manager.session_maker:
        print("DEBUG: poll_and_dispatch returned early because session_maker is None")
        return
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        now = datetime.utcnow()
        # Query due deliveries:
        # 1. status is 'PENDING'
        # 2. OR status is 'FAILED' AND retry_eligible is True AND next_retry_at <= now
        stmt = select(NotificationDelivery).where(
            or_(
                NotificationDelivery.status == "PENDING",
                and_(
                    NotificationDelivery.status == "FAILED",
                    NotificationDelivery.retry_eligible,
                    NotificationDelivery.next_retry_at <= now,
                ),
            )
        )
        result = await session.execute(stmt)
        due = result.scalars().all()
        print(
            f"DEBUG: poll_and_dispatch found {len(due)} due deliveries: {[d.id for d in due]}"
        )

        for d in due:
            if d.id in active_deliveries:
                print(f"DEBUG: delivery {d.id} is already in active_deliveries")
                continue
            active_deliveries.add(d.id)
            print(f"DEBUG: Dispatching delivery {d.id}")
            task = asyncio.create_task(deliver_channel_wrapper(d.id))
            active_tasks.add(task)
            task.add_done_callback(active_tasks.discard)


async def deliver_channel_wrapper(delivery_id: str) -> None:
    print(f"DEBUG: deliver_channel_wrapper called with id {delivery_id}")
    try:
        await deliver_channel(delivery_id)
    except Exception:
        import traceback

        print("DEBUG: Exception in deliver_channel_wrapper:")
        traceback.print_exc()
    finally:
        active_deliveries.discard(delivery_id)


async def deliver_channel(delivery_id: str) -> None:
    """
    Executes a single delivery channel attempt for a specific notification.
    """
    from apps.notifications.delivery import (
        send_email_notification,
        send_webhook_notification,
    )

    if not db_manager.session_maker:
        print("DEBUG: deliver_channel session_maker is None")
        return
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        print("DEBUG: deliver_channel session opened")
        stmt = (
            select(NotificationDelivery)
            .where(NotificationDelivery.id == delivery_id)
            .with_for_update()
        )
        print("DEBUG: deliver_channel executing select query...")
        result = await session.execute(stmt)
        delivery = result.scalars().first()
        print(f"DEBUG: deliver_channel found delivery: {delivery is not None}")
        if not delivery:
            return

        # Defensive check for multi-replica race conditions after acquiring the lock
        if delivery.status not in ("PENDING", "FAILED"):
            print(
                f"DEBUG: deliver_channel delivery status is not pending/failed: {delivery.status}"
            )
            return

        stmt_notif = select(Notification).where(
            Notification.id == delivery.notification_id
        )
        result_notif = await session.execute(stmt_notif)
        notification = result_notif.scalars().first()
        print(f"DEBUG: deliver_channel found notification: {notification is not None}")
        if not notification:
            delivery.status = "FAILED"
            delivery.retry_eligible = False
            delivery.last_error = "Parent notification not found"
            await session.commit()
            return

        # Increment attempt count
        delivery.attempts += 1
        notification.retries = max(notification.retries, delivery.attempts)
        print(f"DEBUG: deliver_channel channel: {delivery.channel}")

        try:
            if delivery.channel == "IN_APP":
                # In-app delivery is instant and always succeeds
                delivery.status = "SUCCESS"
                delivery.completed_at = datetime.utcnow()
                notification.delivery_state = "DELIVERED"

            elif delivery.channel == "EMAIL":
                print("DEBUG: deliver_channel calling send_email_notification...")
                await send_email_notification(notification)
                delivery.status = "SUCCESS"
                delivery.completed_at = datetime.utcnow()
                print(
                    "DEBUG: deliver_channel send_email_notification finished successfully"
                )

            elif delivery.channel == "WEBHOOK":
                print("DEBUG: deliver_channel calling send_webhook_notification...")
                await send_webhook_notification(notification)
                delivery.status = "SUCCESS"
                delivery.completed_at = datetime.utcnow()
                print(
                    "DEBUG: deliver_channel send_webhook_notification finished successfully"
                )

            else:
                raise ValueError(f"Unknown channel: {delivery.channel}")

        except Exception as e:
            print(f"DEBUG: deliver_channel exception in try: {e}")
            delivery.status = "FAILED"
            delivery.last_error = str(e)

            # Determine retry eligibility and next retry delay with bounded exponential backoff
            max_attempts = int(os.getenv("NOTIFICATION_MAX_ATTEMPTS", "5"))
            if delivery.attempts >= max_attempts:
                delivery.retry_eligible = False
                # Write exhaustion audit entry
                await write_audit_log(
                    session=session,
                    user_id="system",
                    user_role="system",
                    action="NOTIFICATION_DELIVERY_EXHAUSTED",
                    details=f"Delivery exhausted for channel '{delivery.channel}' of notification '{delivery.notification_id}' after {delivery.attempts} attempts. Last error: '{str(e)}'",
                )
            else:
                base_delay = float(os.getenv("NOTIFICATION_RETRY_BASE_DELAY", "2.0"))
                max_delay = float(os.getenv("NOTIFICATION_RETRY_MAX_DELAY", "3600.0"))
                backoff_delay = min(
                    max_delay, base_delay * (2 ** (delivery.attempts - 1))
                )
                delivery.next_retry_at = datetime.utcnow() + timedelta(
                    seconds=backoff_delay
                )
                delivery.retry_eligible = True

            # Emit a per-failure audit/log record on each failed attempt
            await write_audit_log(
                session=session,
                user_id="system",
                user_role="system",
                action="NOTIFICATION_DELIVERY_FAILED",
                details=f"Delivery failed for channel '{delivery.channel}' of notification '{delivery.notification_id}' on attempt {delivery.attempts}. Error: '{str(e)}'",
            )

        await session.commit()


async def dispatcher_lifecycle_worker() -> None:
    """
    Background worker loop that periodically ticks the poller.
    """
    while True:
        try:
            await poll_and_dispatch()
        except asyncio.CancelledError:
            break
        except Exception:
            # Prevent failures from crashing the background loop
            pass
        await asyncio.sleep(1.0)


dispatcher_task: asyncio.Task | None = None


async def start_dispatcher() -> None:
    """Startup hook to start the notifications background dispatcher."""
    global dispatcher_task
    import sys

    if "pytest" in sys.modules or os.getenv("PYTEST_CURRENT_TEST"):
        return
    dispatcher_task = asyncio.create_task(dispatcher_lifecycle_worker())


async def stop_dispatcher() -> None:
    """Shutdown hook to cleanly cancel the notifications background dispatcher."""
    global dispatcher_task
    if dispatcher_task:
        dispatcher_task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await dispatcher_task


app = FastAPI(
    title="Cadence Clinical - Notifications Service",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
        startup_hooks=[start_dispatcher],
        shutdown_hooks=[stop_dispatcher],
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


get_db_session = DatabaseSessionDependency(db_manager)


async def write_audit_log(
    session: AsyncSession,
    user_id: str,
    user_role: str,
    action: str,
    details: str,
) -> None:
    """
    Utility helper to write to the append-only NotificationAuditLog.
    """
    log_entry = NotificationAuditLog(
        user_id=user_id,
        user_role=user_role,
        action=action,
        details=details,
    )
    session.add(log_entry)
    await session.flush()


def map_notification_to_response(notif: Notification) -> NotificationResponse:
    return NotificationResponse(
        id=notif.id,
        recipient_user_id=notif.recipient_user_id,
        recipient_role=notif.recipient_role,
        category=notif.category,
        priority=notif.priority,
        channels=notif.channels,
        message_content=notif.message_content,
        related_entity_id=notif.related_entity_id,
        related_entity_type=notif.related_entity_type,
        status=notif.status,
        delivery_state=notif.delivery_state,
        retries=notif.retries,
        created_at=notif.created_at.isoformat(),
        created_by=notif.created_by,
        version_index=notif.version_index,
        reason_for_change=notif.reason_for_change,
    )


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "notifications"}


@app.post("/api/v1/notifications", response_model=NotificationResponse, status_code=201)
async def create_notification(
    request: Request,
    payload: NotificationCreate,
    session: AsyncSession = Depends(get_db_session),
) -> NotificationResponse:
    """
    Create a new notification targeting a specific user ID or role.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles_list = get_normalized_roles(request)
    user_role = ",".join(user_roles_list) or "system"
    change_reason = getattr(request.state, "change_reason", "Notification creation")

    requested_channels = [c.strip() for c in payload.channels.split(",") if c.strip()]
    # Starts as PENDING if in_app is requested so it's not immediately visible on dashboard until processed.
    initial_delivery_state = (
        "PENDING" if "IN_APP" in requested_channels else "DELIVERED"
    )

    notif = Notification(
        recipient_user_id=payload.recipient_user_id,
        recipient_role=payload.recipient_role,
        category=payload.category,
        priority=payload.priority,
        channels=payload.channels,
        message_content=payload.message_content,
        related_entity_id=payload.related_entity_id,
        related_entity_type=payload.related_entity_type,
        status=NotificationStatus.OPEN,
        delivery_state=initial_delivery_state,
        retries=0,
        created_by=user_id,
        version_index=1,
        reason_for_change=change_reason,
    )
    session.add(notif)
    await session.flush()

    # Create associated NotificationDelivery rows for async channel dispatching
    for channel in requested_channels:
        delivery = NotificationDelivery(
            notification_id=notif.id,
            channel=channel,
            status="PENDING",
            attempts=0,
            retry_eligible=True,
        )
        session.add(delivery)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="NOTIFICATION_CREATE",
        details=f"Created notification ID '{notif.id}' targeting user '{payload.recipient_user_id}' / role '{payload.recipient_role}'.",
    )

    # GxP compliance: Explicitly identify direct Sponsor Medical Monitor alerts to bypass NotificationRouter and record PII-safe attempt
    if payload.recipient_role and payload.recipient_role.lower() in (
        "sponsor_mm",
        "sponsor medical monitor",
        "medical_monitor",
        "medical monitor",
        "mm",
    ):
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="MEDICAL_MONITOR_ALERT_ATTEMPT",
            details=f"Direct PII-safe Sponsor Medical Monitor notification attempt recorded for notification '{notif.id}'. Bypassed NotificationRouter.",
        )

    return map_notification_to_response(notif)


@app.get("/api/v1/notifications", response_model=list[NotificationResponse])
async def list_notifications(
    request: Request,
    category: NotificationCategory | None = Query(
        None, description="Filter by category"
    ),
    priority: NotificationPriority | None = Query(
        None, description="Filter by priority"
    ),
    status: NotificationStatus | None = Query(None, description="Filter by status"),
    session: AsyncSession = Depends(get_db_session),
) -> list[NotificationResponse]:
    """
    Retrieve notification records targeted to the current user's user ID or roles, with optional filtering.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles_list = get_normalized_roles(request)
    user_role = ",".join(user_roles_list) or "system"

    stmt = select(Notification)

    # Enforce role- and recipient-based visibility
    # A user can only view notifications where recipient_user_id matches current user_id
    # OR recipient_role is in the user's active normalized roles.
    # If no target recipient is specified (both NULL), it is visible to everyone (or global/system).
    # For GxP and audit trails, we strictly enforce this identity context boundary.
    visibility_clauses = [
        Notification.recipient_user_id == user_id,
    ]
    for r in user_roles_list:
        visibility_clauses.append(Notification.recipient_role == r)

    # Also include global notifications with no specific targets if any
    visibility_clauses.append(
        (Notification.recipient_user_id.is_(None))
        & (Notification.recipient_role.is_(None))
    )

    stmt = stmt.where(or_(*visibility_clauses))

    # Ensure in-app notifications are delivered and visible to the dashboard
    stmt = stmt.where(Notification.delivery_state == "DELIVERED")

    if category:
        stmt = stmt.where(Notification.category == category)
    if priority:
        stmt = stmt.where(Notification.priority == priority)
    if status:
        stmt = stmt.where(Notification.status == status)

    result = await session.execute(stmt)
    notifications = result.scalars().all()

    # Log listing action
    filters = f"category={category}, priority={priority}, status={status}"
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="NOTIFICATION_LIST",
        details=f"Listed notifications matching criteria: {filters}.",
    )

    return [map_notification_to_response(notif) for notif in notifications]


@app.get("/api/v1/notifications/{id}", response_model=NotificationResponse)
async def view_notification(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> NotificationResponse:
    """
    Retrieve a specific notification by ID. Enforces strict role- and recipient-based visibility.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles_list = get_normalized_roles(request)
    user_role = ",".join(user_roles_list) or "system"

    stmt = select(Notification).where(Notification.id == id)
    result = await session.execute(stmt)
    notif = result.scalars().first()

    if not notif:
        raise HTTPException(status_code=404, detail="Notification not found")

    # Enforce role- and recipient-based visibility
    has_visibility = (
        notif.recipient_user_id == user_id
        or (notif.recipient_role in user_roles_list)
        or (notif.recipient_user_id is None and notif.recipient_role is None)
    )

    if not has_visibility:
        raise HTTPException(
            status_code=403, detail="Forbidden: Recipient target boundary violation."
        )

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="NOTIFICATION_VIEW",
        details=f"Viewed notification ID: {id}.",
    )

    return map_notification_to_response(notif)


@app.post("/api/v1/notifications/{id}/acknowledge", response_model=NotificationResponse)
async def acknowledge_notification(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> NotificationResponse:
    """
    Transition notification state from OPEN to ACKNOWLEDGED.
    Requires a reason for lifecycle transition and checks target-based visibility.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles_list = get_normalized_roles(request)
    user_role = ",".join(user_roles_list) or "system"
    change_reason = getattr(request.state, "change_reason", None)

    if not change_reason:
        raise HTTPException(
            status_code=400, detail="Missing change justification reason"
        )

    stmt = select(Notification).where(Notification.id == id)
    result = await session.execute(stmt)
    notif = result.scalars().first()

    if not notif:
        raise HTTPException(status_code=404, detail="Notification not found")

    # Enforce role- and recipient-based visibility
    has_visibility = (
        notif.recipient_user_id == user_id
        or (notif.recipient_role in user_roles_list)
        or (notif.recipient_user_id is None and notif.recipient_role is None)
    )

    if not has_visibility:
        raise HTTPException(
            status_code=403, detail="Forbidden: Recipient target boundary violation."
        )

    current_status = notif.status
    if current_status != NotificationStatus.OPEN:
        raise HTTPException(
            status_code=422,
            detail=f"Invalid transition from state '{current_status}' to 'ACKNOWLEDGED'. Only 'OPEN' notifications can be acknowledged.",
        )

    notif.status = NotificationStatus.ACKNOWLEDGED
    notif.version_index += 1
    notif.reason_for_change = change_reason

    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="NOTIFICATION_ACKNOWLEDGE",
        details=f"Acknowledged notification ID '{notif.id}' (OPEN -> ACKNOWLEDGED) with reason: '{change_reason}'.",
    )

    return map_notification_to_response(notif)


@app.post("/api/v1/notifications/{id}/resolve", response_model=NotificationResponse)
async def resolve_notification(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> NotificationResponse:
    """
    Transition notification state from OPEN or ACKNOWLEDGED to RESOLVED.
    Requires a reason for lifecycle transition and checks target-based visibility.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles_list = get_normalized_roles(request)
    user_role = ",".join(user_roles_list) or "system"
    change_reason = getattr(request.state, "change_reason", None)

    if not change_reason:
        raise HTTPException(
            status_code=400, detail="Missing change justification reason"
        )

    stmt = select(Notification).where(Notification.id == id)
    result = await session.execute(stmt)
    notif = result.scalars().first()

    if not notif:
        raise HTTPException(status_code=404, detail="Notification not found")

    # Enforce role- and recipient-based visibility
    has_visibility = (
        notif.recipient_user_id == user_id
        or (notif.recipient_role in user_roles_list)
        or (notif.recipient_user_id is None and notif.recipient_role is None)
    )

    if not has_visibility:
        raise HTTPException(
            status_code=403, detail="Forbidden: Recipient target boundary violation."
        )

    current_status = notif.status
    if current_status not in (NotificationStatus.OPEN, NotificationStatus.ACKNOWLEDGED):
        raise HTTPException(
            status_code=422,
            detail=f"Invalid transition from state '{current_status}' to 'RESOLVED'. Only 'OPEN' or 'ACKNOWLEDGED' notifications can be resolved.",
        )

    notif.status = NotificationStatus.RESOLVED
    notif.version_index += 1
    notif.reason_for_change = change_reason

    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="NOTIFICATION_RESOLVE",
        details=f"Resolved notification ID '{notif.id}' ({current_status} -> RESOLVED) with reason: '{change_reason}'.",
    )

    return map_notification_to_response(notif)
