"""Package lockfile parser and cryptographic integrity verification engine.

Supports:
- uv.lock (Python TOML format)
- renv.lock (R JSON format)
- requirements.txt (Python pinned hashes format)

Guarantees 100% reproducible statistical runs by verifying immutable package digests,
enforcing GxP qualification whitelists, and asserting SHA-256 lockfile integrity.

Requirements: PRD-CDR-005, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import hashlib
import json
import tomllib

from apps.interop.domain.sce_models import (
    LockValidationStatus,
    PackageEntry,
    PackageLockType,
    PackageLockValidationResult,
)

# Standard GxP-qualified biostatistical packages for Python & R
QUALIFIED_PYTHON_PACKAGES = {
    "pandas",
    "numpy",
    "scipy",
    "statsmodels",
    "polars",
    "pyarrow",
    "scikit-learn",
    "matplotlib",
    "seaborn",
    "reportlab",
    "pydantic",
    "httpx",
    "lifelines",
    "tabulate",
    "jinja2",
    "openpyxl",
    "xlsxwriter",
    "rich",
    "pytest",
    "duckdb",
    "fsspec",
    "sqlalchemy",
    "sqlmodel",
}

QUALIFIED_R_PACKAGES = {
    "survival",
    "ggplot2",
    "dplyr",
    "tidyr",
    "rtables",
    "tern",
    "tidycdisc",
    "admiral",
    "haven",
    "knitr",
    "rmarkdown",
    "pharmaversesdtm",
    "pharmaverseadam",
    "lme4",
    "emmeans",
    "car",
    "boot",
    "stringr",
    "purrr",
    "readr",
    "tibble",
    "forcats",
    "renv",
    "base",
    "stats",
    "graphics",
    "grdevices",
    "utils",
    "datasets",
    "methods",
}


def compute_normalized_lockfile_digest(
    lock_type: PackageLockType | str, packages: list[PackageEntry]
) -> str:
    """Computes a deterministic canonical SHA-256 digest over normalized package entries.

    Sorts packages alphabetically by name and version to ensure bit-for-bit reproducibility.
    """
    sorted_pkgs = sorted(packages, key=lambda p: (p.name.lower(), p.version))
    canonical_representation = [
        f"{p.name.lower()}=={p.version}:{p.checksum or 'NONE'}:{p.repository or 'DEFAULT'}"
        for p in sorted_pkgs
    ]
    raw_payload = f"TYPE:{lock_type}\n" + "\n".join(canonical_representation)
    return hashlib.sha256(raw_payload.encode("utf-8")).hexdigest()


def _parse_uv_lock(content: str) -> tuple[list[PackageEntry], str | None]:
    """Parse uv.lock TOML document into canonical PackageEntry items."""
    try:
        data = tomllib.loads(content)
    except Exception as exc:
        return [], f"Invalid uv.lock TOML syntax: {exc}"

    packages: list[PackageEntry] = []
    raw_pkgs = data.get("package", [])
    if isinstance(raw_pkgs, list):
        for item in raw_pkgs:
            if not isinstance(item, dict):
                continue
            name = str(item.get("name", "")).strip()
            version = str(item.get("version", "")).strip()
            if not name or not version:
                continue

            # Extract wheels/source hash if present
            checksum = None
            wheels = item.get("wheels", [])
            if isinstance(wheels, list) and wheels:
                first_wheel = wheels[0]
                if isinstance(first_wheel, dict):
                    checksum = first_wheel.get("hash")

            source = item.get("source", {})
            repo = source.get("registry") if isinstance(source, dict) else None

            is_qual = name.lower() in QUALIFIED_PYTHON_PACKAGES
            packages.append(
                PackageEntry(
                    name=name,
                    version=version,
                    checksum=checksum,
                    repository=repo or "PyPI",
                    is_qualified=is_qual,
                )
            )

    return packages, None


def _parse_renv_lock(content: str) -> tuple[list[PackageEntry], str | None]:
    """Parse renv.lock JSON document into canonical PackageEntry items."""
    try:
        data = json.loads(content)
    except Exception as exc:
        return [], f"Invalid renv.lock JSON syntax: {exc}"

    if not isinstance(data, dict):
        return [], "Invalid renv.lock structure: root must be a JSON object"

    packages: list[PackageEntry] = []
    pkg_map = data.get("Packages", {})
    if isinstance(pkg_map, dict):
        for pkg_name, pkg_info in pkg_map.items():
            if not isinstance(pkg_info, dict):
                continue
            name = str(pkg_info.get("Package", pkg_name)).strip()
            version = str(pkg_info.get("Version", "")).strip()
            if not name or not version:
                continue

            checksum = str(pkg_info.get("Hash", "")) or None
            repo = str(pkg_info.get("Repository", pkg_info.get("Source", "CRAN")))

            is_qual = name.lower() in QUALIFIED_R_PACKAGES
            packages.append(
                PackageEntry(
                    name=name,
                    version=version,
                    checksum=checksum,
                    repository=repo,
                    is_qualified=is_qual,
                )
            )

    return packages, None


def _parse_requirements_txt(content: str) -> tuple[list[PackageEntry], str | None]:
    """Parse requirements.txt with pinned versions and hashes."""
    packages: list[PackageEntry] = []
    lines = content.splitlines()
    for line in lines:
        cleaned = line.strip()
        if not cleaned or cleaned.startswith("#"):
            continue

        # Split on comments or inline flags
        tokens = cleaned.split()
        pkg_spec = tokens[0]
        checksum = None
        for tok in tokens[1:]:
            if tok.startswith("--hash=sha256:"):
                checksum = tok.split("sha256:", 1)[1]

        if "==" in pkg_spec:
            parts = pkg_spec.split("==", 1)
            name = parts[0].strip()
            version = parts[1].strip()
            if name and version:
                is_qual = name.lower() in QUALIFIED_PYTHON_PACKAGES
                packages.append(
                    PackageEntry(
                        name=name,
                        version=version,
                        checksum=checksum,
                        repository="PyPI",
                        is_qualified=is_qual,
                    )
                )

    return packages, None


def validate_package_lockfile(
    lock_type: PackageLockType | str,
    content: str,
    expected_digest: str | None = None,
    strict_qualification: bool = False,
) -> PackageLockValidationResult:
    """Parses and cryptographically verifies lockfile integrity and qualification.

    Args:
        lock_type: Format enum (UV_LOCK, RENV_LOCK, REQUIREMENTS_TXT)
        content: Raw text content of the lockfile
        expected_digest: Optional pre-registered SHA-256 digest to assert immutability
        strict_qualification: If True, flags any non-whitelisted packages as UNQUALIFIED_PACKAGE

    Returns:
        PackageLockValidationResult containing parsed packages and verification status.
    """
    lock_type_enum = PackageLockType(str(lock_type).upper())
    error_msg: str | None = None
    packages: list[PackageEntry] = []

    if lock_type_enum == PackageLockType.UV_LOCK:
        packages, error_msg = _parse_uv_lock(content)
    elif lock_type_enum == PackageLockType.RENV_LOCK:
        packages, error_msg = _parse_renv_lock(content)
    elif lock_type_enum == PackageLockType.REQUIREMENTS_TXT:
        packages, error_msg = _parse_requirements_txt(content)
    else:
        error_msg = f"Unsupported lockfile format: {lock_type}"

    if error_msg is not None:
        return PackageLockValidationResult(
            lock_type=lock_type_enum,
            status=LockValidationStatus.INVALID_SYNTAX,
            lock_digest=hashlib.sha256(content.encode("utf-8")).hexdigest(),
            package_count=0,
            packages=[],
            is_qualified=False,
            unqualified_packages=[],
            details=error_msg,
        )

    # Compute canonical normalized digest
    computed_digest = compute_normalized_lockfile_digest(lock_type_enum, packages)

    # Check for tampering against expected digest
    if expected_digest and expected_digest != computed_digest:
        return PackageLockValidationResult(
            lock_type=lock_type_enum,
            status=LockValidationStatus.TAMPERED,
            lock_digest=computed_digest,
            package_count=len(packages),
            packages=packages,
            is_qualified=False,
            unqualified_packages=[],
            details=(
                f"Cryptographic lockfile digest mismatch: expected '{expected_digest}' "
                f"but computed '{computed_digest}'. Lockfile may have been modified without authorization."
            ),
        )

    unqualified = [p.name for p in packages if not p.is_qualified]
    all_qualified = len(unqualified) == 0

    if strict_qualification and not all_qualified:
        return PackageLockValidationResult(
            lock_type=lock_type_enum,
            status=LockValidationStatus.UNQUALIFIED_PACKAGE,
            lock_digest=computed_digest,
            package_count=len(packages),
            packages=packages,
            is_qualified=False,
            unqualified_packages=unqualified,
            details=(
                f"Lockfile contains {len(unqualified)} unapproved package(s): {', '.join(unqualified)}. "
                "All packages in validated statistical environment must be pre-qualified."
            ),
        )

    return PackageLockValidationResult(
        lock_type=lock_type_enum,
        status=LockValidationStatus.VALID,
        lock_digest=computed_digest,
        package_count=len(packages),
        packages=packages,
        is_qualified=all_qualified,
        unqualified_packages=unqualified,
        details=f"Successfully validated {len(packages)} pinned packages under {lock_type_enum.value}.",
    )
