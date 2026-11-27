import { nextTick, onMounted, onUnmounted, ref, watch } from "vue";
import { useSignOutGuard } from "ui";
import { rehydrateOnboardingPersona } from "../../auth/sessionCoordinator.js";
import { isRouteAuthorizedForRoles } from "../../router";
import { resolveStorePinia } from "../../stores/onboardingSummary.js";
import {
  clearShellStickyHeaderHeight,
  ensureFocusedElementClearsStickyChrome,
  recordStickyChromePointerDown,
  syncRootStickyChromeOffset,
  updateShellStickyHeaderHeight,
} from "../../utils/stickyChromeOffset";
import { hasUserScopedDrafts } from "../../utils/userScopedStorage.js";
import { hasShellBlockingModalOverlay } from "./appShellScope.js";

export function useAppShellChromeAndSession({
  authStore,
  pinia,
  route,
  router,
  shellHeaderCompRef,
  normalizedRoles,
  onboardingSummary,
  isOnboardingTourActive,
  showOnboardingTour,
}) {
  const isCommandPaletteOpen = ref(false);
  const hasActivatedCommandPalette = ref(false);
  const pendingChecklistOpen = ref(false);
  const hasActivatedChecklistDrawer = ref(false);
  const hasActivatedOnboardingTour = ref(false);
  const hasActivatedSignOutModal = ref(false);
  const statusAnnouncement = ref("");
  const headerHeight = ref(48);
  const effectiveHeaderHeight = ref(48);
  let headerResizeObserver = null;

  function focusSkipTarget(targetId) {
    const target = document.getElementById(targetId);
    if (!target) return;
    target.focus();
    target.scrollIntoView?.({ block: "nearest" });
  }

  function syncHeaderHeight() {
    const el = shellHeaderCompRef.value?.getHeaderElement?.();
    if (el && typeof el.getBoundingClientRect === "function") {
      const measured = Math.round(el.getBoundingClientRect().height);
      if (measured > 0) headerHeight.value = measured;
    }
    const synced = updateShellStickyHeaderHeight(headerHeight.value);
    effectiveHeaderHeight.value = synced.shellHeight;
  }

  function onMenuStateChange() {
    nextTick(syncHeaderHeight);
  }

  function handleViewportResize() {
    syncHeaderHeight();
    syncRootStickyChromeOffset();
  }

  function handleGlobalFocusIn(event) {
    const target = event?.target;
    ensureFocusedElementClearsStickyChrome(target);
    if (
      typeof window !== "undefined" &&
      typeof window.requestAnimationFrame === "function"
    ) {
      window.requestAnimationFrame(() => {
        if (
          typeof document !== "undefined" &&
          document.activeElement === target
        ) {
          ensureFocusedElementClearsStickyChrome(target);
        }
      });
    }
  }

  const checkActiveRouteAuth = () =>
    authStore.isAuthenticated &&
    route?.path !== "/forbidden" &&
    route?.path !== "/login" &&
    !isRouteAuthorizedForRoles(route, normalizedRoles.value) &&
    router?.replace?.("/forbidden");

  async function handlePersonaChange(event) {
    const nextPersona = event?.target?.value;
    if (nextPersona) {
      authStore.switchPersona(nextPersona);
      rehydrateOnboardingPersona(
        authStore.currentPersona || nextPersona,
        resolveStorePinia(authStore) || pinia
      );
      const matched = (authStore.availablePersonas || []).find(
        (p) => p.key === nextPersona
      );
      statusAnnouncement.value = `Switched workspace view to ${matched?.label || nextPersona}`;
      await checkActiveRouteAuth();
    }
  }

  async function handleLogin() {
    if (typeof authStore.login === "function") {
      await authStore.login();
    } else {
      router.push("/login");
    }
  }

  async function handleRegister() {
    if (typeof authStore.register === "function") {
      const href = router.resolve("/onboarding/trial").href;
      await authStore.register({
        redirectUri: new URL(href, window.location.origin).toString(),
      });
    } else {
      router.push("/plans");
    }
  }

  const {
    isConfirmOpen: isSignOutConfirmOpen,
    requestSignOut: handleLogout,
    confirmSignOut,
    cancelSignOut,
  } = useSignOutGuard({
    hasExternalDrafts: () => hasUserScopedDrafts(),
    onConfirmSignOut: async () => {
      await authStore.logout();
      if (route?.path !== "/login") {
        await router?.replace?.("/login");
      }
    },
  });

  watch(
    () => authStore.isAuthenticated && onboardingSummary.value.isDrawerOpen,
    (open) => {
      if (open) hasActivatedChecklistDrawer.value = true;
    },
    { immediate: true }
  );
  watch(
    () => isCommandPaletteOpen.value && Boolean(authStore.isAuthenticated),
    (open) => {
      if (open) hasActivatedCommandPalette.value = true;
    },
    { immediate: true }
  );
  watch(
    isSignOutConfirmOpen,
    (open) => {
      if (open) hasActivatedSignOutModal.value = true;
    },
    { immediate: true }
  );
  watch(
    () => showOnboardingTour.value && isOnboardingTourActive.value,
    (active) => {
      if (active) hasActivatedOnboardingTour.value = true;
    },
    { immediate: true }
  );
  watch(showOnboardingTour, (allowed) => {
    if (!allowed) hasActivatedOnboardingTour.value = false;
  });
  watch(
    () => authStore.isAuthenticated,
    (authenticated) => {
      if (!authenticated) {
        hasActivatedOnboardingTour.value = false;
        hasActivatedChecklistDrawer.value = false;
        pendingChecklistOpen.value = false;
        hasActivatedCommandPalette.value = false;
      }
    }
  );

  function handleToggleChecklist() {
    const targetPinia = resolveStorePinia(authStore) || pinia;
    const mountedOnboarding = targetPinia?._s?.get("onboarding");
    if (
      mountedOnboarding &&
      typeof mountedOnboarding.toggleDrawer === "function"
    ) {
      hasActivatedChecklistDrawer.value = true;
      mountedOnboarding.toggleDrawer();
      return;
    }
    hasActivatedChecklistDrawer.value = true;
    pendingChecklistOpen.value = true;
  }

  function handleToggleContextualHelp() {
    const targetPinia = resolveStorePinia(authStore) || pinia;
    const mountedKnowledge = targetPinia?._s?.get("knowledge");
    if (
      mountedKnowledge &&
      typeof mountedKnowledge.toggleDrawer === "function"
    ) {
      mountedKnowledge.toggleDrawer();
      return;
    }
    void import("../../stores/knowledge").then(({ useKnowledgeStore }) => {
      const store = targetPinia
        ? useKnowledgeStore(targetPinia)
        : useKnowledgeStore();
      store.toggleDrawer();
    });
  }

  function handleDemoReset(message) {
    statusAnnouncement.value = message;
  }

  function handleGlobalKeydown(event) {
    if (event.key === "Escape" && !event.defaultPrevented) {
      shellHeaderCompRef.value?.handleEscapeKey?.(event);
      return;
    }
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "e") {
      (resolveStorePinia(authStore) || pinia)?._s
        ?.get("studyCanvas")
        ?.handleExplainShortcut?.(event);
      return;
    }
    if (!authStore.isAuthenticated) return;
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
      event.preventDefault();
      if (hasShellBlockingModalOverlay("canvas-palette")) return;
      isCommandPaletteOpen.value = !isCommandPaletteOpen.value;
    }
  }

  watch(
    () => [authStore.isAuthenticated, authStore.identity?.tenantId],
    ([authenticated], [wasAuthenticated] = []) => {
      if (authenticated && typeof authStore.loadEntitlements === "function") {
        authStore.loadEntitlements();
      } else if (
        !authenticated &&
        wasAuthenticated &&
        (route?.meta?.requiresAuth ||
          route?.matched?.some((r) => r.meta?.requiresAuth))
      ) {
        router?.replace?.("/login");
      }
    }
  );
  watch(
    () => [authStore.currentPersona, normalizedRoles.value],
    () => {
      void checkActiveRouteAuth();
    }
  );
  watch(
    () => [
      authStore.isAuthenticated,
      authStore.activePlanBadge,
      route?.fullPath,
    ],
    () => {
      nextTick(syncHeaderHeight);
    }
  );

  onMounted(() => {
    window.addEventListener("keydown", handleGlobalKeydown);
    window.addEventListener("resize", handleViewportResize);
    document.addEventListener(
      "pointerdown",
      recordStickyChromePointerDown,
      true
    );
    document.addEventListener("focusin", handleGlobalFocusIn);
    syncHeaderHeight();
    const headerEl = shellHeaderCompRef.value?.getHeaderElement?.();
    if (typeof ResizeObserver !== "undefined" && headerEl) {
      headerResizeObserver = new ResizeObserver(() => syncHeaderHeight());
      headerResizeObserver.observe(headerEl, { box: "border-box" });
    }
    if (
      authStore.isAuthenticated &&
      typeof authStore.loadEntitlements === "function"
    ) {
      authStore.loadEntitlements();
    }
  });

  onUnmounted(() => {
    window.removeEventListener("keydown", handleGlobalKeydown);
    window.removeEventListener("resize", handleViewportResize);
    document.removeEventListener(
      "pointerdown",
      recordStickyChromePointerDown,
      true
    );
    document.removeEventListener("focusin", handleGlobalFocusIn);
    if (headerResizeObserver) {
      headerResizeObserver.disconnect();
      headerResizeObserver = null;
    }
    clearShellStickyHeaderHeight();
  });

  return {
    isCommandPaletteOpen,
    hasActivatedCommandPalette,
    pendingChecklistOpen,
    hasActivatedChecklistDrawer,
    hasActivatedOnboardingTour,
    hasActivatedSignOutModal,
    statusAnnouncement,
    effectiveHeaderHeight,
    isSignOutConfirmOpen,
    focusSkipTarget,
    onMenuStateChange,
    handlePersonaChange,
    handleLogin,
    handleRegister,
    handleLogout,
    confirmSignOut,
    cancelSignOut,
    handleToggleChecklist,
    handleToggleContextualHelp,
    handleDemoReset,
  };
}
