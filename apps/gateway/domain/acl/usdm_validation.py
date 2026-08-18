"""Gateway Anti-Corruption Layer USDM validation utilities.

Requirements: PRD-SYS-001
"""

import json
from typing import Any

from pydantic import BaseModel, Field


class ValidationIssueDTO(BaseModel):
    """Represents an individual validation error or warning issue in USDM payloads."""

    field: str | None = Field(
        None, description="JSON path or field name where issue occurred."
    )
    reason: str = Field(..., description="Detailed explanation of validation issue.")
    value: str | None = Field(
        None, description="Offending input value string representation."
    )


class USDMValidationDTO(BaseModel):
    """Gateway ACL DTO representing USDM ingestion/export validation report."""

    version: str = Field(
        ..., description="Resolved USDM specification version ('v2' or 'v3')."
    )
    format: str = Field(
        ..., description="Detected payload file format ('JSON' or 'YAML')."
    )
    validity: bool = Field(..., description="True if payload is completely valid.")
    errors: list[ValidationIssueDTO] = Field(
        default_factory=list, description="List of structural validation errors."
    )
    warnings: list[ValidationIssueDTO] = Field(
        default_factory=list, description="List of validation warnings."
    )
    version_resolution_evidence: list[str] = Field(
        default_factory=list,
        description="Evidence log used during version resolution.",
    )


def resolve_usdm_version(
    payload_or_str: Any,
) -> tuple[str, str, list[dict[str, Any]], list[str]]:
    """Resolves USDM version ('v2' or 'v3') and format ('JSON' or 'YAML')."""
    fmt = "JSON"
    evidence = []
    if isinstance(payload_or_str, str):
        try:
            data = json.loads(payload_or_str)
            fmt = "JSON"
        except Exception:
            import yaml

            data = yaml.safe_load(payload_or_str)
            fmt = "YAML"
    else:
        data = payload_or_str

    if isinstance(data, dict):
        if (
            "usdmVersion" in data
            or "study" in data
            or data.get("instanceType") == "Study"
        ):
            version = "v3"
            evidence.append("v3 detected via instanceType/study structure")
        else:
            version = "v2"
            evidence.append("v2 detected by default")
    else:
        version = "v2"

    return version, fmt, [data] if isinstance(data, dict) else [], evidence


def _collect_ids(obj: Any, current_path: str, id_map: dict[str, list[str]]) -> None:
    if isinstance(obj, dict):
        if "id" in obj and obj["id"]:
            eid = str(obj["id"])
            id_map.setdefault(eid, []).append(current_path or "root")
        for k, v in obj.items():
            if k != "id":
                new_path = f"{current_path}.{k}" if current_path else k
                _collect_ids(v, new_path, id_map)
    elif isinstance(obj, list):
        for idx, item in enumerate(obj):
            new_path = f"{current_path}[{idx}]"
            _collect_ids(item, new_path, id_map)


def validate_usdm_payload(payload_str: str) -> USDMValidationDTO:
    """Validates USDM payload string and returns USDMValidationDTO."""
    version, fmt, parsed_objs, evidence = resolve_usdm_version(payload_str)
    errors: list[ValidationIssueDTO] = []
    warnings: list[ValidationIssueDTO] = []

    if not parsed_objs or not isinstance(parsed_objs[0], dict):
        errors.append(
            ValidationIssueDTO(
                field="root", reason="Payload must be a valid JSON object or YAML map."
            )
        )
        return USDMValidationDTO(
            version=version,
            format=fmt,
            validity=False,
            errors=errors,
            warnings=warnings,
            version_resolution_evidence=evidence,
        )

    data = parsed_objs[0]

    id_map: dict[str, list[str]] = {}
    _collect_ids(data, "", id_map)
    for eid, paths in id_map.items():
        if len(paths) > 1:
            errors.append(
                ValidationIssueDTO(
                    field="multiple_elements",
                    reason=f"Duplicate physical ID '{eid}' detected across: {', '.join(paths)}.",
                    value=eid,
                )
            )

    if not data.get("id"):
        errors.append(
            ValidationIssueDTO(
                field="id", reason="Study must contain a non-empty physical ID."
            )
        )
    if not data.get("name"):
        errors.append(
            ValidationIssueDTO(
                field="name",
                reason="Study must contain a non-empty physical name/title.",
            )
        )

    validity = len(errors) == 0
    return USDMValidationDTO(
        version=version,
        format=fmt,
        validity=validity,
        errors=errors,
        warnings=warnings,
        version_resolution_evidence=evidence,
    )
