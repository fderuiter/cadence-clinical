/**
 * Metadata for workspace routes, mapping each unmigrated screen to its product family
 * in docs/strategy/PRODUCT_CATALOG.md and its pre-release delivery status.
 *
 * Extracted into a dedicated leaf module (and statically materialized by
 * `createWebRouteCatalogPlugin` during production builds) so UnavailableView loads catalog
 * copy on demand without bloating initial cold-boot route availability checks.
 */

import { webRouteDescriptors } from "./registry";

export const SCREEN_CATALOG = /* @__PURE__ */ Object.freeze(
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
