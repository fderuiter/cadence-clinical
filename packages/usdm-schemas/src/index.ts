// This file is auto-generated from USDM models and OpenAPI specs. DO NOT EDIT DIRECTLY.
import { z } from "zod";

z.config({ jitless: true });

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
  config: z.record(z.string(), z.any()),
  gridSpan: z.number().int().default(12),
  unit: z.string().nullable().optional(),
});
export type BiomedicalConceptProperty = z.infer<
  typeof BiomedicalConceptPropertySchema
>;

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
  definedProcedures: z.array(z.record(z.string(), z.any())).default([]),
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

export const UsdmV4VersionStringSchema = z
  .string()
  .refine((v) => v.trim().split(".")[0] === "4", {
    message: "usdmVersion is not supported; USDM v4 Wrapper requires 4.x.",
  });

export const UsdmV4CodeSchema = z
  .object({
    id: z.string().nullable().optional(),
    code: z.string().min(1),
    codeSystem: z.string().nullable().optional(),
    codeSystemVersion: z.string().nullable().optional(),
    decode: z.string().nullable().optional(),
    instanceType: z.string().nullable().optional(),
  })
  .passthrough();
export type UsdmV4Code = z.infer<typeof UsdmV4CodeSchema>;

export const UsdmV4AliasCodeSchema = z
  .object({
    id: z.string().nullable().optional(),
    standardCode: UsdmV4CodeSchema.nullable().optional(),
    standardCodeAliases: z.array(UsdmV4CodeSchema).optional(),
    code: z.string().nullable().optional(),
    decode: z.string().nullable().optional(),
    instanceType: z.string().nullable().optional(),
  })
  .passthrough();
export type UsdmV4AliasCode = z.infer<typeof UsdmV4AliasCodeSchema>;

export const UsdmV4ResponseCodeSchema = z
  .object({
    id: z.string().nullable().optional(),
    name: z.string().nullable().optional(),
    label: z.string().nullable().optional(),
    isEnabled: z.boolean().nullable().optional(),
    code: z
      .union([UsdmV4CodeSchema, UsdmV4AliasCodeSchema])
      .nullable()
      .optional(),
    instanceType: z.string().nullable().optional(),
  })
  .passthrough();
export type UsdmV4ResponseCode = z.infer<typeof UsdmV4ResponseCodeSchema>;

export const UsdmV4BiomedicalConceptPropertySchema = z
  .object({
    id: z.string().min(1),
    name: z.string().nullable().optional(),
    label: z.string().nullable().optional(),
    isRequired: z.boolean().nullable().optional(),
    isEnabled: z.boolean().nullable().optional(),
    datatype: z.string().nullable().optional(),
    responseCodes: z.array(UsdmV4ResponseCodeSchema).optional(),
    code: z
      .union([UsdmV4AliasCodeSchema, UsdmV4CodeSchema])
      .nullable()
      .optional(),
    unit: z
      .union([z.string(), UsdmV4AliasCodeSchema, UsdmV4CodeSchema])
      .nullable()
      .optional(),
    instanceType: z.string().nullable().optional(),
  })
  .passthrough();
export type UsdmV4BiomedicalConceptProperty = z.infer<
  typeof UsdmV4BiomedicalConceptPropertySchema
>;

export const UsdmV4BiomedicalConceptSchema = z
  .object({
    id: z.string().min(1),
    name: z.string().nullable().optional(),
    label: z.string().nullable().optional(),
    reference: z.string().nullable().optional(),
    code: z
      .union([UsdmV4AliasCodeSchema, UsdmV4CodeSchema])
      .nullable()
      .optional(),
    properties: z.array(UsdmV4BiomedicalConceptPropertySchema).optional(),
    instanceType: z.string().nullable().optional(),
  })
  .passthrough();
export type UsdmV4BiomedicalConcept = z.infer<
  typeof UsdmV4BiomedicalConceptSchema
>;

export const UsdmV4StudyTitleSchema = z
  .object({
    id: z.string().nullable().optional(),
    text: z.string(),
    type: z
      .union([UsdmV4CodeSchema, UsdmV4AliasCodeSchema, z.string()])
      .nullable()
      .optional(),
    instanceType: z.string().nullable().optional(),
  })
  .passthrough();
export type UsdmV4StudyTitle = z.infer<typeof UsdmV4StudyTitleSchema>;

export const UsdmV4StudyDesignSchema = z
  .object({
    id: z.string().min(1),
    name: z.string().nullable().optional(),
    label: z.string().nullable().optional(),
    description: z.string().nullable().optional(),
    arms: z.array(z.record(z.string(), z.any())).optional(),
    epochs: z.array(z.record(z.string(), z.any())).optional(),
    encounters: z.array(z.record(z.string(), z.any())).optional(),
    activities: z.array(z.record(z.string(), z.any())).optional(),
    scheduleTimelines: z.array(z.record(z.string(), z.any())).optional(),
    objectives: z.array(z.record(z.string(), z.any())).optional(),
    estimands: z.array(z.record(z.string(), z.any())).optional(),
    eligibilityCriteria: z.array(z.record(z.string(), z.any())).optional(),
    analysisPopulations: z.array(z.record(z.string(), z.any())).optional(),
    studyCells: z.array(z.record(z.string(), z.any())).optional(),
    elements: z.array(z.record(z.string(), z.any())).optional(),
    studyInterventionIds: z.array(z.string()).optional(),
    instanceType: z.string().nullable().optional(),
  })
  .passthrough();
export type UsdmV4StudyDesign = z.infer<typeof UsdmV4StudyDesignSchema>;

export const UsdmV4StudyVersionSchema = z
  .object({
    id: z.string().min(1),
    versionIdentifier: z.string().nullable().optional(),
    rationale: z.string().nullable().optional(),
    titles: z.array(UsdmV4StudyTitleSchema).optional(),
    biomedicalConcepts: z.array(UsdmV4BiomedicalConceptSchema).optional(),
    eligibilityCriterionItems: z
      .array(z.record(z.string(), z.any()))
      .optional(),
    studyInterventions: z.array(z.record(z.string(), z.any())).optional(),
    studyDesigns: z
      .array(UsdmV4StudyDesignSchema)
      .min(1, "The study version must contain at least one study design."),
    instanceType: z.string().nullable().optional(),
  })
  .passthrough();
export type UsdmV4StudyVersion = z.infer<typeof UsdmV4StudyVersionSchema>;

export const UsdmV4StudySchema = z
  .object({
    id: z.string().nullable().optional(),
    name: z.string().nullable().optional(),
    description: z.string().nullable().optional(),
    label: z.string().nullable().optional(),
    versions: z
      .array(UsdmV4StudyVersionSchema)
      .min(1, "The study must contain at least one study version."),
    documentedBy: z.array(z.record(z.string(), z.any())).optional(),
    instanceType: z.string().nullable().optional(),
  })
  .passthrough();
export type UsdmV4Study = z.infer<typeof UsdmV4StudySchema>;

export const WrapperSchema = z
  .object({
    study: UsdmV4StudySchema,
    usdmVersion: UsdmV4VersionStringSchema,
    systemName: z.string().nullable().optional(),
    systemVersion: z.string().nullable().optional(),
  })
  .passthrough();
export type Wrapper = z.infer<typeof WrapperSchema>;

export const USDMV3StudySchema = z.object({
  id: z.string(),
  name: z.string().default(""),
  protocolTitle: z.string().default(""),
  protocolId: z.string().nullable().optional(),
  phase: z.string().nullable().optional(),
  therapeuticArea: z.string().nullable().optional(),
  usdmVersion: z
    .string()
    .default("3.0")
    .refine((v) => v.trim().split(".")[0] === "3", {
      message:
        "USDM v3 root study payload requires usdmVersion 3.x; USDM 4.x payloads must use a Wrapper with 'study'.",
    }),
  studyVersions: z.array(StudyVersionSchema).default([]),
  studyDesigns: z.array(StudyDesignSchema).default([]),
  biomedicalConcepts: z.array(BiomedicalConceptSchema).default([]),
});
export type USDMV3Study = z.infer<typeof USDMV3StudySchema>;

function isUsdmV4Candidate(input: unknown): boolean {
  if (!input || typeof input !== "object" || Array.isArray(input)) {
    return false;
  }
  const obj = input as Record<string, unknown>;
  if ("study" in obj) {
    return true;
  }
  if (typeof obj.usdmVersion === "string") {
    const major = obj.usdmVersion.trim().split(".")[0];
    if (major !== "3") {
      return true;
    }
  }
  return false;
}

export const USDMStudySchema: z.ZodType<Wrapper | USDMV3Study> = z
  .any()
  .superRefine((val, ctx) => {
    if (!val || typeof val !== "object" || Array.isArray(val)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "The document is not a USDM Study or Wrapper object.",
      });
      return;
    }
    const targetSchema = isUsdmV4Candidate(val)
      ? WrapperSchema
      : USDMV3StudySchema;
    const parsed = targetSchema.safeParse(val);
    if (!parsed.success) {
      for (const issue of parsed.error.issues) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: issue.message,
          path: issue.path,
        });
      }
    }
  })
  .transform((val) =>
    isUsdmV4Candidate(val)
      ? WrapperSchema.parse(val)
      : USDMV3StudySchema.parse(val)
  );
export type USDMStudy = z.infer<typeof USDMStudySchema>;

export * from "./graph-validator.js";
export * from "./epro.js";
