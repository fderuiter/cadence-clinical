import { defineStore } from "pinia";

/**
 * Seeding Gap Documentation:
 * In the Keycloak realm configuration (`cadence-realm.json`), there is a role-seeding gap:
 * The "Study Designer" (or `sponsor_designer`) role required by the backend/designer
 * microservice is missing from the realm roles definition.
 *
 * Future deployments should add the `sponsor_designer` (or `Study Designer`) role to Keycloak
 * and map it accordingly in this normalizer.
 */
export const useAuthStore = defineStore("auth", {
  state: () => ({
    isAuthenticated: false,
    accessToken: null,
    idToken: null,
    refreshToken: null,
    user: null, // includes identity details like username, email, firstName, lastName, and id
    rawRoles: [],
    isDemoMode: true, // defaults to true, set to false if Keycloak initializes successfully
  }),
  getters: {
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
    token: (state) => state.accessToken,
    normalizedRoles: (state) => {
      if (state.isDemoMode && !state.isAuthenticated) {
        // Fallback demo roles normalized
        return ["monitor", "sponsor_admin"];
      }
      // Normalize raw roles to UI roles
      return state.rawRoles.map((role) => {
        // Map Keycloak realm roles to standard UI roles (lowercase with underscores)
        // E.g. "Sponsor Admin" -> "sponsor_admin", "Data Manager" -> "data_manager"
        return role
          .trim()
          .toLowerCase()
          .replace(/[\s-_]+/g, "_");
      });
    },
  },
  actions: {
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
    },
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
          "CRA",
          "Data Manager",
          "Site Investigator",
          "Auditor",
        ];
      }
    },
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
    },
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
