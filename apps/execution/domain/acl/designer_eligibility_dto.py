"""Anti-Corruption Layer DTOs for eligibility criteria received from Designer Service."""

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field, model_validator


class FieldReferenceDTO(BaseModel):
    """Structured field reference DTO."""

    raw_reference: str = Field(
        ..., description="Raw field reference (e.g. 'eCRF.DM.AGE')."
    )
    domain: str = Field(default="", description="Domain name (e.g. 'DM').")
    variable: str = Field(default="", description="Variable name (e.g. 'AGE').")

    @model_validator(mode="before")
    @classmethod
    def sync_ref_before(cls, data: Any) -> Any:
        if isinstance(data, dict):
            raw = data.get("raw_reference") or data.get("field_id") or ""
            if raw and not data.get("raw_reference"):
                data["raw_reference"] = raw
            if raw.startswith("eCRF.") and len(raw.split(".")) == 3:
                parts = raw.split(".")
                data.setdefault("domain", parts[1])
                data.setdefault("variable", parts[2])
        return data


class DesignerExpressionNodeDTO(BaseModel):
    """AST node representation inside structured eligibility expressions."""

    type: Literal["logical", "comparison", "field_ref", "constant"] = Field(
        ..., description="Node type of the AST expression tree."
    )
    operator: str | None = Field(
        None,
        description="Logical ('and', 'or', 'not') or comparison ('==', '!=', '<', '<=', '>', '>=') operator.",
    )
    operands: list[DesignerExpressionNodeDTO] | None = Field(
        None, description="Child operands of logical or comparison nodes."
    )
    value: Any | None = Field(
        None, description="Literal constant value when type is 'constant'."
    )
    field_ref: FieldReferenceDTO | dict[str, Any] | None = Field(
        None,
        description="Structured field reference object when type is 'field_ref'.",
    )

    @model_validator(mode="after")
    def convert_field_ref(self) -> DesignerExpressionNodeDTO:
        if isinstance(self.field_ref, dict):
            self.field_ref = FieldReferenceDTO(**self.field_ref)
        return self


DesignerExpressionNodeDTO.model_rebuild()


class DesignerEligibilityCriterionDTO(BaseModel):
    """Local Execution ACL DTO representing eligibility criterion from Designer Service."""

    id: str = Field(
        default="", description="Unique identifier of the criterion (e.g. 'INC_01')."
    )
    criterion_type: Literal["inclusion", "exclusion"] = Field(
        ..., description="Whether this criterion is inclusion or exclusion."
    )
    identifier: str = Field(
        default="", description="Business identifier (e.g. 'INC-001')."
    )
    human_readable_text: str = Field(
        default="", description="Human-readable criterion text description."
    )
    dsl_expression_string: str = Field(
        default="", description="Raw DSL statement source (e.g. 'eCRF.DM.AGE >= 18')."
    )
    structured_expression_tree: DesignerExpressionNodeDTO | None = Field(
        default=None, description="Parsed AST expression tree."
    )
    expected_outcome: bool = Field(
        default=True,
        description="Expected outcome (True for inclusion, False for exclusion).",
    )

    # Legacy/Compatibility Aliases
    criterion_id: str = Field(default="", description="Alias for id/identifier.")
    description: str = Field(default="", description="Alias for human_readable_text.")
    dsl_source: str = Field(default="", description="Alias for dsl_expression_string.")
    condition: DesignerExpressionNodeDTO | None = Field(
        default=None, description="Alias for structured_expression_tree."
    )

    # GxP Audit Trail Fields
    created_at: datetime | None = Field(None, description="Creation timestamp.")
    created_by: str = Field(
        "designer", description="User or service that created criterion."
    )
    reason_for_change: str = Field(
        "Initial definition", description="GxP change justification."
    )
    version_index: int = Field(1, description="Version index of criterion.")

    @model_validator(mode="before")
    @classmethod
    def sync_aliases_before(cls, data: Any) -> Any:
        if isinstance(data, dict):
            cid = data.get("criterion_id") or data.get("id") or data.get("identifier")
            if cid:
                data.setdefault("id", cid)
                data.setdefault("criterion_id", cid)
                data.setdefault("identifier", cid)

            desc = data.get("description") or data.get("human_readable_text")
            if desc:
                data.setdefault("description", desc)
                data.setdefault("human_readable_text", desc)

            dsl = data.get("dsl_source") or data.get("dsl_expression_string")
            if dsl:
                data.setdefault("dsl_source", dsl)
                data.setdefault("dsl_expression_string", dsl)

            cond = data.get("condition") or data.get("structured_expression_tree")
            if cond:
                data.setdefault("condition", cond)
                data.setdefault("structured_expression_tree", cond)

        return data


class NodeEvaluationDTO(BaseModel):
    """Traceability detail node evaluation."""

    node_type: str
    operator: str | None = None
    value: Any | None = None
    is_indeterminate: bool = False
    explanation: str = ""
    children: list[NodeEvaluationDTO] = Field(default_factory=list)


NodeEvaluationDTO.model_rebuild()


class CriterionEvaluationDTO(BaseModel):
    """Criterion level evaluation detail."""

    criterion_id: str
    criterion_type: Literal["inclusion", "exclusion"]
    description: str
    dsl_source: str
    expected_outcome: bool
    evaluation_detail: NodeEvaluationDTO
    is_indeterminate: bool
    is_met: bool


class AggregateEligibilityResultDTO(BaseModel):
    """Local Execution ACL DTO for aggregated subject eligibility evaluation results."""

    eligible: bool | None = Field(
        None,
        description="True if all criteria met; False if any failed; None if indeterminate without failures.",
    )
    failed_criteria: list[str] = Field(
        default_factory=list, description="List of failed criterion IDs."
    )
    indeterminate_criteria: list[str] = Field(
        default_factory=list, description="List of indeterminate criterion IDs."
    )
    criteria_evaluations: dict[str, Any] = Field(
        default_factory=dict,
        description="Map of detailed evaluations keyed by criterion ID.",
    )


def evaluate_node_dto(
    node: DesignerExpressionNodeDTO, context: dict[str, Any]
) -> NodeEvaluationDTO:
    """Evaluates AST node using Kleene logic."""
    if node.type == "constant":
        return NodeEvaluationDTO(
            node_type="constant",
            value=node.value,
            is_indeterminate=False,
            explanation=f"Constant value is {node.value!r}.",
        )

    if node.type == "field_ref":
        ref = node.field_ref
        raw_ref = None
        if isinstance(ref, FieldReferenceDTO):
            raw_ref = ref.raw_reference
        elif isinstance(ref, dict):
            raw_ref = ref.get("raw_reference") or ref.get("field_id")
        if not raw_ref or raw_ref not in context or context[raw_ref] is None:
            return NodeEvaluationDTO(
                node_type="field_ref",
                value=None,
                is_indeterminate=True,
                explanation=f"Field {raw_ref!r} missing or null.",
            )
        val = context[raw_ref]
        return NodeEvaluationDTO(
            node_type="field_ref",
            value=val,
            is_indeterminate=False,
            explanation=f"Field {raw_ref!r} is {val!r}.",
        )

    if node.type == "logical":
        if not node.operands:
            return NodeEvaluationDTO(
                node_type="logical",
                operator=node.operator,
                is_indeterminate=True,
                explanation="No operands.",
            )
        child_evals = [evaluate_node_dto(op, context) for op in node.operands]
        if node.operator == "not":
            c = child_evals[0]
            if c.is_indeterminate:
                return NodeEvaluationDTO(
                    node_type="logical",
                    operator="not",
                    is_indeterminate=True,
                    explanation="Indeterminate NOT.",
                    children=child_evals,
                )
            return NodeEvaluationDTO(
                node_type="logical",
                operator="not",
                value=not c.value,
                is_indeterminate=False,
                explanation=f"not {c.value!r}.",
                children=child_evals,
            )
        if node.operator == "and":
            has_indet = False
            for c in child_evals:
                if not c.is_indeterminate and c.value is False:
                    return NodeEvaluationDTO(
                        node_type="logical",
                        operator="and",
                        value=False,
                        is_indeterminate=False,
                        explanation="AND short-circuited by False.",
                        children=child_evals,
                    )
                if c.is_indeterminate:
                    has_indet = True
            if has_indet:
                return NodeEvaluationDTO(
                    node_type="logical",
                    operator="and",
                    is_indeterminate=True,
                    explanation="AND indeterminate.",
                    children=child_evals,
                )
            return NodeEvaluationDTO(
                node_type="logical",
                operator="and",
                value=True,
                is_indeterminate=False,
                explanation="AND True.",
                children=child_evals,
            )
        if node.operator == "or":
            has_indet = False
            for c in child_evals:
                if not c.is_indeterminate and c.value is True:
                    return NodeEvaluationDTO(
                        node_type="logical",
                        operator="or",
                        value=True,
                        is_indeterminate=False,
                        explanation="OR short-circuited by True.",
                        children=child_evals,
                    )
                if c.is_indeterminate:
                    has_indet = True
            if has_indet:
                return NodeEvaluationDTO(
                    node_type="logical",
                    operator="or",
                    is_indeterminate=True,
                    explanation="OR indeterminate.",
                    children=child_evals,
                )
            return NodeEvaluationDTO(
                node_type="logical",
                operator="or",
                value=False,
                is_indeterminate=False,
                explanation="OR False.",
                children=child_evals,
            )

    if node.type == "comparison":
        if not node.operands or len(node.operands) != 2:
            return NodeEvaluationDTO(
                node_type="comparison",
                operator=node.operator,
                is_indeterminate=True,
                explanation="Comparison needs 2 operands.",
            )
        l_eval = evaluate_node_dto(node.operands[0], context)
        r_eval = evaluate_node_dto(node.operands[1], context)
        children = [l_eval, r_eval]
        if l_eval.is_indeterminate or r_eval.is_indeterminate:
            return NodeEvaluationDTO(
                node_type="comparison",
                operator=node.operator,
                is_indeterminate=True,
                explanation="Missing operand value.",
                children=children,
            )
        l_val, r_val = l_eval.value, r_eval.value
        op = node.operator

        l_coerced = l_val
        r_coerced = r_val

        if isinstance(l_val, str):
            try:
                l_coerced = float(l_val)
            except (ValueError, TypeError):
                pass
        if isinstance(r_val, str):
            try:
                r_coerced = float(r_val)
            except (ValueError, TypeError):
                pass

        try:
            res = False
            if op == "==":
                res = l_coerced == r_coerced
            elif op == "!=":
                res = l_coerced != r_coerced
            elif op == "<":
                res = l_coerced < r_coerced
            elif op == "<=":
                res = l_coerced <= r_coerced
            elif op == ">":
                res = l_coerced > r_coerced
            elif op == ">=":
                res = l_coerced >= r_coerced
            else:
                return NodeEvaluationDTO(
                    node_type="comparison",
                    operator=op,
                    is_indeterminate=True,
                    explanation=f"Unsupported comparison operator: {op!r}.",
                    children=children,
                )
        except TypeError as err:
            explanation = (
                f"Comparison failed due to incompatible operand types: "
                f"{type(l_val).__name__} and {type(r_val).__name__}. Details: {err}"
            )
            return NodeEvaluationDTO(
                node_type="comparison",
                operator=op,
                value=None,
                is_indeterminate=True,
                explanation=explanation,
                children=children,
            )

        return NodeEvaluationDTO(
            node_type="comparison",
            operator=op,
            value=res,
            is_indeterminate=False,
            explanation=f"{l_val!r} {op} {r_val!r} -> {res}.",
            children=children,
        )

    return NodeEvaluationDTO(
        node_type="unknown", is_indeterminate=True, explanation="Unknown AST node type."
    )


def evaluate_eligibility_dto(
    criteria: list[DesignerEligibilityCriterionDTO], context: dict[str, Any]
) -> AggregateEligibilityResultDTO:
    """Aggregates eligibility for Execution service using local ACL DTOs."""
    failed = []
    indet = []
    evaluations = {}

    for crit in criteria:
        cond = crit.structured_expression_tree or crit.condition
        if not cond:
            indet.append(crit.criterion_id or crit.id)
            continue

        node_eval = evaluate_node_dto(cond, context)
        cid = crit.criterion_id or crit.id or crit.identifier
        if node_eval.is_indeterminate:
            is_met = False
            is_indet = True
            indet.append(cid)
        else:
            is_indet = False
            is_met = bool(node_eval.value) == bool(crit.expected_outcome)
            if not is_met:
                failed.append(cid)

        evaluations[cid] = CriterionEvaluationDTO(
            criterion_id=cid,
            criterion_type=crit.criterion_type,
            description=crit.description or crit.human_readable_text,
            dsl_source=crit.dsl_source or crit.dsl_expression_string,
            expected_outcome=crit.expected_outcome,
            evaluation_detail=node_eval,
            is_indeterminate=is_indet,
            is_met=is_met,
        )

    if len(failed) > 0:
        eligible = False
    elif len(indet) > 0:
        eligible = None
    else:
        eligible = True

    return AggregateEligibilityResultDTO(
        eligible=eligible,
        failed_criteria=failed,
        indeterminate_criteria=indet,
        criteria_evaluations=evaluations,
    )
