import subprocess
import time
from pathlib import Path


def run_merge_driver(
    ancestor_content: str, current_content: str, other_content: str, filename: str
):
    """Helper to run the custom merge driver on temporary files and return the result and exit code."""
    # Write to temp files
    ancestor_path = Path(f"/tmp/ancestor_{filename}")
    current_path = Path(f"/tmp/current_{filename}")
    other_path = Path(f"/tmp/other_{filename}")

    ancestor_path.write_text(ancestor_content, encoding="utf-8")
    current_path.write_text(current_content, encoding="utf-8")
    other_path.write_text(other_content, encoding="utf-8")

    try:
        # Run driver
        start_time = time.time()
        repo_root = Path(__file__).resolve().parent.parent
        driver_path = repo_root / "scripts" / "ast_merge_driver.py"
        res = subprocess.run(
            [
                "python3",
                str(driver_path),
                str(ancestor_path),
                str(current_path),
                str(other_path),
                filename,
            ],
            capture_output=True,
            text=True,
        )
        duration = time.time() - start_time

        merged_content = current_path.read_text(encoding="utf-8")
        return res.returncode, merged_content, duration, res.stderr, res.stdout
    finally:
        # Clean up
        if ancestor_path.exists():
            ancestor_path.unlink()
        if current_path.exists():
            current_path.unlink()
        if other_path.exists():
            other_path.unlink()


def test_python_reordered_helper_functions():
    """Verify that reordered helper functions in Python auto-resolve cleanly without conflict.

    @req:PRD-SYS-001
    """
    ancestor = """
def first():
    return 1

def second():
    return 2
"""

    current = """
def second():
    return 2

def first():
    return 1
"""

    other = """
def first():
    return 1

def second():
    return 2
"""

    code, merged, duration, stderr, stdout = run_merge_driver(
        ancestor, current, other, "helpers.py"
    )
    assert code == 0, f"Driver failed: {stderr}\n{stdout}"
    assert "def second" in merged
    assert "def first" in merged
    assert duration < 10.0


def test_js_reordered_helper_functions():
    """Verify that reordered helper functions in JS auto-resolve cleanly without conflict.

    @req:PRD-SYS-001
    """
    ancestor = """
export function first() {
  return 1;
}

export function second() {
  return 2;
}
"""

    current = """
export function second() {
  return 2;
}

export function first() {
  return 1;
}
"""

    other = """
export function first() {
  return 1;
}

export function second() {
  return 2;
}
"""

    code, merged, duration, stderr, stdout = run_merge_driver(
        ancestor, current, other, "helpers.js"
    )
    assert code == 0, f"Driver failed: {stderr}\n{stdout}"
    assert "export function second" in merged
    assert "export function first" in merged
    assert duration < 10.0


def test_python_edited_and_reordered():
    """Verify that edits to one function on one branch and reordering on the other branch merge cleanly.

    @req:PRD-SYS-002
    """
    ancestor = """
def first():
    return 1

def second():
    return 2
"""

    # Branch A reordered
    current = """
def second():
    return 2

def first():
    return 1
"""

    # Branch B edited 'second'
    other = """
def first():
    return 1

def second():
    return 2 + 2
"""

    code, merged, duration, stderr, stdout = run_merge_driver(
        ancestor, current, other, "helpers.py"
    )
    assert code == 0, f"Driver failed: {stderr}\n{stdout}"
    assert "return 2 + 2" in merged
    assert duration < 10.0


def test_python_overlapping_logical_edits_fallback():
    """Verify that overlapping logical edits to the same node trigger a fallback to standard Git conflict.

    @req:PRD-SYS-003
    """
    ancestor = """
def first():
    return 1
"""

    current = """
def first():
    return 2
"""

    other = """
def first():
    return 3
"""

    code, merged, duration, stderr, stdout = run_merge_driver(
        ancestor, current, other, "helpers.py"
    )
    # Should exit with git merge-file's return code indicating conflicts
    assert code != 0
    assert "<<<<<<<" in merged or "=======" in merged
    assert duration < 10.0


def test_python_imports_merged_and_sorted():
    """Verify that python imports are merged, sorted, and deduped.

    @req:PRD-SYS-002
    """
    ancestor = """
import sys  # noqa: F401
"""

    current = """
import sys  # noqa: F401
import os  # noqa: F401
"""

    other = """
import sys  # noqa: F401
import json  # noqa: F401
"""

    code, merged, duration, stderr, stdout = run_merge_driver(
        ancestor, current, other, "helpers.py"
    )
    assert code == 0, f"Driver failed: {stderr}\n{stdout}"
    assert "import json" in merged
    assert "import os" in merged
    assert "import sys" in merged
    assert duration < 10.0
