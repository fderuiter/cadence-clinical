"""Pre-commit hook for automated schema documentation regeneration and staging.

This script identifies if any SQLAlchemy models, database files, or schema generator
script files have been modified. If so, it executes the schema documentation generator
and stages the updated visualizer and CDISC USDM catalog artifacts.
"""

import os
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))


def get_staged_files() -> list[str]:
    """Retrieve the list of currently staged files in git."""
    try:
        result = subprocess.run(
            ["git", "diff", "--cached", "--name-only", "--diff-filter=ACM"],
            capture_output=True,
            text=True,
            check=True,
        )
        return [line.strip() for line in result.stdout.splitlines() if line.strip()]
    except (subprocess.SubprocessError, FileNotFoundError) as e:
        print(
            f"Warning: Failed to retrieve staged files from git: {e}", file=sys.stderr
        )
        return []


def should_trigger_schema_doc(staged_files: list[str]) -> bool:
    """Determine if any staged files should trigger schema doc regeneration."""
    if not staged_files:
        # If no files specified or running on full repo, trigger
        return True

    for f in staged_files:
        if "scripts/generate_schema_documentation.py" in f:
            return True
        if not f.endswith(".py") and not f.endswith(".cs"):
            continue
        parts = f.split(os.sep)
        filename = parts[-1]
        if (
            "models.py" in filename
            or "database.py" in filename
            or "Entities.cs" in filename
            or "DbContext.cs" in filename
            or "infrastructure" in parts
            or "Infrastructure" in parts
            or "domain" in parts
            or "Domain" in parts
            or "adapters" in parts
            or "alembic" in parts
            or "Migrations" in parts
        ):
            return True
    return False


def main() -> int:
    """Run schema documentation generator and stage generated artifacts."""
    staged_files = get_staged_files()
    if staged_files and not should_trigger_schema_doc(staged_files):
        print(
            "No database models or schema definitions modified. Bypassing schema doc compilation."
        )
        return 0

    print(
        "Database models or schema generator changes detected. Generating schema documentation..."
    )

    if os.name == "nt":
        python_exec = str(REPO_ROOT / ".venv" / "Scripts" / "python.exe")
    else:
        python_exec = str(REPO_ROOT / ".venv" / "bin" / "python")

    if not os.path.exists(python_exec):
        python_exec = sys.executable

    generator_script = str(REPO_ROOT / "scripts" / "generate_schema_documentation.py")

    try:
        res = subprocess.run(
            [python_exec, generator_script],
            capture_output=True,
            text=True,
            cwd=str(REPO_ROOT),
        )
        if res.returncode != 0:
            print("[ERROR] Schema documentation generation failed:", file=sys.stderr)
            print(res.stdout, file=sys.stderr)
            print(res.stderr, file=sys.stderr)
            return res.returncode

        print(res.stdout)
    except subprocess.SubprocessError as e:
        print(
            f"[ERROR] Failed to run schema documentation generator: {e}",
            file=sys.stderr,
        )
        return 1

    # Stage generated artifacts
    artifacts = [
        str(REPO_ROOT / "docs" / "schema_visualizer.html"),
        str(REPO_ROOT / "docs" / "schema"),
        str(REPO_ROOT / "docs" / "CDISC"),
    ]

    try:
        subprocess.run(
            ["git", "add"] + artifacts,
            cwd=str(REPO_ROOT),
            check=True,
        )
        print(
            "Schema documentation visualizers and CDISC USDM catalog successfully staged!"
        )
    except subprocess.SubprocessError as e:
        print(f"Warning: Failed to stage generated artifacts: {e}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
