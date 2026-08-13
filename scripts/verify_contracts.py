#!/usr/bin/env python3
import subprocess
import sys


def main():
    print("--- Running Backend Port Contract Static Verification ---")
    files_to_check = [
        "apps/execution/domain/ports.py",
        "apps/execution/adapters/repositories.py",
        "apps/execution/infrastructure/repositories/execution_repositories.py",
        "apps/execution/application/ports.py",
    ]
    cmd = [
        "uv",
        "run",
        "mypy",
        *files_to_check,
        "--ignore-missing-imports",
        "--follow-imports=silent",
    ]
    print(f"Executing: {' '.join(cmd)}")
    result = subprocess.run(cmd, capture_output=True, text=True)

    if result.returncode == 0:
        print(
            "\033[92m✔ Static verification succeeded. Zero type contract failures found!\033[0m"
        )
        print(result.stdout)
        sys.exit(0)
    else:
        print(
            "\033[91m✘ Static verification failed! Signature mismatch or type error detected:\033[0m"
        )
        print(result.stdout)
        print(result.stderr)
        sys.exit(result.returncode)


if __name__ == "__main__":
    main()
