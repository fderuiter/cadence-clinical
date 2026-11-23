import { reactive } from "vue";
import {
  DEFAULT_SESSION_WARNING_LEAD_SECONDS,
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

export const webSessionWarningState = reactive({
  isOpen: false,
  remainingSeconds: DEFAULT_SESSION_WARNING_LEAD_SECONDS,
  warningLeadSeconds: DEFAULT_SESSION_WARNING_LEAD_SECONDS,
  isExtending: false,
  extensionCount: 0,
});

export function resetWebSessionWarningState() {
  webSessionWarningState.isOpen = false;
  webSessionWarningState.remainingSeconds =
    DEFAULT_SESSION_WARNING_LEAD_SECONDS;
  webSessionWarningState.warningLeadSeconds =
    DEFAULT_SESSION_WARNING_LEAD_SECONDS;
  webSessionWarningState.isExtending = false;
  webSessionWarningState.extensionCount = 0;
}

let authSessionBridge = null;
let clinicalSessionBridge = null;
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

export function setSessionEndedHandler(handler) {
  sessionEndedHandler = typeof handler === "function" ? handler : null;
}

export function notifySessionEnded(context = {}) {
  sessionEndedHandler?.(context);
}

export const webSessionCoordinator = createOidcSessionCoordinator({
  getKeycloak: () => getKeycloakClient(),
  minValiditySeconds: 30,
  warningLeadSeconds: DEFAULT_SESSION_WARNING_LEAD_SECONDS,
  returnUrlKey: WEB_AUTH_RETURN_URL_KEY,
  getCurrentReturnUrl: getCurrentInAppReturnUrl,
  onTokenRefreshed: (token, kc) => {
    authSessionBridge?.onTokenRefreshed?.(token, kc);
  },
  onSessionExpiring: ({
    remainingSeconds,
    warningLeadSeconds,
    extensionCount,
  }) => {
    webSessionWarningState.remainingSeconds = remainingSeconds;
    webSessionWarningState.warningLeadSeconds = warningLeadSeconds;
    webSessionWarningState.extensionCount = extensionCount;
    webSessionWarningState.isOpen = true;
  },
  onSessionExpiryTick: ({ remainingSeconds, warningLeadSeconds }) => {
    webSessionWarningState.remainingSeconds = remainingSeconds;
    webSessionWarningState.warningLeadSeconds = warningLeadSeconds;
  },
  onSessionExtended: ({ extensionCount }) => {
    webSessionWarningState.extensionCount = extensionCount;
    webSessionWarningState.isExtending = false;
    webSessionWarningState.isOpen = false;
  },
  onSessionWarningCleared: () => {
    webSessionWarningState.isExtending = false;
    webSessionWarningState.isOpen = false;
  },
  onSessionExpired: (context) => {
    resetWebSessionWarningState();
    authSessionBridge?.onSessionExpired?.(context);
    notifySessionEnded(context);
  },
  onAuthLogout: () => {
    resetWebSessionWarningState();
    notifySessionEnded({ reason: "auth_logout" });
  },
});

export async function extendActiveWebSession() {
  webSessionWarningState.isExtending = true;
  try {
    return await webSessionCoordinator.extendSession();
  } finally {
    webSessionWarningState.isExtending = false;
  }
}

export function syncKeycloakClientWithCoordinator(client) {
  if (client) {
    webSessionCoordinator.attachKeycloak(client);
  } else {
    webSessionCoordinator.reset();
    resetWebSessionWarningState();
    sessionEndedHandler = null;
  }
}
