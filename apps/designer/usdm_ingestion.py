"""
USDM Ingestion Service (Task 1)

This module serves as the primary format-agnostic ingestion utility for USDM content.
It supports:
- Detecting and parsing JSON or YAML payloads.
- Resolving the USDM version and normalizing the shape (via usdm_adapter).
- Validating the canonical structure and business integrity (via usdm_validator).
"""

import json
from typing import Any, Dict, List, Optional, Tuple
import yaml

# Imports from version handling & adapter module (Task 2)
from apps.designer.usdm_adapter import (
    resolve_usdm_version,
    normalize_usdm_payload,
)

# Imports from validation module (Task 3)
from apps.designer.usdm_validator import (
    ValidationIssue,
    USDMValidationReport,
    run_usdm_validation,
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
        parsed = yaml.safe_load(stripped)
        if isinstance(parsed, dict):
            return parsed, "YAML"
        raise ValueError("Parsed payload is not a dictionary.")
    except Exception as ye:
        raise ValueError(f"Payload parsing failed as both JSON and YAML: {str(ye)}")


def validate_usdm_payload(
    raw_text: str, override: Optional[str] = None
) -> USDMValidationReport:
    """
    Performs format-agnostic parsing, version resolution, normalization, and full validation.
    """
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

    # 2. Version Resolution (delegated to adapter)
    resolved_version, evidence = resolve_usdm_version(payload, override)

    # 3. Normalization (delegated to adapter)
    try:
        normalized_payload = normalize_usdm_payload(payload, resolved_version)
    except Exception as e:
        return USDMValidationReport(
            version=resolved_version,
            format=detected_format,
            validity=False,
            errors=[ValidationIssue(reason=f"Normalization failed: {str(e)}")],
            version_resolution_evidence=evidence,
        )

    # 4. In-depth validation (delegated to validator)
    return run_usdm_validation(
        normalized_payload=normalized_payload,
        resolved_version=resolved_version,
        detected_format=detected_format,
        evidence=evidence,
    )
