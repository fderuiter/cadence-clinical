import json
import os
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
            "Please run: uv run python scripts/clean_secrets_baseline.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime

EXCLUDE_REGEX = re.compile(
    r"(pnpm-lock\.yaml|uv\.lock|tests/fixtures/keys/|docs/CDISC/|docs/NCI/)"
)


def clean_baseline(filepath):
    if not os.path.exists(filepath):
        print(f"File not found: {filepath}")
        return

    try:
        with open(filepath, encoding="utf-8") as f:
            data = json.load(f)

        # Remove generated_at timestamp to prevent merge friction
        if "generated_at" in data:
            del data["generated_at"]

        # Ensure exclude section is configured
        if "exclude" not in data or not data["exclude"]:
            data["exclude"] = {
                "files": r"(pnpm-lock\.yaml|uv\.lock|tests/fixtures/keys/|docs/CDISC/|docs/NCI/)",
                "lines": None,
            }

        # Remove line_number from all detected secret results and filter excluded files
        results = data.get("results", {})
        cleaned_results = {}
        for filename, file_results in results.items():
            if EXCLUDE_REGEX.search(filename):
                continue
            for result in file_results:
                if "line_number" in result:
                    del result["line_number"]
            cleaned_results[filename] = file_results
        data["results"] = cleaned_results

        with open(filepath, "w", encoding="utf-8") as f:
            # Sort keys to ensure deterministic ordering of properties
            json.dump(data, f, indent=2, sort_keys=True)
            f.write("\n")

        print(
            f"Successfully cleaned and made secrets baseline deterministic at {filepath}"
        )
    except Exception as e:
        print(f"Error cleaning secrets baseline: {e}")
        sys.exit(1)


if __name__ == "__main__":
    # Always operate on the absolute path to avoid cwd issues
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    baseline_path = os.path.join(base_dir, ".secrets.baseline")
    clean_baseline(baseline_path)
