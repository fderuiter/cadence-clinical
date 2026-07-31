import { createRouter, createWebHistory } from "vue-router";
import { useAuthStore, ROLE_ALIASES } from "./stores/auth";

/**
 * Seeding Gap Documentation Resolved:
 * The "Sponsor Designer" role has been successfully seeded in the Keycloak realm configuration
 * (`cadence-realm.json`) and is centrally mapped in the OIDC normalizer.
 */

// Helper to check if user has required roles, mapping UI roles to Keycloak roles
export function hasRequiredRole(userRoles, requiredRoles) {
  return userRoles.some((uRole) => {
    if (requiredRoles.includes(uRole)) return true;
    const aliases = ROLE_ALIASES[uRole] || [];
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
      // - Designer (sponsor_designer) → /mdr
      // - CRC (site_investigator / crc) → /ecrf
      // - CRA (cra / monitor) → /ctms
      // - Data Manager (data_manager) → /rules
      // - TMF Auditor (auditor / tmf_auditor) → /audit
      if (roles.includes("sponsor_designer")) {
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
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
  {
    path: "/icf-builder",
    name: "icf-builder",
    component: () => import("./views/ICFBuilderView.vue"),
    meta: {
      requiresAuth: false,
    },
  },
  {
    path: "/econsent-authoring",
    name: "econsent-authoring",
    component: () => import("./views/ConsentAuthoringView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
  {
    path: "/ecrf",
    name: "ecrf",
    component: () => import("./views/EcrfView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
      ],
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
    path: "/auditor",
    name: "auditor",
    component: () => import("./views/AuditorView.vue"),
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
  {
    path: "/etmf",
    name: "etmf",
    component: () => import("./views/DocumentManagerView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["cra", "monitor", "auditor", "tmf_auditor", "sponsor_admin"],
    },
  },
  {
    path: "/notifications",
    name: "notifications",
    component: () => import("./views/NotificationsView.vue"),
    meta: {
      requiresAuth: true,
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
            redirectUri:
              window.location.origin +
              (import.meta.env.BASE_URL || "/cadence-clinical/") +
              to.fullPath.replace(/^\//, ""),
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
