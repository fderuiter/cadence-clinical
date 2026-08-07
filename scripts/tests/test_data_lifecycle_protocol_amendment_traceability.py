import re
from pathlib import Path


def test_data_lifecycle_protocol_amendment_traceability() -> None:
    """
    Validation Test: Assert that the newly added 'Protocol Amendment Lifecycle' section is present
    in `docs/DATA_LIFECYCLE.md`, contains only valid requirements and TDD citations, and strictly
    excludes the non-existent 'PRD-UNI-003'.
    """
    lifecycle_path = Path("docs/DATA_LIFECYCLE.md")
    assert lifecycle_path.exists(), "docs/DATA_LIFECYCLE.md must exist"

    content = lifecycle_path.read_text(encoding="utf-8")

    # Verify the header exists
    assert "# Data Lifecycle Specification: Protocol Amendment Lifecycle" in content, (
        "The new H1 section header must be present in docs/DATA_LIFECYCLE.md"
    )

    # Enforce strictly no citation of PRD-UNI-003
    assert "PRD-UNI-003" not in content, (
        "The non-existent requirement PRD-UNI-003 must not be cited in DATA_LIFECYCLE.md"
    )

    # Validate that only authorized PRD/TDD trace IDs are present in the Protocol Amendment section
    # Let's extract the Protocol Amendment section text
    sections = content.split(
        "# Data Lifecycle Specification: Protocol Amendment Lifecycle"
    )
    assert len(sections) >= 2, (
        "Expected at least one Protocol Amendment Lifecycle section split"
    )

    amendment_section = sections[1]

    # Scope to the Protocol Amendment section only — stop at the next top-level
    # H1 section (introduced by PR #812 which added the eSignature section).
    # This prevents PRD IDs from subsequent sections bleeding into the validation.
    next_section_markers = [
        "\n# Data Lifecycle Specification:",
        "\n---\n\n# Data Lifecycle Specification:",
    ]
    for marker in next_section_markers:
        if marker in amendment_section:
            amendment_section = amendment_section.split(marker)[0]
            break

    # Use regex to find all PRD- style requirements
    prd_matches = re.findall(r"PRD-[A-Z0-9\-]+", amendment_section)

    valid_prd_ids = {"PRD-SYS-001", "PRD-MDR-002", "PRD-SUB-007"}

    for match in prd_matches:
        assert match in valid_prd_ids, (
            f"Found invalid or unauthorized requirements citation in amendment lifecycle: '{match}'"
        )

    # Confirm correct citations are explicitly present in the section
    assert "PRD-SYS-001" in prd_matches, "PRD-SYS-001 must be cited"
    assert "PRD-MDR-002" in prd_matches, "PRD-MDR-002 must be cited"
    assert "PRD-SUB-007" in prd_matches, "PRD-SUB-007 must be cited"

    # Confirm valid design and QA trace IDs are present
    assert "TDD §3.4/§3.5" in amendment_section, "TDD §3.4/§3.5 must be cited"
    assert "QA §5.1 TC-VAL-LOG-001" in amendment_section, (
        "QA §5.1 TC-VAL-LOG-001 must be cited"
    )
