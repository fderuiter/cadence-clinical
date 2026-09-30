#!/usr/bin/env python3
"""
Unit tests for scripts/validate_adrs.py
"""

import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

# Add scripts directory to sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import validate_adrs


class TestValidateAdrsScript(unittest.TestCase):

    def test_repository_adrs_pass_validation(self):
        """Validates that the repository's own docs/adr directory passes validation."""
        success, errors = validate_adrs.validate_adrs(REPO_ROOT)
        self.assertTrue(success, f"Validation failed with errors: {errors}")
        self.assertEqual(len(errors), 0)

    def test_fix_command_runs_successfully(self):
        """Validates that running validate_adrs with --fix succeeds and master indices exist."""
        validate_adrs.sync_master_indices(REPO_ROOT)
        self.assertTrue((REPO_ROOT / "docs" / "adr" / "index.md").exists())
        self.assertTrue((REPO_ROOT / "docs" / "adr" / "README.md").exists())
        self.assertTrue((REPO_ROOT / "docs" / "ADR_INDEX.md").exists())

        success, errors = validate_adrs.validate_adrs(REPO_ROOT)
        self.assertTrue(success, f"Validation after sync failed: {errors}")

    def test_detects_invalid_filename(self):
        """Tests that a non-conforming filename is flagged."""
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_root = Path(temp_dir)
            docs_dir = temp_root / "docs"
            adr_dir = docs_dir / "adr"
            adr_dir.mkdir(parents=True)

            # Valid ADR
            (adr_dir / "2026-01-01-valid.md").write_text(
                "# ADR-100: Valid\n\n## Status\n## Context\n## Decision\n## Alternatives Considered\n## Trade-offs\n",
                encoding="utf-8"
            )
            # Invalid filename ADR
            (adr_dir / "invalid-filename.md").write_text(
                "# ADR-101: Invalid Filename\n\n## Status\n## Context\n## Decision\n## Alternatives Considered\n## Trade-offs\n",
                encoding="utf-8"
            )

            validate_adrs.sync_master_indices(temp_root)
            success, errors = validate_adrs.validate_adrs(temp_root)
            self.assertFalse(success)
            self.assertTrue(any("does not follow standard date pattern" in e for e in errors))

    def test_detects_duplicate_adr_number(self):
        """Tests that duplicate ADR numbers are detected."""
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_root = Path(temp_dir)
            docs_dir = temp_root / "docs"
            adr_dir = docs_dir / "adr"
            adr_dir.mkdir(parents=True)

            (adr_dir / "2026-01-01-first.md").write_text(
                "# ADR-100: First\n\n## Status\n## Context\n## Decision\n## Alternatives Considered\n## Trade-offs\n",
                encoding="utf-8"
            )
            (adr_dir / "2026-01-02-second.md").write_text(
                "# ADR-100: Second Duplicate\n\n## Status\n## Context\n## Decision\n## Alternatives Considered\n## Trade-offs\n",
                encoding="utf-8"
            )

            validate_adrs.sync_master_indices(temp_root)
            success, errors = validate_adrs.validate_adrs(temp_root)
            self.assertFalse(success)
            self.assertTrue(any("used by multiple files" in e for e in errors))


if __name__ == "__main__":
    unittest.main()
