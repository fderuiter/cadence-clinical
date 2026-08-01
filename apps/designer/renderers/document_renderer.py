import importlib.util
import os
import sys

# Load the shared document_renderer.py module directly from packages/core-models to avoid name conflicts.
_current_dir = os.path.dirname(os.path.abspath(__file__))
_shared_path = os.path.abspath(
    os.path.join(
        _current_dir,
        "..",
        "..",
        "..",
        "packages",
        "core-models",
        "document_renderer.py",
    )
)

spec = importlib.util.spec_from_file_location("document_renderer_shared", _shared_path)
_shared_mod = importlib.util.module_from_spec(spec)
sys.modules["document_renderer_shared"] = _shared_mod
spec.loader.exec_module(_shared_mod)

ProtocolDocumentRenderer = _shared_mod.ProtocolDocumentRenderer
