"""DDF Translation Service Facade.

Backward-compatible entry point delegating to the deep DDFCompiler pipeline
and transactional DDFJobRunner.
"""

from typing import Any

from apps.execution.compiler.job_runner import DDFJobRunner
from packages.ddf.ports import AccessibilityValidatorPort
from packages.ddf.rules.compiler import (
    compile_condition_to_xpath,
    compile_to_xpath,
    extract_appearance,
    sanitize_identifier,
)

__all__ = [
    "compile_condition_to_xpath",
    "compile_to_xpath",
    "extract_appearance",
    "process_translation",
    "sanitize_identifier",
]


async def process_translation(
    study_id: str,
    payload: dict[str, Any],
    session_factory: Any,
    user_id: str | None = None,
    change_reason: str | None = None,
    job_id: str | None = None,
    accessibility_validator: AccessibilityValidatorPort | None = None,
) -> None:
    """Background worker that translates USDM payload into CDISC ODM and OpenRosa XML layouts.

    Delegates execution to the transactional DDFJobRunner and deep DDFCompiler.

    Args:
        study_id: The unique identifier of the source study.
        payload: The raw USDM protocol payload.
        session_factory: The SQLAlchemy asynchronous session factory.
        user_id: The user ID to attribute database modifications to.
        change_reason: The reason/justification for database modifications.
        job_id: The pre-generated UUID for the translation job.
        accessibility_validator: Optional injected accessibility validator port adapter.
    """
    await DDFJobRunner.process_translation(
        study_id=study_id,
        payload=payload,
        session_factory=session_factory,
        user_id=user_id,
        change_reason=change_reason,
        job_id=job_id,
        accessibility_validator=accessibility_validator,
    )
