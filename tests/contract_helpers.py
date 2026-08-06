import os
from typing import Any


def rewrite_references(data: Any, prefix: str, visited: set | None = None) -> Any:
    """Recursively rewrite component references in an OpenAPI schema payload."""
    if visited is None:
        visited = set()

    if id(data) in visited:
        return {
            "type": "object",
            "description": "Circular reference detected and isolated",
        }

    if isinstance(data, dict):
        visited.add(id(data))
        new_data = {}
        for k, v in data.items():
            if (
                k == "$ref"
                and isinstance(v, str)
                and v.startswith("#/components/schemas/")
            ):
                ref_name = v[len("#/components/schemas/") :]
                new_data[k] = f"#/components/schemas/{prefix}{ref_name}"
            else:
                new_data[k] = rewrite_references(v, prefix, visited)
        visited.remove(id(data))
        return new_data
    if isinstance(data, list):
        visited.add(id(data))
        new_list = [rewrite_references(item, prefix, visited) for item in data]
        visited.remove(id(data))
        return new_list
    return data


def extract_openapi_yaml(filepath: str) -> str:
    """Locate and extract the YAML spec from SDLC file."""
    if not os.path.exists(filepath):
        raise FileNotFoundError(f"Markdown specification file not found: {filepath}")

    with open(filepath, encoding="utf-8") as f:
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


def resolve_ref(
    schema: Any, spec: dict[str, Any], seen: set[str] | None = None
) -> tuple[Any, str | None]:
    """Recursively resolve $ref references in the spec dictionary with recursion guard."""
    if seen is None:
        seen = set()
    if isinstance(schema, dict) and "$ref" in schema:
        ref = schema["$ref"]
        if ref in seen:
            return {
                "type": "object",
                "description": f"Recursive reference to {ref}",
            }, ref

        ref_path = ref.split("/")
        resolved = spec
        for part in ref_path[1:]:
            resolved = resolved.get(part, {}) if isinstance(resolved, dict) else {}

        new_seen = seen | {ref}
        inner_resolved, inner_ref = resolve_ref(resolved, spec, new_seen)
        return inner_resolved, (inner_ref or ref)
    return schema, None


def resolve_schema(schema: Any, spec: dict[str, Any], seen: Any = None) -> Any:
    """Recursively resolve $ref references in the spec dictionary with recursion guard (backward compatibility)."""
    if seen is None:
        seen = set()
    if isinstance(schema, dict):
        if "$ref" in schema:
            ref = schema["$ref"]
            if ref in seen:
                return {
                    "type": "object",
                    "description": f"Recursive reference to {ref}",
                }
            new_seen = set(seen)
            new_seen.add(ref)
            ref_path = ref.split("/")
            resolved = spec
            for part in ref_path[1:]:
                resolved = resolved.get(part, {}) if isinstance(resolved, dict) else {}
            return resolve_schema(resolved, spec, new_seen)
        return {k: resolve_schema(v, spec, seen) for k, v in schema.items()}
    if isinstance(schema, list):
        return [resolve_schema(item, spec, seen) for item in schema]
    return schema


def normalize_type(t: Any) -> Any:
    """Normalize types and handles nullable union structures."""
    if isinstance(t, list):
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
    return {type_spec, type_code} == {"number", "float"} or {type_spec, type_code} == {
        "number",
        "integer",
    }


def normalize_schema(schema: Any, spec: dict[str, Any], seen: set | None = None) -> Any:
    """Symmetrically normalize nullable schemas and complex multi-value schema types."""
    if seen is None:
        seen = set()
    schema, _ = resolve_ref(schema, spec)
    if not isinstance(schema, dict):
        return schema

    schema = dict(schema)

    # 1. Handle anyOf / oneOf with null
    for key in ["anyOf", "oneOf"]:
        if key in schema and isinstance(schema[key], list):
            union_list = schema[key]
            has_null = False
            non_null_schemas = []
            for sub in union_list:
                sub_resolved, _ = resolve_ref(sub, spec)
                if (
                    isinstance(sub_resolved, dict)
                    and sub_resolved.get("type") == "null"
                ):
                    has_null = True
                else:
                    non_null_schemas.append(sub)

            if has_null:
                schema["nullable"] = True
                if len(non_null_schemas) == 1:
                    sub_norm = normalize_schema(non_null_schemas[0], spec, seen)
                    if isinstance(sub_norm, dict):
                        schema.pop(key, None)
                        for k, v in sub_norm.items():
                            schema[k] = v
                        schema["nullable"] = True
                    else:
                        return sub_norm
                else:
                    schema[key] = non_null_schemas

    # 2. Handle type list containing "null"
    type_val = schema.get("type")
    if isinstance(type_val, list):
        if "null" in type_val:
            schema["nullable"] = True
            non_null_types = [t for t in type_val if t != "null"]
            if len(non_null_types) == 1:
                schema["type"] = non_null_types[0]
            else:
                schema["type"] = non_null_types
    elif type_val == "null":
        schema["nullable"] = True
        schema["type"] = None

    # 3. Handle explicit nullable: True
    if schema.get("nullable") is True:
        schema["nullable"] = True

    return schema


def _compare_schemas_internal(
    spec_schema: Any,
    code_schema: Any,
    spec_full: dict[str, Any],
    code_full: dict[str, Any],
    path_context: str,
    bidirectional_required: bool,
    seen_pairs: set[tuple[str, str]],
    diffs: list[str],
) -> None:
    # Resolve references
    s_resolved, s_ref = resolve_ref(spec_schema, spec_full)
    c_resolved, c_ref = resolve_ref(code_schema, code_full)

    # If both sides have references and we are currently comparing them, stop recursion safely.
    if s_ref and c_ref:
        pair = (s_ref, c_ref)
        if pair in seen_pairs:
            return
        seen_pairs = seen_pairs | {pair}

    s_norm = normalize_schema(s_resolved, spec_full)
    c_norm = normalize_schema(c_resolved, code_full)

    if not isinstance(s_norm, dict) or not isinstance(c_norm, dict):
        if type(s_norm) is not type(c_norm):
            diffs.append(
                f"Type mismatch at {path_context}: spec={type(s_norm).__name__}, code={type(c_norm).__name__}"
            )
            return
        if s_norm != c_norm:
            diffs.append(
                f"Value mismatch at {path_context}: spec={s_norm}, code={c_norm}"
            )
            return
        return

    # Compare nullable flag symmetrically
    s_nullable = s_norm.get("nullable", False)
    c_nullable = c_norm.get("nullable", False)
    if s_nullable != c_nullable:
        diffs.append(
            f"Nullable flag mismatch at {path_context}: spec={s_nullable}, code={c_nullable}"
        )

    # Compare type
    s_type = s_norm.get("type")
    c_type = c_norm.get("type")
    if s_type or c_type:
        if not compare_types(s_type, c_type):
            diffs.append(
                f"Mismatched type at {path_context}: spec={s_type}, code={c_type}"
            )

    # Compare Enum values
    if "enum" in s_norm:
        if "enum" not in c_norm:
            diffs.append(
                f"Missing enum constraint in codebase schema at {path_context}"
            )
        else:
            s_enum = set(s_norm["enum"])
            c_enum = set(c_norm["enum"])
            if s_enum != c_enum:
                diffs.append(
                    f"Mismatched enum values at {path_context}: spec={sorted(list(s_enum))}, code={sorted(list(c_enum))}"
                )

    # Compare Properties for objects
    if s_type == "object" or "properties" in s_norm:
        s_props = s_norm.get("properties", {})
        c_props = c_norm.get("properties", {})

        # Verify that all properties defined in spec exist in code and match
        for prop_name, prop_spec in s_props.items():
            if prop_name not in c_props:
                diffs.append(
                    f"Property '{prop_name}' defined in contract specification is missing in codebase at {path_context}"
                )
            else:
                _compare_schemas_internal(
                    spec_schema=prop_spec,
                    code_schema=c_props[prop_name],
                    spec_full=spec_full,
                    code_full=code_full,
                    path_context=f"{path_context}.{prop_name}"
                    if path_context
                    else prop_name,
                    bidirectional_required=bidirectional_required,
                    seen_pairs=seen_pairs,
                    diffs=diffs,
                )

        # Compare Required fields list
        s_req = set(s_norm.get("required", []))
        c_req = set(c_norm.get("required", []))

        # Ensure bidirectional parity of required fields if requested
        missing_in_code = s_req - c_req
        if missing_in_code:
            diffs.append(
                f"Required properties {sorted(list(missing_in_code))} in spec contract are not marked required in codebase at {path_context}"
            )

        if bidirectional_required:
            missing_in_spec = c_req - s_req
            if missing_in_spec:
                diffs.append(
                    f"Required properties {sorted(list(missing_in_spec))} in codebase are not marked required in spec contract at {path_context}"
                )

    # Compare Items for arrays
    if s_type == "array" or "items" in s_norm:
        if "items" not in c_norm:
            diffs.append(
                f"Array schema missing 'items' property in codebase at {path_context}"
            )
        else:
            _compare_schemas_internal(
                spec_schema=s_norm["items"],
                code_schema=c_norm["items"],
                spec_full=spec_full,
                code_full=code_full,
                path_context=f"{path_context}[]",
                bidirectional_required=bidirectional_required,
                seen_pairs=seen_pairs,
                diffs=diffs,
            )


def assert_schema_parity(
    spec_schema: Any,
    code_schema: Any,
    spec_full: dict[str, Any],
    code_full: dict[str, Any],
    path_context: str = "",
    bidirectional_required: bool = True,
) -> None:
    """Compare two OpenAPI schemas semantically for complete parity with detailed diff collection."""
    diffs = []
    _compare_schemas_internal(
        spec_schema=spec_schema,
        code_schema=code_schema,
        spec_full=spec_full,
        code_full=code_full,
        path_context=path_context,
        bidirectional_required=bidirectional_required,
        seen_pairs=set(),
        diffs=diffs,
    )
    if diffs:
        error_msg = f"Contract validation failed at {path_context or 'root'}. Discrepancies found:\n"
        error_msg += "\n".join(f"- {d}" for d in diffs)
        raise AssertionError(error_msg)


def find_code_route(
    spec_path: str, code_routes: dict[str, Any], tags: list[str] | None = None
) -> Any:
    """Match a specification relative path to its registered codebase route, stripping prefixes like /api/v1."""
    if tags is None:
        tags = []

    # Determine likely prefix from tags
    pfx_match = None
    for pfx in [
        "designer",
        "execution",
        "org",
        "eisf",
        "econsent",
        "ctms",
        "etmf",
        "quality",
    ]:
        if pfx in tags or pfx.upper() in tags or pfx.capitalize() in tags:
            pfx_match = "/" + pfx
            break

    # Strip prefixes like /api/v1 or leading/trailing slashes for comparison
    clean_spec = spec_path.replace("/api/v1", "").strip("/")

    # Try matching prioritizing pfx_match first
    if pfx_match:
        for c_path, c_route_info in code_routes.items():
            if not c_path.startswith(pfx_match):
                continue
            norm_path = c_path
            for pfx in [
                "/designer",
                "/execution",
                "/org",
                "/eisf",
                "/econsent",
                "/ctms",
                "/etmf",
                "/quality",
            ]:
                if c_path.startswith(pfx):
                    norm_path = c_path[len(pfx) :]
                    break
            clean_code = norm_path.replace("/api/v1", "").strip("/")
            if clean_spec == clean_code:
                return c_route_info

    # Fallback to any match if no prioritized match found
    for c_path, c_route_info in code_routes.items():
        norm_path = c_path
        for pfx in [
            "/designer",
            "/execution",
            "/org",
            "/eisf",
            "/econsent",
            "/ctms",
            "/etmf",
            "/quality",
        ]:
            if c_path.startswith(pfx):
                norm_path = c_path[len(pfx) :]
                break
        clean_code = norm_path.replace("/api/v1", "").strip("/")
        if clean_spec == clean_code:
            return c_route_info
    return None


def find_spec_route(code_path: str, spec_paths: dict) -> str:
    """Match a codebase path to its registered specification path, stripping prefixes like /api/v1."""
    norm_path = code_path
    for pfx in [
        "/designer",
        "/execution",
        "/org",
        "/eisf",
        "/econsent",
        "/ctms",
        "/etmf",
        "/quality",
    ]:
        if code_path.startswith(pfx):
            norm_path = code_path[len(pfx) :]
            break
    clean_code = norm_path.replace("/api/v1", "").strip("/")
    for s_path in spec_paths:
        clean_spec = s_path.replace("/api/v1", "").strip("/")
        if clean_code == clean_spec:
            return s_path
    return None
