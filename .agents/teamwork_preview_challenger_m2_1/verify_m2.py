"""Verification script for M2 Primary Services Domain Migration challenge."""

import ast
import importlib
import os
import sys

# Ensure root directory is on sys.path
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, PROJECT_ROOT)

# Set required environment variables for security/signature modules before importing
os.environ.setdefault("AUDIT_LOG_SECRET_KEY", "test-secret-key-1234567890-challenger")
os.environ.setdefault("GATEWAY_SECRET_KEY", "test-gateway-secret-key-challenger")
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-inbound-email-hmac-secret-challenger"
)

print(f"Project root: {PROJECT_ROOT}")

# 1. Test importing all 7 target modules requested in prompt
TARGET_MODULES = [
    "apps.designer.src.domain.cdisc.usdm_models",
    "apps.safety.src.domain.sae_icsr.models",
    "apps.ctms.src.domain.doa_models",
    "apps.etmf.src.domain.tmf_reference_model.models",
    "apps.notifications.src.domain.event_models",
    "apps.org.src.domain.models",
    "apps.interop.src.domain.sync_engine",
]

# Additional relocated M2 modules to test for completeness
ALL_M2_RELOCATED_MODULES = [
    "apps.designer.src.domain.cdisc.usdm_models",
    "apps.designer.src.domain.synopsis_transport_models",
    "apps.designer.src.domain.usdm_ingestion",
    "apps.designer.src.domain.protocol_authoring.models",
    "apps.designer.src.domain.protocol_render.models",
    "apps.designer.src.domain.protocol_version_ref.models",
    "apps.designer.src.domain.eligibility.models",
    "apps.designer.src.domain.document_renderer",
    "apps.safety.src.domain.sae_icsr.models",
    "apps.ctms.src.domain.doa_models",
    "apps.etmf.src.domain.etmf.eisf_models",
    "apps.etmf.src.domain.etmf.eisf_transport_models",
    "apps.etmf.src.domain.tmf_reference_model.models",
    "apps.notifications.src.domain.event_models",
    "apps.org.src.domain.models",
    "apps.interop.src.domain.sync_engine",
]

print("\n=== TEST 1: IMPORTING 7 TARGET RELOCATED DOMAIN MODULES ===")
import_errors = []
for mod_name in TARGET_MODULES:
    try:
        mod = importlib.import_module(mod_name)
        symbols = [s for s in dir(mod) if not s.startswith("_")]
        print(
            f"[PASS] Successfully imported {mod_name} ({len(symbols)} public symbols)"
        )
    except Exception as e:
        print(f"[FAIL] Error importing {mod_name}: {e}")
        import_errors.append((mod_name, str(e)))

print("\n=== TEST 1b: IMPORTING ALL OTHER M2 RELOCATED MODULES ===")
for mod_name in ALL_M2_RELOCATED_MODULES:
    if mod_name in TARGET_MODULES:
        continue
    try:
        mod = importlib.import_module(mod_name)
        symbols = [s for s in dir(mod) if not s.startswith("_")]
        print(
            f"[PASS] Successfully imported {mod_name} ({len(symbols)} public symbols)"
        )
    except Exception as e:
        print(f"[FAIL] Error importing {mod_name}: {e}")
        import_errors.append((mod_name, str(e)))

# 2. AST scanning for lingering imports of M2 relocated domain models from packages.core_models
M2_CORE_MODEL_SUBPATHS = [
    "packages.core_models.cdisc",
    "packages.core_models.designer",
    "packages.core_models.usdm_ingestion",
    "packages.core_models.protocol_authoring",
    "packages.core_models.protocol_render",
    "packages.core_models.protocol_version_ref",
    "packages.core_models.eligibility",
    "packages.core_models.document_renderer",
    "packages.core_models.sae_icsr",
    "packages.core_models.ctms",
    "packages.core_models.etmf",
    "packages.core_models.tmf_reference_model",
    "packages.core_models.notifications",
    "packages.core_models.organization_domain",
    "packages.core_models.sync_engine",
]

print("\n=== TEST 2: AST SCANNING FOR STALE M2 IMPORTS FROM PACKAGES.CORE_MODELS ===")

stale_imports = []


def check_file_ast(file_path):
    with open(file_path, encoding="utf-8") as f:
        try:
            tree = ast.parse(f.read(), filename=file_path)
        except SyntaxError as e:
            print(f"Syntax error parsing {file_path}: {e}")
            return

    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                for subpath in M2_CORE_MODEL_SUBPATHS:
                    if alias.name == subpath or alias.name.startswith(subpath + "."):
                        stale_imports.append((file_path, node.lineno, alias.name))
        elif isinstance(node, ast.ImportFrom):
            if node.module:
                for subpath in M2_CORE_MODEL_SUBPATHS:
                    if node.module == subpath or node.module.startswith(subpath + "."):
                        stale_imports.append(
                            (file_path, node.lineno, f"from {node.module} import ...")
                        )


search_dirs = [
    os.path.join(PROJECT_ROOT, "apps"),
    os.path.join(PROJECT_ROOT, "packages"),
    os.path.join(PROJECT_ROOT, "scripts"),
    os.path.join(PROJECT_ROOT, "tests"),
]

for sdir in search_dirs:
    for root, _, files in os.walk(sdir):
        for file in files:
            if file.endswith(".py"):
                full_path = os.path.join(root, file)
                check_file_ast(full_path)

if stale_imports:
    print(
        f"[FAIL] Found {len(stale_imports)} stale imports of M2 domain models from packages.core_models:"
    )
    for path, line, imp in stale_imports:
        print(f"  {os.path.relpath(path, PROJECT_ROOT)}:{line} -> {imp}")
else:
    print(
        "[PASS] Zero stale imports found for M2 relocated domain models from packages.core_models!"
    )

# Summary
print("\n=== SUMMARY OF VERIFICATION ===")
print(f"Import errors: {len(import_errors)}")
print(f"Stale imports: {len(stale_imports)}")
if import_errors or stale_imports:
    print("VERDICT: REQUEST_CHANGES")
    sys.exit(1)
else:
    print("VERDICT: APPROVE")
    sys.exit(0)
