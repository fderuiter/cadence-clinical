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