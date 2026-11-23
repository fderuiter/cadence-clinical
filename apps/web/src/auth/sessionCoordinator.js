import {
  clearAuthReturnUrl,
  consumeAuthReturnUrl,
  createOidcSessionCoordinator,
  peekAuthReturnUrl,
  resolveOidcRedirectUri,
  sanitizeReturnUrl,
  saveAuthReturnUrl,
} from "ui";
import { getKeycloakClient } from "./keycloak.js";

export const WEB_AUTH_RETURN_URL_KEY = "cadence_web_return_url";

let authSessionBridge = null;
let clinicalSessionBridge = null;
let onboardingSessionBridge = null;
let sessionEndedHandler = null;
const sessionCacheClearHandlers = new Set();
const logoutCleanupHandlers = new Set();

export function registerSessionCacheClearHandler(handler) {
  if (typeof handler === "function") {
    sessionCacheClearHandlers.add(handler);
  }
  return () => {
    sessionCacheClearHandlers.delete(handler);
  };
}

export function notifySessionCacheCleared(context = {}) {
  for (const handler of sessionCacheClearHandlers) {
    try {
      handler(context);
    } catch {
      // Cache clear listener failed or Pinia store not active yet
    }
  }
}

export function registerLogoutCleanup(handler) {
  if (typeof handler === "function") {
    logoutCleanupHandlers.add(handler);
  }
}

export function runLogoutCleanups() {
  for (const handler of logoutCleanupHandlers) {
    try {
      handler();
    } catch {
      // Ignore cleanup errors during logout
    }
  }
}

/**
 * Computes the current in-app relative path (`pathname` stripped of Vite `BASE_URL` + `search` + `hash`).
 */
export function getCurrentInAppReturnUrl() {
  if (typeof window === "undefined" || !window.location) {
    return null;
  }
  const base = String(import.meta.env?.BASE_URL || "/").replace(/\/$/, "");
  const pathname = window.location.pathname || "/";
  const strippedPath =
    base && pathname.startsWith(base)
      ? pathname.slice(base.length) || "/"
      : pathname;
  const candidate = `${strippedPath.startsWith("/") ? strippedPath : `/${strippedPath}`}${window.location.search || ""}${window.location.hash || ""}`;
  return sanitizeReturnUrl(candidate);
}

export function saveWebReturnUrl(url = getCurrentInAppReturnUrl()) {
  return saveAuthReturnUrl(url, { key: WEB_AUTH_RETURN_URL_KEY });
}

export function peekWebReturnUrl() {
  return peekAuthReturnUrl({ key: WEB_AUTH_RETURN_URL_KEY });
}

export function consumeWebReturnUrl() {
  return consumeAuthReturnUrl({ key: WEB_AUTH_RETURN_URL_KEY });
}

export function clearWebReturnUrl() {
  clearAuthReturnUrl({ key: WEB_AUTH_RETURN_URL_KEY });
}

export function resolveWebRedirectUri(targetPathOrUrl) {
  const base = String(import.meta.env?.BASE_URL || "/");
  return resolveOidcRedirectUri(targetPathOrUrl, { basePath: base });
}

export function registerAuthSessionBridge(bridge) {
  authSessionBridge = bridge || null;
}

export function registerClinicalSessionBridge(bridge) {
  clinicalSessionBridge = bridge || null;
}

export function rehydrateClinicalPersona(personaKey, pinia) {
  if (!personaKey) return;
  clinicalSessionBridge?.onRehydratePersona?.(personaKey, pinia);
}

export function resetClinicalSession(pinia) {
  clinicalSessionBridge?.onReset?.(pinia);
}

export function registerOnboardingSessionBridge(bridge) {
  onboardingSessionBridge = bridge || null;
}

export function resetOnboardingSession(pinia) {
  onboardingSessionBridge?.onReset?.(pinia);
}

export function setSessionEndedHandler(handler) {
  sessionEndedHandler = typeof handler === "function" ? handler : null;
}

export function notifySessionEnded(context = {}) {
  sessionEndedHandler?.(context);
}

export const webSessionCoordinator = createOidcSessionCoordinator({
  getKeycloak: () => getKeycloakClient(),
  minValiditySeconds: 30,
  returnUrlKey: WEB_AUTH_RETURN_URL_KEY,
  getCurrentReturnUrl: getCurrentInAppReturnUrl,
  onTokenRefreshed: (token, kc) => {
    authSessionBridge?.onTokenRefreshed?.(token, kc);
  },
  onSessionExpired: (context) => {
    authSessionBridge?.onSessionExpired?.(context);
    notifySessionEnded(context);
  },
  onAuthLogout: () => {
    notifySessionEnded({ reason: "auth_logout" });
  },
});

export function syncKeycloakClientWithCoordinator(client) {
  if (client) {
    webSessionCoordinator.attachKeycloak(client);
  } else {
    webSessionCoordinator.reset();
    sessionEndedHandler = null;
  }
}
