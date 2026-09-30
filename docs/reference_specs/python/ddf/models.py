"""Data Contracts and Intermediate Representation for Digital Data Flow (DDF).

Defines strongly-typed Intermediate Representation (IR) models that decouple
protocol schema ingestion from XML layout rendering.
"""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field


class CompilerWarning(BaseModel):
    """Structured compiler warning emitted during ingestion or rule normalization."""

    field: str
    generated_value: Any = None
    warning_reason: str

    def to_dict(self) -> dict[str, Any]:
        """Convert warning to dictionary representation for database compatibility."""
        return {
            "field": self.field,
            "generated_value": self.generated_value,
            "warning_reason": self.warning_reason,
        }


class ItemLocalization(BaseModel):
    """Localized human-readable labels, descriptions, and instructions for an eCRF item."""

    label: str | None = None
    description: str | None = None
    hint: str | None = None

    def to_dict(self) -> dict[str, Any]:
        """Convert localization to dictionary."""
        return {
            "label": self.label,
            "description": self.description,
            "hint": self.hint,
        }


class DDFItemIR(BaseModel):
    """Intermediate representation of an individual clinical data collection element."""

    id: str
    name: str
    type: str = "string"
    appearance: str | None = None
    relevant: str | None = None
    constraint: str | None = None
    constraint_message: str | None = None
    localizations: dict[str, dict[str, Any]] = Field(default_factory=dict)

    def to_template_dict(self) -> dict[str, Any]:
        """Converts intermediate item into Jinja2 template context dictionary."""
        return {
            "id": self.id,
            "name": self.name,
            "type": self.type,
            "appearance": self.appearance,
            "relevant": self.relevant,
            "constraint": self.constraint,
            "constraint_message": self.constraint_message,
            "localizations": self.localizations,
        }


class DDFStudyIR(BaseModel):
    """Intermediate representation of a complete clinical study protocol form schema."""

    study_id: str
    name: str
    items: list[DDFItemIR] = Field(default_factory=list)
    version: str | None = None
    metadata: dict[str, Any] = Field(default_factory=dict)

    def to_template_dict(self) -> dict[str, Any]:
        """Converts intermediate study into Jinja2 template context dictionary."""
        return {
            "study_id": self.study_id,
            "name": self.name,
            "items": [item.to_template_dict() for item in self.items],
        }


class AccessibilityReport(BaseModel):
    """Outcome of layout rendering and automated WCAG accessibility evaluation."""

    status: str = "PASS"  # PASS or FAIL
    form_hash: str
    violations: list[dict[str, Any]] = Field(default_factory=list)
    passes: list[dict[str, Any]] = Field(default_factory=list)
    incomplete: list[dict[str, Any]] = Field(default_factory=list)
    inapplicable: list[dict[str, Any]] = Field(default_factory=list)
    layout_errors: list[str] = Field(default_factory=list)


class CompileOptions(BaseModel):
    """Runtime options governing the compilation pipeline."""

    validate_accessibility: bool = True
    default_language: str = "en"
    strict_schema_validation: bool = True


class DDFCompilationResult(BaseModel):
    """Final output artifact package emitted by the DDF Compiler Pipeline."""

    study_id: str
    name: str
    odm_xml: str
    openrosa_xml: str
    form_hash: str
    warnings: list[dict[str, Any]] = Field(default_factory=list)
    accessibility_report: AccessibilityReport | None = None
    status: str = "COMPLETED"
