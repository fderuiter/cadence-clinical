import {
  computed,
  getCurrentInstance,
  onMounted,
  onUnmounted,
  watch,
} from "vue";
import { useRouter } from "vue-router";
import { hasActiveModalFocusTrap, useEscapeClose } from "ui";
import {
  buildScopedWorkflowPath,
  resolveWorkspaceNavigationScope,
} from "../../canvas/palette";
import { useActiveStudyScope } from "../../composables/useActiveStudyScope";
import { isScreenAvailable } from "../../router/availability.js";
import {
  canonicalizeContextualRoute,
  sanitizePseudonymousSubjectCode,
} from "../../stores/knowledge.js";
import { PERSONA_PRESETS } from "../../stores/auth.js";
import { resolveStorePinia } from "../../stores/onboardingSummary.js";
import { useToastStore } from "../../stores/toasts.js";
import { isDemoBuild } from "../../utils/demoMode.js";
import {
  hasBlockingModalDialog,
  isEditableOrFormControl,
} from "../../utils/lazyOverlay.js";
import { useTicketMutationPermission } from "../../utils/ticketPermissions";
import { resolveActiveRouteLocation } from "./contextualHelpUtils.js";

export function useContextualHelpSync({
  route,
  authStore,
  knowledgeStore,
  drawerRootRef,
  isSearchOpen,
  searchInput,
  clearSearchTimer,
}) {
  const pinia = resolveStorePinia(authStore);
  const toastStore = useToastStore(pinia);
  const instance = getCurrentInstance();
  const { activeStudyCode } = useActiveStudyScope();
  const activeCanvasStore = computed(
    () =>
      (resolveStorePinia(authStore) || pinia)?.state?.value?.studyCanvas || null
  );
  let injectedRouter = null;
  try {
    injectedRouter = useRouter();
  } catch {
    injectedRouter = null;
  }

  const { isReadOnlyTicketViewer: isReadOnlyAuditor } =
    useTicketMutationPermission(authStore);

  const canonicalRoute = computed(() =>
    canonicalizeContextualRoute({
      path: route?.path || "/",
      query: route?.query || {},
    })
  );

  const activePersonaLabel = computed(() => {
    const pKey =
      authStore.currentPersona || knowledgeStore.activePersona || "super_admin";
    const p = PERSONA_PRESETS.find((preset) => preset.key === pKey);
    return p ? p.label.replace(/^[^\w\s]+\s*/, "").trim() : "Super Admin";
  });

  function resolveEscalationRoute() {
    const liveRoute = resolveActiveRouteLocation(route);
    const storeRoute = String(
      knowledgeStore.rawRoute || knowledgeStore.currentRoute || ""
    ).trim();
    if (!knowledgeStore.hasLoadedContextualHelp || !storeRoute) {
      return liveRoute || storeRoute || "/";
    }
    if (liveRoute && liveRoute.includes("?")) {
      const livePath = liveRoute.split("?")[0] || "/";
      const storePath = storeRoute.split("?")[0] || "/";
      if (livePath === storePath) return liveRoute;
    }
    return storeRoute;
  }

  function resolveDrawerHubScope() {
    const scope = resolveWorkspaceNavigationScope(
      route,
      activeCanvasStore.value,
      undefined,
      activeStudyCode.value
    );
    const storeScope = knowledgeStore.activeRouteScope || {};
    const studyCode = scope.studyCode || storeScope.study_id || "";
    const sameOrUnsetStoreStudy =
      !studyCode || !storeScope.study_id || storeScope.study_id === studyCode;
    const siteId =
      scope.siteId || (sameOrUnsetStoreStudy ? storeScope.site_id || "" : "");
    const rawSubject =
      scope.subjectCode ||
      (sameOrUnsetStoreStudy ? storeScope.subject_id || "" : "");
    return {
      studyCode,
      siteId,
      subjectCode: sanitizePseudonymousSubjectCode(rawSubject),
    };
  }

  function openHubRoute(path) {
    const scopedPath = buildScopedWorkflowPath(path, resolveDrawerHubScope());
    knowledgeStore.closeDrawer();
    const router =
      injectedRouter || instance?.appContext?.config?.globalProperties?.$router;
    if (router && typeof router.push === "function") {
      router.push(scopedPath);
    }
  }

  function handleEscalate() {
    if (isReadOnlyAuditor.value) return;
    const hasRouteScope =
      route?.path &&
      canonicalRoute.value === knowledgeStore.currentRoute &&
      route.query &&
      Object.keys(route.query).length > 0;
    knowledgeStore.prepareEscalation({
      route: resolveEscalationRoute(),
      query: hasRouteScope ? route.query : undefined,
    });
  }

  function handleTicketCreated(createdTicket) {
    if (!createdTicket) {
      knowledgeStore.cancelEscalation();
      return;
    }
    knowledgeStore.openCreatedTicketInDrawer(createdTicket);
    const refId = createdTicket.reference || createdTicket.id;
    const queueSuffix = createdTicket.assigned_queue
      ? ` (${createdTicket.assigned_queue})`
      : "";
    const simulated = isDemoBuild() || String(refId || "").startsWith("SIM-");
    const scopedTicketsBase = buildScopedWorkflowPath(
      "/tickets",
      resolveDrawerHubScope()
    );
    const refJoiner = scopedTicketsBase.includes("?") ? "&" : "?";
    toastStore.push({
      tone: "success",
      title: simulated
        ? `Simulated ticket ${refId} saved in demo session`
        : `Support ticket ${refId} submitted`,
      message: `Reference ID: ${refId}${queueSuffix}`,
      action:
        refId && isScreenAvailable("tickets")
          ? {
              label: `Open ${refId}`,
              to: `${scopedTicketsBase}${refJoiner}ref=${encodeURIComponent(refId)}`,
            }
          : null,
    });
  }

  useEscapeClose(() => knowledgeStore.closeDrawer(), {
    isOpen: () => knowledgeStore.isOpen && !knowledgeStore.isEscalating,
  });

  watch(canonicalRoute, (newRoute) => {
    if (newRoute && newRoute !== knowledgeStore.currentRoute) {
      searchInput.value = "";
      isSearchOpen.value = false;
      const activeLocation = resolveActiveRouteLocation(route) || newRoute;
      if (knowledgeStore.isOpen) {
        knowledgeStore.fetchContextualHelp(
          activeLocation,
          authStore.currentPersona
        );
        if (
          knowledgeStore.activeDrawerTab === "tickets" &&
          !knowledgeStore.selectedTicket
        ) {
          knowledgeStore.fetchDrawerTickets();
        }
      } else {
        knowledgeStore.syncDeferredContext(
          activeLocation,
          newRoute,
          authStore.currentPersona
        );
      }
    }
  });

  watch(
    () => resolveActiveRouteLocation(route),
    (newFullRoute) => {
      if (
        newFullRoute &&
        canonicalRoute.value === knowledgeStore.currentRoute &&
        newFullRoute !== knowledgeStore.rawRoute
      ) {
        knowledgeStore.rawRoute = newFullRoute;
        if (
          knowledgeStore.isOpen &&
          knowledgeStore.activeDrawerTab === "tickets" &&
          !knowledgeStore.selectedTicket
        ) {
          knowledgeStore.fetchDrawerTickets();
        }
      }
    }
  );

  watch(
    () => authStore.currentPersona,
    (newPersona) => {
      if (newPersona && newPersona !== knowledgeStore.activePersona) {
        if (isReadOnlyAuditor.value && knowledgeStore.isEscalating) {
          knowledgeStore.cancelEscalation();
        }
        searchInput.value = "";
        isSearchOpen.value = false;
        const activeLocation =
          resolveActiveRouteLocation(route) ||
          canonicalRoute.value ||
          knowledgeStore.currentRoute;
        if (knowledgeStore.isOpen) {
          knowledgeStore.fetchContextualHelp(activeLocation, newPersona);
          if (
            knowledgeStore.activeDrawerTab === "tickets" &&
            !knowledgeStore.selectedTicket
          ) {
            knowledgeStore.fetchDrawerTickets();
          }
        } else {
          knowledgeStore.syncDeferredContext(
            activeLocation,
            canonicalRoute.value || activeLocation,
            newPersona
          );
        }
      }
    }
  );

  watch(
    () => knowledgeStore.isOpen,
    (isOpen) => {
      if (
        isOpen &&
        (!knowledgeStore.hasLoadedContextualHelp ||
          knowledgeStore.needsContextualRefresh) &&
        !knowledgeStore.loading
      ) {
        const activeLocation =
          resolveActiveRouteLocation(route) ||
          canonicalRoute.value ||
          knowledgeStore.currentRoute;
        if (activeLocation) {
          knowledgeStore.fetchContextualHelp(
            activeLocation,
            authStore.currentPersona
          );
        }
      }
    },
    { immediate: true }
  );

  watch(
    () => [knowledgeStore.isOpen, knowledgeStore.activeDrawerTab],
    ([isOpen, tab]) => {
      if (
        isOpen &&
        tab === "tickets" &&
        !knowledgeStore.selectedTicket &&
        !knowledgeStore.isTicketsLoading &&
        !knowledgeStore.hasLoadedDrawerTickets
      ) {
        knowledgeStore.fetchDrawerTickets();
      }
    },
    { immediate: true }
  );

  function hasActiveExternalModalTrap() {
    if (knowledgeStore.isEscalating) return true;
    if (hasActiveModalFocusTrap({ excludeId: "contextual-help-drawer" })) {
      return true;
    }
    let ancestor = drawerRootRef.value;
    while (ancestor && ancestor !== document.body) {
      if (
        ancestor.inert ||
        ancestor.hasAttribute?.("inert") ||
        ancestor.getAttribute?.("aria-hidden") === "true"
      ) {
        return true;
      }
      ancestor = ancestor.parentElement;
    }
    return hasBlockingModalDialog("contextual-help-drawer");
  }

  function handleKeydown(e) {
    if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.key !== "F1" && e.key !== "?") return;
    if (hasActiveExternalModalTrap() || isEditableOrFormControl(e.target)) {
      return;
    }
    e.preventDefault();
    knowledgeStore.toggleDrawer();
  }

  onMounted(() => {
    window.addEventListener("keydown", handleKeydown);
    const initialRoute = resolveActiveRouteLocation(route);
    if (
      initialRoute &&
      !knowledgeStore.hasLoadedContextualHelp &&
      !knowledgeStore.primaryArticle &&
      !knowledgeStore.loading
    ) {
      if (knowledgeStore.isOpen) {
        knowledgeStore.fetchContextualHelp(
          initialRoute || canonicalRoute.value,
          authStore.currentPersona
        );
      } else {
        knowledgeStore.syncDeferredContext(
          initialRoute || canonicalRoute.value,
          canonicalRoute.value,
          authStore.currentPersona
        );
      }
    }
  });

  onUnmounted(() => {
    window.removeEventListener("keydown", handleKeydown);
    clearSearchTimer?.();
  });

  return {
    isReadOnlyAuditor,
    activePersonaLabel,
    openHubRoute,
    handleEscalate,
    handleTicketCreated,
  };
}
