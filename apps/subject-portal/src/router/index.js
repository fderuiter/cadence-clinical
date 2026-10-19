// The portal router. Every view stays mounted (the imperative feature modules
// render into their elements), so the router does not swap components: it
// keeps the URL and `state.currentView` in step in both directions.
//
// - showView(), or any write to state.currentView, pushes the view's route.
// - A URL change (back/forward, or a deep link on load) calls showView().
import {
  START_LOCATION,
  createMemoryHistory,
  createRouter,
  createWebHistory,
} from "vue-router";
import { watch } from "vue";

import { showView } from "../features/navigation.js";
import { state } from "../stores/state.js";
import { routeForView, viewRoutes } from "./routes.js";

// Vite serves and builds the portal under this base (vite.config.js).
export const ROUTER_BASE = "/subject-portal/";

// Memory history under Vitest or without a `window.location` (there is no
// URL to follow there); browser history in the app.
const memoryHistory =
  import.meta.env?.MODE === "test" ||
  typeof window === "undefined" ||
  !window.location;

export function createPortalHistory() {
  return memoryHistory
    ? createMemoryHistory(ROUTER_BASE)
    : createWebHistory(ROUTER_BASE);
}

let router = null;

// The router singleton, created on first mount and shared by later mounts.
export function getPortalRouter() {
  if (router) return router;

  router = createRouter({
    history: createPortalHistory(),
    routes: [
      ...viewRoutes,
      { path: "/:pathMatch(.*)*", redirect: { name: "tasks" } },
    ],
  });

  // On mount (the first navigation, and the first after every app unmounted):
  // with memory history the current view wins, so mounting never changes it;
  // with browser history the URL wins (a deep link), except that the
  // questionnaire needs one in progress and otherwise opens the tasks.
  router.beforeEach((to, from) => {
    if (from !== START_LOCATION || to.meta.viewId === state.currentView) {
      return;
    }
    if (memoryHistory) {
      const route = routeForView(state.currentView);
      return route ? { name: route.name } : undefined;
    }
    if (to.meta.requiresActiveQuestionnaire && !state.activeQuestionnaire) {
      return { name: "tasks" };
    }
  });

  router.afterEach((to, from, failure) => {
    if (!failure && to.meta.viewId && to.meta.viewId !== state.currentView) {
      showView(to.meta.viewId);
    }
  });

  // Synchronous, so the push supersedes any navigation still in flight.
  watch(
    () => state.currentView,
    (viewId) => {
      const route = routeForView(viewId);
      if (!route) return;
      router.push({ name: route.name }).catch(console.warn);
    },
    { flush: "sync" }
  );

  return router;
}
