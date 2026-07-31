"""
USDM Validator Module (Sibling to apps/designer/validator.py)

This module implements the USDM schema validation with typed reports (Task 3).
It validates normalized USDM payload structures by:
1. Instantiating the official `usdm_model.Study` Pydantic model.
2. Layering additional structural/business checks (required study/version/design elements),
   mirroring the style of `apps/execution/cdisc_validator.py`.
3. Returning a Pydantic v2 validation report.
"""

from typing import Any, Dict, List, Optional
import usdm_model
from pydantic import BaseModel, Field, ValidationError
from apps.designer.rules import detect_circular_dependencies


class ValidationIssue(BaseModel):
    """
    Represents an individual validation error or warning.
    """
    field: Optional[str] = None
    reason: str
    value: Optional[str] = None


class USDMValidationReport(BaseModel):
    """
    USDM ingestion validation report.
    """
    version: str = Field(..., description="Resolved USDM version ('v2' or 'v3')")
    format: str = Field(..., description="Detected file format ('JSON' or 'YAML')")
    validity: bool = Field(..., description="True if payload is completely valid")
    errors: List[ValidationIssue] = Field(
        default_factory=list, description="Validation errors"
    )
    warnings: List[ValidationIssue] = Field(
        default_factory=list, description="Validation warnings"
    )
    version_resolution_evidence: List[str] = Field(
        default_factory=list, description="Evidence used to resolve version"
    )


def traverse_rules_in_payload(
    normalized_payload: Dict[str, Any],
) -> List[Dict[str, Any]]:
    """
    Extracts all rule dictionaries (top-level and activity-level) from the normalized payload.
    """
    rules = []
    # Top-level rules
    top_rules = normalized_payload.get("rules", [])
    if isinstance(top_rules, list):
        for r in top_rules:
            if isinstance(r, dict):
                rules.append(r)

    # Nested rules in activities
    versions = normalized_payload.get("versions", [])
    if isinstance(versions, list):
        for ver in versions:
            if not isinstance(ver, dict):
                continue
            designs = ver.get("studyDesigns", [])
            if isinstance(designs, list):
                for design in designs:
                    if not isinstance(design, dict):
                        continue
                    activities = design.get("activities", [])
                    if isinstance(activities, list):
                        for act in activities:
                            if not isinstance(act, dict):
                                continue
                            act_rules = act.get("rules", [])
                            if isinstance(act_rules, list):
                                for r in act_rules:
                                    if isinstance(r, dict):
                                        rules.append(r)
    return rules


def detect_stochastic_operators(node: Any) -> List[str]:
    """
    Recursively check condition nodes for complex/stochastic math operators or invalid function names.
    Returns a list of unsupported operator messages.
    """
    failures = []
    if not isinstance(node, dict):
        return failures

    op = node.get("operator")

    if op:
        allowed_operators = {
            "and",
            "or",
            "not",
            "==",
            "!=",
            "<",
            "<=",
            ">",
            ">=",
            "is_empty",
            "is_not_empty",
            "sum",
            "avg",
            "min",
            "max",
            "count",
        }
        if op.lower() not in allowed_operators:
            failures.append(
                f"Unsupported or complex operator/function '{op}' detected in rule condition."
            )

    # Recursively check operands
    operands = node.get("operands")
    if isinstance(operands, list):
        for operand in operands:
            failures.extend(detect_stochastic_operators(operand))

    return failures


def run_usdm_validation(
    normalized_payload: Dict[str, Any],
    resolved_version: str,
    detected_format: str,
    evidence: List[str]
) -> USDMValidationReport:
    """
    Validates a normalized payload using Pydantic models (usdm_model.Study) and
    runs structural and business integrity checks.
    """
    errors: List[ValidationIssue] = []
    warnings: List[ValidationIssue] = []

    # 1. Pydantic Validation against usdm_model.Study
    try:
        usdm_model.Study(**normalized_payload)
    except ValidationError as ve:
        for err in ve.errors():
            loc_path = (
                " -> ".join(str(x) for x in err["loc"]) if err.get("loc") else "root"
            )
            inp_val = str(err.get("input")) if err.get("input") is not None else None
            errors.append(
                ValidationIssue(field=loc_path, reason=err["msg"], value=inp_val)
            )

    # 2. Structural/Business Checks (mirroring style of cdisc_validator)
    # Check for study identifier and name
    if not normalized_payload.get("id"):
        errors.append(
            ValidationIssue(
                field="id", reason="Study must contain a non-empty physical ID."
            )
        )
    if not normalized_payload.get("name"):
        errors.append(
            ValidationIssue(
                field="name",
                reason="Study must contain a non-empty physical name/title.",
            )
        )

    # Validate physical ID uniqueness across all study elements (Study, StudyVersions, StudyDesigns, Arms, Epochs, Encounters, Activities)
    all_ids: Dict[str, List[str]] = {}

    def add_id(element_id: Any, path: str):
        if not element_id:
            return
        element_id_str = str(element_id)
        if element_id_str not in all_ids:
            all_ids[element_id_str] = []
        all_ids[element_id_str].append(path)

    add_id(normalized_payload.get("id"), "Study")

    versions = normalized_payload.get("versions", [])
    if isinstance(versions, list):
        for v_idx, ver in enumerate(versions):
            if not isinstance(ver, dict):
                continue
            add_id(ver.get("id"), f"versions[{v_idx}]")

            designs = ver.get("studyDesigns", [])
            if isinstance(designs, list):
                for d_idx, design in enumerate(designs):
                    if not isinstance(design, dict):
                        continue
                    add_id(design.get("id"), f"versions[{v_idx}].studyDesigns[{d_idx}]")

                    # Arms
                    arms = design.get("arms", [])
                    if isinstance(arms, list):
                        for a_idx, arm in enumerate(arms):
                            if not isinstance(arm, dict):
                                continue
                            add_id(
                                arm.get("id"),
                                f"versions[{v_idx}].studyDesigns[{d_idx}].arms[{a_idx}]",
                            )

                    # Epochs
                    epochs = design.get("epochs", [])
                    if isinstance(epochs, list):
                        for ep_idx, epoch in enumerate(epochs):
                            if not isinstance(epoch, dict):
                                continue
                            add_id(
                                epoch.get("id"),
                                f"versions[{v_idx}].studyDesigns[{d_idx}].epochs[{ep_idx}]",
                            )

                    # Encounters
                    encounters = design.get("encounters", [])
                    if isinstance(encounters, list):
                        for enc_idx, enc in enumerate(encounters):
                            if not isinstance(enc, dict):
                                continue
                            add_id(
                                enc.get("id"),
                                f"versions[{v_idx}].studyDesigns[{d_idx}].encounters[{enc_idx}]",
                            )

                    # Activities
                    activities = design.get("activities", [])
                    if isinstance(activities, list):
                        for act_idx, act in enumerate(activities):
                            if not isinstance(act, dict):
                                continue
                            add_id(
                                act.get("id"),
                                f"versions[{v_idx}].studyDesigns[{d_idx}].activities[{act_idx}]",
                            )

    # Report duplicates as Material failures (Errors)
    for eid, paths in all_ids.items():
        if len(paths) > 1:
            errors.append(
                ValidationIssue(
                    field="multiple_elements",
                    reason=f"Duplicate physical ID '{eid}' detected across: {', '.join(paths)}.",
                    value=eid,
                )
            )

    # Audit & GxP non-empty change reasons checks (Part 11)
    audit_meta = (
        normalized_payload.get("audit_metadata")
        or normalized_payload.get("AuditFields")
        or {}
    )
    reason = None
    if isinstance(audit_meta, dict):
        reason = audit_meta.get("reason_for_change") or audit_meta.get("changeReason")
    if not reason:
        reason = normalized_payload.get("reason_for_change") or normalized_payload.get("changeReason")

    if not reason or not str(reason).strip():
        warnings.append(
            ValidationIssue(
                field="audit_metadata.reason_for_change",
                reason="Missing non-empty audit comment (reason_for_change or changeReason) under GxP audit fields.",
            )
        )

    # Rules expressions checks
    rules = traverse_rules_in_payload(normalized_payload)
    for r_idx, rule in enumerate(rules):
        rule_id = rule.get("id") or f"index_{r_idx}"
        cond = rule.get("condition")
        if cond:
            stoch_failures = detect_stochastic_operators(cond)
            for failure in stoch_failures:
                errors.append(
                    ValidationIssue(
                        field=f"rules[{r_idx}].condition",
                        reason=f"Rule '{rule_id}' violation: {failure}",
                    )
                )

    # Circular skip-logic dependencies
    try:
        cycles = detect_circular_dependencies(rules)
        if cycles:
            for cycle in cycles:
                errors.append(
                    ValidationIssue(
                        field="rules",
                        reason=f"Circular skip-logic dependency detected: {cycle}.",
                    )
                )
    except Exception as e:
        warnings.append(
            ValidationIssue(
                field="rules",
                reason=f"Warning while detecting circular dependencies: {str(e)}.",
            )
        )

    # Custom extensible elements check (Warn on custom tags)
    known_study_fields = set(usdm_model.Study.model_fields.keys())
    for key in normalized_payload.keys():
        if key == "studyVersions":
            continue
        if key not in known_study_fields:
            warnings.append(
                ValidationIssue(
                    field=key,
                    reason=f"Custom extensible element '{key}' detected (ignored or mapped to preservation_metadata).",
                )
            )

    is_valid = len(errors) == 0

    return USDMValidationReport(
        version=resolved_version,
        format=detected_format,
        validity=is_valid,
        errors=errors,
        warnings=warnings,
        version_resolution_evidence=evidence,
    )
