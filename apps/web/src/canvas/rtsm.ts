/** Semantic actions required by Execution's electronic signature filter (RandomizationEndpoints.cs). */
export const APPROVE_SCHEME_SEMANTIC_ACTION = "execution.form.approve";
export const RANDOMIZE_SUBJECT_SEMANTIC_ACTION = "execution.subject.randomize";

/** Minimum clinical reason length required by ClinicalSubject.Unblind (ClinicalSubject.cs). */
export const EMERGENCY_UNBLIND_MIN_REASON_LENGTH = 50;

/** Action URL bound into a scheme approval X-Sig-Token. */
export function approveSchemeSignatureAction(schemeId: string): string {
  return `/api/v1/execution/randomization/schemes/${schemeId.trim().toLowerCase()}/approve`;
}

/** Action URL bound into a participant randomization X-Sig-Token. */
export function randomizeSubjectSignatureAction(subjectId: string): string {
  return `/api/v1/execution/subjects/${subjectId.trim()}/randomize`;
}
