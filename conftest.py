import os
import sys

# Ensure repo root is on sys.path early
repo_root = os.path.dirname(os.path.abspath(__file__))
if repo_root not in sys.path:
    sys.path.insert(0, repo_root)

# Re-export all fixtures and hooks from tests.conftest
from tests.conftest import *  # noqa: E402, F401, F403
