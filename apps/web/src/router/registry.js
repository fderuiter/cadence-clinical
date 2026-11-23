import { hasRequiredRole } from "./helpers.js";

const routeModules = import.meta.glob("./routes/*.route.js", { eager: true });

export const webRouteDescriptors = Object.freeze(
  Object.values(routeModules)
    .map((mod) => mod.default)
    .filter((descriptor) => Boolean(descriptor?.route?.name))
    .sort((a, b) => {
      const orderDiff = (a.order ?? 9999) - (b.order ?? 9999);
      if (orderDiff !== 0) return orderDiff;
      return String(a.route.name).localeCompare(String(b.route.name));
    })
);

const descriptorByName = new Map(
  webRouteDescriptors.map((descriptor) => [descriptor.route.name, descriptor])
);

export function getRouteDescriptor(routeName) {
  if (!routeName) return null;
  return descriptorByName.get(String(routeName)) || null;
}

export function findRouteDescriptorBySpec(routeSpec) {
  if (!routeSpec) return null;
  const spec = String(routeSpec).trim();
  if (!spec) return null;
  const direct = descriptorByName.get(spec);
  if (direct) return direct;
  return (
    webRouteDescriptors.find(
      (descriptor) =>
        descriptor.onboardingTaskPath === spec ||
        descriptor.route.path === spec ||
        (descriptor.onboardingTaskPath &&
          spec.startsWith(`${descriptor.onboardingTaskPath}/`))
    ) || null
  );
}

export function getRouteRequiredRoles(routeOrTarget) {
  if (!routeOrTarget) return [];
  if (typeof routeOrTarget === "string") {
    return getRouteDescriptor(routeOrTarget)?.route?.meta?.requiresRole || [];
  }
  const matched = routeOrTarget?.matched;
  const roles = matched?.length
    ? matched.flatMap((r) => r.meta?.requiresRole || [])
    : [];
  if (roles.length) return roles;
  if (routeOrTarget?.meta?.requiresRole?.length) {
    return routeOrTarget.meta.requiresRole;
  }
  if (routeOrTarget?.name) {
    return (
      getRouteDescriptor(routeOrTarget.name)?.route?.meta?.requiresRole || []
    );
  }
  return [];
}

export function isRouteAuthorizedForRoles(routeOrTarget, userRoles) {
  const req = getRouteRequiredRoles(routeOrTarget);
  return !req.length || hasRequiredRole(userRoles || [], req);
}

export function getRouteHubRoles(routeName) {
  const descriptor = getRouteDescriptor(routeName);
  if (!descriptor) return [];
  return descriptor.hubRoles || descriptor.route?.meta?.requiresRole || [];
}

export function isRouteRoleAuthorized(routeName, userRoles) {
  const required = getRouteRequiredRoles(routeName);
  return hasRequiredRole(userRoles, required);
}

export function isRouteHubAuthorized(routeName, userRoles) {
  const required = getRouteHubRoles(routeName);
  return hasRequiredRole(userRoles, required);
}

export function isRouteTourRoleAuthorized(routeName, userRoles) {
  const descriptor = getRouteDescriptor(routeName);
  if (!descriptor?.tourEnabled) return false;
  const required =
    descriptor.tourRoles || descriptor.route?.meta?.requiresRole || [];
  return hasRequiredRole(userRoles, required);
}

const landingDescriptors = webRouteDescriptors
  .filter((descriptor) => Boolean(descriptor.landingRoles))
  .sort((a, b) => {
    const prioDiff =
      (a.landingRoles?.priority ?? a.order ?? 9999) -
      (b.landingRoles?.priority ?? b.order ?? 9999);
    if (prioDiff !== 0) return prioDiff;
    return String(a.route.name).localeCompare(String(b.route.name));
  });

const defaultLandingRouteName =
  landingDescriptors.find((d) => d.landingRoles?.defaultFallback)?.route.name ||
  "mdr";

export function resolvePreferredLandingRouteName(roles = [], demo = false) {
  for (const descriptor of landingDescriptors) {
    const modeRoles = demo
      ? [
          ...(descriptor.landingRoles?.all || []),
          ...(descriptor.landingRoles?.demo || []),
        ]
      : [
          ...(descriptor.landingRoles?.all || []),
          ...(descriptor.landingRoles?.live || []),
        ];
    if (modeRoles.some((role) => roles.includes(role))) {
      return descriptor.route.name;
    }
  }
  return defaultLandingRouteName;
}

export function getPrimaryFallbackRouteNames(demo = false) {
  const modeKey = demo ? "demo" : "live";
  return new Set(
    webRouteDescriptors
      .filter(
        (descriptor) =>
          descriptor.primaryFallback === "all" ||
          descriptor.primaryFallback === modeKey
      )
      .map((descriptor) => descriptor.route.name)
  );
}

export const NON_ONBOARDING_ROUTE_NAMES = new Set([
  "landing",
  ...webRouteDescriptors
    .filter((descriptor) => descriptor.nonOnboarding)
    .map((descriptor) => descriptor.route.name),
]);

export const NON_WORKFLOW_ROUTE_NAMES = new Set([
  "landing",
  "home",
  ...webRouteDescriptors
    .filter((descriptor) => descriptor.nonWorkflow)
    .map((descriptor) => descriptor.route.name),
]);

export const NON_WORKFLOW_PATHS = new Set([
  "/",
  ...webRouteDescriptors
    .filter((descriptor) => descriptor.nonWorkflow && descriptor.route?.path)
    .map((descriptor) => descriptor.route.path),
]);

export const TOUR_HIDDEN_ROUTE_NAMES = new Set(
  webRouteDescriptors
    .filter((descriptor) => descriptor.hideTourOverlay)
    .map((descriptor) => descriptor.route.name)
);

export const TOUR_HIDDEN_PATHS = new Set(
  webRouteDescriptors
    .filter(
      (descriptor) => descriptor.hideTourOverlay && descriptor.route?.path
    )
    .map((descriptor) => descriptor.route.path)
);

export const WORKFLOW_CONTEXT_LABELS = Object.freeze(
  Object.fromEntries(
    webRouteDescriptors.flatMap((descriptor) => {
      if (!descriptor.contextLabel) return [];
      const entries = [[descriptor.route.name, descriptor.contextLabel]];
      const cleanPath = String(descriptor.route.path || "").replace(/^\//, "");
      if (
        cleanPath &&
        !cleanPath.includes(":") &&
        cleanPath !== descriptor.route.name
      ) {
        entries.push([cleanPath, descriptor.contextLabel]);
      }
      return entries;
    })
  )
);

export const SITE_READINESS_DESCRIPTORS = Object.freeze(
  webRouteDescriptors.filter((descriptor) =>
    Boolean(descriptor.siteReadinessTier)
  )
);

export const DATA_COLLECTION_HUB_DESCRIPTOR =
  webRouteDescriptors.find((descriptor) => descriptor.dataCollectionHub) ||
  null;

export const PALETTE_DESCRIPTORS = Object.freeze(
  webRouteDescriptors
    .filter((descriptor) => Boolean(descriptor.palette))
    .sort((a, b) => {
      const orderDiff =
        (a.palette.order ?? a.order ?? 9999) -
        (b.palette.order ?? b.order ?? 9999);
      if (orderDiff !== 0) return orderDiff;
      return String(a.route.name).localeCompare(String(b.route.name));
    })
);

const sopEscalationRules = Object.freeze(
  webRouteDescriptors
    .flatMap((descriptor) => {
      if (!descriptor.sopEscalation?.prefixes?.length) return [];
      return descriptor.sopEscalation.prefixes.map((prefix) => ({
        prefix,
        category: descriptor.sopEscalation.category,
      }));
    })
    .sort((a, b) => b.prefix.length - a.prefix.length)
);

export function resolveSopEscalationCategory(
  routePath,
  fallbackCategory = "SYSTEM_SUPPORT"
) {
  const path = String(routePath || "/");
  const matched = sopEscalationRules.find((rule) =>
    path.startsWith(rule.prefix)
  );
  return matched ? matched.category : fallbackCategory;
}
