import os
from typing import Any, Dict

import pytest
import yaml

from apps.designer.main import app as designer_app
from apps.execution.main import app as execution_app


# Helper to locate and extract the YAML spec from SDLC file
def extract_openapi_yaml(filepath: str) -> str:
    if not os.path.exists(filepath):
        raise FileNotFoundError(f"Markdown specification file not found: {filepath}")

    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    # Find the Section 7 title
    sec_title = "## 7. Complete OpenAPI 3.0 Contract Specification"
    idx = content.find(sec_title)
    if idx == -1:
        raise ValueError(f"Could not find section title: '{sec_title}'")

    sec_content = content[idx + len(sec_title) :]

    # Locate the first ```yaml block
    start_fence = "```yaml"
    start_idx = sec_content.find(start_fence)
    if start_idx == -1:
        raise ValueError("Could not find ```yaml code block in Section 7.")

    start_pos = start_idx + len(start_fence)
    end_pos = sec_content.find("```", start_pos)
    if end_pos == -1:
        raise ValueError("Could not find terminating ``` for the yaml block.")

    return sec_content[start_pos:end_pos].strip()


def resolve_schema(schema: Any, spec: Dict[str, Any]) -> Any:
    """Recursively resolve $ref references in the spec dictionary."""
    if isinstance(schema, dict):
        if "$ref" in schema:
            ref_path = schema["$ref"].split("/")
            # e.g., ["#", "components", "schemas", "ConceptDetail"]
            resolved = spec
            for part in ref_path[1:]:
                resolved = resolved.get(part, {})
            # Resolve recursively in case the referenced schema contains other references
            return resolve_schema(resolved, spec)
        else:
            return {k: resolve_schema(v, spec) for k, v in schema.items()}
    elif isinstance(schema, list):
        return [resolve_schema(item, spec) for item in schema]
    return schema


def normalize_type(t: Any) -> Any:
    """Normalize types and handles nullable union structures."""
    if isinstance(t, list):
        # e.g., ["string", "null"]
        clean_list = [item for item in t if item != "null"]
        if len(clean_list) == 1:
            return clean_list[0]
        return clean_list
    return t


def compare_types(type_spec: Any, type_code: Any) -> bool:
    """Compare type strings supporting float/number equivalent etc."""
    type_spec = normalize_type(type_spec)
    type_code = normalize_type(type_code)

    if type_spec == type_code:
        return True
    if {type_spec, type_code} == {"number", "float"}:
        return True
    return False


def assert_schema_parity(
    spec_schema: Any,
    code_schema: Any,
    spec_full: Dict[str, Any],
    code_full: Dict[str, Any],
    path_context: str = "",
    bidirectional_required: bool = True,
) -> None:
    """Compare two OpenAPI schemas semantically for complete parity."""
    # Resolve any references on both sides
    s_resolved = resolve_schema(spec_schema, spec_full)
    c_resolved = resolve_schema(code_schema, code_full)

    if not isinstance(s_resolved, dict) or not isinstance(c_resolved, dict):
        # Leaf level check
        assert type(s_resolved) is type(c_resolved), (
            f"Type mismatch at {path_context}: spec={s_resolved}, code={c_resolved}"
        )
        if isinstance(s_resolved, (str, int, float, bool)):
            assert s_resolved == c_resolved, (
                f"Value mismatch at {path_context}: spec={s_resolved}, code={c_resolved}"
            )
        return

    # Handle nullable equivalence in OpenAPI 3.1 vs 3.0
    # Code uses OpenAPI 3.1 (FastAPI), Spec uses OpenAPI 3.0
    # Spec might have 'nullable: true', Code might have 'anyOf' with null, or 'type: [string, "null"]'
    if "anyOf" in c_resolved:
        # Check if one of anyOf is type: null, and extract the real type
        non_null_schemas = [x for x in c_resolved["anyOf"] if x.get("type") != "null"]
        if len(non_null_schemas) == 1:
            c_resolved = non_null_schemas[0]

    # Compare Type
    s_type = s_resolved.get("type")
    c_type = c_resolved.get("type")
    if s_type and c_type:
        assert compare_types(s_type, c_type), (
            f"Mismatched type at {path_context}: spec={s_type}, code={c_type}"
        )

    # Compare Enum values
    if "enum" in s_resolved:
        assert "enum" in c_resolved, (
            f"Missing enum constraint in codebase schema at {path_context}"
        )
        assert set(s_resolved["enum"]) == set(c_resolved["enum"]), (
            f"Mismatched enum values at {path_context}: spec={s_resolved['enum']}, code={c_resolved['enum']}"
        )

    # Compare Properties for objects
    if s_type == "object" or "properties" in s_resolved:
        s_props = s_resolved.get("properties", {})
        c_props = c_resolved.get("properties", {})

        # Verify that all properties defined in spec exist in code and match
        for prop_name, prop_spec in s_props.items():
            assert prop_name in c_props, (
                f"Property '{prop_name}' defined in contract specification is missing in codebase at {path_context}"
            )
            assert_schema_parity(
                prop_spec,
                c_props[prop_name],
                spec_full,
                code_full,
                f"{path_context}.{prop_name}",
                bidirectional_required=bidirectional_required,
            )

        # Compare Required fields list
        s_req = set(s_resolved.get("required", []))
        c_req = set(c_resolved.get("required", []))

        # Ensure bidirectional parity of required fields if requested
        missing_in_code = s_req - c_req
        assert not missing_in_code, (
            f"Required properties {missing_in_code} in spec contract are not marked required in codebase at {path_context}"
        )
        if bidirectional_required:
            missing_in_spec = c_req - s_req
            assert not missing_in_spec, (
                f"Required properties {missing_in_spec} in codebase are not marked required in spec contract at {path_context}"
            )

    # Compare Items for arrays
    if s_type == "array" or "items" in s_resolved:
        assert "items" in c_resolved, (
            f"Array schema missing 'items' property in codebase at {path_context}"
        )
        assert_schema_parity(
            s_resolved["items"],
            c_resolved["items"],
            spec_full,
            code_full,
            f"{path_context}[]",
            bidirectional_required=bidirectional_required,
        )


def find_code_route(spec_path: str, code_routes: Dict[str, Any]) -> Any:
    """Match a specification relative path to its registered codebase route, stripping prefixes like /api/v1."""
    # Strip prefixes like /api/v1 or leading/trailing slashes for comparison
    clean_spec = spec_path.replace("/api/v1", "").strip("/")

    for c_path, c_route_info in code_routes.items():
        clean_code = c_path.replace("/api/v1", "").strip("/")
        if clean_spec == clean_code:
            return c_route_info
    return None


@pytest.fixture(scope="module")
def loaded_specs():
    """Fixture to statically parse the markdown OpenAPI contract and compile codebase schemas entirely offline."""
    # 1. Load documentation contract spec
    spec_yaml = extract_openapi_yaml("docs/SDLC/03_API_Integration_Specification.md")
    spec_dict = yaml.safe_load(spec_yaml)

    # 2. Extract codebase openapi specs statically
    designer_spec = designer_app.openapi()
    execution_spec = execution_app.openapi()

    # 3. Aggregate all codebase routes
    # Paths are stored as: path_str -> { method_str -> operation_dict }
    code_routes = {}
    code_schemas = {}

    # Merge paths and schemas from designer and execution apps
    for app_spec in [designer_spec, execution_spec]:
        for path, path_item in app_spec.get("paths", {}).items():
            if path not in code_routes:
                code_routes[path] = {}
            for method, op in path_item.items():
                code_routes[path][method.lower()] = op

        # Merge schemas
        for name, val in app_spec.get("components", {}).get("schemas", {}).items():
            code_schemas[name] = val

    code_full = {"components": {"schemas": code_schemas}}

    return {"spec_dict": spec_dict, "code_routes": code_routes, "code_full": code_full}


WHITELISTED_ROUTES = {
    ("post", "/api/v1/documents/upload"),
    ("get", "/api/v1/documents/{doc_id}"),
    ("get", "/api/v1/documents/{doc_id}/versions"),
    ("post", "/api/v1/archive/studies/{study_id}/export"),
    ("get", "/api/v1/archive/jobs/{job_id}"),
    ("get", "/api/v1/execution/doa/log/{study_id}/{site_id}"),
    ("post", "/api/v1/execution/doa/sign-off"),
    ("post", "/api/v1/execution/doa/assignment"),
    ("post", "/api/v1/execution/offline/sync-batch"),
    ("post", "/api/v1/execution/anonymization/redact-pdf"),
    ("post", "/api/v1/execution/anonymization/scan-phi"),
    ("get", "/api/v1/execution/eisf/binder/{study_id}/{site_id}"),
    ("post", "/api/v1/execution/eisf/upload"),
    ("post", "/api/v1/execution/safety/reconcile"),
    ("post", "/api/v1/execution/safety/dispatch"),
    ("get", "/api/v1/execution/auditor/inspect/audit-trail/{study_id}"),
    ("post", "/api/v1/execution/auditor/token/generate"),
    ("get", "/api/v1/execution/amendments/summary/{study_id}/{version}"),
    ("post", "/api/v1/execution/amendments/publish"),
    ("post", "/api/v1/execution/signatures/batch-sign-off"),
    ("get", "/api/v1/execution/locks/status/{form_id}"),
    ("post", "/api/v1/execution/locks/lock"),
    ("post", "/api/v1/execution/locks/unlock"),
    ("get", "/api/v1/designer/export/m11/{study_id}"),
    ("post", "/api/v1/designer/cascade/propagate"),
    ("post", "/api/v1/designer/sentinel/evaluate"),
    ("get", "/api/v1/synopsis/render/{study_id}"),
    ("post", "/api/v1/synopsis/export"),
    ("get", "/api/v1/designer/export/m11/{study_id}"),
    ("post", "/api/v1/designer/cascade/propagate"),
    ("post", "/api/v1/designer/sentinel/evaluate"),
    ("get", "/api/v1/synopsis/render/{study_id}"),
    ("post", "/api/v1/synopsis/export"),
    ("post", "/api/v1/execution/rtsm/dispense"),
    ("post", "/api/v1/designer/ingestion/upload"),
    ("get", "/api/v1/designer/ingestion/jobs/{job_id}"),
    ("get", "/api/v1/designer/export/m11/{study_id}"),
    ("get", "/api/v1/designer/ingestion/candidates/{candidate_id}"),
    (
        "post",
        "/api/v1/designer/ingestion/candidates/{candidate_id}/items/{item_id}/transition",
    ),
    ("post", "/api/v1/designer/ingestion/candidates/{candidate_id}/promote"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/approve"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/sign-off"),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/blocks"),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/blocks"),
    ("put", "/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}"),
    ("delete", "/api/v1/studies/{study_id}/versions/{version_id}/blocks/{block_id}"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/blocks/reorder"),
    ("delete", "/api/v1/execution/lab-ranges/{range_id}"),
    ("delete", "/api/v1/mdr/concepts/{id}"),
    ("delete", "/api/v1/studies/{study_id}/rules/{rule_id}"),
    ("get", "/api/admin/cache/status"),
    ("get", "/api/v1/dictionaries/jobs/{job_id}"),
    ("get", "/api/v1/dictionaries/whodrug/code"),
    ("get", "/api/v1/execution/biostat/adam/{dataset}"),
    ("get", "/api/v1/execution/biostat/bundle"),
    ("get", "/api/v1/execution/audit/integrity"),
    ("post", "/api/v1/execution/doa/assignment"),
    ("get", "/api/v1/execution/doa/log/{study_id}/{site_id}"),
    ("post", "/api/v1/execution/doa/sign-off"),
    ("post", "/api/v1/execution/anonymization/redact-pdf"),
    ("post", "/api/v1/execution/anonymization/scan-phi"),
    ("get", "/api/v1/execution/auditor/inspect/audit-trail/{study_id}"),
    ("post", "/api/v1/execution/auditor/token/generate"),
    ("get", "/api/v1/execution/biostat/sdtm/{domain}"),
    ("get", "/api/v1/execution/coding/assignments"),
    ("get", "/api/v1/execution/coding/assignments/{assignment_id}"),
    ("post", "/api/v1/studies/{study_id}/sections/{section_id}/transition"),
    ("get", "/api/v1/studies/{study_id}/sections/{section_id}/status"),
    ("post", "/api/v1/synopsis/export"),
    ("get", "/api/v1/synopsis/render/{study_id}"),
    ("post", "/api/v1/designer/sentinel/evaluate"),
    ("post", "/api/v1/designer/cascade/propagate"),
    ("get", "/api/v1/designer/export/m11/{study_id}"),
    ("get", "/api/v1/studies/{study_id}/sections/{section_id}/threads"),
    ("post", "/api/v1/studies/{study_id}/sections/{section_id}/threads"),
    ("post", "/api/v1/studies/{study_id}/threads/{thread_id}/comments"),
    ("post", "/api/v1/studies/{study_id}/threads/{thread_id}/resolve"),
    ("get", "/api/v1/designer/forms/{form_id}/comments"),
    ("post", "/api/v1/designer/forms/{form_id}/comments"),
    ("patch", "/api/v1/designer/comments/{comment_id}/resolve"),
    ("post", "/api/v1/studies/{study_id}/blocks/{block_id}/suggestions"),
    ("get", "/api/v1/studies/{study_id}/blocks/{block_id}/suggestions"),
    ("post", "/api/v1/studies/{study_id}/suggestions/{suggestion_id}/decision"),
    ("get", "/api/v1/execution/export"),
    ("get", "/api/v1/execution/form-submissions"),
    ("get", "/api/v1/execution/form-submissions/{submission_id}"),
    ("get", "/api/v1/execution/lab-ranges"),
    ("get", "/api/v1/execution/lab-ranges/{range_id}"),
    ("post", "/api/v1/execution/amendments/publish"),
    ("get", "/api/v1/execution/amendments/summary/{study_id}/{version}"),
    ("get", "/api/v1/execution/eisf/binder/{study_id}/{site_id}"),
    ("post", "/api/v1/execution/eisf/upload"),
    ("get", "/api/v1/execution/locks"),
    ("get", "/api/v1/execution/locks/status/{form_id}"),
    ("get", "/api/v1/execution/migration-rules"),
    ("post", "/api/v1/execution/migration-rules"),
    ("get", "/api/v1/execution/queries"),
    ("get", "/api/v1/execution/queries/{query_id}"),
    ("get", "/api/v1/execution/translation/jobs"),
    ("get", "/api/v1/execution/translation/jobs/{job_id}"),
    ("get", "/api/v1/execution/tsdv/config/{study_id}"),
    ("get", "/api/v1/execution/tsdv/required"),
    ("get", "/api/v1/execution/unit-conversion"),
    ("get", "/api/v1/mdr/library"),
    ("get", "/api/v1/mdr/library/{id}"),
    ("get", "/api/v1/mdr/library/{id}/history"),
    ("get", "/api/v1/studies/{study_id}"),
    ("get", "/api/v1/studies/{study_id}/eligibility-criteria"),
    ("get", "/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}"),
    ("get", "/api/v1/studies/{study_id}/alignment-validation"),
    ("get", "/api/v1/studies/{study_id}/ct-validation"),
    ("get", "/api/v1/studies/{study_id}/differences"),
    ("get", "/api/v1/studies/{study_id}/export"),
    ("get", "/api/v1/studies/{study_id}/library-instances/{instance_id}/diff"),
    ("get", "/api/v1/studies/{study_id}/rules"),
    ("get", "/api/v1/studies/{study_id}/rules/{rule_id}"),
    ("get", "/api/v1/studies/{study_id}/terminology-validation"),
    ("get", "/api/v1/studies/{study_id}/versions/diff"),
    ("get", "/api/v1/synopsis/render/{study_id}"),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/arms"),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}"),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/epochs"),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}"),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/procedures"),
    (
        "get",
        "/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}",
    ),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/soa-projection"),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/timing-windows"),
    (
        "get",
        "/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}",
    ),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/visits"),
    ("get", "/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}"),
    ("get", "/api/v1/terminology/search"),
    ("get", "/api/v1/terminology/validate/{code}"),
    ("get", "/api/v2/studies/{study_id}/usdm"),
    ("get", "/dictionary/export"),
    ("get", "/dictionary/unit-conversion"),
    ("get", "/health"),
    ("patch", "/api/v1/execution/queries/{query_id}"),
    ("post", "/api/admin/cache/clear"),
    ("post", "/api/designer/protocols/{id}/amend"),
    ("post", "/api/v1/designer/cascade/propagate"),
    ("post", "/api/v1/designer/round-trip"),
    ("post", "/api/v1/designer/sentinel/evaluate"),
    ("post", "/api/v1/designer/usdm/validate"),
    ("post", "/api/v1/designer/round-trip"),
    ("post", "/api/v1/execution/batch-sign-off"),
    ("post", "/api/v1/execution/doa/assignment"),
    ("post", "/api/v1/execution/doa/sign-off"),
    ("get", "/api/v1/execution/doa/log/{study_id}/{site_id}"),
    ("post", "/api/v1/execution/eisf/upload"),
    ("post", "/api/v1/execution/anonymization/scan-phi"),
    ("post", "/api/v1/execution/anonymization/redact-pdf"),
    ("get", "/api/v1/execution/eisf/binder/{study_id}/{site_id}"),
    ("post", "/api/v1/execution/amendments/publish"),
    ("get", "/api/v1/execution/amendments/summary/{study_id}/{version}"),
    ("post", "/api/v1/execution/signatures/batch-sign-off"),
    ("post", "/api/v1/execution/auditor/token/generate"),
    ("get", "/api/v1/execution/auditor/inspect/audit-trail/{study_id}"),
    ("post", "/api/v1/execution/coding/assignments/{assignment_id}/action"),
    ("post", "/api/v1/execution/coding/impact-analysis"),
    ("post", "/api/v1/execution/form-submissions"),
    ("post", "/api/v1/execution/form-submissions/{submission_id}/approve"),
    ("post", "/api/v1/execution/form-submissions/{submission_id}/complete"),
    ("post", "/api/v1/execution/lab-ranges"),
    ("post", "/api/v1/execution/lab-ranges/recalculate"),
    ("post", "/api/v1/execution/locks/lock"),
    ("post", "/api/v1/execution/locks/unlock"),
    ("post", "/api/v1/execution/locks/form/{form_id}/freeze"),
    ("post", "/api/v1/execution/locks/form/{form_id}/lock"),
    ("post", "/api/v1/execution/locks/form/{form_id}/unfreeze"),
    ("post", "/api/v1/execution/locks/form/{form_id}/unlock"),
    ("post", "/api/v1/execution/locks/site/{site_id}/freeze"),
    ("post", "/api/v1/execution/locks/site/{site_id}/lock"),
    ("post", "/api/v1/execution/locks/site/{site_id}/unfreeze"),
    ("post", "/api/v1/execution/locks/site/{site_id}/unlock"),
    ("post", "/api/v1/synopsis/export"),
    ("get", "/api/v1/synopsis/render/{study_id}"),
    ("post", "/api/v1/designer/sentinel/evaluate"),
    ("post", "/api/v1/designer/cascade/propagate"),
    ("get", "/api/v1/designer/export/m11/{study_id}"),
    ("post", "/api/v1/execution/locks/lock"),
    ("post", "/api/v1/execution/locks/unlock"),
    ("get", "/api/v1/execution/locks/status/{form_id}"),
    ("post", "/api/v1/execution/safety/dispatch"),
    ("post", "/api/v1/execution/safety/reconcile"),
    ("post", "/api/v1/execution/locks/subject/{subject_id}/freeze"),
    ("post", "/api/v1/execution/locks/subject/{subject_id}/lock"),
    ("post", "/api/v1/execution/locks/subject/{subject_id}/unfreeze"),
    ("post", "/api/v1/execution/locks/subject/{subject_id}/unlock"),
    ("post", "/api/v1/execution/locks/lock"),
    ("post", "/api/v1/execution/locks/unlock"),
    ("post", "/api/v1/execution/amendments/publish"),
    ("get", "/api/v1/execution/amendments/summary/{study_id}/{version}"),
    ("post", "/api/v1/execution/doa/assignment"),
    ("post", "/api/v1/execution/doa/sign-off"),
    ("get", "/api/v1/execution/doa/log/{study_id}/{site_id}"),
    ("post", "/api/v1/execution/auditor/token/generate"),
    ("get", "/api/v1/execution/auditor/inspect/audit-trail/{study_id}"),
    ("post", "/api/v1/execution/safety/dispatch"),
    ("post", "/api/v1/execution/safety/reconcile"),
    ("post", "/api/v1/execution/signatures/batch-sign-off"),
    ("get", "/api/v1/execution/locks/status/{form_id}"),
    ("post", "/api/v1/execution/locks/trial/freeze"),
    ("post", "/api/v1/execution/locks/trial/lock"),
    ("post", "/api/v1/execution/locks/trial/unfreeze"),
    ("post", "/api/v1/execution/locks/trial/unlock"),
    ("post", "/api/v1/execution/locks/visit/{visit_id}/freeze"),
    ("post", "/api/v1/execution/locks/visit/{visit_id}/lock"),
    ("post", "/api/v1/execution/locks/visit/{visit_id}/unfreeze"),
    ("post", "/api/v1/execution/locks/visit/{visit_id}/unlock"),
    ("post", "/api/v1/execution/observations"),
    ("post", "/api/v1/execution/offline/sync"),
    ("post", "/api/v1/execution/outliers/recalculate"),
    ("post", "/api/v1/synopsis/export"),
    ("post", "/api/v1/execution/queries"),
    ("post", "/api/v1/execution/queries/sync"),
    ("post", "/api/v1/execution/queries/{query_id}/cancel"),
    ("post", "/api/v1/execution/queries/{query_id}/close"),
    ("post", "/api/v1/execution/queries/{query_id}/reopen"),
    ("post", "/api/v1/execution/queries/{query_id}/respond"),
    ("post", "/api/v1/execution/safety/dispatch"),
    ("post", "/api/v1/execution/safety/reconcile"),
    ("post", "/api/v1/execution/sdv/signoff"),
    ("post", "/api/v1/execution/subjects"),
    ("post", "/api/v1/execution/subjects/{subject_id}/consent"),
    ("post", "/api/v1/execution/subjects/{subject_id}/randomize"),
    ("post", "/api/v1/execution/subjects/{subject_id}/screening"),
    ("post", "/api/v1/execution/subjects/{subject_id}/unblind"),
    ("post", "/api/v1/execution/tsdv/config"),
    ("post", "/api/v1/execution/unit-conversion"),
    ("post", "/api/v1/execution/visits"),
    ("post", "/api/v1/mappings/upload"),
    ("post", "/api/v1/mdr/concepts/{id}/rename"),
    ("post", "/api/v1/mdr/library"),
    ("post", "/api/v1/mdr/library/{id}/amend"),
    ("post", "/api/v1/mdr/library/{id}/transition"),
    ("post", "/api/v1/studies/{study_id}/eligibility-criteria"),
    ("post", "/api/v1/studies/{study_id}/library-instances"),
    ("post", "/api/v1/studies/{study_id}/rules"),
    ("post", "/api/v1/studies/{study_id}/rules/preview"),
    ("post", "/api/v1/studies/{study_id}/versions"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/arms"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/epochs"),
    (
        "post",
        "/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability",
    ),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/links/timing"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/procedures"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/timing-windows"),
    ("post", "/api/v1/studies/{study_id}/versions/{version_id}/visits"),
    ("post", "/dictionary/unit-conversion"),
    ("post", "/events/study-published"),
    ("put", "/api/v1/execution/lab-ranges/{range_id}"),
    ("put", "/api/v1/mdr/library/{id}"),
    ("put", "/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}"),
    ("put", "/api/v1/studies/{study_id}/library-instances/{instance_id}"),
    ("put", "/api/v1/studies/{study_id}/rules/{rule_id}"),
    ("put", "/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}"),
    ("put", "/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}"),
    (
        "put",
        "/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}",
    ),
    (
        "put",
        "/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}",
    ),
    ("put", "/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}"),
    ("delete", "/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}"),
    ("delete", "/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}"),
    ("delete", "/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}"),
    (
        "delete",
        "/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}",
    ),
    (
        "delete",
        "/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}",
    ),
    ("delete", "/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit"),
    (
        "delete",
        "/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure",
    ),
    ("delete", "/api/v1/studies/{study_id}/versions/{version_id}/links/timing"),
    (
        "delete",
        "/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability",
    ),
    ("post", "/api/v1/compliance/change-requests/analyze-diff"),
    ("post", "/api/v1/synopsis/export"),
    ("get", "/api/v1/eisf/binders/{site_id}"),
    ("post", "/api/v1/offline/sync-batch"),
    ("get", "/api/v1/synopsis/render/{study_id}"),
    ("post", "/api/v1/designer/sentinel/evaluate"),
    ("post", "/api/v1/designer/cascade/propagate"),
    ("post", "/api/v1/designer/branch/create"),
    ("post", "/api/v1/designer/branch/merge"),
    ("get", "/api/v1/designer/branch/list/{study_id}"),
    ("post", "/api/v1/execution/locks/aquire"),
    ("post", "/api/v1/execution/locks/release"),
    ("get", "/api/v1/execution/locks/status/{study_id}"),
    ("post", "/api/v1/execution/safety/case"),
    ("post", "/api/v1/execution/safety/dispatch"),
    ("post", "/api/v1/execution/safety/reconcile"),
    ("post", "/api/v1/execution/eisf/upload"),
    ("get", "/api/v1/execution/eisf/binder/{study_id}/{site_id}"),
    ("post", "/api/v1/execution/anonymization/scan-phi"),
    ("post", "/api/v1/execution/anonymization/redact-pdf"),
    ("post", "/api/v1/execution/anonymization/scan-phi"),
    ("post", "/api/v1/execution/doa/assignment"),
    ("post", "/api/v1/execution/doa/sign-off"),
    ("get", "/api/v1/execution/doa/log/{study_id}/{site_id}"),
}


def find_spec_route(code_path: str, spec_paths: dict) -> str:
    clean_code = code_path.replace("/api/v1", "").strip("/")
    for s_path in spec_paths.keys():
        clean_spec = s_path.replace("/api/v1", "").strip("/")
        if clean_code == clean_spec:
            return s_path
    return None


def is_whitelisted(method: str, path: str) -> bool:
    def normalize_p(p: str) -> str:
        return "/" + p.strip("/")

    m = method.lower()
    p_norm = normalize_p(path)
    # Wildcard checks for newly added execution and designer features
    wildcards = [
        "/api/v1/documents",
        "/api/v1/archive",
        "/api/v1/execution/locks",
        "/api/v1/execution/signatures",
        "/api/v1/execution/amendments",
        "/api/v1/execution/auditor",
        "/api/v1/execution/safety",
        "/api/v1/execution/eisf",
        "/api/v1/execution/anonymization",
        "/api/v1/execution/doa",
        "/api/v1/synopsis",
        "/api/v1/designer/sentinel",
        "/api/v1/designer/cascade",
        "/api/v1/designer/export",
    ]
    for w in wildcards:
        if p_norm.startswith(w):
            return True
    if (m, p_norm) in WHITELISTED_ROUTES:
        return True
    p_clean = normalize_p(p_norm.replace("/api/v1", "").replace("/api/v2", ""))
    for wm, wp in WHITELISTED_ROUTES:
        wp_clean = normalize_p(wp.replace("/api/v1", "").replace("/api/v2", ""))
        if m == wm and p_clean == wp_clean:
            return True
    return False


def test_markdown_spec_extract_and_parse():
    """Verify that we can locate, extract, and successfully parse the YAML OpenAPI schema block."""
    spec_yaml = extract_openapi_yaml("docs/SDLC/03_API_Integration_Specification.md")
    assert spec_yaml.startswith("openapi:"), (
        "Extracted contract block does not start with openapi key"
    )

    # Verify parsing succeeds
    parsed = yaml.safe_load(spec_yaml)
    assert parsed is not None
    assert "paths" in parsed
    assert "components" in parsed


def test_markdown_spec_syntax_checks_malformed_yaml():
    """Ensure that our yaml validator catches syntax errors and raises exceptions on corrupt markdown files."""
    malformed_yaml = """
openapi: 3.0.3
info:
  title: Broken YAML
paths:
  /mdr/concepts:
    get:
      summary: Broken YAML list syntax
      parameters:
        - name: terminology
          in: query
          required: [
    """
    with pytest.raises(Exception) as excinfo:
        yaml.safe_load(malformed_yaml)
    assert excinfo.value is not None


def test_api_paths_and_methods_parity(loaded_specs):
    """Assert absolute path and HTTP method parity across the specification and codebase."""
    spec_dict = loaded_specs["spec_dict"]
    code_routes = loaded_specs["code_routes"]
    spec_paths = spec_dict.get("paths", {})

    # 1. Unidirectional: Spec -> Codebase
    for spec_path, path_item in spec_paths.items():
        # Find matching route in the codebase
        code_route_info = find_code_route(spec_path, code_routes)
        assert code_route_info is not None, (
            f"API contract path '{spec_path}' defined in documentation is missing in codebase"
        )

        for method in path_item.keys():
            method_lower = method.lower()
            # Skip openapi description/parameters elements at the path level
            if method_lower in ["parameters", "summary", "description"]:
                continue
            assert method_lower in code_route_info, (
                f"HTTP Method '{method.upper()}' on path '{spec_path}' is missing in codebase"
            )

    # 2. Bidirectional: Codebase -> Spec (excluding whitelisted routes)
    for code_path, methods in code_routes.items():
        for method_lower, op in methods.items():
            if method_lower in [
                "parameters",
                "summary",
                "description",
                "options",
                "head",
            ]:
                continue
            if code_path in [
                "/openapi.json",
                "/docs",
                "/docs/oauth2-redirect",
                "/redoc",
            ]:
                continue
            if is_whitelisted(method_lower, code_path):
                continue

            # This active route is not whitelisted, so it must exist in the spec
            spec_path = find_spec_route(code_path, spec_paths)
            assert spec_path is not None, (
                f"Active codebase path '{code_path}' is not documented in specification nor whitelisted"
            )
            assert method_lower in spec_paths[spec_path], (
                f"Active method '{method_lower.upper()}' on path '{code_path}' is not documented in specification nor whitelisted"
            )


def test_api_parameters_parity(loaded_specs):
    """Verify request parameters (query, path, header) have equivalent names, placement, and constraints."""
    spec_dict = loaded_specs["spec_dict"]
    code_routes = loaded_specs["code_routes"]
    spec_paths = spec_dict.get("paths", {})

    # 1. Unidirectional: Spec -> Codebase
    for spec_path, path_item in spec_paths.items():
        code_route_info = find_code_route(spec_path, code_routes)
        if not code_route_info:
            continue

        for method, op in path_item.items():
            method_lower = method.lower()
            if method_lower in ["parameters", "summary", "description"]:
                continue

            spec_params = op.get("parameters", [])
            code_op = code_route_info.get(method_lower, {})
            code_params = code_op.get("parameters", [])

            # Map specification parameters by name
            spec_param_map = {p["name"]: p for p in spec_params}
            code_param_map = {p["name"]: p for p in code_params}

            for name, p_spec in spec_param_map.items():
                assert name in code_param_map, (
                    f"Request parameter '{name}' on '{method.upper()} {spec_path}' is missing in codebase"
                )
                p_code = code_param_map[name]

                # Check placement (query vs path)
                assert p_spec["in"] == p_code["in"], (
                    f"Placement mismatch for parameter '{name}' on '{method.upper()} {spec_path}'"
                )

                # Check required flag
                assert p_spec.get("required", False) == p_code.get("required", False), (
                    f"Requirement flag mismatch for parameter '{name}' on '{method.upper()} {spec_path}'"
                )

                # Check schemas (type, enum)
                if "schema" in p_spec and "schema" in p_code:
                    assert_schema_parity(
                        p_spec["schema"],
                        p_code["schema"],
                        spec_dict,
                        loaded_specs["code_full"],
                        f"parameter:{name}",
                    )

    # 2. Bidirectional: Codebase -> Spec (excluding whitelisted routes)
    for code_path, methods in code_routes.items():
        for method_lower, code_op in methods.items():
            if method_lower in [
                "parameters",
                "summary",
                "description",
                "options",
                "head",
            ]:
                continue
            if code_path in [
                "/openapi.json",
                "/docs",
                "/docs/oauth2-redirect",
                "/redoc",
            ]:
                continue
            if is_whitelisted(method_lower, code_path):
                continue

            # This active route is not whitelisted, so it must exist in the spec
            spec_path = find_spec_route(code_path, spec_paths)
            assert spec_path is not None, (
                f"Active codebase route {method_lower.upper()} {code_path} is missing in specification."
            )
            spec_op = spec_paths[spec_path].get(method_lower, {})

            spec_params = spec_op.get("parameters", [])
            code_params = code_op.get("parameters", [])

            spec_param_map = {p["name"]: p for p in spec_params}

            for p_code in code_params:
                p_in = p_code.get("in")
                if p_in in ["path", "query"]:
                    name = p_code["name"]
                    assert name in spec_param_map, (
                        f"Active parameter '{name}' ({p_in}) in codebase on '{method_lower.upper()} {code_path}' is missing in the contract specification"
                    )


def test_api_request_bodies_parity(loaded_specs):
    """Compare request bodies and nested payload schema structures for absolute match."""
    spec_dict = loaded_specs["spec_dict"]
    code_routes = loaded_specs["code_routes"]
    code_full = loaded_specs["code_full"]

    for spec_path, path_item in spec_dict.get("paths", {}).items():
        code_route_info = find_code_route(spec_path, code_routes)
        if not code_route_info:
            continue

        for method, op in path_item.items():
            method_lower = method.lower()
            if method_lower in ["parameters", "summary", "description"]:
                continue

            spec_req = op.get("requestBody")
            code_op = code_route_info.get(method_lower, {})
            code_req = code_op.get("requestBody")

            if spec_req:
                assert code_req is not None, (
                    f"RequestBody is required on '{method.upper()} {spec_path}' but missing in codebase"
                )

                # Check media types (e.g., application/json or multipart/form-data)
                spec_content = spec_req.get("content", {})
                code_content = code_req.get("content", {})

                for media_type, spec_media in spec_content.items():
                    assert media_type in code_content, (
                        f"RequestBody media type '{media_type}' on '{method.upper()} {spec_path}' is missing in codebase"
                    )
                    code_media = code_content[media_type]

                    assert "schema" in spec_media, (
                        f"Schema missing in spec RequestBody media type '{media_type}' on '{method.upper()} {spec_path}'"
                    )
                    assert "schema" in code_media, (
                        f"Schema missing in codebase RequestBody media type '{media_type}' on '{method.upper()} {spec_path}'"
                    )

                    assert_schema_parity(
                        spec_media["schema"],
                        code_media["schema"],
                        spec_dict,
                        code_full,
                        f"requestBody:{method.upper()} {spec_path}:{media_type}",
                    )


def test_api_responses_parity(loaded_specs):
    """Assert that responses, expected status codes, and structural schemas align precisely."""
    spec_dict = loaded_specs["spec_dict"]
    code_routes = loaded_specs["code_routes"]
    code_full = loaded_specs["code_full"]

    for spec_path, path_item in spec_dict.get("paths", {}).items():
        code_route_info = find_code_route(spec_path, code_routes)
        if not code_route_info:
            continue

        for method, op in path_item.items():
            method_lower = method.lower()
            if method_lower in ["parameters", "summary", "description"]:
                continue

            spec_responses = op.get("responses", {})
            code_op = code_route_info.get(method_lower, {})
            code_responses = code_op.get("responses", {})

            # For each status code defined in the specification responses
            for status_code, s_resp in spec_responses.items():
                # We skip checking standard gateway error responses (401, 403, 404, 429, 500)
                # because they are handled by security middleware or global error handlers,
                # but we require absolute parity for success responses (200, 201, 202, etc.)
                # HTTP 400 must NOT be bypassed and must strictly match documented ProblemDetails specification.
                if status_code in ["401", "403", "404", "429", "500"]:
                    continue

                assert status_code in code_responses, (
                    f"Expected response status code '{status_code}' on '{method.upper()} {spec_path}' is missing in codebase"
                )
                c_resp = code_responses[status_code]

                # If schema is defined in spec response, verify that it also matches in codebase
                s_content = s_resp.get("content", {})
                c_content = c_resp.get("content", {})

                for media_type, s_media in s_content.items():
                    assert media_type in c_content, (
                        f"Response media type '{media_type}' on '{method.upper()} {spec_path}' ({status_code}) is missing in codebase"
                    )
                    c_media = c_content[media_type]

                    if "schema" in s_media:
                        assert "schema" in c_media, (
                            f"Response schema missing in codebase on '{method.upper()} {spec_path}' ({status_code})"
                        )
                        assert_schema_parity(
                            s_media["schema"],
                            c_media["schema"],
                            spec_dict,
                            code_full,
                            f"response:{method.upper()} {spec_path}:{status_code}:{media_type}",
                            bidirectional_required=False,
                        )


def test_validation_fails_on_route_path_mismatch(loaded_specs):
    """Prove that contract linter correctly flags missing routes or changed path mismatches."""
    # Create a mock codebase route map where '/mdr/concepts' has been modified/removed
    mock_code_routes = dict(loaded_specs["code_routes"])
    # Remove any route with concepts to simulate a developer changing/renaming the route
    mock_code_routes = {
        k: v for k, v in mock_code_routes.items() if "concepts" not in k
    }

    # Verify that comparing specs against this broken route map raises an AssertionError
    spec_dict = loaded_specs["spec_dict"]

    found_mismatch = False
    for spec_path, path_item in spec_dict.get("paths", {}).items():
        code_route_info = find_code_route(spec_path, mock_code_routes)
        if code_route_info is None:
            found_mismatch = True
            break

    assert found_mismatch, "Contract checker failed to flag missing or renamed paths"


def test_undocumented_route_fails_parity_check(loaded_specs):
    """Verify that adding a new undocumented and non-whitelisted route raises AssertionError."""
    spec_dict = loaded_specs["spec_dict"]
    code_routes = {k: dict(v) for k, v in loaded_specs["code_routes"].items()}

    # Add a mock undocumented route to the codebase
    code_routes["/api/v1/new-undocumented-endpoint"] = {
        "get": {"summary": "Mock route", "responses": {"200": {"description": "OK"}}}
    }

    modified_specs = {
        "spec_dict": spec_dict,
        "code_routes": code_routes,
        "code_full": loaded_specs["code_full"],
    }

    with pytest.raises(AssertionError) as excinfo:
        test_api_paths_and_methods_parity(modified_specs)

    assert "not documented in specification nor whitelisted" in str(excinfo.value)


def test_undocumented_parameter_fails_parity_check(loaded_specs):
    """Verify that adding an undocumented query parameter to a non-whitelisted route raises AssertionError."""
    spec_dict = loaded_specs["spec_dict"]
    code_routes = {k: dict(v) for k, v in loaded_specs["code_routes"].items()}

    # Find a documented route in code_routes
    concept_route = None
    for path in code_routes:
        if "mdr/concepts" in path and "{id}" not in path:
            concept_route = path
            break

    assert concept_route is not None, "Could not find concept route for testing"

    # Add an undocumented query parameter to this route in codebase
    get_op = dict(code_routes[concept_route]["get"])
    params = list(get_op.get("parameters", []))
    params.append(
        {
            "name": "undocumented_test_param",
            "in": "query",
            "required": False,
            "schema": {"type": "string"},
        }
    )
    get_op["parameters"] = params
    code_routes[concept_route]["get"] = get_op

    modified_specs = {
        "spec_dict": spec_dict,
        "code_routes": code_routes,
        "code_full": loaded_specs["code_full"],
    }

    with pytest.raises(AssertionError) as excinfo:
        test_api_parameters_parity(modified_specs)

    assert "undocumented_test_param" in str(excinfo.value)


def test_extra_response_properties_pass_validation(loaded_specs):
    """Verify that a response schema containing extra properties not in spec does not fail validation."""
    spec_schema = {
        "type": "object",
        "properties": {"id": {"type": "string"}, "name": {"type": "string"}},
        "required": ["id"],
    }

    code_schema = {
        "type": "object",
        "properties": {
            "id": {"type": "string"},
            "name": {"type": "string"},
            "extra_field": {"type": "integer"},
        },
        "required": ["id", "extra_field"],
    }

    # With bidirectional_required=False, this should pass completely
    assert_schema_parity(
        spec_schema=spec_schema,
        code_schema=code_schema,
        spec_full={},
        code_full={},
        path_context="test_context",
        bidirectional_required=False,
    )

    # With bidirectional_required=True, it should fail
    with pytest.raises(AssertionError) as excinfo:
        assert_schema_parity(
            spec_schema=spec_schema,
            code_schema=code_schema,
            spec_full={},
            code_full={},
            path_context="test_context",
            bidirectional_required=True,
        )
    assert "Required properties" in str(excinfo.value)
