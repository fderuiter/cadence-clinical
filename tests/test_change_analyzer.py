"""
Unit and integration tests for the automated setting diff analyzer and impact assessment.

Traceability: PRD-SYS-001
"""

import pytest
from fastapi import HTTPException
from fastapi.testclient import TestClient

from apps.tickets.main import app
from apps.tickets.services.change_analyzer import (
    SettingChangeAnalyzer,
    analyze_setting_change,
)
from tests.test_tickets_service import get_auth_headers


def test_disable_audit_logging_is_blocked_outright():
    """
    Verify that attempting to disable audit logging is blocked outright with an HTTP 400 Bad Request exception.

    Requirements: PRD-SYS-001
    """
    # Attempt to disable audit logging using "false"
    with pytest.raises(HTTPException) as exc_info:
        analyze_setting_change("audit_trail_enabled", "True", "False")
    assert exc_info.value.status_code == 400
    assert (
        "Disabling audit trail logging is strictly prohibited" in exc_info.value.detail
    )

    # Attempt to disable audit logging using "disabled"
    with pytest.raises(HTTPException) as exc_info:
        analyze_setting_change("audit_logging", "enabled", "disabled")
    assert exc_info.value.status_code == 400
    assert (
        "Disabling audit trail logging is strictly prohibited" in exc_info.value.detail
    )


def test_high_risk_compliance_setting_changes():
    """
    Verify that altering eSignature or audit trail settings (without disabling) triggers HIGH_RISK and requires QA signoff.

    Requirements: PRD-SYS-001
    """
    analyzer = SettingChangeAnalyzer()

    # Changing audit retention days (not disabling logging itself)
    res_audit = analyzer.analyze_change("audit_retention_days", "365", "730")
    assert res_audit.risk_level == "HIGH_RISK"
    assert "21 CFR Part 11.10(e)" in res_audit.affected_gxp_clauses
    assert "Annex 11.9" in res_audit.affected_gxp_clauses
    assert res_audit.requires_qa_signoff is True

    # Disabling eSignature double-authentication (high risk, but not audit logging)
    res_esign = analyzer.analyze_change("esignature_double_auth", "True", "False")
    assert res_esign.risk_level == "HIGH_RISK"
    assert "21 CFR Part 11.10(e)" in res_esign.affected_gxp_clauses
    assert "Annex 11.9" in res_esign.affected_gxp_clauses
    assert res_esign.requires_qa_signoff is True


def test_medium_risk_configuration_changes():
    """
    Verify that changing session timeout or password requirements returns MEDIUM_RISK.

    Requirements: PRD-SYS-001
    """
    analyzer = SettingChangeAnalyzer()

    # Changing session timeout
    res_timeout = analyzer.analyze_change("session_timeout_minutes", "15", "30")
    assert res_timeout.risk_level == "MEDIUM_RISK"
    assert "21 CFR Part 11.10(g)" in res_timeout.affected_gxp_clauses
    assert "ISO 27001 A.9" in res_timeout.affected_gxp_clauses
    assert res_timeout.requires_qa_signoff is False

    # Modifying password min length
    res_password = analyzer.analyze_change("password_min_length", "8", "12")
    assert res_password.risk_level == "MEDIUM_RISK"
    assert "21 CFR Part 11.10(g)" in res_password.affected_gxp_clauses
    assert "ISO 27001 A.9" in res_password.affected_gxp_clauses
    assert res_password.requires_qa_signoff is False


def test_low_risk_ui_display_changes():
    """
    Verify that modifying display format, ui theme or pagination returns LOW_RISK.

    Requirements: PRD-SYS-001
    """
    analyzer = SettingChangeAnalyzer()

    # Changing theme
    res_theme = analyzer.analyze_change("ui_theme", "light", "dark")
    assert res_theme.risk_level == "LOW_RISK"
    assert res_theme.affected_gxp_clauses == []
    assert res_theme.requires_qa_signoff is False

    # Changing default pagination limit
    res_page = analyzer.analyze_change("default_pagination_limit", "20", "50")
    assert res_page.risk_level == "LOW_RISK"
    assert res_page.affected_gxp_clauses == []
    assert res_page.requires_qa_signoff is False


def test_type_aware_diff_no_op():
    """
    Verify that type-aware comparison correctly detects no-op changes even with minor formatting differences.

    Requirements: PRD-SYS-001
    """
    analyzer = SettingChangeAnalyzer()

    # Float formatting difference (no-op)
    res_float = analyzer.analyze_change("some_numeric_setting", "10", "10.0")
    assert res_float.risk_level == "LOW_RISK"
    assert "No functional configuration delta detected" in res_float.summary
    assert res_float.requires_qa_signoff is False

    # Case formatting difference on boolean (no-op)
    res_bool = analyzer.analyze_change("some_bool_setting", "True", "true")
    assert res_bool.risk_level == "LOW_RISK"
    assert "No functional configuration delta detected" in res_bool.summary
    assert res_bool.requires_qa_signoff is False


def test_analyze_diff_endpoint_via_gateway():
    """
    Verify the FastAPI endpoint POST /api/v1/compliance/change-requests/analyze-diff.

    Requirements: PRD-SYS-001
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Analyze diff testing")

    # 1. Medium Risk setting change analysis via POST API
    payload_medium = {
        "setting_key": "session_timeout",
        "old_value": "15",
        "new_value": "30",
        "data_type": "int",
    }
    res_medium = client.post(
        "/api/v1/compliance/change-requests/analyze-diff",
        json=payload_medium,
        headers=headers,
    )
    assert res_medium.status_code == 200
    data_medium = res_medium.json()
    assert data_medium["risk_level"] == "MEDIUM_RISK"
    assert data_medium["requires_qa_signoff"] is False
    assert "21 CFR Part 11.10(g)" in data_medium["affected_gxp_clauses"]

    # 2. Blocked audit disable attempt via POST API (should return HTTP 400 Bad Request)
    payload_block = {
        "setting_key": "audit_trail_enabled",
        "old_value": "True",
        "new_value": "False",
        "data_type": "bool",
    }
    res_block = client.post(
        "/api/v1/compliance/change-requests/analyze-diff",
        json=payload_block,
        headers=headers,
    )
    assert res_block.status_code == 400
    assert (
        "Disabling audit trail logging is strictly prohibited"
        in res_block.json()["detail"]
    )

    # 3. High Risk change (without disabling audit) via POST API
    payload_high = {
        "setting_key": "esignature_expiration_days",
        "old_value": "90",
        "new_value": "180",
        "data_type": "int",
    }
    res_high = client.post(
        "/api/v1/compliance/change-requests/analyze-diff",
        json=payload_high,
        headers=headers,
    )
    assert res_high.status_code == 200
    data_high = res_high.json()
    assert data_high["risk_level"] == "HIGH_RISK"
    assert data_high["requires_qa_signoff"] is True
    assert "21 CFR Part 11.10(e)" in data_high["affected_gxp_clauses"]
