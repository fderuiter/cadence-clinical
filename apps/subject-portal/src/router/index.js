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

let router = null;

// The router singleton, created on first mount and shared by later mounts.
export function getPortalRouter() {
  if (router) return router;

  router = createRouter({
    history: createPortalHistory(),
    routes: [
      ...viewRoutes,
      unavailableRoute,
      { path: "/:pathMatch(.*)*", redirect: { name: "tasks" } },
    ],
  });

  router.beforeEach((to, from) => {
    if (from === START_LOCATION && memoryHistory) {
      const initialRoute = findRouteForView(state.currentView);
      if (initialRoute && isScreenAvailable(initialRoute.name)) {
        return to.name === initialRoute.name
          ? undefined
          : { name: initialRoute.name };
      }
      if (initialRoute && !isScreenAvailable(initialRoute.name)) {
        state.unavailableScreen = String(initialRoute.name);
        return {
          name: "unavailable",
          query: { screen: String(initialRoute.name) },
        };
      }
    }

    if (to.name && !isScreenAvailable(to.name)) {
      state.unavailableScreen = String(to.name);
      return { name: "unavailable", query: { screen: String(to.name) } };
    }

    if (to.name === "unavailable" && to.query?.screen) {
      state.unavailableScreen = String(to.query.screen);
    }

    if (from !== START_LOCATION || to.meta.viewId === state.currentView) {
      return;
    }

    if (to.meta.requiresActiveQuestionnaire && !state.activeQuestionnaire) {
      return { name: "tasks" };
    }
  });

  router.afterEach((to, _from, failure) => {
    if (!failure && to.meta.viewId && to.meta.viewId !== state.currentView) {
      showView(to.meta.viewId);
    }
  });

  // Synchronous, so the push supersedes any navigation still in flight
  // (including rapid A -> B -> A transitions before B commits currentRoute).
  let pushSeq = 0;
  let activePushSeq = 0;
  let pendingRouteName = null;

  function syncPushRoute(targetLocation, targetName) {
    const seq = ++pushSeq;
    activePushSeq = seq;
    pendingRouteName = targetName;
    router
      .push(targetLocation)
      .catch(console.warn)
      .finally(() => {
        if (activePushSeq === seq) {
          pendingRouteName = null;
        }
      });
  }

  watch(
    () => state.currentView,
    (viewId) => {
      const route = findRouteForView(viewId);
      if (!route) return;

      if (!isScreenAvailable(route.name)) {
        state.unavailableScreen = String(route.name);
        state.currentView = "view-unavailable";
        return;
      }

      const effectiveCurrentName =
        pendingRouteName ?? router.currentRoute.value.name;

      if (route.name === "unavailable") {
        if (effectiveCurrentName !== "unavailable") {
          syncPushRoute(
            {
              name: "unavailable",
              query: state.unavailableScreen
                ? { screen: state.unavailableScreen }
                : undefined,
            },
            "unavailable"
          );
        }
        return;
      }

      if (effectiveCurrentName !== route.name) {
        syncPushRoute({ name: route.name }, route.name);
      }
    },
    { flush: "sync" }
  );

  return router;
}
