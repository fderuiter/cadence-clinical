// Lightweight URL query keys and subscription hook for peek deep-links so
// `router/index.js` and `App.vue` can detect peek query parameters without
// statically importing `features/peek.js` on cold boot.
export const PEEK_QUERY_KEYS = Object.freeze([
  "notifId",
  "claimId",
  "txId",
  "ledgerIndex",
  "complianceId",
  "syncSeq",
]);

export function hasPeekQueryParam(query) {
  if (!query || typeof query !== "object") {
    return false;
  }
  return PEEK_QUERY_KEYS.some((key) => {
    const val = query[key];
    return val !== undefined && val !== null && val !== "";
  });
}

let syncPeekQueryHandler = null;

export function registerPeekQuerySync(handler) {
  syncPeekQueryHandler = typeof handler === "function" ? handler : null;
}

export function notifyPeekQueryChange(record) {
  if (typeof syncPeekQueryHandler === "function") {
    syncPeekQueryHandler(record);
  }
}
