import {
  createRouter,
  createWebHashHistory,
  createWebHistory,
} from "vue-router";
import { TOKEN_MIN_VALIDITY_SECONDS } from "../api/authorization.js";
import { useAuthStore } from "../stores/auth";
import { useOnboardingStore } from "../stores/onboarding.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { AVAILABLE_SCREENS, isScreenAvailable } from "./availability";
import { hasRequiredRole } from "./helpers.js";
import {
  getPrimaryFallbackRouteNames,
  getRouteRequiredRoles,
  isRouteAuthorizedForRoles,
  resolvePreferredLandingRouteName,
  webRouteDescriptors,
} from "./registry.js";

export { getRouteRequiredRoles, hasRequiredRole, isRouteAuthorizedForRoles };

const routes = [
  {
    path: "/",
    name: "landing",
    redirect: () => {
      const authStore = useAuthStore();
      if (!authStore.isAuthenticated) {
        return "/login";
      }
      if (authStore.needsTrialOnboarding) {
        return { name: "trial-onboarding" };
      }
      const onboardingStore = useOnboardingStore();
      if (!onboardingStore.bypassOverview) {
        return { name: "overview" };
      }
      const roles = authStore.normalizedRoles || [];
      const demo = isDemoBuild();
      const preferred = resolvePreferredLandingRouteName(roles, demo);
      if (isScreenAvailable(preferred)) {
        return { name: preferred };
      }
      // The preferred screen has no .NET backend yet: land on the first
      // role-authorized primary workflow screen that does, or open the study
      // workspace so every authenticated persona lands on a live screen.
      const fallbackScreens = getPrimaryFallbackRouteNames(demo);
      const fallback = routes.find(
        (route) =>
          fallbackScreens.has(route.name) &&
          AVAILABLE_SCREENS.has(route.name) &&
          (!route.meta?.requiresRole?.length ||
            hasRequiredRole(roles, route.meta.requiresRole))
      );
      if (fallback) {
        return { name: fallback.name };
      }
      return isScreenAvailable("study")
        ? { name: "study" }
        : { name: "unavailable" };
    },
  },
  ...webRouteDescriptors.map((descriptor) => descriptor.route),
];

export function createAppHistory(
  env = import.meta.env,
  base = env?.BASE_URL || "/cadence-clinical/"
) {
  if (isDemoBuild(env)) {
    if (
      typeof window !== "undefined" &&
      window.location &&
      !window.location.hash
    ) {
      const normalizedBase = (base || "/").replace(/\/$/, "");
      const { pathname, search } = window.location;
      if (normalizedBase && pathname.startsWith(`${normalizedBase}/`)) {
        const subpath = pathname.slice(normalizedBase.length);
        if (subpath && subpath !== "/" && subpath !== "/index.html") {
          window.history.replaceState(
            window.history.state,
            "",
            `${normalizedBase}/#${subpath}${search}`
          );
        }
      }
    }
    return createWebHashHistory(base);
  }
  return createWebHistory(base);
}

export const router = createRouter({
  // Use hash history for static subpath demo builds and web history for Keycloak OIDC deployments
  history: createAppHistory(),
  routes,
});

// OIDC & RBAC navigation guard. Sessions come only from Keycloak: an
// unauthenticated user, or one whose token can no longer be refreshed, is sent
// to the sign-in page.
router.beforeEach(async (to) => {
  const authStore = useAuthStore();

  const requiresAuth = to.matched.some((record) => record.meta.requiresAuth);

  if (requiresAuth) {
    if (authStore.isAuthenticated) {
      try {
        await authStore.refresh(TOKEN_MIN_VALIDITY_SECONDS);
      } catch (err) {
        console.error("Keycloak session could not be refreshed:", err);
      }
    }

    if (!authStore.isAuthenticated) {
      return { path: "/login", query: { redirect: to.fullPath } };
    }

    if (authStore.needsTrialOnboarding && to.name !== "trial-onboarding") {
      return { name: "trial-onboarding" };
    }

    // Role-based authorization guard
    const requiredRoles = getRouteRequiredRoles(to);
    if (requiredRoles.length > 0) {
      const hasRole = hasRequiredRole(authStore.normalizedRoles, requiredRoles);
      if (!hasRole) {
        console.warn(
          `User does not have required roles: ${requiredRoles.join(", ")}`
        );
        return { path: "/forbidden" };
      }
    }
  }

  if (to.name && !isScreenAvailable(to.name)) {
    return { name: "unavailable", query: { screen: String(to.name) } };
  }
});
