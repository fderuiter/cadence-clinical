import subprocess
from pathlib import Path
from unittest import mock

import pytest

import scripts.validate_markdown as vm


@pytest.fixture(autouse=True)
def clear_vm_errors():
    """Clears the global error list in the validator script before and after each test."""
    vm.errors.clear()
    yield
    vm.errors.clear()


def test_clean_token():
    """Verifies that clean_token correctly strips enclosing and trailing punctuation while preserving leading dot."""
    assert vm.clean_token("`docs/adr/`") == "docs/adr/"
    assert vm.clean_token("docs/adr/,") == "docs/adr/"
    assert vm.clean_token("`docs/adr/index.md`.") == "docs/adr/index.md"
    assert (
        vm.clean_token(".github/workflows/production-pipeline.yml")
        == ".github/workflows/production-pipeline.yml"
    )
    assert vm.clean_token("tests/.") == "tests/"
    assert (
        vm.clean_token("(e.g., `docs/adr/2026-06-06-usdm-pydantic-models.md`).")
        == "e.g., `docs/adr/2026-06-06-usdm-pydantic-models.md"
    )


def test_is_potential_path_ref():
    """Verifies is_potential_path_ref logic for detecting workspace paths and files."""
    root_dirs = {"apps", "packages", "docs", "scripts", "tests", "docker", ".github"}
    root_files = {"pyproject.toml", "package.json", "run-checks.sh", "README.md"}

    # Ignored cases
    assert not vm.is_potential_path_ref("-d", root_dirs, root_files)
    assert not vm.is_potential_path_ref("http://localhost:8000", root_dirs, root_files)
    assert not vm.is_potential_path_ref("foo$BAR", root_dirs, root_files)
    assert not vm.is_potential_path_ref("your-placeholder-here", root_dirs, root_files)
    assert not vm.is_potential_path_ref("/dev/null", root_dirs, root_files)
    assert not vm.is_potential_path_ref(
        "/openapi.json", root_dirs, root_files
    )  # not in root files

    # Matches
    assert vm.is_potential_path_ref("./run-checks.sh", root_dirs, root_files)
    assert vm.is_potential_path_ref("../docs/SRS.md", root_dirs, root_files)
    assert vm.is_potential_path_ref("apps/execution/main.py", root_dirs, root_files)
    assert vm.is_potential_path_ref("pyproject.toml", root_dirs, root_files)
    assert vm.is_potential_path_ref(
        "/docs/LOCAL_DEV_ENVIRONMENT.md", root_dirs, root_files
    )
    assert vm.is_potential_path_ref("scripts/validate_adrs.py", root_dirs, root_files)


def test_resolve_path(tmp_path):
    """Verifies that path resolution works relative to repository root and markdown files."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    docs_dir = repo_root / "docs"
    docs_dir.mkdir()

    apps_dir = repo_root / "apps"
    apps_dir.mkdir()

    md_file = docs_dir / "LOCAL_DEV_ENVIRONMENT.md"
    md_file.touch()

    root_dirs = {"apps", "packages", "docs", "scripts", "tests", "docker"}
    root_files = {"pyproject.toml", "package.json", "run-checks.sh", "README.md"}

    # 1. Workspace root-relative path starting with known top-level folder
    res = vm.resolve_path(
        "apps/execution/main.py", md_file, repo_root, root_dirs, root_files
    )
    assert res == repo_root / "apps/execution/main.py"

    # 2. Workspace root-relative path starting with leading slash
    res = vm.resolve_path(
        "/docs/LOCAL_DEV_ENVIRONMENT.md", md_file, repo_root, root_dirs, root_files
    )
    assert res == repo_root / "docs/LOCAL_DEV_ENVIRONMENT.md"

    # 3. Path relative to current markdown file
    res = vm.resolve_path("./index.md", md_file, repo_root, root_dirs, root_files)
    assert res == docs_dir / "index.md"

    # 4. Relative path escaping up
    res = vm.resolve_path("../apps/designer", md_file, repo_root, root_dirs, root_files)
    assert Path(res).resolve() == Path(apps_dir / "designer").resolve()

    # 5. External URL and placeholders return None
    assert (
        vm.resolve_path("https://google.com", md_file, repo_root, root_dirs, root_files)
        is None
    )
    assert (
        vm.resolve_path(
            "your-placeholder-file", md_file, repo_root, root_dirs, root_files
        )
        is None
    )


def test_validate_path(tmp_path):
    """Verifies validate_path detects existing/nonexistent files and boundaries."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    docs_dir = repo_root / "docs"
    docs_dir.mkdir()

    md_file = docs_dir / "LOCAL_DEV_ENVIRONMENT.md"
    md_file.touch()

    root_dirs = {"docs"}
    root_files = set()

    # Existing file
    target_file = docs_dir / "index.md"
    target_file.touch()
    vm.validate_path("./index.md", md_file, 10, repo_root, root_dirs, root_files)
    assert len(vm.errors) == 0

    # Nonexistent file
    vm.validate_path("./nonexistent.md", md_file, 20, repo_root, root_dirs, root_files)
    assert len(vm.errors) == 1
    assert (
        "Referenced path './nonexistent.md' does not exist." in vm.errors[0]["message"]
    )


def test_validate_cli_command_python_and_pytest(tmp_path):
    """Verifies validation of python3 and pytest command targets."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    tests_dir = repo_root / "tests"
    tests_dir.mkdir()

    md_file = repo_root / "README.md"
    md_file.touch()

    root_dirs = {"tests"}
    root_files = set()

    # Valid pytest target
    test_file = tests_dir / "test_main.py"
    test_file.touch()

    vm.validate_cli_command(
        ["pytest", "tests/test_main.py"], 12, md_file, repo_root, root_dirs, root_files
    )
    assert len(vm.errors) == 0

    # Invalid pytest target
    vm.validate_cli_command(
        ["pytest", "tests/test_nonexistent.py"],
        15,
        md_file,
        repo_root,
        root_dirs,
        root_files,
    )
    assert len(vm.errors) == 1
    assert (
        "Target path 'tests/test_nonexistent.py' for executable 'pytest' does not exist."
        in vm.errors[0]["message"]
    )


def test_validate_cli_command_flag_checks(tmp_path):
    """Verifies that malformed flags are detected."""
    repo_root = tmp_path
    md_file = repo_root / "README.md"

    root_dirs = set()
    root_files = set()

    # Valid flags
    vm.validate_cli_command(
        ["pytest", "-v", "--cov=apps"], 5, md_file, repo_root, root_dirs, root_files
    )
    assert len(vm.errors) == 0

    # Malformed flag
    vm.validate_cli_command(
        ["pytest", "---cov=apps"], 10, md_file, repo_root, root_dirs, root_files
    )
    assert len(vm.errors) == 1
    assert (
        "Malformed or invalid CLI flag structure: '---cov=apps'"
        in vm.errors[0]["message"]
    )


def test_validate_docker_compose_scenarios(tmp_path):
    """Verifies docker compose file presence checks and config dry-run behavior."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    docker_dir = repo_root / "docker"
    docker_dir.mkdir()

    md_file = repo_root / "README.md"
    md_file.touch()

    root_dirs = {"docker"}
    root_files = set()

    # Compose file does not exist
    vm.validate_cli_command(
        ["docker", "compose", "-f", "docker/docker-compose.yml", "up", "-d"],
        20,
        md_file,
        repo_root,
        root_dirs,
        root_files,
    )
    assert len(vm.errors) == 1
    assert (
        "Docker compose file 'docker/docker-compose.yml' does not exist."
        in vm.errors[0]["message"]
    )

    # Compose file exists, mock docker config success
    vm.errors.clear()
    compose_file = docker_dir / "docker-compose.yml"
    compose_file.touch()

    with (
        mock.patch("shutil.which", return_value="/bin/docker"),
        mock.patch("subprocess.run") as mock_run,
    ):
        mock_run.return_value = mock.Mock(returncode=0)

        vm.validate_cli_command(
            ["docker", "compose", "-f", "docker/docker-compose.yml", "up", "-d"],
            20,
            md_file,
            repo_root,
            root_dirs,
            root_files,
        )
        assert len(vm.errors) == 0
        mock_run.assert_called_once_with(
            ["docker", "compose", "-f", str(compose_file), "config"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE,
            cwd=str(repo_root),
            check=True,
        )


def test_process_markdown_file_e2e(tmp_path):
    """Performs an end-to-end parse check on a simulated markdown file containing paths, links, and code blocks."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    docs_dir = repo_root / "docs"
    docs_dir.mkdir()

    tests_dir = repo_root / "tests"
    tests_dir.mkdir()

    # Real files on mock filesystem
    (docs_dir / "LOCAL_DEV_ENVIRONMENT.md").touch()
    (tests_dir / "test_audit.py").touch()
    (repo_root / "run-checks.sh").touch()

    root_dirs = {"docs", "tests"}
    root_files = {"run-checks.sh"}

    md_content = """# System Guide

Please check our [Local Dev Guide](docs/LOCAL_DEV_ENVIRONMENT.md).
See also [Nonexistent Guide](docs/NONEXISTENT.md).

Here are some commands:
```bash
# This is a comment
./run-checks.sh

pytest tests/test_audit.py
pytest tests/test_nonexistent.py
```
"""
    md_file = repo_root / "README.md"
    with open(md_file, "w", encoding="utf-8") as f:
        f.write(md_content)

    vm.process_markdown_file(md_file, repo_root, root_dirs, root_files)

    # Errors expected:
    # 1. docs/NONEXISTENT.md does not exist
    # 2. pytest tests/test_nonexistent.py target does not exist
    assert len(vm.errors) == 2

    error_msgs = [e["message"] for e in vm.errors]
    assert "Referenced link 'docs/NONEXISTENT.md' does not exist." in error_msgs
    assert (
        "Target path 'tests/test_nonexistent.py' for executable 'pytest' does not exist."
        in error_msgs
    )
