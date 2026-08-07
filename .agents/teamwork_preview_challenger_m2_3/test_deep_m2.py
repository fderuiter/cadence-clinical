"""
Deep Empirical Verification Script for Milestone M2 relocated domain models.
Tests instantiation, validation, serialization, deserialization, and import integrity.
"""

import importlib
import inspect
import os
import sys
import time
import uuid
from datetime import UTC, date, datetime
from enum import Enum
from typing import Any, Literal, Union, get_args, get_origin

from pydantic import BaseModel
from pydantic_core import PydanticUndefined

# Ensure required env vars for module import checks
os.environ["AUDIT_LOG_SECRET_KEY"] = "test_secret_key_12345678901234567890"
os.environ["INBOUND_EMAIL_HMAC_SECRET"] = "test_hmac_secret_12345678901234567890"
os.environ["GATEWAY_SECRET_KEY"] = "test_gateway_secret_12345678901234567890"

TARGET_MODULES = [
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


def generate_valid_value(field_name: str, type_hint: Any, depth: int = 0) -> Any:
    if depth > 5:
        return None

    origin = get_origin(type_hint)
    args = get_args(type_hint)

    # Handle Literal types
    if origin is Literal:
        return args[0]

    # Handle Optional / Union
    if origin is Union:
        non_none = [a for a in args if a is not type(None)]
        if non_none:
            return generate_valid_value(field_name, non_none[0], depth + 1)
        return None

    fn = field_name.lower()

    if fn == "raw_reference":
        return "eCRF.DM.AGE"
    if fn == "domain":
        return "DM"
    if fn == "variable":
        return "AGE"
    if "criterion_type" in fn:
        return "inclusion"
    if "reason" in fn:
        return "Standard GxP compliance reason for change verification."
    if "age_unit" in fn:
        return "YEARS"
    if "drug_role" in fn:
        return "SUSPECT"
    if "flag" in fn:
        return "Y"
    if (
        fn in ("id", "section_id")
        or fn.endswith("_id")
        or "uuid" in fn
        or fn in ("section_code", "zone_code", "code")
    ):
        if type_hint is str or type_hint is uuid.UUID or type_hint is str | None:
            return str(uuid.uuid4())
    if "date" in fn or "dtc" in fn or "timestamp" in fn or fn.endswith("_at"):
        if type_hint is datetime or type_hint is datetime | None:
            return datetime.now(UTC)
        if type_hint is date or type_hint is date | None:
            return date.today()
        if type_hint is str or type_hint is str | None:
            return "2026-08-07T12:00:00Z"
    if "sex" in fn:
        return "M"
    if "status" in fn:
        if type_hint is str or type_hint is str | None:
            return "DRAFT"
    if fn in ("type", "node_type"):
        if type_hint is str or type_hint is str | None:
            return "logical"
    if "severity" in fn or "aesev" in fn:
        return "MILD"
    if "seriousness" in fn or "aeser" in fn:
        return "Y"
    if fn in ("procedure_ids", "activity_ids", "visit_ids"):
        return ["test_id_1"]
    if fn in ("order", "document_count", "sequence"):
        return 1
    if fn in ("explanation", "title", "text", "name"):
        return "Empirical Test String"

    if type_hint is str:
        return "test_str"
    if type_hint is int:
        return 1
    if type_hint is float:
        return 1.0
    if type_hint is bool:
        return True
    if type_hint is datetime:
        return datetime.now(UTC)
    if type_hint is date:
        return date.today()
    if type_hint is uuid.UUID:
        return uuid.uuid4()

    if inspect.isclass(type_hint) and issubclass(type_hint, Enum):
        return list(type_hint)[0]

    if origin in (list, list):
        item_type = args[0] if args else str
        if inspect.isclass(item_type) and issubclass(item_type, BaseModel):
            val = build_model_instance(item_type, depth + 1)
            return [val]
        if fn in ("notes", "children", "subfolders"):
            return []
        val = generate_valid_value(field_name, item_type, depth + 1)
        return [val] if val is not None else []

    if origin in (dict, dict):
        key_type = args[0] if args else str
        val_type = args[1] if len(args) > 1 else str
        k = generate_valid_value("key", key_type, depth + 1)
        v = generate_valid_value("val", val_type, depth + 1)
        return {k: v} if k is not None else {}

    if inspect.isclass(type_hint) and issubclass(type_hint, BaseModel):
        return build_model_instance(type_hint, depth + 1)

    return "test_val"


def build_model_instance(model_cls: type[BaseModel], depth: int = 0) -> BaseModel:
    fields_data = {}
    for name, field in model_cls.model_fields.items():
        val = generate_valid_value(name, field.annotation, depth)
        if val is not None:
            fields_data[name] = val
        elif field.default is not PydanticUndefined and field.default is not None:
            fields_data[name] = field.default
        elif field.default_factory is not None:
            fields_data[name] = field.default_factory()

    return model_cls.model_validate(fields_data)


def run_empirical_verification():
    print("=======================================================================")
    print("EMPIRICAL DOMAIN MODEL VERIFICATION (M2)")
    print("=======================================================================")

    results = []
    total_models_tested = 0
    total_instantiated = 0
    total_serialized = 0
    total_deserialized = 0

    for mod_path in TARGET_MODULES:
        t0 = time.perf_counter()
        try:
            mod = importlib.import_module(mod_path)
            t_import = (time.perf_counter() - t0) * 1000
        except Exception as e:
            print(f"[FAIL IMPORT] {mod_path}: {e}")
            results.append(
                {"module": mod_path, "status": "FAIL_IMPORT", "error": str(e)}
            )
            continue

        mod_models = [
            (name, obj)
            for name, obj in inspect.getmembers(mod, inspect.isclass)
            if obj.__module__ == mod_path and issubclass(obj, BaseModel)
        ]

        print(
            f"\nModule: {mod_path} (Import time: {t_import:.2f}ms, Models: {len(mod_models)})"
        )

        mod_success = True
        for name, model_cls in mod_models:
            total_models_tested += 1
            try:
                # 1. Instantiation / Validation
                instance = build_model_instance(model_cls)
                total_instantiated += 1

                # 2. Serialization
                json_str = instance.model_dump_json()
                instance.model_dump()
                total_serialized += 1

                # 3. Deserialization
                model_cls.model_validate_json(json_str)
                total_deserialized += 1

                print(
                    f"  ✓ {name}: Passed lifecycle (instantiate, serialize, deserialize)"
                )
            except Exception as e:
                mod_success = False
                print(f"  ✗ {name}: Lifecycle error - {e}")
                results.append(
                    {
                        "module": mod_path,
                        "model": name,
                        "status": "FAIL_MODEL",
                        "error": str(e),
                    }
                )

        if mod_models or mod_path:
            if mod_success:
                results.append(
                    {
                        "module": mod_path,
                        "status": "PASS",
                        "models_count": len(mod_models),
                    }
                )

    print("\n=======================================================================")
    print("SUMMARY RESULTS")
    print(f"Total Relocated Pydantic Models Tested: {total_models_tested}")
    print(
        f"Successfully Instantiated:              {total_instantiated}/{total_models_tested}"
    )
    print(
        f"Successfully Serialized:                {total_serialized}/{total_models_tested}"
    )
    print(
        f"Successfully Deserialized:              {total_deserialized}/{total_models_tested}"
    )
    print("=======================================================================")

    failures = [r for r in results if r["status"] != "PASS"]
    if failures:
        print(f"\nFAILURES DETECTED ({len(failures)}):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    else:
        print("\nALL M2 RELOCATED DOMAIN MODELS PASSED EMPIRICAL VERIFICATION!")


if __name__ == "__main__":
    run_empirical_verification()
