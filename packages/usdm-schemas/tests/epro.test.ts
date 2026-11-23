import { describe, expect, it } from "vitest";
import {
  validateEproPayload as validateRootPayload,
  validateEproSubmission as validateRootSubmission,
} from "../src/index";
import {
  EPROAnswersSchema,
  EPROSubmissionSchema,
  validateEproPayload,
  validateEproSubmission,
} from "../src/epro";

describe("usdm-schemas/epro parity with root USDM ePRO validators", () => {
  it("matches validateEproPayload across valid and invalid demographic/clinical payloads", () => {
    const cases: Record<string, any>[] = [
      {},
      { age: 35, gender: "FEMALE", pain_score: 4 },
      { age: "18", gender: "m", pain_score: "0" },
      { age: 110, gender: "Other", pain_score: 10 },
      { age: "", gender: null, pain_score: undefined },
      { age: 15, gender: "M", pain_score: 5 },
      { age: 120, gender: "F", pain_score: 2 },
      { age: true, gender: "UNKNOWN", pain_score: 11 },
      { age: 24.5, gender: "O", pain_score: -1 },
      { age: "not-a-number", gender: "M", pain_score: 3.5 },
    ];

    for (const payload of cases) {
      expect(validateEproPayload(payload)).toEqual(
        validateRootPayload(payload)
      );
    }

    expect(
      validateEproPayload({ age: "18", gender: "m", pain_score: "0" })
    ).toEqual({
      valid: true,
      errors: [],
    });

    const invalidResult = validateEproPayload({
      age: 15,
      gender: "UNKNOWN",
      pain_score: 11,
    });
    expect(invalidResult.valid).toBe(false);
    expect(invalidResult.errors).toEqual([
      "Demographic Validation Error: Participant age must be between 18 and 110.",
      "Demographic Validation Error: Gender must be one of M, F, or O.",
      "Clinical Validation Error: Pain score must be between 0 and 10.",
    ]);
  });

  it("matches validateEproSubmission across valid and invalid submission envelopes", () => {
    const submissions: any[] = [
      null,
      undefined,
      {
        subject_id: "sub_1",
        diary_id: "diary_1",
        answers: { age: 22, gender: "M", pain_score: 5 },
      },
      {
        subject_id: "",
        diary_id: "",
        answers: { age: 12, gender: "INVALID", pain_score: 14 },
      },
      {
        subject_id: "sub_2",
        diary_id: "diary_2",
        assignment_id: null,
        version_index: 2,
        device_timestamp: "2026-11-23T00:00:00Z",
        answers: { symptom_notes: "Steady recovery" },
      },
    ];

    for (const submission of submissions) {
      expect(validateEproSubmission(submission)).toEqual(
        validateRootSubmission(submission)
      );
    }

    expect(validateEproSubmission(null as any)).toEqual({
      valid: false,
      errors: ["Submission payload must be an object"],
    });
    expect(
      validateEproSubmission({
        subject_id: "sub_1",
        diary_id: "diary_1",
        answers: { age: 22, gender: "M", pain_score: 5 },
      })
    ).toEqual({ valid: true, errors: [] });

    const invalidSubmission = validateEproSubmission({
      subject_id: "",
      diary_id: "",
      answers: { age: 12, gender: "INVALID", pain_score: 14 },
    });
    expect(invalidSubmission.valid).toBe(false);
    expect(invalidSubmission.errors).toContain("subject_id is required");
    expect(invalidSubmission.errors).toContain("diary_id is required");
    expect(invalidSubmission.errors).toContain(
      "Demographic Validation Error: Participant age must be between 18 and 110."
    );
  });

  it("supports safeParse and parse on EPROAnswersSchema and EPROSubmissionSchema", () => {
    const parsedAnswers = EPROAnswersSchema.parse({ age: 42, pain_score: 3 });
    expect(parsedAnswers).toEqual({ age: 42, pain_score: 3 });

    const parsedSubmission = EPROSubmissionSchema.parse({
      subject_id: "sub_9",
      diary_id: "diary_pain",
      answers: { pain_score: 6 },
    });
    expect(parsedSubmission.version_index).toBe(1);

    expect(() => EPROAnswersSchema.parse({ age: 9 })).toThrow(
      "Participant age must be between 18 and 110"
    );
  });
});
