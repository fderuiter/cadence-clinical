#!/usr/bin/env python3
"""
Lightweight Code Duplication Detector for Cadence Clinical Platform.
Scans Python, JavaScript, and Vue files in apps/ and packages/ directories
to detect copied blocks of logic before commits or merges.
"""

import hashlib
import json
import os
import re
import sys
from typing import Dict, List, Tuple


def normalize_line(line: str) -> str:
    """Normalizes a single line of code to make the duplication check robust.

    Strips whitespaces, removes comments, and ignores empty or boilerplate lines.

    Args:
        line: The raw line string from a file.

    Returns:
        The normalized line string.
    """
    cleaned = line.strip()

    # 1. Handle CSS / JS block comments on single line or partial line
    cleaned = re.sub(r"/\*.*?\*/", "", cleaned).strip()

    # If the line is part of a block comment or starts with comment markers
    if cleaned.startswith("/*") or cleaned.startswith("*/") or cleaned.startswith("* "):
        return ""

    # Remove single line comments
    if cleaned.startswith("#") or cleaned.startswith("//"):
        return ""
    # Strip inline comments
    if "#" in cleaned:
        cleaned = cleaned.split("#", 1)[0].strip()
    if "//" in cleaned:
        cleaned = cleaned.split("//", 1)[0].strip()

    # 2. Normalize standard URLs to avoid false positives from different URLs
    cleaned = re.sub(r"https?://\S+", "http://url-placeholder", cleaned)

    # 3. Normalize string formats (single quotes, backticks to double quotes)
    cleaned = cleaned.replace("'", '"').replace("`", '"')

    # Ignore imports/exports/braces
    if (
        cleaned.startswith("import ")
        or cleaned.startswith("from ")
        or cleaned.startswith("export ")
        or cleaned.startswith("const {")
        or cleaned == "}"
        or cleaned == "{"
        or cleaned == "};"
        or cleaned == "],"
        or cleaned == "["
        or cleaned == "]"
        or not cleaned
    ):
        return ""

    return cleaned


def scan_file_for_lines(
    file_path: str,
) -> List[Tuple[str, int, str]]:
    """Reads a file and returns a list of its normalized non-empty lines with metadata.

    Args:
        file_path: Absolute or relative path to the file.

    Returns:
        A list of tuples: (normalized_line, line_number, original_line)
    """
    valid_lines = []
    try:
        with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
            for idx, line in enumerate(f, start=1):
                norm = normalize_line(line)
                if norm:
                    valid_lines.append((norm, idx, line.rstrip()))
    except Exception:
        pass
    return valid_lines


def main() -> None:
    """Main execution entry point."""
    print("--- Running Cadence Code Duplication Scanner ---")

    # Command line arguments can specify target files (e.g. from pre-commit or git diff)
    args = sys.argv[1:]
    target_files_mode = False
    target_files = []

    if args:
        target_files_mode = True
        for arg in args:
            abs_path = os.path.abspath(arg)
            if (
                abs_path.endswith((".py", ".js", ".vue", ".css"))
                and os.path.exists(abs_path)
                and not any(
                    p in abs_path
                    for p in [
                        "node_modules",
                        "tests",
                        ".venv",
                        "__pycache__",
                        "dist",
                        "build",
                    ]
                )
            ):
                target_files.append(abs_path)
        print(
            f"Running in changed-files mode. Target files to verify: {len(target_files)}"
        )

    # Window size threshold (number of consecutive identical lines)
    window_size = 15

    # 1. Collect all source files in the entire codebase as reference
    scan_dirs = ["/app/apps", "/app/packages"]
    all_files = []
    for directory in scan_dirs:
        if not os.path.exists(directory):
            continue
        for root, _, files in os.walk(directory):
            if any(
                p in root
                for p in [
                    ".venv",
                    "node_modules",
                    "tests",
                    "__pycache__",
                    ".git",
                    "dist",
                    "build",
                ]
            ):
                continue
            for file in files:
                if file.endswith((".py", ".js", ".vue", ".css")):
                    all_files.append(os.path.join(root, file))

    # 2. Extract blocks from all files to index them
    # seen_blocks mapping: block_hash -> list of locations (file_path, start_line, end_line, preview_text)
    seen_blocks: Dict[str, List[Tuple[str, int, int, str]]] = {}

    for file_path in all_files:
        lines_meta = scan_file_for_lines(file_path)
        if len(lines_meta) < window_size:
            continue

        for i in range(len(lines_meta) - window_size + 1):
            window = lines_meta[i : i + window_size]
            normalized_block_text = "\n".join(item[0] for item in window)
            block_hash = hashlib.sha256(
                normalized_block_text.encode("utf-8")
            ).hexdigest()

            start_line = window[0][1]
            end_line = window[-1][1]
            preview = "\n".join(item[2] for item in window[:3]) + "\n..."

            if block_hash not in seen_blocks:
                seen_blocks[block_hash] = []
            seen_blocks[block_hash].append((file_path, start_line, end_line, preview))

    # 3. Find duplications
    duplicates_found = []
    unique_dups_reported = set()

    for block_hash, locations in seen_blocks.items():
        if len(locations) > 1:
            # Check if any duplication is within our target files (if in target_files_mode)
            if target_files_mode:
                has_target_file = any(loc[0] in target_files for loc in locations)
                if not has_target_file:
                    continue

            # Group locations to avoid overlapping blocks in the same file
            filtered_locations = []
            for loc in locations:
                overlap = False
                for existing in filtered_locations:
                    if (
                        existing[0] == loc[0]
                        and abs(existing[1] - loc[1]) < window_size
                    ):
                        overlap = True
                        break
                if not overlap:
                    filtered_locations.append(loc)

            if len(filtered_locations) > 1:
                # We have a duplication!
                # Report pairs of duplications
                for i in range(len(filtered_locations)):
                    for j in range(i + 1, len(filtered_locations)):
                        loc1 = filtered_locations[i]
                        loc2 = filtered_locations[j]

                        p_file1 = os.path.relpath(loc1[0], "/app")
                        p_file2 = os.path.relpath(loc2[0], "/app")

                        if p_file1 == p_file2:
                            continue

                        pair_set = {p_file1, p_file2}
                        if any(
                            pair_set.issubset(ignored)
                            for ignored in [
                                {
                                    "apps/etmf/sealer.py",
                                    "apps/execution/database/sealer.py",
                                },
                                {
                                    "apps/gateway/main.py",
                                    "packages/security/middleware.py",
                                },
                                {
                                    "apps/interop/main.py",
                                    "apps/notifications/main.py",
                                    "apps/econsent/main.py",
                                    "apps/eisf/main.py",
                                    "apps/quality/main.py",
                                    "apps/safety/main.py",
                                    "apps/ctms/main.py",
                                    "apps/etmf/main.py",
                                    "apps/org/main.py",
                                    "apps/tickets/main.py",
                                },
                                {
                                    "apps/web/src/api/terminologyClient.js",
                                    "apps/web/src/api/soaClient.js",
                                },
                                {
                                    "packages/core-models/audit.py",
                                    "packages/core-models/sdtm/models.py",
                                },
                                {
                                    "apps/execution/biostat/adsl.py",
                                    "apps/execution/biostat/extractors.py",
                                },
                                {
                                    "apps/web/index.js",
                                    "apps/web/src/stores/clinical.js",
                                    "apps/web/src/views/MdrView.vue",
                                },
                            ]
                        ):
                            continue

                        dup_key = tuple(
                            sorted([f"{p_file1}:{loc1[1]}", f"{p_file2}:{loc2[1]}"])
                        )
                        if dup_key not in unique_dups_reported:
                            unique_dups_reported.add(dup_key)
                            duplicates_found.append((loc1, loc2))

    # Write summary
    summary_data = {"duplicates": []}
    for loc1, loc2 in duplicates_found:
        p_file1 = os.path.relpath(loc1[0], "/app")
        p_file2 = os.path.relpath(loc2[0], "/app")
        summary_data["duplicates"].append(
            {
                "loc1": {"file": p_file1, "start": loc1[1], "end": loc1[2]},
                "loc2": {"file": p_file2, "start": loc2[1], "end": loc2[2]},
                "preview": loc1[3],
            }
        )
    try:
        with open("duplication_summary.json", "w", encoding="utf-8") as f:
            json.dump(summary_data, f, indent=2)
    except Exception as e:
        print(f"Error writing duplication summary: {e}")

    if duplicates_found:
        print("\n\033[91m[ERROR] Code Duplication Detected Above Threshold!\033[0m")
        print(
            f"Detected {len(duplicates_found)} duplicate blocks of {window_size}+ lines:\n"
        )

        for loc1, loc2 in duplicates_found:
            p_file1 = os.path.relpath(loc1[0], "/app")
            p_file2 = os.path.relpath(loc2[0], "/app")
            print(f"  - Block 1: \033[93m{p_file1}\033[0m (Lines {loc1[1]}-{loc1[2]})")
            print(f"    Block 2: \033[93m{p_file2}\033[0m (Lines {loc2[1]}-{loc2[2]})")
            print("    Code Preview:")
            for line in loc1[3].split("\n"):
                print(f"      | {line}")
            print()

        sys.exit(1)

    print(
        "\n\033[92m[SUCCESS] No duplicate code structures found above the threshold.\033[0m"
    )
    sys.exit(0)


if __name__ == "__main__":
    main()
