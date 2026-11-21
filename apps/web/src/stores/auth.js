import { defineStore } from "pinia";
import { flushRegisteredDraftSourcesSync } from "ui";
import { getKeycloakClient } from "../auth/keycloak.js";
import {
  clearWebReturnUrl,
  getCurrentInAppReturnUrl,
  notifySessionEnded,
  peekWebReturnUrl,
  registerAuthSessionBridge,
  resolveWebRedirectUri,
  saveWebReturnUrl,
  webSessionCoordinator,
} from "../auth/sessionCoordinator.js";
import { kernelApi } from "../api/kernelApi";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/productCatalog.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { useNotificationsStore } from "./notifications.js";
import { useClinicalStore } from "./clinical.ts";
import { useOnboardingStore } from "./onboarding.js";
import {
  purgeUserScopedStorage,
  storageScopeOf,
} from "../utils/userScopedStorage.js";

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
  tenantId: "demo",
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

/**
 * Workspace personas. A persona only selects which workspace, help content and
 * onboarding tour the UI shows; it never grants a role. Roles always come from
 * the Keycloak access token (`realm_access.roles`). A user can view a persona
 * only when their token carries one of the persona's `requires` roles, and
 * `super_admin` requires the `Sponsor Admin` realm role.
 * Presets are listed in the order used to pick a user's default persona.
 */
export const PERSONA_PRESETS = [
  {
    key: "super_admin",
    label: "👑 Super Admin (All Access)",
    requires: ["sponsor_admin"],
  },
  {
    key: "sponsor_designer",
    label: "📋 Sponsor Protocol Designer",
    requires: ["sponsor_designer"],
  },
  {
    key: "data_manager",
    label: "⚙️ Clinical Data Manager",
    requires: ["data_manager"],
  },
  {
    key: "site_crc",
    label: "🩺 Site Coordinator / CRC",
    requires: ["site_investigator", "crc"],
  },
  {
    key: "cra_monitor",
    label: "📊 CRA Clinical Monitor",
    requires: ["cra", "monitor"],
  },
  {
    key: "auditor",
    label: "🔒 GxP & Part 11 Auditor",
    requires: ["auditor", "tmf_auditor"],
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

/** Storage key older builds used to persist tokens; purged on start-up. */
const LEGACY_AUTH_STORAGE_KEY = "cadence_auth";

function purgeLegacyAuthStorage() {
  if (typeof window === "undefined") return;
  try {
    window.sessionStorage?.removeItem(LEGACY_AUTH_STORAGE_KEY);
    window.localStorage?.removeItem(LEGACY_AUTH_STORAGE_KEY);
  } catch {
    // Storage can be unavailable (private mode, sandboxed frames).
  }
}

/**
 * Purges legacy unscoped local storage keys on sign-in/bootstrap to prevent state contamination.
 * Onboarding keys scoped to a persona (`onboarding_${persona}_*`) are preserved. Clinical values are
 * account-scoped and handled by `purgeUserScopedStorage` (#5369).
 */
export function purgeLegacyUnscopedKeys() {
  if (typeof window === "undefined" || !window.localStorage) return;
  const legacyKeys = [
    "subjects",
    "formValues",
    "formQueries",
    "ledgerBlocks",
    "currentUsdm",
    "cadence_clinical_subjects",
    "onboarding_current_step",
    "onboarding_is_active",
    "onboarding_disabled",
    "onboarding_events",
    "onboarding_global_disabled",
    "onboarding_global_dismissed",
  ];
  try {
    for (const key of legacyKeys) {
      window.localStorage.removeItem(key);
    }
  } catch {
    // Storage can be unavailable
  }
}

function rehydrateStoresForPersona(personaKey) {
  if (!personaKey) return;
  try {
    const clinicalStore = useClinicalStore();
    if (typeof clinicalStore.loadPersonaState === "function") {
      clinicalStore.loadPersonaState(personaKey);
    }
  } catch {
    // Pinia or store not ready
  }
  try {
    const onboardingStore = useOnboardingStore();
    if (typeof onboardingStore.loadPersonaState === "function") {
      onboardingStore.loadPersonaState(personaKey);
    }
  } catch {
    // Pinia or store not ready
  }
}

/**
 * True when one of the user's realm roles is, or implies (via ROLE_ALIASES),
 * one of the normalized `targets`. Implication runs one way only: a Sponsor
 * Admin holds `crc`, but a CRC does not hold `sponsor_admin`.
 */
function holdsAnyRole(rawRoles, targets) {
  return (rawRoles || []).map(normalizeRole).some((role) => {
    if (targets.includes(role)) return true;
    return (ROLE_ALIASES[role] || []).some((alias) => targets.includes(alias));
  });
}

/** Personas the given realm roles entitle the user to view. */
export function personasForRoles(rawRoles) {
  return PERSONA_PRESETS.filter((p) => holdsAnyRole(rawRoles, p.requires));
}

/**
 * @typedef {object} AuthUser
 * @property {string} username - `preferred_username` claim
 * @property {string} email
 * @property {string} firstName
 * @property {string} lastName
 * @property {string} id - `sub` claim
 * @property {string} tenantId - `tenant_id` claim; blank when the token has none
 */

/** Milliseconds per UTC calendar day for trial countdown calculation. */
const MS_PER_DAY = 86_400_000;

/**
 * Computes exact whole days remaining until `trialEndsAtIso`, clamped at 0.
 *
 * @param {string|null|undefined} trialEndsAtIso
 * @param {number} [nowMs=Date.now()]
 * @returns {number|null}
 */
export function computeTrialDaysRemaining(trialEndsAtIso, nowMs = Date.now()) {
  if (!trialEndsAtIso || typeof trialEndsAtIso !== "string") {
    return null;
  }
  const endMs = Date.parse(trialEndsAtIso);
  if (Number.isNaN(endMs)) {
    return null;
  }
  return Math.max(0, Math.ceil((endMs - nowMs) / MS_PER_DAY));
}

/**
 * Formats an ISO-8601 trial end timestamp as a deterministic UTC calendar date (`Jan 15, 2030`).
 *
 * @param {string|null|undefined} trialEndsAtIso
 * @returns {string|null}
 */
export function formatTrialEndDate(trialEndsAtIso) {
  if (!trialEndsAtIso || typeof trialEndsAtIso !== "string") {
    return null;
  }
  const parsed = new Date(trialEndsAtIso);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }
  return new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    timeZone: "UTC",
  }).format(parsed);
}

function emptyAuthState() {
  return {
    isAuthenticated: false,
    sessionExpired: false,
    isLoggingOut: false,
    /** @type {string | null} */
    accessToken: null,
    /** @type {string | null} */
    idToken: null,
    /** @type {number | null} epoch milliseconds */
    tokenExpiresAt: null,
    /** @type {AuthUser | null} */
    user: null,
    /** @type {string[]} Keycloak realm roles from `realm_access.roles` */
    rawRoles: [],
    /** @type {string | null} */
    currentPersona: null,
    /** @type {string | null} */
    authError: null,
    /** @type {import("../api/kernelApi").KernelEntitlements | null} */
    entitlements: null,
    entitlementsLoaded: false,
    /** @type {string | null} */
    subscriptionExpiredNotice: null,
    /** True only in a demo build; nothing at run time can turn it on (#4523). */
    isDemoMode: isDemoBuild(),
  };
}

/**
 * Authentication state for the signed-in Keycloak user.
 *
 * Identity and roles come only from the Keycloak token. There is no offline
 * sign-in: when no one is signed in the store is empty and the router sends the
 * user to the sign-in page. Tokens are kept in memory only. The one exception
 * is a demo build (`isDemoBuild()`), which has no identity provider and signs
 * in a fixed offline demo identity instead (`startDemoSession`).
 */
export const useAuthStore = defineStore("auth", {
  state: () => {
    purgeLegacyAuthStorage();
    return emptyAuthState();
  },
  getters: {
    identity: (state) => state.user,
    token: (state) => state.accessToken,
    userId: (state) => state.user?.id || null,
    normalizedRoles: (state) => (state.rawRoles || []).map(normalizeRole),
    hasRole: (state) => (role) => {
      if (!role) return false;
      return holdsAnyRole(state.rawRoles, [normalizeRole(role)]);
    },
    isCrc: (state) =>
      holdsAnyRole(state.rawRoles, ["crc", "site_investigator"]),
    availablePersonas: (state) => personasForRoles(state.rawRoles),
    /**
     * True for newly self-registered OIDC sessions that have no kernel `tenant_id`
     * claim and no assigned clinical realm roles yet, prompting trial setup.
     */
    needsTrialOnboarding: (state) =>
      Boolean(
        state.isAuthenticated &&
        !state.isDemoMode &&
        state.accessToken &&
        !state.user?.tenantId &&
        personasForRoles(state.rawRoles).length === 0
      ),
    activeTier: (state) =>
      state.entitlements?.tier ||
      (state.isDemoMode ? "Complete" : "Individual"),
    subscriptionStatus: (state) =>
      state.entitlements?.status || (state.isDemoMode ? "Active" : null),
    isTrialExpired: (state) =>
      Boolean(
        state.entitlements?.is_read_only ||
        state.entitlements?.status === "Expired"
      ),
    trialEndsAt: (state) => state.entitlements?.trial_ends_at || null,
    trialEndsAtFormatted: (state) =>
      formatTrialEndDate(state.entitlements?.trial_ends_at),
    trialDaysRemaining: (state) => {
      if (
        state.entitlements?.is_read_only ||
        state.entitlements?.status === "Expired"
      ) {
        return 0;
      }
      if (state.entitlements?.status !== "Trialing") {
        return null;
      }
      return computeTrialDaysRemaining(state.entitlements?.trial_ends_at);
    },
    entitledFamilies: (state) =>
      state.entitlements?.product_families ||
      (state.isDemoMode
        ? [
            "Blueprint",
            "Capture",
            "Orbit",
            "Engage",
            "Allocate",
            "Vigil",
            "Lens",
            "Assure",
          ]
        : ["Blueprint", "Capture"]),
    activePlanBadge: (state) => {
      if (!state.isAuthenticated) return null;
      if (state.isDemoMode && !state.entitlements) {
        return "Demo · Pre-release";
      }
      const tier = state.entitlements?.tier || "Individual";
      const status = state.entitlements?.status;
      if (state.entitlements?.is_read_only || status === "Expired") {
        return `${tier} · Trial Expired (Read & Export)`;
      }
      if (status === "Trialing") {
        const days = computeTrialDaysRemaining(
          state.entitlements?.trial_ends_at
        );
        const endDate = formatTrialEndDate(state.entitlements?.trial_ends_at);
        if (days !== null && endDate) {
          const unit = days === 1 ? "day" : "days";
          return `${tier} · Trial · ${days} ${unit} left (ends ${endDate})`;
        }
        return `${tier} · Trial`;
      }
      if (state.entitlements?.tier) {
        return `${tier} Plan`;
      }
      if (
        !state.user?.tenantId &&
        personasForRoles(state.rawRoles).length === 0
      ) {
        return "Trial Setup Pending";
      }
      return `${tier} Plan`;
    },
  },
  actions: {
    /**
     * Copies identity, roles and tokens from an authenticated keycloak-js
     * instance. Anything else clears the session.
     */
    setAuth(keycloak) {
      if (!keycloak || !keycloak.authenticated || !keycloak.token) {
        this.clearAuth();
        return;
      }
      const claims = keycloak.tokenParsed || {};
      this.isAuthenticated = true;
      this.sessionExpired = false;
      this.isLoggingOut = false;
      this.accessToken = keycloak.token;
      this.idToken = keycloak.idToken || null;
      this.tokenExpiresAt = claims.exp ? claims.exp * 1000 : null;
      this.user = {
        username: claims.preferred_username || claims.sub || "",
        email: claims.email || "",
        firstName: claims.given_name || "",
        lastName: claims.family_name || "",
        id: claims.sub || "",
        tenantId: claims.tenant_id || "",
      };
      this.rawRoles = Array.isArray(claims.realm_access?.roles)
        ? [...claims.realm_access.roles]
        : [];
      this.authError = null;
      webSessionCoordinator.markAuthenticated();

      const entitled = personasForRoles(this.rawRoles);
      if (!entitled.some((p) => p.key === this.currentPersona)) {
        this.currentPersona = entitled[0]?.key || null;
      }
      purgeLegacyUnscopedKeys();
      // Another account's (or an unattributable) clinical draft never survives
      // this account signing in (#5369).
      purgeUserScopedStorage(storageScopeOf(this));
      if (this.currentPersona) {
        rehydrateStoresForPersona(this.currentPersona);
      }
    },
    clearAuth() {
      const authError = this.authError;
      Object.assign(this, emptyAuthState());
      this.authError = authError;
    },
    /**
     * Ends an expired OIDC session without purging account-scoped browser drafts,
     * flushes any pending debounced form edits while the user scope is still active,
     * and records the current in-app return path for post-sign-in restoration.
     */
    expireSession(returnPath) {
      const authError = this.authError;
      flushRegisteredDraftSourcesSync();
      saveWebReturnUrl(returnPath || getCurrentInAppReturnUrl());
      Object.assign(this, emptyAuthState());
      this.authError = authError;
      this.sessionExpired = true;
    },
    /**
     * Signs in the offline demo identity. Only a demo build can do this; in any
     * other build it throws, so no code path can sign in without Keycloak.
     */
    startDemoSession() {
      if (!isDemoBuild()) {
        throw new Error("Demo sign-in is only available in a demo build.");
      }
      Object.assign(this, emptyAuthState());
      this.isAuthenticated = true;
      this.sessionExpired = false;
      this.isLoggingOut = false;
      this.user = { ...DEMO_IDENTITY };
      this.rawRoles = [...DEMO_ROLES];
      this.currentPersona = personasForRoles(this.rawRoles)[0]?.key || null;
      purgeLegacyUnscopedKeys();
      purgeUserScopedStorage(storageScopeOf(this));
      if (this.currentPersona) {
        rehydrateStoresForPersona(this.currentPersona);
      }
    },
    /** Starts the Keycloak authorization code + PKCE sign-in redirect. */
    async login(options = {}) {
      if (isDemoBuild()) {
        this.startDemoSession();
        return;
      }
      const keycloak = getKeycloakClient();
      if (!keycloak) {
        throw new Error(
          "Keycloak sign-in is unavailable. The identity provider could not be initialised."
        );
      }
      if (options && options.redirectUri) {
        await keycloak.login(options);
        return;
      }
      const returnPath = peekWebReturnUrl() || getCurrentInAppReturnUrl();
      if (returnPath) {
        saveWebReturnUrl(returnPath);
      }
      await keycloak.login({
        ...options,
        redirectUri: resolveWebRedirectUri(returnPath || "/"),
      });
    },
    /** Starts the Keycloak authorization code + PKCE S256 self-registration redirect. */
    async register(options = {}) {
      if (isDemoBuild()) {
        this.startDemoSession();
        return;
      }
      const keycloak = getKeycloakClient();
      if (!keycloak) {
        throw new Error(
          "Keycloak registration is unavailable. The identity provider could not be initialised."
        );
      }
      const registerOptions = { pkceMethod: "S256", ...options };
      if (typeof keycloak.register === "function") {
        await keycloak.register(registerOptions);
        return;
      }
      await keycloak.login({ ...registerOptions, action: "register" });
    },
    /** Loads commercial subscription and product-family entitlements for the active kernel tenant. */
    async loadEntitlements({ force = false } = {}) {
      if (!this.isAuthenticated || this.needsTrialOnboarding) {
        return null;
      }
      if (this.entitlementsLoaded && !force && this.entitlements) {
        return this.entitlements;
      }
      if (
        !this.isDemoMode &&
        (!this.accessToken || this.accessToken === "test-access-token")
      ) {
        if (!force) {
          return this.entitlements;
        }
      }
      try {
        const snapshot = await kernelApi.entitlements();
        this.applyEntitlements(snapshot);
        return snapshot;
      } catch {
        if (this.isDemoMode && !this.entitlements) {
          this.entitlements = {
            tenant_ref: "tenant:00000000-0000-0000-0000-000000000000",
            tenant_id: "00000000-0000-0000-0000-000000000000",
            subscription_ref: null,
            tier: "Complete",
            status: "Active",
            started_at: null,
            trial_ends_at: null,
            is_read_only: false,
            product_families: [
              "Blueprint",
              "Capture",
              "Orbit",
              "Engage",
              "Allocate",
              "Vigil",
              "Lens",
              "Assure",
            ],
            baseline_capabilities: [
              "audit_trail",
              "electronic_signatures",
              "authorization",
              "record_export",
            ],
            environment_notice: PRE_RELEASE_ENVIRONMENT_NOTICE,
          };
          this.entitlementsLoaded = true;
        }
        return this.entitlements;
      }
    },
    /** Applies an updated entitlement snapshot and clears any stale expired-trial alert when active. */
    applyEntitlements(snapshot) {
      this.entitlements = snapshot;
      this.entitlementsLoaded = Boolean(snapshot);
      if (snapshot && !snapshot.is_read_only && snapshot.status !== "Expired") {
        this.subscriptionExpiredNotice = null;
      }
    },
    /**
     * Marks the workspace subscription as expired (`Read & Export` mode) when a
     * mutation fails with `403 K.Subscription.Expired` before entitlements reload.
     */
    markSubscriptionExpired(message = null) {
      const existing = this.entitlements || {
        tenant_ref: "",
        tenant_id: this.user?.tenantId || "",
        subscription_ref: null,
        tier: "Individual",
        status: "Expired",
        started_at: null,
        trial_ends_at: null,
        is_read_only: true,
        product_families: ["Blueprint", "Capture"],
        baseline_capabilities: [
          "audit_trail",
          "electronic_signatures",
          "authorization",
          "record_export",
        ],
        environment_notice: PRE_RELEASE_ENVIRONMENT_NOTICE,
      };
      this.entitlements = {
        ...existing,
        status: "Expired",
        is_read_only: true,
      };
      this.entitlementsLoaded = true;
      this.subscriptionExpiredNotice =
        message ||
        "Workspace trial has expired (Read & Export mode). Complete study scoping on Plans to activate write access.";
    },
    /**
     * Clears the local session, every account's browser drafts and the
     * in-memory clinical state, then ends the Keycloak SSO session.
     */
    async logout(options = {}) {
      const keycloak = getKeycloakClient();
      webSessionCoordinator.beginVoluntaryLogout();
      clearWebReturnUrl();
      this.authError = null;
      this.clearAuth();
      this.isLoggingOut = true;
      purgeLegacyAuthStorage();
      purgeUserScopedStorage(null);
      try {
        useClinicalStore().$reset();
      } catch {
        // Pinia or store not ready
      }
      if (keycloak) {
        await keycloak.logout(options);
      }
    },
    /**
     * Refreshes the access token when it expires within `minValidity` seconds.
     * Deduplicated via `webSessionCoordinator`. A failed refresh ends the
     * session while preserving local drafts so the user can sign in again.
     */
    async refresh(minValidity = 30) {
      const keycloak = getKeycloakClient();
      if (
        !keycloak ||
        typeof keycloak.updateToken !== "function" ||
        !this.isAuthenticated
      ) {
        return false;
      }
      const result = await webSessionCoordinator.ensureFreshToken(minValidity);
      if (result.error) {
        this.expireSession();
        throw result.error;
      }
      if (!result.ok) {
        this.expireSession();
        return false;
      }
      this.setAuth(keycloak);
      return Boolean(result.refreshed);
    },
    /**
     * Performs a single-flight forced token refresh after an HTTP 401 response.
     * Preserves local drafts and notifies session listeners if refresh fails.
     */
    async refreshAfterUnauthorized() {
      const keycloak = getKeycloakClient();
      if (!keycloak) {
        if (this.isAuthenticated) {
          const returnUrl = saveWebReturnUrl();
          this.expireSession(returnUrl);
          notifySessionEnded({ reason: "unauthorized", returnUrl });
        }
        return null;
      }
      const result = await webSessionCoordinator.refreshOnUnauthorized();
      if (result.ok && result.token) {
        this.setAuth(keycloak);
        return this.accessToken;
      }
      if (this.isAuthenticated) {
        this.expireSession();
      }
      return null;
    },
    /**
     * Returns an access token valid for at least `minValidity` seconds, or
     * null when no one is signed in or the session could not be refreshed.
     */
    async ensureFreshToken(minValidity = 30) {
      if (!this.isAuthenticated) {
        return null;
      }
      try {
        await this.refresh(minValidity);
      } catch {
        return null;
      }
      return this.accessToken;
    },
    /**
     * Switches the workspace persona. Only personas the token's roles entitle
     * the user to are accepted; roles are never changed.
     */
    switchPersona(personaKey) {
      const entitled = personasForRoles(this.rawRoles);
      if (!entitled.some((p) => p.key === personaKey)) {
        return false;
      }
      this.currentPersona = personaKey;
      rehydrateStoresForPersona(personaKey);
      try {
        useNotificationsStore().fetchNotifications();
      } catch {
        // Pinia store not ready or active
      }
      return true;
    },
  },
});

registerAuthSessionBridge({
  onTokenRefreshed: (_token, kc) => {
    try {
      if (kc?.authenticated && kc?.token) {
        useAuthStore().setAuth(kc);
      }
    } catch {
      // Pinia store not active yet
    }
  },
  onSessionExpired: (ctx) => {
    try {
      useAuthStore().expireSession(ctx?.returnUrl);
    } catch {
      // Pinia store not active yet
    }
  },
});
