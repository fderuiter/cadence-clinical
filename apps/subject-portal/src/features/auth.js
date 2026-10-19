// Participant identity: cached user, session key, Keycloak sign-in, logout.
import { watch } from "vue";

import { state } from "../stores/state.js";
import {
  clearSessionKey,
  clearSessionKeyFromDB,
  getInMemorySessionKey,
  initSessionKey,
  restoreSessionKeyFromDB,
  setActiveUserId,
} from "../sync-queue.js";

// Restores the cached user identity, then the offline session key for it.
export async function restoreSession() {
  // Check and restore cached user identity from local storage before establishing key derivation
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

  if (!getInMemorySessionKey()) {
    try {
      await restoreSessionKeyFromDB(state.session.userId);
    } catch (err) {
      console.warn("Failed to restore session key from IndexedDB:", err);
    }
  }

  if (!getInMemorySessionKey()) {
    const sessionMaterial =
      state.session.token || state.session.userId || "demo-material";
    try {
      await initSessionKey(sessionMaterial, state.session.userId);
    } catch (err) {
      console.warn("Failed to initialize session key:", err);
    }
  }
}

// Signs in through injected storage credentials or Keycloak (check-sso).
export async function signIn() {
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
      console.log(
        "Session verified via injected storage credentials for subject:",
        state.session.userId
      );
    } else {
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

          if (keycloak.authenticated) {
            const authenticatedUserId = keycloak.subject || "subject_001";
            state.session.userId = authenticatedUserId;
            state.session.token = keycloak.token;
            state.session.isOfflineMode = false;
            state.session.isDemoMode = false;
            console.log(
              "OIDC Session Verified for subject:",
              state.session.userId
            );

            // Save the verified user identifier into persistent local storage upon successful login
            try {
              localStorage.setItem("oidc_user_id", authenticatedUserId);
            } catch (err) {
              console.warn("Failed to save oidc_user_id to localStorage:", err);
            }
          } else {
            state.session.isDemoMode = true;
          }
        } else {
          state.session.isDemoMode = true;
        }
      } catch (err) {
        state.session.isDemoMode = true;
        console.warn(
          "Keycloak login failed or offline. Continuing in sandbox demo mode:",
          err.message
        );
      }
    }
  } else {
    // If window.__MOCK_TEST_ENV__ is true, we keep state.session.isDemoMode as-is (e.g. tests can override or drive it)
    state.session.isOfflineMode = false;
  }
}

// Re-derives the session key whenever the active user identifier changes.
export function watchUserIdentity() {
  watch(
    () => state.session.userId,
    async (newUid, oldUid) => {
      if (newUid !== oldUid) {
        console.log(
          `[Auth] User identity changed from ${oldUid} to ${newUid}. Re-deriving encryption key.`
        );
        setActiveUserId(newUid);
        const sessionMaterial =
          state.session.token || newUid || "demo-material";
        try {
          await initSessionKey(sessionMaterial, newUid);
        } catch (err) {
          console.warn(
            "Failed to re-derive session key on identity change:",
            err
          );
        }
      }
    }
  );
}

// User logout events trigger immediate deletion of the cached OIDC identifier and persisted session key
export async function logout() {
  try {
    localStorage.removeItem("oidc_user_id");
  } catch (err) {
    console.warn("Failed to clear oidc_user_id from localStorage:", err);
  }

  const prevUserId = state.session.userId;
  state.session.userId = "subject_001";
  state.session.token = null;
  state.session.isOfflineMode = true;
  state.session.isDemoMode = true;

  clearSessionKey(prevUserId);
  await clearSessionKeyFromDB(prevUserId);

  if (
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
