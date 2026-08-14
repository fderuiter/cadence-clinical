#!/usr/bin/env python3
"""Pre-commit hook to lint query string keywords and alias operators for uppercase style consistency."""

import ast
import re
import sys
from pathlib import Path

# Add repository root to sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

# Enforce Python 3.14+ runtime before loading standard modules or packages
if sys.version_info < (3, 14):
    try:
        from scripts.runtime_guard import enforce_python_runtime

        enforce_python_runtime()
    except Exception:
        sys.stderr.write(
            f"[FATAL] Incompatible Python runtime {sys.version.split()[0]} ({sys.executable}).\n"
            "Cadence Clinical requires Python 3.14+.\n"
            "Please run: uv run python scripts/pre_commit_query_linter.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime

# Precise regex to find lowercase/mixed-case keywords and alias operators,
# preventing false positives on variables, property access, parameters, or map keys.
KEYWORDS_RE = re.compile(
    r"(?<![\.\$:\w])\b(match|return|where|with|merge|create|delete|unwind|select|from|insert|update|as)\b(?![\w:])",
    re.IGNORECASE,
)


def strip_comments(val: str) -> str:
    """Strips SQL and Cypher comments from a query string to avoid false positives in comments."""
    lines = val.splitlines()
    cleaned_lines = []
    for line in lines:
        line_stripped = line.strip()
        if (
            line_stripped.startswith("//")
            or line_stripped.startswith("#")
            or line_stripped.startswith("--")
        ):
            continue
        # Also remove inline comment suffix
        if "//" in line:
            line = line.split("//", 1)[0]
        if "--" in line:
            line = line.split("--", 1)[0]
        cleaned_lines.append(line)
    return "\n".join(cleaned_lines)


def is_query_string(val: str) -> bool:
    """Determine if a string literal is a database query.

    Filters based on standard SQL and Cypher query start keywords,
    skipping leading whitespace and comments.

    Args:
        val: The string content to evaluate.

    Returns:
        bool: True if the string is identified as a query, False otherwise.
    """
    val_stripped = val.strip()
    # A single keyword/word alone is never a valid database query string.
    # A real query string always contains multiple components separated by spaces/brackets/newlines.
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
        # Skip SQL / Cypher comment lines
        if (
            line_stripped.startswith("//")
            or line_stripped.startswith("#")
            or line_stripped.startswith("--")
        ):
            continue
        cleaned_lines.append(line_stripped)

    if not cleaned_lines:
        return False

    # A query must contain at least some common query/database characters or multiple lines
    # or have query-specific constructs.
    # If the string is just plain english words with no punctuation, it's not a query.
    text_only = re.sub(r"[a-zA-Z0-9\s]", "", val_stripped)
    if not text_only and len(cleaned_lines) == 1:
        # Single line with only alphanumeric and spaces, e.g. "create arm" - not a query
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
    # Check if the first word matches any query start keyword
    first_word = re.match(r"^[a-z]+", first_line)
    if not first_word:
        return False

    start_kw = first_word.group(0)
    if start_kw not in query_start_keywords:
        return False

    # Extra heuristic: if the first word is with/update/create/delete/select/optional/return,
    # it must look like a query (e.g., have parameters like $, colons, parenthesis, operators, or multiple SQL keywords)
    # to avoid false positives on user-facing messages or labels.
    if start_kw in {
        "with",
        "update",
        "create",
        "delete",
        "select",
        "optional",
        "return",
        "match",
        "merge",
    }:
        # Check if it has any query punctuation
        has_query_punc = any(c in val_stripped for c in "()[]{}$:,;*=><")
        if not has_query_punc:
            return False

    return True


class QueryStyleVisitor(ast.NodeVisitor):
    """AST visitor to find and lint query string constants."""

    def __init__(self, filename: str) -> None:
        """Initialize the visitor with the filename."""
        self.filename = filename
        self.success = True

    def visit_Expr(self, node: ast.Expr) -> None:
        """Skip standalone expression statements (which are docstrings/comments)."""
        if isinstance(node.value, ast.Constant) and isinstance(node.value.value, str):
            return
        self.generic_visit(node)

    def visit_Constant(self, node: ast.Constant) -> None:
        """Process string constants to identify and lint query definitions."""
        if isinstance(node.value, str):
            val = node.value
            if not is_query_string(val):
                return

            val_clean = strip_comments(val)

            # Look for lowercase/mixed-case keywords or alias operators in the query string
            for match in KEYWORDS_RE.finditer(val_clean):
                word = match.group(1)
                if word != word.upper():
                    # Calculate precise line number of the match
                    string_offset = val.find(match.group(0))
                    if string_offset == -1:
                        string_offset = match.start()
                    lines_before = val[:string_offset].count("\n")
                    precise_lineno = node.lineno + lines_before

                    print(
                        f"Violation: {self.filename}:{precise_lineno} - Query contains lowercase/mixed-case keyword or alias operator: '{word}'"
                    )
                    print(f"  Offending Query: {val.strip()}")
                    self.success = False


def check_file(filename: str) -> bool:
    """Check a single python file for query style violations.

    Args:
        filename: The path to the Python file to lint.

    Returns:
        bool: True if no violations are found, False otherwise.
    """
    try:
        with open(filename, encoding="utf-8") as f:
            content = f.read()
    except Exception:
        # If we cannot read the file (e.g. deleted or binary), skip it.
        return True

    # Fast pre-filter: if none of the query keywords are in the file at all,
    # skip parsing to avoid any performance overhead.
    fast_keywords = [
        "match",
        "return",
        "where",
        "with",
        "merge",
        "create",
        "delete",
        "unwind",
        "select",
        "from",
        "insert",
        "update",
    ]
    if not any(kw in content.lower() for kw in fast_keywords):
        return True

    try:
        tree = ast.parse(content, filename=filename)
    except SyntaxError:
        # Ignore syntax errors and let other Python tooling report them.
        return True

    visitor = QueryStyleVisitor(filename)
    visitor.visit(tree)
    return visitor.success


def main() -> None:
    """Main execution function to process passed file arguments."""
    files = sys.argv[1:]
    if not files:
        sys.exit(0)

    success = True
    for file in files:
        if file.endswith(".py"):
            if not check_file(file):
                success = False

    if not success:
        sys.exit(1)


if __name__ == "__main__":
    main()
