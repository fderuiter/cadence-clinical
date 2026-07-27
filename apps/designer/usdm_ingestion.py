import json
from typing import Any, Dict, List, Optional, Tuple

import usdm_model
import yaml
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


def safe_parse_payload(raw_text: str) -> Tuple[Dict[str, Any], str]:
    """
    Safely parses JSON or YAML payload.
    Returns parsed dictionary and detected format ("JSON" or "YAML").
    """
    stripped = raw_text.strip()
    if not stripped:
        raise ValueError("Empty payload.")

    # Try JSON first
    if stripped.startswith("{") or stripped.startswith("["):
        try:
            parsed = json.loads(stripped)
            if isinstance(parsed, dict):
                return parsed, "JSON"
            else:
                raise ValueError("JSON payload must be a dictionary.")
        except json.JSONDecodeError:
            # Let's fall back to YAML or raise
            pass

    # Try YAML (safe_load only!)
    try:
        # standard yaml.safe_load is safe and supports standard YAML
        parsed = yaml.safe_load(stripped)
        if isinstance(parsed, dict):
            return parsed, "YAML"
        raise ValueError("Parsed payload is not a dictionary.")
    except Exception as ye:
        raise ValueError(f"Payload parsing failed as both JSON and YAML: {str(ye)}")


def resolve_usdm_version(
    payload: Dict[str, Any], override: Optional[str] = None
) -> Tuple[str, List[str]]:
    """
    Resolves USDM v2 vs v3 version using structural rules or explicit override.
    """
    evidence = []
    if override:
        if override in ("v2", "v3"):
            evidence.append(f"Explicit version override provided: {override}")
            return override, evidence
        else:
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

    # Check deeper for specific v2/v3 structures
    for key in payload.keys():
        if "version" in key.lower():
            evidence.append(
                f"Detected key '{key}' in root, treating as USDM v3 fallback."
            )
            return "v3", evidence

    evidence.append(
        "No version-specific keys found in root. Falling back to USDM v3 by default."
    )
    return "v3", evidence


def normalize_usdm_payload(payload: Dict[str, Any], version: str) -> Dict[str, Any]:
    """
    Normalizes USDM shape differences into the contract expected by usdm_model.Study.
    """
    import copy

    normalized = copy.deepcopy(payload)

    # If v2, rename studyVersions -> versions
    if version == "v2":
        if "studyVersions" in normalized and "versions" not in normalized:
            normalized["versions"] = normalized.pop("studyVersions")

    # Ensure versions is a list of dicts
    versions_list = normalized.get("versions")
    if isinstance(versions_list, list):
        for ver in versions_list:
            if not isinstance(ver, dict):
                continue

            # Normalize StudyVersion fields
            # Check if studyDesigns is present under different keys (e.g. studyDesign, designs)
            if "studyDesign" in ver and "studyDesigns" not in ver:
                ver["studyDesigns"] = ver.pop("studyDesign")
            elif "designs" in ver and "studyDesigns" not in ver:
                ver["studyDesigns"] = ver.pop("designs")

            designs = ver.get("studyDesigns")
            if isinstance(designs, list):
                for design in designs:
                    if not isinstance(design, dict):
                        continue

                    # In StudyDesign, normalize studyArms -> arms, studyEpochs -> epochs
                    if "studyArms" in design and "arms" not in design:
                        design["arms"] = design.pop("studyArms")
                    if "studyEpochs" in design and "epochs" not in design:
                        design["epochs"] = design.pop("studyEpochs")

    # Ensure basic instanceType values are present if absent
    if "instanceType" not in normalized:
        normalized["instanceType"] = "Study"

    return normalized


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
        # Check against allowed operators
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
        # Case insensitive compare
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


def validate_usdm_payload(
    raw_text: str, override: Optional[str] = None
) -> USDMValidationReport:
    """
    Performs parsing, version resolution, normalization, and full validation (Pydantic + business checks).
    """
    errors: List[ValidationIssue] = []
    warnings: List[ValidationIssue] = []
    evidence: List[str] = []

    # 1. Safe Parse
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

    # 2. Version Resolution
    resolved_version, evidence = resolve_usdm_version(payload, override)

    # 3. Normalization
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

    # 4. Pydantic Validation against usdm_model.Study
    try:
        usdm_model.Study(**normalized_payload)
    except ValidationError as ve:
        for err in ve.errors():
            loc_path = (
                " -> ".join(str(x) for x in err["loc"]) if err.get("loc") else "root"
            )
            # Extract input value if it fits in string
            inp_val = str(err.get("input")) if err.get("input") is not None else None
            errors.append(
                ValidationIssue(field=loc_path, reason=err["msg"], value=inp_val)
            )

    # 5. Required Business & Structural Checks

    # Unique ID validation across study elements
    # Gather IDs of study, versions, designs, arms, epochs, encounters, activities
    all_ids: Dict[str, List[str]] = {}  # id -> paths where it appeared

    def add_id(element_id: Any, path: str):
        if not element_id:
            return
        element_id_str = str(element_id)
        if element_id_str not in all_ids:
            all_ids[element_id_str] = []
        all_ids[element_id_str].append(path)

    # Traverse normalized payload for IDs
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

                    # Encounters / Visits
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

    # Report duplicate physical identity infractions as Material failures (Errors)
    for eid, paths in all_ids.items():
        if len(paths) > 1:
            errors.append(
                ValidationIssue(
                    field="multiple_elements",
                    reason=f"Duplicate physical ID '{eid}' detected across: {', '.join(paths)}.",
                    value=eid,
                )
            )

    # Check for empty study element names/IDs (Material Fidelity Check)
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

    # Audit & GxP non-empty change reasons checks (Part 11)
    # Check top-level or audit_metadata / AuditFields
    # Check for audit trail parameters
    audit_meta = (
        normalized_payload.get("audit_metadata")
        or normalized_payload.get("AuditFields")
        or {}
    )
    reason = None
    if isinstance(audit_meta, dict):
        reason = audit_meta.get("reason_for_change") or audit_meta.get("changeReason")
    if not reason:
        # Check root or query
        reason = normalized_payload.get("reason_for_change") or normalized_payload.get(
            "changeReason"
        )

    if not reason or not str(reason).strip():
        # Missing change_reason is a material risk for FDA CFR Part 11 auditing
        warnings.append(
            ValidationIssue(
                field="audit_metadata.reason_for_change",
                reason="Missing non-empty audit comment (reason_for_change or changeReason) under GxP audit fields.",
            )
        )

    # Parse and validate expressions inside rules
    rules = traverse_rules_in_payload(normalized_payload)
    for r_idx, rule in enumerate(rules):
        rule_id = rule.get("id") or f"index_{r_idx}"
        cond = rule.get("condition")
        if cond:
            # 1. Stochastic operator detection
            stoch_failures = detect_stochastic_operators(cond)
            for failure in stoch_failures:
                errors.append(
                    ValidationIssue(
                        field=f"rules[{r_idx}].condition",
                        reason=f"Rule '{rule_id}' violation: {failure}",
                    )
                )

    # 2. Circular dependency checks on skip-logic rules
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

    # 6. Extensible custom elements warning
    # Warn when payload contains non-standard XML/JSON tags that do not map to USDM schema
    known_study_fields = set(usdm_model.Study.model_fields.keys())
    for key in payload.keys():
        # Treat studyVersions as a known normalized key, ignore it
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
