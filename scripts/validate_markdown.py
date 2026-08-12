#!/usr/bin/env python3
# CI Trigger
"""
Repository-Wide Custom Markdown Linter
Statically validates workspace paths/links and dry-runs CLI subcommands.
"""

import ast
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
from pathlib import Path

# Common developer tools/executables we whitelist even if not natively installed
ALLOWED_COMMON_TOOLS = {
    "git",
    "docker",
    "docker-compose",
    "python",
    "python3",
    "pip",
    "pip3",
    "pytest",
    "ruff",
    "pnpm",
    "npm",
    "yarn",
    "node",
    "nvm",
    "uv",
    "bash",
    "sh",
    "curl",
    "wget",
    "cat",
    "grep",
    "echo",
    "cd",
    "ls",
    "source",
    "export",
    "powershell",
    "pre-commit",
    "poetry",
    "make",
    "chmod",
    "sudo",
    "aws",
    "gcloud",
    "kubectl",
    "helm",
    "touch",
    "mkdir",
    "rm",
    "cp",
    "mv",
    "set",
    "systemctl",
    "tail",
    "gunzip",
    "pg_backrest",
    "neo4j-admin",
    "neo4j",
    "cypher-shell",
    "EOF",
    "tee",
    "powershell",
    "pre-commit",
}

# Regex to check if a flag is syntactically well-formed (cannot start with triple dashes)
FLAG_PATTERN = re.compile(
    r"^-[a-zA-Z0-9][a-zA-Z0-9_-]*(=.*)?$|^--[a-zA-Z0-9][a-zA-Z0-9_-]*(=.*)?$"
)

# List of errors collected during scanning
errors = []


def add_error(file_path, line_no, message):
    errors.append({"file": str(file_path), "line": line_no, "message": message})


def clean_token(token):
    """Strips surrounding quotes, parentheses, brackets, braces, backticks, underscores and trailing punctuation from a token."""
    token = token.strip()
    while token and token[-1] in "`'\"()[]{}<>,;:!?.)_":
        token = token[:-1]
    while token and token[0] in "`'\"()[]{}<>,;:!?(_":
        token = token[1:]
    return token


def is_potential_path_ref(token, root_dirs, root_files):
    """
    Statically decides whether a cleaned token represents a local path reference
    that must be validated.
    """
    if not token:
        return False

    # Ignore flags
    if token.startswith("-"):
        return False

    # Ignore web/external links
    if (
        token.startswith(("http://", "https://", "mailto:", "tel:"))
        or "://" in token
        or token.startswith("#")
    ):
        return False

    # Ignore environment variables and placeholder syntax
    if any(char in token for char in ("$", "*", "<", ">", "{", "}", "[", "]")):
        return False
    if (
        "placeholder" in token.lower()
        or "your-" in token.lower()
        or "example" in token.lower()
        or "templates" in token.lower()
        or "node.js" in token.lower()
    ):
        return False

    # Ignore absolute system/container paths
    if token.startswith(
        (
            "/dev/",
            "/opt/",
            "/bin/",
            "/usr/",
            "/etc/",
            "/proc/",
            "/sys/",
            "/var/",
            "/tmp/",  # nosec B108
        )
    ):
        return False

    # If starts with leading slash, only treat as path if the first component is a root dir/file
    if token.startswith("/"):
        normalized = token.lstrip("/")
        parts = normalized.split("/")
        if not parts or (parts[0] not in root_dirs and parts[0] not in root_files):
            return False

    # Check if starts with relative path prefix
    if token.startswith(("./", "../")):
        return True

    # Check if starts with a known root directory
    parts = token.replace("\\", "/").split("/")
    if parts[0] in root_dirs:
        return True

    # Check if is exactly one of the root files
    if token in root_files:
        return True

    # If it contains a slash and ends with a typical code/config/doc extension
    if "/" in token:
        # Avoid things like "and/or", "true/false"
        ext = os.path.splitext(token)[1].lower()
        if ext in (
            ".py",
            ".md",
            ".toml",
            ".json",
            ".sh",
            ".yml",
            ".yaml",
            ".js",
            ".mjs",
            ".ts",
            ".tsx",
            ".html",
            ".css",
            ".txt",
            ".xml",
            ".lock",
            ".db",
        ):
            return True

    return False


def resolve_path(path_str, md_file_path, repo_root, root_dirs, root_files):
    """Resolves path string relative to workspace or markdown directory."""
    path_str = path_str.strip()
    if not path_str:
        return None

    # Ignore gitignored build folders or coverage folders
    if any(
        p in path_str.replace("\\", "/").split("/")
        for p in ("dist", "build", "node_modules", "coverage")
    ):
        return None

    # Ignore web/external URLs
    if (
        path_str.startswith(("http://", "https://", "mailto:", "tel:"))
        or "://" in path_str
        or path_str.startswith("#")
    ):
        return None

    # Strip query parameters, anchors, or pytest-style class/function selectors (e.g., #, ?, or ::)
    path_str = path_str.split("#")[0].split("?")[0].split("::")[0].strip()
    if not path_str:
        return None

    # Ignore environment variables and placeholder syntax
    if any(char in path_str for char in ("$", "*", "<", ">", "{", "}", "[", "]")):
        return None
    if (
        "placeholder" in path_str.lower()
        or "your-" in path_str.lower()
        or "example" in path_str.lower()
        or "templates" in path_str.lower()
        or "node.js" in path_str.lower()
        or "core-models" in path_str.lower()
    ):
        return None

    # Standardize path separators
    path_str = path_str.replace("\\", "/")

    # Ignore compiled/generated build artifacts that are typically gitignored
    path_parts = path_str.split("/")
    if "dist" in path_parts or "build" in path_parts or "node_modules" in path_parts:
        return None

    # Strip leading slash for workspace relative resolve
    stripped_path = path_str.lstrip("/")

    # Absolute repo-level path starting with /app/ or any custom leading slash
    if path_str.startswith("/"):
        # Ignore absolute system/container paths
        if not path_str.startswith(
            (
                "/dev/",
                "/opt/",
                "/bin/",
                "/usr/",
                "/etc/",
                "/proc/",
                "/sys/",
                "/var/",
                "/tmp/",  # nosec B108
            )
        ):
            if path_str.startswith("/app/"):
                stripped_path = path_str[5:]
            return repo_root / stripped_path
        return None

    # If it starts with a known root dir or root file, resolve relative to root
    first_part = stripped_path.split("/")[0]
    if first_part in root_dirs or first_part in root_files:
        candidate = repo_root / stripped_path
        if candidate.exists():
            return candidate

        # Fallback for decentralized tests: if path starts with tests/test_*, search in workspace test dirs
        if first_part == "tests" and "/test_" in stripped_path:
            test_filename = os.path.basename(stripped_path)
            for search_dir in ("apps", "packages", "scripts", "tests"):
                for root, _, files in os.walk(repo_root / search_dir):
                    if test_filename in files:
                        return Path(root) / test_filename

        return candidate

    # Relative path starts with ./ or ../
    if path_str.startswith(("./", "../")):
        return md_file_path.parent / path_str

    # Default: resolve relative to current file's directory
    return md_file_path.parent / path_str


def validate_path(
    path_str, md_file_path, line_no, repo_root, root_dirs, root_files, ref_type="path"
):
    """Resolves and checks if a path exists within the repository boundary."""
    resolved = resolve_path(path_str, md_file_path, repo_root, root_dirs, root_files)
    if not resolved:
        return

    try:
        # Check repository boundary
        resolved_absolute = resolved.resolve()
        if (
            repo_root not in resolved_absolute.parents
            and resolved_absolute != repo_root
        ):
            # Escaped repository boundary
            return
    except Exception:
        # Resolving physical path failed (could mean file doesn't exist)
        pass

    if not resolved.exists():
        add_error(
            md_file_path, line_no, f"Referenced {ref_type} '{path_str}' does not exist."
        )


def validate_docker_compose_args(
    compose_args, line_no, md_file_path, repo_root, root_dirs, root_files
):
    """Checks referenced compose files and dry-runs syntax validation."""
    compose_files = []
    i = 0
    limit = len(compose_args)
    while i < limit:
        if compose_args[i] in ("-f", "--file") and i + 1 < limit:
            compose_files.append(compose_args[i + 1])
            i += 2
            continue
        i += 1

    for compose_file in compose_files:
        # Ignore files with placeholders/variables
        if any(
            char in compose_file for char in ("$", "*", "<", ">", "{", "}", "[", "]")
        ):
            continue
        if (
            "placeholder" in compose_file.lower()
            or "your-" in compose_file.lower()
            or "example" in compose_file.lower()
        ):
            continue

        resolved_cf = resolve_path(
            compose_file, md_file_path, repo_root, root_dirs, root_files
        )
        if not resolved_cf or not resolved_cf.exists():
            add_error(
                md_file_path,
                line_no,
                f"Docker compose file '{compose_file}' does not exist.",
            )
            continue

        # Dry-run docker compose file config if docker command is available
        if shutil.which("docker"):
            try:
                subprocess.run(
                    ["docker", "compose", "-f", str(resolved_cf), "config"],
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.PIPE,
                    cwd=str(repo_root),
                    check=True,
                )
            except subprocess.CalledProcessError as e:
                add_error(
                    md_file_path,
                    line_no,
                    f"Docker compose validation failed for '{compose_file}': {e.stderr.decode().strip()}",
                )


def validate_cli_command(args, line_no, md_file_path, repo_root, root_dirs, root_files):
    """Statically and safely validates a CLI command and its subcommands/targets/flags."""
    if not args:
        return

    # Ignore configuration lines or assignment lines like restore_command = ...
    if len(args) >= 2 and args[1] == "=":
        return

    # Ignore shell variables, shell subshells/expansions
    if any("$" in arg or "(" in arg or ")" in arg for arg in args):
        return

    # Skip prepended env variables like PORT=3000 pnpm start
    while args and "=" in args[0] and not args[0].startswith("-"):
        args.pop(0)

    if not args:
        return

    executable = args[0]

    # If command starts with ./ or ../ or is a path
    if executable.startswith(("./", "../")) or "/" in executable:
        resolved_exec = resolve_path(
            executable, md_file_path, repo_root, root_dirs, root_files
        )
        if resolved_exec and not resolved_exec.exists():
            # Try workspace relative
            alt_path = repo_root / executable.lstrip("./")
            if alt_path.exists():
                resolved_exec = alt_path

        if not resolved_exec or not resolved_exec.exists():
            add_error(
                md_file_path, line_no, f"Executable file '{executable}' does not exist."
            )
            return
        # Skip standard execution validation for custom local script, as long as it exists
        return

    # Check if executable exists or is in common tools whitelist
    if shutil.which(executable) is None and executable not in ALLOWED_COMMON_TOOLS:
        add_error(
            md_file_path,
            line_no,
            f"Executable '{executable}' is not installed/found in PATH.",
        )
        return

    # Check flags for obvious typos (e.g. triple dash or trailing punctuation)
    for arg in args[1:]:
        if arg.startswith("-") and not FLAG_PATTERN.match(arg):
            add_error(
                md_file_path,
                line_no,
                f"Malformed or invalid CLI flag structure: '{arg}'",
            )

    # Handle specialized tools
    if executable == "docker" and len(args) >= 2 and args[1] == "compose":
        validate_docker_compose_args(
            args[2:], line_no, md_file_path, repo_root, root_dirs, root_files
        )
    elif executable == "docker-compose":
        validate_docker_compose_args(
            args[1:], line_no, md_file_path, repo_root, root_dirs, root_files
        )
    elif executable in ("python", "python3", "pytest"):
        # Verify python/pytest targets actually exist on disk
        for arg in args[1:]:
            if not arg.startswith("-") and ("." in arg or "/" in arg):
                # Ignore placeholders
                if any(
                    char in arg for char in ("$", "*", "<", ">", "{", "}", "[", "]")
                ):
                    continue
                if (
                    "placeholder" in arg.lower()
                    or "your-" in arg.lower()
                    or "example" in arg.lower()
                ):
                    continue
                resolved_arg = resolve_path(
                    arg, md_file_path, repo_root, root_dirs, root_files
                )
                if resolved_arg and not resolved_arg.exists():
                    add_error(
                        md_file_path,
                        line_no,
                        f"Target path '{arg}' for executable '{executable}' does not exist.",
                    )


def build_codebase_map(repo_root):
    # Map of name -> list of dicts: {'file_path': Path, 'type': 'function'|'class', 'node': AST_node}
    codebase_map = {}
    for root, dirs, files in os.walk(repo_root):
        dirs[:] = [
            d
            for d in dirs
            if d
            not in {
                ".git",
                ".venv",
                "node_modules",
                ".ruff_cache",
                ".pytest_cache",
                ".coverage",
                ".mypy_cache",
                "build",
                "dist",
                "tests",
                "test",
            }
            and not d.startswith(".")
        ]
        for f in files:
            if f.endswith(".py"):
                file_path = Path(root) / f
                try:
                    tree = ast.parse(file_path.read_text(encoding="utf-8"))
                    for node in ast.walk(tree):
                        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                            codebase_map.setdefault(node.name, []).append(
                                {
                                    "file_path": file_path,
                                    "type": "function",
                                    "node": node,
                                }
                            )
                        elif isinstance(node, ast.ClassDef):
                            codebase_map.setdefault(node.name, []).append(
                                {"file_path": file_path, "type": "class", "node": node}
                            )
                except Exception:
                    pass
    return codebase_map


def find_init_method(class_node):
    for item in class_node.body:
        if (
            isinstance(item, (ast.FunctionDef, ast.AsyncFunctionDef))
            and item.name == "__init__"
        ):
            return item
    return None


def get_args_list(func_node):
    args = []
    if hasattr(func_node.args, "posonlyargs") and func_node.args.posonlyargs:
        args.extend([a.arg for a in func_node.args.posonlyargs])
    if hasattr(func_node.args, "args") and func_node.args.args:
        args.extend([a.arg for a in func_node.args.args])
    if hasattr(func_node.args, "kwonlyargs") and func_node.args.kwonlyargs:
        args.extend([a.arg for a in func_node.args.kwonlyargs])
    if hasattr(func_node.args, "vararg") and func_node.args.vararg:
        args.append("*" + func_node.args.vararg.arg)
    if hasattr(func_node.args, "kwarg") and func_node.args.kwarg:
        args.append("**" + func_node.args.kwarg.arg)
    return args


def parse_type_node(node):
    if node is None:
        return {"type": "unknown"}
    if hasattr(node, "value") and isinstance(node, getattr(ast, "Index", type(None))):
        return parse_type_node(node.value)
    if isinstance(node, ast.Name):
        return {"type": "name", "name": node.id}
    if isinstance(node, ast.Constant):
        if node.value is None:
            return {"type": "none"}
        return {"type": "constant", "value": node.value}
    if isinstance(node, ast.Subscript):
        value_rep = parse_type_node(node.value)
        slice_rep = parse_type_node(node.slice)
        return {"type": "subscript", "value": value_rep, "slice": slice_rep}
    if isinstance(node, ast.BinOp) and isinstance(node.op, ast.BitOr):
        left_rep = parse_type_node(node.left)
        right_rep = parse_type_node(node.right)
        return {"type": "union", "elements": [left_rep, right_rep]}
    if isinstance(node, ast.Tuple):
        return {"type": "tuple", "elements": [parse_type_node(el) for el in node.elts]}
    if isinstance(node, ast.Attribute):
        return {
            "type": "attribute",
            "attr": node.attr,
            "value": parse_type_node(node.value),
        }
    return {"type": "unknown"}


def get_model_schema_statically(class_name, codebase_map):
    occurrences = codebase_map.get(class_name, [])
    for occ in occurrences:
        if occ["type"] == "class":
            node = occ["node"]
            fields = {}
            for item in node.body:
                if isinstance(item, ast.AnnAssign) and isinstance(
                    item.target, ast.Name
                ):
                    field_name = item.target.id
                    type_annotation = parse_type_node(item.annotation)
                    required = True
                    if item.value is not None:
                        if isinstance(item.value, ast.Call):
                            if (
                                isinstance(item.value.func, ast.Name)
                                and item.value.func.id == "Field"
                            ):
                                is_required = True
                                if item.value.args:
                                    first_arg = item.value.args[0]
                                    if (
                                        isinstance(first_arg, ast.Constant)
                                        and first_arg.value is not Ellipsis
                                    ) or (
                                        isinstance(first_arg, ast.Name)
                                        and first_arg.id != "..."
                                    ):
                                        is_required = False
                                for kw in item.value.keywords:
                                    if kw.arg == "default":
                                        if (
                                            isinstance(kw.value, ast.Constant)
                                            and kw.value.value is not Ellipsis
                                        ) or (
                                            isinstance(kw.value, ast.Name)
                                            and kw.value.id != "..."
                                        ):
                                            is_required = False
                                required = is_required
                        else:
                            required = False
                    fields[field_name] = {"type": type_annotation, "required": required}
            return fields
    return {}


def get_model_fields_ast_from_map(class_name, codebase_map):
    schema = get_model_schema_statically(class_name, codebase_map)
    return {k: v["required"] for k, v in schema.items()}


def validate_value_type(val, type_rep, codebase_map, visited=None):
    if visited is None:
        visited = set()
    t = type_rep.get("type")
    if t == "name":
        name = type_rep["name"]
        if name in ("str", "bytes", "AwareDatetime", "NaiveDatetime"):
            return isinstance(val, (str, bytes)), f"expected {name}"
        if name == "int":
            return isinstance(val, int) and not isinstance(val, bool), "expected int"
        if name == "float":
            return isinstance(val, (int, float)) and not isinstance(
                val, bool
            ), "expected float"
        if name == "bool":
            return isinstance(val, bool), "expected bool"
        if name in ("dict", "Dict"):
            return isinstance(val, dict), "expected dict"
        if name in ("list", "List"):
            return isinstance(val, list), "expected list"
        if name in ("set", "Set"):
            return isinstance(val, (list, set)), "expected list/set"
        if name in ("tuple", "Tuple"):
            return isinstance(val, (list, tuple)), "expected list/tuple"
        if name == "Any":
            return True, ""
        if name in ("None", "NoneType"):
            return val is None, "expected None"
        if name in codebase_map:
            if name in visited:
                return True, ""
            visited.add(name)
            ok, err_msg = validate_model_statically(val, name, codebase_map, visited)
            visited.remove(name)
            if not ok:
                if isinstance(err_msg, list):
                    return False, f"nested validation failed: {'; '.join(err_msg)}"
                return False, f"nested validation failed: {err_msg}"
            return True, ""
        return True, ""
    if t == "none":
        return val is None, "expected None"
    if t == "constant":
        return val == type_rep["value"], f"expected constant {type_rep['value']}"
    if t == "subscript":
        value_rep = type_rep["value"]
        slice_rep = type_rep["slice"]
        if value_rep.get("type") == "name" and value_rep.get("name") in (
            "Optional",
            "Union",
        ):
            elements = []
            if slice_rep.get("type") == "tuple":
                elements = slice_rep["elements"]
            else:
                elements = [slice_rep]
            if value_rep.get("name") == "Optional":
                elements.append({"type": "none"})
            errors = []
            for elem in elements:
                ok, err = validate_value_type(val, elem, codebase_map, visited)
                if ok:
                    return True, ""
                errors.append(err)
            return False, f"expected Union: {', '.join(errors)}"
        if value_rep.get("type") == "name" and value_rep.get("name") in (
            "list",
            "List",
            "set",
            "Set",
            "Sequence",
            "Iterable",
        ):
            if not isinstance(val, list):
                return False, f"expected list, got {type(val).__name__}"
            for i, item in enumerate(val):
                ok, err = validate_value_type(item, slice_rep, codebase_map, visited)
                if not ok:
                    return False, f"list item at index {i}: {err}"
            return True, ""
        if value_rep.get("type") == "name" and value_rep.get("name") in (
            "dict",
            "Dict",
        ):
            if not isinstance(val, dict):
                return False, f"expected dict, got {type(val).__name__}"
            val_type = slice_rep
            if slice_rep.get("type") == "tuple" and len(slice_rep["elements"]) == 2:
                val_type = slice_rep["elements"][1]
            for k, v in val.items():
                ok, err = validate_value_type(v, val_type, codebase_map, visited)
                if not ok:
                    return False, f"dict value at key '{k}': {err}"
            return True, ""
        return validate_value_type(val, value_rep, codebase_map, visited)
    if t == "union":
        errors = []
        for elem in type_rep["elements"]:
            ok, err = validate_value_type(val, elem, codebase_map, visited)
            if ok:
                return True, ""
            errors.append(err)
        return False, f"expected Union: {', '.join(errors)}"
    if t == "tuple":
        if not isinstance(val, list):
            return False, f"expected list/tuple, got {type(val).__name__}"
        elements = type_rep["elements"]
        if (
            len(elements) == 2
            and elements[1].get("type") == "constant"
            and elements[1].get("value") is Ellipsis
        ):
            for i, item in enumerate(val):
                ok, err = validate_value_type(item, elements[0], codebase_map, visited)
                if not ok:
                    return False, f"tuple item at index {i}: {err}"
            return True, ""
        if len(val) != len(elements):
            return False, f"expected tuple of length {len(elements)}, got {len(val)}"
        for idx, item in enumerate(val):
            ok, err = validate_value_type(item, elements[idx], codebase_map, visited)
            if not ok:
                return False, f"tuple item at index {idx}: {err}"
        return True, ""
    if t == "attribute":
        attr = type_rep["attr"]
        if attr == "Optional" and val is None:
            return True, ""
        return True, ""
    return True, ""


def validate_model_statically(doc_dict, class_name, codebase_map, visited=None):
    if visited is None:
        visited = set()
    if not isinstance(doc_dict, dict):
        return False, f"expected dict for {class_name}, got {type(doc_dict).__name__}"
    fields = get_model_schema_statically(class_name, codebase_map)
    if not fields:
        return True, ""
    errors = []
    for field_name, field_info in fields.items():
        if field_info["required"] and field_name not in doc_dict:
            errors.append(f"field '{field_name}' - Field required")
    for field_name, field_value in doc_dict.items():
        if field_name in fields:
            field_info = fields[field_name]
            ok, err_msg = validate_value_type(
                field_value, field_info["type"], codebase_map, visited
            )
            if not ok:
                errors.append(f"field '{field_name}' - {err_msg}")
    if errors:
        return False, errors
    return True, ""


def import_class_by_name_from_map(class_name, codebase_map):
    # Standard warning fallback is deprecated since we now perform robust static AST validation.
    # Return a mock representation to preserve compatibility if needed.
    return None, "Dynamic import is disabled for security."


def clean_json_text(text):
    text = text.replace("\t", "    ")
    text = re.sub(r"(?<!:)\/\/.*$", "", text, flags=re.MULTILINE)
    text = re.sub(r"\.\.\.", "null", text)
    return re.sub(r",\s*([\]}])", r"\1", text)


def validate_python_block(
    file_path,
    start_line,
    content,
    codebase_map,
    lines,
    repo_root,
    root_dirs,
    root_files,
):
    try:
        doc_tree = ast.parse(content)
    except SyntaxError as e:
        add_error(
            file_path,
            start_line + e.lineno - 1,
            f"Python SyntaxError in markdown code block: {e}",
        )
        return

    for node in doc_tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            name = node.name
            preceding_text = "".join(lines[max(0, start_line - 11) : start_line - 1])
            path_matches = re.findall(r"([\w\-\./]+\.py)", preceding_text)
            candidate_file = None
            for match in path_matches:
                resolved = resolve_path(
                    match, file_path, repo_root, root_dirs, root_files
                )
                if resolved and resolved.exists():
                    candidate_file = resolved
                    break

            if candidate_file:
                try:
                    codebase_tree = ast.parse(
                        candidate_file.read_text(encoding="utf-8")
                    )
                    found_in_referenced = False
                    for cb_node in ast.walk(codebase_tree):
                        if (
                            isinstance(cb_node, (ast.FunctionDef, ast.AsyncFunctionDef))
                            and cb_node.name == name
                        ):
                            found_in_referenced = True
                            doc_args = [
                                a
                                for a in get_args_list(node)
                                if a not in ("self", "cls")
                            ]
                            code_args = [
                                a
                                for a in get_args_list(cb_node)
                                if a not in ("self", "cls")
                            ]
                            if doc_args != code_args:
                                add_error(
                                    file_path,
                                    start_line + node.lineno - 1,
                                    f"Mismatched Python signature for function '{name}': documented arguments: {doc_args}, codebase arguments: {code_args} in '{candidate_file.relative_to(repo_root)}'.",
                                )
                            break
                    if not found_in_referenced:
                        add_error(
                            file_path,
                            start_line + node.lineno - 1,
                            f"Counterpart function '{name}' not found in referenced file '{candidate_file.relative_to(repo_root)}'.",
                        )
                except Exception as e:
                    add_error(
                        file_path,
                        start_line + node.lineno - 1,
                        f"Error parsing referenced file '{candidate_file}': {e}",
                    )
            else:
                if name in codebase_map:
                    for occ in codebase_map[name]:
                        if occ["type"] == "function":
                            cb_node = occ["node"]
                            cb_file = occ["file_path"]
                            doc_args = [
                                a
                                for a in get_args_list(node)
                                if a not in ("self", "cls")
                            ]
                            code_args = [
                                a
                                for a in get_args_list(cb_node)
                                if a not in ("self", "cls")
                            ]
                            if doc_args != code_args:
                                add_error(
                                    file_path,
                                    start_line + node.lineno - 1,
                                    f"Mismatched Python signature for function '{name}': documented arguments: {doc_args}, codebase arguments: {code_args} in '{cb_file.relative_to(repo_root)}'.",
                                )

        elif isinstance(node, ast.ClassDef):
            name = node.name
            preceding_text = "".join(lines[max(0, start_line - 11) : start_line - 1])
            path_matches = re.findall(r"([\w\-\./]+\.py)", preceding_text)
            candidate_file = None
            for match in path_matches:
                resolved = resolve_path(
                    match, file_path, repo_root, root_dirs, root_files
                )
                if resolved and resolved.exists():
                    candidate_file = resolved
                    break

            if candidate_file:
                try:
                    codebase_tree = ast.parse(
                        candidate_file.read_text(encoding="utf-8")
                    )
                    found_in_referenced = False
                    for cb_node in ast.walk(codebase_tree):
                        if isinstance(cb_node, ast.ClassDef) and cb_node.name == name:
                            found_in_referenced = True
                            doc_init = find_init_method(node)
                            code_init = find_init_method(cb_node)
                            if doc_init and code_init:
                                doc_args = [
                                    a
                                    for a in get_args_list(doc_init)
                                    if a not in ("self", "cls")
                                ]
                                code_args = [
                                    a
                                    for a in get_args_list(code_init)
                                    if a not in ("self", "cls")
                                ]
                                if doc_args != code_args:
                                    add_error(
                                        file_path,
                                        start_line + doc_init.lineno - 1,
                                        f"Mismatched Python signature for class '{name}' constructor: documented arguments: {doc_args}, codebase arguments: {code_args} in '{candidate_file.relative_to(repo_root)}'.",
                                    )
                            break
                    if not found_in_referenced:
                        add_error(
                            file_path,
                            start_line + node.lineno - 1,
                            f"Counterpart class '{name}' not found in referenced file '{candidate_file.relative_to(repo_root)}'.",
                        )
                except Exception as e:
                    add_error(
                        file_path,
                        start_line + node.lineno - 1,
                        f"Error parsing referenced file '{candidate_file}': {e}",
                    )
            else:
                if name in codebase_map:
                    for occ in codebase_map[name]:
                        if occ["type"] == "class":
                            cb_node = occ["node"]
                            cb_file = occ["file_path"]
                            doc_init = find_init_method(node)
                            code_init = find_init_method(cb_node)
                            if doc_init and code_init:
                                doc_args = [
                                    a
                                    for a in get_args_list(doc_init)
                                    if a not in ("self", "cls")
                                ]
                                code_args = [
                                    a
                                    for a in get_args_list(code_init)
                                    if a not in ("self", "cls")
                                ]
                                if doc_args != code_args:
                                    add_error(
                                        file_path,
                                        start_line + doc_init.lineno - 1,
                                        f"Mismatched Python signature for class '{name}' constructor: documented arguments: {doc_args}, codebase arguments: {code_args} in '{cb_file.relative_to(repo_root)}'.",
                                    )


def validate_json_block(
    file_path,
    start_line,
    content,
    codebase_map,
    lines,
    repo_root,
    root_dirs,
    root_files,
):
    cleaned = clean_json_text(content)
    try:
        doc_dict = json.loads(cleaned)
    except json.JSONDecodeError as e:
        add_error(
            file_path,
            start_line + e.lineno - 1,
            f"JSON SyntaxError in markdown code block: {e}",
        )
        return

    pydantic_class_names = []
    # Find classes that directly inherit from BaseModel or have base classes that do
    for name, occurrences in codebase_map.items():
        for occ in occurrences:
            if occ["type"] == "class":
                node = occ["node"]
                is_pydantic = False
                for base in node.bases:
                    if (
                        isinstance(base, ast.Name)
                        and base.id == "BaseModel"
                        or isinstance(base, ast.Attribute)
                        and base.attr == "BaseModel"
                    ):
                        is_pydantic = True
                if is_pydantic and name not in pydantic_class_names:
                    pydantic_class_names.append(name)

    # Transitive closure for subclasses of BaseModel
    changed = True
    while changed:
        changed = False
        for name, occurrences in codebase_map.items():
            if name in pydantic_class_names:
                continue
            for occ in occurrences:
                if occ["type"] == "class":
                    node = occ["node"]
                    is_pydantic = False
                    for base in node.bases:
                        base_name = None
                        if isinstance(base, ast.Name):
                            base_name = base.id
                        elif isinstance(base, ast.Attribute):
                            base_name = base.attr
                        if base_name in pydantic_class_names:
                            is_pydantic = True
                            break
                    if is_pydantic:
                        pydantic_class_names.append(name)
                        changed = True

    matched_model_name = None
    matched_explicitly = False

    if isinstance(doc_dict, dict) and len(doc_dict) == 1:
        key = list(doc_dict.keys())[0]
        val = list(doc_dict.values())[0]
        if isinstance(val, dict):
            camel_key = "".join(w.capitalize() for w in key.split("_"))
            for cname in pydantic_class_names:
                if cname.lower() == camel_key.lower():
                    matched_model_name = cname
                    matched_explicitly = True
                    doc_dict = val
                    break

    if not matched_model_name:
        preceding_lines = lines[max(0, start_line - 11) : start_line - 1]
        preceding_text_lower = " ".join(preceding_lines).lower()
        for cname in pydantic_class_names:
            if len(cname) <= 5:
                # Require title case or backtick matching for short class names
                joined_preceding = "".join(preceding_lines)
                if (
                    f"`{cname}`" in joined_preceding
                    or f"'{cname}'" in joined_preceding
                    or re.search(r"\b" + re.escape(cname) + r"\b", joined_preceding)
                ):
                    matched_model_name = cname
                    matched_explicitly = True
                    break
            else:
                if re.search(
                    r"\b" + re.escape(cname.lower()) + r"\b", preceding_text_lower
                ):
                    matched_model_name = cname
                    matched_explicitly = True
                    break

    if not matched_model_name and isinstance(doc_dict, dict):
        best_score = 0
        best_model = None
        json_keys = set(doc_dict.keys())
        if json_keys:
            for cname in pydantic_class_names:
                fields = get_model_schema_statically(cname, codebase_map)
                if fields:
                    model_keys = set(fields.keys())
                    overlap = json_keys.intersection(model_keys)
                    if len(overlap) >= 2:
                        score = len(overlap) / len(model_keys)
                        if score > best_score:
                            best_score = score
                            best_model = cname
            if best_score > 0.3:
                matched_model_name = best_model

    if matched_model_name and len(matched_model_name) <= 5:
        fields = get_model_schema_statically(matched_model_name, codebase_map)
        if fields and isinstance(doc_dict, dict) and doc_dict:
            overlap = set(doc_dict.keys()).intersection(set(fields.keys()))
            if not overlap:
                matched_model_name = None

    if matched_model_name:
        success, err_msgs = validate_model_statically(
            doc_dict, matched_model_name, codebase_map
        )

        if not success and isinstance(err_msgs, str):
            err_msgs = [err_msgs]

        if not success and err_msgs:
            if not matched_explicitly:
                return  # Discard implicit key overlap matches that failed to avoid false positives!

            for msg in err_msgs:
                add_error(
                    file_path,
                    start_line,
                    f"JSON example mismatch with Pydantic model '{matched_model_name}': {msg}",
                )


def check_preceding_skip(code_block_start_line, lines):
    # code_block_start_line is 1-based index of the opening ```.
    idx = code_block_start_line - 2
    for _ in range(3):
        if idx < 0:
            break
        preceding_line = lines[idx].strip().lower()
        if preceding_line:  # Skip empty lines
            if any(w in preceding_line for w in ("skip", "raw-text", "raw")):
                return True
            break
        idx -= 1
    return False


def process_markdown_file(
    file_path, repo_root, root_dirs, root_files, codebase_map=None, strict=False
):
    """Parses a markdown file to validate inline paths, links, and code blocks."""
    if codebase_map is None:
        codebase_map = {}
    try:
        with open(file_path, encoding="utf-8") as f:
            lines = f.readlines()
    except Exception as e:
        add_error(file_path, 1, f"Failed to read file: {e}")
        return

    # Standard markdown link regex pattern
    # [label](path)
    link_pattern = re.compile(r"\[[^\]]*\]\(([^)#\s]+)(?:#[^)]*)?\)")

    # Reference-style link pattern: e.g. [label]: path
    ref_link_pattern = re.compile(r"^\s{0,3}\[[^\]]+\]:\s*(\S+)")

    # Track any code block state
    in_code_block = False
    is_bash_block = False
    is_python_block = False
    is_json_block = False
    is_skip_block = False
    code_block_lines = []
    code_block_start_line = 1

    # Track HTML comment state across lines
    in_html_comment = False

    for line_idx, raw_line in enumerate(lines, 1):
        line = raw_line.strip()

        # If we are inside an active code block:
        if in_code_block:
            if line.startswith("```"):
                if is_bash_block:
                    # Process collected code block lines
                    current_cmd_parts = []
                    start_line_no = None

                    for c_idx, c_line in code_block_lines:
                        c_stripped = c_line.strip()
                        if not c_stripped or c_stripped.startswith("#"):
                            continue

                        if start_line_no is None:
                            start_line_no = c_idx

                        if c_stripped.endswith("\\"):
                            current_cmd_parts.append(c_line.rstrip("\\ \t\r\n"))
                        else:
                            current_cmd_parts.append(c_line)
                            cmd_str = " ".join(current_cmd_parts)
                            try:
                                args = shlex.split(cmd_str)
                                validate_cli_command(
                                    args,
                                    start_line_no,
                                    file_path,
                                    repo_root,
                                    root_dirs,
                                    root_files,
                                )
                            except Exception as e:
                                add_error(
                                    file_path,
                                    start_line_no,
                                    f"Failed to parse shell command (shlex error): {e}",
                                )
                            current_cmd_parts = []
                            start_line_no = None

                    if current_cmd_parts:
                        cmd_str = " ".join(current_cmd_parts)
                        try:
                            args = shlex.split(cmd_str)
                            validate_cli_command(
                                args,
                                start_line_no or code_block_start_line,
                                file_path,
                                repo_root,
                                root_dirs,
                                root_files,
                            )
                        except Exception as e:
                            add_error(
                                file_path,
                                start_line_no or code_block_start_line,
                                f"Failed to parse shell command (shlex error): {e}",
                            )

                elif is_python_block:
                    block_content = "".join(line for _, line in code_block_lines)
                    has_skip_comment = any(
                        any(
                            w in cl.strip().lower() for w in ("skip", "raw-text", "raw")
                        )
                        for _, cl in code_block_lines
                        if cl.strip().startswith("#")
                    )
                    has_preceding_skip = check_preceding_skip(
                        code_block_start_line, lines
                    )

                    if strict:
                        is_skip_block_active = False
                        has_skip_comment_active = False
                        has_preceding_skip_active = False
                    else:
                        is_skip_block_active = is_skip_block
                        has_skip_comment_active = has_skip_comment
                        has_preceding_skip_active = has_preceding_skip

                    if (
                        not is_skip_block_active
                        and not has_skip_comment_active
                        and not has_preceding_skip_active
                    ) and ("adr" not in Path(file_path).parts):
                        validate_python_block(
                            file_path,
                            code_block_start_line,
                            block_content,
                            codebase_map,
                            lines,
                            repo_root,
                            root_dirs,
                            root_files,
                        )

                elif is_json_block:
                    block_content = "".join(line for _, line in code_block_lines)
                    has_preceding_skip = check_preceding_skip(
                        code_block_start_line, lines
                    )

                    if strict:
                        is_skip_block_active = False
                        has_preceding_skip_active = False
                    else:
                        is_skip_block_active = is_skip_block
                        has_preceding_skip_active = has_preceding_skip

                    if not is_skip_block_active and not has_preceding_skip_active:
                        if "adr" not in Path(file_path).parts:
                            validate_json_block(
                                file_path,
                                code_block_start_line,
                                block_content,
                                codebase_map,
                                lines,
                                repo_root,
                                root_dirs,
                                root_files,
                            )

                in_code_block = False
                is_bash_block = False
                is_python_block = False
                is_json_block = False
                is_skip_block = False
                code_block_lines = []
            else:
                if is_bash_block or is_python_block or is_json_block:
                    code_block_lines.append((line_idx, raw_line))
            continue

        # --- OUTSIDE CODE BLOCKS ---

        # 1. Multi-Line & Single-Line HTML Comment State Machine
        line_to_process = raw_line
        if in_html_comment:
            if "-->" in line_to_process:
                in_html_comment = False
                line_to_process = line_to_process.split("-->", 1)[1]
            else:
                continue
        else:
            line_to_process = re.sub(r"<!--.*?-->", "", line_to_process)
            if "<!--" in line_to_process:
                in_html_comment = True
                line_to_process = line_to_process.split("<!--", 1)[0]

        # Check if code block starts on this uncommented line portion
        stripped_to_process = line_to_process.strip()
        if stripped_to_process.startswith("```"):
            in_code_block = True
            lang_line = stripped_to_process[3:].strip().lower()
            is_bash_block = lang_line in ("bash", "sh", "shell")
            is_python_block = lang_line.startswith("python") or lang_line == "py"
            is_json_block = lang_line.startswith("json")
            is_skip_block = any(w in lang_line for w in ("skip", "raw-text", "raw"))
            code_block_start_line = line_idx
            continue

        if not stripped_to_process:
            continue

        # 2. Outside Code Blocks: Extract Reference-Style Links
        # E.g. [my-doc]: ./docs/SDLC/guidelines.md
        ref_match = ref_link_pattern.match(line_to_process)
        if ref_match:
            path_str = ref_match.group(1)
            cleaned = clean_token(path_str)
            if cleaned:
                validate_path(
                    cleaned,
                    file_path,
                    line_idx,
                    repo_root,
                    root_dirs,
                    root_files,
                    ref_type="reference-link",
                )
            # Skip plain text token parsing on this line to avoid duplicates
            continue

        # 3. Outside Code Blocks: Extract Standard Markdown Links
        for match in link_pattern.finditer(line_to_process):
            path_str = match.group(1)
            # Standard links are checked with high priority
            cleaned = clean_token(path_str)
            if cleaned:
                validate_path(
                    cleaned,
                    file_path,
                    line_idx,
                    repo_root,
                    root_dirs,
                    root_files,
                    ref_type="link",
                )

        # 4. Outside Code Blocks: Extract Workspace/Path References in Inline Code or Plain Text
        # Split line by whitespace to scan for potential path words
        tokens = line_to_process.split()
        for token in tokens:
            cleaned = clean_token(token)
            if is_potential_path_ref(cleaned, root_dirs, root_files):
                validate_path(
                    cleaned,
                    file_path,
                    line_idx,
                    repo_root,
                    root_dirs,
                    root_files,
                    ref_type="reference",
                )


def main():
    # Compute repository root dynamically relative to the script's location to be environment-agnostic
    repo_root = Path(__file__).resolve().parent.parent

    # Dynamically build current root level directories and files
    try:
        root_entries = os.listdir(repo_root)
        root_dirs = {
            e
            for e in root_entries
            if (repo_root / e).is_dir() and (not e.startswith(".") or e == ".github")
        }
        root_files = {
            e
            for e in root_entries
            if (repo_root / e).is_file() and not e.startswith(".")
        }
    except Exception as e:
        print(f"Error scanning repository root: {e}")
        sys.exit(1)

    # Directories to completely exclude from markdown scanning
    exclude_dirs = {
        ".git",
        ".venv",
        "node_modules",
        ".ruff_cache",
        ".pytest_cache",
        ".coverage",
        ".mypy_cache",
        "build",
        "dist",
    }

    strict = False
    args_to_process = []
    for arg in sys.argv[1:]:
        if arg in ("--strict", "-s"):
            strict = True
        else:
            args_to_process.append(arg)

    # Scan and process target .md files
    md_files = []
    if args_to_process:
        for arg in args_to_process:
            p = Path(arg).resolve()
            if p.is_file() and p.suffix == ".md":
                md_files.append(p)
    else:
        for root, dirs, files in os.walk(repo_root):
            # Exclude directories in-place to optimize walk
            dirs[:] = [
                d for d in dirs if d not in exclude_dirs and not d.startswith(".")
            ]
            for f in files:
                if f.endswith(".md"):
                    file_path = Path(root) / f
                    md_files.append(file_path)

    print("Building codebase map for targeted validations...")
    codebase_map = build_codebase_map(repo_root)

    if args_to_process:
        print(f"Scanning {len(md_files)} specified markdown file(s)...")
    else:
        print(f"Scanning {len(md_files)} markdown files across the repository...")
    for md_file in sorted(md_files):
        process_markdown_file(
            md_file, repo_root, root_dirs, root_files, codebase_map, strict=strict
        )

    if errors:
        print(f"\n[!] Markdown Validation Failed with {len(errors)} error(s):")
        for err in sorted(errors, key=lambda x: (x["file"], x["line"])):
            print(f"  {err['file']}:{err['line']}: {err['message']}")
        sys.exit(1)
    else:
        print(
            "\n[+] All repository markdown files, links, and CLI commands verified successfully!"
        )
        sys.exit(0)


if __name__ == "__main__":
    main()
