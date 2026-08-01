import importlib.util
import os
import sys

# Load the shared watermark.py module directly from packages/core-models to avoid name conflicts.
_current_dir = os.path.dirname(os.path.abspath(__file__))
_shared_path = os.path.abspath(
    os.path.join(_current_dir, "..", "..", "packages", "core-models", "watermark.py")
)

spec = importlib.util.spec_from_file_location("watermark_shared", _shared_path)
_shared_mod = importlib.util.module_from_spec(spec)
sys.modules["watermark_shared"] = _shared_mod
spec.loader.exec_module(_shared_mod)

apply_watermark = _shared_mod.apply_watermark
