import os
from pathlib import Path

import scripts.validate_markdown as vm


def test_gxp_compliance_drifts_identified():
    """
    GxP Compliance Verification Test:
    Ensures that the repository linter successfully scans the actual docs/SDLC directory
    and identifies the 7 critical architectural and electronic signature signature/schema drifts.
    @req:PRD-SYS-001
    """
    vm.errors.clear()

    # Run the linter across the entire repository
    repo_root = Path(__file__).resolve().parent.parent.parent
    codebase_map = vm.build_codebase_map(repo_root)

    # Dynamically build current root level directories and files
    root_entries = os.listdir(repo_root)
    root_dirs = {
        e
        for e in root_entries
        if (repo_root / e).is_dir() and (not e.startswith(".") or e == ".github")
    }
    root_files = {
        e for e in root_entries if (repo_root / e).is_file() and not e.startswith(".")
    }

    # Walk docs/SDLC and process all markdown files
    sdlc_dir = repo_root / "docs" / "SDLC"
    for p in sdlc_dir.rglob("*.md"):
        vm.process_markdown_file(p, repo_root, root_dirs, root_files, codebase_map)

    error_messages = [e["message"] for e in vm.errors]

    # 1. Verify drift in assert_graph_mutable is detected
    assert any(
        "assert_graph_mutable" in m and "mismatched" in m.lower()
        for m in error_messages
    ), "Failed to identify drift in assert_graph_mutable signature!"

    # 2. Verify drift in compute_graph_diff is detected
    assert any(
        "compute_graph_diff" in m and "mismatched" in m.lower() for m in error_messages
    ), "Failed to identify drift in compute_graph_diff signature!"

    # 3. Verify drift in execute_audit_sealing_cycle is detected
    assert any(
        "execute_audit_sealing_cycle" in m and "mismatched" in m.lower()
        for m in error_messages
    ), "Failed to identify drift in execute_audit_sealing_cycle signature!"

    # 4. Verify drift in SignatureManifestation is detected
    assert any(
        "SignatureManifestation" in m and "mismatch" in m.lower()
        for m in error_messages
    ), "Failed to identify drift in SignatureManifestation schema!"

    # 5. Verify drift in run_migrations is detected
    assert any(
        "run_migrations" in m and "mismatched" in m.lower() for m in error_messages
    ), "Failed to identify drift in run_migrations signature!"

    # 6. Verify missing provision_new_tenant is detected
    assert any(
        "provision_new_tenant" in m and "not found" in m.lower() for m in error_messages
    ), "Failed to identify missing provision_new_tenant implementation!"

    # 7. Verify missing rollback_schema_to_version is detected
    assert any(
        "rollback_schema_to_version" in m and "not found" in m.lower()
        for m in error_messages
    ), "Failed to identify missing rollback_schema_to_version implementation!"

    print(f"[+] Successfully identified {len(vm.errors)} documentation drifts!")
