"""
Shared AST, eligibility criteria, and deterministic evaluation domain contracts.

This module provides Pydantic v2 models for inclusion/exclusion criteria,
structured AST expression trees, and comprehensive detailed node/aggregate
evaluation outputs. All models conform to FDA 21 CFR Part 11 auditing principles.
"""

import re
from enum import StrEnum
from typing import Any, Literal

from pydantic import BaseModel, Field, field_validator, model_validator

# Import standard GxP audit fields
from packages.database.audit import Part11AuditMixin


class ComparisonOperator(StrEnum):
    """
    Allowed binary comparison operators for criteria evaluations.
    """

    EQ = "=="
    NE = "!="
    LT = "<"
    LE = "<="
    GT = ">"
    GE = ">="


class LogicalOperator(StrEnum):
    """
    Allowed logical connectors for composite criteria expressions.
    """

    AND = "and"
    OR = "or"
    NOT = "not"


# Regex pattern for eCRF.<DOMAIN>.<VARIABLE> references
FIELD_REF_RE = re.compile(r"^eCRF\.([a-zA-Z0-9_]+)\.([a-zA-Z0-9_]+)$")


class FieldReference(BaseModel):
    """
    Represents a structured field reference pointing to an eCRF domain variable.
    Format must strictly follow: eCRF.<DOMAIN>.<VARIABLE>
    """

    raw_reference: str = Field(
        ...,
        description="Raw field reference string, e.g., 'eCRF.DM.AGE'.",
    )
    domain: str = Field(
        ...,
        description="The target eCRF domain, e.g., 'DM'.",
    )
    variable: str = Field(
        ...,
        description="The domain variable, e.g., 'AGE'.",
    )

    @field_validator("raw_reference")
    @classmethod
    def validate_raw_reference(cls, v: str) -> str:
        """
        Validate that the raw reference string strictly follows the 'eCRF.<DOMAIN>.<VARIABLE>' format.
        """
        match = FIELD_REF_RE.match(v)
        if not match:
            raise ValueError(
                f"Field reference '{v}' is malformed. Must strictly follow format: 'eCRF.<DOMAIN>.<VARIABLE>'"
            )
        return v

    @model_validator(mode="after")
    def validate_internal_consistency(self) -> FieldReference:
        """
        Ensure that the domain and variable match the components extracted from raw_reference.
        """
        match = FIELD_REF_RE.match(self.raw_reference)
        if match:
            dom, var = match.groups()
            if self.domain != dom or self.variable != var:
                raise ValueError(
                    f"Domain/variable mismatch. raw_reference={self.raw_reference!r} suggests domain={dom!r}, variable={var!r}."
                )
        return self


class ExpressionNode(BaseModel):
    """
    Recursive node inside a structured clinical expression tree (AST).
    Supported types are: logical, comparison, field_ref, constant.
    """

    type: Literal["logical", "comparison", "field_ref", "constant"] = Field(
        ...,
        description="Node type indicating the structure of the node.",
    )
    operator: ComparisonOperator | LogicalOperator | str | None = Field(
        None,
        description="Operator for logical (and, or, not) or comparison (==, !=, <, <=, >, >=) nodes.",
    )
    operands: list[ExpressionNode] | None = Field(
        None,
        description="Child operands of logical or comparison nodes.",
    )
    value: Any | None = Field(
        None,
        description="Literal constant value of type constant.",
    )
    field_ref: FieldReference | None = Field(
        None,
        description="Field reference details of type field_ref.",
    )

    @model_validator(mode="after")
    def validate_node(self) -> ExpressionNode:
        """
        Validate node contents based on its type.
        """
        if self.type == "constant":
            # value can be any literal (including None / boolean False)
            pass
        elif self.type == "field_ref":
            if self.field_ref is None:
                raise ValueError("Field reference node must provide 'field_ref'.")
        elif self.type == "logical":
            if self.operator not in ("and", "or", "not"):
                raise ValueError(f"Invalid logical operator: '{self.operator}'.")
            if not self.operands:
                raise ValueError(f"Logical node '{self.operator}' requires operands.")
            if self.operator == "not" and len(self.operands) != 1:
                raise ValueError("Logical 'not' operator requires exactly 1 operand.")
            if self.operator in ("and", "or") and len(self.operands) < 2:
                raise ValueError(
                    f"Logical '{self.operator}' operator requires at least 2 operands."
                )
        elif self.type == "comparison":
            if self.operator not in ("==", "!=", "<", "<=", ">", ">="):
                raise ValueError(f"Invalid comparison operator: '{self.operator}'.")
            if not self.operands or len(self.operands) != 2:
                raise ValueError(
                    f"Comparison operator '{self.operator}' requires exactly 2 operands."
                )
        return self


# Rebuild recursive models in Pydantic v2
ExpressionNode.model_rebuild()


class EligibilityCriterion(Part11AuditMixin):
    """
    Represents a single inclusion or exclusion criterion with full GxP audit metadata.
    """

    id: str = Field(
        default="",
        description="Unique identifier of this eligibility criterion, e.g., 'INC_01'.",
    )
    criterion_type: Literal["inclusion", "exclusion"] = Field(
        ...,
        description="Whether this is an inclusion or exclusion criterion.",
    )
    identifier: str = Field(
        default="",
        description="Business identifier of this criterion, e.g., 'INC-001'.",
    )
    human_readable_text: str = Field(
        default="",
        description="Human-readable text description of the criterion.",
    )
    dsl_expression_string: str = Field(
        default="",
        description="The raw DSL statement source, e.g., 'eCRF.DM.AGE >= 18'.",
    )
    structured_expression_tree: ExpressionNode = Field(
        default=None,  # type: ignore
        description="The parsed structured AST of this criterion.",
    )
    expected_outcome: bool = Field(
        True,
        description="Expected Boolean outcome of evaluating the condition node. "
        "Typically True for inclusions and False for exclusions.",
    )

    # For backward compatibility with existing code/tests:
    criterion_id: str = Field(
        default="",
        description="Backward compatible criterion_id field.",
    )
    description: str = Field(
        default="",
        description="Backward compatible description field.",
    )
    dsl_source: str = Field(
        default="",
        description="Backward compatible dsl_source field.",
    )
    condition: ExpressionNode = Field(
        default=None,  # type: ignore
        description="Backward compatible condition field.",
    )

    @model_validator(mode="before")
    @classmethod
    def sync_backwards_compatibility_before(cls, data: Any) -> Any:
        if isinstance(data, dict):
            # Resolve id / criterion_id / identifier
            cid = data.get("criterion_id") or data.get("id") or data.get("identifier")
            if cid:
                if "id" not in data or not data["id"]:
                    data["id"] = cid
                if "criterion_id" not in data or not data["criterion_id"]:
                    data["criterion_id"] = cid
                if "identifier" not in data or not data["identifier"]:
                    data["identifier"] = cid

            # Resolve description / human_readable_text
            desc = data.get("description") or data.get("human_readable_text")
            if desc:
                if "description" not in data or not data["description"]:
                    data["description"] = desc
                if "human_readable_text" not in data or not data["human_readable_text"]:
                    data["human_readable_text"] = desc

            # Resolve dsl_source / dsl_expression_string
            dsl = data.get("dsl_source") or data.get("dsl_expression_string")
            if dsl:
                if "dsl_source" not in data or not data["dsl_source"]:
                    data["dsl_source"] = dsl
                if (
                    "dsl_expression_string" not in data
                    or not data["dsl_expression_string"]
                ):
                    data["dsl_expression_string"] = dsl

            # Resolve condition / structured_expression_tree
            cond = data.get("condition") or data.get("structured_expression_tree")
            if cond:
                if "condition" not in data or not data["condition"]:
                    data["condition"] = cond
                if (
                    "structured_expression_tree" not in data
                    or not data["structured_expression_tree"]
                ):
                    data["structured_expression_tree"] = cond

        return data

    @model_validator(mode="after")
    def sync_backwards_compatibility_after(self) -> EligibilityCriterion:
        # Ensure all fields are fully synchronized on the instance
        cid = self.id or self.criterion_id or self.identifier
        if cid:
            self.id = cid
            self.criterion_id = cid
            self.identifier = cid
        else:
            raise ValueError(
                "Criterion unique identifier (id / criterion_id / identifier) must be provided."
            )

        desc = self.human_readable_text or self.description
        if desc:
            self.human_readable_text = desc
            self.description = desc
        else:
            raise ValueError(
                "Criterion description (human_readable_text / description) must be provided."
            )

        dsl = self.dsl_expression_string or self.dsl_source
        if dsl:
            self.dsl_expression_string = dsl
            self.dsl_source = dsl
        else:
            raise ValueError(
                "Criterion DSL expression string (dsl_expression_string / dsl_source) must be provided."
            )

        cond = self.structured_expression_tree or self.condition
        if cond:
            self.structured_expression_tree = cond
            self.condition = cond
        else:
            raise ValueError(
                "Criterion structured expression tree (structured_expression_tree / condition) must be provided."
            )

        return self


class NodeEvaluation(BaseModel):
    """
    Detailed evaluation output for a single node inside the AST expression tree.
    Provides complete node-level traceability for regulatory compliance.
    """

    node_type: str = Field(
        ...,
        description="The type of AST node that was evaluated.",
    )
    operator: str | None = Field(
        None,
        description="The operator utilized during evaluation.",
    )
    value: Any | None = Field(
        None,
        description="The evaluated literal value of the node, if determined.",
    )
    is_indeterminate: bool = Field(
        False,
        description="Indicates if the evaluation is indeterminate (e.g. due to missing or null data).",
    )
    explanation: str = Field(
        ...,
        description="Trace explanation detailing how this node evaluated to its outcome.",
    )
    children: list[NodeEvaluation] = Field(
        default_factory=list,
        description="Child node evaluation details.",
    )


NodeEvaluation.model_rebuild()


class CriterionEvaluation(BaseModel):
    """
    Evaluation summary for a single eligibility criterion.
    """

    criterion_id: str = Field(
        ...,
        description="The identifier of the criterion evaluated.",
    )
    criterion_type: Literal["inclusion", "exclusion"] = Field(
        ...,
        description="Whether this is an inclusion or exclusion criterion.",
    )
    description: str = Field(
        ...,
        description="Human-readable description of the criterion.",
    )
    dsl_source: str = Field(
        ...,
        description="Raw DSL source string of the criterion.",
    )
    expected_outcome: bool = Field(
        ...,
        description="Expected Boolean outcome of the condition evaluation.",
    )
    evaluation_detail: NodeEvaluation = Field(
        ...,
        description="The recursive evaluation trace tree of the condition.",
    )
    is_indeterminate: bool = Field(
        ...,
        description="Indicates if the evaluation was indeterminate.",
    )
    is_met: bool = Field(
        ...,
        description="Indicates if the subject satisfies this criterion.",
    )


class AggregateEligibilityResult(BaseModel):
    """
    Aggregated eligibility outcome over a set of inclusion/exclusion criteria.
    """

    eligible: bool | None = Field(
        None,
        description="Aggregated eligibility. True if all criteria are met. "
        "False if any criterion failed. None if indeterminate and no hard failures exist.",
    )
    failed_criteria: list[str] = Field(
        default_factory=list,
        description="List of criterion IDs that failed evaluation.",
    )
    indeterminate_criteria: list[str] = Field(
        default_factory=list,
        description="List of criterion IDs that were indeterminate due to missing/null values.",
    )
    criteria_evaluations: dict[str, CriterionEvaluation] = Field(
        default_factory=dict,
        description="A map of detailed evaluation results keyed by criterion ID.",
    )
