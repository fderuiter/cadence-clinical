"""Unit tests for scripts/validate_dependencies.py.

Requirements: PRD-SYS-001
"""

import json

from scripts.validate_dependencies import check_package_json


def test_validate_dependencies_passes_on_clean_package_json(tmp_path):
    """Verify check_package_json passes when no forbidden packages are present."""
    pkg_json_path = tmp_path / "package.json"
    content = {
        "name": "clean-package",
        "dependencies": {"vue": "^3.0.0", "pinia": "^2.0.0"},
        "devDependencies": {"vite": "^2.0.0"},
    }
    with open(pkg_json_path, "w", encoding="utf-8") as f:
        json.dump(content, f)

    violations = check_package_json(pkg_json_path)
    assert not violations


def test_validate_dependencies_fails_on_forbidden_package(tmp_path):
    """Verify check_package_json fails when a forbidden asymmetric package is present."""
    pkg_json_path = tmp_path / "package.json"
    content = {
        "name": "dirty-package",
        "dependencies": {"node-forge": "^1.3.0", "vue": "^3.0.0"},
        "peerDependencies": {"elliptic": "^6.5.4"},
    }
    with open(pkg_json_path, "w", encoding="utf-8") as f:
        json.dump(content, f)

    violations = check_package_json(pkg_json_path)
    assert len(violations) == 2
    assert any("node-forge" in v for v in violations)
    assert any("elliptic" in v for v in violations)
