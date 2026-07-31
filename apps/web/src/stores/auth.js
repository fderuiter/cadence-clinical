import { defineStore } from "pinia";

/**
 * Seeding Gap Documentation Resolved:
 * The "Sponsor Designer" role has been successfully seeded in the Keycloak realm configuration
 * (`cadence-realm.json`) and is centrally mapped in the OIDC normalizer.
 */
export const ROLE_ALIASES = {
  crc: ["site_investigator", "crc"],
  site_investigator: ["site_investigator", "crc"],
  cra: ["cra", "monitor"],
  monitor: ["cra", "monitor"],
  auditor: ["auditor", "tmf_auditor"],
  tmf_auditor: ["auditor", "tmf_auditor"],
  designer: ["sponsor_designer"],
  sponsor_designer: ["sponsor_designer"],
  study_designer: ["sponsor_designer"],
};

/**
 * Pinia Store managing the OIDC (Keycloak) authentication state.
 * Employs fallback mechanisms for offline demo mode and maps realm roles to canonical UI roles.
 *
 * @see {@link ROLE_ALIASES} for mapping definitions.
 * @see PRD-SYS-001 (GxP Compliance & 21 CFR Part 11)
 */
export const useAuthStore = defineStore("auth", {
  state: () => {
    let saved = {};
    if (typeof window !== "undefined" && window.localStorage) {
      try {
        const stored = window.localStorage.getItem("cadence_auth");
        if (stored) {
          saved = JSON.parse(stored);
        }
      } catch (e) {
        console.error("Failed to parse auth from localStorage", e);
      }
    }
    return {
      isAuthenticated: saved.isAuthenticated || false,
      accessToken: saved.accessToken || null,
      idToken: saved.idToken || null,
      refreshToken: saved.refreshToken || null,
      user: saved.user || null, // includes identity details like username, email, firstName, lastName, and id
      rawRoles: saved.rawRoles || [],
      isDemoMode: saved.isDemoMode !== undefined ? saved.isDemoMode : true, // defaults to true, set to false if Keycloak initializes successfully
    };
  },
  getters: {
    /**
     * Resolves user identity, falling back to a dummy profile when in demo mode.
     * @param {Object} state - The Pinia store state object.
     * @returns {Object|null} The resolved user identity object.
     */
    identity: (state) => {
      if (state.isDemoMode && !state.isAuthenticated) {
        // Fallback demo identity
        return {
          username: "fderuiter",
          email: "fderuiter@example.com",
          firstName: "Frans",
          lastName: "de Ruiter",
          id: "fderuiter-id-12345",
        };
      }
      return state.user;
    },
    /**
     * Getter returning the active JWT access token.
     * @param {Object} state - The Pinia store state object.
     * @returns {string|null} The access token string.
     */
    token: (state) => state.accessToken,
    /**
     * Normalizes mixed realm roles into lowercase, snake_case canonical keys.
     * @param {Object} state - The Pinia store state object.
     * @returns {string[]} Normalized list of standard UI roles.
     */
    normalizedRoles: (state) => {
      if (state.isDemoMode && !state.isAuthenticated) {
        // Fallback demo roles normalized
        return ["monitor", "sponsor_admin"];
      }
      // Normalize raw roles to UI roles
      return state.rawRoles.map((role) => {
        // Map Keycloak realm roles to standard UI roles (lowercase with underscores)
        // E.g. "Sponsor Admin" -> "sponsor_admin", "Data Manager" -> "data_manager"
        const normalized = role
          .trim()
          .toLowerCase()
          .replace(/[\s-_]+/g, "_");
        if (
          normalized === "study_designer" ||
          normalized === "designer" ||
          normalized === "sponsor_designer"
        ) {
          return "sponsor_designer";
        }
        return normalized;
      });
    },
  },
  actions: {
    /**
     * Persists the authentication store status to local storage for persistence across reloads.
     */
    persist() {
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.setItem(
          "cadence_auth",
          JSON.stringify({
            isAuthenticated: this.isAuthenticated,
            accessToken: this.accessToken,
            idToken: this.idToken,
            refreshToken: this.refreshToken,
            user: this.user,
            rawRoles: this.rawRoles,
            isDemoMode: this.isDemoMode,
          })
        );
      }
    },
    /**
     * Populates store state using claims and tokens parsed from an authenticated Keycloak instance.
     * @param {Object|null} keycloak - Keycloak JS SDK instance.
     */
    setAuth(keycloak) {
      if (keycloak && keycloak.authenticated) {
        this.isAuthenticated = true;
        this.accessToken = keycloak.token || null;
        this.idToken = keycloak.idToken || null;
        this.refreshToken = keycloak.refreshToken || null;
        this.isDemoMode = false;

        const tokenParsed = keycloak.tokenParsed || {};
        this.user = {
          username:
            tokenParsed.preferred_username || tokenParsed.username || "unknown",
          email: tokenParsed.email || "",
          firstName: tokenParsed.given_name || "",
          lastName: tokenParsed.family_name || "",
          id: tokenParsed.sub || "unknown-sub",
        };
        this.rawRoles = tokenParsed.realm_access?.roles || [];
      } else {
        this.isAuthenticated = false;
        this.accessToken = null;
        this.idToken = null;
        this.refreshToken = null;
        this.user = null;
        this.rawRoles = [];
      }
      this.persist();
    },
    /**
     * Executes standard login flow via Keycloak or offline fallback credentials.
     * @param {Object} options - Keycloak login parameters.
     */
    async login(options = {}) {
      if (window.keycloakInstance && !this.isDemoMode) {
        await window.keycloakInstance.login(options);
      } else {
        console.warn(
          "Keycloak not initialized or running in demo mode. Logging in with offline mock."
        );
        this.isAuthenticated = true;
        this.isDemoMode = true;
        // Seed default roles so the offline UI is functional
        this.rawRoles = [
          "Sponsor Admin",
          "Sponsor Designer",
          "CRA",
          "Data Manager",
          "Site Investigator",
          "Auditor",
        ];
      }
      this.persist();
    },
    /**
     * Clears session credentials and executes logout sequence.
     * @param {Object} options - Keycloak logout options.
     */
    async logout(options = {}) {
      if (window.keycloakInstance && !this.isDemoMode) {
        await window.keycloakInstance.logout(options);
      } else {
        console.warn(
          "Keycloak not initialized or running in demo mode. Logging out from offline mock."
        );
        this.isAuthenticated = false;
        this.isDemoMode = true;
        this.rawRoles = [];
      }
      this.persist();
    },
    /**
     * Dispatches token refresh sequence if token validity falls below the minimum threshold.
     * @param {number} minValidity - Validity threshold in seconds.
     * @returns {Promise<boolean>} Resolves indicating if token refresh completed.
     */
    async refresh(minValidity = 30) {
      if (window.keycloakInstance && !this.isDemoMode) {
        try {
          const refreshed =
            await window.keycloakInstance.updateToken(minValidity);
          if (refreshed) {
            this.setAuth(window.keycloakInstance);
          }
          return refreshed;
        } catch (err) {
          console.error("Failed to refresh token:", err);
          this.setAuth(null);
          throw err;
        }
      }
      return false;
    },
  },
});
