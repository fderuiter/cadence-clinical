import os
import sys

# Inject 'core-models' path into sys.path to allow importing modules directly
_core_models_path = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "core-models")
)
if _core_models_path not in sys.path:
    sys.path.insert(0, _core_models_path)
