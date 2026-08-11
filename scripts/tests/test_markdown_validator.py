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

    # 6. Compiled build artifacts (dist, build, node_modules) return None
    assert (
        vm.resolve_path(
            "packages/ui/dist/index.js", md_file, repo_root, root_dirs, root_files
        )
        is None
    )
    assert (
        vm.resolve_path("build/bundle.js", md_file, repo_root, root_dirs, root_files)
        is None
    )
    assert (
        vm.resolve_path(
            "node_modules/vue/package.json", md_file, repo_root, root_dirs, root_files
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


def test_python_block_validation(tmp_path):
    """Verifies static Python AST signature validation logic."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    # Create a codebase file with a target function
    apps_dir = repo_root / "apps" / "designer"
    apps_dir.mkdir(parents=True)
    cb_file = apps_dir / "delta.py"
    cb_file.write_text("def test_func(tx, study_id, object_id):\n    pass\n")

    # Create a markdown file inside docs/
    docs_dir = repo_root / "docs"
    docs_dir.mkdir()
    md_file = docs_dir / "test_doc.md"

    # 1. Signature mismatch
    md_content = """# Doc
```python
def test_func(tx, study_version_id):
    pass
```
"""
    md_file.write_text(md_content)

    codebase_map = vm.build_codebase_map(repo_root)
    vm.process_markdown_file(md_file, repo_root, set(), set(), codebase_map)
    assert len(vm.errors) == 1
    assert (
        "Mismatched Python signature for function 'test_func'"
        in vm.errors[0]["message"]
    )


def test_json_block_validation(tmp_path):
    """Verifies JSON schema validation with Pydantic models."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    # Create a codebase file with a BaseModel
    packages_dir = repo_root / "packages" / "security"
    packages_dir.mkdir(parents=True)
    cb_file = packages_dir / "signature.py"
    cb_file.write_text("""from pydantic import BaseModel, Field
class TestModel(BaseModel):
    id: str = Field(...)
    name: str = Field(None, description="Optional name")
""")

    docs_dir = repo_root / "docs"
    docs_dir.mkdir()
    md_file = docs_dir / "test_doc.md"

    # 1. Missing required field
    md_content = """# Doc
#### TestModel
```json
{
  "name": "John"
}
```
"""
    md_file.write_text(md_content)

    codebase_map = vm.build_codebase_map(repo_root)
    vm.process_markdown_file(md_file, repo_root, set(), set(), codebase_map)
    assert len(vm.errors) == 1
    assert (
        "JSON example mismatch with Pydantic model 'TestModel'"
        in vm.errors[0]["message"]
    )


def test_skip_and_raw_text_flags(tmp_path):
    """Verifies that skip and raw-text annotations prevent model validation."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    # Create a codebase file with a BaseModel
    packages_dir = repo_root / "packages" / "security"
    packages_dir.mkdir(parents=True)
    cb_file = packages_dir / "signature.py"
    cb_file.write_text("""from pydantic import BaseModel, Field
class TestModel(BaseModel):
    id: str = Field(...)
""")

    docs_dir = repo_root / "docs"
    docs_dir.mkdir()
    md_file = docs_dir / "test_doc.md"

    # Both of these should be skipped
    md_content = """# Doc
<!-- skip -->
```json
{
  "name": "John"
}
```

```json skip
{
  "name": "Doe"
}
```
"""
    md_file.write_text(md_content)

    codebase_map = vm.build_codebase_map(repo_root)
    vm.process_markdown_file(md_file, repo_root, set(), set(), codebase_map)
    # Both skipped, so 0 errors!
    assert len(vm.errors) == 0


def test_contributing_guide_skip_and_validation(tmp_path):
    """Verifies that an inline skip comment inside a Python block prevents validation,
    while non-annotated blocks in the same file are still validated and fail on mismatch.
    """
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    # Create a codebase file with a function
    apps_dir = repo_root / "apps" / "designer"
    apps_dir.mkdir(parents=True)
    cb_file = apps_dir / "delta.py"
    cb_file.write_text("def test_func(tx, study_id):\n    pass\n")

    # Create a markdown file (guideline)
    md_file = repo_root / "CONTRIBUTING.md"

    md_content = """# Guide

This block is skipped because of the inline skip comment:
```python
# skip
def test_func(tx, study_version_id):
    pass
```

This block is standard, so it should be validated and fail on mismatch:
```python
# apps/designer/delta.py
def test_func(tx, study_version_id):
    pass
```
"""
    md_file.write_text(md_content)

    codebase_map = vm.build_codebase_map(repo_root)
    # Clear errors before run
    vm.errors.clear()
    vm.process_markdown_file(md_file, repo_root, set(), set(), codebase_map)

    # We expect exactly 1 error (from the second block)
    assert len(vm.errors) == 1
    assert (
        "Mismatched Python signature for function 'test_func'"
        in vm.errors[0]["message"]
    )


def test_main_with_arguments(monkeypatch):
    """Verifies that main() processes only the markdown files specified in sys.argv."""
    import sys

    monkeypatch.setattr(sys, "argv", ["validate_markdown.py", "README.md"])

    processed_files = []
    original_process = vm.process_markdown_file

    def mock_process(
        file_path, repo_root_arg, root_dirs, root_files, codebase_map, *args, **kwargs
    ):
        processed_files.append(Path(file_path).name)
        original_process(
            file_path,
            repo_root_arg,
            root_dirs,
            root_files,
            codebase_map,
            *args,
            **kwargs,
        )

    monkeypatch.setattr(vm, "process_markdown_file", mock_process)

    exit_codes = []
    monkeypatch.setattr(sys, "exit", exit_codes.append)

    vm.main()

    assert "README.md" in processed_files
    assert len(processed_files) == 1
    assert exit_codes == [0]


def test_html_comment_filtering(tmp_path):
    """Verifies that links and paths inside single-line and multi-line HTML comments are ignored."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    docs_dir = repo_root / "docs"
    docs_dir.mkdir()

    (docs_dir / "valid-doc.md").touch()

    root_dirs = {"docs"}
    root_files = set()

    md_content = """# Doc with Comments

This is a valid link [Valid](./valid-doc.md).

<!-- This is a commented-out broken link [Broken](./does-not-exist.md) -->

And some text outside the single-line comment.

<!--
This is a multi-line HTML comment.
[Broken Multi](./also-does-not-exist.md)
-->

This is a plain text path reference to a non-existent file in a comment <!-- docs/non-existent.md --> but outside <!-- comment --> we should not have errors.
"""
    md_file = docs_dir / "comment_test.md"
    md_file.write_text(md_content, encoding="utf-8")

    vm.process_markdown_file(md_file, repo_root, root_dirs, root_files)
    assert len(vm.errors) == 0


def test_reference_style_link_validation(tmp_path):
    """Verifies reference-style links are parsed, resolved relative to correct dir, and validated."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    docs_dir = repo_root / "docs"
    docs_dir.mkdir()

    (docs_dir / "valid-ref.md").touch()

    root_dirs = {"docs"}
    root_files = set()

    # We test:
    # 1. A valid reference-style link relative to current directory
    # 2. An invalid reference-style link
    # 3. Reference link with query/anchor
    # 4. Reference link starting with / resolved relative to repo root
    md_content = """# Reference Links

See [my document][ref1] and also [broken doc][ref2].

[ref1]: ./valid-ref.md
[ref2]: ./nonexistent-ref.md
[ref3]: /docs/valid-ref.md#anchor?query=1
"""
    md_file = docs_dir / "ref_test.md"
    md_file.write_text(md_content, encoding="utf-8")

    vm.process_markdown_file(md_file, repo_root, root_dirs, root_files)

    # We expect 1 error from ref2: ./nonexistent-ref.md
    assert len(vm.errors) == 1
    assert (
        "Referenced reference-link './nonexistent-ref.md' does not exist."
        in vm.errors[0]["message"]
    )


def test_sys_path_append():
    """Verifies that packages and apps subdirectories are appended to sys.path, not prepended."""
    import sys
    from pathlib import Path

    resolved_sys_path = []
    for x in sys.path:
        try:
            resolved_sys_path.append(str(Path(x).resolve()))
        except Exception:
            resolved_sys_path.append(x)

    project_root = Path(__file__).resolve().parent.parent
    for p in (project_root / "packages").glob("*"):
        if p.is_dir() and p.name != "__pycache__":
            resolved_path_str = str(p.resolve())
            assert resolved_path_str in resolved_sys_path
            # Since they were appended, they should not be at index 0
            found_index = -1
            for idx, x in enumerate(sys.path):
                try:
                    if str(Path(x).resolve()) == resolved_path_str:
                        found_index = idx
                        break
                except Exception:
                    pass
            assert found_index > 0


def test_mock_environment_variables():
    """Verifies that default/mock environment configurations are injected into os.environ."""
    import os

    assert os.environ.get("DATABASE_URL") is not None
    assert os.environ.get("ENV") is not None
    assert os.environ.get("ENVIRONMENT") is not None
    assert os.environ.get("DEBUG") == "True"


def test_exclude_tests_from_codebase_map(tmp_path):
    """Verifies that 'tests' and 'test' directories are excluded during codebase map generation."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    apps_dir = repo_root / "apps" / "designer"
    apps_dir.mkdir(parents=True)
    (apps_dir / "models.py").write_text("class RealModel:\n    pass\n")

    tests_dir = repo_root / "tests"
    tests_dir.mkdir()
    (tests_dir / "test_models.py").write_text("class TestModel:\n    pass\n")

    test_dir = repo_root / "test"
    test_dir.mkdir()
    (test_dir / "test_something.py").write_text("class AnotherTestModel:\n    pass\n")

    codebase_map = vm.build_codebase_map(repo_root)

    assert "RealModel" in codebase_map
    assert "TestModel" not in codebase_map
    assert "AnotherTestModel" not in codebase_map


def test_degraded_linter_warnings_and_fallback(tmp_path, capsys):
    """Verifies that a dynamic import failure emits a degraded coverage warning to stderr, then falls back."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    packages_dir = repo_root / "packages" / "security"
    packages_dir.mkdir(parents=True)
    cb_file = packages_dir / "failing_model.py"
    cb_file.write_text("""from pydantic import BaseModel, Field
raise ValueError("Simulated top-level import failure")
class BrokenModel(BaseModel):
    id: str = Field(...)
""")

    docs_dir = repo_root / "docs"
    docs_dir.mkdir()
    md_file = docs_dir / "test_doc.md"

    md_content = """# Doc
#### BrokenModel
```json
{
  "id": "123"
}
```
"""
    md_file.write_text(md_content)

    codebase_map = vm.build_codebase_map(repo_root)

    assert "BrokenModel" in codebase_map

    vm.process_markdown_file(md_file, repo_root, set(), set(), codebase_map)

    captured = capsys.readouterr()

    assert "[WARNING] Degraded linter coverage" in captured.err
    assert "Failed to dynamically load Pydantic model 'BrokenModel'" in captured.err
    assert "ValueError: Simulated top-level import failure" in captured.err
    assert "Falling back to basic shallow AST structure verification" in captured.err

    assert len(vm.errors) == 0


def test_degraded_linter_warnings_and_fallback_failure(tmp_path, capsys):
    """Verifies that if fallback also fails (missing required field), errors are reported."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    packages_dir = repo_root / "packages" / "security"
    packages_dir.mkdir(parents=True)
    cb_file = packages_dir / "failing_model2.py"
    cb_file.write_text("""from pydantic import BaseModel, Field
raise ValueError("Another simulated top-level failure")
class BrokenModel2(BaseModel):
    id: str = Field(...)
    name: str = Field(...)
""")

    docs_dir = repo_root / "docs"
    docs_dir.mkdir()
    md_file = docs_dir / "test_doc.md"

    md_content = """# Doc
#### BrokenModel2
```json
{
  "id": "123"
}
```
"""
    md_file.write_text(md_content)

    codebase_map = vm.build_codebase_map(repo_root)

    vm.process_markdown_file(md_file, repo_root, set(), set(), codebase_map)

    captured = capsys.readouterr()

    assert "[WARNING] Degraded linter coverage" in captured.err
    assert "ValueError: Another simulated top-level failure" in captured.err

    assert len(vm.errors) == 1
    assert (
        "JSON example mismatch with Pydantic model 'BrokenModel2'"
        in vm.errors[0]["message"]
    )
    assert "Missing required fields: ['name']" in vm.errors[0]["message"]


def test_nested_code_blocks_in_html_comments(tmp_path):
    """Verifies that nested code blocks inside single-line and multi-line HTML comments are ignored."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    md_content = """# Nested Code Blocks in Comments

Please ignore this single-line comment containing code block start: <!-- ```python def ignored(): pass ``` -->

<!--
```python
def commented_out_syntax_error():
    this is invalid python syntax and has a mismatched signature!
```
-->

This is a valid section after comments.
"""
    md_file = repo_root / "test_comments.md"
    md_file.write_text(md_content, encoding="utf-8")

    vm.process_markdown_file(md_file, repo_root, set(), set(), {})
    assert len(vm.errors) == 0


def test_uncommented_code_block_errors_maintained(tmp_path):
    """Verifies that uncommented code blocks with syntax errors are still correctly caught with accurate line numbers."""
    repo_root = tmp_path / "repo"
    repo_root.mkdir()

    # Line 1: # Document
    # Line 2: (empty)
    # Line 3: <!--
    # Line 4: ```python
    # Line 5: def commented_error():
    # Line 6:     !!!
    # Line 7: ```
    # Line 8: -->
    # Line 9: (empty)
    # Line 10: Below is an active code block with a syntax error
    # Line 11: ```python
    # Line 12: def active_syntax_error():
    # Line 13:     this is invalid python
    # Line 14: ```
    md_content = """# Document

<!--
```python
def commented_error():
    !!!
```
-->

Below is an active code block with a syntax error
```python
def active_syntax_error():
    this is invalid python
```
"""
    md_file = repo_root / "test_uncommented.md"
    md_file.write_text(md_content, encoding="utf-8")

    vm.process_markdown_file(md_file, repo_root, set(), set(), {})

    # We expect exactly 1 error on line 12 (due to existing start_line + e.lineno - 1 formula)
    assert len(vm.errors) == 1
    err = vm.errors[0]
    assert err["line"] == 12
    assert "Python SyntaxError" in err["message"]
