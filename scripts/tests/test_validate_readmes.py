import sys
import tempfile
from pathlib import Path

from scripts.scaffold_readmes import main as scaffold_main
from scripts.validate_readmes import (
    check_readme_file,
    find_target_directories,
)
from scripts.validate_readmes import (
    main as validate_main,
)


def test_validate_readmes_pass_on_current_repo():
    repo_root = Path(__file__).resolve().parent.parent.parent
    package_dirs, app_dirs = find_target_directories(repo_root)
    assert len(package_dirs) >= 17
    assert len(app_dirs) >= 18

    # Test that check_readme_file succeeds for all
    for module_dir in package_dirs + app_dirs:
        errors = check_readme_file(module_dir)
        assert errors == [], f"Validation failed for {module_dir}: {errors}"


def test_validate_readmes_missing_file():
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp_path = Path(tmpdir)
        pkg_dir = tmp_path / "packages" / "sample_pkg"
        pkg_dir.mkdir(parents=True)

        errors = check_readme_file(pkg_dir)
        assert len(errors) == 1
        assert "Missing README.md" in errors[0]


def test_validate_readmes_empty_file():
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp_path = Path(tmpdir)
        pkg_dir = tmp_path / "packages" / "sample_pkg"
        pkg_dir.mkdir(parents=True)
        (pkg_dir / "README.md").write_text("   \n\n  ", encoding="utf-8")

        errors = check_readme_file(pkg_dir)
        assert len(errors) == 1
        assert "Empty README.md" in errors[0]


def test_validate_readmes_missing_header():
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp_path = Path(tmpdir)
        pkg_dir = tmp_path / "packages" / "sample_pkg"
        pkg_dir.mkdir(parents=True)

        incomplete_content = """# Sample Package
## Overview
This is a sample.
## Architectural Boundaries
Boundaries defined.
## Public APIs / Exports
None.
## Dependencies
None.
"""
        (pkg_dir / "README.md").write_text(incomplete_content, encoding="utf-8")

        errors = check_readme_file(pkg_dir)
        assert len(errors) == 1
        assert (
            "missing required section header(s): Local Development & Testing"
            in errors[0]
        )


def test_scaffold_readmes_and_validate_integration():
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp_path = Path(tmpdir)
        packages_dir = tmp_path / "packages"
        apps_dir = tmp_path / "apps"

        (packages_dir / "my_pkg").mkdir(parents=True)
        (apps_dir / "my_app").mkdir(parents=True)

        # Run scaffolding
        sys.argv = ["scaffold_readmes.py", "--repo-root", str(tmp_path)]
        scaffold_ret = scaffold_main()
        assert scaffold_ret == 0

        # Verify READMEs were created
        assert (packages_dir / "my_pkg" / "README.md").exists()
        assert (apps_dir / "my_app" / "README.md").exists()

        # Run validation
        sys.argv = ["validate_readmes.py", "--repo-root", str(tmp_path)]
        val_ret = validate_main()
        assert val_ret == 0


def test_scaffold_readmes_non_destructive():
    with tempfile.TemporaryDirectory() as tmpdir:
        tmp_path = Path(tmpdir)
        pkg_dir = tmp_path / "packages" / "existing_pkg"
        pkg_dir.mkdir(parents=True)

        custom_readme = "# Existing\nCustom Content"
        readme_path = pkg_dir / "README.md"
        readme_path.write_text(custom_readme, encoding="utf-8")

        sys.argv = ["scaffold_readmes.py", "--repo-root", str(tmp_path)]
        scaffold_main()

        # Check content was preserved
        assert readme_path.read_text(encoding="utf-8") == custom_readme
