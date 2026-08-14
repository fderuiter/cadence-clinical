"""
Unit tests for the centralized Python runtime guard and environment validator.
"""

import sys

import pytest

import scripts.runtime_guard as rg


def test_get_runtime_info():
    """Verify get_runtime_info returns properly formatted runtime string."""
    info = rg.get_runtime_info()
    assert info.startswith("Python ")
    assert str(sys.version_info.major) in info
    assert sys.executable in info


def test_print_runtime_info(capsys):
    """Verify print_runtime_info outputs expected message and respects quiet flag."""
    rg.print_runtime_info("test_tool.py", quiet=False)
    captured = capsys.readouterr()
    assert "[INFO] [test_tool.py] Python Runtime: Python " in captured.out

    # Test without script_name prefix
    rg.print_runtime_info(quiet=False)
    captured = capsys.readouterr()
    assert "[INFO] Python Runtime: Python " in captured.out

    # Test quiet suppression
    rg.print_runtime_info("test_tool.py", quiet=True)
    captured = capsys.readouterr()
    assert captured.out == ""


def test_enforce_python_runtime_passes_on_valid_version():
    """Verify enforce_python_runtime succeeds when runtime meets version threshold."""
    # Under current environment (Python 3.14+), should succeed without error
    rg.enforce_python_runtime(min_version=(3, 14), script_name="valid_script.py")


def test_enforce_python_runtime_fails_on_outdated_version(monkeypatch, capsys):
    """Verify enforce_python_runtime exits with code 1 and prints remediation message on outdated Python."""
    mock_version = (3, 9, 6, "final", 0)
    monkeypatch.setattr(sys, "version_info", mock_version)

    with pytest.raises(SystemExit) as exc_info:
        rg.enforce_python_runtime(
            min_version=(3, 14), script_name="validate_markdown.py"
        )

    assert exc_info.value.code == 1
    captured = capsys.readouterr()
    assert "[FATAL] Incompatible Python Runtime Detected!" in captured.err
    assert "Current runtime:  Python 3.9.6" in captured.err
    assert "Required runtime: Python 3.14+" in captured.err
    assert "$ uv run python scripts/validate_markdown.py" in captured.err
    assert "$ uv run cadence check" in captured.err


@pytest.mark.parametrize(
    "outdated_version,version_str",
    [
        ((3, 7, 0, "final", 0), "3.7.0"),
        ((3, 10, 12, "final", 0), "3.10.12"),
        ((3, 11, 8, "final", 0), "3.11.8"),
        ((3, 12, 4, "final", 0), "3.12.4"),
        ((3, 13, 0, "final", 0), "3.13.0"),
    ],
)
def test_enforce_python_runtime_matrix(
    monkeypatch, capsys, outdated_version, version_str
):
    """Verify various sub-3.14 Python versions trigger the fatal runtime guard."""
    monkeypatch.setattr(sys, "version_info", outdated_version)

    with pytest.raises(SystemExit) as exc_info:
        rg.enforce_python_runtime(min_version=(3, 14), script_name="validate_adrs.py")

    assert exc_info.value.code == 1
    captured = capsys.readouterr()
    assert f"Current runtime:  Python {version_str}" in captured.err
    assert "Required runtime: Python 3.14+" in captured.err
