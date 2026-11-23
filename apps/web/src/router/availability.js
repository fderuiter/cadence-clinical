/**
 * Screen availability and commercial product-family mapping for workspace routes.
 *
 * Route descriptors in ./routes/*.route.js declare availability ("always", "available",
 * or "demo") and optional catalog metadata. Routes whose .NET Clinical Kernel endpoints
 * are live populate AVAILABLE_SCREENS; pre-release screens resolve through SCREEN_CATALOG.
 */

import { isDemoBuild } from "../utils/demoMode";
import { webRouteDescriptors } from "./registry";

/** Route names whose screens call endpoints the .NET services serve. */
export const AVAILABLE_SCREENS = new Set(
  webRouteDescriptors
    .filter((descriptor) => descriptor.availability === "available")
    .map((descriptor) => descriptor.route.name)
);

/** Routes unlocked in interactive static demo builds backed by stateful MSW handlers. */
export const DEMO_AVAILABLE_SCREENS = new Set(
  webRouteDescriptors
    .filter(
      (descriptor) =>
        descriptor.availability === "available" ||
        descriptor.availability === "demo"
    )
    .map((descriptor) => descriptor.route.name)
);

/** Routes that do not depend on study-scoped module APIs (public plans, onboarding, sign-in, error pages). */
const ALWAYS_AVAILABLE = new Set([
  "landing",
  ...webRouteDescriptors
    .filter((descriptor) => descriptor.availability === "always")
    .map((descriptor) => descriptor.route.name),
]);

/**
 * Metadata for workspace routes, mapping each unmigrated screen to its product family
 * in docs/strategy/PRODUCT_CATALOG.md and its pre-release delivery status.
 */
export const SCREEN_CATALOG = Object.freeze(
  Object.fromEntries(
    webRouteDescriptors
      .filter((descriptor) => Boolean(descriptor.catalog))
      .map((descriptor) => [
        descriptor.route.name,
        Object.freeze({ ...descriptor.catalog }),
      ])
  )
);

const DEFAULT_SCREEN_METADATA = Object.freeze({
  title: "This screen",
  family: "Blueprint",
  minimumTier: "Individual",
  isBaselineCapability: false,
  roadmapStage: "Pre-release module roadmap",
  summary:
    "This workspace module is progressing through pre-release Clinical Kernel integration.",
});

export function screenMetadata(routeName) {
  if (!routeName) {
    return DEFAULT_SCREEN_METADATA;
  }
  return SCREEN_CATALOG[routeName] || DEFAULT_SCREEN_METADATA;
}

export function screenTitle(routeName) {
  return SCREEN_CATALOG[routeName]?.title || "This screen";
}

export function isScreenAvailable(routeName, env) {
  if (!routeName) return false;
  if (ALWAYS_AVAILABLE.has(routeName) || AVAILABLE_SCREENS.has(routeName)) {
    return true;
  }
  const demoActive =
    env !== undefined
      ? isDemoBuild(env)
      : import.meta.env?.MODE === "demo" ||
        import.meta.env?.VITE_DEMO_MODE === "true";
  if (demoActive) {
    return DEMO_AVAILABLE_SCREENS.has(routeName);
  }
  return false;
}
