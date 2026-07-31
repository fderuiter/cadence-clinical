import hashlib
import hmac
import json
import os
import random
import time
import uuid

from locust import HttpUser, between, task

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)


def generate_gateway_signature(
    user_id: str,
    roles: str,
    timestamp: str,
    secret: bytes,
    change_reason: str = "",
    site_id: str = "",
    sponsor_id: str = "",
    unblinded_access: bool = False,
    tenant_id: str = "tenant_default",
    sig_token: str = None,
) -> str:
    """Helper to generate internal gateway HMAC signatures matching packages/security/signing.py"""
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
        "site_id": site_id,
        "sponsor_id": sponsor_id,
        "unblinded_access": unblinded_access,
        "tenant_id": tenant_id,
    }
    if sig_token is not None:
        payload["sig_token"] = sig_token
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    return hmac.new(secret, serialized.encode("utf-8"), hashlib.sha256).hexdigest()


def make_auth_headers(
    method: str,
    user_id: str = "perf_test_user",
    roles: str = "SPONSOR_ADMIN",
    change_reason: str = "Performance Test Automated Run",
    sponsor_id: str = "SPON-MOCK-123",
    site_id: str = "SITE-MOCK-456",
) -> dict:
    """Construct valid, signed headers required by GatewayAuthMiddleware."""
    timestamp = str(time.time())
    is_mutation = method.upper() in ("POST", "PUT", "DELETE", "PATCH")
    reason = change_reason if is_mutation else ""

    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET,
        change_reason=reason,
        sponsor_id=sponsor_id,
        site_id=site_id,
        tenant_id="tenant_default",
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Tenant-Id": "tenant_default",
        "X-Sponsor-Id": sponsor_id,
        "X-Site-Id": site_id,
        "Content-Type": "application/json",
    }
    if is_mutation:
        headers["X-Change-Reason"] = reason
    return headers


class DesignerUser(HttpUser):
    """Simulates active clinical designers hitting the Designer Service (port 8001)."""

    host = "http://localhost:8001"
    wait_time = between(1, 3)

    @task(3)
    def check_health(self):
        """Standard health check."""
        self.client.get("/health")

    @task(2)
    def propagate_cascade(self):
        """Test cascade propagation of clinical study designs."""
        study_id = f"study_cascade_perf_{uuid.uuid4().hex[:8]}"
        headers = make_auth_headers(
            "POST", change_reason="Cascade propagation load test"
        )
        payload = {
            "id": study_id,
            "name": f"Synthetic Study {study_id}",
            "studyDesigns": [
                {
                    "id": f"design_{uuid.uuid4().hex[:6]}",
                    "activities": [
                        {
                            "id": f"act_{uuid.uuid4().hex[:6]}",
                            "name": "Vital Signs Assessment",
                        },
                        {
                            "id": f"act_{uuid.uuid4().hex[:6]}",
                            "name": "Central Lab Blood Draw",
                        },
                    ],
                }
            ],
        }
        self.client.post(
            "/api/v1/designer/cascade/propagate?amendment_version=1",
            json=payload,
            headers=headers,
            name="/api/v1/designer/cascade/propagate",
        )

    @task(2)
    def list_comments(self):
        """Retrieve comment threads on a dynamic eCRF form."""
        form_id = f"form_comments_perf_{random.randint(1, 100)}"
        headers = make_auth_headers("GET")
        self.client.get(
            f"/api/v1/designer/forms/{form_id}/comments",
            headers=headers,
            name="/api/v1/designer/forms/{form_id}/comments",
        )

    @task(1)
    def post_comment(self):
        """Create an inline review comment anchoring to a specific field."""
        form_id = f"form_comments_perf_{random.randint(1, 100)}"
        headers = make_auth_headers("POST", change_reason="Adding review feedback")
        payload = {
            "field_id": f"field_id_{uuid.uuid4().hex[:6]}",
            "comment_text": f"Automated performance test comment feedback {uuid.uuid4().hex[:8]}",
        }
        self.client.post(
            f"/api/v1/designer/forms/{form_id}/comments",
            json=payload,
            headers=headers,
            name="/api/v1/designer/forms/{form_id}/comments",
        )

    @task(2)
    def evaluate_quality_sentinel(self):
        """Analyze protocol design using Quality Sentinel rules."""
        headers = make_auth_headers("POST", change_reason="Evaluate study quality")
        payload = {
            "id": f"study_sentinel_{uuid.uuid4().hex[:8]}",
            "name": "Sentinel Evaluation Study",
            "studyDesigns": [
                {
                    "id": "design_sentinel_01",
                    "activities": [{"id": "act_sentinel_vs", "name": "Vital Signs"}],
                }
            ],
        }
        self.client.post(
            "/api/v1/designer/sentinel/evaluate",
            json=payload,
            headers=headers,
            name="/api/v1/designer/sentinel/evaluate",
        )

    @task(2)
    def export_synopsis_html(self):
        """Export synopsis into lightweight and rapid HTML rendering."""
        headers = make_auth_headers("POST", change_reason="Exporting synopsis")
        payload = {
            "study_id": f"study_synopsis_{uuid.uuid4().hex[:8]}",
            "format": "html",
            "creator": "System Perf Runner",
            "change_reason": "Baseline Draft Review",
        }
        self.client.post(
            "/api/v1/synopsis/export",
            json=payload,
            headers=headers,
            name="/api/v1/synopsis/export",
        )

    @task(1)
    def export_m11_metadata(self):
        """Download synthetic USDM JSON representation for ICH M11."""
        study_id = f"study_m11_{uuid.uuid4().hex[:8]}"
        headers = make_auth_headers("GET")
        self.client.get(
            f"/api/v1/designer/export/m11/{study_id}?format=json",
            headers=headers,
            name="/api/v1/designer/export/m11/{study_id}",
        )


class ExecutionUser(HttpUser):
    """Simulates active clinical data managers and sites hitting the Execution Service (port 8002)."""

    host = "http://localhost:8002"
    wait_time = between(1, 3)

    @task(3)
    def check_health(self):
        """Standard health check."""
        self.client.get("/health")

    @task(2)
    def acquire_datalock(self):
        """Perform granular form, item-group, or field lock/freeze operations."""
        headers = make_auth_headers(
            "POST", change_reason="Data lock under concurrency test"
        )
        payload = {
            "study_id": "study_concurrency_perf_101",
            "subject_id": f"PAT-SUBJ-{uuid.uuid4().hex[:8]}",
            "form_id": f"form_execution_{uuid.uuid4().hex[:8]}",
            "item_group_id": "group_vital_signs",
            "field_name": "systolic_bp",
            "scope": "FIELD",
            "action": "LOCK",
            "reason_for_change": "Automated data lock concurrency performance validation",
        }
        self.client.post(
            "/api/v1/execution/locks/lock",
            json=payload,
            headers=headers,
            name="/api/v1/execution/locks/lock",
        )

    @task(1)
    def unlock_datalock_override(self):
        """Unlock previously frozen or locked field with administrative audit reason."""
        headers = make_auth_headers(
            "POST", change_reason="Administrative unlock override"
        )
        payload = {
            "study_id": "study_concurrency_perf_101",
            "subject_id": f"PAT-SUBJ-{uuid.uuid4().hex[:8]}",
            "form_id": f"form_execution_{uuid.uuid4().hex[:8]}",
            "item_group_id": "group_vital_signs",
            "field_name": "systolic_bp",
            "scope": "FIELD",
            "action": "UNLOCK",
            "reason_for_change": "Administrative audit correction overriding lock",
        }
        self.client.post(
            "/api/v1/execution/locks/unlock",
            json=payload,
            headers=headers,
            name="/api/v1/execution/locks/unlock",
        )

    @task(2)
    def get_lock_status(self):
        """Retrieve existing data locks for specified eCRF form submission."""
        form_id = f"form_execution_{uuid.uuid4().hex[:8]}"
        headers = make_auth_headers("GET")
        self.client.get(
            f"/api/v1/execution/locks/status/{form_id}",
            headers=headers,
            name="/api/v1/execution/locks/status/{form_id}",
        )

    @task(2)
    def sync_offline_deltas(self):
        """Synchronize batched transactions queued offline from remote mobile/web applications."""
        headers = make_auth_headers("POST", change_reason="Offline synchronization run")
        payload = {
            "client_batch_id": f"batch_{uuid.uuid4().hex[:12]}",
            "device_id": f"pwa_device_site_{random.randint(1, 10)}",
            "deltas": [
                {
                    "entity_type": "ECRF_FORM",
                    "entity_id": f"form_v1_{random.randint(100, 10000)}",
                    "client_timestamp_utc": "2026-07-31T15:00:00Z",
                    "action": "SUBMIT",
                    "payload": {
                        "study_id": "STUDY-MOCK-PERF",
                        "site_id": "SITE-MOCK-PERF",
                        "subject_id": f"PAT-SUBJ-{uuid.uuid4().hex[:8]}",
                        "visit_id": f"VISIT-{random.randint(1, 5)}",
                        "VS.SYSBP": random.randint(110, 130),
                        "VS.DIABP": random.randint(70, 90),
                    },
                    "reason_for_change": "Initial offline data capture",
                }
            ],
        }
        self.client.post(
            "/api/v1/execution/offline/sync",
            json=payload,
            headers=headers,
            name="/api/v1/execution/offline/sync",
        )
