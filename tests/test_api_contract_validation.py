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
            )

        # Compare Required fields list
        s_req = set(s_resolved.get("required", []))
        c_req = set(c_resolved.get("required", []))
        # Ensure that required fields in specification are also required in codebase
        missing_reqs = s_req - c_req
        assert not missing_reqs, (
            f"Required properties {missing_reqs} in spec contract are not marked required in codebase at {path_context}"
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

    for spec_path, path_item in spec_dict.get("paths", {}).items():
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


def test_api_parameters_parity(loaded_specs):
    """Verify request parameters (query, path, header) have equivalent names, placement, and constraints."""
    spec_dict = loaded_specs["spec_dict"]
    code_routes = loaded_specs["code_routes"]

    for spec_path, path_item in spec_dict.get("paths", {}).items():
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
                if status_code in ["401", "403", "404", "429", "500", "400"]:
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
