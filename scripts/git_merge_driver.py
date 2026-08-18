#!/usr/bin/env python3
"""
Custom Git Merge Driver for Cadence Clinical Platform.
Handles programmatic, deterministic merging for metadata, baselines, and documentation,
while immediately escalating logical code conflicts (Python, JS, etc.) to manual resolution.
"""

import json
import os
import subprocess
import sys


def run_git_merge_file(ancestor, current, other) -> int:
    """Run standard git merge-file to write standard conflict markers into current."""
    try:
        res = subprocess.run(
            ["git", "merge-file", current, ancestor, other],
            capture_output=True,
            text=True,
        )
        return res.returncode
    except Exception as e:
        print(f"Error running git merge-file: {e}", file=sys.stderr)
        return 1


def is_logical_code(pathname: str) -> bool:
    """Check if the path represents logical code that requires manual escalation."""
    logical_extensions = {
        ".py",
        ".js",
        ".mjs",
        ".ts",
        ".tsx",
        ".jsx",
        ".vue",
        ".go",
        ".rs",
        ".c",
        ".cpp",
        ".h",
        ".java",
        ".sh",
        ".toml",
        ".yaml",
        ".yml",
    }
    _, ext = os.path.splitext(pathname.lower())
    if ext in logical_extensions:
        return True

    # Also check if it's within core application code directories
    code_dirs = [
        "apps/execution",
        "apps/designer",
        "packages/security",
    ]
    if any(pathname.startswith(d) for d in code_dirs):
        # Unless it's a known documentation directory inside those
        if "docs" not in pathname:
            return True

    return False


def merge_secrets_baseline(ancestor, current, other) -> bool:
    """Specialized merge logic for .secrets.baseline JSON."""
    try:
        with open(current, encoding="utf-8") as f:
            curr_data = json.load(f)
        with open(other, encoding="utf-8") as f:
            oth_data = json.load(f)
    except Exception as e:
        print(f"Error loading secrets baseline JSON files: {e}", file=sys.stderr)
        return False

    try:
        merged_data = curr_data.copy()

        # Merge filters_used
        filters = {f["path"]: f for f in curr_data.get("filters_used", [])}
        for f in oth_data.get("filters_used", []):
            filters[f["path"]] = f
        merged_data["filters_used"] = sorted(
            list(filters.values()), key=lambda x: x.get("path", "")
        )

        # Merge plugins_used
        plugins = {p["name"]: p for p in curr_data.get("plugins_used", [])}
        for p in oth_data.get("plugins_used", []):
            plugins[p["name"]] = p
        merged_data["plugins_used"] = sorted(
            list(plugins.values()), key=lambda x: x.get("name", "")
        )

        # Merge results
        curr_results = curr_data.get("results", {})
        oth_results = oth_data.get("results", {})
        all_files = set(curr_results.keys()).union(oth_results.keys())

        merged_results = {}
        for filename in all_files:
            curr_list = curr_results.get(filename, [])
            oth_list = oth_results.get(filename, [])

            # Deduplicate by hashed_secret
            secrets_by_hash = {}
            for secret in curr_list + oth_list:
                h = secret.get("hashed_secret")
                if h:
                    # Scrub line_number to avoid merge conflicts
                    clean_sec = secret.copy()
                    if "line_number" in clean_sec:
                        del clean_sec["line_number"]
                    secrets_by_hash[h] = clean_sec

            merged_results[filename] = sorted(
                list(secrets_by_hash.values()), key=lambda x: x.get("hashed_secret", "")
            )

        merged_data["results"] = merged_results

        # Clean generated_at to avoid timestamp friction
        if "generated_at" in merged_data:
            del merged_data["generated_at"]

        with open(current, "w", encoding="utf-8") as f:
            json.dump(merged_data, f, indent=2, sort_keys=True)
            f.write("\n")

        return True
    except Exception as e:
        print(f"Error processing secrets baseline merge logic: {e}", file=sys.stderr)
        return False


def merge_generic_json(ancestor, current, other) -> bool:
    """Generic JSON merge strategy that combines keys and lists recursively."""
    try:
        with open(current, encoding="utf-8") as f:
            curr_data = json.load(f)
        with open(other, encoding="utf-8") as f:
            oth_data = json.load(f)
    except Exception as e:
        print(f"Error loading generic JSON files: {e}", file=sys.stderr)
        return False

    def deep_merge(dict_a, dict_b):
        result = dict_a.copy()
        for k, v in dict_b.items():
            if k in result:
                if isinstance(result[k], dict) and isinstance(v, dict):
                    result[k] = deep_merge(result[k], v)
                elif isinstance(result[k], list) and isinstance(v, list):
                    # Combine lists and deduplicate primitives/simple dicts
                    combined = result[k] + v
                    unique = []
                    for item in combined:
                        if item not in unique:
                            unique.append(item)
                    result[k] = unique
                else:
                    # Conflict: default to keeping ours or theirs
                    pass
            else:
                result[k] = v
        return result

    try:
        if isinstance(curr_data, dict) and isinstance(oth_data, dict):
            merged = deep_merge(curr_data, oth_data)
        elif isinstance(curr_data, list) and isinstance(oth_data, list):
            combined = curr_data + oth_data
            merged = []
            for item in combined:
                if item not in merged:
                    merged.append(item)
        else:
            return False

        with open(current, "w", encoding="utf-8") as f:
            json.dump(merged, f, indent=2, sort_keys=True)
            f.write("\n")
        return True
    except Exception as e:
        print(f"Error executing generic JSON merge: {e}", file=sys.stderr)
        return False


def merge_markdown_text(ancestor, current, other) -> bool:
    """Merge markdown or text files by resolving simple non-overlapping updates cleanly.

    If overlapping changes occur, git merge-file leaves standard conflict markers
    in current and returns non-zero, in which case we return False to escalate.
    """
    rc = run_git_merge_file(ancestor, current, other)
    return rc == 0


def main():
    if len(sys.argv) < 5:
        print(
            "Usage: git_merge_driver.py <ancestor> <current> <other> <pathname>",
            file=sys.stderr,
        )
        sys.exit(1)

    ancestor = sys.argv[1]
    current = sys.argv[2]
    other = sys.argv[3]
    pathname = sys.argv[4]

    print(f"[Merge-Driver] processing: {pathname}", file=sys.stderr)

    # 1. Logical code files MUST immediately escalate to manual review
    if is_logical_code(pathname):
        print(
            f"[Merge-Driver] Logical code conflict in '{pathname}' - Escalating to manual review.",
            file=sys.stderr,
        )
        run_git_merge_file(ancestor, current, other)
        sys.exit(1)

    # 2. Handle secrets baseline
    if os.path.basename(pathname) == ".secrets.baseline":
        print(
            f"[Merge-Driver] Auto-resolving secrets baseline in '{pathname}'",
            file=sys.stderr,
        )
        success = merge_secrets_baseline(ancestor, current, other)
        if success:
            sys.exit(0)
        else:
            print(
                f"[Merge-Driver] Failed to merge secrets baseline '{pathname}' - Escalating to manual review.",
                file=sys.stderr,
            )
            run_git_merge_file(ancestor, current, other)
            sys.exit(1)

    # 3. Handle JSON metadata or configuration
    if pathname.lower().endswith(".json"):
        print(
            f"[Merge-Driver] Auto-resolving JSON metadata in '{pathname}'",
            file=sys.stderr,
        )
        success = merge_generic_json(ancestor, current, other)
        if success:
            sys.exit(0)
        else:
            print(
                f"[Merge-Driver] Failed to merge JSON '{pathname}' - Escalating to manual review.",
                file=sys.stderr,
            )
            run_git_merge_file(ancestor, current, other)
            sys.exit(1)

    # 4. Handle Markdown or text documentation
    if pathname.lower().endswith((".md", ".txt")):
        print(
            f"[Merge-Driver] Auto-resolving documentation conflict in '{pathname}'",
            file=sys.stderr,
        )
        success = merge_markdown_text(ancestor, current, other)
        if success:
            sys.exit(0)
        else:
            print(
                f"[Merge-Driver] Failed to merge doc/text '{pathname}' - Escalating to manual review.",
                file=sys.stderr,
            )
            sys.exit(1)

    # 5. Handle snapshot or binary .tar.gz files cleanly by keeping our version
    if pathname.lower().endswith(".tar.gz"):
        print(
            f"[Merge-Driver] Auto-resolving binary snapshot in '{pathname}' by preserving current branch version.",
            file=sys.stderr,
        )
        sys.exit(0)

    # Fallback to standard merge with conflict markers
    print(
        f"[Merge-Driver] Unhandled file type for '{pathname}' - Escalating to manual review.",
        file=sys.stderr,
    )
    run_git_merge_file(ancestor, current, other)
    sys.exit(1)


if __name__ == "__main__":
    main()
