import os
import re
import sys

ADR_DIR = "docs/adr"
INDEX_FILE = os.path.join(ADR_DIR, "index.md")
IGNORE_FILES = {"TEMPLATE.md", "index.md"}

# Regex patterns
FILENAME_PATTERN = re.compile(r"^\d{4}-\d{2}-\d{2}-.+\.md$")
DATE_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}")
TITLE_PATTERN = re.compile(r"^# .*\d{4}-\d{2}-\d{2}.*$")

REQUIRED_SECTIONS = [
    "## Status",
    "## Context",
    "## Decision",
    "## Alternatives Considered",
    "## Trade-offs",
]


def main():
    if not os.path.isdir(ADR_DIR):
        print(f"Error: Directory {ADR_DIR} not found.")
        sys.exit(1)

    try:
        with open(INDEX_FILE, "r") as f:
            index_content = f.read()
    except Exception as e:
        print(f"Error reading {INDEX_FILE}: {e}")
        sys.exit(1)

    all_passed = True

    # Check for ADRs outside the proper folder
    for root, _, files in os.walk("."):
        if ".git" in root or ".venv" in root or "node_modules" in root:
            continue
        for filename in files:
            if not filename.endswith(".md"):
                continue
            if FILENAME_PATTERN.match(filename):
                # Ensure it resides in docs/adr
                expected_dir = os.path.join(".", "docs", "adr")
                if os.path.abspath(root) != os.path.abspath(expected_dir):
                    print(
                        f"Error: ADR file '{filename}' found outside the proper directory ({root}). Must reside in {ADR_DIR}."
                    )
                    all_passed = False

    for filename in os.listdir(ADR_DIR):
        if not filename.endswith(".md"):
            continue
        if filename in IGNORE_FILES:
            continue

        filepath = os.path.join(ADR_DIR, filename)

        # 1. Check filename pattern
        if not FILENAME_PATTERN.match(filename):
            print(
                f"Error: File '{filename}' does not follow the standard chronological date pattern (YYYY-MM-DD-...)."
            )
            all_passed = False

        # 2. Check if file is in index
        if f"({filename})" not in index_content:
            print(
                f"Error: File '{filename}' is missing from the index log ({INDEX_FILE})."
            )
            all_passed = False

        # 3. Read file and check contents
        try:
            with open(filepath, "r") as f:
                content = f.read()
        except Exception as e:
            print(f"Error reading {filepath}: {e}")
            all_passed = False
            continue

        lines = content.split("\n")

        # Check title
        if not lines or not TITLE_PATTERN.match(lines[0]):
            print(
                f"Error: File '{filename}' title (first line) does not contain the correct date format."
            )
            all_passed = False

        # Check required sections
        missing_sections = []
        for section in REQUIRED_SECTIONS:
            if section not in content:
                missing_sections.append(section)

        if missing_sections:
            print(
                f"Error: File '{filename}' is missing required sections: {', '.join(missing_sections)}"
            )
            all_passed = False

    if not all_passed:
        print("ADR validation failed.")
        sys.exit(1)

    print("All ADRs passed validation.")


if __name__ == "__main__":
    main()
