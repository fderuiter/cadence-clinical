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

sae_mod = importlib.import_module("apps.safety.src.domain.sae_icsr.models")
print(
    "SeriousAdverseEvent fields:", list(sae_mod.SeriousAdverseEvent.model_fields.keys())
)

tmf_mod = importlib.import_module("apps.etmf.src.domain.tmf_reference_model.models")
print("Artifact fields:", list(tmf_mod.Artifact.model_fields.keys()))

evt_mod = importlib.import_module("apps.notifications.src.domain.event_models")
print("SystemDomainEvent fields:", list(evt_mod.SystemDomainEvent.model_fields.keys()))

org_mod = importlib.import_module("apps.org.src.domain.models")
print("Org mod items:", [x for x in dir(org_mod) if not x.startswith("_")])
