import { sha256 } from "ui";

/**
 * Step-up token bindings for batch e-signatures (ADR-2515). A batch route accepts only a token whose `batch_id` is
 * the lower-case hex SHA-256 of exactly the targets it signs, computed the same way as the service.
 */

/** The action and semantic action a delegation-log sign-off token is bound to. */
export const DELEGATION_SIGN_OFF_ACTION =
  "/api/v1/training/delegations/bulk-signoff";
export const DELEGATION_SIGN_OFF_SEMANTIC_ACTION =
  "knowledge.delegation.sign_off";

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
