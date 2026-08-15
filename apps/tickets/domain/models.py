"""
Pure domain models, enums, and state machines for the Tickets microservice.
"""

from dataclasses import dataclass
from enum import StrEnum


class TicketCategory(StrEnum):
    """
    Enum representing categories of support tickets.
    """

    TECHNICAL = "TECHNICAL"
    CLINICAL = "CLINICAL"
    HARDWARE = "HARDWARE"
    ACCESS = "ACCESS"
    OTHER = "OTHER"


class TicketPriority(StrEnum):
    """
    Enum representing priority levels of support tickets.
    """

    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class TicketStatus(StrEnum):
    """
    Enum representing statuses of support tickets.
    """

    OPEN = "OPEN"
    IN_PROGRESS = "IN_PROGRESS"
    RESOLVED = "RESOLVED"
    CLOSED = "CLOSED"
    REOPENED = "REOPENED"
    CANCELLED = "CANCELLED"


# Valid state transitions for TicketStatus
TICKET_TRANSITIONS = {
    TicketStatus.OPEN: {
        TicketStatus.IN_PROGRESS,
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
        TicketStatus.CLOSED,
    },
    TicketStatus.IN_PROGRESS: {
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
        TicketStatus.OPEN,
    },
    TicketStatus.RESOLVED: {
        TicketStatus.CLOSED,
        TicketStatus.REOPENED,
        TicketStatus.IN_PROGRESS,
    },
    TicketStatus.CLOSED: {TicketStatus.REOPENED},
    TicketStatus.REOPENED: {
        TicketStatus.IN_PROGRESS,
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
    },
    TicketStatus.CANCELLED: {TicketStatus.REOPENED},
}

# Explicit lifecycle rule categories
TERMINAL_STATES = {TicketStatus.CLOSED, TicketStatus.CANCELLED}
CANCELLABLE_STATES = {
    TicketStatus.OPEN,
    TicketStatus.IN_PROGRESS,
    TicketStatus.RESOLVED,
    TicketStatus.REOPENED,
}
REOPENABLE_STATES = {TicketStatus.CLOSED, TicketStatus.CANCELLED}


@dataclass
class CommittedTicket:
    """
    Committed ticket snapshot for notifications and background tasks.
    """

    id: str
    reference: str
    assignee_user: str | None
    assignee_role: str | None
    reporter: str
    version_index: int


@dataclass
class RegulatoryRiskAssessment:
    """
    Schema representing a clinical and regulatory risk assessment for a setting change.
    """

    risk_level: str
    affected_gxp_clauses: list[str]
    requires_qa_signoff: bool
    summary: str
    risk_summary: str
