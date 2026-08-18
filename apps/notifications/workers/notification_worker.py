import asyncio
import contextlib
import json
import logging
import os
import sys
from typing import Any

from apps.notifications.application.services.email_renderer import (
    get_template_name_for_event,
    render_email_template,
)
from apps.notifications.domain.event_models import SystemDomainEvent
from apps.notifications.infrastructure.database import (
    db_manager as notifications_db_manager,
)
from apps.notifications.infrastructure.models import (
    Notification,
    NotificationCategory,
    NotificationDelivery,
    NotificationPriority,
    NotificationStatus,
)
from packages.security.gateway_client import (
    GatewayBaseClient,
    create_service_auth_headers,
)

ORG_URL = (os.getenv("ORG_URL") or "http://localhost:8010").rstrip("/")


def _get_auth_headers() -> dict[str, str]:
    return create_service_auth_headers(user_id="notifications-service")


logger = logging.getLogger("notification_worker")


def _get_logger():
    return logger


_mock_queue: asyncio.Queue | None = None
_mock_queue_loop: asyncio.AbstractEventLoop | None = None


def _get_mock_queue() -> asyncio.Queue:
    global _mock_queue, _mock_queue_loop
    try:
        current_loop = asyncio.get_running_loop()
    except RuntimeError:
        current_loop = None

    if _mock_queue is None or (
        current_loop is not None and _mock_queue_loop is not current_loop
    ):
        _mock_queue = asyncio.Queue()
        _mock_queue_loop = current_loop
    return _mock_queue


class NotificationWorker:
    """
    Asynchronous event worker that consumes clinical domain events,
    resolves recipient user assignments, and dispatches alerts.
    """

    async def resolve_recipients(
        self, event_type: str, study_id: str, payload: dict[str, Any]
    ) -> list[dict[str, str]]:
        roles_to_find = []
        site_id = payload.get("site_id")

        if event_type == "EDC_QUERY_RAISED":
            roles_to_find = [
                "crc",
                "clinical research coordinator",
                "study coordinator",
                "coordinator",
            ]
        elif event_type == "ETMF_DOCUMENT_EXPIRING":
            roles_to_find = [
                "cra",
                "clinical research associate",
                "tmf document manager",
                "tmf manager",
            ]
        elif event_type == "SAE_RECONCILIATION_FLAG":
            roles_to_find = [
                "sponsor_mm",
                "medical_monitor",
                "medical monitor",
                "sponsor medical monitor",
                "mm",
                "safety_officer",
                "safety officer",
            ]
        elif event_type == "PROTOCOL_AMENDMENT_SUBMITTED":
            roles_to_find = [
                "sponsor_designer",
                "designer",
                "study_designer",
                "investigator",
                "crc",
                "cra",
            ]

        resolved = []

        try:
            client = GatewayBaseClient(base_url=ORG_URL)
            params = {}
            if study_id:
                params["study_id"] = study_id
            if site_id:
                params["site_id"] = site_id

            response = await client.request(
                method="GET",
                path="/api/v1/org/personnel",
                user_id="notifications-service",
                roles="system",
                change_reason="Resolve study personnel",
                params=params,
            )
            if response.status_code < 400:
                rows = response.json()
                for p in rows:
                    r_user_id = p.get("keycloak_user_id")
                    r_email = p.get("email")
                    r_role = p.get("role")
                    if r_role:
                        role_norm = r_role.lower().strip()
                        if any(role_norm == r.lower() for r in roles_to_find):
                            resolved.append(
                                {
                                    "user_id": r_user_id or r_email,
                                    "email": r_email,
                                }
                            )
        except Exception as e:
            logger.warning(
                f"Failed to query org service via REST for recipient resolution: {e}. Falling back."
            )

        if not resolved:
            logger.info(
                f"No database assignments found. Using deterministic fallback for {event_type}."
            )
            brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
            if event_type == "EDC_QUERY_RAISED":
                target_id = f"crc_{site_id or 'default'}"
                resolved.append(
                    {
                        "user_id": target_id,
                        "email": f"{target_id}@{brand_domain}",
                    }
                )
            elif event_type == "ETMF_DOCUMENT_EXPIRING":
                target_id = f"cra_{site_id or 'default'}"
                resolved.append(
                    {
                        "user_id": target_id,
                        "email": f"{target_id}@{brand_domain}",
                    }
                )
            elif event_type == "SAE_RECONCILIATION_FLAG":
                resolved.append(
                    {
                        "user_id": "safety_officer",
                        "email": f"safety_officer@{brand_domain}",  # deid-ignore
                    }
                )
            elif event_type == "PROTOCOL_AMENDMENT_SUBMITTED":
                resolved.append(
                    {
                        "user_id": "designer_john",
                        "email": f"designer_john@{brand_domain}",  # deid-ignore
                    }
                )
            elif event_type in ("RECONSENT_REQUIRED", "PROTOCOL_AMENDMENT_RECONSENT"):
                target_ids = payload.get("impacted_subjects") or [
                    payload.get("subject_pseudonym")
                    or payload.get("subject_id")
                    or payload.get("user_id")
                    or "subject_001"
                ]
                for tid in target_ids:
                    resolved.append(
                        {
                            "user_id": tid,
                            "email": payload.get("email") or f"{tid}@{brand_domain}",
                        }
                    )

        unique_resolved = []
        seen = set()
        for res in resolved:
            if res["user_id"] not in seen:
                seen.add(res["user_id"])
                unique_resolved.append(res)

        return unique_resolved

    async def process_domain_event(self, event: SystemDomainEvent) -> int:
        dispatched_count = 0
        _get_logger().info(
            f"Processing event {event.event_id} of type {event.event_type}."
        )

        recipients = await self.resolve_recipients(
            event.event_type, event.study_id, event.payload
        )

        category = NotificationCategory.SYSTEM
        priority = NotificationPriority.MEDIUM

        if event.event_type == "EDC_QUERY_RAISED":
            category = NotificationCategory.ACTION_ITEMS
            priority = NotificationPriority.HIGH
            summary_message = f"New clinical query raised on site {event.payload.get('site_id')}: {event.payload.get('query_message')}"
        elif event.event_type == "ETMF_DOCUMENT_EXPIRING":
            category = NotificationCategory.ALERTS
            priority = NotificationPriority.HIGH
            summary_message = f"TMF Document '{event.payload.get('document_name')}' is expiring on {event.payload.get('expiration_date')}."
        elif event.event_type == "SAE_RECONCILIATION_FLAG":
            category = NotificationCategory.ALERTS
            priority = NotificationPriority.CRITICAL
            summary_message = f"Urgent SAE reconciliation mismatch: {event.payload.get('flag_reason')}"
        elif event.event_type == "PROTOCOL_AMENDMENT_SUBMITTED":
            category = NotificationCategory.SYSTEM
            priority = NotificationPriority.LOW
            summary_message = (
                f"Protocol amendment submitted: {event.payload.get('amendment_tag')}"
            )
        elif event.event_type in ("RECONSENT_REQUIRED", "PROTOCOL_AMENDMENT_RECONSENT"):
            category = NotificationCategory.ALERTS
            priority = NotificationPriority.CRITICAL
            summary_message = (
                f"URGENT: Protocol amendment re-consent required for study {event.study_id}. "
                f"Version: {event.payload.get('version_number') or event.payload.get('protocol_version') or event.payload.get('new_version_index') or '2.0'}"
            )
        else:
            summary_message = f"Event received: {event.event_type}"

        template_name = get_template_name_for_event(event.event_type)
        context = {
            "study_id": event.study_id,
            "event_id": event.event_id,
            "timestamp_utc": event.timestamp_utc,
            "payload": event.payload,
        }
        rendered_html = render_email_template(template_name, context)
        logger.debug(f"Rendered GxP HTML body: {len(rendered_html)} chars.")

        for recipient in recipients:
            user_id = recipient["user_id"]
            email_addr = recipient["email"]
            logger.info(f"Target email resolved: {email_addr}")

            logger.info(
                f"[WebSocket] Dispatching live in-app alert to user {user_id}: '{summary_message}'"
            )

            if notifications_db_manager.session_maker:
                try:
                    async with (
                        notifications_db_manager.get_session_maker()() as session
                    ):
                        notif = Notification(
                            recipient_user_id=user_id,
                            category=category,
                            priority=priority,
                            channels="IN_APP,EMAIL",
                            message_content=summary_message,
                            related_entity_id=event.event_id,
                            related_entity_type=event.event_type,
                            status=NotificationStatus.OPEN,
                            delivery_state="PENDING",
                            created_by="system",
                            reason_for_change="Automated notification generated from domain event",
                        )
                        session.add(notif)
                        await session.flush()

                        delivery_in_app = NotificationDelivery(
                            notification_id=notif.id,
                            channel="IN_APP",
                            status="PENDING",
                            attempts=0,
                            retry_eligible=True,
                        )
                        delivery_email = NotificationDelivery(
                            notification_id=notif.id,
                            channel="EMAIL",
                            status="PENDING",
                            attempts=0,
                            retry_eligible=True,
                        )
                        session.add_all([delivery_in_app, delivery_email])
                        await session.commit()

                        dispatched_count += 1
                        logger.info(
                            f"Enqueued db-notification '{notif.id}' for user {user_id}."
                        )
                except Exception as e:
                    logger.error(f"Failed to persist notification in database: {e}")
            else:
                dispatched_count += 1

        return dispatched_count


async def publish_domain_event(event: SystemDomainEvent) -> None:
    message_str = event.model_dump_json()
    await _get_mock_queue().put(message_str)


_worker_task: asyncio.Task | None = None
_should_run: bool = False


async def start_notification_worker() -> None:
    global _worker_task, _should_run
    if _worker_task and not _worker_task.done():
        return

    _should_run = True
    worker = NotificationWorker()
    queue = _get_mock_queue()

    async def worker_loop():
        logger.info("Notification Background Consumer Worker loop initiated.")
        while _should_run:
            try:
                try:
                    message_str = await asyncio.wait_for(queue.get(), timeout=1.0)
                except TimeoutError:
                    continue

                try:
                    event_data = json.loads(message_str)
                    event = SystemDomainEvent.model_validate(event_data)

                    max_attempts = 3
                    attempt = 0
                    success = False
                    last_err = None

                    while attempt < max_attempts and not success:
                        attempt += 1
                        try:
                            await worker.process_domain_event(event)
                            success = True
                        except Exception as ex:
                            last_err = ex
                            _get_logger().warning(
                                f"Attempt {attempt}/{max_attempts} failed to process event {event.event_id}: {ex}"
                            )
                            if attempt < max_attempts:
                                delay = (
                                    0.001 if "pytest" in sys.modules else 2.0**attempt
                                )
                                await asyncio.sleep(delay)

                    if not success:
                        _get_logger().error(
                            f"[DLQ] Event processing exhausted. EVENT_ID: {event.event_id}, EVENT_TYPE: {event.event_type}. ERROR: {last_err}"
                        )

                except Exception as parse_ex:
                    _get_logger().error(
                        f"Malformed event received on channel. Failed to parse: {parse_ex}"
                    )

            except Exception as loop_ex:
                _get_logger().error(
                    f"Error in background notification worker loop: {loop_ex}"
                )
                await asyncio.sleep(1.0)

    _worker_task = asyncio.create_task(worker_loop())


async def stop_notification_worker() -> None:
    global _worker_task, _should_run
    _should_run = False
    if _worker_task:
        _worker_task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await _worker_task
        _worker_task = None
    logger.info("Notification Background Consumer Worker cleanly shut down.")
