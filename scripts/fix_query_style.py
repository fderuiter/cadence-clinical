#!/usr/bin/env python3
import ast
import re
import sys
from pathlib import Path

KEYWORDS_RE = re.compile(
    r"(?<![\.\$:\w])\b(match|return|where|with|merge|create|delete|unwind|select|from|insert|update|as)\b(?![\w:])",
    re.IGNORECASE,
)


def is_query_string(val: str) -> bool:
    val_stripped = val.strip()
    if (
        not val_stripped
        or " " not in val_stripped
        and "\n" not in val_stripped
        and "(" not in val_stripped
    ):
        return False

    lines = val_stripped.splitlines()
    cleaned_lines = []
    for line in lines:
        line_stripped = line.strip()
        if not line_stripped:
            continue
        if (
            line_stripped.startswith("//")
            or line_stripped.startswith("#")
            or line_stripped.startswith("--")
        ):
            continue
        cleaned_lines.append(line_stripped)

    if not cleaned_lines:
        return False

    text_only = re.sub(r"[a-zA-Z0-9\s]", "", val_stripped)
    if not text_only and len(cleaned_lines) == 1:
        return False

    first_line = cleaned_lines[0].lower()
    query_start_keywords = {
        "match",
        "merge",
        "create",
        "optional",
        "return",
        "select",
        "insert",
        "update",
        "delete",
        "with",
        "pragma",
    }
    first_word = re.match(r"^[a-z]+", first_line)
    if not first_word:
        return False

    start_kw = first_word.group(0)
    if start_kw not in query_start_keywords:
        return False

    if start_kw in {"with", "update", "create", "delete", "select", "optional", "return", "match", "merge"}:
        has_query_punc = any(c in val_stripped for c in "()[]{}$:,;*=><")
        if not has_query_punc:
            return False

    return True


def fix_file(filepath: Path) -> bool:
    content = filepath.read_text(encoding="utf-8")
    try:
        tree = ast.parse(content)
    except SyntaxError:
        return False

    queries = []
    for node in ast.walk(tree):
        if isinstance(node, ast.Constant) and isinstance(node.value, str):
            val = node.value
            if is_query_string(val):
                queries.append(val)

    if not queries:
        return False

    queries = sorted(list(set(queries)), key=len, reverse=True)

    new_content = content
    modified = False
    for q in queries:
        lines = q.splitlines()
        fixed_lines = []
        for line in lines:
            line_strip = line.strip()
            if (
                line_strip.startswith("//")
                or line_strip.startswith("#")
                or line_strip.startswith("--")
            ):
                fixed_lines.append(line)
                continue

            comment_char = None
            if "//" in line:
                comment_char = "//"
            elif "--" in line:
                comment_char = "--"

            if comment_char:
                code_part, comment_part = line.split(comment_char, 1)
                new_code_part = KEYWORDS_RE.sub(
                    lambda m: m.group(1).upper(), code_part
                )
                fixed_lines.append(new_code_part + comment_char + comment_part)
            else:
                new_code = KEYWORDS_RE.sub(lambda m: m.group(1).upper(), line)
                fixed_lines.append(new_code)

        fixed_q = "\n".join(fixed_lines)
        if fixed_q != q:
            # Replace exactly the representation in Python source code
            if q in new_content:
                new_content = new_content.replace(q, fixed_q)
                modified = True

    if modified and new_content != content:
        filepath.write_text(new_content, encoding="utf-8")
        print(f"Fixed query style in {filepath}")
        return True
    return False


def main():
    target_files = []
    if len(sys.argv) > 1:
        for arg in sys.argv[1:]:
            p = Path(arg).resolve()
            if p.is_file():
                target_files.append(p)
    else:
        # Search all python files in apps/ and packages/
        repo_root = Path(__file__).resolve().parent.parent
        for folder in (repo_root / "apps", repo_root / "packages"):
            for p in folder.rglob("*.py"):
                target_files.append(p)

    for filepath in sorted(target_files):
        fix_file(filepath)


if __name__ == "__main__":
    main()
