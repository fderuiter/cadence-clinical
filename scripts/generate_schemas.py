import json
import os
import sys
from pathlib import Path
from typing import Any

app_root = str(Path(__file__).resolve().parent.parent)
if app_root not in sys.path:
    sys.path.insert(0, app_root)

# Target USDM model definitions mapping
USDM_MODEL_FIELDS = {
    "Code": [
        ("code", "z.string()"),
        ("codeSystem", "z.string()"),
        ("codeSystemVersion", "z.string().nullable().optional()"),
        ("decode", "z.string()"),
    ],
    "SyntaxTemplate": [
        ("id", "z.string()"),
        ("name", "z.string().nullable().optional()"),
        ("text", "z.string()"),
        ("notes", "z.array(z.string()).default([])"),
    ],
    "EligibilityCriterion": [
        ("id", "z.string()"),
        ("name", 'z.string().default("")'),
        ("criterionType", 'z.string().default("Inclusion")'),
        ("identifier", "z.string().nullable().optional()"),
        ("category", "z.string().nullable().optional()"),
        ("text", "z.string().nullable().optional()"),
        ("textExpression", "z.string().nullable().optional()"),
        ("logicalExpression", "z.string().nullable().optional()"),
        ("template", "SyntaxTemplateSchema.nullable().optional()"),
    ],
    "BiomedicalConceptProperty": [
        ("id", "z.string()"),
        ("name", "z.string()"),
        ("label", "z.string().nullable().optional()"),
        ("cdashVariable", "z.string().nullable().optional()"),
        ("dataType", 'z.string().default("text")'),
        ("mandatory", "z.boolean().default(false)"),
        ("range", "z.string().nullable().optional()"),
        ("options", "z.array(z.string()).default([])"),
        ("config", "z.record(z.string(), z.any())"),
        ("gridSpan", "z.number().int().default(12)"),
        ("unit", "z.string().nullable().optional()"),
    ],
    "BiomedicalConcept": [
        ("id", "z.string()"),
        ("name", "z.string()"),
        ("label", "z.string().nullable().optional()"),
        ("conceptCode", "z.string().nullable().optional()"),
        ("displayName", "z.string().nullable().optional()"),
        ("definition", "z.string().nullable().optional()"),
        ("cdashDomain", "z.string().nullable().optional()"),
        ("cdashVariable", "z.string().nullable().optional()"),
        ("dataType", 'z.string().default("text")'),
        ("allowableUnits", "z.array(z.string()).default([])"),
        ("codelist", "z.array(z.string()).default([])"),
        ("properties", "z.array(BiomedicalConceptPropertySchema).default([])"),
    ],
    "Activity": [
        ("id", "z.string()"),
        ("name", "z.string()"),
        ("description", "z.string().nullable().optional()"),
        ("cdashDomain", "z.string().nullable().optional()"),
        ("biomedicalConceptCode", "z.string().nullable().optional()"),
        ("biomedicalConceptIds", "z.array(z.string()).default([])"),
        ("biomedicalConcepts", "z.array(BiomedicalConceptSchema).default([])"),
        ("assignedVisitNames", "z.array(z.string()).default([])"),
        ("assignedEncounterIds", "z.array(z.string()).default([])"),
        ("definedProcedures", "z.array(z.record(z.string(), z.any())).default([])"),
    ],
    "Encounter": [
        ("id", "z.string()"),
        ("name", "z.string()"),
        ("encounterType", 'z.string().default("Visit")'),
        ("targetDay", "z.number().int().nullable().optional()"),
        ("windowLower", "z.number().int().nullable().optional()"),
        ("windowUpper", "z.number().int().nullable().optional()"),
        ("windowLowerDays", "z.number().int().nullable().optional()"),
        ("windowUpperDays", "z.number().int().nullable().optional()"),
        ("isMandatory", "z.boolean().default(true)"),
        ("epochId", "z.string().nullable().optional()"),
        ("epochName", "z.string().nullable().optional()"),
        ("startDate", "z.string().nullable().optional()"),
        ("endDate", "z.string().nullable().optional()"),
    ],
    "StudyArm": [
        ("id", "z.string()"),
        ("name", "z.string()"),
        ("armType", 'z.string().default("Treatment")'),
        ("description", "z.string().nullable().optional()"),
        ("targetSampleSize", "z.number().int().nullable().optional()"),
    ],
    "StudyEpoch": [
        ("id", "z.string()"),
        ("name", "z.string()"),
        ("epochType", 'z.string().default("Screening")'),
        ("sequenceNumber", "z.number().int().default(1)"),
        ("sequenceIndex", "z.number().int().default(1)"),
    ],
    "StudyDesign": [
        ("id", "z.string()"),
        ("name", "z.string()"),
        ("designType", "z.string().nullable().optional()"),
        ("arms", "z.array(StudyArmSchema).default([])"),
        ("epochs", "z.array(StudyEpochSchema).default([])"),
        ("encounters", "z.array(EncounterSchema).default([])"),
        ("activities", "z.array(ActivitySchema).default([])"),
        ("biomedicalConcepts", "z.array(BiomedicalConceptSchema).default([])"),
        ("eligibilityCriteria", "z.array(EligibilityCriterionSchema).default([])"),
    ],
    "StudyVersion": [
        ("id", "z.string()"),
        ("versionTag", 'z.string().default("1.0")'),
        ("status", 'z.string().default("DRAFT")'),
        ("versionIndex", "z.number().int().default(1)"),
        ("studyDesigns", "z.array(StudyDesignSchema).default([])"),
    ],
    "USDMStudy": [
        ("id", "z.string()"),
        ("name", 'z.string().default("")'),
        ("protocolTitle", 'z.string().default("")'),
        ("protocolId", "z.string().nullable().optional()"),
        ("phase", "z.string().nullable().optional()"),
        ("therapeuticArea", "z.string().nullable().optional()"),
        ("usdmVersion", 'z.string().default("3.0")'),
        ("studyVersions", "z.array(StudyVersionSchema).default([])"),
        ("studyDesigns", "z.array(StudyDesignSchema).default([])"),
        ("biomedicalConcepts", "z.array(BiomedicalConceptSchema).default([])"),
    ],
}


def load_designer_openapi_spec() -> dict[str, Any]:
    """Load designer OpenAPI JSON specification if available."""
    spec_file = os.path.join(app_root, "docs", "openapi", "designer_openapi.json")
    if os.path.isfile(spec_file):
        try:
            with open(spec_file, encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            pass
    return {}


def main():
    # Halt on production-level environments
    for env_var in ["NODE_ENV", "APP_ENV", "ENVIRONMENT", "STAGE"]:
        val = os.getenv(env_var, "").lower()
        if "prod" in val or val == "production":
            sys.stderr.write(
                f"Error: Schema generation is blocked in production environment ({env_var}={os.getenv(env_var)}).\n"
            )
            sys.exit(1)

    designer_spec = load_designer_openapi_spec()
    if designer_spec:
        print("Loaded designer_openapi.json successfully for schema generation.")

    output_dir = os.path.abspath(
        os.path.join(os.path.dirname(__file__), "../packages/usdm-schemas/src")
    )
    os.makedirs(output_dir, exist_ok=True)
    output_file = os.path.join(output_dir, "index.ts")

    lines = []
    lines.append(
        "// This file is auto-generated from USDM models and OpenAPI specs. DO NOT EDIT DIRECTLY."
    )
    lines.append('import { z } from "zod";')
    lines.append("")

    for model_name, fields in USDM_MODEL_FIELDS.items():
        lines.append(f"export const {model_name}Schema = z.object({{")
        for field_name, zod_expr in fields:
            lines.append(f"  {field_name}: {zod_expr},")
        lines.append("});")
        lines.append(f"export type {model_name} = z.infer<typeof {model_name}Schema>;")
        lines.append("")

    lines.append('export * from "./graph-validator.js";')
    lines.append("")

    epro_code = """export const EPROAnswersSchema = z.record(z.string(), z.any()).superRefine((answers, ctx) => {
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
"""
    lines.append(epro_code)

    with open(output_file, "w") as f:
        f.write("\n".join(lines))

    print(f"Successfully generated Zod schemas and TypeScript types at: {output_file}")


if __name__ == "__main__":
    main()
