#!/usr/bin/env python3
"""AST-Aware Custom Git Merge Driver.

This script implements an AST-aware 3-way merge driver for Python and JavaScript/TypeScript.
It is registered with Git to automatically resolve non-overlapping structural edits
(like shifted function positions, sorted imports, etc.) and falls back to standard
line-by-line conflict resolution when logical code changes collide on the same AST nodes.
"""

import ast
import json
import os
import shutil
import subprocess
import sys


def is_comment_or_blank(line: str, file_type: str) -> bool:
    """Check if a line is a comment or empty for the given file type."""
    stripped = line.strip()
    if not stripped:
        return True
    if file_type == "python":
        return stripped.startswith("#")
    if file_type in ("javascript", "typescript"):
        return (
            stripped.startswith("//")
            or stripped.startswith("/*")
            or stripped.startswith("*")
            or stripped.endswith("*/")
        )
    return False


def parse_python_blocks(
    source: str,
) -> tuple[dict[str, list[str]], list[str]] | None:
    """Parse Python source into a dict of block content and an order list of block keys."""
    lines = source.splitlines(keepends=True)
    num_lines = len(lines)

    try:
        tree = ast.parse(source)
    except Exception:
        return None

    line_to_block = [None] * num_lines

    # 1. Identify imports
    import_lines = []
    for node in tree.body:
        if isinstance(node, (ast.Import, ast.ImportFrom)):
            for line_idx in range(node.lineno, node.end_lineno + 1):
                import_lines.append(line_idx - 1)

    for idx in import_lines:
        line_to_block[idx] = "imports"

    # 2. Identify functions and classes
    nodes = []
    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            nodes.append(node)

    blocks = {}
    for node in nodes:
        node_start = node.lineno
        if hasattr(node, "decorator_list") and node.decorator_list:
            node_start = min(node_start, min(dec.lineno for dec in node.decorator_list))
        node_end = node.end_lineno

        # Scan preceding comments
        block_start = node_start
        for line_idx in range(node_start - 1, 0, -1):
            line_str = lines[line_idx - 1].strip()
            if line_to_block[line_idx - 1] is not None:
                break
            if line_str.startswith("#") or line_str == "":
                block_start = line_idx
            else:
                break

        key_prefix = (
            "func"
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
            else "class"
        )
        block_key = f"{key_prefix}:{node.name}"

        orig_key = block_key
        counter = 1
        while block_key in blocks:
            block_key = f"{orig_key}_{counter}"
            counter += 1

        blocks[block_key] = True  # Reserve key
        for line_idx in range(block_start, node_end + 1):
            if line_to_block[line_idx - 1] is None:
                line_to_block[line_idx - 1] = block_key

    # 3. Group other lines
    other_counter = 0
    in_other = False
    current_other_key = None

    for idx in range(num_lines):
        if line_to_block[idx] is None:
            if not in_other:
                current_other_key = f"other:{other_counter}"
                other_counter += 1
                in_other = True
            line_to_block[idx] = current_other_key
        else:
            in_other = False

    # Build final blocks dictionary
    blocks_content = {}
    block_order = []
    for idx in range(num_lines):
        key = line_to_block[idx]
        if key not in blocks_content:
            blocks_content[key] = []
            block_order.append(key)
        blocks_content[key].append(lines[idx])

    return blocks_content, block_order


def parse_js_blocks(
    filepath: str, file_type: str
) -> tuple[dict[str, list[str]], list[str]] | None:
    """Parse JavaScript/TypeScript source into a dict of block content and an order list of block keys."""
    try:
        with open(filepath, encoding="utf-8") as f:
            source = f.read()
    except Exception:
        return None

    lines = source.splitlines(keepends=True)
    num_lines = len(lines)

    # Run Node script to get AST ranges
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    node_script = os.path.join(repo_root, "scripts", "parse_js_blocks.js")
    if not os.path.exists(node_script):
        return None

    res = subprocess.run(
        ["node", node_script, filepath], capture_output=True, text=True
    )
    if res.returncode != 0:
        return None

    try:
        nodes = json.loads(res.stdout)
    except Exception:
        return None

    line_to_block = [None] * num_lines

    # Process imports first
    for node in nodes:
        if node["key"] == "imports":
            start = node["start"]
            end = node["end"]
            for line_idx in range(start, end + 1):
                if 1 <= line_idx <= num_lines:
                    line_to_block[line_idx - 1] = "imports"

    # Process other nodes (functions, classes, vars)
    blocks = {}
    for node in nodes:
        if node["key"] != "imports":
            node_start = node["start"]
            node_end = node["end"]

            # Scan preceding comments
            block_start = node_start
            for line_idx in range(node_start - 1, 0, -1):
                if 1 <= line_idx <= num_lines:
                    line_str = lines[line_idx - 1].strip()
                    if line_to_block[line_idx - 1] is not None:
                        break
                    if is_comment_or_blank(line_str, file_type):
                        block_start = line_idx
                    else:
                        break

            block_key = node["key"]
            orig_key = block_key
            counter = 1
            while block_key in blocks:
                block_key = f"{orig_key}_{counter}"
                counter += 1

            blocks[block_key] = True
            for line_idx in range(block_start, node_end + 1):
                if 1 <= line_idx <= num_lines:
                    if line_to_block[line_idx - 1] is None:
                        line_to_block[line_idx - 1] = block_key

    # Group remaining lines into 'other' blocks
    other_counter = 0
    in_other = False
    current_other_key = None

    for idx in range(num_lines):
        if line_to_block[idx] is None:
            if not in_other:
                current_other_key = f"other:{other_counter}"
                other_counter += 1
                in_other = True
            line_to_block[idx] = current_other_key
        else:
            in_other = False

    # Build final blocks dictionary
    blocks_content = {}
    block_order = []
    for idx in range(num_lines):
        key = line_to_block[idx]
        if key not in blocks_content:
            blocks_content[key] = []
            block_order.append(key)
        blocks_content[key].append(lines[idx])

    return blocks_content, block_order


def merge_blocks(
    blocks_o: dict[str, list[str]],
    order_o: list[str],
    blocks_a: dict[str, list[str]],
    order_a: list[str],
    blocks_b: dict[str, list[str]],
    order_b: list[str],
    file_type: str,
) -> tuple[dict[str, list[str]], list[str]] | None:
    """Perform structural 3-way merge on the parsed blocks."""
    all_keys = set(blocks_o.keys()) | set(blocks_a.keys()) | set(blocks_b.keys())
    blocks_merged = {}

    for k in all_keys:
        if k == "imports":
            continue
        content_o = "".join(blocks_o[k]) if k in blocks_o else None
        content_a = "".join(blocks_a[k]) if k in blocks_a else None
        content_b = "".join(blocks_b[k]) if k in blocks_b else None

        # 1. Block deleted in both
        if k not in blocks_a and k not in blocks_b:
            continue

        # 2. Block deleted in B, exists in O and A
        if k in blocks_o and k in blocks_a and k not in blocks_b:
            if content_a == content_o:
                # Unchanged in A, deleted in B -> delete
                continue
            # Modified in A, deleted in B -> conflict!
            return None

        # 3. Block deleted in A, exists in O and B
        if k in blocks_o and k in blocks_b and k not in blocks_a:
            if content_b == content_o:
                # Unchanged in B, deleted in A -> delete
                continue
            # Modified in B, deleted in A -> conflict!
            return None

        # 4. Block added in both A and B (not in O)
        if k not in blocks_o and k in blocks_a and k in blocks_b:
            if content_a == content_b:
                blocks_merged[k] = blocks_a[k]
            else:
                # Added on both with different content -> conflict!
                return None

        # 5. Block added in A only
        elif k not in blocks_o and k in blocks_a and k not in blocks_b:
            blocks_merged[k] = blocks_a[k]

        # 6. Block added in B only
        elif k not in blocks_o and k not in blocks_a and k in blocks_b:
            blocks_merged[k] = blocks_b[k]

        # 7. Block exists in all three
        elif k in blocks_o and k in blocks_a and k in blocks_b:
            if content_a == content_b:
                blocks_merged[k] = blocks_a[k]
            elif content_a == content_o and content_b != content_o:
                blocks_merged[k] = blocks_b[k]
            elif content_b == content_o and content_a != content_o:
                blocks_merged[k] = blocks_a[k]
            else:
                # Both modified differently -> conflict!
                return None

    # Merge imports block specially if present
    if "imports" in all_keys:
        imports_o = blocks_o.get("imports", [])
        imports_a = blocks_a.get("imports", [])
        imports_b = blocks_b.get("imports", [])

        def get_clean_imports(lines):
            cleaned = []
            for line in lines:
                stripped = line.strip()
                if stripped and not is_comment_or_blank(stripped, file_type):
                    cleaned.append(stripped)
            return cleaned

        clean_o = get_clean_imports(imports_o)
        clean_a = get_clean_imports(imports_a)
        clean_b = get_clean_imports(imports_b)

        merged_set = set(clean_a) | set(clean_b)
        for imp in clean_o:
            if (
                imp not in clean_a
                and imp in clean_b
                or imp not in clean_b
                and imp in clean_a
            ):
                merged_set.discard(imp)

        sorted_imports = sorted(list(merged_set))
        blocks_merged["imports"] = [imp + "\n" for imp in sorted_imports]

    # Order the merged blocks
    keep_keys = set(blocks_merged.keys())

    # If A's order has not changed from O, but B's order changed, prefer B's order
    if order_a == order_o and order_b != order_o:
        preferred_order = order_b
        secondary_order = order_a
    else:
        preferred_order = order_a
        secondary_order = order_b

    merged_order = [k for k in preferred_order if k in keep_keys]
    b_only = [k for k in secondary_order if k in keep_keys and k not in merged_order]

    for k in b_only:
        idx_b = secondary_order.index(k)
        inserted = False

        for i in range(idx_b - 1, -1, -1):
            pred = secondary_order[i]
            if pred in merged_order:
                idx_m = merged_order.index(pred)
                merged_order.insert(idx_m + 1, k)
                inserted = True
                break

        if not inserted:
            for i in range(idx_b + 1, len(secondary_order)):
                succ = secondary_order[i]
                if succ in merged_order:
                    idx_m = merged_order.index(succ)
                    merged_order.insert(idx_m, k)
                    inserted = True
                    break

        if not inserted:
            merged_order.append(k)

    return blocks_merged, merged_order


def fallback_to_git_merge(ancestor: str, current: str, other: str) -> int:
    """Run git merge-file to perform a standard line-level 3-way merge with conflict markers."""
    res = subprocess.run(
        ["git", "merge-file", current, ancestor, other],
        capture_output=True,
        text=True,
    )
    return res.returncode


def format_file(file_path: str, file_type: str):
    """Run project code formatting/linting on the merged file."""
    if file_type == "python":
        if shutil.which("uv"):
            subprocess.run(
                ["uv", "run", "ruff", "format", file_path], capture_output=True
            )
            subprocess.run(
                ["uv", "run", "ruff", "check", "--fix", file_path],
                capture_output=True,
            )
    elif file_type in ("javascript", "typescript"):
        if shutil.which("pnpm"):
            subprocess.run(
                ["pnpm", "exec", "prettier", "--write", file_path],
                capture_output=True,
            )
        elif shutil.which("npx"):
            subprocess.run(
                ["npx", "prettier", "--write", file_path],
                capture_output=True,
            )


def log_resolved_file(file_path: str):
    """Log successfully auto-resolved files to report.json."""
    report_path = "/tmp/ast_merge_report.json"
    data = {"resolved_files": []}
    if os.path.exists(report_path):
        try:
            with open(report_path, encoding="utf-8") as f:
                data = json.load(f)
        except Exception:
            pass

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    rel_path = (
        os.path.relpath(file_path, repo_root)
        if file_path.startswith(repo_root)
        else file_path
    )
    if rel_path not in data["resolved_files"]:
        data["resolved_files"].append(rel_path)

    try:
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
    except Exception:
        pass


def main():
    if len(sys.argv) < 5:
        print(
            "Usage: python3 ast_merge_driver.py <ancestor> <current> <other> <pathname>"
        )
        sys.exit(1)

    ancestor, current, other, pathname = sys.argv[1:5]

    # Detect file type based on extension
    _, ext = os.path.splitext(pathname.lower())
    if ext == ".py":
        file_type = "python"
    elif ext in (".js", ".ts", ".jsx", ".tsx"):
        file_type = "javascript"
    else:
        # Unsupported file type; fallback to standard line merge
        sys.exit(fallback_to_git_merge(ancestor, current, other))

    # Parse Ancestor (O)
    if file_type == "python":
        try:
            with open(ancestor, encoding="utf-8") as f:
                src_o = f.read()
            res_o = parse_python_blocks(src_o)
        except Exception:
            res_o = None
    else:
        res_o = parse_js_blocks(ancestor, file_type)

    if not res_o:
        sys.exit(fallback_to_git_merge(ancestor, current, other))
    blocks_o, order_o = res_o

    # Parse Current (A)
    if file_type == "python":
        try:
            with open(current, encoding="utf-8") as f:
                src_a = f.read()
            res_a = parse_python_blocks(src_a)
        except Exception:
            res_a = None
    else:
        res_a = parse_js_blocks(current, file_type)

    if not res_a:
        sys.exit(fallback_to_git_merge(ancestor, current, other))
    blocks_a, order_a = res_a

    # Parse Other (B)
    if file_type == "python":
        try:
            with open(other, encoding="utf-8") as f:
                src_b = f.read()
            res_b = parse_python_blocks(src_b)
        except Exception:
            res_b = None
    else:
        res_b = parse_js_blocks(other, file_type)

    if not res_b:
        sys.exit(fallback_to_git_merge(ancestor, current, other))
    blocks_b, order_b = res_b

    # Perform structural merge
    merge_res = merge_blocks(
        blocks_o, order_o, blocks_a, order_a, blocks_b, order_b, file_type
    )
    if not merge_res:
        # Overlapping logical edit or conflict; safe fallback
        sys.exit(fallback_to_git_merge(ancestor, current, other))

    blocks_merged, merged_order = merge_res

    # Assemble the final merged source
    merged_source = ""
    for k in merged_order:
        merged_source += "".join(blocks_merged[k])

    # Write back to current (%A)
    try:
        with open(current, "w", encoding="utf-8") as f:
            f.write(merged_source)
    except Exception:
        sys.exit(fallback_to_git_merge(ancestor, current, other))

    # Apply project linter/formatter styles
    format_file(current, file_type)

    # Log successful AST-resolved merge
    log_resolved_file(pathname)

    # Return success (0)
    sys.exit(0)


if __name__ == "__main__":
    main()
