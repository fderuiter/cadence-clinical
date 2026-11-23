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
import { defaultPortalRouteName, isScreenAvailable } from "./availability.js";
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
      {
        path: "/:pathMatch(.*)*",
        redirect: () => ({ name: defaultPortalRouteName() }),
      },
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
        if (initialRoute.name === "tasks") {
          const fallbackName = defaultPortalRouteName();
          if (fallbackName !== "unavailable") {
            state.unavailableScreen = null;
            return to.name === fallbackName
              ? undefined
              : { name: fallbackName };
          }
          if (to.name && to.name !== "tasks" && to.name !== "unavailable") {
            state.unavailableScreen = String(to.name);
            return {
              name: "unavailable",
              query: { screen: String(to.name) },
            };
          }
          state.unavailableScreen = null;
          return to.name === "unavailable"
            ? undefined
            : { name: "unavailable" };
        }
        state.unavailableScreen = String(initialRoute.name);
        return {
          name: "unavailable",
          query: { screen: String(initialRoute.name) },
        };
      }
    }

    if (to.name === "tasks" && !isScreenAvailable("tasks")) {
      const fallbackName = defaultPortalRouteName();
      state.unavailableScreen = null;
      return { name: fallbackName };
    }

    if (to.name && !isScreenAvailable(to.name)) {
      state.unavailableScreen = String(to.name);
      return { name: "unavailable", query: { screen: String(to.name) } };
    }

    if (to.name === "unavailable") {
      state.unavailableScreen = to.query?.screen
        ? String(to.query.screen)
        : state.unavailableScreen || null;
      return;
    }

    if (from !== START_LOCATION || to.meta.viewId === state.currentView) {
      return;
    }

    if (to.meta.requiresActiveQuestionnaire && !state.activeQuestionnaire) {
      return { name: defaultPortalRouteName() };
    }
  });

  router.afterEach((to, _from, failure) => {
    if (!failure && to.meta.viewId && to.meta.viewId !== state.currentView) {
      showView(to.meta.viewId);
    }
  });

  // Synchronous, so the push supersedes any navigation still in flight.
  watch(
    () => state.currentView,
    (viewId) => {
      const route = findRouteForView(viewId);
      if (!route) return;

      if (!isScreenAvailable(route.name)) {
        if (route.name === "tasks" && isScreenAvailable("wallet")) {
          state.unavailableScreen = null;
          state.currentView = "view-wallet";
          return;
        }
        state.unavailableScreen =
          route.name === "tasks" ? null : String(route.name);
        state.currentView = "view-unavailable";
        return;
      }

      if (route.name === "unavailable") {
        const targetQuery = state.unavailableScreen
          ? { screen: state.unavailableScreen }
          : undefined;
        if (
          router.currentRoute.value.name !== "unavailable" ||
          (router.currentRoute.value.query?.screen || null) !==
            (state.unavailableScreen || null)
        ) {
          router
            .push({
              name: "unavailable",
              query: targetQuery,
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
