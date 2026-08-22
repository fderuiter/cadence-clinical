"""Standard Part 11 compliant audit and metadata fields for Pydantic v2."""

from datetime import UTC, datetime
from enum import StrEnum
from typing import Any, Self

from pydantic import BaseModel, Field, field_validator, model_validator

from packages.database.datetime_helpers import AwareDatetime


class AIReviewStatus(StrEnum):
    """Lifecycle review statuses for AI-generated or AI-assisted clinical entities."""

    DRAFT_AI = "DRAFT_AI"
    PENDING_REVIEW = "PENDING_REVIEW"
    APPROVED = "APPROVED"
    REJECTED = "REJECTED"


class AIAssistedRecordMixin(BaseModel):
    """A reusable Pydantic v2 mixin providing 21 CFR Part 11 dual-attribution for AI-generated entities."""

    model_identifier: str = Field(
        ...,
        description="Name and version of the AI model that generated or assisted this draft.",
    )
    prompt_hash: str = Field(
        ...,
        description="Cryptographic SHA-256 hash of the input prompt/context used to produce this draft.",
    )
    confidence_score: float = Field(
        ...,
        ge=0.0,
        le=1.0,
        description="Mathematical confidence or semantic similarity score (0.0 to 1.0).",
    )
    review_status: AIReviewStatus = Field(
        default=AIReviewStatus.DRAFT_AI,
        description="Human-in-the-loop lifecycle state.",
    )
    approved_by_user_id: str | None = Field(
        default=None,
        description="User identifier of the human reviewer who validated and approved this record.",
    )
    approved_at: AwareDatetime | None = Field(
        default=None,
        description="Chronological UTC timestamp when human approval and e-signature occurred.",
    )
    esignature_manifest_id: str | None = Field(
        default=None,
        description="Reference identifier to the 21 CFR Part 11 cryptographic signature manifest.",
    )

    @model_validator(mode="after")
    def validate_approval_state(self) -> Self:
        """Enforce that APPROVED state strictly requires human reviewer ID, timestamp, and signature manifest."""

        if self.review_status == AIReviewStatus.APPROVED:
            if not self.approved_by_user_id or not self.approved_by_user_id.strip():
                raise ValueError(
                    "approved_by_user_id is mandatory when review_status is APPROVED."
                )
            if self.approved_at is None:
                raise ValueError(
                    "approved_at is mandatory when review_status is APPROVED."
                )
            if (
                not self.esignature_manifest_id
                or not self.esignature_manifest_id.strip()
            ):
                raise ValueError(
                    "esignature_manifest_id is mandatory when review_status is APPROVED."
                )
        return self

    def is_active_clinical_data(self) -> bool:
        """Check if this AI-assisted record has been approved for active clinical execution."""
        return (
            self.review_status == AIReviewStatus.APPROVED
            and self.approved_by_user_id is not None
            and self.esignature_manifest_id is not None
        )


class Part11AuditMixin(BaseModel):
    """A reusable Pydantic v2 mixin providing 21 CFR Part 11 compliant audit metadata."""

    created_at: AwareDatetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Chronological UTC timestamp when the record was created.",
    )
    created_by: str = Field(
        ...,
        description="Unique identifier of the user who created the record.",
    )
    reason_for_change: str = Field(
        ...,
        description="Mandatory explanation or audit justification for creating or mutating this record.",
    )
    version_index: int = Field(
        default=1,
        description="Row version counter or index.",
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason_for_change(cls, v: str) -> str:
        """Validate that the reason_for_change is a non-empty, non-blank string."""
        if not isinstance(v, str) or not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class AIGenerationManifest(Part11AuditMixin):
    """Audit ledger model capturing immutable generation parameters and cryptographic hashes for inspection."""

    manifest_id: str = Field(
        ...,
        description="Unique identifier or UUID for the AI generation manifest.",
    )
    model_identifier: str = Field(
        ...,
        description="Identifier of the model invoked (e.g. gpt-4o, claude-3-5-sonnet, meddra-embed-v1).",
    )
    model_version: str | None = Field(
        default=None,
        description="Specific model checkpoint or release version string.",
    )
    prompt_hash: str = Field(
        ...,
        description="Cryptographic SHA-256 hash of the input prompt.",
    )
    prompt_template_version: str | None = Field(
        default=None,
        description="Version tag of the prompt template used.",
    )
    hyperparameters: dict[str, Any] = Field(
        default_factory=dict,
        description="Sampling temperature, top_p, max_tokens, and other generation parameters.",
    )
    completion_hash: str = Field(
        ...,
        description="Cryptographic SHA-256 hash of the raw completion text.",
    )
    raw_completion: str = Field(
        ...,
        description="Exact output text or JSON string emitted by the AI engine.",
    )
    confidence_score: float = Field(
        ...,
        ge=0.0,
        le=1.0,
        description="Mathematical model confidence score (0.0 to 1.0).",
    )
    deid_applied: bool = Field(
        default=True,
        description="Flag indicating whether in-flight de-identification was applied to the prompt.",
    )
    deid_tokens_count: int = Field(
        default=0,
        ge=0,
        description="Count of surrogate PHI tokens replaced during in-flight air-gap scrubbing.",
    )


class AuditFields(Part11AuditMixin):
    """A reusable Pydantic v2 model/mixin containing standard 21 CFR Part 11 compliant audit and metadata fields."""

    pass


__all__ = [
    "AIAssistedRecordMixin",
    "AIGenerationManifest",
    "AIReviewStatus",
    "AuditFields",
    "Part11AuditMixin",
]
