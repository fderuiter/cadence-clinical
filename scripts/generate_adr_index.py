#!/usr/bin/env python3
import os
import re

ADR_DIR = "docs/adr"
INDEX_FILE = os.path.join(ADR_DIR, "index.md")


def generate_index():
    if not os.path.isdir(ADR_DIR):
        print(f"Directory {ADR_DIR} not found.")
        return

    adr_files = []
    for filename in os.listdir(ADR_DIR):
        if not filename.endswith(".md"):
            continue
        if filename in ("TEMPLATE.md", "index.md"):
            continue

        filepath = os.path.join(ADR_DIR, filename)

        # Extract date from filename, e.g. "2023-01-01"
        date_match = re.match(r"^(\d{4}-\d{2}-\d{2})", filename)
        if not date_match:
            continue
        date_str = date_match.group(1)

        try:
            with open(filepath, "r", encoding="utf-8") as f:
                first_line = f.readline().strip()
        except Exception as e:
            print(f"Error reading {filepath}: {e}")
            continue

        # Clean up title
        # Strip leading "# ", "# ADR YYYY-MM-DD: ", etc.
        title = first_line.lstrip("#").strip()
        title = re.sub(r"^ADR-(?:\d+|\[NUMBER\]):\s*", "", title, flags=re.IGNORECASE)
        title = re.sub(r"^ADR\s+\d{4}-\d{2}-\d{2}:\s*", "", title, flags=re.IGNORECASE)
        title = re.sub(r"^\d{4}-\d{2}-\d{2}:\s*", "", title, flags=re.IGNORECASE)
        title = re.sub(r"^\d{4}-\d{2}-\d{2}\s+", "", title, flags=re.IGNORECASE)
        title = title.strip()

        adr_files.append({"date": date_str, "filename": filename, "title": title})

    # Sort chronologically by date and filename
    adr_files.sort(key=lambda x: (x["date"], x["filename"]))

    # Generate index content
    lines = [
        "# Architectural Decision Records (ADRs) Index Log",
        "",
        "This document tracks all Architectural Decision Records for the Cadence Clinical platform in chronological order.",
        "",
        "## Standard ADR Template",
        "- [ADR Template](TEMPLATE.md)",
        "",
        "## Decisions Log",
        "",
    ]

    for adr in adr_files:
        lines.append(f"- [{adr['date']}: {adr['title']}]({adr['filename']})")

    lines.append("")  # trailing newline

    content = "\n".join(lines)

    try:
        with open(INDEX_FILE, "w", encoding="utf-8") as f:
            f.write(content)
        print(
            f"Successfully regenerated {INDEX_FILE} with {len(adr_files)} ADR entries."
        )
    except Exception as e:
        print(f"Error writing {INDEX_FILE}: {e}")


if __name__ == "__main__":
    generate_index()
