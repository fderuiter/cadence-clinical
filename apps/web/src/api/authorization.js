import { isStepUpAuthFailure } from "ui";
import { useAuthStore } from "../stores/auth";
import { resolveGatewayBaseUrl } from "../utils/demoMode";

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

/** Canonical RFC 9457 error code returned when a tenant's trial has expired. */
export const KERNEL_SUBSCRIPTION_EXPIRED_CODE = "K.Subscription.Expired";

/** Actionable user-facing upgrade guidance when a write is blocked by trial expiry. */
export const SUBSCRIPTION_EXPIRED_GUIDANCE =
  "Workspace trial has expired (Read & Export mode). Clinical reads, regulated exports, and electronic signatures remain available; complete study scoping on Plans to activate full write access.";

/**
 * True when an HTTP error payload represents an expired tenant subscription (`K.Subscription.Expired`).
 *
 * @param {number|null|undefined} status
 * @param {unknown} errorBody
 * @returns {boolean}
 */
export function isSubscriptionExpiredProblem(status, errorBody) {
  if (!errorBody || typeof errorBody !== "object") {
    return false;
  }
  const code = /** @type {{ code?: unknown }} */ (errorBody).code;
  if (code !== KERNEL_SUBSCRIPTION_EXPIRED_CODE) {
    return false;
  }
  return status === 403 || status == null || status === 0;
}

/**
 * Synchronises the auth store's expired-subscription state when a mutation fails
 * with `403 K.Subscription.Expired` and returns actionable upgrade guidance.
 *
 * @param {number|null|undefined} status
 * @param {unknown} errorBody
 * @returns {string|null}
 */
export function notifySubscriptionExpiredIfNeeded(status, errorBody) {
  if (!isSubscriptionExpiredProblem(status, errorBody)) {
    return null;
  }
  const detail =
    typeof (/** @type {{ detail?: unknown }} */ (errorBody).detail) ===
      "string" &&
    /** @type {{ detail: string }} */ (errorBody).detail.trim().length > 0
      ? /** @type {{ detail: string }} */ (errorBody).detail.trim()
      : null;
  try {
    const authStore = useAuthStore();
    if (typeof authStore.markSubscriptionExpired === "function") {
      authStore.markSubscriptionExpired(
        detail || SUBSCRIPTION_EXPIRED_GUIDANCE
      );
    }
  } catch {
    // Pinia store not active (e.g. unit test outside app context)
  }
  return SUBSCRIPTION_EXPIRED_GUIDANCE;
}

export function getApiBaseUrl() {
  return resolveGatewayBaseUrl();
}

/**
 * Returns a fresh access token for the signed-in user, or null when no one is
 * signed in (the gateway then answers 401 and the router sends the user to
 * sign in).
 */
export async function getBearerToken() {
  let authStore;
  try {
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
 * Attempts one single-flight OIDC refresh after an HTTP 401 gateway response.
 * Ignores 21 CFR Part 11 step-up credential or `X-Sig-Token` failures so a
 * wrong signature password never logs the clinician out of their workspace.
 *
 * @param {string|URL} url
 * @param {unknown} [errorBody=null]
 * @returns {Promise<string|null>}
 */
export async function refreshBearerTokenOnUnauthorized(url, errorBody = null) {
  if (isStepUpAuthFailure(url, 401, errorBody)) {
    return null;
  }
  let authStore;
  try {
    authStore = useAuthStore();
  } catch {
    return null;
  }
  if (typeof authStore.refreshAfterUnauthorized === "function") {
    return authStore.refreshAfterUnauthorized();
  }
  return null;
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
 * Reconstructs a `Request` with an updated URL and/or headers while preserving
 * non-empty request bodies across 401 retries or dynamic base-URL rewrites.
 *
 * @param {Request} source
 * @param {string} [url=source.url]
 * @param {HeadersInit} [headers=source.headers]
 * @returns {Promise<Request>}
 */
export async function rebuildRequest(
  source,
  url = source.url,
  headers = source.headers
) {
  const hasBody =
    source.body !== null && source.method !== "GET" && source.method !== "HEAD";
  const body = hasBody ? await source.arrayBuffer() : undefined;
  return new Request(url, {
    method: source.method,
    headers,
    body: body && body.byteLength > 0 ? body : undefined,
    credentials: source.credentials,
    mode: source.mode,
    cache: source.cache,
    redirect: source.redirect,
    referrer: source.referrer,
    referrerPolicy: source.referrerPolicy,
    integrity: source.integrity,
    keepalive: source.keepalive,
    signal: source.signal,
  });
}

/**
 * Executes a `fetch` call and performs at most one single-flight OIDC token refresh
 * and retry when the gateway answers HTTP 401 (ignoring 21 CFR Part 11 step-up failures).
 *
 * @param {RequestInfo | URL} input
 * @param {RequestInit} [init]
 * @param {{
 *   fetchImpl?: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>,
 *   skipRefresh?: boolean,
 * }} [options]
 * @returns {Promise<Response>}
 */
export async function fetchWithUnauthorizedRetry(
  input,
  init,
  {
    fetchImpl = (reqInput, reqInit) => globalThis.fetch(reqInput, reqInit),
    skipRefresh = false,
  } = {}
) {
  const url = input instanceof Request ? input.url : String(input);
  const retryClone =
    !skipRefresh && input instanceof Request ? input.clone() : null;
  const response = await fetchImpl(input, init);
  if (response.status !== 401 || skipRefresh) {
    return response;
  }

  const errBody = await response
    .clone()
    .json()
    .catch(() => null);
  const freshToken = await refreshBearerTokenOnUnauthorized(url, errBody);
  if (!freshToken) {
    return response;
  }

  if (retryClone) {
    const retryHeaders = new Headers(retryClone.headers);
    if (init?.headers) {
      new Headers(init.headers).forEach((value, key) => {
        retryHeaders.set(key, value);
      });
    }
    retryHeaders.set("Authorization", `Bearer ${freshToken}`);
    const retriedRequest = await rebuildRequest(
      retryClone,
      retryClone.url,
      retryHeaders
    );
    const retryInit = init ? { ...init, headers: retryHeaders } : undefined;
    return fetchImpl(retriedRequest, retryInit);
  }

  const retryHeaders = new Headers(init?.headers);
  retryHeaders.set("Authorization", `Bearer ${freshToken}`);
  return fetchImpl(input, { ...init, headers: retryHeaders });
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
    const hadExplicitAuth = headers.has("Authorization");
    if (!hadExplicitAuth) {
      const token = await getBearerToken();
      if (token) {
        headers.set("Authorization", `Bearer ${token}`);
      }
    }
    const response = await fetchWithUnauthorizedRetry(
      input,
      { ...init, headers },
      {
        fetchImpl: (reqInput, reqInit) =>
          original.call(target, reqInput, reqInit),
        skipRefresh: hadExplicitAuth,
      }
    );
    if (response.status === 403) {
      const errBody = await response
        .clone()
        .json()
        .catch(() => null);
      notifySubscriptionExpiredIfNeeded(403, errBody);
    }
    return response;
  };
  authorizedFetch.__cadenceAuthorized = true;
  target.fetch = authorizedFetch;
}
