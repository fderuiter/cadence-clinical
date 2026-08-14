#!/usr/bin/env python3
"""
Centralized Python runtime guard and environment validator.
Ensures that all developer scripts and quality gate validators run under the
pinned workspace Python runtime (Python 3.14+) and provides interpreter telemetry.
"""

import sys
from pathlib import Path

MIN_PYTHON_VERSION: tuple[int, int] = (3, 14)


def get_runtime_info() -> str:
    """Returns a formatted string containing Python version and executable path."""
    version_str = f"{sys.version_info[0]}.{sys.version_info[1]}.{sys.version_info[2]}"
    return f"Python {version_str} ({sys.executable})"


def print_runtime_info(script_name: str | None = None, quiet: bool = False) -> None:
    """Emits runtime interpreter telemetry to stdout."""
    if quiet:
        return
    prefix = f"[{script_name}] " if script_name else ""
    print(f"[INFO] {prefix}Python Runtime: {get_runtime_info()}", flush=True)


def enforce_python_runtime(
    min_version: tuple[int, int] = MIN_PYTHON_VERSION,
    script_name: str | None = None,
) -> None:
    """
    Enforces that the current Python interpreter meets the minimum version requirement.
    If the current interpreter is incompatible, prints an actionable error message
    and immediately terminates execution with exit code 1.
    """
    if sys.version_info < min_version:
        current_ver = (
            f"{sys.version_info[0]}.{sys.version_info[1]}.{sys.version_info[2]}"
        )
        min_ver_str = f"{min_version[0]}.{min_version[1]}+"
        target_script = script_name or (
            Path(sys.argv[0]).name
            if sys.argv and sys.argv[0]
            else "scripts/<script>.py"
        )
        error_msg = (
            f"\n"
            f"======================================================================\n"
            f"[FATAL] Incompatible Python Runtime Detected!\n"
            f"======================================================================\n"
            f"  Current runtime:  Python {current_ver} ({sys.executable})\n"
            f"  Required runtime: Python {min_ver_str}\n"
            f"\n"
            f"Cadence Clinical validators require modern Python syntax and features.\n"
            f"Running with an outdated or system interpreter causes degraded validations.\n"
            f"\n"
            f"Remediation:\n"
            f"  Run using the pinned workspace environment with 'uv':\n"
            f"    $ uv run python scripts/{target_script}\n"
            f"  Or execute the unified quality gates runner:\n"
            f"    $ uv run cadence check\n"
            f"======================================================================\n"
        )
        sys.stderr.write(error_msg)
        sys.stderr.flush()
        sys.exit(1)


# Enforce immediately if executed or imported
enforce_python_runtime()
