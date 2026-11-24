import { describe, expect, it } from "vitest";
import {
  EPROAnswersSchema,
  EPROSubmissionSchema,
  validateEproPayload,
  validateEproSubmission,
} from "../src/index";
import {
  validateEproPayload as validateEproPayloadSubpath,
  validateEproSubmission as validateEproSubmissionSubpath,
} from "../src/epro-validator";

describe("ePRO schemas and subpath validator parity", () => {
  it("re-exports the identical validator helpers across root and ./epro", () => {
    expect(validateEproPayload).toBe(validateEproPayloadSubpath);
    expect(validateEproSubmission).toBe(validateEproSubmissionSubpath);
  });

  it("matches EPROAnswersSchema.safeParse across valid and invalid answers", () => {
    const cases = [
      { age: 34, gender: "F", pain_score: 4 },
      { age: "18", gender: "male", pain_score: "0" },
      { age: 15, gender: "X", pain_score: 12 },
      { age: true, gender: "M", pain_score: 3.5 },
      {},
    ];

    for (const payload of cases) {
      const schemaRes = EPROAnswersSchema.safeParse(payload);
      const helperRes = validateEproPayload(payload);
      expect(helperRes.valid).toBe(schemaRes.success);
      if (!schemaRes.success) {
        expect(helperRes.errors).toEqual(
          schemaRes.error.issues.map((i) => i.message)
        );
      }
    }
  });

  it("matches EPROSubmissionSchema.safeParse across valid and invalid submissions", () => {
    const valid = {
      subject_id: "sub_01",
      diary_id: "diary_01",
      assignment_id: null,
      answers: { age: 29, gender: "O", pain_score: 7 },
    };
    expect(EPROSubmissionSchema.safeParse(valid).success).toBe(true);
    expect(validateEproSubmission(valid)).toEqual({ valid: true, errors: [] });

    const invalid = {
      subject_id: "",
      diary_id: "",
      assignment_id: 42,
      version_index: 1.5,
      answers: { age: 12, pain_score: -1 },
    };
    expect(EPROSubmissionSchema.safeParse(invalid).success).toBe(false);
    const helperRes = validateEproSubmission(invalid);
    expect(helperRes.valid).toBe(false);
    expect(helperRes.errors).toContain("subject_id is required");
    expect(helperRes.errors).toContain("diary_id is required");
    expect(helperRes.errors).toContain(
      "Demographic Validation Error: Participant age must be between 18 and 110."
    );
    expect(helperRes.errors).toContain(
      "Clinical Validation Error: Pain score must be between 0 and 10."
    );
  });
});
