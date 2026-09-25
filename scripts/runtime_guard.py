#!/usr/bin/env python3
"""
Centralized Python runtime guard and environment validator.
Ensures that all developer scripts and quality gate validators run under the
pinned workspace Python runtime (Python 3.14+) and provides interpreter telemetry.
"""

import contextlib
import os
import shutil
import sys
from pathlib import Path

MIN_PYTHON_VERSION: tuple[int, int] = (3, 10)


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
    auto_reexec: bool = True,
) -> None:
    """
    Enforces that the current Python interpreter meets the minimum version requirement.
    If the current interpreter is incompatible, automatically delegates execution to
    the managed Python 3.14+ runtime via uv if available. Otherwise, prints an
    actionable error message and terminates execution with exit code 1.
    """
    if sys.version_info < min_version:
        # Check if auto re-execution via uv should be attempted
        disable_reexec = os.environ.get(
            "CADENCE_DISABLE_RUNTIME_REEXEC", ""
        ).lower() in ("1", "true", "yes")
        is_testing = False

        if auto_reexec and not disable_reexec and not is_testing:
            uv_bin = shutil.which("uv")
            if uv_bin:
                current_ver = (
                    f"{sys.version_info[0]}.{sys.version_info[1]}.{sys.version_info[2]}"
                )
                min_ver_str = f"{min_version[0]}.{min_version[1]}+"
                sys.stderr.write(
                    f"[INFO] Incompatible Python runtime ({current_ver}) detected (<{min_ver_str}). "
                    f"Delegating execution to uv managed Python {min_ver_str} runtime...\n"
                )
                sys.stderr.flush()
                args = sys.argv if sys.argv else ["python"]
                cmd = [
                    uv_bin,
                    "run",
                    "--python",
                    f"{min_version[0]}.{min_version[1]}",
                    "python",
                ] + args
                with contextlib.suppress(Exception):
                    os.execvp(uv_bin, cmd)

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
