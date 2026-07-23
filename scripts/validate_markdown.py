#!/usr/bin/env python3
"""
Repository-Wide Custom Markdown Linter
Statically validates workspace paths/links and dry-runs CLI subcommands.
"""

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

    # Ignore environment variables and placeholder syntax
    if any(char in path_str for char in ("$", "*", "<", ">", "{", "}", "[", "]")):
        return None
    if (
        "placeholder" in path_str.lower()
        or "your-" in path_str.lower()
        or "example" in path_str.lower()
    ):
        return None

    # Standardize path separators
    path_str = path_str.replace("\\", "/")

    # Strip leading slash for workspace relative resolve
    stripped_path = path_str.lstrip("/")

    # Absolute repo-level path starting with /app/
    if path_str.startswith("/app/"):
        rel_path = path_str[5:]
        return repo_root / rel_path

    # If it starts with a known root dir or root file, resolve relative to root
    first_part = stripped_path.split("/")[0]
    if first_part in root_dirs or first_part in root_files:
        return repo_root / stripped_path

    # If starts with leading slash and is not system path, treat as workspace-relative only if first component is in root_dirs or root_files
    if path_str.startswith("/"):
        if first_part in root_dirs or first_part in root_files:
            return repo_root / stripped_path
        return None

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


def process_markdown_file(file_path, repo_root, root_dirs, root_files):
    """Parses a markdown file to validate inline paths, links, and code blocks."""
    try:
        with open(file_path, "r", encoding="utf-8") as f:
            lines = f.readlines()
    except Exception as e:
        add_error(file_path, 1, f"Failed to read file: {e}")
        return

    # Standard markdown link regex pattern
    # [label](path)
    link_pattern = re.compile(r"\[[^\]]*\]\(([^)#\s]+)(?:#[^)]*)?\)")

    # Track any code block state
    in_code_block = False
    is_bash_block = False
    code_block_lines = []
    code_block_start_line = 1

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

                in_code_block = False
                is_bash_block = False
                code_block_lines = []
            else:
                in_code_block = True
                lang = line[3:].strip().lower()
                is_bash_block = lang in ("bash", "sh", "shell")
                code_block_start_line = line_idx
            continue

        if in_code_block:
            if is_bash_block:
                code_block_lines.append((line_idx, raw_line))
            continue

        # Outside Code Blocks: Extract Standard Markdown Links
        for match in link_pattern.finditer(raw_line):
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

        # Outside Code Blocks: Extract Workspace/Path References in Inline Code or Plain Text
        # Split line by whitespace to scan for potential path words
        tokens = raw_line.split()
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
    repo_root = Path("/app").resolve()

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
                md_files.append(Path(root) / f)

    print(f"Scanning {len(md_files)} markdown files across the repository...")
    for md_file in sorted(md_files):
        process_markdown_file(md_file, repo_root, root_dirs, root_files)

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
