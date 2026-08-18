"""Unit and integration tests for native directory sweeping documentation pipeline.

@req:PRD-SYS-001
"""

import subprocess
import sys
from pathlib import Path

import pytest

import scripts.compliance_utility as compliance_utility
from scripts.generate_rtm import (
    check_fragment_relative_links,
    check_orphan_fragments,
    parse_prd,
    parse_srs,
)

REPO_ROOT = Path(__file__).resolve().parents[2]


def test_sweep_and_aggregate_modular_fragments(tmp_path):
    """Verify SRS and PRD parsers recursively sweep subdirectories and aggregate requirements."""
    sdlc_dir = tmp_path / "SDLC"
    fragments_dir = sdlc_dir / "fragments"
    fragments_dir.mkdir(parents=True)

    # Fragment 1: numerical order 01_auth.md
    frag1 = fragments_dir / "01_auth.md"
    frag1.write_text(
        "#### PRD-AUTH-001: User Identity Authentication\nSystem must authenticate users.\n\n"
        "- **Trace 101: Keycloak Session Validation:** Token validation logic.\n",
        encoding="utf-8",
    )

    # Fragment 2: numerical order 02_data.md
    frag2 = fragments_dir / "02_data.md"
    frag2.write_text(
        "#### PRD-DATA-002: Audit Trail Immutability\nAll changes must be logged.\n",
        encoding="utf-8",
    )

    prd_reqs = parse_prd(str(sdlc_dir))
    srs_reqs = parse_srs(str(sdlc_dir))

    assert "PRD-AUTH-001" in prd_reqs
    assert "PRD-DATA-002" in prd_reqs
    assert "Trace-101" in srs_reqs

    assert prd_reqs["PRD-AUTH-001"]["title"] == "User Identity Authentication"
    assert prd_reqs["PRD-DATA-002"]["title"] == "Audit Trail Immutability"


def test_compliance_utility_directory_sweeping(tmp_path):
    """Verify get_valid_requirements sweeps subdirectories without single-file path configurations."""
    sdlc_sub = tmp_path / "SDLC" / "subfolder"
    sdlc_sub.mkdir(parents=True)

    frag = sdlc_sub / "modular_spec.md"
    frag.write_text(
        "#### PRD-MOD-003: Modular Documentation Support\n\n- **Trace 202: Dynamic Sweeper:** Sweeps subfolders.\n",
        encoding="utf-8",
    )

    reqs = compliance_utility.get_valid_requirements(str(tmp_path))

    assert "PRD-MOD-003" in reqs
    assert "Trace-202" in reqs


def test_duplicate_requirement_id_in_fragments_fails(tmp_path):
    """Verify duplicate requirement IDs across swept fragment subdirectories trigger exit(1)."""
    frag_dir = tmp_path / "SDLC" / "fragments"
    frag_dir.mkdir(parents=True)

    f1 = frag_dir / "01_first.md"
    f1.write_text("#### PRD-SYS-001: First Definition\n", encoding="utf-8")

    f2 = frag_dir / "02_second.md"
    f2.write_text("#### PRD-SYS-001: Duplicate Definition\n", encoding="utf-8")

    with pytest.raises(SystemExit) as exc_info:
        parse_prd(str(tmp_path / "SDLC"))

    assert exc_info.value.code == 1


def test_malformed_trace_id_definition_fails(tmp_path):
    """Verify malformed trace ID definition lines trigger diagnostic errors and exit(1)."""
    frag_dir = tmp_path / "SDLC" / "fragments"
    frag_dir.mkdir(parents=True)

    f1 = frag_dir / "invalid_spec.md"
    f1.write_text("#### PRD-SYS-: Malformed PRD ID Missing Digits\n", encoding="utf-8")

    with pytest.raises(SystemExit) as exc_info:
        parse_prd(str(tmp_path / "SDLC"))

    assert exc_info.value.code == 1


def test_orphan_fragment_detection(tmp_path):
    """Verify orphan fragments (no requirement IDs and no incoming links) trigger exit(1)."""
    sdlc_dir = tmp_path / "SDLC"
    sub_dir = sdlc_dir / "subfolder"
    sub_dir.mkdir(parents=True)

    # Valid parent document
    parent = sdlc_dir / "01_Main.md"
    parent.write_text("#### PRD-SYS-001: Main Requirement\n", encoding="utf-8")

    # Unreferenced, empty-requirement fragment in subfolder
    orphan = sub_dir / "unlinked_fragment.md"
    orphan.write_text(
        "# Unlinked Document\nThis file defines no requirement and has no links.\n",
        encoding="utf-8",
    )

    all_reqs = parse_prd(str(sdlc_dir))

    with pytest.raises(SystemExit) as exc_info:
        check_orphan_fragments(str(sdlc_dir), all_reqs)

    assert exc_info.value.code == 1


def test_broken_fragment_relative_link_detection(tmp_path):
    """Verify broken relative links within fragment subdirectories trigger exit(1)."""
    sdlc_dir = tmp_path / "SDLC"
    sub_dir = sdlc_dir / "subfolder"
    sub_dir.mkdir(parents=True)

    frag = sub_dir / "fragment.md"
    frag.write_text(
        "#### PRD-SYS-001: Sample Req\nSee [nonexistent target](../nonexistent_file.md) for details.\n",
        encoding="utf-8",
    )

    with pytest.raises(SystemExit) as exc_info:
        check_fragment_relative_links(str(sdlc_dir))

    assert exc_info.value.code == 1


def test_full_generate_rtm_cli_sweeping(tmp_path):
    """Verify generate_rtm.py CLI executes successfully with modular subdirectory fragments."""
    docs_dir = tmp_path / "docs"
    sdlc_dir = docs_dir / "SDLC"
    fragments_dir = sdlc_dir / "fragments"
    fragments_dir.mkdir(parents=True)

    # Main index doc linking fragment
    index_doc = sdlc_dir / "01_Product_Requirements_Document_PRD.md"
    index_doc.write_text(
        "#### PRD-SYS-001: Core System Baseline\nSee [Auth Fragment](fragments/01_auth.md).\n",
        encoding="utf-8",
    )

    # Modular fragment file
    auth_frag = fragments_dir / "01_auth.md"
    auth_frag.write_text(
        "#### PRD-AUTH-002: Multi-Factor Authentication\n- **Trace 301: MFA Gate:** MFA requirement.\n",
        encoding="utf-8",
    )

    out_dir = tmp_path / "out"
    cmd = [
        sys.executable,
        "scripts/generate_rtm.py",
        "--output-dir",
        str(out_dir),
        "--draft",
    ]

    # Run command in repo root
    res = subprocess.run(cmd, capture_output=True, text=True, cwd=str(REPO_ROOT))
    assert res.returncode == 0, f"Command failed: {res.stderr}"

    rtm_file = out_dir / "Requirements_Traceability_Matrix.md"
    assert rtm_file.is_file()
    rtm_content = rtm_file.read_text(encoding="utf-8")

    assert "PRD-SYS-001" in rtm_content
