import { z } from "zod/mini";

z.config({ jitless: true });

export const EPROAnswersSchema = z.record(z.string(), z.any()).check((ctx) => {
  const answers = ctx.value;
  if (answers == null || typeof answers !== "object") {
    ctx.issues.push({
      code: "custom",
      input: answers,
      message: "Answers payload must be an object",
    });
    return;
  }

  if (
    "age" in answers &&
    answers.age !== null &&
    answers.age !== undefined &&
    answers.age !== ""
  ) {
    const rawAge = answers.age;
    const ageNum = typeof rawAge === "number" ? rawAge : Number(rawAge);
    if (
      typeof rawAge === "boolean" ||
      isNaN(ageNum) ||
      !Number.isInteger(ageNum)
    ) {
      ctx.issues.push({
        code: "custom",
        input: rawAge,
        message:
          "Demographic Validation Error: Participant age must be a valid integer.",
        path: ["age"],
      });
    } else if (ageNum < 18 || ageNum > 110) {
      ctx.issues.push({
        code: "custom",
        input: rawAge,
        message:
          "Demographic Validation Error: Participant age must be between 18 and 110.",
        path: ["age"],
      });
    }
  }

  if (
    "gender" in answers &&
    answers.gender !== null &&
    answers.gender !== undefined &&
    answers.gender !== ""
  ) {
    const genderStr = String(answers.gender).toUpperCase();
    const validGenders = ["M", "F", "O", "MALE", "FEMALE", "OTHER"];
    if (!validGenders.includes(genderStr)) {
      ctx.issues.push({
        code: "custom",
        input: answers.gender,
        message:
          "Demographic Validation Error: Gender must be one of M, F, or O.",
        path: ["gender"],
      });
    }
  }

  if (
    "pain_score" in answers &&
    answers.pain_score !== null &&
    answers.pain_score !== undefined &&
    answers.pain_score !== ""
  ) {
    const rawPain = answers.pain_score;
    const painNum = typeof rawPain === "number" ? rawPain : Number(rawPain);
    if (
      typeof rawPain === "boolean" ||
      isNaN(painNum) ||
      !Number.isInteger(painNum)
    ) {
      ctx.issues.push({
        code: "custom",
        input: rawPain,
        message:
          "Clinical Validation Error: Pain score must be a valid integer.",
        path: ["pain_score"],
      });
    } else if (painNum < 0 || painNum > 10) {
      ctx.issues.push({
        code: "custom",
        input: rawPain,
        message:
          "Clinical Validation Error: Pain score must be between 0 and 10.",
        path: ["pain_score"],
      });
    }
  }
});
export type EPROAnswers = z.infer<typeof EPROAnswersSchema>;

export const EPROSubmissionSchema = z.object({
  subject_id: z.string().check(z.minLength(1, "subject_id is required")),
  diary_id: z.string().check(z.minLength(1, "diary_id is required")),
  assignment_id: z.optional(z.nullable(z.string())),
  version_index: z._default(z.optional(z.int()), 1),
  device_timestamp: z.optional(z.string()),
  answers: EPROAnswersSchema,
});
export type EPROSubmissionPayload = z.infer<typeof EPROSubmissionSchema>;

export function validateEproPayload(answers: Record<string, any>): {
  valid: boolean;
  errors: string[];
} {
  const parseResult = EPROAnswersSchema.safeParse(answers || {});
  if (parseResult.success) {
    return { valid: true, errors: [] };
  }
  return {
    valid: false,
    errors: parseResult.error.issues.map((i) => i.message),
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
