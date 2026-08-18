#!/usr/bin/env python3
"""GxP Electronic Signature Verification Tool — Cadence Clinical.

Verifies the cryptographic authenticity, certificate binding, and tamper-resistance
of signed GxP Markdown compliance documents in compliance with 21 CFR Part 11.

Requirements: PRD-SYS-001
"""

import argparse
import os
import sys
import time
from pathlib import Path

os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY", "internal-audit-key-for-gxp-sync"
)  # pragma: allowlist secret
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "internal-email-hmac-secret-12345"
)  # pragma: allowlist secret

# Add repository root to sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

# Enforce Python 3.14+ runtime
if sys.version_info < (3, 14):
    try:
        from scripts.runtime_guard import enforce_python_runtime

        enforce_python_runtime()
    except Exception:
        sys.stderr.write(
            f"[FATAL] Incompatible Python runtime {sys.version.split()[0]}.\n"
            "Cadence Clinical requires Python 3.14+.\n"
        )
        sys.exit(1)

from packages.compliance.services.esignature_verifier import ESignatureVerifier
from scripts.runtime_guard import print_runtime_info


def verify_gxp_signatures(target_path: Path) -> bool:
    """Scan and verify all signed Markdown files under target_path.

    Args:
        target_path: Path to file or directory to scan.

    Returns:
        True if all signed Markdown files pass verification; False otherwise.
    """
    start_time = time.perf_counter()
    verifier = ESignatureVerifier()

    files_to_check: list[Path] = []
    if target_path.is_file():
        if target_path.suffix.lower() == ".md":
            files_to_check.append(target_path)
    elif target_path.is_dir():
        files_to_check.extend(sorted(target_path.rglob("*.md")))

    if not files_to_check:
        print(f"No Markdown files found under '{target_path}'.")
        return True

    signed_files_count = 0
    failed_files_count = 0

    print(
        f"Scanning {len(files_to_check)} Markdown files for electronic signatures...\n"
    )

    for file_path in files_to_check:
        try:
            content_bytes = file_path.read_bytes()
        except Exception as exc:
            print(f"✘ {file_path}: ERROR reading file: {exc}")
            failed_files_count += 1
            continue

        # Check if file has embedded signature markers
        if (
            b"-----BEGIN CERTIFICATE-----" in content_bytes
            and b"-----BEGIN SIGNATURE-----" in content_bytes
        ):
            signed_files_count += 1
            rel_path = (
                file_path.relative_to(REPO_ROOT)
                if file_path.is_relative_to(REPO_ROOT)
                else file_path
            )

            result = verifier.verify_markdown(content_bytes)
            if result.is_valid:
                print(f"✔ [VALID] {rel_path} — Electronic Signature Verified")
            else:
                print(
                    f"✘ [TAMPER DETECTED / INVALID] {rel_path}\n"
                    f"  Status: {result.status}\n"
                    f"  Reason: {result.failure_reason}"
                )
                failed_files_count += 1

    elapsed = time.perf_counter() - start_time
    print(
        f"\nVerification summary: {signed_files_count} signed files checked "
        f"({failed_files_count} failed) in {elapsed:.3f}s."
    )

    if elapsed > 5.0:
        print(
            f"⚠ WARNING: Signature verification elapsed time ({elapsed:.3f}s) exceeded 5s threshold!"
        )

    return failed_files_count == 0


def main() -> None:
    """CLI entry point for GxP electronic signature verification."""
    parser = argparse.ArgumentParser(
        description="Verify cryptographic signatures and integrity of GxP Markdown reports."
    )
    parser.add_argument(
        "target",
        nargs="?",
        default="docs/SDLC",
        help="Markdown file or directory to scan and verify (default: docs/SDLC)",
    )
    args = parser.parse_args()

    print_runtime_info("verify_gxp_signatures.py")
    print("Cadence Clinical — GxP Electronic Signature Verifier")
    print("=" * 60)

    target_path = (
        REPO_ROOT / args.target
        if not Path(args.target).is_absolute()
        else Path(args.target)
    )
    if not target_path.exists():
        print(f"ERROR: Target path '{target_path}' does not exist.", file=sys.stderr)
        sys.exit(1)

    all_valid = verify_gxp_signatures(target_path)
    if not all_valid:
        print("\n✘ GxP Electronic Signature Verification Failed!", file=sys.stderr)
        sys.exit(1)

    print("\n✔ All GxP Electronic Signatures Passed Verification.")


if __name__ == "__main__":
    main()
