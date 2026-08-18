"""Pure domain entities for eConsent microservice.

Independent of persistence, frameworks, and external drivers.
Complies with FDA 21 CFR Part 11 and ICH GCP E6(R2)/(R3).
"""

from dataclasses import dataclass, field
from datetime import datetime
from enum import StrEnum
from typing import Any


class TemplateStatus(StrEnum):
    """Lifecycle states for consent templates."""

    DRAFT = "DRAFT"
    IN_REVIEW = "IN_REVIEW"
    APPROVED = "APPROVED"
    PUBLISHED = "PUBLISHED"
    RETIRED = "RETIRED"


class TranslationStatus(StrEnum):
    """Lifecycle states for consent translations."""

    DRAFT = "DRAFT"
    IN_REVIEW = "IN_REVIEW"
    APPROVED = "APPROVED"
    REJECTED = "REJECTED"
    PUBLISHED = "PUBLISHED"


class SignerRole(StrEnum):
    """Permitted 21 CFR Part 11 signing roles."""

    SUBJECT = "SUBJECT"
    LAR = "LAR"
    MINOR_ASSENT = "MINOR_ASSENT"
    INVESTIGATOR = "INVESTIGATOR"
    WITNESS = "WITNESS"


class GranularOptionCategory(StrEnum):
    """Categories for tiered optional consent items."""

    GENETICS = "GENETICS"
    BIOBANKING = "BIOBANKING"
    DATA_SHARING = "DATA_SHARING"
    FUTURE_CONTACT = "FUTURE_CONTACT"
    SUBSTUDY = "SUBSTUDY"
    OTHER = "OTHER"


class ReconsentStatus(StrEnum):
    """Status of an individual subject re-consent requirement."""

    PENDING = "PENDING"
    COMPLETED = "COMPLETED"
    WAIVED = "WAIVED"
    OVERDUE = "OVERDUE"


class WithdrawalScope(StrEnum):
    """Scope of consent revocation."""

    STOP_INTERVENTIONS_ONLY = "STOP_INTERVENTIONS_ONLY"
    STOP_ALL_DATA_COLLECTION = "STOP_ALL_DATA_COLLECTION"
    DESTROY_SAMPLES = "DESTROY_SAMPLES"


@dataclass
class ConsentClauseEntity:
    """Represents a versioned, reusable Informed Consent Form clause."""

    id: str
    clause_id: str
    study_id: str
    title: str
    text: str
    version_index: int
    created_at: datetime
    created_by: str
    reason_for_change: str


@dataclass
class ConsentTemplateEntity:
    """Represents a composed eConsent template with ordered clauses."""

    id: str
    template_id: str
    study_id: str
    template_name: str
    protocol_version: str
    is_published: bool
    requires_reconsent: bool
    version_index: int
    clauses: list[str]
    workflow_steps: list[dict[str, Any]]
    created_at: datetime
    created_by: str
    reason_for_change: str


@dataclass
class ConsentTranslationEntity:
    """Represents a localized translation of a clause or template."""

    id: str
    translation_id: str
    source_id: str
    source_type: str
    source_version_index: int
    language_code: str
    translated_title: str
    translated_text: str
    status: TranslationStatus
    version_index: int
    created_at: datetime
    created_by: str
    reason_for_change: str


@dataclass
class ComprehensionQuestionEntity:
    """A comprehension question linked to a clause or concept."""

    id: str
    text: str
    options: list[str]
    correct_answer: str
    clause_reference: str | None = None
    hint: str | None = None
    explanation: str | None = None


@dataclass
class ComprehensionCheckEntity:
    """Comprehension configuration bound to a template version."""

    id: str
    template_id: str
    version_index: int
    questions: list[dict[str, Any]]
    expected_answers: dict[str, str]
    threshold_policy: dict[str, Any]
    created_at: datetime
    created_by: str
    reason_for_change: str


@dataclass
class GranularConsentOptionEntity:
    """Tiered / optional research consent choice."""

    id: str
    template_id: str
    version_index: int
    option_code: str
    title: str
    description: str
    category: GranularOptionCategory
    is_mandatory: bool
    default_selected: bool
    created_at: datetime
    created_by: str
    reason_for_change: str


@dataclass
class ConsentSignatureEntity:
    """A 21 CFR Part 11 compliant electronic signature record."""

    id: str
    template_id: str
    version_index: int
    subject_pseudonym: str
    role: SignerRole
    signer_name: str
    signer_email: str | None
    meaning: str
    signature_data: str | None
    signed_at: datetime
    digest_sha256: str | None
    lar_relationship: str | None = None
    lar_authority_basis: str | None = None
    created_by: str = "system"
    reason_for_change: str = "eConsent Signature Execution"


@dataclass
class GranularOptionSelectionEntity:
    """A subject's discrete choice for an optional consent item."""

    id: str
    consent_id: str
    subject_pseudonym: str
    option_code: str
    selected: bool
    selected_at: datetime
    created_by: str
    reason_for_change: str


@dataclass
class SubjectConsentEntity:
    """An immutable, cryptographically sealed record of subject consent."""

    id: str
    subject_pseudonym: str
    study_id: str
    site_id: str
    template_id: str
    version_index: int
    protocol_version: str
    source_content_identity: str
    server_timestamp: datetime
    device_timestamp: datetime | None
    signature_manifest: dict[str, Any]
    created_at: datetime
    created_by: str
    reason_for_change: str
    status: str = "ACTIVE"
    signatures: list[ConsentSignatureEntity] = field(default_factory=list)
    granular_selections: list[GranularOptionSelectionEntity] = field(
        default_factory=list
    )


@dataclass
class ReconsentRequirementEntity:
    """Tracks required re-consent for a subject after protocol/ICF amendment."""

    id: str
    study_id: str
    site_id: str | None
    template_id: str
    prior_version_index: int
    new_version_index: int
    subject_pseudonym: str
    status: ReconsentStatus
    change_summary: str
    substantive_changes: list[dict[str, Any]]
    deadline_at: datetime | None
    completed_consent_id: str | None
    created_at: datetime
    created_by: str
    reason_for_change: str


@dataclass
class ConsentWithdrawalEntity:
    """An immutable record of subject consent revocation."""

    id: str
    study_id: str
    site_id: str
    subject_pseudonym: str
    template_id: str
    withdrawal_date: datetime
    reason_category: str
    reason_detail: str
    scope: WithdrawalScope
    acknowledged_by_investigator: bool
    investigator_id: str | None
    created_at: datetime
    created_by: str
    reason_for_change: str


@dataclass
class ConsentAuditLogEntity:
    """21 CFR Part 11 append-only audit trail entry."""

    id: str
    timestamp: datetime
    actor_id: str
    actor_role: str
    action: str
    document_id: str | None
    details: str
    reason_for_change: str
