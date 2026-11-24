export interface EproValidationIssue {
  message: string;
  path?: string[];
}

export type EPROAnswers = Record<string, any>;

export interface EPROSubmissionPayload {
  subject_id: string;
  diary_id: string;
  assignment_id?: string | null | undefined;
  version_index: number;
  device_timestamp?: string | undefined;
  answers: EPROAnswers;
}

const VALID_GENDERS = ["M", "F", "O", "MALE", "FEMALE", "OTHER"];

export function collectEproAnswerIssues(
  answers: unknown
): EproValidationIssue[] {
  if (
    answers == null ||
    typeof answers !== "object" ||
    Array.isArray(answers)
  ) {
    return [{ message: "Answers payload must be an object" }];
  }

  const record = answers as Record<string, any>;
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
      isNaN(ageNum) ||
      !Number.isInteger(ageNum)
    ) {
      issues.push({
        message:
          "Demographic Validation Error: Participant age must be a valid integer.",
        path: ["age"],
      });
    } else if (ageNum < 18 || ageNum > 110) {
      issues.push({
        message:
          "Demographic Validation Error: Participant age must be between 18 and 110.",
        path: ["age"],
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
    if (!VALID_GENDERS.includes(genderStr)) {
      issues.push({
        message:
          "Demographic Validation Error: Gender must be one of M, F, or O.",
        path: ["gender"],
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
      isNaN(painNum) ||
      !Number.isInteger(painNum)
    ) {
      issues.push({
        message:
          "Clinical Validation Error: Pain score must be a valid integer.",
        path: ["pain_score"],
      });
    } else if (painNum < 0 || painNum > 10) {
      issues.push({
        message:
          "Clinical Validation Error: Pain score must be between 0 and 10.",
        path: ["pain_score"],
      });
    }
  }

  return issues;
}

export function validateEproPayload(answers: Record<string, any>): {
  valid: boolean;
  errors: string[];
} {
  const issues = collectEproAnswerIssues(answers === undefined ? {} : answers);
  if (issues.length === 0) {
    return { valid: true, errors: [] };
  }
  return {
    valid: false,
    errors: issues.map((i) => i.message),
  };
}

export function validateEproSubmission(submission: Record<string, any>): {
  valid: boolean;
  errors: string[];
} {
  if (
    !submission ||
    typeof submission !== "object" ||
    Array.isArray(submission)
  ) {
    return { valid: false, errors: ["Submission payload must be an object"] };
  }

  const errors: string[] = [];

  if (typeof submission.subject_id !== "string") {
    errors.push("subject_id is required");
  } else if (submission.subject_id.length < 1) {
    errors.push("subject_id is required");
  }

  if (typeof submission.diary_id !== "string") {
    errors.push("diary_id is required");
  } else if (submission.diary_id.length < 1) {
    errors.push("diary_id is required");
  }

  if (
    submission.assignment_id !== undefined &&
    submission.assignment_id !== null &&
    typeof submission.assignment_id !== "string"
  ) {
    errors.push("assignment_id must be a string or null");
  }

  if (
    submission.version_index !== undefined &&
    (typeof submission.version_index !== "number" ||
      !Number.isInteger(submission.version_index))
  ) {
    errors.push("version_index must be an integer");
  }

  if (
    submission.device_timestamp !== undefined &&
    typeof submission.device_timestamp !== "string"
  ) {
    errors.push("device_timestamp must be a string");
  }

  for (const issue of collectEproAnswerIssues(submission.answers)) {
    errors.push(issue.message);
  }

  if (errors.length === 0) {
    return { valid: true, errors: [] };
  }
  return { valid: false, errors };
}
