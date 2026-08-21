#!/usr/bin/env python3
"""
Subsystem Profile Orchestrator CLI for Cadence Clinical.

Enables isolated startup and orchestration of target subsystem profiles
('designer', 'execution', 'operations', 'all') to optimize local developer
resource usage.
"""

import argparse
import subprocess
import sys
from pathlib import Path

# Add repository root to sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

# Enforce Python 3.14+ runtime before loading standard modules or packages
if sys.version_info < (3, 14):
    try:
        from scripts.runtime_guard import enforce_python_runtime

        enforce_python_runtime()
    except Exception:
        sys.stderr.write(
            f"[FATAL] Incompatible Python runtime {sys.version.split()[0]} ({sys.executable}).\n"
            "Cadence Clinical requires Python 3.14+.\n"
            "Please run: uv run python scripts/dev_orchestrator.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime, print_runtime_info

COMPOSE_PATH = REPO_ROOT / "docker" / "docker-compose.yml"
VALID_PROFILES = ["designer", "execution", "operations", "all"]
VALID_ACTIONS = ["up", "down", "logs", "ps", "restart", "build", "stop"]


def parse_arguments(args: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Launch and manage Cadence Clinical subsystem profiles natively."
    )
    parser.add_argument(
        "action_or_profile",
        nargs="?",
        default=None,
        help="Docker compose action (up, down, logs, ps, restart, build, stop) OR target subsystem profile (designer, execution, operations, all).",
    )
    parser.add_argument(
        "secondary",
        nargs="?",
        default=None,
        help="Subsystem profile when action is specified as first argument.",
    )
    parser.add_argument(
        "-p",
        "--profile",
        action="append",
        dest="profiles",
        help="Target profile name(s): designer, execution, operations, or all.",
    )
    parser.add_argument(
        "-f",
        "--file",
        default=str(COMPOSE_PATH),
        help="Path to docker-compose configuration file.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the docker compose command without executing it.",
    )

    parsed_args, extra_args = parser.parse_known_args(args)
    parsed_args.extra_args = extra_args
    return parsed_args


def build_compose_command(
    action_or_profile: str | None,
    secondary: str | None,
    profiles_opt: list[str] | None,
    compose_file: str,
    extra_args: list[str],
) -> tuple[str, list[str], list[str]]:
    """
    Determines action, active profile list, and constructs the docker compose command list.
    """
    profiles: list[str] = []

    # Handle explicit --profile / -p flags
    if profiles_opt:
        for p in profiles_opt:
            for item in p.split(","):
                clean = item.strip().lower()
                if clean:
                    profiles.append(clean)

    action = "up"

    if action_or_profile:
        clean_first = action_or_profile.strip().lower()
        if clean_first in VALID_ACTIONS:
            action = clean_first
            if secondary:
                clean_sec = secondary.strip().lower()
                if clean_sec not in profiles:
                    profiles.append(clean_sec)
        elif clean_first in VALID_PROFILES or clean_first == "all":
            if clean_first not in profiles:
                profiles.append(clean_first)
            if secondary and secondary.strip().lower() in VALID_ACTIONS:
                action = secondary.strip().lower()

    if not profiles:
        # If no profile specified, default to 'all'
        profiles = ["all"]

    # Expand 'all' profile to individual domain subsystem profiles
    resolved_profiles: list[str] = []
    for p in profiles:
        if p in ("all", "*"):
            for profile_name in ["designer", "execution", "operations"]:
                if profile_name not in resolved_profiles:
                    resolved_profiles.append(profile_name)
        elif p in VALID_PROFILES:
            if p not in resolved_profiles:
                resolved_profiles.append(p)
        else:
            # Pass through custom service or profile name
            if p not in resolved_profiles:
                resolved_profiles.append(p)

    cmd = ["docker", "compose", "-f", compose_file]
    for prof in resolved_profiles:
        cmd.extend(["--profile", prof])
    cmd.append(action)

    # Automatically add -d for 'up' unless detachment preference is specified
    if action == "up":
        if (
            "-d" not in extra_args
            and "--detach" not in extra_args
            and "--no-detach" not in extra_args
        ):
            cmd.append("-d")

    # Pass through additional docker compose flags
    for arg in extra_args:
        if arg != "--no-detach":
            cmd.append(arg)

    return action, resolved_profiles, cmd


def main(args_list: list[str] | None = None) -> int:
    print_runtime_info("dev_orchestrator")
    parsed = parse_arguments(args_list)

    action, profiles, cmd = build_compose_command(
        parsed.action_or_profile,
        parsed.secondary,
        parsed.profiles,
        parsed.file,
        parsed.extra_args,
    )

    print(
        f"[DEV_ORCHESTRATOR] Action: {action.upper()} | Subsystem Profiles: {', '.join(profiles)}"
    )
    print(f"[DEV_ORCHESTRATOR] Running command: {' '.join(cmd)}")

    if parsed.dry_run:
        print("[DEV_ORCHESTRATOR] Dry run complete. Command was not executed.")
        return 0

    try:
        res = subprocess.run(cmd)
        return res.returncode
    except FileNotFoundError:
        sys.stderr.write(
            "[ERROR] 'docker' CLI or 'docker compose' is not installed or available in PATH.\n"
        )
        return 1


if __name__ == "__main__":
    sys.exit(main())
