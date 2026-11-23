// Discovers per-screen portal route descriptors via Vite glob import in
// deterministic sort order so the shell and tab bar keep canonical DOM/ARIA IDs.
const routeModules = import.meta.glob("./routes/*.route.js", { eager: true });

export const portalRouteDescriptors = Object.values(routeModules)
  .map((mod) => mod.default ?? mod)
  .filter((descriptor) => descriptor && descriptor.route)
  .sort((left, right) => {
    const orderDiff = (left.order ?? 0) - (right.order ?? 0);
    if (orderDiff !== 0) return orderDiff;
    const leftName = String(left.route.name ?? "");
    const rightName = String(right.route.name ?? "");
    return leftName === rightName ? 0 : leftName < rightName ? -1 : 1;
  });

export const viewRoutes = portalRouteDescriptors.map(
  (descriptor) => descriptor.route
);

// The tab bar entries, with the view each one controls and canonical DOM IDs.
export const tabs = viewRoutes
  .filter((route) => route.meta?.tab)
  .map((route) => ({
    ...route.meta.tab,
    viewId: route.meta.viewId,
    itemId: `tab-btn-${route.meta.tab.id}`,
    buttonId: `btn-tab-btn-${route.meta.tab.id}`,
  }));

const activeTabOverrideByViewId = new Map(
  portalRouteDescriptors
    .filter(
      (descriptor) => descriptor.activeTabId && descriptor.route.meta?.viewId
    )
    .map((descriptor) => [descriptor.route.meta.viewId, descriptor.activeTabId])
);

export function activeTabForView(viewId) {
  const overrideTabId = activeTabOverrideByViewId.get(viewId);
  if (overrideTabId) {
    return tabs.find((tab) => tab.id === overrideTabId) || null;
  }
  return tabs.find((tab) => tab.viewId === viewId) || null;
}

export function routeForView(viewId) {
  return viewRoutes.find((route) => route.meta?.viewId === viewId);
}
