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

async function sha256Hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(text)
  );
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

/** `sha256(sortedIds joined with ",")` for the delegation-log sign-off. */
export function delegationBatchBindingId(
  delegationIds: string[]
): Promise<string> {
  return sha256Hex(
    ordinalSort(delegationIds.map((id) => String(id).trim())).join(",")
  );
}

export type SignedFindingDecision = "Accepted" | "Explained";

function stripNodeRefPrefix(refText: string): string {
  const trimmed = String(refText || "").trim();
  const atIndex = trimmed.indexOf("@");
  const unversioned = atIndex < 0 ? trimmed : trimmed.slice(0, atIndex);
  const colonIndex = unversioned.indexOf(":");
  return colonIndex < 0 ? unversioned : unversioned.slice(colonIndex + 1);
}

/**
 * Canonical `FindingDecisionContent.Hash` (`sha256:` + lower-case hex SHA-256 of `{decision,detectedBy,finding,text}`)
 * bound into `batch_id` of step-up `X-Sig-Token` headers for `AcceptFinding` and `ExplainFinding` (K-FIND-2).
 */
export async function findingDecisionHash(
  findingRef: string,
  lastDetectedRunRef: string,
  decision: SignedFindingDecision,
  text: string
): Promise<string> {
  const canonical = JSON.stringify({
    decision,
    detectedBy: stripNodeRefPrefix(lastDetectedRunRef),
    finding: stripNodeRefPrefix(findingRef),
    text,
  });
  return `sha256:${await sha256Hex(canonical)}`;
}
