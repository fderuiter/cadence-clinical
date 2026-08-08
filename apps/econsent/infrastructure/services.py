from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.domain.evaluator import evaluate_comprehension
from apps.econsent.infrastructure.models import ComprehensionCheck, ComprehensionResult


async def submit_comprehension_answers(
    session: AsyncSession,
    template_id: str,
    version_index: int,
    subject_pseudonym: str,
    submitted_answers: dict[str, str],
    created_by: str,
    reason_for_change: str,
) -> ComprehensionResult:
    """
    Orchestrate db queries, fetch check details, evaluate, and persist append-only ComprehensionResult.
    """
    stmt = select(ComprehensionCheck).where(
        ComprehensionCheck.template_id == template_id,
        ComprehensionCheck.version_index == version_index,
    )
    result_check = await session.execute(stmt)
    check = result_check.scalars().first()

    if not check:
        raise ValueError(
            f"No comprehension check defined for template '{template_id}' version {version_index}."
        )

    passed, score, correct_count = evaluate_comprehension(
        submitted_answers=submitted_answers,
        expected_answers=check.expected_answers,
        threshold_policy=check.threshold_policy,
    )

    result = ComprehensionResult(
        template_id=template_id,
        version_index=version_index,
        subject_pseudonym=subject_pseudonym,
        questions=check.questions,
        expected_answers=check.expected_answers,
        threshold_policy=check.threshold_policy,
        submitted_answers=submitted_answers,
        passed=passed,
        score=score,
        created_by=created_by,
        reason_for_change=reason_for_change,
    )
    session.add(result)
    await session.flush()
    return result
