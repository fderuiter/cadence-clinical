// Participant identity: cached user, PIN-gated session state, Keycloak sign-in, logout.
import {
  DEFAULT_DEMO_PARTICIPANT,
  DEFAULT_DEMO_SITE,
  DEMO_SPONSOR,
  DEMO_STUDY,
} from "demo-fixtures";
import { extractPrimarySiteId, extractPrimaryStudyId } from "ui/caller-scope";
import {
  flushRegisteredDraftSources,
  hasRegisteredUnsavedDrafts,
  useSignOutGuard,
} from "ui/portal-shell";
import {
  createOidcSessionCoordinator,
  resolveOidcRedirectUri,
} from "ui/session";
import { watch } from "vue";

import { state } from "../stores/state.js";
import { isDemoBuild, isDemoModeActive } from "../utils/demoMode.js";
import {
  hasTrackedLocalQueueActivity,
  setTrackedLocalQueueActivity,
} from "../utils/localQueueActivity.js";
import { checkOnline } from "../utils/networkStatus.js";
import { showToast } from "../widgets/toast.js";
import { showView } from "./navigation.js";

let lastInspectedSignOutState = {
  draftCount: 0,
  unsyncedCount: 0,
};
let activeScopedUserId = null;
let authTransitionSeq = 0;
let syncQueueModule = null;
let syncQueueModulePromise = null;
let pinModule = null;
let pinModulePromise = null;
let syncStatusModule = null;
let syncStatusModulePromise = null;

function loadSyncQueueModule() {
  if (syncQueueModule) {
    return Promise.resolve(syncQueueModule);
  }
  return (syncQueueModulePromise ??= import("../sync-queue.js").then((mod) => {
    syncQueueModule = mod;
    return mod;
  }));
}

function loadPinModule() {
  if (pinModule) {
    return Promise.resolve(pinModule);
  }
  return (pinModulePromise ??= import("./pin.js").then((mod) => {
    pinModule = mod;
    return mod;
  }));
}

function loadSyncStatusModule() {
  if (syncStatusModule) {
    return Promise.resolve(syncStatusModule);
  }
  return (syncStatusModulePromise ??= import("./sync-status.js").then((mod) => {
    syncStatusModule = mod;
    return mod;
  }));
}

function hasLocalDraftState() {
  if (hasRegisteredUnsavedDrafts()) {
    return true;
  }
  if (
    state.activeQuestionnaire?.answers &&
    Object.values(state.activeQuestionnaire.answers).some(
      (val) => val !== "" && val !== null && val !== undefined
    )
  ) {
    return true;
  }
  return Object.values(state.draftPresence || {}).some(Boolean);
}

export function isUnsyncedOrLockedSubmission(sub) {
  if (!sub || sub.unrecoverable) {
    return false;
  }
  return (
    sub.status === "QUEUED" ||
    sub.status === "DECRYPTION_ERROR" ||
    Boolean(sub.locked)
  );
}

export async function prepareSignOutGuardState(options = {}) {
  lastInspectedSignOutState = { draftCount: 0, unsyncedCount: 0 };
  try {
    if (!options.skipRegisteredFlush) {
      await flushRegisteredDraftSources();
    }
    if (
      state.activeQuestionnaire &&
      (isDemoBuild() || import.meta.env?.MODE === "test")
    ) {
      const { flushActiveQuestionnaireDraft } =
        await import("./questionnaire.js");
      await flushActiveQuestionnaireDraft();
    }
  } catch (err) {
    console.warn("Could not flush active draft before sign-out check:", err);
  }
  const syncQueue = syncQueueModule || (await loadSyncQueueModule());
  const [draftIds, allSubs] = await Promise.all([
    syncQueue.getDraftAssignmentIds(state.session.userId).catch(() => []),
    syncQueue.getAllSubmissions().catch(() => null),
  ]);
  lastInspectedSignOutState.draftCount = Array.isArray(draftIds)
    ? draftIds.length
    : 0;
  if (Array.isArray(allSubs)) {
    state.submissions = allSubs;
    lastInspectedSignOutState.unsyncedCount = allSubs.filter(
      isUnsyncedOrLockedSubmission
    ).length;
  } else {
    lastInspectedSignOutState.unsyncedCount = 0;
  }
  const hasPersistedPending =
    lastInspectedSignOutState.draftCount > 0 ||
    lastInspectedSignOutState.unsyncedCount > 0;
  setTrackedLocalQueueActivity(hasPersistedPending);
  if (!hasLocalDraftState() && Array.isArray(state.assignments)) {
    if (lastInspectedSignOutState.draftCount === 0) {
      state.assignments.forEach((a) => {
        if (a && a.has_draft) a.has_draft = false;
      });
    }
    if (lastInspectedSignOutState.unsyncedCount === 0) {
      state.assignments.forEach((a) => {
        if (a && a.status === "PENDING_SYNC") a.status = "PENDING";
      });
    }
  }
}

export function hasUnsavedOrUnsyncedLocalData() {
  if (hasLocalDraftState()) {
    return true;
  }
  if (hasTrackedLocalQueueActivity()) {
    return true;
  }
  if (
    lastInspectedSignOutState.draftCount > 0 ||
    lastInspectedSignOutState.unsyncedCount > 0
  ) {
    return true;
  }
  if (
    Array.isArray(state.assignments) &&
    state.assignments.some(
      (a) => Boolean(a?.has_draft) || a?.status === "PENDING_SYNC"
    )
  ) {
    return true;
  }
  if (
    Array.isArray(state.submissions) &&
    state.submissions.some(isUnsyncedOrLockedSubmission)
  ) {
    return true;
  }
  return false;
}

export function usePortalSignOutGuard() {
  return useSignOutGuard({
    flushExternalDrafts: () =>
      prepareSignOutGuardState({ skipRegisteredFlush: true }),
    hasExternalDrafts: hasUnsavedOrUnsyncedLocalData,
    onConfirmSignOut: () => logout({ confirmed: true }),
  });
}

async function transitionParticipantScope(nextUserId) {
  const normalizedNext = nextUserId || null;
  if (
    activeScopedUserId === null &&
    normalizedNext === null &&
    !hasTrackedLocalQueueActivity() &&
    import.meta.env?.MODE !== "test"
  ) {
    state.submissions = [];
    return;
  }

  const syncQueue = await loadSyncQueueModule();
  const prevUserId = syncQueue.getActiveUserId();
  if (prevUserId !== normalizedNext) {
    const { clearPendingResetMasterKey } = await loadPinModule();
    clearPendingResetMasterKey();
    syncQueue.clearInMemoryKey();
    state.submissions = [];
    const { renderSyncQueueList } = await loadSyncStatusModule();
    void renderSyncQueueList([]);
  }
  syncQueue.setActiveUserId(normalizedNext);
  activeScopedUserId = normalizedNext;
}

async function ensureEphemeralDemoSessionKey() {
  if (!state.session.token && isDemoModeActive()) {
    const syncQueue = await loadSyncQueueModule();
    if (!syncQueue.getInMemorySessionKey()) {
      const config = await syncQueue.getWrappedMasterKeyConfig();
      if (!config.wrappedKey) {
        const demoMaterial = state.session.userId || "demo-material";
        try {
          await syncQueue.initSessionKey(demoMaterial, state.session.userId);
        } catch (err) {
          console.warn("Failed to initialize ephemeral demo session key:", err);
        }
      }
    }
  }
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

function readScopeFromCurrentUrl() {
  if (typeof window === "undefined" || !window.location) {
    return { studyId: null, siteId: null };
  }
  const searchParams = new URLSearchParams(window.location.search || "");
  let studyId = searchParams.get("studyId")?.trim() || null;
  let siteId = searchParams.get("siteId")?.trim() || null;

  if ((!studyId || !siteId) && window.location.hash?.includes("?")) {
    const hashQuery = window.location.hash.slice(
      window.location.hash.indexOf("?") + 1
    );
    const hashParams = new URLSearchParams(hashQuery);
    studyId = studyId || hashParams.get("studyId")?.trim() || null;
    siteId = siteId || hashParams.get("siteId")?.trim() || null;
  }

  return { studyId, siteId };
}

export function hydrateSessionScopeFromClaims(
  claimsSource = null,
  fallbackToken = null
) {
  const claimStudyId =
    extractPrimaryStudyId(claimsSource) || extractPrimaryStudyId(fallbackToken);
  const claimSiteId =
    extractPrimarySiteId(claimsSource) || extractPrimarySiteId(fallbackToken);
  const urlScope = readScopeFromCurrentUrl();

  let storedStudyId;
  let storedSiteId;
  try {
    storedStudyId =
      (typeof sessionStorage !== "undefined" &&
        sessionStorage.getItem("mock_study_id")) ||
      (typeof localStorage !== "undefined" &&
        (localStorage.getItem("mock_study_id") ||
          localStorage.getItem("oidc_study_id"))) ||
      null;
    storedSiteId =
      (typeof sessionStorage !== "undefined" &&
        sessionStorage.getItem("mock_site_id")) ||
      (typeof localStorage !== "undefined" &&
        (localStorage.getItem("mock_site_id") ||
          localStorage.getItem("oidc_site_id"))) ||
      null;
  } catch {
    storedStudyId = null;
    storedSiteId = null;
  }

  const nextStudyId =
    claimStudyId ||
    urlScope.studyId ||
    storedStudyId ||
    state.session.studyId ||
    null;
  const nextSiteId =
    claimSiteId ||
    urlScope.siteId ||
    storedSiteId ||
    state.session.siteId ||
    null;

  state.session.studyId = nextStudyId;
  state.session.siteId = nextSiteId;

  try {
    if (typeof localStorage !== "undefined") {
      if (claimStudyId) {
        localStorage.setItem("oidc_study_id", claimStudyId);
      }
      if (claimSiteId) {
        localStorage.setItem("oidc_site_id", claimSiteId);
      }
    }
  } catch {
    // Ignore storage write errors in restricted environments.
  }

  return { studyId: nextStudyId, siteId: nextSiteId };
}

export const portalSessionCoordinator = createOidcSessionCoordinator({
  minValiditySeconds: 30,
  getKeycloak: () =>
    typeof window !== "undefined" ? window.keycloakInstance || null : null,
  getCurrentReturnUrl: getPortalCurrentReturnUrl,
  onTokenRefreshed: (nextToken, kc) => {
    if (nextToken) {
      state.session.token = nextToken;
      state.session.sessionExpired = false;
      state.authError = "";
      hydrateSessionScopeFromClaims(kc?.tokenParsed || null, nextToken);
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
  hydrateSessionScopeFromClaims();
  await transitionParticipantScope(state.session.userId);
  if (
    Boolean(state.session.userId) ||
    hasTrackedLocalQueueActivity() ||
    import.meta.env?.MODE === "test"
  ) {
    const { clearSessionKeyFromDB } = await loadSyncQueueModule();
    await clearSessionKeyFromDB(state.session.userId);
  }

  // Only explicit demo sessions with no PIN wrapper and no injected auth token initialize an ephemeral in-memory demo key.
  let hasInjectedAuth = false;
  if (typeof window !== "undefined" && !window.__MOCK_TEST_ENV__) {
    try {
      const storageUser =
        sessionStorage.getItem("mock_user_id") ||
        localStorage.getItem("mock_user_id");
      const storageToken =
        sessionStorage.getItem("mock_token") ||
        localStorage.getItem("mock_token");
      hasInjectedAuth = Boolean(storageUser && storageToken);
    } catch {
      hasInjectedAuth = false;
    }
  }
  if (!hasInjectedAuth) {
    await ensureEphemeralDemoSessionKey();
  }
}

// Signs in through injected storage credentials or Keycloak (check-sso).
// Outside explicit demo builds, never silently falls back to demo mode or a synthetic user ID.
export async function signIn() {
  authTransitionSeq += 1;
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
      if (state.session.userId !== storageUser) {
        const { clearInMemoryKey } = await loadSyncQueueModule();
        clearInMemoryKey();
      }
      state.session.userId = storageUser;
      state.session.token = storageToken;
      state.session.isOfflineMode = false;
      state.session.isDemoMode = false;
      state.session.sessionExpired = false;
      hydrateSessionScopeFromClaims(null, storageToken);
      portalSessionCoordinator.markAuthenticated(storageToken);
      await transitionParticipantScope(storageUser);
      await ensureEphemeralDemoSessionKey();
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
      await transitionParticipantScope(state.session.userId);
      await ensureEphemeralDemoSessionKey();
      return;
    } else {
      try {
        const KeycloakClass =
          window.Keycloak || (await import("keycloak-js")).default;
        if (KeycloakClass) {
          const defaultKeycloakUrl = import.meta.env.PROD
            ? ""
            : "http://localhost:8080/";
          const keycloak = new KeycloakClass({
            url: import.meta.env.VITE_KEYCLOAK_URL || defaultKeycloakUrl,
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

          const portalBase = (
            import.meta.env?.BASE_URL || "/subject-portal/"
          ).replace(/\/?$/, "/");
          await keycloak.init({
            onLoad: "check-sso",
            pkceMethod: "S256",
            silentCheckSsoRedirectUri: `${window.location.origin}${portalBase}silent-check-sso.html`,
            silentCheckSsoFallback: false,
            messageReceiveTimeout: 1500,
          });

          if (keycloak.authenticated && keycloak.token) {
            const authenticatedUserId =
              keycloak.subject ||
              keycloak.tokenParsed?.preferred_username ||
              null;
            state.session.userId = authenticatedUserId;
            state.session.token = keycloak.token;
            state.session.isOfflineMode = false;
            state.session.isDemoMode = false;
            state.session.sessionExpired = false;
            hydrateSessionScopeFromClaims(
              keycloak.tokenParsed || null,
              keycloak.token
            );
            portalSessionCoordinator.markAuthenticated(keycloak.token);
            await transitionParticipantScope(authenticatedUserId);

            if (authenticatedUserId) {
              try {
                localStorage.setItem("oidc_user_id", authenticatedUserId);
              } catch (err) {
                console.warn(
                  "Failed to save oidc_user_id to localStorage:",
                  err
                );
              }
            }
            applyRestoredReturnUrl(portalSessionCoordinator.consumeReturnUrl());
            return;
          }
        }
      } catch (err) {
        console.warn("Keycloak check-sso failed or unreachable:", err.message);
        window.keycloakInstance = null;
        portalSessionCoordinator.attachKeycloak(null);
      }

      // Unauthenticated in a non-demo build: preserve cached offline identity only when offline with a PIN wrapper.
      state.session.isDemoMode = false;
      state.session.token = null;
      if (!checkOnline() && state.session.userId) {
        await transitionParticipantScope(state.session.userId);
        const { getWrappedMasterKeyConfig } = await loadSyncQueueModule();
        const config = await getWrappedMasterKeyConfig();
        if (config.wrappedKey) {
          state.session.isOfflineMode = true;
          return;
        }
      }

      state.session.userId = null;
      state.session.isOfflineMode = false;
      await transitionParticipantScope(null);
    }
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
    await transitionParticipantScope(state.session.userId);
  }
}

// Initiates interactive OIDC sign-in when an unauthenticated participant clicks Sign In.
export async function login(options = {}) {
  authTransitionSeq += 1;
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
      hydrateSessionScopeFromClaims(null, storageToken);
      portalSessionCoordinator.markAuthenticated(storageToken);
      await transitionParticipantScope(resolvedUser);
      await ensureEphemeralDemoSessionKey();
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
      await transitionParticipantScope(demoUserId);
      await ensureEphemeralDemoSessionKey();
      return;
    } else {
      let keycloak =
        typeof window !== "undefined" ? window.keycloakInstance : null;
      if (!keycloak && typeof window !== "undefined") {
        const KeycloakClass =
          window.Keycloak || (await import("keycloak-js")).default;
        if (KeycloakClass) {
          const defaultKeycloakUrl = import.meta.env.PROD
            ? ""
            : "http://localhost:8080/";
          keycloak = new KeycloakClass({
            url: import.meta.env.VITE_KEYCLOAK_URL || defaultKeycloakUrl,
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
    }
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
        const [
          syncQueue,
          { checkPINWrapper, clearPendingResetMasterKey },
          { renderSyncQueueList },
        ] = await Promise.all([
          loadSyncQueueModule(),
          loadPinModule(),
          loadSyncStatusModule(),
        ]);
        clearPendingResetMasterKey();
        syncQueue.clearInMemoryKey();
        state.submissions = [];
        void renderSyncQueueList([]);
        syncQueue.setActiveUserId(newUid);
        activeScopedUserId = newUid || null;
        if (isDemoModeActive()) {
          const config = await syncQueue.getWrappedMasterKeyConfig();
          if (!config.wrappedKey && newUid) {
            try {
              await syncQueue.initSessionKey(newUid, newUid);
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
export async function logout(options = {}) {
  const confirmed = Boolean(options?.confirmed || options?.force);
  if (!confirmed && hasUnsavedOrUnsyncedLocalData()) {
    if (!hasLocalDraftState() && typeof indexedDB !== "undefined") {
      await prepareSignOutGuardState();
    }
    if (hasUnsavedOrUnsyncedLocalData()) {
      state.signOutGuardOpen = true;
      showToast(
        "You have unsaved survey drafts or unsent submissions on this device. Please confirm sign-out to continue.",
        { variant: "error" }
      );
      return { signedOut: false, guarded: true };
    }
  }

  lastInspectedSignOutState = { draftCount: 0, unsyncedCount: 0 };
  setTrackedLocalQueueActivity(false);
  state.signOutGuardOpen = false;
  portalSessionCoordinator.beginVoluntaryLogout();
  const wasDemo = Boolean(state.session.isDemoMode);
  state.authError = "";
  state.session.sessionExpired = false;

  try {
    localStorage.removeItem("oidc_user_id");
    localStorage.removeItem("oidc_study_id");
    localStorage.removeItem("oidc_site_id");
    localStorage.removeItem("mock_user_id");
    localStorage.removeItem("mock_token");
    localStorage.removeItem("mock_study_id");
    localStorage.removeItem("mock_site_id");
    if (typeof sessionStorage !== "undefined") {
      sessionStorage.removeItem("mock_user_id");
      sessionStorage.removeItem("mock_token");
      sessionStorage.removeItem("mock_study_id");
      sessionStorage.removeItem("mock_site_id");
      sessionStorage.removeItem("oidc_pending_pin_reset");
    }
  } catch (err) {
    console.warn("Failed to clear session storage keys on logout:", err);
  }

  const logoutSeq = ++authTransitionSeq;
  const prevUserId = state.session.userId;

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
  state.draftPresence = {};
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
    activeScopedUserId = demoUserId;
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
    activeScopedUserId = null;
    if (
      state.currentView === "view-wallet" ||
      state.currentView === "view-tasks"
    ) {
      state.unavailableScreen = null;
      showView("view-tasks");
    }
  }

  const [syncQueue, { clearPendingResetMasterKey }, { renderSyncQueueList }] =
    await Promise.all([
      loadSyncQueueModule(),
      loadPinModule(),
      loadSyncStatusModule(),
    ]);

  if (logoutSeq === authTransitionSeq) {
    clearPendingResetMasterKey();
    syncQueue.clearInMemoryKey();
    if (wasDemo) {
      syncQueue.setActiveUserId(state.session.userId);
    } else {
      void renderSyncQueueList([]);
      syncQueue.setActiveUserId(null);
      if (isDemoBuild() || import.meta.env?.MODE === "test") {
        const [{ renderTasks }, { renderCompliance }, { renderInbox }] =
          await Promise.all([
            import("./tasks.js"),
            import("./compliance.js"),
            import("./inbox.js"),
          ]);
        if (logoutSeq === authTransitionSeq) {
          renderTasks();
          renderCompliance();
          renderInbox();
        }
      }
    }
  }

  if (
    logoutSeq === authTransitionSeq ||
    (prevUserId || null) !== (state.session.userId || null)
  ) {
    await syncQueue.clearSessionKeyFromDB(prevUserId);
    if ((prevUserId || null) !== (state.session.userId || null)) {
      syncQueue.closeDatabase(prevUserId);
    }
  }

  if (
    logoutSeq === authTransitionSeq &&
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
