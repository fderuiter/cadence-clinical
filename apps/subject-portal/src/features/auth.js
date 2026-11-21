// Participant identity: cached user, PIN-gated session state, Keycloak sign-in, logout.
import {
  DEFAULT_DEMO_PARTICIPANT,
  DEFAULT_DEMO_SITE,
  DEMO_SPONSOR,
  DEMO_STUDY,
} from "demo-fixtures";
import { createOidcSessionCoordinator, resolveOidcRedirectUri } from "ui";
import { watch } from "vue";

import { dispatchApi } from "../api/client.js";
import { state } from "../stores/state.js";
import {
  clearInMemoryKey,
  clearSessionKeyFromDB,
  getInMemorySessionKey,
  getWrappedMasterKeyConfig,
  initSessionKey,
  setActiveUserId,
} from "../sync-queue.js";
import { isDemoBuild, isDemoModeActive } from "../utils/demoMode.js";
import { loadCompliance, renderCompliance } from "./compliance.js";
import { loadNotifications, renderInbox } from "./inbox.js";
import { checkPINWrapper } from "./pin.js";
import { showView } from "./navigation.js";
import { checkOnline } from "./sync-status.js";
import {
  loadAssignedInstruments,
  loadAssignedTasks,
  renderTasks,
} from "./tasks.js";

function extractTokenSubjectCode(keycloak) {
  const accessClaim = keycloak?.tokenParsed?.subject_code;
  if (typeof accessClaim === "string" && accessClaim.trim()) {
    return accessClaim.trim();
  }
  const idClaim = keycloak?.idTokenParsed?.subject_code;
  if (typeof idClaim === "string" && idClaim.trim()) {
    return idClaim.trim();
  }
  return null;
}

function getPortalCurrentReturnUrl() {
  if (typeof window === "undefined" || !window.location) {
    return null;
  }
  const defaultBase = import.meta.env?.BASE_URL || "/subject-portal/";
  return `${window.location.pathname || defaultBase}${window.location.search || ""}${window.location.hash || ""}`;
}

function applyRestoredReturnUrl(savedReturnUrl) {
  if (
    !savedReturnUrl ||
    typeof window === "undefined" ||
    !window.history?.replaceState
  ) {
    return;
  }
  const currentUrl = getPortalCurrentReturnUrl();
  if (savedReturnUrl !== currentUrl) {
    try {
      window.history.replaceState(null, "", savedReturnUrl);
    } catch {
      // Ignore history errors in restricted test environments.
    }
  }
  const hashIdx = savedReturnUrl.indexOf("#");
  if (hashIdx >= 0) {
    const hashTarget = savedReturnUrl.slice(hashIdx + 1).replace(/^\/+/, "");
    if (hashTarget.startsWith("view-")) {
      showView(hashTarget);
    }
  }
}

export const portalSessionCoordinator = createOidcSessionCoordinator({
  minValiditySeconds: 30,
  getKeycloak: () =>
    typeof window !== "undefined" ? window.keycloakInstance || null : null,
  getCurrentReturnUrl: getPortalCurrentReturnUrl,
  onTokenRefreshed: (nextToken) => {
    if (nextToken) {
      state.session.token = nextToken;
      state.session.sessionExpired = false;
      state.authError = "";
    }
  },
  onSessionExpired: ({ returnUrl }) => {
    expirePortalSession({ returnUrl, fromCoordinator: true });
  },
});

/**
 * Transitions the portal out of Authenticated when token refresh or HTTP 401
 * recovery fails, clearing the stale bearer token while preserving active
 * questionnaires and encrypted IndexedDB drafts.
 */
export function expirePortalSession({
  message,
  returnUrl,
  fromCoordinator = false,
} = {}) {
  state.session.token = null;
  state.session.isDemoMode = false;
  state.session.sessionExpired = true;
  state.authError =
    message ||
    "Your session has expired. Sign in again to continue — your local survey drafts remain saved on this device.";

  if (!fromCoordinator) {
    portalSessionCoordinator.markSessionExpired({
      reason: "expired",
      returnUrl,
    });
  }
}

export async function ensurePortalSessionToken(minValidity = 30) {
  if (typeof window !== "undefined" && window.keycloakInstance) {
    if (
      portalSessionCoordinator.resolveKeycloak() !== window.keycloakInstance
    ) {
      portalSessionCoordinator.attachKeycloak(window.keycloakInstance);
    }
    return await portalSessionCoordinator.ensureFreshToken(minValidity);
  }
  return {
    ok: Boolean(state.session.token),
    refreshed: false,
    token: state.session.token || null,
  };
}

export async function refreshPortalSessionOnUnauthorized({ returnUrl } = {}) {
  if (typeof window !== "undefined" && window.keycloakInstance) {
    if (
      portalSessionCoordinator.resolveKeycloak() !== window.keycloakInstance
    ) {
      portalSessionCoordinator.attachKeycloak(window.keycloakInstance);
    }
    const result = await portalSessionCoordinator.refreshOnUnauthorized({
      returnUrl,
    });
    if (result.ok && result.token) {
      state.session.token = result.token;
      state.session.sessionExpired = false;
      state.authError = "";
      return result;
    }
    if (!state.session.sessionExpired) {
      expirePortalSession({ returnUrl, fromCoordinator: true });
    }
    return result;
  }

  expirePortalSession({ returnUrl });
  return { ok: false, refreshed: false, token: null };
}

// Restores the cached user identity without persisting or pre-populating unwrapped keys in production.
export async function restoreSession() {
  let cachedUserId = null;
  try {
    cachedUserId = localStorage.getItem("oidc_user_id");
  } catch (err) {
    console.warn("Failed to read oidc_user_id from localStorage:", err);
  }

  if (cachedUserId) {
    state.session.userId = cachedUserId;
  }
  setActiveUserId(state.session.userId);
  await clearSessionKeyFromDB(state.session.userId);

  // Only explicit demo sessions with no PIN wrapper initialize an ephemeral in-memory demo key.
  if (isDemoModeActive() && !getInMemorySessionKey()) {
    const config = await getWrappedMasterKeyConfig();
    if (!config.wrappedKey) {
      const demoMaterial = state.session.userId || "demo-material";
      try {
        await initSessionKey(demoMaterial, state.session.userId);
      } catch (err) {
        console.warn("Failed to initialize ephemeral demo session key:", err);
      }
    }
  }
}

// Signs in through injected storage credentials or Keycloak (check-sso).
// Outside explicit demo builds, never silently falls back to demo mode or a synthetic user ID.
export async function signIn() {
  state.authError = "";

  if (typeof window !== "undefined" && !window.__MOCK_TEST_ENV__) {
    let storageUser = null;
    let storageToken = null;
    try {
      storageUser =
        sessionStorage.getItem("mock_user_id") ||
        localStorage.getItem("mock_user_id");
      storageToken =
        sessionStorage.getItem("mock_token") ||
        localStorage.getItem("mock_token");
    } catch (e) {
      console.warn("Storage access failed:", e);
    }

    if (storageUser && storageToken) {
      state.session.userId = storageUser;
      state.session.token = storageToken;
      state.session.isOfflineMode = false;
      state.session.isDemoMode = false;
      state.session.sessionExpired = false;
      portalSessionCoordinator.markAuthenticated(storageToken);
      setActiveUserId(storageUser);
      applyRestoredReturnUrl(portalSessionCoordinator.consumeReturnUrl());
      return;
    }

    if (isDemoBuild()) {
      const demoSubjectCode = DEFAULT_DEMO_PARTICIPANT?.subjectCode || null;
      state.session.userId = state.session.userId || demoSubjectCode;
      state.session.studyId = state.session.studyId || DEMO_STUDY?.code || null;
      state.session.siteId =
        state.session.siteId || DEFAULT_DEMO_SITE?.code || null;
      state.session.sponsorCode =
        state.session.sponsorCode || DEMO_SPONSOR?.code || null;
      state.session.token = null;
      state.session.isOfflineMode = true;
      state.session.isDemoMode = true;
      state.session.sessionExpired = false;
      setActiveUserId(state.session.userId);
      return;
    }

    try {
      const KeycloakClass =
        window.Keycloak || (await import("keycloak-js")).default;
      if (KeycloakClass) {
        const keycloak = new KeycloakClass({
          url: import.meta.env.VITE_KEYCLOAK_URL || "http://localhost:8080/",
          realm: import.meta.env.VITE_KEYCLOAK_REALM || "cadence",
          clientId: import.meta.env.VITE_KEYCLOAK_CLIENT_ID || "cadence-web",
        });
        window.keycloakInstance = keycloak;
        portalSessionCoordinator.attachKeycloak(keycloak);
        keycloak.onAuthLogout = () => {
          if (!portalSessionCoordinator.isSessionExpired()) {
            expirePortalSession();
          }
        };

        await keycloak.init({
          onLoad: "check-sso",
          pkceMethod: "S256",
        });

        if (keycloak.authenticated && keycloak.token) {
          state.session.token = keycloak.token;
          state.session.isOfflineMode = false;
          state.session.isDemoMode = false;
          state.session.sessionExpired = false;
          portalSessionCoordinator.markAuthenticated(keycloak.token);

          let authenticatedUserId = extractTokenSubjectCode(keycloak);
          if (!authenticatedUserId) {
            try {
              const binding = await dispatchApi(
                "api/v1/execution/subjects/activation-codes/me"
              );
              if (
                binding &&
                typeof binding.subject_id === "string" &&
                binding.subject_id.trim()
              ) {
                authenticatedUserId = binding.subject_id.trim();
              }
            } catch {
              // Fall back to standard OIDC subject claim when no activation code was redeemed.
            }
          }

          if (!authenticatedUserId) {
            authenticatedUserId =
              keycloak.subject ||
              keycloak.tokenParsed?.preferred_username ||
              null;
          }

          state.session.userId = authenticatedUserId;
          setActiveUserId(authenticatedUserId);

          if (authenticatedUserId) {
            try {
              localStorage.setItem("oidc_user_id", authenticatedUserId);
            } catch (err) {
              console.warn("Failed to save oidc_user_id to localStorage:", err);
            }
          }
          applyRestoredReturnUrl(portalSessionCoordinator.consumeReturnUrl());
          return;
        }
      }
    } catch (err) {
      console.warn("Keycloak check-sso failed or unreachable:", err.message);
    }

    // Unauthenticated in a non-demo build: preserve cached offline identity only when offline with a PIN wrapper.
    state.session.isDemoMode = false;
    state.session.token = null;
    if (!checkOnline() && state.session.userId) {
      const config = await getWrappedMasterKeyConfig();
      if (config.wrappedKey) {
        state.session.isOfflineMode = true;
        return;
      }
    }

    state.session.userId = null;
    state.session.isOfflineMode = false;
    setActiveUserId(null);
  } else {
    // Under Vitest (__MOCK_TEST_ENV__), preserve explicit test configuration without forcing demo mode.
    if (!state.session.token && !isDemoModeActive()) {
      let cachedUserId;
      try {
        cachedUserId = localStorage.getItem("oidc_user_id");
      } catch {
        cachedUserId = null;
      }
      const defaultDemoId = DEFAULT_DEMO_PARTICIPANT?.subjectCode || null;
      if (
        !cachedUserId &&
        (state.session.userId === "subject_001" ||
          (defaultDemoId && state.session.userId === defaultDemoId))
      ) {
        state.session.userId = null;
      }
      state.session.isOfflineMode = false;
    } else if (state.session.token) {
      state.session.isOfflineMode = false;
    }
    setActiveUserId(state.session.userId);
  }
}

// Initiates interactive OIDC sign-in when an unauthenticated participant clicks Sign In.
export async function login(options = {}) {
  state.authError = "";
  const candidateReturnUrl =
    options?.returnUrl ||
    portalSessionCoordinator.peekReturnUrl() ||
    getPortalCurrentReturnUrl();
  const savedReturnUrl =
    portalSessionCoordinator.saveReturnUrl(candidateReturnUrl) ||
    portalSessionCoordinator.peekReturnUrl();

  try {
    let storageUser = null;
    let storageToken = null;
    try {
      storageUser =
        (typeof sessionStorage !== "undefined" &&
          sessionStorage.getItem("mock_user_id")) ||
        (typeof localStorage !== "undefined" &&
          localStorage.getItem("mock_user_id"));
      storageToken =
        (typeof sessionStorage !== "undefined" &&
          sessionStorage.getItem("mock_token")) ||
        (typeof localStorage !== "undefined" &&
          localStorage.getItem("mock_token"));
    } catch {
      storageUser = null;
      storageToken = null;
    }

    if (storageToken && (storageUser || state.session.userId)) {
      const resolvedUser = storageUser || state.session.userId;
      state.session.userId = resolvedUser;
      state.session.token = storageToken;
      state.session.isOfflineMode = false;
      state.session.isDemoMode = false;
      state.session.sessionExpired = false;
      portalSessionCoordinator.markAuthenticated(storageToken);
      setActiveUserId(resolvedUser);
      applyRestoredReturnUrl(portalSessionCoordinator.consumeReturnUrl());
      return;
    }

    if (isDemoBuild()) {
      const demoUserId =
        state.session.userId || DEFAULT_DEMO_PARTICIPANT?.subjectCode || null;
      state.session.userId = demoUserId;
      state.session.studyId = state.session.studyId || DEMO_STUDY?.code || null;
      state.session.siteId =
        state.session.siteId || DEFAULT_DEMO_SITE?.code || null;
      state.session.sponsorCode =
        state.session.sponsorCode || DEMO_SPONSOR?.code || null;
      state.session.token = null;
      state.session.isOfflineMode = true;
      state.session.isDemoMode = true;
      state.session.sessionExpired = false;
      setActiveUserId(demoUserId);
      return;
    }

    let keycloak =
      typeof window !== "undefined" ? window.keycloakInstance : null;
    if (!keycloak && typeof window !== "undefined") {
      const KeycloakClass =
        window.Keycloak || (await import("keycloak-js")).default;
      if (KeycloakClass) {
        keycloak = new KeycloakClass({
          url: import.meta.env.VITE_KEYCLOAK_URL || "http://localhost:8080/",
          realm: import.meta.env.VITE_KEYCLOAK_REALM || "cadence",
          clientId: import.meta.env.VITE_KEYCLOAK_CLIENT_ID || "cadence-web",
        });
        window.keycloakInstance = keycloak;
        portalSessionCoordinator.attachKeycloak(keycloak);
        await keycloak.init({ pkceMethod: "S256" });
      }
    } else if (
      keycloak &&
      portalSessionCoordinator.resolveKeycloak() !== keycloak
    ) {
      portalSessionCoordinator.attachKeycloak(keycloak);
    }

    if (keycloak && typeof keycloak.login === "function") {
      const redirectUri =
        options?.redirectUri ||
        resolveOidcRedirectUri(savedReturnUrl, {
          basePath: import.meta.env?.BASE_URL || "/subject-portal/",
        });
      await keycloak.login({ redirectUri });
      return;
    }

    state.authError =
      "Identity provider is currently unavailable. Please check your connection and try signing in again.";
  } catch (err) {
    state.authError = `Unable to start sign-in: ${err.message || err}`;
  }
}

let identityWatcherInstalled = false;

// Switches per-user database context and triggers PIN unlock/setup when the active user identifier changes.
export function watchUserIdentity() {
  if (identityWatcherInstalled) return;
  identityWatcherInstalled = true;

  watch(
    () => state.session.userId,
    async (newUid, oldUid) => {
      if (newUid !== oldUid) {
        clearInMemoryKey();
        setActiveUserId(newUid);
        if (isDemoModeActive()) {
          const config = await getWrappedMasterKeyConfig();
          if (!config.wrappedKey && newUid) {
            try {
              await initSessionKey(newUid, newUid);
            } catch (err) {
              console.warn(
                "Failed to derive ephemeral demo key on identity change:",
                err
              );
            }
          }
        } else if (newUid) {
          await checkPINWrapper();
        }
      }
    }
  );
}

// User logout clears cached OIDC identity, active in-memory PIN key, and session state.
export async function logout() {
  portalSessionCoordinator.beginVoluntaryLogout();
  const wasDemo = Boolean(state.session.isDemoMode);
  state.authError = "";
  state.session.sessionExpired = false;

  try {
    localStorage.removeItem("oidc_user_id");
    localStorage.removeItem("mock_user_id");
    localStorage.removeItem("mock_token");
    if (typeof sessionStorage !== "undefined") {
      sessionStorage.removeItem("mock_user_id");
      sessionStorage.removeItem("mock_token");
      sessionStorage.removeItem("oidc_pending_pin_reset");
    }
  } catch (err) {
    console.warn("Failed to clear session storage keys on logout:", err);
  }

  const prevUserId = state.session.userId;
  clearInMemoryKey();

  state.pinSetup.isOpen = false;
  state.pinSetup.pin = "";
  state.pinSetup.confirmPin = "";
  state.pinSetup.error = "";

  state.pinUnlock.isOpen = false;
  state.pinUnlock.pin = "";
  state.pinUnlock.error = "";
  state.pinUnlock.confirmingReset = false;
  state.pinUnlock.failedAttempts = 0;
  state.pinUnlock.isLockedOut = false;
  state.pinUnlock.requiresReauth = false;

  state.activeQuestionnaire = null;
  state.walletBalance = null;

  if (wasDemo) {
    const demoUserId = DEFAULT_DEMO_PARTICIPANT?.subjectCode || null;
    state.session.userId = demoUserId;
    state.session.studyId = DEMO_STUDY?.code || null;
    state.session.siteId = DEFAULT_DEMO_SITE?.code || null;
    state.session.sponsorCode = DEMO_SPONSOR?.code || null;
    state.session.token = null;
    state.session.isOfflineMode = true;
    state.session.isDemoMode = true;
    setActiveUserId(demoUserId);
  } else {
    state.session.userId = null;
    state.session.studyId = null;
    state.session.siteId = null;
    state.session.sponsorCode = null;
    state.session.token = null;
    state.session.isOfflineMode = false;
    state.session.isDemoMode = false;
    state.assignments = [];
    state.notifications = [];
    state.compliance = null;
    state.submissions = [];
    setActiveUserId(null);
    renderTasks();
    renderCompliance();
    renderInbox();
  }

  await clearSessionKeyFromDB(prevUserId);

  if (
    typeof window !== "undefined" &&
    window.keycloakInstance &&
    typeof window.keycloakInstance.logout === "function"
  ) {
    try {
      await window.keycloakInstance.logout();
    } catch (err) {
      console.warn("Keycloak logout failed:", err);
    }
  }
}

// Redeems a 30-day BYOD activation code against Execution and binds the authenticated session to the study SubjectCode.
export async function redeemActivationCode(
  activationCode,
  reason = "Participant redeemed 30-day BYOD ePRO activation code"
) {
  const normalizedCode =
    typeof activationCode === "string" ? activationCode.trim() : "";
  if (!normalizedCode) {
    throw new Error("Please enter a valid activation code.");
  }

  const effectiveReason =
    typeof reason === "string" && reason.trim()
      ? reason.trim()
      : "Participant redeemed 30-day BYOD ePRO activation code";

  const redeemed = await dispatchApi(
    "api/v1/execution/subjects/activation-codes/redeem",
    {
      method: "POST",
      change_reason: effectiveReason,
      body: JSON.stringify({ activation_code: normalizedCode }),
    }
  );

  const boundSubjectId = redeemed?.subject_id?.trim?.() || null;
  if (!boundSubjectId) {
    throw new Error("Activation code response did not contain a subject_id.");
  }

  state.session.userId = boundSubjectId;
  setActiveUserId(boundSubjectId);
  try {
    localStorage.setItem("oidc_user_id", boundSubjectId);
  } catch (err) {
    console.warn("Failed to persist redeemed subject_id:", err);
  }

  await loadAssignedTasks(boundSubjectId);
  await loadAssignedInstruments(boundSubjectId);
  await loadCompliance(boundSubjectId);
  await loadNotifications(boundSubjectId);
  renderTasks();
  renderCompliance();
  renderInbox();

  return redeemed;
}
