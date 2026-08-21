"""Unit and contract tests for TerminalDocument and CLI formatting utilities.

@req:PRD-SYS-049
"""

import json

from packages.cli.formatting import (
    TerminalDocument,
    format_file_link,
)


def test_format_file_link():
    """Verify clickable file:// link helper generates standard markdown and terminal format.

    @req:PRD-SYS-049
    """
    link = format_file_link("/path/to/file.py", line=42)
    assert link == "[file.py:L42](file:///path/to/file.py#L42)"

    simple_link = format_file_link("/path/to/model.py")
    assert simple_link == "[model.py](file:///path/to/model.py)"


def test_terminal_document_builder_structured_dict():
    """Verify TerminalDocument builds structured dictionary representation for JSON/agent consumption.

    @req:PRD-SYS-049
    """
    doc = TerminalDocument(
        title="Cadence System Diagnostics",
        subtitle="Environment and service connectivity report",
    )
    doc.add_metric("Passed", 8, style="green")
    doc.add_metric("Failed", 0, style="red")
    doc.add_metric("Duration", "0.45s", style="cyan")

    doc.add_key_value("Python Runtime", "3.14.7")
    doc.add_key_value("Repository Root", "/Code/cadence-clinical")

    doc.add_item("Neo4j Graph Database", status="pass", detail="bolt://localhost:7687")
    doc.add_item("PostgreSQL Relational DB", status="pass", detail="port 5432 open")
    doc.add_item("SQLite Schemas", status="fail", detail="econsent.db missing")

    doc.set_cta(
        "Run 'uv run cadence doctor --auto-fix' to initialize missing SQLite schemas"
    )

    payload = doc.to_dict()
    assert payload["title"] == "Cadence System Diagnostics"
    assert payload["subtitle"] == "Environment and service connectivity report"
    assert payload["metrics"] == [
        {"label": "Passed", "value": 8, "style": "green"},
        {"label": "Failed", "value": 0, "style": "red"},
        {"label": "Duration", "value": "0.45s", "style": "cyan"},
    ]
    assert payload["key_values"]["Python Runtime"] == "3.14.7"
    assert len(payload["items"]) == 3
    assert payload["items"][2]["status"] == "fail"
    assert "auto-fix" in payload["cta"]

    json_str = doc.to_json()
    parsed = json.loads(json_str)
    assert parsed == payload


def test_terminal_document_render_output():
    """Verify TerminalDocument renders styled Rich layout containing title, metrics, items, and CTA.

    @req:PRD-SYS-049
    """
    doc = TerminalDocument(
        title="Architecture Sentinels",
        subtitle="10 quality gates evaluated",
    )
    doc.add_metric("Gates Passed", 10, style="green")
    doc.add_metric("Gates Failed", 0, style="red")
    doc.add_item("ruff-lint", status="pass", detail="0 violations")
    doc.add_item("import-boundaries", status="pass", detail="0 cross-app violations")
    doc.set_cta("Run 'uv run cadence test --fast' for sub-second test feedback")

    rendered = doc.render_text()
    assert "Architecture Sentinels" in rendered
    assert "10 quality gates evaluated" in rendered
    assert "Gates Passed" in rendered
    assert "ruff-lint" in rendered
    assert "Next Action:" in rendered
