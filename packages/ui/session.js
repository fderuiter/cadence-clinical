/**
 * Shared OIDC session coordinator for Cadence Clinical web and participant applications.
 *
 * Centralizes Keycloak token-expiration and refresh hooks, deduplicates concurrent
 * `updateToken` calls (single-flight), distinguishes step-up signature re-authentication
 * failures from OIDC bearer session expiration, and preserves safe internal return URLs
 * without persisting raw bearer tokens or unwrapped encryption keys in web storage.
 */

export const DEFAULT_AUTH_RETURN_URL_KEY = "cadence_auth_return_url";
export const MIN_SESSION_WARNING_LEAD_SECONDS = 20;
export const DEFAULT_SESSION_WARNING_LEAD_SECONDS = 60;
export const MIN_REQUIRED_SESSION_EXTENSIONS = 10;

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

function decodeJwtPayloadExp(token) {
  if (typeof token !== "string") return null;
  const parts = token.split(".");
  if (parts.length < 2 || !parts[1]) return null;
  try {
    const base64 = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
    let jsonText = "";
    if (typeof atob === "function") {
      jsonText = atob(padded);
    } else if (typeof Buffer !== "undefined") {
      jsonText = Buffer.from(padded, "base64").toString("utf8");
    } else {
      return null;
    }
    const parsed = JSON.parse(jsonText);
    return typeof parsed?.exp === "number" && Number.isFinite(parsed.exp)
      ? parsed.exp
      : null;
  } catch {
    return null;
  }
}

function extractTokenExpiryEpochMs(keycloakInstance, explicitToken = null) {
  if (
    keycloakInstance &&
    typeof keycloakInstance.tokenExpMs === "number" &&
    Number.isFinite(keycloakInstance.tokenExpMs)
  ) {
    return keycloakInstance.tokenExpMs;
  }
  const timeSkewSeconds =
    typeof keycloakInstance?.timeSkew === "number" &&
    Number.isFinite(keycloakInstance.timeSkew)
      ? keycloakInstance.timeSkew
      : 0;
  const parsedExp = keycloakInstance?.tokenParsed?.exp;
  if (typeof parsedExp === "number" && Number.isFinite(parsedExp)) {
    return (parsedExp - timeSkewSeconds) * 1000;
  }
  const candidateToken =
    explicitToken ||
    (typeof keycloakInstance?.token === "string"
      ? keycloakInstance.token
      : null);
  const jwtExp = decodeJwtPayloadExp(candidateToken);
  if (jwtExp !== null) {
    return (jwtExp - timeSkewSeconds) * 1000;
  }
  return null;
}

/**
 * Creates a single-flight OIDC session coordinator that wires `onTokenExpired`
 * and `onAuthRefreshSuccess`, deduplicates concurrent `updateToken` requests,
 * tracks wall-clock `exp` session warnings (WCAG 2.2 SC 2.2.1 / SCR16 / SCR1),
 * and handles involuntary session expiry without destroying local drafts.
 *
 * @param {{
 *   getKeycloak?: () => any,
 *   minValiditySeconds?: number,
 *   forceRefreshMinValiditySeconds?: number,
 *   warningLeadSeconds?: number,
 *   warningTickIntervalMs?: number,
 *   now?: () => number,
 *   onTokenRefreshed?: (token: string | null, keycloak: any) => void,
 *   onSessionExpiring?: (context: { remainingSeconds: number, warningLeadSeconds: number, expiresAtMs: number, extensionCount: number }) => void,
 *   onSessionExpiryTick?: (context: { remainingSeconds: number, warningLeadSeconds: number, expiresAtMs: number, extensionCount: number }) => void,
 *   onSessionExtended?: (context: { token: string | null, expiresAtMs: number | null, extensionCount: number }) => void,
 *   onSessionWarningCleared?: () => void,
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
  const requestedWarningLeadSeconds =
    typeof options.warningLeadSeconds === "number" &&
    Number.isFinite(options.warningLeadSeconds)
      ? Math.floor(options.warningLeadSeconds)
      : DEFAULT_SESSION_WARNING_LEAD_SECONDS;
  const warningLeadSeconds = Math.max(
    MIN_SESSION_WARNING_LEAD_SECONDS,
    requestedWarningLeadSeconds
  );
  const warningTickIntervalMs =
    typeof options.warningTickIntervalMs === "number" &&
    options.warningTickIntervalMs > 0
      ? options.warningTickIntervalMs
      : 1000;
  const now =
    typeof options.now === "function" ? options.now : () => Date.now();
  const getCurrentReturnUrl =
    options.getCurrentReturnUrl || defaultCurrentReturnUrl;

  let attachedKeycloak = null;
  let lastObservedKeycloak = null;
  let explicitLatestToken = null;
  let inflightRefreshPromise = null;
  let inflightRequestedValidity = null;
  let sessionExpired = false;
  let voluntaryLogoutInProgress = false;
  let expiringInProgress = false;

  let warningLeadTimeoutId = null;
  let warningTickIntervalId = null;
  let warningExpiryTimeoutId = null;
  let warningActive = false;
  let trackedExpiresAtMs = null;
  let extensionCount = 0;
  let lifecycleListenersAttached = false;

  function clearWarningTimers() {
    if (warningLeadTimeoutId !== null) {
      clearTimeout(warningLeadTimeoutId);
      warningLeadTimeoutId = null;
    }
    if (warningTickIntervalId !== null) {
      clearInterval(warningTickIntervalId);
      warningTickIntervalId = null;
    }
    if (warningExpiryTimeoutId !== null) {
      clearTimeout(warningExpiryTimeoutId);
      warningExpiryTimeoutId = null;
    }
  }

  function clearWarningState(notifyCleared = true) {
    clearWarningTimers();
    trackedExpiresAtMs = null;
    if (warningActive) {
      warningActive = false;
      if (notifyCleared) {
        options.onSessionWarningCleared?.();
      }
    }
  }

  function handleForegroundCheck() {
    if (sessionExpired || voluntaryLogoutInProgress) return;
    if (
      trackedExpiresAtMs === null &&
      !attachedKeycloak &&
      !options.getKeycloak
    ) {
      return;
    }
    evaluateSessionExpiry();
  }

  function handleVisibilityChange() {
    if (
      typeof document !== "undefined" &&
      document.visibilityState &&
      document.visibilityState !== "visible"
    ) {
      return;
    }
    handleForegroundCheck();
  }

  function attachLifecycleListeners() {
    if (lifecycleListenersAttached) return;
    if (
      typeof document !== "undefined" &&
      typeof document.addEventListener === "function"
    ) {
      document.addEventListener("visibilitychange", handleVisibilityChange);
      lifecycleListenersAttached = true;
    }
    if (
      typeof window !== "undefined" &&
      typeof window.addEventListener === "function"
    ) {
      window.addEventListener("focus", handleForegroundCheck);
      lifecycleListenersAttached = true;
    }
  }

  function detachLifecycleListeners() {
    if (!lifecycleListenersAttached) return;
    if (
      typeof document !== "undefined" &&
      typeof document.removeEventListener === "function"
    ) {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
    }
    if (
      typeof window !== "undefined" &&
      typeof window.removeEventListener === "function"
    ) {
      window.removeEventListener("focus", handleForegroundCheck);
    }
    lifecycleListenersAttached = false;
  }

  function resolveKeycloak() {
    const dynamicKc = options.getKeycloak ? options.getKeycloak() : undefined;
    const current = dynamicKc || attachedKeycloak;
    if (current !== lastObservedKeycloak) {
      lastObservedKeycloak = current;
      if (!current || (current.authenticated && current.token)) {
        sessionExpired = false;
        voluntaryLogoutInProgress = false;
      }
    }
    return current || null;
  }

  function getRemainingSeconds() {
    const kc = resolveKeycloak();
    const expMs =
      extractTokenExpiryEpochMs(kc, explicitLatestToken) ?? trackedExpiresAtMs;
    if (expMs === null) return null;
    return Math.max(0, Math.ceil((expMs - now()) / 1000));
  }

  function evaluateSessionExpiry() {
    if (sessionExpired || voluntaryLogoutInProgress) {
      clearWarningState(true);
      return;
    }
    const kc = resolveKeycloak();
    const expMs = extractTokenExpiryEpochMs(kc, explicitLatestToken);
    if (expMs === null) {
      clearWarningState(true);
      return;
    }

    attachLifecycleListeners();
    trackedExpiresAtMs = expMs;
    const currentNow = now();
    const remainingMs = expMs - currentNow;
    const warningThresholdMs = warningLeadSeconds * 1000;

    if (remainingMs <= 0) {
      clearWarningState(true);
      if (!inflightRefreshPromise && !sessionExpired) {
        markSessionExpired({ reason: "token_expired" });
      }
      return;
    }

    clearWarningTimers();

    if (remainingMs <= warningThresholdMs) {
      const remainingSeconds = Math.max(1, Math.ceil(remainingMs / 1000));
      const payload = {
        remainingSeconds,
        warningLeadSeconds,
        expiresAtMs: expMs,
        extensionCount,
      };
      if (!warningActive) {
        warningActive = true;
        options.onSessionExpiring?.(payload);
      }
      options.onSessionExpiryTick?.(payload);

      warningTickIntervalId = setInterval(() => {
        if (sessionExpired || voluntaryLogoutInProgress) {
          clearWarningState(true);
          return;
        }
        const remMs = expMs - now();
        if (remMs <= 0) {
          clearWarningState(true);
          if (!inflightRefreshPromise && !sessionExpired) {
            markSessionExpired({ reason: "token_expired" });
          }
          return;
        }
        const remSec = Math.max(1, Math.ceil(remMs / 1000));
        options.onSessionExpiryTick?.({
          remainingSeconds: remSec,
          warningLeadSeconds,
          expiresAtMs: expMs,
          extensionCount,
        });
      }, warningTickIntervalMs);

      warningExpiryTimeoutId = setTimeout(() => {
        evaluateSessionExpiry();
      }, remainingMs);
      return;
    }

    if (warningActive) {
      warningActive = false;
      options.onSessionWarningCleared?.();
    }

    const delayUntilWarningMs = Math.max(1, remainingMs - warningThresholdMs);
    warningLeadTimeoutId = setTimeout(() => {
      evaluateSessionExpiry();
    }, delayUntilWarningMs);
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
      clearWarningState(true);
      explicitLatestToken = null;
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
        explicitLatestToken = nextToken;
        if (refreshed || nextToken) {
          options.onTokenRefreshed?.(nextToken, kc);
        }
        evaluateSessionExpiry();
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

  async function extendSession() {
    if (voluntaryLogoutInProgress) {
      return {
        ok: false,
        refreshed: false,
        token: null,
        extensionCount,
        expiresAtMs: null,
      };
    }
    const result = await ensureFreshToken(forceRefreshMinValiditySeconds);
    if (result.ok && result.token) {
      extensionCount += 1;
      explicitLatestToken = result.token;
      const kc = resolveKeycloak();
      const expMs = extractTokenExpiryEpochMs(kc, explicitLatestToken);
      evaluateSessionExpiry();
      options.onSessionExtended?.({
        token: result.token,
        expiresAtMs: expMs,
        extensionCount,
      });
      return {
        ...result,
        extensionCount,
        expiresAtMs: expMs,
      };
    }
    return {
      ...result,
      extensionCount,
      expiresAtMs: null,
    };
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
        explicitLatestToken = attachedKeycloak.token;
      }
      attachedKeycloak.onAuthRefreshSuccess = () => {
        sessionExpired = false;
        voluntaryLogoutInProgress = false;
        explicitLatestToken = attachedKeycloak?.token || null;
        options.onTokenRefreshed?.(
          attachedKeycloak?.token || null,
          attachedKeycloak
        );
        evaluateSessionExpiry();
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
      evaluateSessionExpiry();
    } else {
      clearWarningState(true);
    }
    return attachedKeycloak;
  }

  function markAuthenticated(token = null) {
    sessionExpired = false;
    voluntaryLogoutInProgress = false;
    if (token !== null) {
      explicitLatestToken = token;
      options.onTokenRefreshed?.(token, resolveKeycloak());
    }
    evaluateSessionExpiry();
  }

  function beginVoluntaryLogout() {
    voluntaryLogoutInProgress = true;
    sessionExpired = false;
    explicitLatestToken = null;
    clearWarningState(true);
    clearAuthReturnUrl({
      storage: options.storage,
      key: options.returnUrlKey,
    });
  }

  function reset() {
    clearWarningState(true);
    detachLifecycleListeners();
    attachedKeycloak = null;
    lastObservedKeycloak = null;
    explicitLatestToken = null;
    inflightRefreshPromise = null;
    inflightRequestedValidity = null;
    sessionExpired = false;
    voluntaryLogoutInProgress = false;
    expiringInProgress = false;
    extensionCount = 0;
  }

  function dispose() {
    clearWarningState(false);
    detachLifecycleListeners();
  }

  return {
    attachKeycloak,
    resolveKeycloak,
    ensureFreshToken,
    extendSession,
    evaluateSessionExpiry,
    refreshOnUnauthorized,
    markSessionExpired,
    markAuthenticated,
    beginVoluntaryLogout,
    reset,
    dispose,
    getWarningLeadSeconds: () => warningLeadSeconds,
    getExtensionCount: () => extensionCount,
    getRemainingSeconds,
    isWarningActive: () => warningActive,
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
