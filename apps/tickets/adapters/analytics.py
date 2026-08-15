"""
Clinical KPI and KRI analytics engine for Tickets microservice.
"""

from datetime import UTC, datetime
from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.tickets.adapters.models import (
    GxPSeverity,
    Ticket,
    TicketCategory,
)


async def compute_ticket_kpi_summary(
    session: AsyncSession,
    org_id: str | None = None,
    site_id: str | None = None,
    study_id: str | None = None,
) -> dict[str, Any]:
    """
    Computes real-time clinical Key Performance Indicators (KPI) and Key Risk Indicators (KRI).
    Supports multi-site and study filtering.
    """
    base_query = select(Ticket).where(Ticket.is_deleted.is_(False))
    if org_id:
        base_query = base_query.where(Ticket.org_id == org_id)
    if site_id:
        base_query = base_query.where(Ticket.site_id == site_id)
    if study_id:
        base_query = base_query.where(Ticket.study_id == study_id)

    result = await session.execute(base_query)
    tickets = result.scalars().all()

    total_tickets = len(tickets)
    open_tickets = 0
    in_progress_tickets = 0
    waiting_tickets = 0
    resolved_tickets = 0
    closed_tickets = 0
    critical_deviations = 0
    sla_breaches = 0
    sla_amber_warnings = 0

    category_counts: dict[str, int] = {}
    severity_counts: dict[str, int] = {}
    rca_counts: dict[str, int] = {}
    site_counts: dict[str, int] = {}

    durations_hours: list[float] = []
    now = datetime.now(UTC)

    for t in tickets:
        st = str(t.status.value if hasattr(t.status, "value") else t.status)
        cat = str(t.category.value if hasattr(t.category, "value") else t.category)
        sev = str(t.gxp_severity) if t.gxp_severity else "NOT_APPLICABLE"
        rca = str(t.root_cause_category) if t.root_cause_category else "UNASSIGNED"
        site = str(t.site_id) if t.site_id else "GLOBAL"

        category_counts[cat] = category_counts.get(cat, 0) + 1
        severity_counts[sev] = severity_counts.get(sev, 0) + 1
        site_counts[site] = site_counts.get(site, 0) + 1

        if st in ("RESOLVED", "CLOSED") and t.root_cause_category:
            rca_counts[rca] = rca_counts.get(rca, 0) + 1

        if st == "OPEN":
            open_tickets += 1
        elif st == "IN_PROGRESS":
            in_progress_tickets += 1
        elif st in (
            "WAITING_ON_SITE",
            "WAITING_ON_SPONSOR",
            "PENDING_REGULATORY_REVIEW",
        ):
            waiting_tickets += 1
        elif st == "RESOLVED":
            resolved_tickets += 1
        elif st == "CLOSED":
            closed_tickets += 1

        if (
            cat == TicketCategory.PROTOCOL_DEVIATION.value
            and sev == GxPSeverity.CRITICAL.value
        ):
            critical_deviations += 1

        if t.sla_breached:
            sla_breaches += 1
        elif t.sla_amber_warned:
            sla_amber_warnings += 1

        # MTTR calculation for resolved/closed tickets
        if st in ("RESOLVED", "CLOSED") and t.created_at:
            created = (
                t.created_at.replace(tzinfo=UTC)
                if t.created_at.tzinfo is None
                else t.created_at
            )
            delta = (now - created).total_seconds() / 3600.0
            durations_hours.append(max(0.1, delta))

    mttr_hours = (
        round(sum(durations_hours) / len(durations_hours), 1)
        if durations_hours
        else 0.0
    )
    sla_compliance_rate = (
        round(((total_tickets - sla_breaches) / total_tickets) * 100.0, 1)
        if total_tickets > 0
        else 100.0
    )

    return {
        "total_tickets": total_tickets,
        "active_tickets": open_tickets + in_progress_tickets + waiting_tickets,
        "open_tickets": open_tickets,
        "in_progress_tickets": in_progress_tickets,
        "waiting_tickets": waiting_tickets,
        "resolved_tickets": resolved_tickets,
        "closed_tickets": closed_tickets,
        "critical_deviations": critical_deviations,
        "sla_breaches": sla_breaches,
        "sla_amber_warnings": sla_amber_warnings,
        "sla_compliance_rate": sla_compliance_rate,
        "mean_time_to_resolution_hours": mttr_hours,
        "category_distribution": category_counts,
        "severity_distribution": severity_counts,
        "rca_distribution": rca_counts,
        "site_distribution": site_counts,
    }
