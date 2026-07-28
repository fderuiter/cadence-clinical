import copy
from typing import Any, Dict, List, Optional

from apps.designer.mapper import map_study_to_usdm
from apps.designer.inverse_mapper import map_usdm_to_study
from apps.designer.comparison import compare_payloads, flatten_dict
from apps.designer.rules import detect_circular_dependencies


def detect_payload_format(payload: Dict[str, Any]) -> str:
    """
    Detects if the source payload is standard CDISC USDM format or Cadence Internal study projection.
    """
    if "instanceType" in payload or "versions" in payload:
        return "USDM"
    return "internal"


def detect_usdm_version(payload: Dict[str, Any]) -> str:
    """
    Attempts to detect the USDM version (v2, v3, or v4) from the payload structure or versions.
    """
    # Simple heuristic check
    versions = payload.get("versions", [])
    if versions and isinstance(versions, list) and isinstance(versions[0], dict):
        version_identifier = versions[0].get("versionIdentifier")
        if version_identifier:
            return f"v3 (version {version_identifier})"

    # Check if there are keys that resemble v2/v3
    if "studyDesigns" in str(payload):
        return "v3"
    return "v2/v3"


def execute_round_trip(source_payload: Dict[str, Any]) -> Dict[str, Any]:
    """
    Executes a complete, lossless vs lossy round-trip orchestration.
    Automatically detects direction and runs USDM->internal->USDM or internal->USDM->internal.
    Compares the result, collects diagnostics, and produces a comprehensive report.
    """
    payload_copy = copy.deepcopy(source_payload)
    source_format = detect_payload_format(payload_copy)

    direction = ""
    detected_version = "unknown"
    round_tripped = None
    intermediate = None

    errors: List[str] = []
    warnings: List[str] = []
    unsupported_constructs: List[str] = []

    if source_format == "USDM":
        direction = "USDM_to_internal_to_USDM"
        detected_version = detect_usdm_version(payload_copy)

        # 1. Map USDM to Internal Study Projection
        try:
            intermediate = map_usdm_to_study(payload_copy)
        except Exception as e:
            errors.append(f"USDM to Internal transformation failed: {str(e)}")
            # Classify as lossy since the transformation failed
            return {
                "classification": "lossy",
                "source_format": source_format,
                "direction": direction,
                "detected_version": detected_version,
                "fidelity_details": {
                    "added": [],
                    "dropped": [
                        {
                            "field": "all",
                            "value": "Unable to parse source",
                            "is_material": True,
                            "reason": f"Ingestion pipeline failed: {str(e)}"
                        }
                    ],
                    "altered": []
                },
                "mapping_diagnostics": {
                    "unsupported_constructs": [str(e)],
                    "errors": errors,
                    "warnings": ["Payload failed parsing validation."]
                }
            }

        # 2. Map back to USDM
        try:
            round_tripped = map_study_to_usdm(intermediate)
        except Exception as e:
            errors.append(f"Internal to USDM serialization failed: {str(e)}")
            round_tripped = {}

    else:
        direction = "internal_to_USDM_to_internal"
        detected_version = payload_copy.get("current_version", "internal")

        # 1. Map Internal Study Projection to USDM
        try:
            intermediate = map_study_to_usdm(payload_copy)
        except Exception as e:
            errors.append(f"Internal to USDM mapping failed: {str(e)}")
            return {
                "classification": "lossy",
                "source_format": source_format,
                "direction": direction,
                "detected_version": detected_version,
                "fidelity_details": {
                    "added": [],
                    "dropped": [
                        {
                            "field": "all",
                            "value": "Unable to map source",
                            "is_material": True,
                            "reason": f"Export pipeline failed: {str(e)}"
                        }
                    ],
                    "altered": []
                },
                "mapping_diagnostics": {
                    "unsupported_constructs": [str(e)],
                    "errors": errors,
                    "warnings": ["Source internal payload failed mapping."]
                }
            }

        # 2. Map USDM back to Internal Study Projection
        try:
            round_tripped = map_usdm_to_study(intermediate)
        except Exception as e:
            errors.append(f"USDM back to Internal inverse-mapping failed: {str(e)}")
            round_tripped = {}

    # Check for unsupported constructs
    # Check for circular skip logic
    rules = []
    if source_format == "USDM" and intermediate:
        rules = intermediate.get("rules", [])
    elif source_format == "internal":
        rules = payload_copy.get("rules", [])

    try:
        cycles = detect_circular_dependencies(rules)
        if cycles:
            unsupported_constructs.append(f"Circular skip-logic dependency detected: {', '.join(cycles)}")
            warnings.append("Circular dependencies might cause evaluation infinite loops.")
    except Exception as e:
        warnings.append(f"Failed to check for circular skip-logic dependencies: {str(e)}")

    # Check for unsupported stochastic or complex math operators
    # (By checking if rule condition has anything not standard)
    standard_operators = {"==", "!=", "<", ">", "<=", ">=", "AND", "OR", "and", "or"}
    for rule in rules:
        cond = rule.get("condition") or {}
        # Simple checker for operators in flat dictionary
        flat_cond = flatten_dict(cond)
        for k, v in flat_cond.items():
            if "operator" in k and isinstance(v, str):
                if v not in standard_operators:
                    unsupported_constructs.append(f"Stochastic/Complex mathematical operator '{v}' is unsupported.")

    # 3. Perform path-by-path comparison
    comparison_report = compare_payloads(source_payload, round_tripped)

    # Classify as lossless only when:
    # - The comparison reports 'lossless' == True (no material differences exist)
    # - There are no mapping errors or unsupported constructs detected
    is_lossless = comparison_report["lossless"] and len(errors) == 0 and len(unsupported_constructs) == 0
    classification = "lossless" if is_lossless else "lossy"

    # Collect warnings if there are non-material differences
    if comparison_report["non_material_difference_count"] > 0:
        warnings.append(f"Detected {comparison_report['non_material_difference_count']} non-material representation updates (e.g. metadata tags or ID formatting).")

    return {
        "classification": classification,
        "source_format": source_format,
        "direction": direction,
        "detected_version": detected_version,
        "fidelity_details": {
            "added": comparison_report["added"],
            "dropped": comparison_report["dropped"],
            "altered": comparison_report["altered"]
        },
        "mapping_diagnostics": {
            "unsupported_constructs": unsupported_constructs,
            "errors": errors,
            "warnings": warnings
        }
    }
