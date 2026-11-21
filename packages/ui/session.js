/**
 * Shared OIDC session coordinator for Cadence Clinical web and participant applications.
 *
 * Centralizes Keycloak token-expiration and refresh hooks, deduplicates concurrent
 * `updateToken` calls (single-flight), distinguishes step-up signature re-authentication
 * failures from OIDC bearer session expiration, and preserves safe internal return URLs
 * without persisting raw bearer tokens or unwrapped encryption keys in web storage.
 */

export const DEFAULT_AUTH_RETURN_URL_KEY = "cadence_auth_return_url";

function resolveSessionStorage(explicitStorage) {
  if (explicitStorage !== undefined) {
    return explicitStorage;
  }
  try {
    if (typeof window !== "undefined" && window.sessionStorage) {
      return window.sessionStorage;
    }
  } catch {
    // Ignore storage access errors in restricted sandboxes.
  }
  return null;
}

/**
 * Normalizes a candidate return URL into a safe, app-local relative path (`pathname + search + hash`).
 * Rejects protocol-relative URLs, external origins, and `/login` redirect loops.
 *
 * @param {string|null|undefined} rawUrl
 * @param {{ baseOrigin?: string }} [options]
 * @returns {string|null}
 */
export function sanitizeReturnUrl(rawUrl, options = {}) {
  if (typeof rawUrl !== "string") {
    return null;
  }
  const trimmed = rawUrl.trim();
  if (!trimmed) {
    return null;
  }

  const baseOrigin =
    options.baseOrigin ||
    (typeof window !== "undefined" && window.location?.origin
      ? window.location.origin
      : "http://localhost");

  let normalizedPath = trimmed;
  if (/^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)) {
    try {
      const parsed = new URL(trimmed);
      if (parsed.origin !== baseOrigin) {
        return null;
      }
      normalizedPath = `${parsed.pathname}${parsed.search}${parsed.hash}`;
    } catch {
      return null;
    }
  }

  if (!normalizedPath.startsWith("/") || normalizedPath.startsWith("//")) {
    return null;
  }

  const pathOnly = normalizedPath.split(/[?#]/, 1)[0] || "/";
  if (pathOnly === "/login" || pathOnly.endsWith("/login")) {
    return null;
  }

  return normalizedPath;
}

/**
 * Saves the sanitized return URL in `sessionStorage` so post-login navigation
 * can return the user to the exact route they were viewing when their token expired.
 *
 * @param {string|null|undefined} url
 * @param {{ storage?: Storage|null, key?: string, baseOrigin?: string }} [options]
 * @returns {string|null}
 */
export function saveAuthReturnUrl(url, options = {}) {
  const sanitized = sanitizeReturnUrl(url, { baseOrigin: options.baseOrigin });
  if (!sanitized) {
    return null;
  }
  const storage = resolveSessionStorage(options.storage);
  const key = options.key || DEFAULT_AUTH_RETURN_URL_KEY;
  if (storage && typeof storage.setItem === "function") {
    try {
      storage.setItem(key, sanitized);
    } catch {
      // Ignore quota or sandbox storage errors.
    }
  }
  return sanitized;
}

/**
 * Reads the saved return URL without clearing it.
 *
 * @param {{ storage?: Storage|null, key?: string, baseOrigin?: string }} [options]
 * @returns {string|null}
 */
export function peekAuthReturnUrl(options = {}) {
  const storage = resolveSessionStorage(options.storage);
  const key = options.key || DEFAULT_AUTH_RETURN_URL_KEY;
  if (!storage || typeof storage.getItem !== "function") {
    return null;
  }
  try {
    return sanitizeReturnUrl(storage.getItem(key), {
      baseOrigin: options.baseOrigin,
    });
  } catch {
    return null;
  }
}

/**
 * Reads and clears the saved return URL from `sessionStorage`.
 *
 * @param {{ storage?: Storage|null, key?: string, baseOrigin?: string }} [options]
 * @returns {string|null}
 */
export function consumeAuthReturnUrl(options = {}) {
  const storage = resolveSessionStorage(options.storage);
  const key = options.key || DEFAULT_AUTH_RETURN_URL_KEY;
  if (!storage || typeof storage.getItem !== "function") {
    return null;
  }
  try {
    const raw = storage.getItem(key);
    if (typeof storage.removeItem === "function") {
      storage.removeItem(key);
    }
    return sanitizeReturnUrl(raw, { baseOrigin: options.baseOrigin });
  } catch {
    return null;
  }
}

/**
 * Clears any stored return URL (used during confirmed voluntary sign-out).
 *
 * @param {{ storage?: Storage|null, key?: string }} [options]
 */
export function clearAuthReturnUrl(options = {}) {
  const storage = resolveSessionStorage(options.storage);
  const key = options.key || DEFAULT_AUTH_RETURN_URL_KEY;
  if (storage && typeof storage.removeItem === "function") {
    try {
      storage.removeItem(key);
    } catch {
      // Ignore storage errors.
    }
  }
}

/**
 * Resolves an explicit absolute OIDC `redirectUri` under the current application origin.
 *
 * @param {string|null|undefined} targetPathOrUrl
 * @param {{ origin?: string, basePath?: string }} [options]
 * @returns {string}
 */
export function resolveOidcRedirectUri(targetPathOrUrl, options = {}) {
  const origin =
    options.origin ||
    (typeof window !== "undefined" && window.location?.origin
      ? window.location.origin
      : "http://localhost");
  const rawBasePath = options.basePath || "/";
  const normalizedBase = (
    sanitizeReturnUrl(rawBasePath, { baseOrigin: origin }) ||
    (rawBasePath.startsWith("/") ? rawBasePath : `/${rawBasePath}`)
  )
    .split(/[?#]/, 1)[0]
    .replace(/\/+$/, "");

  const sanitized = sanitizeReturnUrl(targetPathOrUrl, { baseOrigin: origin });
  const fallback = normalizedBase ? `${normalizedBase}/` : "/";

  if (!sanitized) {
    return new URL(fallback, origin).toString();
  }

  let resolvedPath = sanitized;
  if (
    normalizedBase &&
    sanitized !== normalizedBase &&
    !sanitized.startsWith(`${normalizedBase}/`) &&
    !sanitized.startsWith(`${normalizedBase}?`) &&
    !sanitized.startsWith(`${normalizedBase}#`)
  ) {
    resolvedPath = `${normalizedBase}${sanitized}`;
  }

  return new URL(resolvedPath, origin).toString();
}

/**
 * Determines whether an HTTP 401 response represents a 21 CFR Part 11 step-up
 * signature credential/token error rather than an expired OIDC bearer session.
 *
 * @param {string|URL|null|undefined} url
 * @param {number} status
 * @param {any} [errorBody=null]
 * @returns {boolean}
 */
export function isStepUpAuthFailure(url, status, errorBody = null) {
  if (status !== 401) {
    return false;
  }
  const urlText = String(url || "");
  if (urlText.includes("/api/v1/auth/signature-verification")) {
    return true;
  }
  const code = String(
    errorBody?.code || errorBody?.error_code || errorBody?.error || ""
  ).toUpperCase();
  if (
    code === "INVALID_CREDENTIALS" ||
    code === "REAUTHENTICATION_REQUIRED" ||
    code === "SIGNATURE_TOKEN_INVALID" ||
    code === "SIGNATURE_TOKEN_EXPIRED"
  ) {
    return true;
  }
  const detailText = String(
    errorBody?.detail || errorBody?.message || errorBody?.title || ""
  );
  return detailText.includes("X-Sig-Token");
}

function defaultCurrentReturnUrl() {
  if (typeof window === "undefined" || !window.location) {
    return null;
  }
  return `${window.location.pathname || "/"}${window.location.search || ""}${window.location.hash || ""}`;
}

function scrubKeycloakTokens(keycloakInstance) {
  if (!keycloakInstance || typeof keycloakInstance !== "object") {
    return;
  }
  keycloakInstance.token = undefined;
  keycloakInstance.refreshToken = undefined;
  keycloakInstance.idToken = undefined;
  keycloakInstance.tokenParsed = undefined;
  keycloakInstance.refreshTokenParsed = undefined;
  keycloakInstance.idTokenParsed = undefined;
  keycloakInstance.authenticated = false;
}

/**
 * Creates a single-flight OIDC session coordinator that wires `onTokenExpired`
 * and `onAuthRefreshSuccess`, deduplicates concurrent `updateToken` requests,
 * and handles involuntary session expiry without destroying local drafts.
 *
 * @param {{
 *   getKeycloak?: () => any,
 *   minValiditySeconds?: number,
 *   forceRefreshMinValiditySeconds?: number,
 *   onTokenRefreshed?: (token: string | null, keycloak: any) => void,
 *   onSessionExpired?: (context: { reason: string, error?: any, returnUrl: string | null }) => void,
 *   onAuthLogout?: () => void,
 *   getCurrentReturnUrl?: () => string | null,
 *   storage?: Storage | null,
 *   returnUrlKey?: string,
 * }} [options]
 */
export function createOidcSessionCoordinator(options = {}) {
  const minValiditySeconds =
    typeof options.minValiditySeconds === "number"
      ? options.minValiditySeconds
      : 30;
  const forceRefreshMinValiditySeconds =
    typeof options.forceRefreshMinValiditySeconds === "number"
      ? options.forceRefreshMinValiditySeconds
      : -1;
  const getCurrentReturnUrl =
    options.getCurrentReturnUrl || defaultCurrentReturnUrl;

  let attachedKeycloak = null;
  let lastObservedKeycloak = null;
  let inflightRefreshPromise = null;
  let inflightRequestedValidity = null;
  let sessionExpired = false;
  let voluntaryLogoutInProgress = false;
  let expiringInProgress = false;

  function resolveKeycloak() {
    const dynamicKc = options.getKeycloak ? options.getKeycloak() : undefined;
    const current = dynamicKc !== undefined ? dynamicKc : attachedKeycloak;
    if (current !== lastObservedKeycloak) {
      lastObservedKeycloak = current;
      if (!current || (current.authenticated && current.token)) {
        sessionExpired = false;
        voluntaryLogoutInProgress = false;
      }
    }
    return current || null;
  }

  function markSessionExpired({
    reason = "expired",
    error = null,
    returnUrl,
  } = {}) {
    if (expiringInProgress || voluntaryLogoutInProgress) {
      return peekAuthReturnUrl({
        storage: options.storage,
        key: options.returnUrlKey,
      });
    }
    expiringInProgress = true;
    try {
      sessionExpired = true;
      const kc = resolveKeycloak();
      scrubKeycloakTokens(kc);

      const candidateUrl =
        returnUrl !== undefined ? returnUrl : getCurrentReturnUrl();
      const existingSaved = peekAuthReturnUrl({
        storage: options.storage,
        key: options.returnUrlKey,
      });
      const savedUrl =
        saveAuthReturnUrl(candidateUrl, {
          storage: options.storage,
          key: options.returnUrlKey,
        }) || existingSaved;

      options.onSessionExpired?.({
        reason,
        error,
        returnUrl: savedUrl,
      });
      return savedUrl;
    } finally {
      expiringInProgress = false;
    }
  }

  function runSingleFlightUpdate(minValidity, failureReason, returnUrl) {
    const kc = resolveKeycloak();
    if (!kc || typeof kc.updateToken !== "function") {
      return Promise.resolve({
        ok: Boolean(kc?.token),
        refreshed: false,
        token: kc?.token || null,
      });
    }

    if (kc.authenticated === false && !kc.token && !kc.refreshToken) {
      return Promise.resolve({
        ok: false,
        refreshed: false,
        token: null,
      });
    }

    if (inflightRefreshPromise) {
      return inflightRefreshPromise;
    }

    inflightRequestedValidity = minValidity;
    let rawUpdatePromise;
    try {
      rawUpdatePromise = Promise.resolve(kc.updateToken(minValidity));
    } catch (syncError) {
      rawUpdatePromise = Promise.reject(syncError);
    }

    inflightRefreshPromise = rawUpdatePromise
      .then((refreshed) => {
        const nextToken = kc.token || null;
        if (!nextToken && minValidity < 0) {
          if (!sessionExpired) {
            markSessionExpired({ reason: failureReason, returnUrl });
          }
          return {
            ok: false,
            refreshed: false,
            token: null,
          };
        }
        sessionExpired = false;
        voluntaryLogoutInProgress = false;
        if (refreshed || nextToken) {
          options.onTokenRefreshed?.(nextToken, kc);
        }
        return {
          ok: Boolean(nextToken),
          refreshed: Boolean(refreshed),
          token: nextToken,
        };
      })
      .catch((error) => {
        if (!sessionExpired) {
          markSessionExpired({ reason: failureReason, error, returnUrl });
        }
        return {
          ok: false,
          refreshed: false,
          token: null,
          error,
        };
      })
      .finally(() => {
        inflightRefreshPromise = null;
        inflightRequestedValidity = null;
      });

    return inflightRefreshPromise;
  }

  function ensureFreshToken(minValidity = minValiditySeconds) {
    return runSingleFlightUpdate(minValidity, "token_expired");
  }

  async function refreshOnUnauthorized({ returnUrl } = {}) {
    if (voluntaryLogoutInProgress) {
      return { ok: false, refreshed: false, token: null };
    }

    const kc = resolveKeycloak();
    if (
      !kc ||
      typeof kc.updateToken !== "function" ||
      (kc.authenticated === false && !kc.token && !kc.refreshToken)
    ) {
      if (!sessionExpired) {
        markSessionExpired({ reason: "unauthorized", returnUrl });
      }
      return { ok: false, refreshed: false, token: null };
    }

    if (inflightRefreshPromise) {
      const pending = await inflightRefreshPromise;
      if (pending.ok && pending.token && inflightRequestedValidity !== null) {
        return pending;
      }
      if (!pending.ok || !pending.token) {
        if (!sessionExpired) {
          markSessionExpired({
            reason: "unauthorized",
            error: pending.error,
            returnUrl,
          });
        }
      }
      return pending;
    }

    const result = await runSingleFlightUpdate(
      forceRefreshMinValiditySeconds,
      "unauthorized",
      returnUrl
    );
    if (!result.ok || !result.token) {
      if (!sessionExpired) {
        markSessionExpired({
          reason: "unauthorized",
          error: result.error,
          returnUrl,
        });
      }
      return {
        ok: false,
        refreshed: false,
        token: null,
        error: result.error,
      };
    }
    return result;
  }

  function attachKeycloak(keycloakInstance) {
    attachedKeycloak = keycloakInstance || null;
    voluntaryLogoutInProgress = false;
    if (attachedKeycloak && typeof attachedKeycloak === "object") {
      if (attachedKeycloak.authenticated && attachedKeycloak.token) {
        sessionExpired = false;
      }
      attachedKeycloak.onAuthRefreshSuccess = () => {
        sessionExpired = false;
        voluntaryLogoutInProgress = false;
        options.onTokenRefreshed?.(
          attachedKeycloak?.token || null,
          attachedKeycloak
        );
      };
      attachedKeycloak.onTokenExpired = () =>
        ensureFreshToken(minValiditySeconds);
      if (options.onAuthLogout) {
        attachedKeycloak.onAuthLogout = () => {
          if (!voluntaryLogoutInProgress && !sessionExpired) {
            markSessionExpired({ reason: "auth_logout" });
          }
          options.onAuthLogout?.();
        };
      }
    }
    return attachedKeycloak;
  }

  function markAuthenticated(token = null) {
    sessionExpired = false;
    voluntaryLogoutInProgress = false;
    if (token !== null) {
      options.onTokenRefreshed?.(token, resolveKeycloak());
    }
  }

  function beginVoluntaryLogout() {
    voluntaryLogoutInProgress = true;
    sessionExpired = false;
    clearAuthReturnUrl({
      storage: options.storage,
      key: options.returnUrlKey,
    });
  }

  function reset() {
    attachedKeycloak = null;
    inflightRefreshPromise = null;
    inflightRequestedValidity = null;
    sessionExpired = false;
    voluntaryLogoutInProgress = false;
    expiringInProgress = false;
  }

  return {
    attachKeycloak,
    resolveKeycloak,
    ensureFreshToken,
    refreshOnUnauthorized,
    markSessionExpired,
    markAuthenticated,
    beginVoluntaryLogout,
    reset,
    isSessionExpired: () => sessionExpired,
    isRefreshing: () => Boolean(inflightRefreshPromise),
    saveReturnUrl: (url) =>
      saveAuthReturnUrl(url, {
        storage: options.storage,
        key: options.returnUrlKey,
      }),
    peekReturnUrl: () =>
      peekAuthReturnUrl({
        storage: options.storage,
        key: options.returnUrlKey,
      }),
    consumeReturnUrl: () =>
      consumeAuthReturnUrl({
        storage: options.storage,
        key: options.returnUrlKey,
      }),
  };
}
