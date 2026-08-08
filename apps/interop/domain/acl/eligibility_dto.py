"""Local Anti-Corruption Layer (ACL) Eligibility DTOs and evaluator for Interop service."""

import re
from enum import StrEnum
from typing import Any, Literal

from pydantic import BaseModel, Field, field_validator, model_validator


class ComparisonOperator(StrEnum):
    EQ = "=="
    NE = "!="
    LT = "<"
    LE = "<="
    GT = ">"
    GE = ">="


class LogicalOperator(StrEnum):
    AND = "and"
    OR = "or"
    NOT = "not"


FIELD_REF_RE = re.compile(r"^eCRF\.([a-zA-Z0-9_]+)\.([a-zA-Z0-9_]+)$")


class FieldReferenceDTO(BaseModel):
    raw_reference: str = Field(
        ..., description="Raw field reference, e.g. 'eCRF.DM.AGE'."
    )
    domain: str = Field(..., description="Target domain, e.g. 'DM'.")
    variable: str = Field(..., description="Target variable, e.g. 'AGE'.")

    @field_validator("raw_reference")
    @classmethod
    def validate_raw_reference(cls, v: str) -> str:
        match = FIELD_REF_RE.match(v)
        if not match:
            raise ValueError(
                f"Field reference '{v}' malformed. Expected 'eCRF.<DOMAIN>.<VARIABLE>'"
            )
        return v

    @model_validator(mode="after")
    def validate_internal_consistency(self) -> FieldReferenceDTO:
        match = FIELD_REF_RE.match(self.raw_reference)
        if match:
            dom, var = match.groups()
            if self.domain != dom or self.variable != var:
                raise ValueError("Domain/variable mismatch with raw_reference.")
        return self


class ExpressionNodeDTO(BaseModel):
    type: Literal["logical", "comparison", "field_ref", "constant"] = Field(...)
    operator: ComparisonOperator | LogicalOperator | str | None = Field(None)
    operands: list[ExpressionNodeDTO] | None = Field(None)
    value: Any | None = Field(None)
    field_ref: FieldReferenceDTO | None = Field(None)

    @model_validator(mode="after")
    def validate_node(self) -> ExpressionNodeDTO:
        if self.type == "field_ref" and self.field_ref is None:
            raise ValueError("Field reference node must provide field_ref.")
        if self.type == "logical":
            if self.operator not in ("and", "or", "not"):
                raise ValueError(f"Invalid logical operator: '{self.operator}'.")
            if not self.operands:
                raise ValueError(f"Logical node '{self.operator}' requires operands.")
        elif self.type == "comparison":
            if self.operator not in ("==", "!=", "<", "<=", ">", ">="):
                raise ValueError(f"Invalid comparison operator: '{self.operator}'.")
            if not self.operands or len(self.operands) != 2:
                raise ValueError(
                    f"Comparison node '{self.operator}' requires 2 operands."
                )
        return self


ExpressionNodeDTO.model_rebuild()


class EligibilityCriterionDTO(BaseModel):
    id: str = Field(default="")
    criterion_type: Literal["inclusion", "exclusion"] = Field(...)
    identifier: str = Field(default="")
    human_readable_text: str = Field(default="")
    dsl_expression_string: str = Field(default="")
    structured_expression_tree: ExpressionNodeDTO | None = Field(default=None)
    expected_outcome: bool = Field(True)

    # Backward-compatible fields
    criterion_id: str = Field(default="")
    description: str = Field(default="")
    dsl_source: str = Field(default="")
    condition: ExpressionNodeDTO | None = Field(default=None)
    created_by: str = Field(default="system")
    reason_for_change: str = Field(default="Initial setup")
    version_index: int = Field(default=1)

    @model_validator(mode="before")
    @classmethod
    def sync_compat_before(cls, data: Any) -> Any:
        if isinstance(data, dict):
            cid = data.get("criterion_id") or data.get("id") or data.get("identifier")
            if cid:
                data["id"] = data.get("id") or cid
                data["criterion_id"] = data.get("criterion_id") or cid
                data["identifier"] = data.get("identifier") or cid

            desc = data.get("description") or data.get("human_readable_text")
            if desc:
                data["description"] = data.get("description") or desc
                data["human_readable_text"] = data.get("human_readable_text") or desc

            dsl = data.get("dsl_source") or data.get("dsl_expression_string")
            if dsl:
                data["dsl_source"] = data.get("dsl_source") or dsl
                data["dsl_expression_string"] = data.get("dsl_expression_string") or dsl

            cond = data.get("condition") or data.get("structured_expression_tree")
            if cond:
                data["condition"] = data.get("condition") or cond
                data["structured_expression_tree"] = (
                    data.get("structured_expression_tree") or cond
                )
        return data

    @model_validator(mode="after")
    def sync_compat_after(self) -> EligibilityCriterionDTO:
        cid = self.id or self.criterion_id or self.identifier
        if cid:
            self.id = cid
            self.criterion_id = cid
            self.identifier = cid
        desc = self.human_readable_text or self.description
        if desc:
            self.human_readable_text = desc
            self.description = desc
        dsl = self.dsl_expression_string or self.dsl_source
        if dsl:
            self.dsl_expression_string = dsl
            self.dsl_source = dsl
        cond = self.structured_expression_tree or self.condition
        if cond:
            self.structured_expression_tree = cond
            self.condition = cond
        return self


class NodeEvaluationDTO(BaseModel):
    node_type: str
    operator: str | None = None
    value: Any | None = None
    is_indeterminate: bool = False
    explanation: str = ""
    children: list[NodeEvaluationDTO] = Field(default_factory=list)


NodeEvaluationDTO.model_rebuild()


class CriterionEvaluationDTO(BaseModel):
    criterion_id: str
    criterion_type: Literal["inclusion", "exclusion"]
    description: str
    dsl_source: str
    expected_outcome: bool
    evaluation_detail: NodeEvaluationDTO
    is_indeterminate: bool
    is_met: bool


class AggregateEligibilityResultDTO(BaseModel):
    eligible: bool | None = None
    failed_criteria: list[str] = Field(default_factory=list)
    indeterminate_criteria: list[str] = Field(default_factory=list)
    criteria_evaluations: dict[str, CriterionEvaluationDTO] = Field(
        default_factory=dict
    )


def parse_dsl(dsl_source: str) -> ExpressionNodeDTO:
    pattern = re.compile(
        r"^\s*(eCRF\.[A-Za-z0-9_]+\.[A-Za-z0-9_]+)\s*(==|!=|<=|>=|<|>)\s*(.+?)\s*$"
    )
    match = pattern.match(dsl_source)
    if not match:
        raise ValueError(f"Unable to parse DSL source: {dsl_source!r}")
    raw_ref, op, val_str = match.groups()
    parts = raw_ref.split(".")
    domain, var = parts[1], parts[2]
    ref = FieldReferenceDTO(raw_reference=raw_ref, domain=domain, variable=var)
    ref_node = ExpressionNodeDTO(type="field_ref", field_ref=ref)

    val_str_clean = val_str.strip()
    if val_str_clean.lower() == "true":
        val: Any = True
    elif val_str_clean.lower() == "false":
        val = False
    else:
        try:
            val = int(val_str_clean)
        except ValueError:
            try:
                val = float(val_str_clean)
            except ValueError:
                val = val_str_clean.strip("'\"")

    val_node = ExpressionNodeDTO(type="constant", value=val)
    return ExpressionNodeDTO(
        type="comparison",
        operator=op,
        operands=[ref_node, val_node],
    )


parse_dsl_dto = parse_dsl


def evaluate_node_dto(
    node: ExpressionNodeDTO, context: dict[str, Any]
) -> NodeEvaluationDTO:
    if node.type == "constant":
        return NodeEvaluationDTO(
            node_type="constant",
            value=node.value,
            explanation=f"Constant value {node.value!r}.",
        )
    if node.type == "field_ref":
        ref = node.field_ref
        val = context.get(ref.raw_reference) if ref else None
        is_indet = val is None
        expl = (
            f"Evaluated field '{ref.raw_reference}' -> {val!r}."
            if ref
            else "No field ref."
        )
        return NodeEvaluationDTO(
            node_type="field_ref",
            value=val,
            is_indeterminate=is_indet,
            explanation=expl,
        )
    if node.type == "comparison":
        left_eval = evaluate_node_dto(node.operands[0], context)
        right_eval = evaluate_node_dto(node.operands[1], context)
        if left_eval.is_indeterminate or right_eval.is_indeterminate:
            return NodeEvaluationDTO(
                node_type="comparison",
                operator=str(node.operator),
                is_indeterminate=True,
                explanation="Indeterminate comparison due to missing operand value.",
                children=[left_eval, right_eval],
            )
        l_val, r_val = left_eval.value, right_eval.value
        op = str(node.operator)
        res = False
        if op == "==":
            res = l_val == r_val
        elif op == "!=":
            res = l_val != r_val
        elif op == "<":
            res = l_val < r_val
        elif op == "<=":
            res = l_val <= r_val
        elif op == ">":
            res = l_val > r_val
        elif op == ">=":
            res = l_val >= r_val
        return NodeEvaluationDTO(
            node_type="comparison",
            operator=op,
            value=res,
            explanation=f"Compared {l_val!r} {op} {r_val!r} -> {res}.",
            children=[left_eval, right_eval],
        )
    if node.type == "logical":
        child_evals = [
            evaluate_node_dto(op_node, context) for op_node in (node.operands or [])
        ]
        op = str(node.operator)
        if op == "and":
            vals = [c.value for c in child_evals if not c.is_indeterminate]
            indets = [c for c in child_evals if c.is_indeterminate]
            res = all(vals) if vals else False
            is_indet = len(indets) > 0 and res is True
            return NodeEvaluationDTO(
                node_type="logical",
                operator="and",
                value=res,
                is_indeterminate=is_indet,
                explanation=f"Logical AND evaluated over {len(child_evals)} operands.",
                children=child_evals,
            )
        if op == "or":
            vals = [c.value for c in child_evals if not c.is_indeterminate]
            res = any(vals)
            return NodeEvaluationDTO(
                node_type="logical",
                operator="or",
                value=res,
                is_indeterminate=False,
                explanation=f"Logical OR evaluated over {len(child_evals)} operands.",
                children=child_evals,
            )
        if op == "not":
            c = child_evals[0]
            return NodeEvaluationDTO(
                node_type="logical",
                operator="not",
                value=not c.value if c.value is not None else None,
                is_indeterminate=c.is_indeterminate,
                explanation="Logical NOT evaluated.",
                children=child_evals,
            )
    return NodeEvaluationDTO(
        node_type="unknown", is_indeterminate=True, explanation="Unknown AST node type."
    )


def evaluate_eligibility(
    criteria: list[EligibilityCriterionDTO], context: dict[str, Any]
) -> AggregateEligibilityResultDTO:
    failed, indet, evals = [], [], {}
    for crit in criteria:
        cond_node = crit.structured_expression_tree or crit.condition
        if not cond_node:
            cid = crit.criterion_id or crit.id
            indet.append(cid)
            continue
        node_eval = evaluate_node_dto(cond_node, context)
        is_indet = node_eval.is_indeterminate
        is_met = (
            False
            if is_indet
            else (bool(node_eval.value) == bool(crit.expected_outcome))
        )
        cid = crit.criterion_id or crit.id
        if is_indet:
            indet.append(cid)
        elif not is_met:
            failed.append(cid)
        evals[cid] = CriterionEvaluationDTO(
            criterion_id=cid,
            criterion_type=crit.criterion_type,
            description=crit.description or crit.human_readable_text,
            dsl_source=crit.dsl_source or crit.dsl_expression_string,
            expected_outcome=crit.expected_outcome,
            evaluation_detail=node_eval,
            is_indeterminate=is_indet,
            is_met=is_met,
        )

    overall_eligible = (
        None if indet and not failed else (len(failed) == 0 and len(indet) == 0)
    )
    return AggregateEligibilityResultDTO(
        eligible=overall_eligible,
        failed_criteria=failed,
        indeterminate_criteria=indet,
        criteria_evaluations=evals,
    )


evaluate_eligibility_dto = evaluate_eligibility
EligibilityCriterion = EligibilityCriterionDTO
ExpressionNode = ExpressionNodeDTO
