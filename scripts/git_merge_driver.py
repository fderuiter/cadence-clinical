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


def _has_dict_collision(curr: dict, oth: dict) -> bool:
    """Check for structural mismatches or scalar value collisions between two dictionaries."""
    common_keys = set(curr.keys()).intersection(set(oth.keys()))
    for k in common_keys:
        val_c = curr[k]
        val_o = oth[k]
        if type(val_c) is not type(val_o) and not (
            isinstance(val_c, (int, float)) and isinstance(val_o, (int, float))
        ):
            return True  # Structural mismatch
        if isinstance(val_c, dict):
            if _has_dict_collision(val_c, val_o):
                return True
        elif isinstance(val_c, list):
            if _has_list_collision(val_c, val_o):
                return True
        else:
            if val_c != val_o:
                return True  # Scalar value collision
    return False


def _has_list_collision(curr_list: list, oth_list: list) -> bool:
    """Check for collisions or mismatches between two lists."""
    curr_dicts = [x for x in curr_list if isinstance(x, dict)]
    oth_dicts = [x for x in oth_list if isinstance(x, dict)]

    if curr_dicts or oth_dicts:
        if len(curr_dicts) != len(curr_list) or len(oth_dicts) != len(oth_list):
            return True  # Structural mismatch (mixed types)

        id_fields = ["name", "id", "path", "hashed_secret", "key"]
        id_field = None
        for f in id_fields:
            if all(f in d for d in curr_dicts) and all(f in d for d in oth_dicts):
                id_field = f
                break

        if id_field:
            map_c = {d[id_field]: d for d in curr_dicts}
            map_o = {d[id_field]: d for d in oth_dicts}
            common_ids = set(map_c.keys()).intersection(set(map_o.keys()))
            for cid in common_ids:
                if _has_dict_collision(map_c[cid], map_o[cid]):
                    return True
    return False


def _deep_merge_dicts(curr: dict, oth: dict) -> dict:
    """Recursively merge two dictionaries assuming no collisions exist."""
    res = curr.copy()
    for k, v in oth.items():
        if k in res:
            if isinstance(res[k], dict) and isinstance(v, dict):
                res[k] = _deep_merge_dicts(res[k], v)
            elif isinstance(res[k], list) and isinstance(v, list):
                res[k] = _merge_lists(res[k], v)
            else:
                res[k] = v
        else:
            res[k] = v
    return res


def _merge_lists(curr_list: list, oth_list: list) -> list:
    """Merge two lists while preserving uniqueness."""
    combined = list(curr_list)
    for item in oth_list:
        if item not in combined:
            combined.append(item)
    return combined


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

    if not isinstance(curr_data, dict) or not isinstance(oth_data, dict):
        return False

    # Make copies and scrub generated_at timestamp to avoid friction
    curr_clean = curr_data.copy()
    oth_clean = oth_data.copy()
    curr_clean.pop("generated_at", None)
    oth_clean.pop("generated_at", None)

    # 1. Check top-level scalar & sub-dict collisions/mismatches
    special_keys = {"filters_used", "plugins_used", "results"}
    all_top_keys = set(curr_clean.keys()).union(set(oth_clean.keys()))

    for key in all_top_keys:
        if key in special_keys:
            continue
        in_curr = key in curr_clean
        in_oth = key in oth_clean

        if in_curr and in_oth:
            val_c = curr_clean[key]
            val_o = oth_clean[key]

            # Structural type mismatch
            if type(val_c) is not type(val_o) and not (
                isinstance(val_c, (int, float)) and isinstance(val_o, (int, float))
            ):
                return False

            if isinstance(val_c, dict):
                if _has_dict_collision(val_c, val_o):
                    return False
            elif isinstance(val_c, list):
                if _has_list_collision(val_c, val_o):
                    return False
            else:
                if val_c != val_o:
                    return False  # Scalar value collision

    # 2. Check filters_used
    filters_c_raw = curr_clean.get("filters_used", [])
    filters_o_raw = oth_clean.get("filters_used", [])
    if not isinstance(filters_c_raw, list) or not isinstance(filters_o_raw, list):
        return False

    filters_c = {
        f["path"]: f for f in filters_c_raw if isinstance(f, dict) and "path" in f
    }
    filters_o = {
        f["path"]: f for f in filters_o_raw if isinstance(f, dict) and "path" in f
    }

    # Check for scalar collisions in matching filters
    for path in set(filters_c.keys()).intersection(set(filters_o.keys())):
        if _has_dict_collision(filters_c[path], filters_o[path]):
            return False

    # 3. Check plugins_used
    plugins_c_raw = curr_clean.get("plugins_used", [])
    plugins_o_raw = oth_clean.get("plugins_used", [])
    if not isinstance(plugins_c_raw, list) or not isinstance(plugins_o_raw, list):
        return False

    plugins_c = {
        p["name"]: p for p in plugins_c_raw if isinstance(p, dict) and "name" in p
    }
    plugins_o = {
        p["name"]: p for p in plugins_o_raw if isinstance(p, dict) and "name" in p
    }

    # Check for scalar collisions in matching plugins
    for name in set(plugins_c.keys()).intersection(set(plugins_o.keys())):
        if _has_dict_collision(plugins_c[name], plugins_o[name]):
            return False

    # 4. Check results
    curr_results = curr_clean.get("results", {})
    oth_results = oth_clean.get("results", {})
    if not isinstance(curr_results, dict) or not isinstance(oth_results, dict):
        return False

    all_files = set(curr_results.keys()).union(set(oth_results.keys()))
    for filename in all_files:
        curr_list = curr_results.get(filename, [])
        oth_list = oth_results.get(filename, [])
        if not isinstance(curr_list, list) or not isinstance(oth_list, list):
            return False

        secrets_by_hash_c = {}
        for secret in curr_list:
            if not isinstance(secret, dict):
                return False
            h = secret.get("hashed_secret")
            if h:
                sec_copy = secret.copy()
                sec_copy.pop("line_number", None)
                secrets_by_hash_c[h] = sec_copy

        secrets_by_hash_o = {}
        for secret in oth_list:
            if not isinstance(secret, dict):
                return False
            h = secret.get("hashed_secret")
            if h:
                sec_copy = secret.copy()
                sec_copy.pop("line_number", None)
                secrets_by_hash_o[h] = sec_copy

        # Check secret metadata collisions for common hashed_secrets
        common_hashes = set(secrets_by_hash_c.keys()).intersection(
            set(secrets_by_hash_o.keys())
        )
        for h in common_hashes:
            if _has_dict_collision(secrets_by_hash_c[h], secrets_by_hash_o[h]):
                return False

    # Build merged data since no collisions were detected
    merged_data = {}
    for key in all_top_keys:
        if key in special_keys:
            continue
        in_curr = key in curr_clean
        in_oth = key in oth_clean

        if in_curr and in_oth:
            if isinstance(curr_clean[key], dict):
                merged_data[key] = _deep_merge_dicts(curr_clean[key], oth_clean[key])
            elif isinstance(curr_clean[key], list):
                merged_data[key] = _merge_lists(curr_clean[key], oth_clean[key])
            else:
                merged_data[key] = curr_clean[key]
        elif in_curr:
            merged_data[key] = curr_clean[key]
        else:
            merged_data[key] = oth_clean[key]

    merged_filters = dict(filters_c)
    for p_path, p_obj in filters_o.items():
        if p_path not in merged_filters:
            merged_filters[p_path] = p_obj
    merged_data["filters_used"] = sorted(
        list(merged_filters.values()), key=lambda x: x.get("path", "")
    )

    merged_plugins = dict(plugins_c)
    for p_name, p_obj in plugins_o.items():
        if p_name not in merged_plugins:
            merged_plugins[p_name] = p_obj
    merged_data["plugins_used"] = sorted(
        list(merged_plugins.values()), key=lambda x: x.get("name", "")
    )

    merged_results = {}
    for filename in all_files:
        curr_list = curr_results.get(filename, [])
        oth_list = oth_results.get(filename, [])

        secrets_by_hash = {}
        for secret in curr_list + oth_list:
            h = secret.get("hashed_secret")
            if h:
                clean_sec = secret.copy()
                clean_sec.pop("line_number", None)
                if h not in secrets_by_hash:
                    secrets_by_hash[h] = clean_sec

        merged_results[filename] = sorted(
            list(secrets_by_hash.values()), key=lambda x: x.get("hashed_secret", "")
        )

    merged_data["results"] = merged_results

    try:
        with open(current, "w", encoding="utf-8") as f:
            json.dump(merged_data, f, indent=2, sort_keys=True)
            f.write("\n")
        return True
    except Exception as e:
        print(f"Error writing merged secrets baseline: {e}", file=sys.stderr)
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
                    sub = deep_merge(result[k], v)
                    if sub is None:
                        return None
                    result[k] = sub
                elif isinstance(result[k], list) and isinstance(v, list):
                    combined = result[k] + v
                    unique = []
                    for item in combined:
                        if item not in unique:
                            unique.append(item)
                    result[k] = unique
                elif type(result[k]) is type(v) and not isinstance(
                    result[k], (dict, list)
                ):
                    if result[k] != v:
                        return None  # Scalar value collision
                else:
                    return None  # Structural mismatch
            else:
                result[k] = v
        return result

    try:
        if isinstance(curr_data, dict) and isinstance(oth_data, dict):
            merged = deep_merge(curr_data, oth_data)
            if merged is None:
                return False
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
    """Merge markdown or text files by resolving simple concurrent updates cleanly."""
    # First, run standard merge-file to see if it cleanly succeeds without conflict markers
    rc = run_git_merge_file(ancestor, current, other)
    if rc == 0:
        return True

    # If conflict markers exist, attempt to resolve them cleanly
    try:
        with open(current, encoding="utf-8") as f:
            content = f.read()

        if "<<<<<<<" not in content:
            return True

        # Custom resolution: split by line and parse conflict blocks
        lines = content.splitlines()
        resolved_lines = []
        i = 0
        n = len(lines)
        while i < n:
            line = lines[i]
            if line.startswith("<<<<<<<"):
                # Conflict starts
                ours_block = []
                theirs_block = []
                i += 1
                # Parse ours block until =======
                while i < n and not lines[i].startswith("======="):
                    ours_block.append(lines[i])
                    i += 1
                i += 1  # Skip =======
                # Parse theirs block until >>>>>>>
                while i < n and not lines[i].startswith(">>>>>>>"):
                    theirs_block.append(lines[i])
                    i += 1
                i += 1  # Skip >>>>>>>

                # Clean resolution of independent bullet points / documentation lines
                # If they are distinct lines, we can just concatenate or union them
                combined_block = []
                # Remove duplicate lines from both sides, preserving order
                seen = set()
                for line in ours_block + theirs_block:
                    if line not in seen or line.strip() == "":
                        seen.add(line)
                        combined_block.append(line)

                resolved_lines.extend(combined_block)
            else:
                resolved_lines.append(line)
                i += 1

        with open(current, "w", encoding="utf-8") as f:
            f.write("\n".join(resolved_lines) + "\n")
        return True

    except Exception as e:
        print(f"Error resolving text conflicts: {e}", file=sys.stderr)
        return False


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
    filename = os.path.basename(pathname)
    if filename == ".secrets.baseline" or filename.endswith(".secrets.baseline"):
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
            run_git_merge_file(ancestor, current, other)
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
