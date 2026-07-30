from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.models import ComprehensionCheck, ComprehensionResult


def evaluate_comprehension(
    submitted_answers: dict[str, str],
    expected_answers: dict[str, str],
    threshold_policy: dict,
) -> tuple[bool, float, int]:
    """
    Stateless, deterministic evaluator for comprehension checks.
    Returns:
        (passed, score_percentage, correct_count)
    """
    if not expected_answers:
        return True, 100.0, 0

    correct_count = 0
    total_questions = len(expected_answers)

    for q_id, expected_val in expected_answers.items():
        sub_val = submitted_answers.get(q_id)
        if (
            sub_val is not None
            and str(sub_val).strip().lower() == str(expected_val).strip().lower()
        ):
            correct_count += 1

    score_percentage = (
        (correct_count / total_questions) * 100.0 if total_questions > 0 else 100.0
    )

    passed = True
    if threshold_policy:
        if "min_correct" in threshold_policy:
            passed = correct_count >= int(threshold_policy["min_correct"])
        elif "passing_percentage" in threshold_policy:
            passed = score_percentage >= float(threshold_policy["passing_percentage"])
        else:
            passed = correct_count == total_questions
    else:
        passed = correct_count == total_questions

    return passed, score_percentage, correct_count


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
    # 1. Fetch the ComprehensionCheck definition for this template version
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

    # 2. Evaluate answers deterministically
    passed, score, correct_count = evaluate_comprehension(
        submitted_answers=submitted_answers,
        expected_answers=check.expected_answers,
        threshold_policy=check.threshold_policy,
    )

    # 3. Create and persist ComprehensionResult
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
