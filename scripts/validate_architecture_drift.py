#!/usr/bin/env python3
"""
Automated Drift Gating Linter Script
Statically validates that all active local development services defined in
docker/docker-compose.yml are represented in the architecture diagrams of:
- ARCHITECTURE.md
- docs/SDLC/02_Technical_Design_Document_TDD.md
"""

import re
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
            "Please run: uv run python scripts/validate_architecture_drift.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime, print_runtime_info

# Paths to the target configuration and documentation files
COMPOSE_PATH = REPO_ROOT / "docker" / "docker-compose.yml"
ARCH_PATH = REPO_ROOT / "ARCHITECTURE.md"
TDD_PATH = REPO_ROOT / "docs" / "SDLC" / "02_Technical_Design_Document_TDD.md"


def get_active_services(compose_file: Path) -> list[str]:
    """Parses docker-compose.yml to extract active service keys."""
    services = []
    inside_services = False

    if not compose_file.exists():
        print(f"Error: Compose file not found at {compose_file}")
        sys.exit(1)

    with open(compose_file, encoding="utf-8") as f:
        for line in f:
            if line.startswith("services:"):
                inside_services = True
                continue
            if inside_services:
                # If we encounter a non-indented line, we left services block (e.g., volumes:)
                if line.strip() and not line.startswith(" "):
                    if line.split(":")[0].strip() in (
                        "volumes",
                        "networks",
                        "configs",
                        "secrets",
                    ):
                        inside_services = False
                        break
                # Service keys are indented by exactly two spaces and end with a colon
                match = re.match(r"^  ([a-zA-Z0-9_-]+):", line)
                if match:
                    services.append(match.group(1))

    return services


def extract_mermaid_blocks(file_path: Path) -> list[str]:
    """Extracts all Mermaid code blocks from a Markdown file."""
    if not file_path.exists():
        print(f"Error: Documentation file not found at {file_path}")
        sys.exit(1)

    with open(file_path, encoding="utf-8") as f:
        content = f.read()

    # Find all ```mermaid ... ``` blocks
    pattern = re.compile(r"```mermaid\s+(.*?)\s+```", re.DOTALL)
    return pattern.findall(content)


def validate_document(file_path: Path, active_services: list[str]) -> bool:
    """
    Validates that there is at least one Mermaid diagram in the file
    that represents all 16 active services.
    """
    mermaid_blocks = extract_mermaid_blocks(file_path)
    if not mermaid_blocks:
        print(f"[{file_path.name}] Failed: No Mermaid diagrams found.")
        return False

    for block_idx, block in enumerate(mermaid_blocks):
        missing_in_this_block = []
        for service in active_services:
            # Match service name with word boundaries
            pattern = re.compile(r"\b" + re.escape(service) + r"\b")
            if not pattern.search(block):
                missing_in_this_block.append(service)

        # If this diagram block contains all active services, the document is valid!
        if not missing_in_this_block:
            print(
                f"[{file_path.name}] Success: Found complete architecture diagram in block #{block_idx + 1}."
            )
            return True

    # If no diagram block contained all active services, report the closest block's omissions
    print(
        f"[{file_path.name}] Failed: No single Mermaid diagram contains all active services."
    )
    print("Active services to map:", active_services)
    for block_idx, block in enumerate(mermaid_blocks):
        missing = [
            s
            for s in active_services
            if not re.compile(r"\b" + re.escape(s) + r"\b").search(block)
        ]
        print(f"  - Diagram block #{block_idx + 1} is missing services: {missing}")

    return False


def validate_feature_matrix(matrix_path: Path, active_services: list[str]) -> bool:
    """
    Validates that every active user-facing microservice in docker-compose.yml
    has at least one corresponding entry mapping in docs/FEATURE_MATRIX.md.
    """
    if not matrix_path.exists():
        print(f"Error: Feature Matrix file not found at {matrix_path}")
        return False

    # Define mapping of service key in compose file to valid names/substrings in the "Sub-system" column
    service_to_doc_names = {
        "designer": ["Designer"],
        "execution": ["Execution"],
        "org": ["Organization Service"],
        "eisf": ["eISF Service"],
        "etmf": ["eTMF Service"],
        "ctms": ["CTMS Service"],
        "quality": ["Quality Service"],
        "interop": ["Interop Service"],
        "tickets": ["Tickets Service"],
        "safety": ["Clinical Safety"],
        "notifications": ["Notifications Service"],
        "econsent": ["Electronic Consent"],
        "subject-portal": ["Subject Portal"],
    }

    # Filter out active services to only include those that have an application folder
    # and are not excluded (e.g. gateway)
    repo_root = matrix_path.resolve().parent.parent
    services_to_check = []
    for s in active_services:
        apps_folder = repo_root / "apps" / s
        if apps_folder.is_dir() and s != "gateway":
            services_to_check.append(s)

    print(
        f"Filtered active services to validate in Feature Matrix: {services_to_check}"
    )

    # Read and parse docs/FEATURE_MATRIX.md
    with open(matrix_path, encoding="utf-8") as f:
        content = f.read()

    # Find Section 2: Clinical Entities Mapping table
    lines = content.splitlines()
    in_section_2 = False
    subsystems_found = set()

    for line in lines:
        if "## 2. Clinical Entities Mapping" in line:
            in_section_2 = True
            continue
        if in_section_2:
            if line.startswith("## ") or line.startswith("---"):
                if line.startswith("## "):
                    break
            stripped = line.strip()
            if stripped.startswith("|") and stripped.endswith("|"):
                parts = [p.strip() for p in stripped.split("|")]
                if len(parts) >= 3:
                    sub_system = parts[2]
                    # Skip header and separator rows
                    if (
                        sub_system
                        and "Sub-system" not in sub_system
                        and not all(c in "-: " for c in sub_system)
                    ):
                        subsystems_found.add(sub_system)

    print(
        f"Sub-systems found in Feature Matrix Clinical Entities table: {subsystems_found}"
    )

    missing_services = []
    for s in services_to_check:
        doc_names = service_to_doc_names.get(s, [])
        found = False
        for doc_name in doc_names:
            for subsystem in subsystems_found:
                if doc_name.lower() in subsystem.lower():
                    found = True
                    break
            if found:
                break
        if not found:
            missing_services.append(s)

    if missing_services:
        print(
            f"[FEATURE MATRIX] Failed: Active service(s) {missing_services} are missing from docs/FEATURE_MATRIX.md."
        )
        print(
            f"Please add appropriate Clinical Entity mappings for {missing_services} in docs/FEATURE_MATRIX.md."
        )
        return False

    print(
        "[FEATURE MATRIX] Success: All active services have corresponding mappings in the Feature Matrix."
    )
    return True


def main():
    print_runtime_info("validate_architecture_drift.py")
    print("Running Automated Architecture Drift Gating Linter...")

    # 1. Get the active local services from docker-compose
    active_services = get_active_services(COMPOSE_PATH)
    print(
        f"Detected {len(active_services)} active services in local configuration: {active_services}"
    )

    if not active_services:
        print("Error: No active services parsed from docker-compose.yml.")
        sys.exit(1)

    # 2. Validate ARCHITECTURE.md
    arch_valid = validate_document(ARCH_PATH, active_services)

    # 3. Validate 02_Technical_Design_Document_TDD.md
    tdd_valid = validate_document(TDD_PATH, active_services)

    # 4. Validate docs/FEATURE_MATRIX.md
    matrix_path = REPO_ROOT / "docs" / "FEATURE_MATRIX.md"
    matrix_valid = validate_feature_matrix(matrix_path, active_services)

    if not arch_valid or not tdd_valid or not matrix_valid:
        print(
            "\n[GATE KEEPER] Failure: Architectural or Feature Matrix documentation drift detected!"
        )
        print("Please ensure that your service topology Mermaid diagrams in both:")
        print("  - ARCHITECTURE.md")
        print("  - docs/SDLC/02_Technical_Design_Document_TDD.md")
        print(
            "accurately represent all active services in the docker-compose.yml orchestrator,"
        )
        print(
            "and that every active microservice has a corresponding mapping in docs/FEATURE_MATRIX.md."
        )
        sys.exit(1)

    print(
        "\n[GATE KEEPER] Success: No architectural or Feature Matrix documentation drift detected. All active services are mapped."
    )
    sys.exit(0)


if __name__ == "__main__":
    main()
