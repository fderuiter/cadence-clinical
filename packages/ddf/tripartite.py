"""Tripartite JSON Schema Architecture and Visual Metadata IDE Contracts.

Defines the decoupled tripartite schema standard:
1. Data Schema (schema.json): Variables, CDISC OIDs, Constraints
2. UI Schema (uischema.json): Layout grids, Widgets, Omni-channel viewports
3. Logic Schema (rules.json): AST edit checks, derived math, cross-visit dependencies

Also defines Patient Migration Matrix and Automated UAT Simulation contracts.
"""

from __future__ import annotations

from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field


class ClinicalDataType(StrEnum):
    """Supported CDISC clinical data types."""

    STRING = "string"
    INTEGER = "integer"
    FLOAT = "float"
    DATE = "date"
    DATETIME = "datetime"
    TIME = "time"
    BOOLEAN = "boolean"
    CODELIST = "codelist"
    TEXT = "text"
    REPEAT_LOG = "repeat_log"


class WidgetType(StrEnum):
    """UI Widget representations for the headless renderer."""

    TEXT_INPUT = "text_input"
    TEXTAREA = "textarea"
    NUMBER_INPUT = "number_input"
    RADIO_GROUP = "radio_group"
    SELECT_DROPDOWN = "select_dropdown"
    MULTI_SELECT = "multi_select"
    DATE_PICKER = "date_picker"
    TIME_PICKER = "time_picker"
    SLIDER_VAS = "slider_vas"
    BODY_MAP_COORDINATE = "body_map_coordinate"
    REPEATING_LOG_GRID = "repeating_log_grid"
    MEDICAL_AUTOCODE = "medical_autocode"
    SENSOR_STREAM_CARD = "sensor_stream_card"


class ViewportMode(StrEnum):
    """Omni-channel preview modes."""

    DESKTOP_EDC = "desktop_edc"
    MOBILE_EPRO = "mobile_epro"
    TABLET_ESOURCE = "tablet_esource"


class MigrationMode(StrEnum):
    """In-flight patient migration policies during protocol amendments."""

    PROSPECTIVE = "prospective"
    RETROSPECTIVE = "retrospective"


class CdiscMetadata(BaseModel):
    """CDISC CDASH/SDTM variable mapping annotations."""

    domain: str = "VS"
    variable_name: str = "VSORRES"
    oid: str
    codelist_oid: str | None = None
    ucum_unit: str | None = None
    core_designation: str = "Req"  # Req, Exp, Perm
    sdtm_target: str | None = None


class ConstraintDefinition(BaseModel):
    """Value-level data validation constraints."""

    required: bool = True
    min_value: float | None = None
    max_value: float | None = None
    min_length: int | None = None
    max_length: int | None = None
    regex_pattern: str | None = None
    allowed_values: list[str] | None = None


class VariableDefinition(BaseModel):
    """Data Schema element defining a single clinical collection variable."""

    id: str
    name: str
    data_type: ClinicalDataType = ClinicalDataType.STRING
    cdisc: CdiscMetadata
    constraints: ConstraintDefinition = Field(default_factory=ConstraintDefinition)
    phi_pii_masked: bool = False
    sdv_tier: str = "TSDV_REPRESENTATIVE"  # SDV_100, TSDV_REPRESENTATIVE, NO_SDV
    part11_reason_required: bool = True


class DataSchema(BaseModel):
    """Pillar 1: Data Schema (schema.json) - Defines what is collected."""

    schema_version: str = "2.0"
    form_id: str
    name: str
    cdisc_form_oid: str
    variables: list[VariableDefinition] = Field(default_factory=list)


class FieldWidgetConfig(BaseModel):
    """UI presentation options for a single variable field."""

    variable_id: str
    widget: WidgetType = WidgetType.TEXT_INPUT
    grid_column_span: int = 12  # 1 to 12
    placeholder: str | None = None
    help_text: str | None = None
    css_class: str | None = None
    readonly: bool = False
    hidden: bool = False


class GridSection(BaseModel):
    """Layout container section in the UI Schema."""

    id: str
    title: str
    columns: int = 12
    widgets: list[FieldWidgetConfig] = Field(default_factory=list)


class UiSchema(BaseModel):
    """Pillar 1: UI Schema (uischema.json) - Defines how it looks."""

    schema_version: str = "2.0"
    form_id: str
    sections: list[GridSection] = Field(default_factory=list)
    default_viewport: ViewportMode = ViewportMode.DESKTOP_EDC
    theme_tokens: dict[str, str] = Field(default_factory=dict)


class AstEditCheck(BaseModel):
    """Isomorphic AST validation and query firing rule."""

    id: str
    name: str
    ast_expression: dict[str, Any]  # JSONLogic / CEL AST
    severity: str = "HARD"  # HARD (prevents save), SOFT (warning query), INFO
    query_message: str
    target_variable_ids: list[str] = Field(default_factory=list)


class DerivedMathRule(BaseModel):
    """Reactive formula derivation rule."""

    id: str
    target_variable_id: str
    formula_expression: str  # e.g. "weight / ((height / 100) ^ 2)"
    input_variable_ids: list[str] = Field(default_factory=list)
    unit_conversion: dict[str, float] = Field(default_factory=dict)


class CrossVisitDependency(BaseModel):
    """Horizontal journey logic linking multiple visits/encounters."""

    id: str
    trigger_visit_id: str
    trigger_variable_id: str
    trigger_condition: dict[str, Any]
    target_visit_ids_to_spawn: list[str] = Field(default_factory=list)
    target_visit_ids_to_suppress: list[str] = Field(default_factory=list)


class LogicSchema(BaseModel):
    """Pillar 1: Logic Schema (rules.json) - Defines how it behaves."""

    schema_version: str = "2.0"
    form_id: str
    edit_checks: list[AstEditCheck] = Field(default_factory=list)
    derived_rules: list[DerivedMathRule] = Field(default_factory=list)
    cross_visit_dependencies: list[CrossVisitDependency] = Field(default_factory=list)


class TripartiteFormDefinition(BaseModel):
    """The unified, versioned Tripartite Clinical Form Document."""

    form_id: str
    protocol_id: str
    version_index: int = 1
    version_tag: str = "1.0.0"
    data_schema: DataSchema
    ui_schema: UiSchema
    logic_schema: LogicSchema
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    created_by: str = "system"
    reason_for_change: str = "Initial eCRF baseline release"


class MigrationDirective(BaseModel):
    """Action directive for an individual clinical field in an amendment."""

    variable_id: str
    action: str  # ADD, REMOVE, MODIFY_CONSTRAINT, MODIFY_RULE
    migration_mode: MigrationMode = MigrationMode.PROSPECTIVE
    backfill_query_message: str | None = None


class PatientMigrationMatrix(BaseModel):
    """Pillar 6: In-Flight Patient Migration Matrix for Protocol Amendments."""

    protocol_id: str
    from_version_tag: str
    to_version_tag: str
    directives: list[MigrationDirective] = Field(default_factory=list)
    effective_date: datetime = Field(default_factory=lambda: datetime.now(UTC))


class SyntheticPayload(BaseModel):
    """AI-generated synthetic test payload targeting rule boundaries."""

    payload_id: str
    test_scenario: str
    data: dict[str, Any]
    expected_rule_outcomes: dict[str, bool]  # rule_id -> expected_fired


class UatEvidenceReport(BaseModel):
    """Pillar 5: Automated 21 CFR Part 11 UAT Evidence Report."""

    report_id: str
    form_id: str
    protocol_id: str
    executed_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    total_scenarios_evaluated: int
    scenarios_passed: int
    scenarios_failed: int
    rule_coverage_percentage: float
    execution_duration_ms: float
    gxp_compliance_verified: bool = True
    sha256_evidence_digest: str
