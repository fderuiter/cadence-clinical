// Static CDASH v1.3 and CDASHIG v2.3 TypeScript interfaces.
// Exact property quoting in CDASHModelVariable and CDASHIGVariable is preserved per ADR-328.

import type { CdashVariableCode } from "../generated/cdashVariables.js";

export type { CdashVariableCode };

/**
 * Represents a variable definition in the CDASH Model v1.3
 */
export interface CDASHModelVariable {
  "Version": string;
  "Class": string;
  "Domain": string | null;
  "Variable Order": number | null;
  "CDASH Variable": string;
  "CDASH Variable Label": string;
  "DRAFT CDASH Definition"?: string | null;
  "Domain Specific"?: boolean | null;
  "Question Text"?: string | null;
  "Prompt"?: string | null;
  "Type": string;
  "SDTM Target"?: string | null;
  "Mapping Instructions"?: string | null;
  "Controlled Terminology Codelist Code"?: string | null;
  "Implementation Notes"?: string | null;
}

/**
 * Represents a variable definition in the CDASHIG v2.3
 */
export interface CDASHIGVariable {
  "Version": string;
  "Class": string;
  "Domain": string;
  "Data Collection Scenario/Implementation Option"?: string | null;
  "Variable Order": number | null;
  "CDASHIG Variable": string;
  "CDASHIG Variable Label": string;
  "DRAFT CDASHIG Definition"?: string | null;
  "Question Text"?: string | null;
  "Prompt"?: string | null;
  "Type": string;
  "CDASHIG Core": "HR" | "R" | "O" | string;
  "Case Report Form Completion Instructions"?: string | null;
  "SDTMIG Target"?: string | null;
  "Mapping Instructions"?: string | null;
  "CDISC CT Codelist Code(s), Subset Codes(s)"?: string | null;
  "CDISC CT Codelist Submission Values(s), Subset Submission Value(s)"?: string | null;
  "Implementation Notes"?: string | null;
}

/**
 * Strict interface definition for clinical fields used by form components.
 * Restricts input to valid CDASH field properties.
 */
export interface CdashField {
  id: string;
  label: string;
  type: "radio" | "choice_single" | "concept_code" | "text" | string;
  cdash?: CdashVariableCode | null;
  gridSpan?: number | string;
  options?: any[] | null;
  required?: boolean;
  [key: string]: any;
}
