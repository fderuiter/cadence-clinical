#!/usr/bin/env python3
"""
CLI Developer Helper for Creating & Indexing Architectural Decision Records (ADRs).
Enforces consistent domain categorization, requirement linking, and index updates.
Supports both CLI flags and interactive prompts for seamless developer experience.
"""

import argparse
import datetime
import os
import re
import sys

# Directory constants
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
ADR_DIR = os.path.join(REPO_ROOT, "docs", "adr")
INDEX_FILE = os.path.join(ADR_DIR, "index.md")

DOMAIN_CHOICES = [
    ("core-platform", "1. Core Platform & Execution Engine"),
    ("gateway-security", "2. API Gateway, Security & Identity"),
    ("data-standards", "3. Clinical Data Interoperability & Standards"),
    ("clinical-ops", "4. Clinical Operations & Business Modules"),
    ("compliance-audit", "5. Compliance, Audit & Governance"),
    ("frontend-ui", "6. Frontend & Design System"),
    ("devops-ci", "7. DevOps, Tooling & CI/CD"),
]

DOMAIN_MAP = {key: f"### {title}" for key, title in DOMAIN_CHOICES}


def slugify(text: str) -> str:
    """Converts a title string into a clean filename slug."""
    text = text.lower().strip()
    text = re.sub(r"[^\w\s-]", "", text)
    text = re.sub(r"[\s_-]+", "-", text)
    return text.strip("-")


def get_next_adr_number() -> int:
    """Calculates the next incremental ADR number by parsing existing ADR titles."""
    max_num = 0
    if not os.path.exists(ADR_DIR):
        return 1

    for filename in os.listdir(ADR_DIR):
        if not filename.endswith(".md") or filename in ("TEMPLATE.md", "index.md"):
            continue
        filepath = os.path.join(ADR_DIR, filename)
        try:
            with open(filepath, "r", encoding="utf-8") as f:
                first_line = f.readline().strip()
                match = re.search(r"ADR-(\d+):", first_line)
                if match:
                    max_num = max(max_num, int(match.group(1)))
        except Exception:
            pass

    return max_num + 1 if max_num > 0 else 100


def create_adr(title: str, domain_key: str, requirement_id: str) -> str:
    today_str = datetime.date.today().strftime("%Y-%m-%d")
    slug = slugify(title)
    filename = f"{today_str}-{slug}.md"
    filepath = os.path.join(ADR_DIR, filename)

    if os.path.exists(filepath):
        print(f"Error: ADR file already exists: {filepath}")
        sys.exit(1)

    adr_num = get_next_adr_number()

    adr_template = f"""# ADR-{adr_num}: {title}

* **Status:** Accepted
* **Date:** {today_str}
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Describe the architectural context and problem statement. Reference the applicable system requirement ({requirement_id}).

## 2. Decision Drivers & Constraints

* Technical constraint 1
* Business/GxP requirement ({requirement_id})

## 3. Options Considered

1. Option A (Selected)
2. Option B (Alternative)

## 4. Decision Outcome

Chosen option: Option A because it satisfies {requirement_id} while ensuring system maintainability.

## 5. Consequences & Trade-offs

* Positive: Clear operational boundaries and compliance tracing.
* Negative: Additional abstraction layer required.

## 6. Implementation & Verification

* Target files/packages modified.
* Verification tests added under `tests/`.
"""

    with open(filepath, "w", encoding="utf-8") as f:
        f.write(adr_template)

    print(f"Successfully created ADR file: {filepath}")

    # Index into docs/adr/index.md under specified domain
    if os.path.exists(INDEX_FILE):
        with open(INDEX_FILE, "r", encoding="utf-8") as f:
            index_content = f.read()

        domain_header = DOMAIN_MAP[domain_key]
        entry = f"- [{today_str}: {title}]({filename})"

        if entry in index_content or filename in index_content:
            print(f"Note: {filename} already indexed in {INDEX_FILE}.")
        elif domain_header in index_content:
            index_content = index_content.replace(
                domain_header, f"{domain_header}\n{entry}"
            )
            with open(INDEX_FILE, "w", encoding="utf-8") as f:
                f.write(index_content)
            print(f"Successfully auto-indexed {filename} under '{domain_header}'.")
        else:
            with open(INDEX_FILE, "a", encoding="utf-8") as f:
                f.write(f"\n{entry}\n")
            print(f"Auto-indexed {filename} at bottom of {INDEX_FILE}.")

    return filepath


def prompt_interactive() -> tuple[str, str, str]:
    """Interactive wizard when CLI arguments are not provided."""
    print("=====================================================")
    print(" Cadence Clinical - ADR Creation Wizard")
    print("=====================================================")

    try:
        title = input(
            "\nEnter ADR Title (e.g. 'Audit Log Hash Chain Verification'): "
        ).strip()
        while not title:
            title = input("Title cannot be empty. Please enter title: ").strip()

        print("\nSelect Functional Domain:")
        for idx, (key, label) in enumerate(DOMAIN_CHOICES, start=1):
            print(f"  [{idx}] {label} ({key})")

        choice = input("\nSelect Domain [1-7] (default 1): ").strip() or "1"
        try:
            choice_idx = int(choice) - 1
            if choice_idx < 0 or choice_idx >= len(DOMAIN_CHOICES):
                choice_idx = 0
        except ValueError:
            choice_idx = 0

        domain_key = DOMAIN_CHOICES[choice_idx][0]

        req = (
            input("\nEnter Requirement ID (default 'PRD-SYS-001'): ").strip()
            or "PRD-SYS-001"
        )

        return title, domain_key, req
    except KeyboardInterrupt:
        print("\nOperation cancelled.")
        sys.exit(0)


def main():
    parser = argparse.ArgumentParser(
        description="CLI Developer Helper for Creating Architectural Decision Records (ADRs)."
    )
    parser.add_argument(
        "--title", required=False, help="Short, descriptive title of the decision."
    )
    parser.add_argument(
        "--domain",
        required=False,
        choices=list(DOMAIN_MAP.keys()),
        help="Functional domain category for the ADR.",
    )
    parser.add_argument(
        "--req",
        default="PRD-SYS-001",
        help="Requirement ID traced to this decision (e.g. PRD-SYS-001 or Trace-1).",
    )

    args = parser.parse_args()

    if args.title and args.domain:
        create_adr(args.title, args.domain, args.req)
    else:
        # Run interactive wizard if flags were not specified
        title, domain, req = prompt_interactive()
        create_adr(title, domain, req)


if __name__ == "__main__":
    main()
