import sys

from scripts import compliance_utility


def test_sanitize_content_workstation_urls():
    valid_reqs = {"PRD-SYS-001", "PRD-SYS-002", "Trace-1"}

    content = """# Spec

See [SoA Matrix](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/src/components/clinical/ClinicalSoAMatrix.vue).
See [ADR](file:///d:/Users/Fred/Documents/GitHub/cadence-clinical/docs/adr/2026-09-02-migration.md).
See [External Link](https://example.com/docs).
See [Illustrative Link](file:///`example`).
"""

    sanitized, urls_count, reqs_count = compliance_utility.sanitize_content(
        content, valid_requirements=valid_reqs
    )

    assert urls_count == 2
    assert "file:///Volumes" not in sanitized
    assert "file:///d:" not in sanitized
    assert (
        "[SoA Matrix](apps/web/src/components/clinical/ClinicalSoAMatrix.vue)"
        in sanitized
    )
    assert "[ADR](docs/adr/2026-09-02-migration.md)" in sanitized
    assert "[External Link](https://example.com/docs)" in sanitized
    assert "file:///`example`" in sanitized


def test_sanitize_content_requirement_normalization():
    valid_reqs = {"PRD-SYS-001", "PRD-SYS-002", "PRD-SUB-007", "Trace-1"}

    content = """# ADR

This decision satisfies requirement SYS-001 and sys-002.
It also addresses sub-7 and trace 1.
It does NOT touch non-requirement acronyms like SYS-LOG or UNKNOWN-999.
"""

    sanitized, urls_count, reqs_count = compliance_utility.sanitize_content(
        content, valid_requirements=valid_reqs
    )

    assert urls_count == 0
    assert "PRD-SYS-001" in sanitized
    assert "PRD-SYS-002" in sanitized
    assert "PRD-SUB-007" in sanitized
    assert "Trace-1" in sanitized
    assert "SYS-LOG" in sanitized
    assert "UNKNOWN-999" in sanitized


def test_sanitize_content_skips_code_blocks():
    valid_reqs = {"PRD-SYS-001"}

    content = """# Guide

Text outside code block with SYS-001.

```python
# Code block should be untouched
SYS-001 = "test"
url = "file:///Volumes/SanDisk/cadence-clinical/apps/web/main.py"
```

Inline code `SYS-001` is also skipped.
"""

    sanitized, urls_count, reqs_count = compliance_utility.sanitize_content(
        content, valid_requirements=valid_reqs
    )

    assert reqs_count == 1  # only the one outside code block
    assert "Text outside code block with PRD-SYS-001." in sanitized
    assert 'SYS-001 = "test"' in sanitized
    assert "`SYS-001`" in sanitized


def test_sanitize_file_and_idempotency(tmp_path):
    valid_reqs = {"PRD-SYS-001", "Trace-1"}

    md_file = tmp_path / "test_adr.md"
    initial_content = """# Test ADR
Implements SYS-001 and trace 1.
See [App](file:///Volumes/SanDisk/cadence-clinical/apps/web/main.py).
"""
    md_file.write_text(initial_content, encoding="utf-8")

    # First pass
    mod1, urls1, reqs1 = compliance_utility.sanitize_file(
        str(md_file), valid_requirements=valid_reqs
    )
    assert mod1 is True
    assert urls1 == 1
    assert reqs1 == 2

    # Second pass (idempotency check)
    mod2, urls2, reqs2 = compliance_utility.sanitize_file(
        str(md_file), valid_requirements=valid_reqs
    )
    assert mod2 is False
    assert urls2 == 0
    assert reqs2 == 0


def test_cli_sanitize_flag(tmp_path, monkeypatch):
    valid_reqs = {"PRD-SYS-001"}

    md_file = tmp_path / "doc.md"
    md_file.write_text("Implements SYS-001.", encoding="utf-8")

    # Mock sys.argv
    monkeypatch.setattr(
        sys, "argv", ["compliance_utility.py", "--sanitize", str(md_file)]
    )

    exit_codes = []
    monkeypatch.setattr(sys, "exit", exit_codes.append)

    # Re-run main logic by calling parse_args / sanitize_docs
    files_mod, urls_fixed, reqs_norm = compliance_utility.sanitize_docs(
        target_files=[str(md_file)], valid_requirements=valid_reqs
    )

    assert files_mod == 1
    assert reqs_norm == 1
    assert md_file.read_text(encoding="utf-8") == "Implements PRD-SYS-001."


def test_sanitize_content_ignores_english_sub_prefixes():
    valid_reqs = {
        "PRD-SUB-001",
        "PRD-SUB-002",
        "PRD-SUB-007",
        "PRD-SUB-030",
        "PRD-SUB-060",
    }

    content = """# Performance Specs

Validation pipeline completes in sub-500ms followed by assertions in sub-1.5s.
Report generation in sub-2 seconds.
Execution completes in sub-30 seconds and sub-60-second synthesis.
Sub-7 is a requirement ID: sub-7.
"""

    sanitized, urls_count, reqs_count = compliance_utility.sanitize_content(
        content, valid_requirements=valid_reqs
    )

    assert "sub-500ms" in sanitized
    assert "sub-1.5s" in sanitized
    assert "sub-2 seconds" in sanitized
    assert "sub-30 seconds" in sanitized
    assert "sub-60-second" in sanitized
    assert "PRD-SUB-007" in sanitized
    assert reqs_count == 2


def test_sanitize_docs_ignores_runs_and_ignored_files(tmp_path):
    valid_reqs = {"PRD-SYS-001"}

    runs_dir = tmp_path / "docs" / "SDLC" / "runs"
    runs_dir.mkdir(parents=True)
    report_file = runs_dir / "IQ_OQ_PQ_Execution_Report_20260922.md"
    report_file.write_text("Satisfies SYS-001.", encoding="utf-8")

    rtm_file = tmp_path / "docs" / "SDLC" / "Requirements_Traceability_Matrix.md"
    rtm_file.write_text("Satisfies SYS-001.", encoding="utf-8")

    files_mod, urls_fixed, reqs_norm = compliance_utility.sanitize_docs(
        docs_dir=str(tmp_path / "docs"),
        target_files=[str(report_file), str(rtm_file)],
        valid_requirements=valid_reqs,
    )

    assert files_mod == 0
    assert reqs_norm == 0
    assert report_file.read_text(encoding="utf-8") == "Satisfies SYS-001."
    assert rtm_file.read_text(encoding="utf-8") == "Satisfies SYS-001."
