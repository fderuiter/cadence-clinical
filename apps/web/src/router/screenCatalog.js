import { webRouteDescriptors } from "./registry";

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
