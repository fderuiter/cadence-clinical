import { computed } from "vue";
import { resolveActiveWorkspaceScope } from "../../canvas/palette";
import { useActiveStudyScope } from "../../composables/useActiveStudyScope";
import { isDemoBuild } from "../../utils/demoMode.js";

const NON_WORKFLOW_ROUTE_NAMES = new Set([
  "landing",
  "home",
  "overview",
  "login",
  "plans",
  "trial-onboarding",
  "forbidden",
  "unavailable",
]);

const NON_WORKFLOW_PATHS = new Set([
  "/",
  "/overview",
  "/login",
  "/plans",
  "/onboarding/trial",
  "/forbidden",
  "/unavailable",
]);

export function hasShellBlockingModalOverlay(excludeId = "") {
  if (typeof document === "undefined") return false;
  const dialogs = document.querySelectorAll(
    '[aria-modal="true"], [role="dialog"], [role="alertdialog"]'
  );
  for (const dialog of dialogs) {
    if (
      excludeId &&
      (dialog.id === excludeId || dialog.closest?.(`#${excludeId}`))
    ) {
      continue;
    }
    if (
      dialog.hasAttribute?.("hidden") ||
      dialog.getAttribute?.("aria-hidden") === "true"
    ) {
      continue;
    }
    return true;
  }
  return false;
}

export function useAppShellWorkspaceNavigation({
  route,
  authStore,
  passiveCanvasState,
  isOnboardingTourActive,
}) {
  const normalizedRoles = computed(() => authStore.normalizedRoles || []);
  const { activeStudyCode: syncedStudyCode } = useActiveStudyScope();
  const workspaceScope = computed(() =>
    resolveActiveWorkspaceScope(route, passiveCanvasState.value)
  );
  const activeStudyCode = computed(
    () => workspaceScope.value.studyCode || syncedStudyCode.value
  );

  const isCanvasRoute = computed(
    () =>
      route?.name === "study" ||
      Boolean(route?.path && route.path.startsWith("/studies"))
  );
  const hasStudyWorkspace = computed(
    () =>
      route?.name === "study" &&
      typeof route?.params?.studyCode === "string" &&
      Boolean(route.params.studyCode.trim())
  );
  const isConductCanvasRoute = computed(
    () =>
      !isDemoBuild() && isCanvasRoute.value && route?.query?.stage === "conduct"
  );

  const showContextBar = computed(() => {
    if (!authStore.isAuthenticated || isCanvasRoute.value) return false;
    const routeName = String(route?.name || "");
    const routePath = String(route?.path || "");
    if (
      NON_WORKFLOW_ROUTE_NAMES.has(routeName) ||
      NON_WORKFLOW_PATHS.has(routePath)
    ) {
      return false;
    }
    return Boolean(routeName || routePath);
  });

  const activeTab = computed(() => String(route?.name || "study"));

  const showOnboardingTour = computed(
    () =>
      Boolean(authStore.isAuthenticated) &&
      route?.name !== "login" &&
      route?.path !== "/login" &&
      route?.name !== "plans" &&
      route?.path !== "/plans" &&
      route?.name !== "trial-onboarding" &&
      route?.path !== "/onboarding/trial" &&
      (isOnboardingTourActive.value ||
        (route?.name !== "forbidden" &&
          route?.path !== "/forbidden" &&
          route?.name !== "unavailable" &&
          route?.path !== "/unavailable"))
  );

  return {
    normalizedRoles,
    workspaceScope,
    activeStudyCode,
    isCanvasRoute,
    hasStudyWorkspace,
    isConductCanvasRoute,
    showContextBar,
    activeTab,
    showOnboardingTour,
  };
}
