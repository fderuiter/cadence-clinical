// This file is auto-generated from Python USDM models. DO NOT EDIT DIRECTLY.
import { z } from "zod";

export const CodeSchema = z.object({
  code: z.string(),
  codeSystem: z.string(),
  codeSystemVersion: z.string().nullable().optional(),
  decode: z.string(),
});
export type Code = z.infer<typeof CodeSchema>;

export const SyntaxTemplateSchema = z.object({
  id: z.string(),
  name: z.string().nullable().optional(),
  text: z.string(),
  notes: z.array(z.string()).default([]),
});
export type SyntaxTemplate = z.infer<typeof SyntaxTemplateSchema>;

export const EligibilityCriterionSchema = z.object({
  id: z.string(),
  name: z.string().default(""),
  criterionType: z.string().default("Inclusion"),
  identifier: z.string().nullable().optional(),
  category: z.string().nullable().optional(),
  text: z.string().nullable().optional(),
  textExpression: z.string().nullable().optional(),
  logicalExpression: z.string().nullable().optional(),
  template: SyntaxTemplateSchema.nullable().optional(),
});
export type EligibilityCriterion = z.infer<typeof EligibilityCriterionSchema>;

export const BiomedicalConceptPropertySchema = z.object({
  id: z.string(),
  name: z.string(),
  label: z.string().nullable().optional(),
  cdashVariable: z.string().nullable().optional(),
  dataType: z.string().default("text"),
  mandatory: z.boolean().default(false),
  range: z.string().nullable().optional(),
  options: z.array(z.string()).default([]),
  config: z.record(z.any()),
  gridSpan: z.number().int().default(12),
  unit: z.string().nullable().optional(),
});
export type BiomedicalConceptProperty = z.infer<typeof BiomedicalConceptPropertySchema>;

export const BiomedicalConceptSchema = z.object({
  id: z.string(),
  name: z.string(),
  label: z.string().nullable().optional(),
  conceptCode: z.string().nullable().optional(),
  displayName: z.string().nullable().optional(),
  definition: z.string().nullable().optional(),
  cdashDomain: z.string().nullable().optional(),
  cdashVariable: z.string().nullable().optional(),
  dataType: z.string().default("text"),
  allowableUnits: z.array(z.string()).default([]),
  codelist: z.array(z.string()).default([]),
  properties: z.array(BiomedicalConceptPropertySchema).default([]),
});
export type BiomedicalConcept = z.infer<typeof BiomedicalConceptSchema>;

export const ActivitySchema = z.object({
  id: z.string(),
  name: z.string(),
  description: z.string().nullable().optional(),
  cdashDomain: z.string().nullable().optional(),
  biomedicalConceptCode: z.string().nullable().optional(),
  biomedicalConceptIds: z.array(z.string()).default([]),
  biomedicalConcepts: z.array(BiomedicalConceptSchema).default([]),
  assignedVisitNames: z.array(z.string()).default([]),
  assignedEncounterIds: z.array(z.string()).default([]),
  definedProcedures: z.array(z.record(z.any())).default([]),
});
export type Activity = z.infer<typeof ActivitySchema>;

export const EncounterSchema = z.object({
  id: z.string(),
  name: z.string(),
  encounterType: z.string().default("Visit"),
  targetDay: z.number().int().nullable().optional(),
  windowLower: z.number().int().nullable().optional(),
  windowUpper: z.number().int().nullable().optional(),
  windowLowerDays: z.number().int().nullable().optional(),
  windowUpperDays: z.number().int().nullable().optional(),
  isMandatory: z.boolean().default(true),
  epochId: z.string().nullable().optional(),
  epochName: z.string().nullable().optional(),
  startDate: z.string().nullable().optional(),
  endDate: z.string().nullable().optional(),
});
export type Encounter = z.infer<typeof EncounterSchema>;

export const StudyArmSchema = z.object({
  id: z.string(),
  name: z.string(),
  armType: z.string().default("Treatment"),
  description: z.string().nullable().optional(),
  targetSampleSize: z.number().int().nullable().optional(),
});
export type StudyArm = z.infer<typeof StudyArmSchema>;

export const StudyEpochSchema = z.object({
  id: z.string(),
  name: z.string(),
  epochType: z.string().default("Screening"),
  sequenceNumber: z.number().int().default(1),
  sequenceIndex: z.number().int().default(1),
});
export type StudyEpoch = z.infer<typeof StudyEpochSchema>;

export const StudyDesignSchema = z.object({
  id: z.string(),
  name: z.string(),
  designType: z.string().nullable().optional(),
  arms: z.array(StudyArmSchema).default([]),
  epochs: z.array(StudyEpochSchema).default([]),
  encounters: z.array(EncounterSchema).default([]),
  activities: z.array(ActivitySchema).default([]),
  biomedicalConcepts: z.array(BiomedicalConceptSchema).default([]),
  eligibilityCriteria: z.array(EligibilityCriterionSchema).default([]),
});
export type StudyDesign = z.infer<typeof StudyDesignSchema>;

export const StudyVersionSchema = z.object({
  id: z.string(),
  versionTag: z.string().default("1.0"),
  status: z.string().default("DRAFT"),
  versionIndex: z.number().int().default(1),
  studyDesigns: z.array(StudyDesignSchema).default([]),
});
export type StudyVersion = z.infer<typeof StudyVersionSchema>;

export const USDMStudySchema = z.object({
  id: z.string(),
  name: z.string().default(""),
  protocolTitle: z.string().default(""),
  protocolId: z.string().nullable().optional(),
  phase: z.string().nullable().optional(),
  therapeuticArea: z.string().nullable().optional(),
  usdmVersion: z.string().default("3.0"),
  studyVersions: z.array(StudyVersionSchema).default([]),
  studyDesigns: z.array(StudyDesignSchema).default([]),
  biomedicalConcepts: z.array(BiomedicalConceptSchema).default([]),
});
export type USDMStudy = z.infer<typeof USDMStudySchema>;

export * from "./graph-validator.js";

export const EPROAnswersSchema = z.record(z.any()).superRefine((answers, ctx) => {
  if (answers == null || typeof answers !== "object") {
    ctx.addIssue({
      code: z.ZodIssueCode.custom,
      message: "Answers payload must be an object",
    });
    return;
  }

  if ("age" in answers && answers.age !== null && answers.age !== undefined && answers.age !== "") {
    const rawAge = answers.age;
    const ageNum = typeof rawAge === "number" ? rawAge : Number(rawAge);
    if (typeof rawAge === "boolean" || isNaN(ageNum) || !Number.isInteger(ageNum)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Demographic Validation Error: Participant age must be a valid integer.",
        path: ["age"],
      });
    } else if (ageNum < 18 || ageNum > 110) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Demographic Validation Error: Participant age must be between 18 and 110.",
        path: ["age"],
      });
    }
  }

  if ("gender" in answers && answers.gender !== null && answers.gender !== undefined && answers.gender !== "") {
    const genderStr = String(answers.gender).toUpperCase();
    const validGenders = ["M", "F", "O", "MALE", "FEMALE", "OTHER"];
    if (!validGenders.includes(genderStr)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Demographic Validation Error: Gender must be one of M, F, or O.",
        path: ["gender"],
      });
    }
  }

  if ("pain_score" in answers && answers.pain_score !== null && answers.pain_score !== undefined && answers.pain_score !== "") {
    const rawPain = answers.pain_score;
    const painNum = typeof rawPain === "number" ? rawPain : Number(rawPain);
    if (typeof rawPain === "boolean" || isNaN(painNum) || !Number.isInteger(painNum)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Clinical Validation Error: Pain score must be a valid integer.",
        path: ["pain_score"],
      });
    } else if (painNum < 0 || painNum > 10) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Clinical Validation Error: Pain score must be between 0 and 10.",
        path: ["pain_score"],
      });
    }
  }
});
export type EPROAnswers = z.infer<typeof EPROAnswersSchema>;

export const EPROSubmissionSchema = z.object({
  subject_id: z.string().min(1, "subject_id is required"),
  diary_id: z.string().min(1, "diary_id is required"),
  assignment_id: z.string().optional().nullable(),
  version_index: z.number().int().optional().default(1),
  device_timestamp: z.string().optional(),
  answers: EPROAnswersSchema,
});
export type EPROSubmissionPayload = z.infer<typeof EPROSubmissionSchema>;

export function validateEproPayload(answers: Record<string, any>): { valid: boolean; errors: string[] } {
  const parseResult = EPROAnswersSchema.safeParse(answers || {});
  if (parseResult.success) {
    return { valid: true, errors: [] };
  }
  return {
    valid: false,
    errors: parseResult.error.issues.map((i) => i.message),
  };
}

export function validateEproSubmission(submission: Record<string, any>): { valid: boolean; errors: string[] } {
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
