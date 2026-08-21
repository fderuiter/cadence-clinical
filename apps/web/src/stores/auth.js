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
  admin: [
    "sponsor_admin",
    "sponsor_designer",
    "data_manager",
    "site_investigator",
    "crc",
    "cra",
    "monitor",
    "auditor",
    "tmf_auditor",
  ],
  sponsor_admin: [
    "sponsor_admin",
    "sponsor_designer",
    "data_manager",
    "site_investigator",
    "crc",
    "cra",
    "monitor",
    "auditor",
    "tmf_auditor",
  ],
  super_admin: [
    "sponsor_admin",
    "sponsor_designer",
    "data_manager",
    "site_investigator",
    "crc",
    "cra",
    "monitor",
    "auditor",
    "tmf_auditor",
  ],
};

export const PERSONA_PRESETS = [
  {
    key: "super_admin",
    label: "👑 Super Admin (All Access)",
    roles: [
      "Sponsor Admin",
      "Sponsor Designer",
      "Data Manager",
      "Site Investigator",
      "CRC",
      "CRA",
      "Monitor",
      "Auditor",
      "TMF Auditor",
    ],
  },
  {
    key: "sponsor_designer",
    label: "📋 Sponsor Protocol Designer",
    roles: ["Sponsor Designer", "Data Manager"],
  },
  {
    key: "site_crc",
    label: "🩺 Site Coordinator / CRC",
    roles: ["Site Investigator", "CRC"],
  },
  {
    key: "cra_monitor",
    label: "📊 CRA Clinical Monitor",
    roles: ["CRA", "Monitor"],
  },
  {
    key: "data_manager",
    label: "⚙️ Clinical Data Manager",
    roles: ["Data Manager", "Sponsor Designer"],
  },
  {
    key: "auditor",
    label: "🔒 GxP & Part 11 Auditor",
    roles: ["Auditor", "TMF Auditor"],
  },
];

export const useAuthStore = defineStore("auth", {
  state: () => {
    let saved = {};
    if (typeof window !== "undefined" && window.sessionStorage) {
      try {
        const stored = window.sessionStorage.getItem("cadence_auth");
        if (stored) {
          saved = JSON.parse(stored);
        }
      } catch (e) {
        console.error("Failed to parse auth from sessionStorage", e);
      }
    }
    return {
      isAuthenticated: saved.isAuthenticated || false,
      accessToken: saved.accessToken || null,
      idToken: saved.idToken || null,
      refreshToken: saved.refreshToken || null,
      user: saved.user || null, // includes identity details like username, email, firstName, lastName, and id
      rawRoles: saved.rawRoles || [
        "Sponsor Admin",
        "Sponsor Designer",
        "Data Manager",
        "Site Investigator",
        "CRC",
        "CRA",
        "Monitor",
        "Auditor",
      ],
      currentPersona: saved.currentPersona || "super_admin",
      isDemoMode: saved.isDemoMode !== undefined ? saved.isDemoMode : true, // defaults to true, set to false if Keycloak initializes successfully
    };
  },
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
      // Normalize raw roles to UI roles
      return (state.rawRoles || []).map((role) => {
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
        if (normalized === "admin" || normalized === "super_admin") {
          return "sponsor_admin";
        }
        return normalized;
      });
    },
    hasRole: (state) => (role) => {
      if (!role) return false;
      const normalized = role.trim().toLowerCase().replace(/[\s-_]+/g, "_");
      const allowedAliases = ROLE_ALIASES[normalized] || [normalized];
      const currentRoles = (state.rawRoles || []).map((r) =>
        r.trim().toLowerCase().replace(/[\s-_]+/g, "_")
      );
      if (
        currentRoles.some(
          (r) => r === "admin" || r === "super_admin" || r === "sponsor_admin"
        )
      ) {
        return true;
      }
      return allowedAliases.some((alias) => currentRoles.includes(alias));
    },
    isCrc: (state) => {
      const currentRoles = (state.rawRoles || []).map((r) =>
        r.trim().toLowerCase().replace(/[\s-_]+/g, "_")
      );
      if (
        currentRoles.some(
          (r) => r === "admin" || r === "super_admin" || r === "sponsor_admin"
        )
      ) {
        return true;
      }
      const crcAliases = ["crc", "site_investigator", "site_crc"];
      return crcAliases.some((alias) => currentRoles.includes(alias));
    },
  },
  actions: {
    persist() {
      if (typeof window !== "undefined" && window.sessionStorage) {
        window.sessionStorage.setItem(
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
    async login(options = {}) {
      if (window.keycloakInstance && !this.isDemoMode) {
        await window.keycloakInstance.login(options);
      } else {
        const isProduction =
          (import.meta.env.PROD || import.meta.env.MODE === "production") &&
          import.meta.env.MODE !== "demo";
        if (isProduction) {
          throw new Error(
            "Offline login fallback is disabled in production environments."
          );
        }
        console.warn(
          "Keycloak not initialized or running in demo mode. Attempting gateway-issued ephemeral session login."
        );
        try {
          const baseUrl =
            import.meta.env?.VITE_API_BASE_URL || "http://localhost:8000";
          const resp = await fetch(`${baseUrl}/api/v1/auth/demo-session`, {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
            },
            body: JSON.stringify({
              username: "demo-user",
              roles: [
                "Sponsor Admin",
                "Sponsor Designer",
                "Data Manager",
                "Site Investigator",
                "CRC",
                "CRA",
                "Monitor",
                "Auditor",
                "TMF Auditor",
              ],
              tenant_id: "sandbox-tenant-default",
            }),
          });
          if (resp.ok) {
            const data = await resp.json();
            this.accessToken = data.access_token;
            this.isAuthenticated = true;
            this.isDemoMode = true;
            this.user = {
              username: data.username,
              email: "demo-user@example.com",
              firstName: "Demo",
              lastName: "User",
              id: "demo-sub-id",
            };
            this.rawRoles = data.roles || [
              "Sponsor Admin",
              "Sponsor Designer",
              "Data Manager",
              "Site Investigator",
              "CRC",
              "CRA",
              "Monitor",
              "Auditor",
              "TMF Auditor",
            ];
          } else {
            throw new Error("Demo session endpoint failed");
          }
        } catch (e) {
          console.error(
            "Gateway demo session login failed. Falling back to offline client mock.",
            e
          );
          this.isAuthenticated = true;
          this.isDemoMode = true;
          // Seed default roles so the offline UI is functional
          this.rawRoles = [
            "Sponsor Admin",
            "Sponsor Designer",
            "Data Manager",
            "Site Investigator",
            "CRC",
            "CRA",
            "Monitor",
            "Auditor",
            "TMF Auditor",
          ];
        }
      }
      this.persist();
    },
    switchPersona(personaKey) {
      const found = PERSONA_PRESETS.find((p) => p.key === personaKey);
      if (found) {
        this.currentPersona = personaKey;
        this.rawRoles = [...found.roles];
        this.persist();
      }
    },
    async logout(options = {}) {
      if (window.keycloakInstance && !this.isDemoMode) {
        await window.keycloakInstance.logout(options);
      } else {
        const isProduction =
          (import.meta.env.PROD || import.meta.env.MODE === "production") &&
          import.meta.env.MODE !== "demo";
        if (isProduction) {
          throw new Error(
            "Offline logout fallback is disabled in production environments."
          );
        }
        console.warn(
          "Keycloak not initialized or running in demo mode. Logging out from offline mock."
        );
        this.isAuthenticated = false;
        this.isDemoMode = true;
        this.rawRoles = [];
      }
      this.persist();
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
