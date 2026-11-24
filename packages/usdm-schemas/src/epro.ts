import * as z from "zod/mini";

const VALID_GENDERS = ["M", "F", "O", "MALE", "FEMALE", "OTHER"];

function collectEproAnswerErrors(answers: unknown): string[] {
  if (
    answers == null ||
    typeof answers !== "object" ||
    Array.isArray(answers)
  ) {
    return ["Answers payload must be an object"];
  }

  const record = answers as Record<string, unknown>;
  const errors: string[] = [];

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
      errors.push(
        "Demographic Validation Error: Participant age must be a valid integer."
      );
    } else if (ageNum < 18 || ageNum > 110) {
      errors.push(
        "Demographic Validation Error: Participant age must be between 18 and 110."
      );
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
      errors.push(
        "Demographic Validation Error: Gender must be one of M, F, or O."
      );
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
      errors.push(
        "Clinical Validation Error: Pain score must be a valid integer."
      );
    } else if (painNum < 0 || painNum > 10) {
      errors.push(
        "Clinical Validation Error: Pain score must be between 0 and 10."
      );
    }
  }

  return errors;
}

export const EPROAnswersSchema = /* @__PURE__ */ (() => {
  z.config({ jitless: true });
  return z.record(z.string(), z.any()).check(
    z.superRefine((answers, ctx) => {
      for (const message of collectEproAnswerErrors(answers)) {
        ctx.addIssue({ code: "custom", message });
      }
    })
  );
})();
export type EPROAnswers = z.infer<typeof EPROAnswersSchema>;

export const EPROSubmissionSchema = /* @__PURE__ */ (() => {
  z.config({ jitless: true });
  return z.object({
    subject_id: z.string().check(z.minLength(1, "subject_id is required")),
    diary_id: z.string().check(z.minLength(1, "diary_id is required")),
    assignment_id: z.nullable(z.optional(z.string())),
    version_index: z._default(z.optional(z.int()), 1),
    device_timestamp: z.optional(z.string()),
    answers: EPROAnswersSchema,
  });
})();
export type EPROSubmissionPayload = z.infer<typeof EPROSubmissionSchema>;

export function validateEproPayload(answers: Record<string, any>): {
  valid: boolean;
  errors: string[];
} {
  const errors = collectEproAnswerErrors(answers ?? {});
  return {
    valid: errors.length === 0,
    errors,
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
    errors.push("Invalid input: expected string, received undefined");
  } else if (submission.subject_id.length < 1) {
    errors.push("subject_id is required");
  }

  if (typeof submission.diary_id !== "string") {
    errors.push("Invalid input: expected string, received undefined");
  } else if (submission.diary_id.length < 1) {
    errors.push("diary_id is required");
  }

  if (
    submission.assignment_id !== undefined &&
    submission.assignment_id !== null &&
    typeof submission.assignment_id !== "string"
  ) {
    errors.push("Invalid assignment_id");
  }

  if (
    submission.version_index !== undefined &&
    (typeof submission.version_index !== "number" ||
      !Number.isInteger(submission.version_index))
  ) {
    errors.push("Invalid version_index");
  }

  if (
    submission.device_timestamp !== undefined &&
    typeof submission.device_timestamp !== "string"
  ) {
    errors.push("Invalid device_timestamp");
  }

  errors.push(...collectEproAnswerErrors(submission.answers));

  return {
    valid: errors.length === 0,
    errors,
  };
}
