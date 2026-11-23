import * as z from "zod/mini";

interface EproAnswerIssue {
  message: string;
  path?: string[];
}

function collectEproAnswerIssues(answers: unknown): EproAnswerIssue[] {
  if (
    answers == null ||
    typeof answers !== "object" ||
    Array.isArray(answers)
  ) {
    return [{ message: "Answers payload must be an object" }];
  }

  const record = answers as Record<string, any>;
  const issues: EproAnswerIssue[] = [];

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
    const validGenders = ["M", "F", "O", "MALE", "FEMALE", "OTHER"];
    if (!validGenders.includes(genderStr)) {
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

export const EPROAnswersSchema = /* @__PURE__ */ (() => {
  z.config({ jitless: true });
  return z.record(z.string(), z.any()).check(
    z.superRefine((answers, ctx) => {
      for (const issue of collectEproAnswerIssues(answers)) {
        ctx.addIssue({
          code: "custom",
          message: issue.message,
          ...(issue.path ? { path: issue.path } : {}),
        });
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
  const issues = collectEproAnswerIssues(answers || {});
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
  if (!submission || typeof submission !== "object") {
    return { valid: false, errors: ["Submission payload must be an object"] };
  }
  const parseResult = EPROSubmissionSchema.safeParse(submission);
  if (parseResult.success) {
    return { valid: true, errors: [] };
  }
  return {
    valid: false,
    errors: parseResult.error.issues.map((i) => i.message),
  };
}
