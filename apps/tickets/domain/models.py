"""
Pure domain models, enums, and state machines for the Tickets microservice.
"""

from dataclasses import dataclass
from enum import StrEnum


class TicketCategory(StrEnum):
    """
    Enum representing categories of support and clinical operational tickets.
    """

    TECHNICAL = "TECHNICAL"
    CLINICAL = "CLINICAL"
    HARDWARE = "HARDWARE"
    ACCESS = "ACCESS"
    PROTOCOL_DEVIATION = "PROTOCOL_DEVIATION"
    DATA_QUERY = "DATA_QUERY"
    SAFETY_ADVERSE_EVENT = "SAFETY_ADVERSE_EVENT"
    SUPPLY_EXCURSION = "SUPPLY_EXCURSION"
    SITE_OPERATIONS = "SITE_OPERATIONS"
    REGULATORY_ETMF = "REGULATORY_ETMF"
    CHANGE_REQUEST = "CHANGE_REQUEST"
    OTHER = "OTHER"


class TicketPriority(StrEnum):
    """
    Enum representing priority levels of support tickets.
    """

    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class GxPSeverity(StrEnum):
    """
    Enum representing GxP and ICH GCP compliance impact severity.
    """

    NOT_APPLICABLE = "NOT_APPLICABLE"
    MINOR = "MINOR"
    MAJOR = "MAJOR"
    CRITICAL = "CRITICAL"


class RootCauseCategory(StrEnum):
    """
    Enum representing Root Cause Analysis (RCA) 5-Whys classification.
    """

    HUMAN_ERROR = "HUMAN_ERROR"
    PROTOCOL_DESIGN = "PROTOCOL_DESIGN"
    PROCESS_WORKFLOW = "PROCESS_WORKFLOW"
    SYSTEM_TECHNICAL = "SYSTEM_TECHNICAL"
    INVESTIGATIONAL_PRODUCT = "INVESTIGATIONAL_PRODUCT"
    VENDOR_LAB = "VENDOR_LAB"
    TRAINING_GAP = "TRAINING_GAP"
    OTHER = "OTHER"


class ResolutionCode(StrEnum):
    """
    Enum representing formal resolution outcome codes for clinical tickets.
    """

    RESOLVED_AS_EXPECTED = "RESOLVED_AS_EXPECTED"
    PROTOCOL_CLARIFICATION_ISSUED = "PROTOCOL_CLARIFICATION_ISSUED"
    DATA_AMENDED = "DATA_AMENDED"
    CAPA_INITIATED = "CAPA_INITIATED"
    FALSE_POSITIVE_VOIDED = "FALSE_POSITIVE_VOIDED"
    WORKAROUND_APPLIED = "WORKAROUND_APPLIED"
    CLOSED_UNRESOLVED = "CLOSED_UNRESOLVED"


class TicketStatus(StrEnum):
    """
    Enum representing statuses of support and clinical tickets.
    """

    OPEN = "OPEN"
    IN_PROGRESS = "IN_PROGRESS"
    WAITING_ON_SITE = "WAITING_ON_SITE"
    WAITING_ON_SPONSOR = "WAITING_ON_SPONSOR"
    PENDING_REGULATORY_REVIEW = "PENDING_REGULATORY_REVIEW"
    RESOLVED = "RESOLVED"
    CLOSED = "CLOSED"
    REOPENED = "REOPENED"
    CANCELLED = "CANCELLED"


class CommentVisibility(StrEnum):
    """
    Enum representing visibility boundaries for ticket comments.
    """

    PUBLIC = "PUBLIC"
    INTERNAL_SPONSOR = "INTERNAL_SPONSOR"


# States in which SLA clock is frozen / paused
SLA_PAUSED_STATES = {
    TicketStatus.WAITING_ON_SITE,
    TicketStatus.WAITING_ON_SPONSOR,
    TicketStatus.PENDING_REGULATORY_REVIEW,
}

# Valid state transitions for TicketStatus
TICKET_TRANSITIONS = {
    TicketStatus.OPEN: {
        TicketStatus.IN_PROGRESS,
        TicketStatus.WAITING_ON_SITE,
        TicketStatus.WAITING_ON_SPONSOR,
        TicketStatus.PENDING_REGULATORY_REVIEW,
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
        TicketStatus.CLOSED,
    },
    TicketStatus.IN_PROGRESS: {
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
        TicketStatus.OPEN,
        TicketStatus.WAITING_ON_SITE,
        TicketStatus.WAITING_ON_SPONSOR,
        TicketStatus.PENDING_REGULATORY_REVIEW,
    },
    TicketStatus.WAITING_ON_SITE: {
        TicketStatus.IN_PROGRESS,
        TicketStatus.OPEN,
        TicketStatus.WAITING_ON_SPONSOR,
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
    },
    TicketStatus.WAITING_ON_SPONSOR: {
        TicketStatus.IN_PROGRESS,
        TicketStatus.OPEN,
        TicketStatus.WAITING_ON_SITE,
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
    },
    TicketStatus.PENDING_REGULATORY_REVIEW: {
        TicketStatus.IN_PROGRESS,
        TicketStatus.RESOLVED,
        TicketStatus.CLOSED,
        TicketStatus.CANCELLED,
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
        TicketStatus.WAITING_ON_SITE,
        TicketStatus.WAITING_ON_SPONSOR,
    },
    TicketStatus.CANCELLED: {TicketStatus.REOPENED},
}

# Explicit lifecycle rule categories
TERMINAL_STATES = {TicketStatus.CLOSED, TicketStatus.CANCELLED}
CANCELLABLE_STATES = {
    TicketStatus.OPEN,
    TicketStatus.IN_PROGRESS,
    TicketStatus.WAITING_ON_SITE,
    TicketStatus.WAITING_ON_SPONSOR,
    TicketStatus.PENDING_REGULATORY_REVIEW,
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
