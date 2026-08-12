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
  name: z.string(),
  criterionType: z.string(),
  category: z.string().nullable().optional(),
  text: z.string().nullable().optional(),
  template: SyntaxTemplateSchema.nullable().optional(),
});
export type EligibilityCriterion = z.infer<typeof EligibilityCriterionSchema>;

export const ActivitySchema = z.object({
  id: z.string(),
  name: z.string(),
  description: z.string().nullable().optional(),
  definedProcedures: z.array(z.record(z.any())).default([]),
});
export type Activity = z.infer<typeof ActivitySchema>;

export const EncounterSchema = z.object({
  id: z.string(),
  name: z.string(),
  encounterType: z.string().default("Visit"),
  startDate: z.string().nullable().optional(),
  endDate: z.string().nullable().optional(),
});
export type Encounter = z.infer<typeof EncounterSchema>;

export const StudyArmSchema = z.object({
  id: z.string(),
  name: z.string(),
  armType: z.string().default("Treatment"),
  description: z.string().nullable().optional(),
});
export type StudyArm = z.infer<typeof StudyArmSchema>;

export const StudyEpochSchema = z.object({
  id: z.string(),
  name: z.string(),
  epochType: z.string().default("Screening"),
  sequenceNumber: z.number().int().default(1),
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
  eligibilityCriteria: z.array(EligibilityCriterionSchema).default([]),
});
export type StudyDesign = z.infer<typeof StudyDesignSchema>;

export const USDMStudySchema = z.object({
  id: z.string(),
  name: z.string(),
  protocolTitle: z.string(),
  usdmVersion: z.string().default("3.0"),
  studyDesigns: z.array(StudyDesignSchema).default([]),
});
export type USDMStudy = z.infer<typeof USDMStudySchema>;
