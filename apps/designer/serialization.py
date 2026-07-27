import json
import yaml
from typing import Any, Dict, List
from apps.designer.usdm_ingestion import validate_usdm_payload
from apps.designer.mapper import to_uuid


class USDMSerializationError(ValueError):
    """
    Custom exception raised when exported USDM payload fails validation.
    """
    def __init__(self, message: str, errors: List[Dict[str, Any]]):
        self.errors = errors
        self.message = message
        super().__init__(message)


def get_canonical_payload(usdm_dict: Dict[str, Any]) -> Dict[str, Any]:
    """
    Extracts and normalizes standard canonical USDM structure from a mapped payload.
    """
    import copy
    payload = copy.deepcopy(usdm_dict)

    # Ensure study-level id is a valid UUID and preserve original ID
    original_id = payload.get("id")
    if original_id:
        payload["_original_id"] = original_id
        payload["id"] = to_uuid(original_id, "study")

    standard_keys = {
        "id",
        "_original_id",
        "name",
        "description",
        "instanceType",
        "versions",
        "audit_metadata",
        "reason_for_change"
    }

    return {k: payload[k] for k in standard_keys if k in payload}


def serialize_usdm(
    usdm_dict: Dict[str, Any],
    format_type: str = "json",
    style: str = "canonical",
    validate: bool = True
) -> str:
    """
    Serializes a mapped USDM dictionary to a deterministic string (JSON or YAML),
    validating it through the shared validation foundation (validate_usdm_payload).

    Args:
        usdm_dict (Dict[str, Any]): The mapped USDM dictionary.
        format_type (str): The output format, either "json" or "yaml" / "yml".
        style (str): The export layout style, either "canonical", "legacy", or "both".
        validate (bool): If True, validates the output against the shared validation foundation.

    Returns:
        str: The serialized, valid USDM payload.

    Raises:
        USDMSerializationError: If validation fails.
    """
    fmt = format_type.strip().lower()
    sty = style.strip().lower()

    # 1. Structure the dictionary according to the requested style
    if sty == "canonical":
        export_dict = get_canonical_payload(usdm_dict)
    elif sty == "legacy":
        legacy_keys = {"id", "name", "version", "description", "arms", "rules", "eligibility_criteria"}
        export_dict = {k: usdm_dict[k] for k in legacy_keys if k in usdm_dict}
    elif sty == "both":
        export_dict = dict(usdm_dict)
    else:
        raise ValueError(f"Unsupported style: '{style}'. Must be 'canonical', 'legacy', or 'both'.")

    # 2. Check basic physical identity rules before serializing
    errors = []
    if "id" not in export_dict or not export_dict["id"]:
        errors.append({
            "field": "id",
            "reason": "Study must contain a non-empty physical ID.",
            "value": export_dict.get("id")
        })
    if "name" not in export_dict or not export_dict["name"]:
        errors.append({
            "field": "name",
            "reason": "Study must contain a non-empty physical name/title.",
            "value": export_dict.get("name")
        })

    if errors:
        raise USDMSerializationError(
            f"USDM Export Validation Failed on pre-flight checks: {errors}",
            errors=errors
        )

    # 3. Deterministic serialization
    if fmt == "json":
        serialized = json.dumps(export_dict, indent=2, sort_keys=True)
    elif fmt in ("yaml", "yml"):
        serialized = yaml.dump(export_dict, default_flow_style=False, sort_keys=True)
    else:
        raise ValueError(f"Unsupported format type: '{format_type}'. Must be 'json' or 'yaml'.")

    # 4. Optional validation using the shared validation foundation
    if validate:
        # Since standard USDM expects versions inside, we should validate it correctly
        # But for legacy layout we skip strict validation against usdm_model.Study
        if sty in ("canonical", "both"):
            report = validate_usdm_payload(serialized)
            if not report.validity:
                errors_list = [
                    {"field": err.field, "reason": err.reason, "value": err.value}
                    for err in report.errors
                ]
                raise USDMSerializationError(
                    f"Exported USDM payload failed validation against official USDM schema. Errors: {errors_list}",
                    errors=errors_list
                )

    return serialized
