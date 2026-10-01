/**
 * Bearer-token attachment for calls to the Cadence API gateway.
 *
 * This is the one place the web app adds `Authorization: Bearer <token>`.
 * `apiClient.js`, the openapi-fetch client and the upload client call
 * `getBearerToken()`; `installAuthorizedFetch()` covers components that still
 * call `fetch("/api/...")` directly. The browser never signs requests and never
 * holds a gateway secret: the gateway validates the token and signs the
 * forwarded identity itself (ADR-2250).
 */

/** Seconds of remaining validity below which the token is refreshed first. */
export const TOKEN_MIN_VALIDITY_SECONDS = 30;

export const getApiBaseUrl = () =>
  import.meta.env?.VITE_API_BASE_URL || "http://localhost:8000";

export async function getBearerToken() {
  let authStore;
  try {
    const { useAuthStore } = await import("../stores/auth");
    authStore = useAuthStore();
  } catch {
    return null; // Pinia not active (e.g. module used outside the app)
  }
  if (typeof authStore.ensureFreshToken === "function") {
    return authStore.ensureFreshToken(TOKEN_MIN_VALIDITY_SECONDS);
  }
  return authStore.accessToken || null;
}

/**
 * True when `url` targets the API gateway: a same-origin `/api/...` path (the
 * Vite dev proxy or a same-origin reverse proxy) or the configured API base
 * URL. Tokens are never attached to any other origin.
 */
export function isGatewayRequest(url, location = globalThis.location) {
  let target;
  try {
    target = new URL(String(url), location?.href || "http://localhost/");
  } catch {
    return false;
  }
  if (location?.origin && target.origin === location.origin) {
    return /(^|\/)api\//.test(target.pathname);
  }
  try {
    const api = new URL(getApiBaseUrl());
    return (
      target.origin === api.origin && target.pathname.startsWith(api.pathname)
    );
  } catch {
    return false;
  }
}

/**
 * Wraps `target.fetch` so gateway-bound requests carry the bearer token.
 * A request that already sets `Authorization` is left unchanged. Idempotent.
 */
export function installAuthorizedFetch(target = globalThis) {
  const original = target.fetch;
  if (typeof original !== "function" || original.__cadenceAuthorized) {
    return;
  }

  const authorizedFetch = async (input, init = {}) => {
    const url = input instanceof Request ? input.url : input;
    if (!isGatewayRequest(url)) {
      return original.call(target, input, init);
    }
    const headers = new Headers(
      init.headers || (input instanceof Request ? input.headers : undefined)
    );
    if (!headers.has("Authorization")) {
      const token = await getBearerToken();
      if (token) {
        headers.set("Authorization", `Bearer ${token}`);
      }
    }
    return original.call(target, input, { ...init, headers });
  };
  authorizedFetch.__cadenceAuthorized = true;
  target.fetch = authorizedFetch;
}
