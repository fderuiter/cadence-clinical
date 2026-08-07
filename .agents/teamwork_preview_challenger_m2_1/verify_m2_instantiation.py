"""Comprehensive Verification and Instantiation Harness for M2 Challenge."""

import importlib
import os
import sys
from datetime import UTC, datetime

PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, PROJECT_ROOT)

os.environ.setdefault("AUDIT_LOG_SECRET_KEY", "test-secret-key-1234567890-challenger")
os.environ.setdefault("GATEWAY_SECRET_KEY", "test-gateway-secret-key-challenger")
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-inbound-email-hmac-secret-challenger"
)

print("=== DEEP VERIFICATION & MODEL INSTANTIATION HARNESS ===")

errors = []

# 1. Designer USDM Models
try:
    usdm = importlib.import_module("apps.designer.src.domain.cdisc.usdm_models")
    study = usdm.USDMStudy(
        id="usdm-study-001",
        name="Phase III USDM Study",
        protocolTitle="Phase III Protocol",
        description="USDM study description",
    )
    if study.id != "usdm-study-001":
        raise ValueError("USDMStudy id mismatch")
    print(
        "[PASS] apps.designer.src.domain.cdisc.usdm_models: USDMStudy instantiated successfully"
    )
except Exception as e:
    print(f"[FAIL] apps.designer.src.domain.cdisc.usdm_models instantiation error: {e}")
    errors.append(f"usdm_models: {e}")

# 2. Safety SAE ICSR Models
try:
    sae_mod = importlib.import_module("apps.safety.src.domain.sae_icsr.models")
    sae_evt = sae_mod.SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="Anaphylaxis",
        AESTDTC="2026-08-01",
        AESEV="SEVERE",
        AESER=True,
        reason_for_change="Initial report",
    )
    if sae_evt.subject_key != "SUBJ-001":
        raise ValueError("SeriousAdverseEvent subject_key mismatch")
    print(
        "[PASS] apps.safety.src.domain.sae_icsr.models: SeriousAdverseEvent instantiated successfully"
    )
except Exception as e:
    print(f"[FAIL] apps.safety.src.domain.sae_icsr.models instantiation error: {e}")
    errors.append(f"sae_icsr: {e}")

# 3. CTMS DOA Models
try:
    doa_mod = importlib.import_module("apps.ctms.src.domain.doa_models")
    rec = doa_mod.DOADelegationRecordCreate(
        id="doa-001",
        site_id="site-101",
        staff_id="staff-001",
        staff_user_id="user-100",
        study_role="SUB_INVESTIGATOR",
        task_code="ICF",
        delegated_tasks=["informed_consent"],
        created_by="admin",
        reason_for_change="Initial onboarding",
        start_date=datetime.now(UTC).date(),
    )
    if rec.site_id != "site-101":
        raise ValueError("DOADelegationRecordCreate site_id mismatch")
    print(
        "[PASS] apps.ctms.src.domain.doa_models: DOADelegationRecordCreate instantiated successfully"
    )
except Exception as e:
    print(f"[FAIL] apps.ctms.src.domain.doa_models instantiation error: {e}")
    errors.append(f"doa_models: {e}")

# 4. eTMF TMF Reference Model
try:
    tmf_mod = importlib.import_module("apps.etmf.src.domain.tmf_reference_model.models")
    artifact = tmf_mod.Artifact(
        code="01.01.01",
        name="Charter",
        section_code="01.01",
        zone_code="01",
    )
    if artifact.code != "01.01.01":
        raise ValueError("Artifact code mismatch")
    print(
        "[PASS] apps.etmf.src.domain.tmf_reference_model.models: Artifact instantiated successfully"
    )
except Exception as e:
    print(
        f"[FAIL] apps.etmf.src.domain.tmf_reference_model.models instantiation error: {e}"
    )
    errors.append(f"tmf_reference_model: {e}")

# 5. Notifications Event Models
try:
    evt_mod = importlib.import_module("apps.notifications.src.domain.event_models")
    sys_evt = evt_mod.SystemDomainEvent(
        event_id="evt-999",
        event_type="SAE_REPORTED",
        source_service="safety",
        study_id="study-001",
        timestamp_utc=datetime.now(UTC).isoformat(),
        payload={"sae_id": "sae-2026-001"},
    )
    if sys_evt.event_id != "evt-999":
        raise ValueError("SystemDomainEvent event_id mismatch")
    print(
        "[PASS] apps.notifications.src.domain.event_models: SystemDomainEvent instantiated successfully"
    )
except Exception as e:
    print(f"[FAIL] apps.notifications.src.domain.event_models instantiation error: {e}")
    errors.append(f"event_models: {e}")

# 6. Org Models
try:
    org_mod = importlib.import_module("apps.org.src.domain.models")
    org_type = org_mod.OrganizationType.SPONSOR
    if org_type.value != "sponsor":
        raise ValueError("OrganizationType enum value mismatch")
    print(
        "[PASS] apps.org.src.domain.models: OrganizationType enum validated successfully"
    )
except Exception as e:
    print(f"[FAIL] apps.org.src.domain.models instantiation error: {e}")
    errors.append(f"org_models: {e}")

# 7. Interop Sync Engine
try:
    sync_mod = importlib.import_module("apps.interop.src.domain.sync_engine")
    record = sync_mod.SyncRecord(
        deduplication_key="subj-001:vitals-01",
        data={"systolic": 120, "diastolic": 80},
        metadata=sync_mod.SyncMetadata(
            timestamps={"systolic": datetime.now(UTC)},
            modified_by="device-bp-01",
        ),
    )
    if record.deduplication_key != "subj-001:vitals-01":
        raise ValueError("SyncRecord deduplication_key mismatch")
    print(
        "[PASS] apps.interop.src.domain.sync_engine: SyncRecord instantiated successfully"
    )
except Exception as e:
    print(f"[FAIL] apps.interop.src.domain.sync_engine instantiation error: {e}")
    errors.append(f"sync_engine: {e}")

print(f"\nInstantiation error count: {len(errors)}")
if errors:
    sys.exit(1)
sys.exit(0)
