#!/usr/bin/env python3
import json
import os
import sys

BASELINE_PATH = ".secrets.baseline"


def clean_baseline():
    if not os.path.exists(BASELINE_PATH):
        print(f"No {BASELINE_PATH} found.")
        return

    try:
        with open(BASELINE_PATH, "r", encoding="utf-8") as f:
            data = json.load(f)
    except Exception as e:
        print(f"Error reading {BASELINE_PATH}: {e}")
        sys.exit(1)

    # Reset all line numbers to 0 to prevent Git merge conflicts
    modified = False
    for filepath, file_results in data.get("results", {}).items():
        for result in file_results:
            if "line_number" in result and result["line_number"] != 0:
                result["line_number"] = 0
                modified = True

    if modified:
        try:
            with open(BASELINE_PATH, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
            print(f"Successfully cleaned line numbers in {BASELINE_PATH}")
        except Exception as e:
            print(f"Error writing {BASELINE_PATH}: {e}")
            sys.exit(1)
    else:
        print(f"No changes needed for {BASELINE_PATH}")


if __name__ == "__main__":
    clean_baseline()
