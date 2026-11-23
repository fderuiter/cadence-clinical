// The portal router. Every view stays mounted (the imperative feature modules
// render into their elements), so the router does not swap components: it
// keeps the URL and `state.currentView` in step in both directions.
import { watch } from "vue";
import {
  START_LOCATION,
  createMemoryHistory,
  createRouter,
  createWebHashHistory,
  createWebHistory,
} from "vue-router";

import { showView } from "../features/navigation.js";
import { state } from "../stores/state.js";
import { isDemoBuild } from "../utils/demoMode.js";
import UnavailableView from "../views/UnavailableView.vue";
import { isScreenAvailable } from "./availability.js";
import { routeForView, viewRoutes } from "./routes.js";

// Vite serves and builds the portal under this base (vite.config.js).
export const ROUTER_BASE = import.meta.env?.BASE_URL || "/subject-portal/";

// Memory history under Vitest or without a `window.location` (there is no
// URL to follow there); hash history in static demo builds; browser history in production/dev.
const memoryHistory =
  import.meta.env?.MODE === "test" ||
  typeof window === "undefined" ||
  !window.location;

export function createPortalHistory(
  env = import.meta.env,
  { memory = memoryHistory, base = env?.BASE_URL || ROUTER_BASE } = {}
) {
  if (memory) {
    return createMemoryHistory(base);
  }
  return isDemoBuild(env) ? createWebHashHistory(base) : createWebHistory(base);
}

const unavailableRoute = {
  path: "/unavailable",
  name: "unavailable",
  component: UnavailableView,
  meta: { viewId: "view-unavailable" },
};

function findRouteForView(viewId) {
  if (viewId === "view-unavailable") return unavailableRoute;
  return routeForView(viewId);
}

export function resolveDefaultPortalRoute() {
  if (isScreenAvailable("tasks")) {
    return { name: "tasks" };
  }
  if (isScreenAvailable("wallet")) {
    return { name: "wallet" };
  }
  return { name: "unavailable" };
}

let router = null;
let portalMounted = false;

export function isPortalRouterMounted() {
  return portalMounted;
}

export function setPortalRouterMounted(mounted) {
  portalMounted = Boolean(mounted);
  if (
    portalMounted &&
    router &&
    memoryHistory &&
    router.currentRoute.value !== START_LOCATION
  ) {
    const targetRoute = findRouteForView(state.currentView);
    if (targetRoute && isScreenAvailable(targetRoute.name)) {
      if (router.currentRoute.value.name !== targetRoute.name) {
        state.unavailableScreen = null;
        router.replace({ name: targetRoute.name }).catch(console.warn);
      }
    } else if (!targetRoute || targetRoute.name === "tasks") {
      const fallback = resolveDefaultPortalRoute();
      state.unavailableScreen = null;
      router.replace(fallback).catch(console.warn);
    } else {
      state.unavailableScreen = String(targetRoute.name);
      router
        .replace({
          name: "unavailable",
          query: { screen: String(targetRoute.name) },
        })
        .catch(console.warn);
    }
  }
}

// The router singleton, created on first mount and shared by later mounts.
export function getPortalRouter() {
  if (router) return router;

  router = createRouter({
    history: createPortalHistory(),
    routes: [
      ...viewRoutes,
      unavailableRoute,
      { path: "/:pathMatch(.*)*", redirect: () => resolveDefaultPortalRoute() },
    ],
  });

  router.beforeEach((to, from) => {
    if (from === START_LOCATION && memoryHistory && to.path === "/") {
      const initialRoute = findRouteForView(state.currentView);
      if (initialRoute && isScreenAvailable(initialRoute.name)) {
        return to.name === initialRoute.name
          ? undefined
          : { name: initialRoute.name };
      }
      if (!initialRoute || initialRoute.name === "tasks") {
        state.unavailableScreen = null;
        const fallback = resolveDefaultPortalRoute();
        return to.name === fallback.name ? undefined : fallback;
      }
      state.unavailableScreen = String(initialRoute.name);
      return {
        name: "unavailable",
        query: { screen: String(initialRoute.name) },
      };
    }

    if (to.path === "/" && to.name === "tasks" && !isScreenAvailable("tasks")) {
      state.unavailableScreen = null;
      return resolveDefaultPortalRoute();
    }

    if (to.name && !isScreenAvailable(to.name)) {
      state.unavailableScreen = String(to.name);
      return { name: "unavailable", query: { screen: String(to.name) } };
    }

    if (to.name === "unavailable") {
      state.unavailableScreen = to.query?.screen
        ? String(to.query.screen)
        : null;
    }

    if (from !== START_LOCATION || to.meta.viewId === state.currentView) {
      return;
    }

    if (to.meta.requiresActiveQuestionnaire && !state.activeQuestionnaire) {
      return resolveDefaultPortalRoute();
    }
  });

  router.afterEach((to, _from, failure) => {
    if (!failure && to.meta.viewId && to.meta.viewId !== state.currentView) {
      showView(to.meta.viewId);
    }
  });

  watch(
    () => isScreenAvailable("wallet"),
    (walletAvailable) => {
      if (!portalMounted) return;
      const current = router.currentRoute.value;
      if (walletAvailable && current.name === "unavailable") {
        const requested = current.query?.screen
          ? String(current.query.screen)
          : "";
        if (!requested || requested === "wallet") {
          state.unavailableScreen = null;
          router.replace({ name: "wallet" }).catch(console.warn);
        }
      } else if (!walletAvailable && current.name === "wallet") {
        state.unavailableScreen = "wallet";
        router
          .replace({ name: "unavailable", query: { screen: "wallet" } })
          .catch(console.warn);
      }
    }
  );

  // Synchronous, so the push supersedes any navigation still in flight.
  watch(
    () => state.currentView,
    (viewId) => {
      if (!portalMounted) return;
      const route = findRouteForView(viewId);
      if (!route) return;

      if (!isScreenAvailable(route.name)) {
        state.unavailableScreen = String(route.name);
        state.currentView = "view-unavailable";
        return;
      }

      if (route.name === "unavailable") {
        if (router.currentRoute.value.name !== "unavailable") {
          router
            .push({
              name: "unavailable",
              query: state.unavailableScreen
                ? { screen: state.unavailableScreen }
                : undefined,
            })
            .catch(console.warn);
        }
        return;
      }

      if (router.currentRoute.value.name !== route.name) {
        router.push({ name: route.name }).catch(console.warn);
      }
    },
    { flush: "sync" }
  );

  return router;
}
