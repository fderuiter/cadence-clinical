"""
Empirical verification script for M2 Domain Model Migration.

Tests:
1. Module import integrity and circular dependency check.
2. Model discovery, instantiation, validation, and JSON serialization/deserialization.
3. Module load performance benchmarking.
"""

import importlib
import inspect
import time
from typing import Any

from pydantic import BaseModel

MODULES_TO_TEST = [
    # CTMS
    "apps.ctms.src.domain.doa_models",
    "apps.ctms.src.domain.doa_transport_models",
    # Designer
    "apps.designer.src.domain.cdisc.branch_models",
    "apps.designer.src.domain.cdisc.cascade_models",
    "apps.designer.src.domain.cdisc.cdisc_library_client",
    "apps.designer.src.domain.cdisc.sentinel_models",
    "apps.designer.src.domain.cdisc.terminology_cache",
    "apps.designer.src.domain.cdisc.usdm_importer",
    "apps.designer.src.domain.cdisc.usdm_models",
    "apps.designer.src.domain.cdisc.usdm_transport_models",
    "apps.designer.src.domain.document_renderer",
    "apps.designer.src.domain.eligibility.evaluator",
    "apps.designer.src.domain.eligibility.models",
    "apps.designer.src.domain.eligibility.parser",
    "apps.designer.src.domain.protocol_authoring.models",
    "apps.designer.src.domain.protocol_authoring.soa",
    "apps.designer.src.domain.protocol_render.models",
    "apps.designer.src.domain.protocol_version_ref.models",
    "apps.designer.src.domain.synopsis_transport_models",
    "apps.designer.src.domain.usdm_ingestion",
    # eTMF
    "apps.etmf.src.domain.etmf.eisf_models",
    "apps.etmf.src.domain.etmf.eisf_transport_models",
    "apps.etmf.src.domain.tmf_reference_model.models",
    # Interop
    "apps.interop.src.domain.sync_engine",
    # Notifications
    "apps.notifications.src.domain.event_models",
    # Org
    "apps.org.src.domain.models",
    # Safety
    "apps.safety.src.domain.sae_icsr.models",
]


def test_imports_and_load_times() -> dict[str, float]:
    print("=== TASK 1: IMPORT INTEGRITY AND LOAD TIMES ===")
    load_times = {}
    failed_imports = []

    for mod_name in MODULES_TO_TEST:
        t0 = time.perf_counter()
        try:
            _ = importlib.import_module(mod_name)
            t1 = time.perf_counter()
            elapsed_ms = (t1 - t0) * 1000
            load_times[mod_name] = elapsed_ms
            print(f"  [PASS] {mod_name} loaded in {elapsed_ms:.2f} ms")
        except Exception as e:
            print(f"  [FAIL] {mod_name}: {e}")
            failed_imports.append((mod_name, str(e)))

    if failed_imports:
        print(f"\nFAILED IMPORTS ({len(failed_imports)}):")
        for m, err in failed_imports:
            print(f"  - {m}: {err}")
    else:
        print("\nAll modules imported successfully without errors!")
    return load_times


def generate_default_value(annotation: Any) -> Any:
    """Generate a placeholder default value for field types."""
    from typing import get_args, get_origin

    origin = get_origin(annotation)
    args = get_args(annotation)

    if annotation is str:
        return "test_string"
    if annotation is int:
        return 1
    if annotation is float:
        return 1.0
    if annotation is bool:
        return True
    if origin is list or annotation is list:
        return []
    if origin is dict or annotation is dict:
        return {}
    if origin is set or annotation is set:
        return set()
    if origin is type(None):
        return None
    if args:
        # e.g., Optional[X] or Union[X, Y]
        non_none = [a for a in args if a is not type(None)]
        if non_none:
            return generate_default_value(non_none[0])
        return None

    # Try calling constructor if possible
    try:
        return annotation()
    except Exception:
        return "test_val"


def instantiate_model(model_cls: type) -> Any:
    """Attempt to instantiate a Pydantic model with minimal default/required values."""
    if not issubclass(model_cls, BaseModel):
        return None

    kwargs = {}
    for name, field_info in model_cls.model_fields.items():
        if field_info.is_required():
            val = generate_default_value(field_info.annotation)
            kwargs[name] = val

    return model_cls(**kwargs)


def test_model_lifecycle():
    print("\n=== TASK 1 (Cont): MODEL INSTANTIATION, VALIDATION & SERIALIZATION ===")
    total_models = 0
    passed_instantiations = 0
    passed_serializations = 0
    passed_deserializations = 0
    errors = []

    for mod_name in MODULES_TO_TEST:
        try:
            mod = importlib.import_module(mod_name)
        except Exception:
            continue

        for attr_name in dir(mod):
            obj = getattr(mod, attr_name)
            if (
                inspect.isclass(obj)
                and obj.__module__ == mod_name
                and issubclass(obj, BaseModel)
            ):
                total_models += 1
                try:
                    # Instantiation
                    inst = instantiate_model(obj)
                    passed_instantiations += 1

                    # Serialization
                    json_str = inst.model_dump_json()
                    _ = inst.model_dump()
                    passed_serializations += 1

                    # Deserialization
                    _ = obj.model_validate_json(json_str)
                    passed_deserializations += 1

                except Exception as e:
                    print(f"  [ERROR] {mod_name}.{attr_name}: {e}")
                    errors.append((f"{mod_name}.{attr_name}", str(e)))

    print("\nModel Lifecycle Summary:")
    print(f"  Total Pydantic Models Found: {total_models}")
    print(f"  Instantiated: {passed_instantiations}/{total_models}")
    print(f"  Serialized:   {passed_serializations}/{total_models}")
    print(f"  Deserialized: {passed_deserializations}/{total_models}")
    if errors:
        print(f"  Errors ({len(errors)}):")
        for m, err in errors:
            print(f"    - {m}: {err}")
    else:
        print(
            "  [SUCCESS] All discovered Pydantic domain models can be instantiated, serialized, and deserialized cleanly!"
        )


if __name__ == "__main__":
    load_times = test_imports_and_load_times()
    test_model_lifecycle()
