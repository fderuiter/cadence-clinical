/**
 * Step-up token bindings for batch e-signatures (ADR-2515). A batch route accepts only a token whose `batch_id` is
 * the lower-case hex SHA-256 of exactly the targets it signs, computed the same way as the service.
 */

/** The action and semantic action a delegation-log sign-off token is bound to. */
export const DELEGATION_SIGN_OFF_ACTION =
  "/api/v1/training/delegations/bulk-signoff";
export const DELEGATION_SIGN_OFF_SEMANTIC_ACTION =
  "knowledge.delegation.sign_off";

/** Canonical batch step-up bindings for CTMS startup greenlight and Interop eSource reconciliation. */
export const CTMS_GREENLIGHT_BATCH_CERTIFY_ACTION =
  "/api/v1/ctms/startup/matrix/batch-certify";
export const INTEROP_ESOURCE_BATCH_ACCEPT_ACTION =
  "/api/v1/interop/esource/reconcile/batch-accept";
export const INTEROP_ESOURCE_BATCH_REJECT_ACTION =
  "/api/v1/interop/esource/reconcile/batch-reject";

/** Ordinal (UTF-16 code unit) order, the same as the service's `StringComparer.Ordinal`. */
function ordinalSort(values: string[]): string[] {
  return [...values].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
}

async function sha256Hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(text)
  );
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

/**
 * Deterministic `batch_id` matching `SigTokenBatchId.FromIds` in `packages/security`:
 * trims each ID, discards blanks, sorts in `StringComparer.Ordinal` order, joins with `","`, and returns lowercase SHA-256 hex.
 */
export function computeBatchBindingId(ids: readonly string[]): Promise<string> {
  return sha256Hex(
    ordinalSort(
      ids.map((id) => String(id).trim()).filter((id) => id.length > 0)
    ).join(",")
  );
}

/** `sha256(sortedIds joined with ",")` for the delegation-log sign-off. */
export function delegationBatchBindingId(
  delegationIds: string[]
): Promise<string> {
  return computeBatchBindingId(delegationIds);
}
