import argparse
import fnmatch
import os
import shutil
import subprocess
import sys
from typing import List, Tuple

from packages.deid.detector import DeidDetector
from packages.deid.models import ComplianceProfile

TEXT_EXTENSIONS = {
    ".py",
    ".js",
    ".ts",
    ".tsx",
    ".jsx",
    ".json",
    ".md",
    ".log",
    ".txt",
    ".yaml",
    ".yml",
    ".csv",
    ".toml",
    ".xml",
    ".html",
    ".css",
}


def load_gitignore_patterns(root_dir: str) -> List[Tuple[bool, str]]:
    """Loads and parses gitignore patterns from the repository's .gitignore file.

    Args:
        root_dir (str): The root directory where .gitignore is located.

    Returns:
        List[Tuple[bool, str]]: A list of tuples containing (is_negated, pattern).
    """
    patterns = []
    gitignore_path = os.path.join(root_dir, ".gitignore")
    if not os.path.exists(gitignore_path):
        return patterns
    try:
        with open(gitignore_path, "r", encoding="utf-8", errors="ignore") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                is_negated = line.startswith("!")
                if is_negated:
                    pat = line[1:]
                else:
                    pat = line
                patterns.append((is_negated, pat))
    except Exception:
        pass
    return patterns


def is_locally_ignored(
    path: str, patterns: List[Tuple[bool, str]], root_dir: str
) -> bool:
    """Checks if a given path matches gitignore patterns using a local fallback engine.

    Args:
        path (str): The absolute or relative path to check.
        patterns (List[Tuple[bool, str]]): List of parsed gitignore patterns.
        root_dir (str): The repository root directory.

    Returns:
        bool: True if the path is ignored, False otherwise.
    """
    try:
        rel_path = os.path.relpath(path, root_dir)
    except ValueError:
        rel_path = path
    rel_path_str = rel_path.replace(os.sep, "/")
    if os.path.isdir(path) and not rel_path_str.endswith("/"):
        rel_path_str += "/"

    ignored = False
    for is_negated, pattern in patterns:
        match = False
        pat_str = pattern.replace(os.sep, "/")
        if pat_str.startswith("/"):
            pat_str = pat_str[1:]

        if pat_str.endswith("/"):
            if rel_path_str.startswith(pat_str) or f"/{pat_str}" in f"/{rel_path_str}":
                match = True
        else:
            if fnmatch.fnmatch(rel_path_str, pat_str) or fnmatch.fnmatch(
                os.path.basename(rel_path_str), pat_str
            ):
                match = True
            elif f"/{pat_str}/" in f"/{rel_path_str}/":
                match = True

        if match:
            ignored = not is_negated
    return ignored


def filter_git_ignored_files(files: List[str], cwd: str) -> List[str]:
    """Filters a list of file paths by querying 'git check-ignore' if git is available.

    Args:
        files (List[str]): List of absolute file paths to filter.
        cwd (str): The working directory for running the git command.

    Returns:
        List[str]: List of file paths that are not ignored by git.
    """
    if not files:
        return []
    if not shutil.which("git"):
        return files
    try:
        proc = subprocess.Popen(
            ["git", "check-ignore", "--stdin", "-z"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            cwd=cwd,
            text=True,
        )
        input_data = "\0".join(files) + "\0"
        stdout, _ = proc.communicate(input=input_data)
        ignored_files = set(stdout.split("\0"))
        return [f for f in files if f not in ignored_files]
    except Exception:
        return files


def should_scan_file(file_path: str) -> bool:
    """Determines whether a file should be scanned based on its extension or content.

    Args:
        file_path (str): The path of the file to examine.

    Returns:
        bool: True if the file should be scanned, False otherwise.
    """
    name = os.path.basename(file_path)
    if name in {
        "Dockerfile",
        "LICENSE",
        "Makefile",
        "Pipfile",
        "pyproject.toml",
        "package.json",
        "pnpm-workspace.yaml",
        "pnpm-lock.yaml",
        "uv.lock",
    }:
        return True
    ext = os.path.splitext(file_path)[1].lower()
    if not ext:
        try:
            with open(file_path, "rb") as f:
                chunk = f.read(1024)
                return b"\x00" not in chunk
        except Exception:
            return False
    return ext in TEXT_EXTENSIONS


def get_line_and_col(content: str, offset: int) -> Tuple[int, int]:
    """Computes the 1-based line number and column index for a given character offset.

    Args:
        content (str): The full content of the file.
        offset (int): The 0-based character offset of the match.

    Returns:
        Tuple[int, int]: A tuple containing (line_number, column_index).
    """
    line_idx = content.count("\n", 0, offset) + 1
    line_start = content.rfind("\n", 0, offset) + 1
    col_idx = offset - line_start + 1
    return line_idx, col_idx


def scan_file(
    file_path: str, detector: DeidDetector, profile: ComplianceProfile
) -> List[dict]:
    """Scans a single file for compliance violations using the specified detector.

    Args:
        file_path (str): The path to the file to scan.
        detector (DeidDetector): The detector instance to use.
        profile (ComplianceProfile): The compliance profile active for the scan.

    Returns:
        List[dict]: A list of violation dictionaries detailing findings.
    """
    try:
        with open(file_path, "r", encoding="utf-8", errors="replace") as f:
            content = f.read()
    except Exception as e:
        print(f"Error reading file {file_path}: {e}", file=sys.stderr)
        return []

    results = detector.detect(content, profile=profile)
    violations = []
    lines = content.split("\n")
    for res in results:
        line, col = get_line_and_col(content, res.start)
        # Check if the specific line contains "deid-ignore" or "pragma: allowlist" or "deid: ignore"
        line_content = lines[line - 1] if line <= len(lines) else ""
        if (
            "deid-ignore" in line_content
            or "pragma: allowlist" in line_content
            or "deid: ignore" in line_content
        ):
            continue

        # Filter out common development, test, and system mock values
        val_lower = res.value.lower()
        cat_lower = (
            res.category.value.lower()
            if hasattr(res.category, "value")
            else str(res.category).lower()
        )

        if cat_lower == "urls" and (
            "localhost" in val_lower
            or "127.0.0.1" in val_lower
            or "0.0.0.0" in val_lower  # nosec B104
            or "transmit-mock" in val_lower
            or "cadence-clinical.com" in val_lower
        ):
            continue
        if cat_lower == "ip_mac_addresses" and (
            val_lower in {"127.0.0.1", "0.0.0.0", "::1"}  # nosec B104
        ):
            continue
        if cat_lower == "zip_geographic" and (
            val_lower == "12345"
            or val_lower == "65537"
            or "secret" in line_content.lower()
            or "salt" in line_content.lower()
            or "key" in line_content.lower()
            or "public_exponent" in line_content.lower()
            or "exponent" in line_content.lower()
        ):
            continue
        if cat_lower == "telephone_fax" and (
            "concept_code" in line_content
            or "concept" in line_content
            or "usr_" in line_content
        ):
            continue

        violations.append(
            {
                "file": file_path,
                "line": line,
                "col": col,
                "category": res.category,
                "value": res.value,
            }
        )
    return violations


def is_excluded_path(path: str, root_dir: str) -> bool:
    """Checks if a file path belongs to directories or names explicitly excluded from scanning.

    Args:
        path (str): The file path to evaluate.
        root_dir (str): The repository root directory.

    Returns:
        bool: True if the path is excluded, False otherwise.
    """
    try:
        rel_path = os.path.relpath(path, root_dir).replace(os.sep, "/")
    except ValueError:
        rel_path = path.replace(os.sep, "/")

    parts = rel_path.lower().split("/")

    # Exclude typical test/dependency/system/documentation directories
    for part in parts:
        if part in {
            "tests",
            "test",
            "node_modules",
            ".git",
            ".venv",
            "env",
            "build",
            "dist",
            ".github",
            ".ruff_cache",
            ".pytest_cache",
            "docs",
            "scripts",
            "docker",
            "verification",
            "stores",
        }:
            return True

    # Exclude files starting with test_ or ending with .test.js, etc. or specific workspace documents
    name = os.path.basename(path).lower()
    if (
        name.startswith("test_")
        or name.endswith(".test.js")
        or name.endswith(".test.ts")
        or name.endswith(".test.jsx")
        or name.endswith(".test.tsx")
        or name
        in {
            "package.json",
            "pyproject.toml",
            "pnpm-workspace.yaml",
            "pnpm-lock.yaml",
            "uv.lock",
            "readme.md",
            "agents.md",
            "license",
            "architecture.md",
            "eslint.config.mjs",
        }
    ):
        return True

    return False


def get_files_to_scan(paths: List[str], root_dir: str) -> List[str]:
    """Resolves and filters all candidate files to scan from input paths and root.

    Args:
        paths (List[str]): A list of file or directory paths to discover.
        root_dir (str): The repository root directory.

    Returns:
        List[str]: A list of file paths that are eligible for scanning.
    """
    gitignore_patterns = load_gitignore_patterns(root_dir)
    raw_files = []

    scan_paths = paths if paths else [root_dir]

    for path in scan_paths:
        path_abs = os.path.abspath(path)
        if not os.path.exists(path_abs):
            continue

        if os.path.isfile(path_abs):
            if not is_excluded_path(path_abs, root_dir):
                raw_files.append(path_abs)
        elif os.path.isdir(path_abs):
            for root, dirs, files in os.walk(path_abs):
                # Prune excluded or ignored directories
                dirs[:] = [
                    d
                    for d in dirs
                    if not is_locally_ignored(
                        os.path.join(root, d), gitignore_patterns, root_dir
                    )
                    and not is_excluded_path(os.path.join(root, d), root_dir)
                ]
                for f in files:
                    file_path = os.path.join(root, f)
                    if not is_locally_ignored(file_path, gitignore_patterns, root_dir):
                        if not is_excluded_path(file_path, root_dir):
                            if should_scan_file(file_path):
                                raw_files.append(file_path)

    return filter_git_ignored_files(raw_files, root_dir)


def main():
    """Main entry point for the Unified CLI Compliance & DEID Scan Tool.

    Parses command-line arguments, walks target directories, filters paths based
    on exclusions and gitignore patterns, runs the de-identification scanner on
    discovered candidate files, and returns appropriate exit codes based on
    detected violations.
    """
    parser = argparse.ArgumentParser(
        description="Unified CLI Compliance & DEID Scan Tool"
    )
    parser.add_argument(
        "paths",
        nargs="*",
        default=[],
        help="Files or directories to scan. Defaults to the current directory.",
    )
    parser.add_argument(
        "--profile",
        choices=["HIPAA", "GDPR", "EU_CTR"],
        default="HIPAA",
        help="Compliance profile to scan against. Defaults to HIPAA.",
    )
    args = parser.parse_args()

    root_dir = os.getcwd()

    try:
        profile = ComplianceProfile(args.profile)
    except ValueError:
        print(f"Error: Invalid profile '{args.profile}'", file=sys.stderr)
        sys.exit(2)

    print(f"Starting compliance scan using profile: {profile.value}")

    files_to_scan = get_files_to_scan(args.paths, root_dir)
    print(f"Found {len(files_to_scan)} files to scan.")

    detector = DeidDetector()
    total_violations = 0

    for file_path in files_to_scan:
        violations = scan_file(file_path, detector, profile)
        if violations:
            for v in violations:
                rel_path = os.path.relpath(v["file"], root_dir)
                print(
                    f"{rel_path}:{v['line']}:{v['col']}: [VIOLATION] Category: '{v['category']}'"
                )
                total_violations += 1

    if total_violations > 0:
        print(
            f"\nScan completed with {total_violations} compliance violations found.",
            file=sys.stderr,
        )
        sys.exit(1)
    else:
        print("\nScan completed successfully. No compliance violations found.")
        sys.exit(0)


if __name__ == "__main__":
    main()
