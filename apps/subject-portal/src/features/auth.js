// Participant identity: cached user, PIN-gated session state, Keycloak sign-in, logout.
import { watch } from "vue";

import { state } from "../stores/state.js";
import {
  clearInMemoryKey,
  clearSessionKey,
  clearSessionKeyFromDB,
  getInMemorySessionKey,
  getWrappedMasterKeyConfig,
  initSessionKey,
  setActiveUserId,
} from "../sync-queue.js";
import { isDemoBuild, isDemoModeActive } from "../utils/demoMode.js";
import { renderCompliance } from "./compliance.js";
import { renderInbox } from "./inbox.js";
import { checkPINWrapper } from "./pin.js";
import { checkOnline } from "./sync-status.js";
import { renderTasks } from "./tasks.js";

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
      setActiveUserId(storageUser);
      return;
    }

    if (isDemoBuild()) {
      state.session.userId = state.session.userId || "subject_001";
      state.session.token = null;
      state.session.isOfflineMode = true;
      state.session.isDemoMode = true;
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
        keycloak.onAuthLogout = () => {
          logout();
        };

        await keycloak.init({
          onLoad: "check-sso",
          pkceMethod: "S256",
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
          setActiveUserId(authenticatedUserId);

          if (authenticatedUserId) {
            try {
              localStorage.setItem("oidc_user_id", authenticatedUserId);
            } catch (err) {
              console.warn("Failed to save oidc_user_id to localStorage:", err);
            }
          }
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
      if (!cachedUserId && state.session.userId === "subject_001") {
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
export async function login() {
  state.authError = "";
  try {
    let keycloak = window.keycloakInstance;
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
        await keycloak.init({ pkceMethod: "S256" });
      }
    }

    if (keycloak && typeof keycloak.login === "function") {
      await keycloak.login();
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
  const wasDemo = isDemoModeActive();

  try {
    localStorage.removeItem("oidc_user_id");
    localStorage.removeItem("mock_user_id");
    localStorage.removeItem("mock_token");
    if (typeof sessionStorage !== "undefined") {
      sessionStorage.removeItem("mock_user_id");
      sessionStorage.removeItem("mock_token");
    }
  } catch (err) {
    console.warn("Failed to clear session storage keys on logout:", err);
  }

  const prevUserId = state.session.userId;
  clearSessionKey(prevUserId);
  await clearSessionKeyFromDB(prevUserId);

  state.pinSetup.isOpen = false;
  state.pinSetup.pin = "";
  state.pinSetup.confirmPin = "";
  state.pinSetup.error = "";

  state.pinUnlock.isOpen = false;
  state.pinUnlock.pin = "";
  state.pinUnlock.error = "";
  state.pinUnlock.confirmingReset = false;

  state.activeQuestionnaire = null;
  state.walletBalance = null;

  if (wasDemo) {
    state.session.userId = "subject_001";
    state.session.token = null;
    state.session.isOfflineMode = true;
    state.session.isDemoMode = true;
    setActiveUserId("subject_001");
  } else {
    state.session.userId = null;
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
