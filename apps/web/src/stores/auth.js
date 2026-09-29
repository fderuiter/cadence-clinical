import { defineStore } from "pinia";
import { useNotificationsStore } from "./notifications.js";
import { isDemoBuild } from "../utils/demoMode.js";

/**
 * Seeding Gap Documentation Resolved:
 * The "Sponsor Designer" role has been successfully seeded in the Keycloak realm configuration
 * (`cadence-realm.json`) and is centrally mapped in the OIDC normalizer.
 */
/** Offline identity used only by demo builds (see utils/demoMode.js). */
export const DEMO_IDENTITY = Object.freeze({
  username: "fderuiter",
  email: "fderuiter@example.com", // deid-ignore
  firstName: "Frans",
  lastName: "de Ruiter",
  id: "fderuiter-id-12345",
});

export const DEMO_ROLES = Object.freeze([
  "Sponsor Admin",
  "Sponsor Designer",
  "Data Manager",
  "Site Investigator",
  "CRC",
  "CRA",
  "Monitor",
  "Auditor",
  "TMF Auditor",
]);

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

export function normalizeRole(role) {
  const normalized = (role || "")
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
}

export const useAuthStore = defineStore("auth", {
  state: () => {
    let saved = {};
    if (typeof window !== "undefined") {
      try {
        const stored =
          (window.sessionStorage &&
            window.sessionStorage.getItem("cadence_auth")) ||
          (window.localStorage && window.localStorage.getItem("cadence_auth"));
        if (stored) {
          saved = JSON.parse(stored);
        }
      } catch (e) {
        console.error("Failed to parse auth from storage", e);
      }
    }

    let restoredPersona = "super_admin";
    let hasSavedPersona = false;
    if (saved && typeof saved.currentPersona === "string") {
      const preset = PERSONA_PRESETS.find(
        (p) => p.key === saved.currentPersona
      );
      if (preset) {
        restoredPersona = saved.currentPersona;
        hasSavedPersona = true;
      }
    }

    const matchedPreset = PERSONA_PRESETS.find(
      (p) => p.key === restoredPersona
    );
    const defaultRoles =
      hasSavedPersona && matchedPreset
        ? [...matchedPreset.roles]
        : [
            "Sponsor Admin",
            "Sponsor Designer",
            "Data Manager",
            "Site Investigator",
            "CRC",
            "CRA",
            "Monitor",
            "Auditor",
          ];

    const restoredRoles =
      saved && Array.isArray(saved.rawRoles) && saved.rawRoles.length > 0
        ? saved.rawRoles
        : defaultRoles;

    return {
      isAuthenticated: saved.isAuthenticated || false,
      accessToken: saved.accessToken || null,
      idToken: saved.idToken || null,
      refreshToken: saved.refreshToken || null,
      user: saved.user || null, // includes identity details like username, email, firstName, lastName, and id
      // Outside a demo build nothing grants roles except a verified token.
      rawRoles: isDemoBuild() ? restoredRoles : saved.rawRoles || [],
      currentPersona: restoredPersona,
      // Only a demo build can run in demo mode; a stored flag cannot turn it on (#4523).
      isDemoMode: isDemoBuild(),
    };
  },
  getters: {
    identity: (state) => {
      if (state.isDemoMode && !state.isAuthenticated) {
        return { ...DEMO_IDENTITY };
      }
      return state.user;
    },
    token: (state) => state.accessToken,
    normalizedRoles: (state) => {
      // Normalize raw roles to UI roles
      return (state.rawRoles || []).map(normalizeRole);
    },
    hasRole: (state) => (role) => {
      if (!role) return false;
      const normalized = normalizeRole(role);
      const targetRoles = ROLE_ALIASES[normalized] || [normalized];
      const userRoles = (state.rawRoles || []).map(normalizeRole);
      return userRoles.some((uRole) => {
        if (targetRoles.includes(uRole)) return true;
        const aliases = ROLE_ALIASES[uRole] || [];
        return aliases.some((alias) => targetRoles.includes(alias));
      });
    },
    isCrc: (state) => {
      const userRoles = (state.rawRoles || []).map(normalizeRole);
      const crcTargets = ["crc", "site_investigator"];
      return userRoles.some((uRole) => {
        if (crcTargets.includes(uRole)) return true;
        const aliases = ROLE_ALIASES[uRole] || [];
        return aliases.some((alias) => crcTargets.includes(alias));
      });
    },
  },
  actions: {
    persist() {
      if (typeof window !== "undefined") {
        if (!this.isAuthenticated) {
          if (window.sessionStorage) {
            window.sessionStorage.removeItem("cadence_auth");
          }
          if (window.localStorage) {
            window.localStorage.removeItem("cadence_auth");
          }
          return;
        }
        if (window.sessionStorage) {
          window.sessionStorage.setItem(
            "cadence_auth",
            JSON.stringify({
              isAuthenticated: this.isAuthenticated,
              accessToken: this.accessToken,
              idToken: this.idToken,
              refreshToken: this.refreshToken,
              user: this.user,
              rawRoles: this.rawRoles,
              currentPersona: this.currentPersona,
              isDemoMode: this.isDemoMode,
            })
          );
        }
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
        this.currentPersona = "super_admin";
      }
      this.persist();
    },
    async login(options = {}) {
      if (window.keycloakInstance && !this.isDemoMode) {
        await window.keycloakInstance.login(options);
      } else {
        if (!isDemoBuild()) {
          throw new Error(
            "Sign-in is unavailable: the identity provider could not be reached."
          );
        }
        // Demo build: an offline identity with every persona's roles.
        this.isAuthenticated = true;
        this.isDemoMode = true;
        this.user = { ...DEMO_IDENTITY };
        this.rawRoles = [...DEMO_ROLES];
      }
      this.persist();
    },
    switchPersona(personaKey) {
      const found = PERSONA_PRESETS.find((p) => p.key === personaKey);
      if (found) {
        this.currentPersona = personaKey;
        this.rawRoles = [...found.roles];
        this.persist();
        try {
          const notificationsStore = useNotificationsStore();
          notificationsStore.fetchNotifications();
        } catch {
          // Pinia store not ready or active
        }
      }
    },
    async logout(options = {}) {
      if (window.keycloakInstance && !this.isDemoMode) {
        await window.keycloakInstance.logout(options);
      } else {
        this.isAuthenticated = false;
        this.rawRoles = [];
      }
      this.currentPersona = "super_admin";
      this.user = null;
      this.accessToken = null;
      this.idToken = null;
      this.refreshToken = null;
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
