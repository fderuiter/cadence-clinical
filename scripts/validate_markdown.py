#!/usr/bin/env python3
# CI Trigger
"""
Repository-Wide Custom Markdown Linter
Statically validates workspace paths/links and dry-runs CLI subcommands.
"""

import ast
import importlib.util
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
from pathlib import Path

# Add packages subfolders and apps to sys.path to resolve imports within modules
for p in Path("/app/packages").glob("*"):
    if p.is_dir() and str(p) not in sys.path:
        sys.path.insert(0, str(p))
for p in Path("/app/apps").glob("*"):
    if p.is_dir() and str(p) not in sys.path:
        sys.path.insert(0, str(p))
if "/app" not in sys.path:
    sys.path.insert(0, "/app")

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
    """Strips surrounding quotes, parentheses, brackets, braces, backticks and trailing punctuation from a token."""
    token = token.strip()
    while token and token[-1] in "`'\"()[]{}<>,;:!?.)":
        token = token[:-1]
    while token and token[0] in "`'\"()[]{}<>,;:!?(":
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

    # Ignore web/external URLs
    if (
        path_str.startswith(("http://", "https://", "mailto:", "tel:"))
        or "://" in path_str
        or path_str.startswith("#")
    ):
        return None

    # Strip query parameters or anchors (e.g., # or ?)
    path_str = path_str.split("#")[0].split("?")[0].strip()
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
    ):
        return None

    # Standardize path separators
    path_str = path_str.replace("\\", "/")

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
                "/tmp/",
            )
        ):
            if path_str.startswith("/app/"):
                stripped_path = path_str[5:]
            return repo_root / stripped_path
        return None

    # If it starts with a known root dir or root file, resolve relative to root
    first_part = stripped_path.split("/")[0]
    if first_part in root_dirs or first_part in root_files:
        return repo_root / stripped_path

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
        if compose_args[i] in ("-f", "--file"):
            if i + 1 < limit:
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
        if arg.startswith("-"):
            if not FLAG_PATTERN.match(arg):
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


def get_model_fields_ast_from_map(class_name, codebase_map):
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
                                    ):
                                        is_required = False
                                    elif (
                                        isinstance(first_arg, ast.Name)
                                        and first_arg.id != "..."
                                    ):
                                        is_required = False
                                for kw in item.value.keywords:
                                    if kw.arg == "default":
                                        if (
                                            isinstance(kw.value, ast.Constant)
                                            and kw.value.value is not Ellipsis
                                        ):
                                            is_required = False
                                        elif (
                                            isinstance(kw.value, ast.Name)
                                            and kw.value.id != "..."
                                        ):
                                            is_required = False
                                required = is_required
                        else:
                            required = False
                    fields[field_name] = required
            return fields
    return {}


def import_class_by_name_from_map(class_name, codebase_map):
    occurrences = codebase_map.get(class_name, [])
    for occ in occurrences:
        if occ["type"] == "class":
            p = occ["file_path"]
            try:
                module_name = p.stem
                temp_mod_name = f"gxp_import_temp_{module_name}"
                spec = importlib.util.spec_from_file_location(temp_mod_name, str(p))
                module = importlib.util.module_from_spec(spec)
                sys.modules[temp_mod_name] = module
                spec.loader.exec_module(module)
                cls = getattr(module, class_name, None)
                if cls is not None:
                    return cls
            except Exception:
                pass
    return None


def clean_json_text(text):
    text = text.replace("\t", "    ")
    text = re.sub(r"(?<!:)\/\/.*$", "", text, flags=re.MULTILINE)
    text = re.sub(r"\.\.\.", "null", text)
    text = re.sub(r",\s*([\]}])", r"\1", text)
    return text


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
    for name, occurrences in codebase_map.items():
        for occ in occurrences:
            if occ["type"] == "class":
                node = occ["node"]
                is_pydantic = False
                for base in node.bases:
                    if isinstance(base, ast.Name) and base.id == "BaseModel":
                        is_pydantic = True
                    elif isinstance(base, ast.Attribute) and base.attr == "BaseModel":
                        is_pydantic = True
                if is_pydantic and name not in pydantic_class_names:
                    pydantic_class_names.append(name)

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
                if (
                    f"`{cname}`" in "".join(preceding_lines)
                    or f"'{cname}'" in "".join(preceding_lines)
                    or cname in "".join(preceding_lines)
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
                fields = get_model_fields_ast_from_map(cname, codebase_map)
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

    if matched_model_name:
        success = False
        err_msgs = []
        try:
            cls = import_class_by_name_from_map(matched_model_name, codebase_map)
            if cls is not None:
                try:
                    cls.model_validate(doc_dict)
                    success = True
                except Exception as val_err:
                    if hasattr(val_err, "errors"):
                        for err in val_err.errors():
                            loc = ".".join(str(item) for item in err.get("loc", []))
                            err_msgs.append(f"field '{loc}' - {err.get('msg')}")
                    else:
                        err_msgs.append(str(val_err))
                    success = False
                else:
                    success = True
        except Exception:
            pass

        if not success and not err_msgs:
            fields = get_model_fields_ast_from_map(matched_model_name, codebase_map)
            missing = [f for f, req in fields.items() if req and f not in doc_dict]
            if missing:
                err_msgs.append(f"Missing required fields: {missing}")

        if not success and err_msgs:
            if not matched_explicitly:
                return  # Discard implicit key overlap matches that failed to avoid false positives!

            for msg in err_msgs:
                add_error(
                    file_path,
                    start_line,
                    f"JSON example mismatch with Pydantic model '{matched_model_name}': {msg}",
                )


def process_markdown_file(
    file_path, repo_root, root_dirs, root_files, codebase_map=None
):
    """Parses a markdown file to validate inline paths, links, and code blocks."""
    if codebase_map is None:
        codebase_map = {}
    try:
        with open(file_path, "r", encoding="utf-8") as f:
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

        # Code Block Boundaries Detection
        if line.startswith("```"):
            if in_code_block:
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
                    has_preceding_skip = False
                    if code_block_start_line >= 2:
                        preceding_line = (
                            lines[code_block_start_line - 2].strip().lower()
                        )
                        has_preceding_skip = any(
                            w in preceding_line for w in ("skip", "raw-text", "raw")
                        )

                    if (
                        not is_skip_block
                        and not has_skip_comment
                        and not has_preceding_skip
                    ):
                        if (
                            "docs" in Path(file_path).parts
                            and "adr" not in Path(file_path).parts
                        ):
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
                    has_preceding_skip = False
                    if code_block_start_line >= 2:
                        preceding_line = (
                            lines[code_block_start_line - 2].strip().lower()
                        )
                        has_preceding_skip = any(
                            w in preceding_line for w in ("skip", "raw-text", "raw")
                        )

                    if not is_skip_block and not has_preceding_skip:
                        if (
                            "docs" in Path(file_path).parts
                            and "adr" not in Path(file_path).parts
                        ):
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
                in_code_block = True
                lang_line = line[3:].strip().lower()
                is_bash_block = lang_line in ("bash", "sh", "shell")
                is_python_block = lang_line.startswith("python") or lang_line == "py"
                is_json_block = lang_line.startswith("json")
                is_skip_block = any(w in lang_line for w in ("skip", "raw-text", "raw"))
                code_block_start_line = line_idx
            continue

        if in_code_block:
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

        if not line_to_process.strip():
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

    # Scan and process all .md files
    md_files = []
    for root, dirs, files in os.walk(repo_root):
        # Exclude directories in-place to optimize walk
        dirs[:] = [d for d in dirs if d not in exclude_dirs and not d.startswith(".")]
        for f in files:
            if f.endswith(".md"):
                file_path = Path(root) / f
                # Skip SDLC documentation suite from standard CLI validation as they contain intentional
                # compliance drifts used for testing the linter in gxp_compliance_suite.py
                if (
                    "docs/SDLC" in file_path.as_posix()
                    or "docs\\SDLC" in file_path.as_posix()
                ):
                    continue
                md_files.append(file_path)

    print("Building codebase map for targeted validations...")
    codebase_map = build_codebase_map(repo_root)

    print(f"Scanning {len(md_files)} markdown files across the repository...")
    for md_file in sorted(md_files):
        process_markdown_file(md_file, repo_root, root_dirs, root_files, codebase_map)

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
