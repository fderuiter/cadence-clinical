import importlib.util
import os
import sys

# Load the shared sync_engine.py module directly from packages/core-models to avoid name conflicts.
_current_dir = os.path.dirname(os.path.abspath(__file__))
_shared_path = os.path.abspath(
    os.path.join(
        _current_dir, "..", "..", "packages", "core-models", "sync_engine.py"
    )
)

spec = importlib.util.spec_from_file_location("sync_engine_shared", _shared_path)
_shared_mod = importlib.util.module_from_spec(spec)
sys.modules["sync_engine_shared"] = _shared_mod
spec.loader.exec_module(_shared_mod)

SignatureValidationError = _shared_mod.SignatureValidationError
SyncMetadata = _shared_mod.SyncMetadata
SyncRecord = _shared_mod.SyncRecord
normalize_to_utc = _shared_mod.normalize_to_utc
get_signature_payload = _shared_mod.get_signature_payload
verify_record_signature = _shared_mod.verify_record_signature
reconcile_records = _shared_mod.reconcile_records
