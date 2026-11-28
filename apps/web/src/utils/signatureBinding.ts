import { sha256 } from "ui";

/**
 * Step-up token bindings for batch e-signatures (ADR-2515). A batch route accepts only a token whose `batch_id` is
 * the lower-case hex SHA-256 of exactly the targets it signs, computed the same way as the service.
 */

/** The action and semantic action a CTMS Delegation of Authority sign-off token is bound to. */
export const CTMS_DOA_SIGN_OFF_ACTION = "/api/v1/ctms/doa/sign-off";
export const CTMS_DOA_SIGN_OFF_SEMANTIC_ACTION = "ctms.doa.sign_off";

/** Builds the step-up action path and semantic action for a CTMS site DoA PDF export. */
export function ctmsDoaExportPdfAction(siteId: string): string {
  return `/api/v1/ctms/doa/sites/${encodeURIComponent(siteId)}/export-pdf`;
}
export const CTMS_DOA_EXPORT_PDF_SEMANTIC_ACTION = "ctms.doa.export_pdf";

/** Builds the step-up action path and semantic action for personal protocol training attestation. */
export function trainingAssignmentCompleteAction(assignmentId: string): string {
  return `/api/v1/training/assignments/${encodeURIComponent(assignmentId)}/complete`;
}
export const TRAINING_ASSIGNMENT_COMPLETE_SEMANTIC_ACTION =
  "training.assignment.complete";

/** Legacy batch binding helper retained for backward-compatible signature contract tests. */
export const DELEGATION_SIGN_OFF_ACTION = CTMS_DOA_SIGN_OFF_ACTION;
export const DELEGATION_SIGN_OFF_SEMANTIC_ACTION =
  CTMS_DOA_SIGN_OFF_SEMANTIC_ACTION;

/** Ordinal (UTF-16 code unit) order, the same as the service's `StringComparer.Ordinal`. */
function ordinalSort(values: string[]): string[] {
  return [...values].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
}

/** `sha256(sortedIds joined with ",")` for the delegation-log sign-off. */
export function delegationBatchBindingId(
  delegationIds: string[]
): Promise<string> {
  return sha256(
    ordinalSort(delegationIds.map((id) => String(id).trim())).join(",")
  );
}
