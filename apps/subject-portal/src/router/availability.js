import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import { portalRouteDescriptors } from "./routes.js";

/**
 * Screens available to any session outside explicit demo builds (docs/web/API_MIGRATION.md),
 * populated dynamically from per-screen `routes/*.route.js` descriptors.
 */
export const AVAILABLE_SCREENS = new Set(
  portalRouteDescriptors
    .filter((descriptor) => descriptor.availability === "available")
    .map((descriptor) => descriptor.route.name)
);

export const DEMO_AVAILABLE_SCREENS = new Set(
  portalRouteDescriptors
    .filter((descriptor) => descriptor.availability === "demo")
    .map((descriptor) => descriptor.route.name)
);

export const AUTHENTICATED_SCREENS = new Set(
  portalRouteDescriptors
    .filter((descriptor) => descriptor.availability === "authenticated")
    .map((descriptor) => descriptor.route.name)
);

const ALWAYS_AVAILABLE = new Set(
  [
    "unavailable",
    ...portalRouteDescriptors
      .filter((descriptor) => descriptor.availability === "always")
      .map((descriptor) => descriptor.route.name),
  ].filter(Boolean)
);

export const UNAVAILABLE_SCREENS_ISSUE =
  "https://github.com/fderuiter/cadence-clinical/issues/4829";

const SCREEN_TITLES = Object.freeze(
  Object.fromEntries(
    portalRouteDescriptors
      .filter((descriptor) => descriptor.route?.name && descriptor.title)
      .map((descriptor) => [descriptor.route.name, descriptor.title])
  )
);

export function screenTitle(routeName) {
  return SCREEN_TITLES[routeName] || "This workflow";
}

export function isScreenAvailable(routeName, env = import.meta.env) {
  if (!routeName) return false;
  if (ALWAYS_AVAILABLE.has(routeName)) return true;
  if (isDemoModeActive(env)) {
    return (
      DEMO_AVAILABLE_SCREENS.has(routeName) ||
      AUTHENTICATED_SCREENS.has(routeName) ||
      AVAILABLE_SCREENS.has(routeName)
    );
  }
  if (AUTHENTICATED_SCREENS.has(routeName)) {
    return Boolean(
      state.session?.token &&
      state.session?.userId &&
      !state.session?.isOfflineMode
    );
  }
  return AVAILABLE_SCREENS.has(routeName);
}

export function defaultPortalRouteName(env = import.meta.env) {
  if (isScreenAvailable("tasks", env)) return "tasks";
  if (isScreenAvailable("wallet", env)) return "wallet";
  return "unavailable";
}

export function defaultPortalViewId(env = import.meta.env) {
  const routeName = defaultPortalRouteName(env);
  if (routeName === "tasks") return "view-tasks";
  if (routeName === "wallet") return "view-wallet";
  return "view-unavailable";
}
