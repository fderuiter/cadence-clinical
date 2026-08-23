"""DDF Execution Job Runner Adapter.

Orchestrates pure in-memory DDF compilation inside transactional database sessions,
managing TranslationJob state lifecycles and 21 CFR Part 11 accessibility audit records.
"""

from __future__ import annotations

import hashlib
import uuid
from typing import Any

from apps.execution.compiler.accessibility_adapter import (
    PlaywrightAccessibilityValidatorAdapter,
)
from apps.execution.database.context import audit_context, current_session
from apps.execution.database.models import TranslationJob
from apps.execution.services.layout_validator import save_accessibility_audit_log
from packages.ddf import CompileOptions, DDFCompiler


class DDFJobRunner:
    """Asynchronous job runner adapter for executing DDF compilation workflows."""

    @classmethod
    async def process_translation(
        cls,
        study_id: str,
        payload: dict[str, Any],
        session_factory: Any,
        user_id: str | None = None,
        change_reason: str | None = None,
        job_id: str | None = None,
    ) -> None:
        """Background worker that translates USDM payload into CDISC ODM and OpenRosa XML layouts.

        Args:
            study_id: The unique identifier of the source study.
            payload: The raw USDM protocol payload.
            session_factory: The SQLAlchemy asynchronous session factory.
            user_id: The user ID to attribute database modifications to.
            change_reason: The reason/justification for database modifications.
            job_id: The pre-generated UUID for the translation job.
        """
        token = None
        with audit_context(user_id, change_reason):
            try:
                async with session_factory() as session:
                    token = current_session.set(session)
                    actual_job_id = job_id if job_id else str(uuid.uuid4())
                    collected_warnings: list[dict[str, Any]] = []

                    try:
                        async with session.begin():
                            job = TranslationJob(
                                id=actual_job_id,
                                study_id=study_id,
                                status="PROCESSING",
                                warnings=[],
                            )
                            session.add(job)

                        compiler = DDFCompiler(
                            accessibility_validator=PlaywrightAccessibilityValidatorAdapter()
                        )

                        result = await compiler.compile(
                            study_id=study_id,
                            payload=payload,
                            options=CompileOptions(validate_accessibility=True),
                        )

                        collected_warnings = result.warnings

                        # Save success state and audit log in transaction
                        async with session.begin():
                            db_job = await session.get(TranslationJob, actual_job_id)
                            if db_job:
                                db_job.odm_payload = result.odm_xml
                                db_job.openrosa_payload = result.openrosa_xml
                                db_job.status = "COMPLETED"
                                db_job.warnings = collected_warnings

                            a11y_report = result.accessibility_report
                            violations = a11y_report.violations if a11y_report else []
                            passes = a11y_report.passes if a11y_report else []
                            incomplete = a11y_report.incomplete if a11y_report else []
                            inapplicable = (
                                a11y_report.inapplicable if a11y_report else []
                            )

                            await save_accessibility_audit_log(
                                session=session,
                                study_id=study_id,
                                status="PASS",
                                form_hash=result.form_hash,
                                violations=violations,
                                passes=passes,
                                incomplete=incomplete,
                                inapplicable=inapplicable,
                                layout_errors=[],
                                user_id=user_id,
                                change_reason=change_reason,
                            )

                    except Exception as e:
                        async with session.begin():
                            db_job = await session.get(TranslationJob, actual_job_id)
                            if db_job:
                                db_job.status = "FAILED"
                                db_job.error_message = str(e)
                                db_job.warnings = collected_warnings
                            else:
                                failed_job = TranslationJob(
                                    id=actual_job_id,
                                    study_id=study_id,
                                    status="FAILED",
                                    error_message=str(e),
                                    warnings=collected_warnings,
                                )
                                session.add(failed_job)

                            # Save audit log for failed run
                            try:
                                dummy_hash = hashlib.sha256(
                                    str(e).encode("utf-8")
                                ).hexdigest()
                                await save_accessibility_audit_log(
                                    session=session,
                                    study_id=study_id,
                                    status="FAIL",
                                    form_hash=dummy_hash,
                                    violations=[],
                                    passes=[],
                                    incomplete=[],
                                    inapplicable=[],
                                    layout_errors=[str(e)],
                                    user_id=user_id,
                                    change_reason=change_reason,
                                )
                            except Exception:
                                pass
            finally:
                if token is not None:
                    current_session.reset(token)
