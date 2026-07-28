import { createRouter, createWebHistory } from "vue-router";
import { useAuthStore } from "./stores/auth";

/**
 * Seeding Gap Documentation:
 * In the Keycloak realm configuration (`cadence-realm.json`), there is a role-seeding gap:
 * The "Study Designer" (or `sponsor_designer` / `designer` / `study_designer`) role required by the backend/designer
 * microservice is missing from the realm roles definition.
 *
 * Future deployments should add the `sponsor_designer` (or "Study Designer") role to Keycloak
 * and map it accordingly in the OIDC normalizer.
 */

// Helper to check if user has required roles, mapping UI roles to Keycloak roles
export function hasRequiredRole(userRoles, requiredRoles) {
  // Map UI roles to Keycloak roles:
  // - CRC ↔ Site Investigator (site_investigator)
  // - CRA ↔ CRA (cra)
  // - Data Manager ↔ Data Manager (data_manager)
  // - TMF Auditor ↔ Auditor (auditor)
  // - Study Designer ↔ Sponsor Designer / Study Designer (sponsor_designer, designer, study_designer)
  const roleAliases = {
    crc: ["site_investigator", "crc"],
    site_investigator: ["site_investigator", "crc"],
    cra: ["cra", "monitor"],
    monitor: ["cra", "monitor"],
    auditor: ["auditor", "tmf_auditor"],
    tmf_auditor: ["auditor", "tmf_auditor"],
    designer: ["sponsor_designer", "designer", "study_designer"],
    sponsor_designer: ["sponsor_designer", "designer", "study_designer"],
    study_designer: ["sponsor_designer", "designer", "study_designer"],
  };

  return userRoles.some((uRole) => {
    if (requiredRoles.includes(uRole)) return true;
    const aliases = roleAliases[uRole] || [];
    return aliases.some((alias) => requiredRoles.includes(alias));
  });
}

const routes = [
  {
    path: "/",
    name: "landing",
    redirect: () => {
      const authStore = useAuthStore();
      if (!authStore.isAuthenticated) {
        return "/login";
      }
      const roles = authStore.normalizedRoles || [];
      // Dynamic Landing Route Redirection based on roles:
      // - Designer (sponsor_designer / designer / study_designer) → /mdr
      // - CRC (site_investigator / crc) → /ecrf
      // - CRA (cra / monitor) → /ctms
      // - Data Manager (data_manager) → /rules
      // - TMF Auditor (auditor / tmf_auditor) → /audit
      if (roles.includes("sponsor_designer") || roles.includes("designer") || roles.includes("study_designer")) {
        return "/mdr";
      }
      if (roles.includes("site_investigator") || roles.includes("crc")) {
        return "/ecrf";
      }
      if (roles.includes("cra") || roles.includes("monitor")) {
        return "/ctms";
      }
      if (roles.includes("data_manager")) {
        return "/rules";
      }
      if (roles.includes("auditor") || roles.includes("tmf_auditor")) {
        return "/audit";
      }
      if (roles.includes("sponsor_admin")) {
        return "/mdr";
      }
      return "/mdr"; // Default fallback
    },
  },
  {
    path: "/login",
    name: "login",
    component: () => import("./views/LoginView.vue"),
    meta: { requiresAuth: false },
  },
  {
    path: "/forbidden",
    name: "forbidden",
    component: () => import("./views/ForbiddenView.vue"),
    meta: { requiresAuth: false },
  },
  {
    path: "/mdr",
    name: "mdr",
    component: () => import("./views/MdrView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "designer", "study_designer", "data_manager", "sponsor_admin"],
    },
  },
  {
    path: "/ecrf",
    name: "ecrf",
    component: () => import("./views/EcrfView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["site_investigator", "crc", "data_manager", "sponsor_admin"],
    },
  },
  {
    path: "/ctms",
    name: "ctms",
    component: () => import("./views/CtmsView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["cra", "monitor", "sponsor_admin"],
    },
  },
  {
    path: "/audit",
    name: "audit",
    component: () => import("./views/AuditView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["auditor", "tmf_auditor", "sponsor_admin"],
    },
  },
  {
    path: "/rules",
    name: "rules",
    component: () => import("./views/RulesView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["data_manager", "sponsor_admin"],
    },
  },
];

export const router = createRouter({
  // Use a GitHub Pages-compatible history mode resolving from the configured Pages base path
  history: createWebHistory(import.meta.env.BASE_URL || "/cadence-clinical/"),
  routes,
});

// OIDC & RBAC Navigation guard (modern Vue Router v4 return-based guard)
router.beforeEach(async (to) => {
  const authStore = useAuthStore();

  const requiresAuth = to.matched.some((record) => record.meta.requiresAuth);

  if (requiresAuth) {
    if (!authStore.isAuthenticated) {
      // If we are not in demo mode, trigger Keycloak redirect or fallback to /login
      if (!authStore.isDemoMode) {
        try {
          await authStore.login({
            redirectUri: window.location.origin + (import.meta.env.BASE_URL || "/cadence-clinical/") + to.fullPath.replace(/^\//, ""),
          });
          return false; // Abort navigation as page is redirecting
        } catch (err) {
          console.error("Authentication redirection failed:", err);
          return { path: "/login", query: { redirect: to.fullPath } };
        }
      } else {
        return { path: "/login", query: { redirect: to.fullPath } };
      }
    }

    // Role-based authorization guard
    const requiredRoles = to.meta.requiresRole;
    if (requiredRoles && requiredRoles.length > 0) {
      const hasRole = hasRequiredRole(authStore.normalizedRoles, requiredRoles);
      if (!hasRole) {
        console.warn(
          `User does not have required roles: ${requiredRoles.join(", ")}`
        );
        return { path: "/forbidden" };
      }
    }
  }
});
