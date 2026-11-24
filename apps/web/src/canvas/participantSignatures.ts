export const UNBLIND_SUBJECT_SEMANTIC_ACTION = "execution.subject.unblind";
export const WITHDRAW_SUBJECT_SEMANTIC_ACTION = "execution.subject.withdraw";
export const MIN_CHANGE_REASON_LENGTH = 10;
export const MIN_CLINICAL_JUSTIFICATION_LENGTH = 50;

export function subjectStateAction(subjectId: string): string {
  return `/api/v1/execution/subjects/${encodeURIComponent(subjectId.trim())}/state`;
}

export function requiresStepUpSignatureForState(state: string): boolean {
  return state === "UNBLINDED" || state === "WITHDRAWN";
}
