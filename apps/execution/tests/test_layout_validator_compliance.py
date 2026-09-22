"""Tests for layout and accessibility validator service."""

import os

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base, TranslationJob
from apps.execution.services.layout_validator import (
    run_layout_and_accessibility_checks,
)
from apps.execution.translator import process_translation
from packages.testing.fakes import FakeAccessibilityValidator


@pytest_asyncio.fixture
async def setup_test_db():
    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        )
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_in_memory_accessibility_auditing():
    """Verify that automated layout WCAG checks identify contrast and element focus violations.

    @req: PRD-CRF-015
    @req: Trace-33
    """
    html_content = """
    <html>
      <head>
        <title>Compliance Check Form</title>
        <style>
          .low-contrast-btn {
            background-color: #eee;
            color: #eed; /* extremely low contrast on light gray background */
            width: 150px;
            height: 50px;
          }
        </style>
      </head>
      <body>
        <button class="low-contrast-btn">Low Contrast Button</button>
      </body>
    </html>
    """

    (
        violations,
        passes,
        incomplete,
        inapplicable,
        layout_errors,
    ) = await run_layout_and_accessibility_checks(html_content)

    assert len(violations) > 0
    violation_ids = {v["id"] for v in violations}
    assert "color-contrast" in violation_ids


@pytest.mark.asyncio
async def test_injected_accessibility_validator_records_audit_log(setup_test_db):
    """Verify that integration execution using an explicitly injected test validator adapter succeeds and records accessibility audit logs."""
    test_validator = FakeAccessibilityValidator(should_fail=False)
    study_id = "test_injected_a11y_study"
    payload = {
        "name": "Injected Validator Trial",
        "protocol": {
            "items": [
                {"id": "q1", "name": "Question 1", "type": "string"},
            ]
        },
    }

    await process_translation(
        study_id=study_id,
        payload=payload,
        session_factory=db_manager.get_session_maker(),
        user_id="test_user_a11y",
        change_reason="Audit test",
        accessibility_validator=test_validator,
    )

    async with db_manager.get_session_maker()() as session:
        # Verify translation job status
        stmt_job = select(TranslationJob).where(TranslationJob.study_id == study_id)
        res_job = await session.execute(stmt_job)
        job = res_job.scalar_one_or_none()
        assert job is not None
        assert job.status == "COMPLETED"

        # Verify accessibility audit log entry
        stmt_audit = select(AuditLog).where(
            AuditLog.table_name == "accessibility_audit",
            AuditLog.record_id == study_id,
        )
        res_audit = await session.execute(stmt_audit)
        audit = res_audit.scalar_one_or_none()
        assert audit is not None
        assert audit.action == "VALIDATE"
        assert audit.new_values["status"] == "PASS"
        assert audit.new_values["form_version_hash"] is not None


@pytest.mark.asyncio
async def test_missing_playwright_raises_runtime_error(monkeypatch):
    """Verify that invoking run_layout_and_accessibility_checks without playwright raises RuntimeError."""
    import sys

    orig_import = __import__

    def fake_import(name, *args, **kwargs):
        if name.startswith("playwright"):
            raise ModuleNotFoundError("No module named 'playwright'")
        return orig_import(name, *args, **kwargs)

    monkeypatch.setattr("builtins.__import__", fake_import)
    monkeypatch.setitem(sys.modules, "playwright", None)
    monkeypatch.setitem(sys.modules, "playwright.async_api", None)

    with pytest.raises(RuntimeError) as exc_info:
        await run_layout_and_accessibility_checks(
            "<html><body><p>Test</p></body></html>"
        )

    assert "Playwright is not installed" in str(exc_info.value)

