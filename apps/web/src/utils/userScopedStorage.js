/**
 * Browser storage that belongs to one signed-in account (#5369).
 *
 * Clinical values a user has typed but not yet committed (eCRF drafts, the
 * clinical store's persisted form state) are kept under a key that names the
 * tenant and the user, both taken from the verified sign-in token. Nothing is
 * written or read when either is missing: there is no fallback identity.
 *
 * Keys look like `cadence_user:<tenant>:<user>:<rest>`, each segment URI-encoded
 * so a `:` inside an identifier cannot move a key into another account's
 * namespace.
 */

/** Root of every account-scoped key. */
export const USER_SCOPED_ROOT = "cadence_user:";

/**
 * Prefixes older builds wrote clinical or orientation values under without
 * naming the owner: `cadence_ecrf_drafts:<study>:<subject>:<visit>`,
 * `clinical_<persona>_<field>`, and `onboarding_<persona>_<field>`. Their
 * ownership can't be established, so they are discarded rather than migrated.
 */
export const LEGACY_UNOWNED_PREFIXES = Object.freeze([
  "cadence_ecrf_drafts:",
  "clinical_",
  "onboarding_",
]);

function encode(segment) {
  return encodeURIComponent(String(segment));
}

function nonBlank(value) {
  return typeof value === "string" && value.trim() !== "" ? value : null;
}

/**
 * The storage scope of the signed-in account, or null when there is no
 * authenticated user with both a tenant and a user id.
 *
 * @param {{ isAuthenticated?: boolean, user?: { id?: string, tenantId?: string } | null } | null | undefined} authStore
 * @returns {{ tenantId: string, userId: string } | null}
 */
export function storageScopeOf(authStore) {
  if (!authStore || !authStore.isAuthenticated) return null;
  const tenantId = nonBlank(authStore.user?.tenantId);
  const userId = nonBlank(authStore.user?.id);
  if (!tenantId || !userId) return null;
  return { tenantId, userId };
}

/** Key prefix for one account, or null without a complete scope. */
export function scopePrefix(scope) {
  if (!scope || !nonBlank(scope.tenantId) || !nonBlank(scope.userId)) {
    return null;
  }
  return `${USER_SCOPED_ROOT}${encode(scope.tenantId)}:${encode(scope.userId)}:`;
}

/**
 * Builds an account-scoped key from the given segments, or null when the scope
 * is incomplete or any segment is blank.
 */
export function scopedKey(scope, ...segments) {
  const prefix = scopePrefix(scope);
  if (!prefix) return null;
  if (segments.some((s) => s === null || s === undefined || s === "")) {
    return null;
  }
  return prefix + segments.map(encode).join(":");
}

function storage() {
  try {
    return typeof window !== "undefined" ? window.localStorage || null : null;
  } catch {
    return null;
  }
}

/**
 * True when account-scoped browser storage currently holds any saved eCRF draft
 * (`ecrf_form_draft` or `ecrf_draft`) that `purgeUserScopedStorage(null)` would delete.
 *
 * @param {{ tenantId: string, userId: string } | null} [scope]
 * @returns {boolean}
 */
export function hasUserScopedDrafts(scope = null) {
  const ls = storage();
  const prefix = scope ? scopePrefix(scope) : USER_SCOPED_ROOT;
  if (!ls || !prefix) return false;
  try {
    for (let i = 0; i < ls.length; i++) {
      const key = ls.key(i);
      if (
        key &&
        key.startsWith(prefix) &&
        /:ecrf_(?:form_)?draft:/.test(key) &&
        (ls.getItem(key) || "").trim()
      ) {
        return true;
      }
    }
  } catch {
    return false;
  }
  return false;
}

/**
 * Removes account-scoped values that do not belong to `keepScope`, and every
 * legacy value whose owner can't be established. With no `keepScope` (sign-out)
 * every account's values are removed.
 *
 * @param {{ tenantId: string, userId: string } | null} [keepScope]
 * @param {boolean} [purgeClinical=false]
 * @returns {number} how many keys were removed
 */
export function purgeUserScopedStorage(
  keepScope = null,
  purgeClinical = false
) {
  const ls = storage();
  if (!ls) return 0;
  const keep = scopePrefix(keepScope);
  try {
    const doomed = [];
    for (let i = 0; i < ls.length; i++) {
      const key = ls.key(i);
      if (
        key !== null &&
        (LEGACY_UNOWNED_PREFIXES.some((p) => key.startsWith(p)) ||
          (key.startsWith(USER_SCOPED_ROOT) &&
            (!keep ||
              !key.startsWith(keep) ||
              (purgeClinical && key.startsWith(`${keep}clinical:`)))))
      ) {
        doomed.push(key);
      }
    }
    doomed.forEach((key) => ls.removeItem(key));
    return doomed.length;
  } catch {
    return 0;
  }
}
