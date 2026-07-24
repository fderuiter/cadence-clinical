"""
Main entry point for packages workspace.
Dynamically injects hyphenated directories such as packages/core-models into sys.path.
"""

import os
import sys

# Inject core-models into sys.path to allow direct imports of tmf_reference_model
_core_models_dir = os.path.join(os.path.dirname(__file__), "core-models")
if os.path.exists(_core_models_dir) and _core_models_dir not in sys.path:
    sys.path.insert(0, _core_models_dir)
