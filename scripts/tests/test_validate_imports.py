from pathlib import Path

from scripts.validate_imports import APPS_DIR, check_file_imports, get_service_name


def test_get_service_name():
    assert get_service_name(APPS_DIR / "etmf" / "main.py") == "etmf"
    assert get_service_name(APPS_DIR / "execution" / "trial_lock.py") == "execution"
    assert get_service_name(Path("/tmp/foo.py")) == ""  # nosec B108


def test_check_file_imports_same_service(tmp_path):
    # Set up a mock structure: apps/etmf/main.py
    etmf_dir = tmp_path / "apps" / "etmf"
    etmf_dir.mkdir(parents=True, exist_ok=True)

    file_path = etmf_dir / "main.py"
    file_path.write_text(
        "import apps.etmf.database\nfrom apps.etmf.models import TMFDocument\n",
        encoding="utf-8",
    )

    # Temporarily patch APPS_DIR in scripts.validate_imports to our temp apps dir
    import scripts.validate_imports

    original_apps_dir = scripts.validate_imports.APPS_DIR
    scripts.validate_imports.APPS_DIR = tmp_path / "apps"

    try:
        violations = check_file_imports(file_path)
        assert len(violations) == 0
    finally:
        scripts.validate_imports.APPS_DIR = original_apps_dir


def test_check_file_imports_shared_packages(tmp_path):
    # Set up a mock structure: apps/etmf/main.py
    etmf_dir = tmp_path / "apps" / "etmf"
    etmf_dir.mkdir(parents=True, exist_ok=True)

    file_path = etmf_dir / "main.py"
    file_path.write_text(
        "import packages.security.signing\n"
        "from packages.database import DatabaseSessionDependency\n",
        encoding="utf-8",
    )

    import scripts.validate_imports

    original_apps_dir = scripts.validate_imports.APPS_DIR
    scripts.validate_imports.APPS_DIR = tmp_path / "apps"

    try:
        violations = check_file_imports(file_path)
        assert len(violations) == 0
    finally:
        scripts.validate_imports.APPS_DIR = original_apps_dir


def test_check_file_imports_cross_service_violation(tmp_path):
    # Set up a mock structure: apps/etmf/main.py
    etmf_dir = tmp_path / "apps" / "etmf"
    etmf_dir.mkdir(parents=True, exist_ok=True)

    file_path = etmf_dir / "main.py"
    file_path.write_text(
        "import apps.execution.trial_lock\nfrom apps.ctms.database import db_manager\n",
        encoding="utf-8",
    )

    import scripts.validate_imports

    original_apps_dir = scripts.validate_imports.APPS_DIR
    scripts.validate_imports.APPS_DIR = tmp_path / "apps"

    try:
        violations = check_file_imports(file_path)
        assert len(violations) == 2
        assert "Direct import of service 'execution'" in violations[0]
        assert "Direct import of service 'ctms'" in violations[1]
    finally:
        scripts.validate_imports.APPS_DIR = original_apps_dir


def test_check_file_imports_relative_same_service(tmp_path):
    # Set up apps/etmf/sub/file.py
    sub_dir = tmp_path / "apps" / "etmf" / "sub"
    sub_dir.mkdir(parents=True, exist_ok=True)

    file_path = sub_dir / "file.py"
    file_path.write_text(
        "from . import sibling\nfrom ..database import db_manager\n", encoding="utf-8"
    )

    import scripts.validate_imports

    original_apps_dir = scripts.validate_imports.APPS_DIR
    original_root_dir = scripts.validate_imports.ROOT_DIR
    scripts.validate_imports.APPS_DIR = tmp_path / "apps"
    scripts.validate_imports.ROOT_DIR = tmp_path

    try:
        violations = check_file_imports(file_path)
        assert len(violations) == 0
    finally:
        scripts.validate_imports.APPS_DIR = original_apps_dir
        scripts.validate_imports.ROOT_DIR = original_root_dir


def test_check_file_imports_relative_cross_service(tmp_path):
    # Set up apps/etmf/sub/file.py
    sub_dir = tmp_path / "apps" / "etmf" / "sub"
    sub_dir.mkdir(parents=True, exist_ok=True)

    file_path = sub_dir / "file.py"
    file_path.write_text("from ...execution import trial_lock\n", encoding="utf-8")

    import scripts.validate_imports

    original_apps_dir = scripts.validate_imports.APPS_DIR
    original_root_dir = scripts.validate_imports.ROOT_DIR
    scripts.validate_imports.APPS_DIR = tmp_path / "apps"
    scripts.validate_imports.ROOT_DIR = tmp_path

    try:
        violations = check_file_imports(file_path)
        assert len(violations) == 1
        assert "Direct import of service 'execution'" in violations[0]
    finally:
        scripts.validate_imports.APPS_DIR = original_apps_dir
        scripts.validate_imports.ROOT_DIR = original_root_dir


def test_check_file_imports_invalid_syntax(tmp_path):
    etmf_dir = tmp_path / "apps" / "etmf"
    etmf_dir.mkdir(parents=True, exist_ok=True)

    file_path = etmf_dir / "bad.py"
    file_path.write_text("this is not valid python code !!!", encoding="utf-8")

    import scripts.validate_imports

    original_apps_dir = scripts.validate_imports.APPS_DIR
    scripts.validate_imports.APPS_DIR = tmp_path / "apps"

    try:
        violations = check_file_imports(file_path)
        assert len(violations) == 1
        assert "Failed to parse file" in violations[0]
    finally:
        scripts.validate_imports.APPS_DIR = original_apps_dir
