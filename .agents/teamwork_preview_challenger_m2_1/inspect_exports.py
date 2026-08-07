"""Print exported classes for the 7 target modules."""

import importlib
import os
import sys

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, PROJECT_ROOT)

os.environ.setdefault("AUDIT_LOG_SECRET_KEY", "test-secret-key-1234567890-challenger")
os.environ.setdefault("GATEWAY_SECRET_KEY", "test-gateway-secret-key-challenger")
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-inbound-email-hmac-secret-challenger"
)

TARGET_MODULES = [
    "apps.designer.src.domain.cdisc.usdm_models",
    "apps.safety.src.domain.sae_icsr.models",
    "apps.ctms.src.domain.doa_models",
    "apps.etmf.src.domain.tmf_reference_model.models",
    "apps.notifications.src.domain.event_models",
    "apps.org.src.domain.models",
    "apps.interop.src.domain.sync_engine",
]

for mod_name in TARGET_MODULES:
    mod = importlib.import_module(mod_name)
    print(f"\nModule: {mod_name}")
    for item in dir(mod):
        if not item.startswith("_"):
            obj = getattr(mod, item)
            if isinstance(obj, type):
                print(f"  Class: {item}")
