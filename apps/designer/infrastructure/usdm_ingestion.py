from __future__ import annotations

import json
from typing import Any, Literal

import usdm_model
import yaml
from pydantic import BaseModel, Field, ValidationError, model_validator


class ValidationIssue(BaseModel):
    """
    Represents an individual validation error or warning.
    """

    field: str | None = None
    reason: str
    value: str | None = None


class USDMValidationReport(BaseModel):
    """
    USDM ingestion validation report.
    """

    version: str = Field(..., description="Resolved USDM version ('v2' or 'v3')")
    format: str = Field(..., description="Detected file format ('JSON' or 'YAML')")
    validity: bool = Field(..., description="True if payload is completely valid")
    errors: list[ValidationIssue] = Field(
        default_factory=list, description="Validation errors"
    )
    warnings: list[ValidationIssue] = Field(
        default_factory=list, description="Validation warnings"
    )
    version_resolution_evidence: list[str] = Field(
        default_factory=list, description="Evidence used to resolve version"
    )


class FieldReference(BaseModel):
    """
    Represents a structured field reference within an expression tree.
    """

    field_id: str
    form_id: str | None = None
    visit_id: str | None = None
    visit_relative: str | None = None  # e.g., "previous", "next"


class ExpressionNode(BaseModel):
    """
    A recursive node in a structured clinical expression tree.
    """

    type: Literal["logical", "comparison", "function", "field_ref", "constant"]
    operator: str | None = None
    operands: list[ExpressionNode] | None = None
    value: Any | None = None
    field_ref: FieldReference | None = None

    @model_validator(mode="after")
    def validate_node(self) -> ExpressionNode:
        if self.type == "constant":
            if self.value is None:
                raise ValueError("Constant node must provide a 'value'")
        elif self.type == "field_ref":
            if self.field_ref is None:
                raise ValueError("Field reference node must provide 'field_ref'")
        elif self.type == "logical":
            if self.operator not in ("and", "or", "not"):
                raise ValueError(f"Invalid logical operator: '{self.operator}'")
        return self


def extract_field_references(node: ExpressionNode) -> list[FieldReference]:
    """
    Traverses the ExpressionNode tree to collect all FieldReferences.
    """
    refs = []
    if node.type == "field_ref" and node.field_ref:
        refs.append(node.field_ref)
    if node.operands:
        for op in node.operands:
            refs.extend(extract_field_references(op))
    return refs


def detect_circular_dependencies(rules: list[dict[str, Any]]) -> list[str]:
    """
    Analyzes skip-logic rules to detect circular visibility dependencies.
    """
    # Filter for active skip logic rules
    skip_rules = [
        r
        for r in rules
        if r.get("type") == "skip_logic" and not r.get("is_deleted", False)
    ]

    # Build adjacency list: target_field -> referenced_fields
    adj = {}
    for rule in skip_rules:
        target = rule.get("target_field")
        if not target:
            continue

        cond_node = rule.get("condition")
        if not cond_node:
            continue

        if isinstance(cond_node, dict):
            try:
                cond_node = ExpressionNode(**cond_node)
            except Exception:
                continue

        refs = extract_field_references(cond_node)
        ref_fields = {ref.field_id for ref in refs}
        adj[target] = list(ref_fields)

    state = {}  # node -> 0 (unvisited/absent), 1 (visiting), 2 (visited)
    cycles = []
    parent = {}

    def dfs(node: str) -> bool:
        state[node] = 1
        for neighbor in adj.get(node, []):
            if neighbor not in state or state[neighbor] == 0:
                parent[neighbor] = node
                if dfs(neighbor):
                    return True
            elif state[neighbor] == 1:
                # Cycle detected! Construct cycle path using parent pointers
                path = [neighbor]
                curr = node
                while curr != neighbor:
                    path.append(curr)
                    curr = parent.get(curr)
                    if curr is None:
                        break
                path.append(neighbor)
                path.reverse()
                cycles.append(" -> ".join(path))
                return True
        state[node] = 2
        return False

    for node in adj:
        if node not in state or state[node] == 0:
            dfs(node)

    return cycles


def safe_parse_payload(raw_text: str) -> tuple[dict[str, Any], str]:
    """
    Safely parses JSON or YAML payload.
    """
    stripped = raw_text.strip()
    if not stripped:
        raise ValueError("Empty payload.")

    if stripped.startswith("{") or stripped.startswith("["):
        try:
            parsed = json.loads(stripped)
            if isinstance(parsed, dict):
                return parsed, "JSON"
            raise ValueError("JSON payload must be a dictionary.")
        except json.JSONDecodeError:
            pass

    try:
        parsed = yaml.safe_load(stripped)
        if isinstance(parsed, dict):
            return parsed, "YAML"
        raise ValueError("Parsed payload is not a dictionary.")
    except Exception as ye:
        raise ValueError(f"Payload parsing failed as both JSON and YAML: {str(ye)}")


def resolve_usdm_version(
    payload: dict[str, Any], override: str | None = None
) -> tuple[str, list[str]]:
    """
    Resolves USDM v2 vs v3 version using structural rules or explicit override.
    """
    evidence = []
    if override:
        if override in ("v2", "v3"):
            evidence.append(f"Explicit version override provided: {override}")
            return override, evidence
        evidence.append(
            f"Ignored invalid override '{override}'. Falling back to structural rules."
        )

    if "studyVersions" in payload:
        evidence.append(
            "Detected 'studyVersions' key in study root, indicative of USDM v2."
        )
        return "v2", evidence

    if "versions" in payload:
        evidence.append("Detected 'versions' key in study root, indicative of USDM v3.")
        return "v3", evidence

    for key in payload:
        if "version" in key.lower():
            evidence.append(
                f"Detected key '{key}' in root, treating as USDM v3 fallback."
            )
            return "v3", evidence

    evidence.append(
        "No version-specific keys found in root. Falling back to USDM v3 by default."
    )
    return "v3", evidence


def normalize_usdm_payload(payload: dict[str, Any], version: str) -> dict[str, Any]:
    """
    Normalizes USDM shape differences into the contract expected by usdm_model.Study.
    """
    import copy

    normalized = copy.deepcopy(payload)

    if version == "v2":
        if "studyVersions" in normalized and "versions" not in normalized:
            normalized["versions"] = normalized.pop("studyVersions")

    versions_list = normalized.get("versions")
    if isinstance(versions_list, list):
        for ver in versions_list:
            if not isinstance(ver, dict):
                continue

            if "studyDesign" in ver and "studyDesigns" not in ver:
                ver["studyDesigns"] = ver.pop("studyDesign")
            elif "designs" in ver and "studyDesigns" not in ver:
                ver["studyDesigns"] = ver.pop("designs")

            designs = ver.get("studyDesigns")
            if isinstance(designs, list):
                for design in designs:
                    if not isinstance(design, dict):
                        continue

                    if "studyArms" in design and "arms" not in design:
                        design["arms"] = design.pop("studyArms")
                    if "studyEpochs" in design and "epochs" not in design:
                        design["epochs"] = design.pop("studyEpochs")

    if "instanceType" not in normalized:
        normalized["instanceType"] = "Study"

    return normalized


def traverse_rules_in_payload(
    normalized_payload: dict[str, Any],
) -> list[dict[str, Any]]:
    """
    Extracts all rule dictionaries from the normalized payload.
    """
    rules = []
    top_rules = normalized_payload.get("rules", [])
    if isinstance(top_rules, list):
        for r in top_rules:
            if isinstance(r, dict):
                rules.append(r)

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


def detect_stochastic_operators(node: Any) -> list[str]:
    """
    Recursively check condition nodes for complex/stochastic math operators or invalid function names.
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

    operands = node.get("operands")
    if isinstance(operands, list):
        for operand in operands:
            failures.extend(detect_stochastic_operators(operand))

    return failures


def validate_usdm_payload(
    raw_text: str, override: str | None = None
) -> USDMValidationReport:
    """
    Performs parsing, version resolution, normalization, and full validation.
    """
    errors: list[ValidationIssue] = []
    warnings: list[ValidationIssue] = []

    try:
        payload, detected_format = safe_parse_payload(raw_text)
    except Exception as e:
        return USDMValidationReport(
            version="v3",
            format="JSON",
            validity=False,
            errors=[ValidationIssue(reason=f"Format parsing error: {str(e)}")],
            version_resolution_evidence=[
                "Parsing failed, version could not be resolved."
            ],
        )

    resolved_version, evidence = resolve_usdm_version(payload, override)

    try:
        normalized_payload = normalize_usdm_payload(payload, resolved_version)
    except Exception as e:
        errors.append(ValidationIssue(reason=f"Normalization failed: {str(e)}"))
        return USDMValidationReport(
            version=resolved_version,
            format=detected_format,
            validity=False,
            errors=errors,
            warnings=warnings,
            version_resolution_evidence=evidence,
        )

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

    all_ids: dict[str, list[str]] = {}

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

                    arms = design.get("arms", [])
                    if isinstance(arms, list):
                        for a_idx, arm in enumerate(arms):
                            if not isinstance(arm, dict):
                                continue
                            add_id(
                                arm.get("id"),
                                f"versions[{v_idx}].studyDesigns[{d_idx}].arms[{a_idx}]",
                            )

                    epochs = design.get("epochs", [])
                    if isinstance(epochs, list):
                        for ep_idx, epoch in enumerate(epochs):
                            if not isinstance(epoch, dict):
                                continue
                            add_id(
                                epoch.get("id"),
                                f"versions[{v_idx}].studyDesigns[{d_idx}].epochs[{ep_idx}]",
                            )

                    encounters = design.get("encounters", [])
                    if isinstance(encounters, list):
                        for enc_idx, enc in enumerate(encounters):
                            if not isinstance(enc, dict):
                                continue
                            add_id(
                                enc.get("id"),
                                f"versions[{v_idx}].studyDesigns[{d_idx}].encounters[{enc_idx}]",
                            )

                    activities = design.get("activities", [])
                    if isinstance(activities, list):
                        for act_idx, act in enumerate(activities):
                            if not isinstance(act, dict):
                                continue
                            add_id(
                                act.get("id"),
                                f"versions[{v_idx}].studyDesigns[{d_idx}].activities[{act_idx}]",
                            )

    for eid, paths in all_ids.items():
        if len(paths) > 1:
            errors.append(
                ValidationIssue(
                    field="multiple_elements",
                    reason=f"Duplicate physical ID '{eid}' detected across: {', '.join(paths)}.",
                    value=eid,
                )
            )

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

    audit_meta = (
        normalized_payload.get("audit_metadata")
        or normalized_payload.get("AuditFields")
        or {}
    )
    reason = None
    if isinstance(audit_meta, dict):
        reason = audit_meta.get("reason_for_change") or audit_meta.get("changeReason")
    if not reason:
        reason = normalized_payload.get("reason_for_change") or normalized_payload.get(
            "changeReason"
        )

    if not reason or not str(reason).strip():
        warnings.append(
            ValidationIssue(
                field="audit_metadata.reason_for_change",
                reason="Missing non-empty audit comment (reason_for_change or changeReason) under GxP audit fields.",
            )
        )

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

    known_study_fields = set(usdm_model.Study.model_fields.keys())
    for key in payload:
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
