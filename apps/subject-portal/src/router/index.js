// The portal router. Every view stays mounted (the imperative feature modules
// render into their elements), so the router does not swap components: it
// keeps the URL, query parameters, and `state.currentView` in step in both directions.
import { watch } from "vue";
import {
  START_LOCATION,
  createMemoryHistory,
  createRouter,
  createWebHashHistory,
  createWebHistory,
} from "vue-router";

import { loadConsentDetails } from "../features/consent.js";
import { showView } from "../features/navigation.js";
import {
  PEEK_QUERY_KEYS,
  registerPeekQuerySync,
  syncPeekFromQuery,
} from "../features/peek.js";
import { startQuestionnaire } from "../features/questionnaire.js";
import { closeSignatureModal } from "../features/signature.js";
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

function normalizeQueryString(value) {
  if (Array.isArray(value)) {
    return normalizeQueryString(value[0]);
  }
  if (typeof value === "string") {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return null;
}

function buildScopedQuery(sourceQuery = {}, fallbackQuery = {}) {
  const next = {};
  const studyId =
    normalizeQueryString(sourceQuery?.studyId) ||
    normalizeQueryString(fallbackQuery?.studyId) ||
    normalizeQueryString(state.session?.studyId);
  const siteId =
    normalizeQueryString(sourceQuery?.siteId) ||
    normalizeQueryString(fallbackQuery?.siteId) ||
    normalizeQueryString(state.session?.siteId);

  if (studyId) next.studyId = studyId;
  if (siteId) next.siteId = siteId;
  return next;
}

function hydrateSessionScopeFromRouteQuery(query = {}) {
  const queryStudy = normalizeQueryString(query?.studyId);
  const querySite = normalizeQueryString(query?.siteId);
  if (queryStudy && state.session.studyId !== queryStudy) {
    state.session.studyId = queryStudy;
  }
  if (querySite && state.session.siteId !== querySite) {
    state.session.siteId = querySite;
  }
}

function isAssignmentEligibleForQuestionnaire(assignmentId) {
  if (!assignmentId) return false;
  const fromAssignments = (state.assignments || []).find(
    (a) => String(a.id) === String(assignmentId)
  );
  const candidate =
    fromAssignments ||
    (state.activeQuestionnaire?.assignment?.id === assignmentId
      ? state.activeQuestionnaire.assignment
      : null);

  if (!candidate) return false;
  if (
    candidate.status === "COMPLETED" ||
    candidate.status === "PENDING_SYNC" ||
    Number(candidate.version_index) > 1
  ) {
    return false;
  }
  return true;
}

function buildViewQuery(routeName, currentRouteQuery = {}) {
  const base = buildScopedQuery(currentRouteQuery);

  if (routeName === "questionnaire") {
    const activeId =
      normalizeQueryString(state.activeQuestionnaire?.assignment?.id) ||
      normalizeQueryString(currentRouteQuery?.assignmentId);
    if (activeId) {
      base.assignmentId = activeId;
    }
  } else if (routeName === "consent") {
    const lang =
      normalizeQueryString(state.consentLanguage) ||
      normalizeQueryString(currentRouteQuery?.lang);
    const version =
      normalizeQueryString(
        state.consentContent?.version_index ??
          state.consentContent?.document_version
      ) || normalizeQueryString(currentRouteQuery?.version);
    if (lang) base.lang = lang;
    if (version) base.version = version;
  } else if (routeName === "unavailable") {
    const screen =
      normalizeQueryString(state.unavailableScreen) ||
      normalizeQueryString(currentRouteQuery?.screen);
    if (screen) base.screen = screen;
  }

  if (state.peekRecord?.queryKey && state.peekRecord?.queryValue) {
    base[state.peekRecord.queryKey] = String(state.peekRecord.queryValue);
  }

  return base;
}

function queriesMatch(a = {}, b = {}) {
  const keysA = Object.keys(a).filter(
    (k) => a[k] !== undefined && a[k] !== null && a[k] !== ""
  );
  const keysB = Object.keys(b).filter(
    (k) => b[k] !== undefined && b[k] !== null && b[k] !== ""
  );
  if (keysA.length !== keysB.length) return false;
  return keysA.every((key) => String(a[key]) === String(b[key]));
}

function resolveInitialWindowRouteForTest() {
  if (typeof window === "undefined" || !window.location) return null;
  const rawPath = window.location.pathname || "/";
  const rawSearch = window.location.search || "";
  const normalizedBase = ROUTER_BASE.replace(/\/+$/, "");
  let subPath = rawPath;
  for (const prefix of [normalizedBase, "/subject-portal"]) {
    if (prefix && (subPath === prefix || subPath.startsWith(`${prefix}/`))) {
      subPath = subPath.slice(prefix.length) || "/";
      break;
    }
  }
  if (!subPath.startsWith("/")) {
    subPath = `/${subPath}`;
  }
  if (subPath === "/" && !rawSearch) {
    return null;
  }
  return `${subPath}${rawSearch}`;
}

let router = null;
let syncingRouteQuery = false;
let pushingFromViewState = false;
let portalMounted = false;

export function setPortalRouterMounted(mounted) {
  portalMounted = Boolean(mounted);
}

export function preparePortalRouterForMount() {
  const instance = getPortalRouter();
  if (!memoryHistory) {
    if (instance.currentRoute.value === START_LOCATION) {
      void instance
        .replace(instance.options.history.location || "/")
        .catch(() => {});
    }
    return instance;
  }

  const initialTestTarget = resolveInitialWindowRouteForTest();
  if (initialTestTarget) {
    instance.options.history.replace(initialTestTarget);
    void instance.replace(initialTestTarget).catch(() => {});
    return instance;
  }

  let desiredRoute = findRouteForView(state.currentView);
  if (
    desiredRoute?.name === "questionnaire" &&
    !isAssignmentEligibleForQuestionnaire(
      normalizeQueryString(state.activeQuestionnaire?.assignment?.id)
    )
  ) {
    state.currentView = "view-tasks";
    desiredRoute = findRouteForView("view-tasks");
  }

  const targetPath = desiredRoute?.path || "/";
  instance.options.history.replace(targetPath);
  void instance.replace(targetPath).catch(() => {});
  return instance;
}

export function resetPortalRouterForTests() {
  router = null;
  syncingRouteQuery = false;
  pushingFromViewState = false;
  portalMounted = false;
}

export async function syncActiveRouteEntities(
  route = router?.currentRoute?.value,
  { fromStatePush = false } = {}
) {
  if (!route) return;
  hydrateSessionScopeFromRouteQuery(route.query);

  if (route.name === "questionnaire") {
    const assignmentId =
      normalizeQueryString(route.query?.assignmentId) ||
      normalizeQueryString(state.activeQuestionnaire?.assignment?.id);
    if (!isAssignmentEligibleForQuestionnaire(assignmentId)) {
      state.activeQuestionnaire = null;
      if (state.signatureModalOpen) {
        closeSignatureModal();
      }
      if (router) {
        await router.replace({
          name: "tasks",
          query: buildScopedQuery(route.query),
        });
      }
      return;
    }

    const container =
      typeof document !== "undefined"
        ? document.getElementById("questionnaire-form-container")
        : null;
    const needsStart =
      state.activeQuestionnaire?.assignment?.id !== assignmentId ||
      (container && container.children.length === 0);
    if (needsStart && container) {
      await startQuestionnaire(assignmentId);
    }
  } else if (route.name === "consent" && !fromStatePush) {
    const requestedLang = normalizeQueryString(route.query?.lang);
    if (requestedLang && requestedLang !== state.consentLanguage) {
      state.consentLanguage = requestedLang;
      const selector =
        typeof document !== "undefined"
          ? document.getElementById("consent-lang-selector")
          : null;
      if (selector) selector.value = requestedLang;
      await loadConsentDetails();
    }
  }

  if (!fromStatePush) {
    syncPeekFromQuery(route.query);
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
      { path: "/:pathMatch(.*)*", redirect: { name: "tasks" } },
    ],
  });

  router.beforeEach((to, from) => {
    hydrateSessionScopeFromRouteQuery(to.query);
    const initialTestTarget = memoryHistory
      ? resolveInitialWindowRouteForTest()
      : null;

    if (
      from === START_LOCATION &&
      memoryHistory &&
      !initialTestTarget &&
      to.path === "/" &&
      Object.keys(to.query || {}).length === 0
    ) {
      const initialRoute = findRouteForView(state.currentView);
      if (initialRoute && isScreenAvailable(initialRoute.name)) {
        const targetQuery = buildViewQuery(initialRoute.name, to.query);
        if (
          to.name === initialRoute.name &&
          queriesMatch(to.query, targetQuery)
        ) {
          return undefined;
        }
        return { name: initialRoute.name, query: targetQuery };
      }
      if (initialRoute && !isScreenAvailable(initialRoute.name)) {
        state.unavailableScreen = String(initialRoute.name);
        return {
          name: "unavailable",
          query: {
            ...buildScopedQuery(to.query),
            screen: String(initialRoute.name),
          },
        };
      }
    }

    if (to.name && to.name !== "unavailable" && !isScreenAvailable(to.name)) {
      state.unavailableScreen = String(to.name);
      return {
        name: "unavailable",
        query: {
          ...buildScopedQuery(to.query, from.query),
          screen: String(to.name),
        },
      };
    }

    if (to.name === "unavailable" && to.query?.screen) {
      state.unavailableScreen = String(to.query.screen);
    }

    // Validate /questionnaire on EVERY navigation (initial load, cold reload, and browser Back/Forward).
    if (to.meta.requiresActiveQuestionnaire) {
      const candidateAssignmentId =
        normalizeQueryString(to.query?.assignmentId) ||
        normalizeQueryString(state.activeQuestionnaire?.assignment?.id);
      if (!isAssignmentEligibleForQuestionnaire(candidateAssignmentId)) {
        state.activeQuestionnaire = null;
        if (state.signatureModalOpen) {
          closeSignatureModal();
        }
        return {
          name: "tasks",
          query: buildScopedQuery(to.query, from.query),
          replace: true,
        };
      }

      const desiredQuery = {
        ...to.query,
        ...buildScopedQuery(to.query, from.query),
        assignmentId: candidateAssignmentId,
      };
      if (!queriesMatch(to.query, desiredQuery)) {
        return {
          name: to.name,
          query: desiredQuery,
          hash: to.hash,
          replace: true,
        };
      }
      return undefined;
    }

    // Preserve studyId and siteId across route/tab transitions whenever scoped.
    const carriedScope = buildScopedQuery(to.query, from.query);
    if (
      (carriedScope.studyId && !to.query?.studyId) ||
      (carriedScope.siteId && !to.query?.siteId)
    ) {
      return {
        name: to.name,
        params: to.params,
        query: { ...to.query, ...carriedScope },
        hash: to.hash,
        replace: from === START_LOCATION,
      };
    }
  });

  router.afterEach((to, _from, failure) => {
    if (failure) return;
    const wasStatePush = pushingFromViewState || syncingRouteQuery;
    if (to.meta.viewId && to.meta.viewId !== state.currentView) {
      showView(to.meta.viewId);
    }
    void syncActiveRouteEntities(to, { fromStatePush: wasStatePush });
  });

  registerPeekQuerySync((record) => {
    if (!router || !portalMounted || syncingRouteQuery) return;
    const current = router.currentRoute.value;
    if (!current || current === START_LOCATION || !current.name) return;

    const nextQuery = { ...current.query };
    for (const key of PEEK_QUERY_KEYS) {
      delete nextQuery[key];
    }
    if (record?.queryKey && record?.queryValue) {
      nextQuery[record.queryKey] = String(record.queryValue);
    }
    const scoped = buildScopedQuery(nextQuery);
    Object.assign(nextQuery, scoped);

    if (!queriesMatch(current.query, nextQuery)) {
      syncingRouteQuery = true;
      router
        .replace({
          name: current.name,
          params: current.params,
          query: nextQuery,
          hash: current.hash,
        })
        .catch(() => {})
        .finally(() => {
          syncingRouteQuery = false;
        });
    }
  });

  // Reflect active questionnaire assignmentId and consent version/lang into URL query.
  watch(
    () => [
      state.activeQuestionnaire?.assignment?.id || null,
      state.consentLanguage || null,
      state.consentContent?.version_index ??
        state.consentContent?.document_version ??
        null,
      state.session?.studyId || null,
      state.session?.siteId || null,
    ],
    () => {
      if (!router || !portalMounted || syncingRouteQuery) return;
      const current = router.currentRoute.value;
      if (!current || current === START_LOCATION || !current.name) return;
      const desiredQuery = {
        ...current.query,
        ...buildScopedQuery(current.query),
      };
      if (current.name === "questionnaire") {
        const activeId = normalizeQueryString(
          state.activeQuestionnaire?.assignment?.id
        );
        if (activeId) {
          desiredQuery.assignmentId = activeId;
        }
      } else {
        delete desiredQuery.assignmentId;
      }

      if (current.name === "consent") {
        if (state.consentLanguage) {
          desiredQuery.lang = String(state.consentLanguage);
        }
        const docVersion =
          state.consentContent?.version_index ??
          state.consentContent?.document_version;
        if (
          docVersion !== undefined &&
          docVersion !== null &&
          docVersion !== ""
        ) {
          desiredQuery.version = String(docVersion);
        }
      }

      if (!queriesMatch(current.query, desiredQuery)) {
        syncingRouteQuery = true;
        router
          .replace({
            name: current.name,
            params: current.params,
            query: desiredQuery,
            hash: current.hash,
          })
          .catch(() => {})
          .finally(() => {
            syncingRouteQuery = false;
          });
      }
    }
  );

  // Synchronous, so the push supersedes any navigation still in flight.
  watch(
    () => state.currentView,
    (viewId) => {
      if (!router || !portalMounted) return;
      const current = router.currentRoute.value;
      if (!current || current === START_LOCATION) return;

      const route = findRouteForView(viewId);
      if (!route) return;

      if (route.name !== "unavailable" && !isScreenAvailable(route.name)) {
        state.unavailableScreen = String(route.name);
        state.currentView = "view-unavailable";
        return;
      }

      if (route.name === "unavailable") {
        if (current.name !== "unavailable") {
          pushingFromViewState = true;
          router
            .push({
              name: "unavailable",
              query: buildViewQuery("unavailable", current.query),
            })
            .catch(console.warn)
            .finally(() => {
              pushingFromViewState = false;
            });
        }
        return;
      }

      if (current.name !== route.name) {
        pushingFromViewState = true;
        router
          .push({
            name: route.name,
            query: buildViewQuery(route.name, current.query),
          })
          .catch(console.warn)
          .finally(() => {
            pushingFromViewState = false;
          });
      }
    },
    { flush: "sync" }
  );

  return router;
}
