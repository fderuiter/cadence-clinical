"""Domain evaluator for comprehension checks.

Provides deterministic, pure functional evaluation of subject quiz submissions
with remediation guidance and threshold enforcement.
"""

from typing import Any


def evaluate_comprehension(
    submitted_answers: dict[str, str],
    expected_answers: dict[str, str],
    threshold_policy: dict[str, Any] | None = None,
) -> tuple[bool, float, int]:
    """Stateless, deterministic evaluator for comprehension checks.

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


def evaluate_detailed_comprehension(
    submitted_answers: dict[str, str],
    questions: list[dict[str, Any]],
    expected_answers: dict[str, str],
    threshold_policy: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Performs deep comprehension evaluation with question-level breakdown and remediation hints."""
    passed, score_pct, correct_count = evaluate_comprehension(
        submitted_answers=submitted_answers,
        expected_answers=expected_answers,
        threshold_policy=threshold_policy,
    )

    question_map = {q.get("id"): q for q in questions if isinstance(q, dict)}
    details = []

    for q_id, expected_val in expected_answers.items():
        sub_val = submitted_answers.get(q_id)
        is_correct = (
            sub_val is not None
            and str(sub_val).strip().lower() == str(expected_val).strip().lower()
        )
        q_meta = question_map.get(q_id, {})

        details.append(
            {
                "question_id": q_id,
                "is_correct": is_correct,
                "submitted_answer": sub_val,
                "hint": q_meta.get("hint") if not is_correct else None,
                "clause_reference": q_meta.get("clause_reference"),
                "explanation": q_meta.get("explanation") if is_correct else None,
            }
        )

    return {
        "passed": passed,
        "score_percentage": score_pct,
        "correct_count": correct_count,
        "total_questions": len(expected_answers),
        "details": details,
    }
