import os
import re

DEFAULT_PRD_PATH = "docs/SDLC/01_Product_Requirements_Document_PRD.md"
DEFAULT_SRS_PATH = "docs/SRS.md"


def get_valid_requirements(
    prd_path: str = DEFAULT_PRD_PATH, srs_path: str = DEFAULT_SRS_PATH
) -> set[str]:
    """
    Parses active requirements from product and system design files to create
    a single master list of valid requirement identifiers.
    """
    valid_reqs = set()

    # Resolve paths relative to repo root if they don't exist as absolute/current-dir paths
    # (just to make it extremely robust under different execution contexts)
    resolved_prd = (
        prd_path
        if os.path.exists(prd_path)
        else os.path.join(os.path.dirname(os.path.dirname(__file__)), prd_path)
    )
    resolved_srs = (
        srs_path
        if os.path.exists(srs_path)
        else os.path.join(os.path.dirname(os.path.dirname(__file__)), srs_path)
    )

    # 1. Parse PRD: #### PRD-[CATEGORY]-[NUMBER]: [TITLE]
    if os.path.exists(resolved_prd):
        with open(resolved_prd, "r", encoding="utf-8") as f:
            content = f.read()
        prd_pattern = re.compile(r"####\s*(PRD-[A-Z]+-\d+)\s*:", re.I)
        for line in content.splitlines():
            match = prd_pattern.search(line)
            if match:
                valid_reqs.add(match.group(1).upper().strip())

    # 2. Parse SRS: * **Trace [NUMBER]: [TITLE]:** [DESCRIPTION]
    if os.path.exists(resolved_srs):
        with open(resolved_srs, "r", encoding="utf-8") as f:
            content = f.read()
        # Look for trace numbers
        srs_pattern = re.compile(r"\bTrace\s*(\d+)\b", re.I)
        for line in content.splitlines():
            # Only count as requirement if it is a trace list item or explicitly matches trace definition
            if "**Trace" in line or "Trace:" in line or "Trace-" in line:
                for match in srs_pattern.finditer(line):
                    valid_reqs.add(f"Trace-{match.group(1)}")

    return valid_reqs


def extract_requirement_references(content: str) -> list[str]:
    """
    Extracts potential requirement references from content using robust regex pattern
    matching PRD-[A-Z]+-\\d+, Trace-\\d+, and Trace\\s+\\d+ (case-insensitive),
    and normalizes them (e.g. 'Trace 1' -> 'Trace-1', 'prd-sys-001' -> 'PRD-SYS-001').
    """
    # Pattern explanation:
    # - PRD-[A-Za-z]+-\d+ : Matches PRD-SYS-001, etc.
    # - Trace(?:-|\s*)\d+ : Matches Trace-1, Trace 1, Trace1, etc.
    pattern = re.compile(r"\b(PRD-[A-Z]+-\d+|Trace(?:-|\s*)\d+)\b", re.I)
    matches = pattern.findall(content)

    normalized = []
    for m in matches:
        if m.upper().startswith("PRD"):
            normalized.append(m.upper().strip())
        elif m.upper().startswith("TRACE"):
            # Extract digits
            num_match = re.search(r"\d+", m)
            if num_match:
                normalized.append(f"Trace-{num_match.group(0)}")

    # Deduplicate while preserving order
    seen = set()
    deduped = []
    for item in normalized:
        if item not in seen:
            seen.add(item)
            deduped.append(item)
    return deduped


def is_post_2026_adr(filename: str) -> bool:
    """
    Checks if an ADR filename belongs to year 2026 or later.
    Expected pattern YYYY-MM-DD-...
    """
    match = re.match(r"^(\d{4})-\d{2}-\d{2}", filename)
    if match:
        year = int(match.group(1))
        return year >= 2026
    return False


def validate_adr_compliance(
    filename: str, content: str, valid_reqs: set[str]
) -> tuple[bool, str]:
    """
    Validates a single ADR for compliance:
    - Pre-2026 legacy ADRs bypass requirement-linkage validation.
    - Post-2026 modern ADRs must reference at least one valid requirement identifier,
      and must not reference any invalid/misspelled identifiers.

    Returns (success, error_message).
    """
    # Legacy bypass
    if not is_post_2026_adr(filename):
        return True, ""

    refs = extract_requirement_references(content)

    if not refs:
        return (
            False,
            f"Error: Modern architectural decision '{filename}' lacks a valid requirement reference.",
        )

    invalid_refs = [r for r in refs if r not in valid_reqs]
    if invalid_refs:
        invalid_str = ", ".join(invalid_refs)
        return (
            False,
            f"Error: Modern architectural decision '{filename}' references invalid or misspelled requirement identifier(s): {invalid_str}.",
        )

    return True, ""
