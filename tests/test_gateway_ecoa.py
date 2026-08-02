"""Integration test suite for Gateway eCOA REST API router.

Requirements: PRD-SYS-001
"""

from datetime import UTC, datetime
from unittest.mock import AsyncMock

import httpx
import pytest
from fastapi.testclient import TestClient

from apps.gateway.main import app
from packages.security.gateway_client import GatewayBaseClient
from packages.security.middleware import get_current_user
from packages.security.rbac import Principal, get_principal


def _mock_user_subject() -> dict:
    return {
        "sub": "subject_alice",
        "roles": ["Subject"],
        "tenant_id": "tenant_test",
    }


def _mock_principal_subject() -> Principal:
    return Principal(
        user_id="subject_alice",
        roles=["subject"],
        assigned_sites=[],
        assigned_studies=["study_test_1"],
    )


def _mock_user_staff() -> dict:
    return {
        "sub": "staff_user",
        "roles": ["sponsor_dm"],
        "tenant_id": "tenant_test",
    }


def _mock_principal_staff() -> Principal:
    return Principal(
        user_id="staff_user",
        roles=["sponsor_dm"],
        assigned_sites=[],
        assigned_studies=["study_test_1"],
    )


def _mock_user_other_subject() -> dict:
    return {
        "sub": "subject_bob",
        "roles": ["Subject"],
        "tenant_id": "tenant_test",
    }


def _mock_principal_other_subject() -> Principal:
    return Principal(
        user_id="subject_bob",
        roles=["subject"],
        assigned_sites=[],
        assigned_studies=["study_test_1"],
    )


@pytest.fixture
def client_subject() -> TestClient:
    app.dependency_overrides[get_current_user] = _mock_user_subject
    app.dependency_overrides[get_principal] = _mock_principal_subject
    client = TestClient(app)
    yield client
    app.dependency_overrides.pop(get_current_user, None)
    app.dependency_overrides.pop(get_principal, None)


@pytest.fixture
def client_staff() -> TestClient:
    app.dependency_overrides[get_current_user] = _mock_user_staff
    app.dependency_overrides[get_principal] = _mock_principal_staff
    client = TestClient(app)
    yield client
    app.dependency_overrides.pop(get_current_user, None)
    app.dependency_overrides.pop(get_principal, None)


@pytest.fixture
def client_unauthorized() -> TestClient:
    # A user that has no roles corresponding to eCOA permissions
    app.dependency_overrides[get_current_user] = lambda: {
        "sub": "user_anon",
        "roles": ["anonymous"],
        "tenant_id": "tenant_test",
    }
    app.dependency_overrides[get_principal] = lambda: Principal(
        user_id="user_anon", roles=["anonymous"], assigned_sites=[], assigned_studies=[]
    )
    client = TestClient(app)
    yield client
    app.dependency_overrides.pop(get_current_user, None)
    app.dependency_overrides.pop(get_principal, None)


@pytest.fixture
def client_unauthenticated() -> TestClient:
    # Requests without override or auth headers -> 401
    app.dependency_overrides.pop(get_current_user, None)
    app.dependency_overrides.pop(get_principal, None)
    return TestClient(app)


def test_submit_epro_entry_authorized(
    client_subject: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate authorized subject submit returns 201 Created and forwards successfully.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=201,
        json={"status": "CREATED", "id": "sub_123"},
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    payload = {
        "subject_id": "subject_alice",
        "diary_id": "diary_01",
        "device_timestamp": datetime.now(UTC).isoformat(),
        "answers": {"pain_score": 3},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "device_alice",
            "conflict_strategy": "CLIENT_WINS",
        },
    }

    response = client_subject.post("/api/v1/ecoa/epro/submit", json=payload)
    assert response.status_code == 201
    assert response.json()["status"] == "CREATED"
    assert response.json()["id"] == "sub_123"

    # Verify downstream forwarding payload was mapped correctly
    mock_request.assert_called_once()
    kwargs = mock_request.call_args[1]
    assert kwargs["json"]["subject_id"] == "subject_alice"
    assert kwargs["json"]["offline_sync_markers"]["conflict_strategy"] == "CLIENT_WINS"


def test_submit_epro_entry_cross_subject_block(client_subject: TestClient) -> None:
    """Validate cross-subject submission attempts are blocked with 403 Forbidden.

    Requirements: PRD-SYS-001
    """
    payload = {
        "subject_id": "subject_bob",  # Mismatched with authenticated alice
        "diary_id": "diary_01",
        "device_timestamp": datetime.now(UTC).isoformat(),
        "answers": {"pain_score": 3},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "device_bob",
            "conflict_strategy": "CLIENT_WINS",
        },
    }

    response = client_subject.post("/api/v1/ecoa/epro/submit", json=payload)
    assert response.status_code == 403
    assert response.json()["detail"] == "Access denied"


def test_bulk_sync_epro_entries_authorized(
    client_subject: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate bulk sync requests are successfully processed and authorized.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=200,
        json={
            "status": "success",
            "processed_count": 1,
            "created_count": 1,
            "updated_count": 0,
            "ignored_count": 0,
            "conflict_count": 0,
            "results": [{"status": "CREATED", "id": "sub_123"}],
        },
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    payload = {
        "submissions": [
            {
                "subject_id": "subject_alice",
                "diary_id": "diary_01",
                "device_timestamp": datetime.now(UTC).isoformat(),
                "answers": {"pain_score": 3},
                "offline_sync_markers": {
                    "sequence_number": 1,
                    "client_id": "device_alice",
                    "conflict_strategy": "CLIENT_WINS",
                },
            }
        ]
    }

    response = client_subject.post("/api/v1/ecoa/epro/sync", json=payload)
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "success"
    assert data["processed_count"] == 1


def test_bulk_sync_epro_entries_cross_subject_block(client_subject: TestClient) -> None:
    """Validate bulk sync containing mismatched subject is blocked with 403 Forbidden.

    Requirements: PRD-SYS-001
    """
    payload = {
        "submissions": [
            {
                "subject_id": "subject_bob",  # Mismatched
                "diary_id": "diary_01",
                "device_timestamp": datetime.now(UTC).isoformat(),
                "answers": {"pain_score": 3},
                "offline_sync_markers": {
                    "sequence_number": 1,
                    "client_id": "device_bob",
                    "conflict_strategy": "CLIENT_WINS",
                },
            }
        ]
    }

    response = client_subject.post("/api/v1/ecoa/epro/sync", json=payload)
    assert response.status_code == 403
    assert response.json()["detail"] == "Access denied"


def test_ecoa_unauthorized_role_block(client_unauthorized: TestClient) -> None:
    """Validate users without sufficient RBAC permissions are blocked with 403 Forbidden.

    Requirements: PRD-SYS-001
    """
    response = client_unauthorized.get("/api/v1/ecoa/instruments/inst_123")
    assert response.status_code == 403


def test_ecoa_unauthenticated_block(client_unauthenticated: TestClient) -> None:
    """Validate unauthenticated requests are blocked with 401 Unauthorized.

    Requirements: PRD-SYS-001
    """
    response = client_unauthenticated.get("/api/v1/ecoa/instruments/inst_123")
    assert response.status_code == 401


def test_get_subject_assignments_authorized(
    client_subject: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate reading subject assignments returns 200 OK for assigned subject.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=200,
        json=[
            {
                "id": "assign_123",
                "subject_id": "subject_alice",
                "instrument_id": "inst_123",
                "start_date": datetime.now(UTC).isoformat(),
                "end_date": datetime.now(UTC).isoformat(),
                "recurrence_pattern": "DAILY",
                "due_at": datetime.now(UTC).isoformat(),
                "created_at": datetime.now(UTC).isoformat(),
                "created_by": "staff_user",
                "reason_for_change": "Initial assignment",
                "version_index": 1,
            }
        ],
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    response = client_subject.get(
        "/api/v1/ecoa/assignments/subject/subject_alice?study_id=study_test_1"
    )
    assert response.status_code == 200
    assert len(response.json()) == 1
    assert response.json()[0]["id"] == "assign_123"


def test_get_subject_assignments_cross_subject_block(
    client_subject: TestClient,
) -> None:
    """Validate reading other subject assignments is blocked with 403 Forbidden.

    Requirements: PRD-SYS-001
    """
    response = client_subject.get(
        "/api/v1/ecoa/assignments/subject/subject_bob?study_id=study_test_1"
    )
    assert response.status_code == 403
    assert response.json()["detail"] == "Access denied"


def test_get_subject_compliance_authorized(
    client_subject: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate retrieving subject compliance metrics.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=200,
        json={
            "subject_id": "subject_alice",
            "compliance_rate": 100.0,
            "completed_count": 1,
            "pending_count": 0,
            "overdue_count": 0,
            "assignments": [],
        },
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    response = client_subject.get(
        "/api/v1/ecoa/subjects/subject_alice/compliance?study_id=study_test_1"
    )
    assert response.status_code == 200
    assert response.json()["compliance_rate"] == 100.0


def test_get_subject_compliance_cross_subject_block(client_subject: TestClient) -> None:
    """Validate retrieving compliance for another subject is blocked with 403 Forbidden.

    Requirements: PRD-SYS-001
    """
    response = client_subject.get(
        "/api/v1/ecoa/subjects/subject_bob/compliance?study_id=study_test_1"
    )
    assert response.status_code == 403
    assert response.json()["detail"] == "Access denied"


def test_get_subject_instruments_authorized(
    client_subject: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate retrieving instruments for subject.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=200,
        json=[
            {
                "id": "inst_123",
                "name": "Instrument One",
                "description": "Desc",
                "items": {},
                "response_types": {},
                "scoring_metadata": {},
                "created_at": datetime.now(UTC).isoformat(),
                "created_by": "staff_user",
                "reason_for_change": "Initial",
                "version_index": 1,
            }
        ],
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    response = client_subject.get(
        "/api/v1/ecoa/subjects/subject_alice/instruments?study_id=study_test_1"
    )
    assert response.status_code == 200
    assert len(response.json()) == 1


def test_get_subject_instruments_cross_subject_block(
    client_subject: TestClient,
) -> None:
    """Validate retrieving other subject instruments is blocked.

    Requirements: PRD-SYS-001
    """
    response = client_subject.get(
        "/api/v1/ecoa/subjects/subject_bob/instruments?study_id=study_test_1"
    )
    assert response.status_code == 403
    assert response.json()["detail"] == "Access denied"


def test_get_subject_notifications_authorized(
    client_subject: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate retrieving notifications for subject.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=200,
        json=[
            {
                "id": "notif_123",
                "subject_id": "subject_alice",
                "assignment_id": "assign_123",
                "due_at": datetime.now(UTC).isoformat(),
                "channel": "EMAIL",
                "delivery_status": "SENT",
                "is_read": False,
                "read_at": None,
                "created_at": datetime.now(UTC).isoformat(),
                "created_by": "system",
                "reason_for_change": "Automated",
                "version_index": 1,
            }
        ],
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    response = client_subject.get(
        "/api/v1/ecoa/subjects/subject_alice/notifications?study_id=study_test_1"
    )
    assert response.status_code == 200
    assert len(response.json()) == 1


def test_get_subject_notifications_cross_subject_block(
    client_subject: TestClient,
) -> None:
    """Validate retrieving other subject notifications is blocked.

    Requirements: PRD-SYS-001
    """
    response = client_subject.get(
        "/api/v1/ecoa/subjects/subject_bob/notifications?study_id=study_test_1"
    )
    assert response.status_code == 403
    assert response.json()["detail"] == "Access denied"


def test_acknowledge_notification_authorized(
    client_subject: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate acknowledging a notification.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=200,
        json={
            "id": "notif_123",
            "subject_id": "subject_alice",
            "assignment_id": "assign_123",
            "due_at": datetime.now(UTC).isoformat(),
            "channel": "EMAIL",
            "delivery_status": "SENT",
            "is_read": True,
            "read_at": datetime.now(UTC).isoformat(),
            "created_at": datetime.now(UTC).isoformat(),
            "created_by": "system",
            "reason_for_change": "Subject acked",
            "version_index": 2,
        },
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    payload = {"reason_for_change": "Read notification"}
    response = client_subject.post(
        "/api/v1/ecoa/notifications/notif_123/acknowledge?study_id=study_test_1",
        json=payload,
    )
    assert response.status_code == 200
    assert response.json()["is_read"] is True


def test_acknowledge_notification_cross_subject_block(
    client_subject: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate acknowledging notification belonging to another subject is blocked.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=200,
        json={
            "id": "notif_123",
            "subject_id": "subject_bob",  # Mismatched owner returned from interop
            "assignment_id": "assign_123",
            "due_at": datetime.now(UTC).isoformat(),
            "channel": "EMAIL",
            "delivery_status": "SENT",
            "is_read": False,
            "read_at": None,
            "created_at": datetime.now(UTC).isoformat(),
            "created_by": "system",
            "reason_for_change": "Automated",
            "version_index": 1,
        },
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    payload = {"reason_for_change": "Bob's notification"}
    response = client_subject.post(
        "/api/v1/ecoa/notifications/notif_123/acknowledge?study_id=study_test_1",
        json=payload,
    )
    assert response.status_code == 403
    assert response.json()["detail"] == "Access denied"


def test_staff_authoring_instruments(
    client_staff: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate that staff roles can author new instruments.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=201,
        json={
            "id": "inst_123",
            "name": "Pain Scale",
            "description": "Pain daily",
            "items": {"pain": "score"},
            "response_types": {"pain": {"type": "numeric"}},
            "scoring_metadata": {},
            "created_at": datetime.now(UTC).isoformat(),
            "created_by": "staff_user",
            "reason_for_change": "Author scale",
            "version_index": 1,
        },
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    payload = {
        "study_id": "study_test_1",
        "name": "Pain Scale",
        "description": "Pain daily",
        "items": {"pain": "score"},
        "response_types": {"pain": {"type": "numeric"}},
        "scoring_metadata": {},
        "reason_for_change": "Author scale",
    }

    response = client_staff.post("/api/v1/ecoa/instruments", json=payload)
    assert response.status_code == 201
    assert response.json()["id"] == "inst_123"


def test_staff_authoring_assignments(
    client_staff: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Validate that staff roles can assign instruments.

    Requirements: PRD-SYS-001
    """
    mock_request = AsyncMock()
    mock_request.return_value = httpx.Response(
        status_code=201,
        json={
            "id": "assign_123",
            "subject_id": "subject_alice",
            "instrument_id": "inst_123",
            "start_date": datetime.now(UTC).isoformat(),
            "end_date": datetime.now(UTC).isoformat(),
            "recurrence_pattern": "DAILY",
            "due_at": datetime.now(UTC).isoformat(),
            "created_at": datetime.now(UTC).isoformat(),
            "created_by": "staff_user",
            "reason_for_change": "Assigning",
            "version_index": 1,
        },
    )
    monkeypatch.setattr(GatewayBaseClient, "request", mock_request)

    payload = {
        "study_id": "study_test_1",
        "subject_id": "subject_alice",
        "instrument_id": "inst_123",
        "start_date": datetime.now(UTC).isoformat(),
        "end_date": datetime.now(UTC).isoformat(),
        "recurrence_pattern": "DAILY",
        "due_at": datetime.now(UTC).isoformat(),
        "reason_for_change": "Assigning",
    }

    response = client_staff.post("/api/v1/ecoa/assignments", json=payload)
    assert response.status_code == 201
    assert response.json()["id"] == "assign_123"
