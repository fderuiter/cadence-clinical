#!/usr/bin/env python3
"""Pre-commit hook to lint query string keywords and alias operators for uppercase style consistency."""

import ast
import re
import sys

# Precise regex to find lowercase/mixed-case keywords and alias operators,
# preventing false positives on variables, property access, parameters, or map keys.
KEYWORDS_RE = re.compile(
    r"(?<![\.\$:\w])\b(match|return|where|with|merge|create|delete|unwind|select|from|insert|update|as)\b(?![\w:])",
    re.IGNORECASE,
)


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
    if first_word:
        return first_word.group(0) in query_start_keywords
    return False


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

            # Look for lowercase/mixed-case keywords or alias operators in the query string
            for match in KEYWORDS_RE.finditer(val):
                word = match.group(1)
                if word != word.upper():
                    # Calculate precise line number of the match
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
