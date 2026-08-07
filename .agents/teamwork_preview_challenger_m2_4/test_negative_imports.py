import importlib
import os
import sys

# Provide mock secret keys for modules requiring environment config
os.environ["AUDIT_LOG_SECRET_KEY"] = "test_secret_key_for_verification"
os.environ["INBOUND_EMAIL_HMAC_SECRET"] = "test_hmac_secret_for_verification"


def test_legacy_and_new_imports():
    # 1. Legacy import paths that MUST raise ModuleNotFoundError
    legacy_modules = [
        "packages.core_models.usdm",
        "packages.core_models.protocol_authoring",
        "packages.core_models.protocol_render",
        "packages.core_models.protocol_version_ref",
        "packages.core_models.eligibility",
        "packages.core_models.usdm_ingestion",
        "packages.core_models.document_renderer",
        "packages.core_models.sae_icsr",
        "packages.core_models.icsr",
        "packages.core_models.ctms",
        "packages.core_models.etmf",
        "packages.core_models.notifications",
        "packages.core_models.organization_domain",
        "packages.core_models.sync_engine",
    ]

    print("--- 1. Testing Legacy Import Failure (ModuleNotFoundError) ---")
    legacy_results = []
    for mod in legacy_modules:
        try:
            importlib.import_module(mod)
            print(f"❌ FAIL: {mod} was imported without error!")
            legacy_results.append((mod, False, "No exception raised"))
        except ModuleNotFoundError as e:
            print(f"✔ PASS: {mod} correctly raised ModuleNotFoundError: {e}")
            legacy_results.append((mod, True, str(e)))
        except Exception as e:
            print(f"⚠️ FAIL: {mod} raised unexpected exception: {type(e).__name__}: {e}")
            legacy_results.append(
                (mod, False, f"Unexpected exception: {type(e).__name__}: {e}")
            )

    # 2. Relocated domain model paths in apps/<service>/src/domain/...
    new_modules = [
        "apps.designer.src.domain.cdisc",
        "apps.designer.src.domain.document_renderer",
        "apps.designer.src.domain.eligibility",
        "apps.designer.src.domain.protocol_authoring",
        "apps.designer.src.domain.protocol_render",
        "apps.designer.src.domain.protocol_version_ref",
        "apps.designer.src.domain.usdm_ingestion",
        "apps.ctms.src.domain.doa_models",
        "apps.ctms.src.domain.doa_transport_models",
        "apps.etmf.src.domain.etmf",
        "apps.etmf.src.domain.tmf_reference_model",
        "apps.interop.src.domain.sync_engine",
        "apps.notifications.src.domain.event_models",
        "apps.org.src.domain.models",
        "apps.safety.src.domain.sae_icsr",
    ]

    print("\n--- 2. Testing New Relocated Import Success ---")
    new_results = []
    for mod in new_modules:
        try:
            m = importlib.import_module(mod)
            print(f"✔ PASS: {mod} successfully imported! Module: {m}")
            new_results.append((mod, True, "Success"))
        except Exception as e:
            print(
                f"❌ FAIL: Could not import new module {mod}: {type(e).__name__}: {e}"
            )
            new_results.append((mod, False, f"{type(e).__name__}: {e}"))

    failed_legacy = [r for r in legacy_results if not r[1]]
    failed_new = [r for r in new_results if not r[1]]

    if failed_legacy or failed_new:
        print(
            f"\nSummary: VERIFICATION FAILED ({len(failed_legacy)} legacy failed, {len(failed_new)} new failed)"
        )
        sys.exit(1)
    else:
        print("\nSummary: ALL NEGATIVE AND NEW IMPORT TESTS PASSED CLEANLY!")
        sys.exit(0)


if __name__ == "__main__":
    test_legacy_and_new_imports()
