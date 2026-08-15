"""Domain evaluator for comprehension checks."""


def evaluate_comprehension(
    submitted_answers: dict[str, str],
    expected_answers: dict[str, str],
    threshold_policy: dict,
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
