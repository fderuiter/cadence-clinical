import * as z from "zod/mini";
import {
  collectEproAnswerIssues,
  validateEproPayload,
  validateEproSubmission,
} from "./epro-validator.js";

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

export { validateEproPayload, validateEproSubmission };
