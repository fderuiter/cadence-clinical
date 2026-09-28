"""Models and schemas for the Derived Variable Math Engine.

Governs standardized clinical formulas (eGFR, BSA, BMI, QTcF, MAP), physiological plausibility
boundary gating, and unit normalization under 21 CFR Part 11.
"""

import hashlib
import json
from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field

from packages.engine_core.rules.ast import ExpressionNode

DerivedFormulaType = Literal[
    "BMI",
    "BSA_MOSTELLER",
    "BSA_DUBOIS",
    "EGFR_CKD_EPI_2021",
    "EGFR_MDRD",
    "QTC_FRIDERICIA",
    "QTC_BAZETT",
    "MAP",
    "COCKCROFT_GAULT",
    "CUSTOM_AST",
]


class PlausibilityRange(BaseModel):
    """Physiological validation boundary for a medical input variable."""

    min_value: float | None = Field(
        default=None, description="Minimum clinically plausible value"
    )
    max_value: float | None = Field(
        default=None, description="Maximum clinically plausible value"
    )
    unit: str | None = Field(
        default=None, description="Expected unit for bounds checking"
    )
    warning_only: bool = Field(
        default=False,
        description="If True, emit a warning rather than failing the calculation entirely",
    )


class FormulaInputRequirement(BaseModel):
    """Specification of an input variable required for a derived calculation."""

    name: str = Field(
        ..., description="Canonical variable name (e.g. 'weight', 'height', 'age')"
    )
    unit: str | None = Field(
        default=None, description="Expected measurement unit (e.g. 'kg', 'cm')"
    )
    required: bool = Field(default=True)
    default_value: float | None = Field(default=None)
    plausibility: PlausibilityRange | None = Field(default=None)


class DerivedFormulaDefinition(BaseModel):
    """Declarative definition of a standardized or custom derived variable formula."""

    formula_id: str = Field(
        ..., description="Unique formula identifier (e.g. 'FORMULA-EGFR-2021')"
    )
    name: str = Field(..., description="Human-readable formula title")
    formula_type: DerivedFormulaType = Field(default="CUSTOM_AST")
    expression: ExpressionNode | None = Field(
        default=None,
        description="UREE AST node used when formula_type is CUSTOM_AST",
    )
    inputs: list[FormulaInputRequirement] = Field(default_factory=list)
    output_variable: str = Field(
        ..., description="Output variable identifier (e.g. 'calculated_bmi')"
    )
    output_unit: str | None = Field(
        default=None, description="Output measurement unit (e.g. 'kg/m^2')"
    )
    version_index: int = Field(default=1, ge=1)
    is_active: bool = Field(default=True)
    reason_for_change: str | None = Field(default=None)
    sha256_checksum: str | None = Field(default=None)

    def model_post_init(self, __context: Any) -> None:
        """Compute SHA-256 seal upon definition instantiation."""
        if not self.sha256_checksum:
            payload = {
                "formula_id": self.formula_id,
                "version_index": self.version_index,
                "formula_type": self.formula_type,
                "output_variable": self.output_variable,
                "inputs": [i.model_dump() for i in self.inputs],
                "expression": self.expression.model_dump() if self.expression else None,
            }
            raw = json.dumps(payload, sort_keys=True, default=str)
            object.__setattr__(
                self, "sha256_checksum", hashlib.sha256(raw.encode("utf-8")).hexdigest()
            )


class DerivedCalculationResult(BaseModel):
    """Result of a derived variable calculation with validation metadata."""

    formula_id: str
    output_variable: str
    calculated_value: float | None = None
    output_unit: str | None = None
    is_valid: bool = True
    error_message: str | None = None
    plausibility_violations: list[str] = Field(default_factory=list)
    input_values_used: dict[str, Any] = Field(default_factory=dict)
    eval_timestamp: str = Field(default_factory=lambda: datetime.now().isoformat())
