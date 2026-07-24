"""
Shared packages root initializer.
Injects the packages/core-models directory into sys.path to resolve hyphen-related
import restrictions seamlessly across Python modules.
"""

import os
import sys

_core_models_path = os.path.join(os.path.dirname(__file__), "core-models")
if os.path.exists(_core_models_path) and _core_models_path not in sys.path:
    sys.path.insert(0, _core_models_path)
