import { createRouter, createWebHistory } from "vue-router";
import { useAuthStore } from "./stores/auth";

const routes = [
  {
    path: "/",
    redirect: "/mdr",
  },
  {
    path: "/mdr",
    name: "mdr",
    component: () => import("./views/MdrView.vue"),
    meta: { requiresAuth: true },
  },
  {
    path: "/ecrf",
    name: "ecrf",
    component: () => import("./views/EcrfView.vue"),
    meta: { requiresAuth: true },
  },
  {
    path: "/ctms",
    name: "ctms",
    component: () => import("./views/CtmsView.vue"),
    meta: { requiresAuth: true, requiresRole: ["monitor", "sponsor_admin"] },
  },
  {
    path: "/audit",
    name: "audit",
    component: () => import("./views/AuditView.vue"),
    meta: { requiresAuth: true },
  },
];

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL || "/cadence-clinical/"),
  routes,
});

// OIDC Navigation guard as per ADR-052
router.beforeEach(async (to, from, next) => {
  const authStore = useAuthStore();

  // If Keycloak is enabled and active (not in local demo fallback), enforce auth
  if (
    !authStore.isDemoMode &&
    to.matched.some((record) => record.meta.requiresAuth)
  ) {
    if (!authStore.isAuthenticated) {
      try {
        await authStore.login({
          redirectUri: window.location.origin + to.fullPath,
        });
        return; // Redirecting, so do not call next()
      } catch (err) {
        console.error("Authentication redirection failed:", err);
        next(false);
        return;
      }
    }

    // Role-based authorization guard
    const requiredRoles = to.meta.requiresRole;
    if (requiredRoles && requiredRoles.length > 0) {
      const hasRole = authStore.normalizedRoles.some((role) =>
        requiredRoles.includes(role)
      );
      if (!hasRole) {
        console.warn(
          `User does not have required roles: ${requiredRoles.join(", ")}`
        );
        next(false);
        return;
      }
    }
  }

  next();
});
