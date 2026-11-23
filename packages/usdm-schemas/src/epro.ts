/**
 * Lightweight zero-dependency ePRO payload & submission schema validators.
 *
 * Exposes `EPROAnswersSchema`, `EPROSubmissionSchema`, `validateEproPayload`,
 * and `validateEproSubmission` for both `usdm-schemas/epro` and `./index.ts`
 * without pulling the USDM v3/v4 study graph and `zod/v4/classic` runtime into
 * participant-facing cold-boot bundles.
 */

export interface EproValidationIssue {
  code: string;
  message: string;
  path: (string | number)[];
}

export type EproSafeParseResult<T> =
  | { success: true; data: T }
  | { success: false; error: { issues: EproValidationIssue[] } };

export type EPROAnswers = Record<string, any>;

export interface EPROSubmissionPayload {
  subject_id: string;
  diary_id: string;
  assignment_id?: string | null;
  version_index: number;
  device_timestamp?: string;
  answers: EPROAnswers;
}

const VALID_GENDERS = new Set(["M", "F", "O", "MALE", "FEMALE", "OTHER"]);

function collectAnswerIssues(
  answers: unknown,
  basePath: (string | number)[] = []
): EproValidationIssue[] {
  if (
    answers == null ||
    typeof answers !== "object" ||
    Array.isArray(answers)
  ) {
    return [
      {
        code: "invalid_type",
        message: "Answers payload must be an object",
        path: basePath,
      },
    ];
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
      Number.isNaN(ageNum) ||
      !Number.isInteger(ageNum)
    ) {
      issues.push({
        code: "custom",
        message:
          "Demographic Validation Error: Participant age must be a valid integer.",
        path: [...basePath, "age"],
      });
    } else if (ageNum < 18 || ageNum > 110) {
      issues.push({
        code: "custom",
        message:
          "Demographic Validation Error: Participant age must be between 18 and 110.",
        path: [...basePath, "age"],
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
    if (!VALID_GENDERS.has(genderStr)) {
      issues.push({
        code: "custom",
        message:
          "Demographic Validation Error: Gender must be one of M, F, or O.",
        path: [...basePath, "gender"],
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
        code: "custom",
        message:
          "Clinical Validation Error: Pain score must be a valid integer.",
        path: [...basePath, "pain_score"],
      });
    } else if (painNum < 0 || painNum > 10) {
      issues.push({
        code: "custom",
        message:
          "Clinical Validation Error: Pain score must be between 0 and 10.",
        path: [...basePath, "pain_score"],
      });
    }
  }

  return issues;
}

export const EPROAnswersSchema = {
  safeParse(input: unknown): EproSafeParseResult<EPROAnswers> {
    const issues = collectAnswerIssues(input);
    if (issues.length > 0) {
      return { success: false, error: { issues } };
    }
    return { success: true, data: { ...(input as Record<string, any>) } };
  },
  parse(input: unknown): EPROAnswers {
    const result = EPROAnswersSchema.safeParse(input);
    if (!result.success) {
      const err = new Error(
        result.error.issues.map((i) => i.message).join("; ")
      );
      (err as Error & { issues?: EproValidationIssue[] }).issues =
        result.error.issues;
      throw err;
    }
    return result.data;
  },
};

export const EPROSubmissionSchema = {
  safeParse(input: unknown): EproSafeParseResult<EPROSubmissionPayload> {
    if (input == null || typeof input !== "object" || Array.isArray(input)) {
      return {
        success: false,
        error: {
          issues: [
            {
              code: "invalid_type",
              message: "Submission payload must be an object",
              path: [],
            },
          ],
        },
      };
    }

    const record = input as Record<string, any>;
    const issues: EproValidationIssue[] = [];

    if (typeof record.subject_id !== "string") {
      issues.push({
        code: "invalid_type",
        message: "subject_id is required",
        path: ["subject_id"],
      });
    } else if (record.subject_id.length < 1) {
      issues.push({
        code: "too_small",
        message: "subject_id is required",
        path: ["subject_id"],
      });
    }

    if (typeof record.diary_id !== "string") {
      issues.push({
        code: "invalid_type",
        message: "diary_id is required",
        path: ["diary_id"],
      });
    } else if (record.diary_id.length < 1) {
      issues.push({
        code: "too_small",
        message: "diary_id is required",
        path: ["diary_id"],
      });
    }

    if (
      record.assignment_id !== undefined &&
      record.assignment_id !== null &&
      typeof record.assignment_id !== "string"
    ) {
      issues.push({
        code: "invalid_type",
        message: "assignment_id must be a string or null",
        path: ["assignment_id"],
      });
    }

    const versionIndex =
      record.version_index === undefined ? 1 : record.version_index;
    if (typeof versionIndex !== "number" || !Number.isInteger(versionIndex)) {
      issues.push({
        code: "invalid_type",
        message: "version_index must be an integer",
        path: ["version_index"],
      });
    }

    if (
      record.device_timestamp !== undefined &&
      typeof record.device_timestamp !== "string"
    ) {
      issues.push({
        code: "invalid_type",
        message: "device_timestamp must be a string",
        path: ["device_timestamp"],
      });
    }

    issues.push(...collectAnswerIssues(record.answers, ["answers"]));

    if (issues.length > 0) {
      return { success: false, error: { issues } };
    }

    const parsed: EPROSubmissionPayload = {
      subject_id: record.subject_id as string,
      diary_id: record.diary_id as string,
      version_index: versionIndex as number,
      answers: { ...(record.answers as Record<string, any>) },
    };
    if ("assignment_id" in record) {
      parsed.assignment_id = record.assignment_id as string | null | undefined;
    }
    if (record.device_timestamp !== undefined) {
      parsed.device_timestamp = record.device_timestamp as string;
    }

    return { success: true, data: parsed };
  },
  parse(input: unknown): EPROSubmissionPayload {
    const result = EPROSubmissionSchema.safeParse(input);
    if (!result.success) {
      const err = new Error(
        result.error.issues.map((i) => i.message).join("; ")
      );
      (err as Error & { issues?: EproValidationIssue[] }).issues =
        result.error.issues;
      throw err;
    }
    return result.data;
  },
};

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
