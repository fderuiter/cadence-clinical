export interface EproValidationIssue {
  path: string[];
  message: string;
}

export interface EproValidationResult {
  valid: boolean;
  errors: string[];
}

const VALID_EPRO_GENDERS = new Set(["M", "F", "O", "MALE", "FEMALE", "OTHER"]);

export function collectEproAnswerIssues(
  answers: unknown
): EproValidationIssue[] {
  if (
    answers == null ||
    typeof answers !== "object" ||
    Array.isArray(answers)
  ) {
    return [{ path: [], message: "Answers payload must be an object" }];
  }

  const record = answers as Record<string, unknown>;
  const issues: EproValidationIssue[] = [];

  if (
    "age" in record &&
    record.age !== null &&
    record.age !== undefined &&
    record.age !== ""
  ) {
    const rawAge = record.age;
    const ageNum = typeof rawAge === "number" ? rawAge : Number(rawAge);
    if (
      typeof rawAge === "boolean" ||
      Number.isNaN(ageNum) ||
      !Number.isInteger(ageNum)
    ) {
      issues.push({
        path: ["age"],
        message:
          "Demographic Validation Error: Participant age must be a valid integer.",
      });
    } else if (ageNum < 18 || ageNum > 110) {
      issues.push({
        path: ["age"],
        message:
          "Demographic Validation Error: Participant age must be between 18 and 110.",
      });
    }
  }

  if (
    "gender" in record &&
    record.gender !== null &&
    record.gender !== undefined &&
    record.gender !== ""
  ) {
    const genderStr = String(record.gender).toUpperCase();
    if (!VALID_EPRO_GENDERS.has(genderStr)) {
      issues.push({
        path: ["gender"],
        message:
          "Demographic Validation Error: Gender must be one of M, F, or O.",
      });
    }
  }

  if (
    "pain_score" in record &&
    record.pain_score !== null &&
    record.pain_score !== undefined &&
    record.pain_score !== ""
  ) {
    const rawPain = record.pain_score;
    const painNum = typeof rawPain === "number" ? rawPain : Number(rawPain);
    if (
      typeof rawPain === "boolean" ||
      Number.isNaN(painNum) ||
      !Number.isInteger(painNum)
    ) {
      issues.push({
        path: ["pain_score"],
        message:
          "Clinical Validation Error: Pain score must be a valid integer.",
      });
    } else if (painNum < 0 || painNum > 10) {
      issues.push({
        path: ["pain_score"],
        message:
          "Clinical Validation Error: Pain score must be between 0 and 10.",
      });
    }
  }

  return issues;
}

export function validateEproPayload(
  answers: Record<string, any>
): EproValidationResult {
  const issues = collectEproAnswerIssues(answers || {});
  if (issues.length === 0) {
    return { valid: true, errors: [] };
  }
  return {
    valid: false,
    errors: issues.map((issue) => issue.message),
  };
}

export function validateEproSubmission(
  submission: Record<string, any>
): EproValidationResult {
  if (
    !submission ||
    typeof submission !== "object" ||
    Array.isArray(submission)
  ) {
    return { valid: false, errors: ["Submission payload must be an object"] };
  }

  const errors: string[] = [];

  if (typeof submission.subject_id !== "string") {
    errors.push("Invalid input: expected string");
  } else if (submission.subject_id.length < 1) {
    errors.push("subject_id is required");
  }

  if (typeof submission.diary_id !== "string") {
    errors.push("Invalid input: expected string");
  } else if (submission.diary_id.length < 1) {
    errors.push("diary_id is required");
  }

  if (
    submission.assignment_id !== undefined &&
    submission.assignment_id !== null &&
    typeof submission.assignment_id !== "string"
  ) {
    errors.push("Invalid input: expected string");
  }

  if (
    submission.version_index !== undefined &&
    (typeof submission.version_index !== "number" ||
      !Number.isInteger(submission.version_index))
  ) {
    errors.push("Invalid input: expected int");
  }

  if (
    submission.device_timestamp !== undefined &&
    typeof submission.device_timestamp !== "string"
  ) {
    errors.push("Invalid input: expected string");
  }

  for (const issue of collectEproAnswerIssues(submission.answers)) {
    errors.push(issue.message);
  }

  if (errors.length === 0) {
    return { valid: true, errors: [] };
  }
  return { valid: false, errors };
}
