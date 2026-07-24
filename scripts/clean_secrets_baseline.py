#!/usr/bin/env python3
import json
import os
import sys


def clean_baseline(filepath):
    if not os.path.exists(filepath):
        print(f"File not found: {filepath}")
        return

    try:
        with open(filepath, "r", encoding="utf-8") as f:
            data = json.load(f)

        # Remove generated_at timestamp to prevent merge friction
        if "generated_at" in data:
            del data["generated_at"]

        # Remove line_number from all detected secret results
        results = data.get("results", {})
        for file_results in results.values():
            for result in file_results:
                if "line_number" in result:
                    del result["line_number"]

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
