<template>
  <div id="app">
    <!-- Accessible Skip to Content Link -->
    <a href="#main-content" class="skip-link">Skip to main content</a>

    <!-- Persistent Demo Mode Synthetic Data Banner -->
    <DemoBanner />

    <!-- Portal Header with the persistent sync status button -->
    <PortalHeader />

    <!-- Global Floating Sync Drawer -->
    <SyncDrawer v-if="hasOpenedSyncDrawer" />

    <!-- Main Layout -->
    <div class="portal-container">
      <!-- Patient Navigation Tabs -->
      <nav class="portal-navigation" aria-label="Participant navigation">
        <TabBar />

        <button
          id="btn-mobile-session-toggle"
          type="button"
          class="btn btn-secondary mobile-session-toggle"
          :aria-expanded="isMobileSessionExpanded ? 'true' : 'false'"
          aria-controls="participant-secondary-details"
          @click="isMobileSessionExpanded = !isMobileSessionExpanded"
        >
          <span>Participant Session</span>
          <span aria-hidden="true">{{
            isMobileSessionExpanded ? "Hide ▴" : "Details ▾"
          }}</span>
        </button>

        <!-- Active Participant Card -->
        <ParticipantCard :mobile-expanded="isMobileSessionExpanded" />
      </nav>

      <!-- Main Viewport -->
      <main id="main-content" class="portal-main" tabindex="-1">
        <!-- Accessible study -> section -> record breadcrumb trail -->
        <ClinicalBreadcrumbs id="portal-breadcrumbs" :items="breadcrumbItems" />

        <!-- Urgent Re-Consent Notification Banner -->
        <ReconsentBanner />

        <!-- Version mismatch banner -->
        <SyncSuspendedBanner />

        <!--
          Views mount on demand when visited so signed-out and production boots
          skip hidden view subtrees while visited imperative containers stay
          mounted during an active session.
        -->
        <template v-for="viewRoute in viewRoutes" :key="viewRoute.name">
          <component
            :is="viewRoute.component"
            v-if="shouldMountViewRoute(viewRoute)"
          />
        </template>
        <UnavailableView v-if="shouldMountUnavailableView" />
      </main>
    </div>

    <!-- Shared 90dvh peek bottom sheet & modals host (mounted on demand) -->
    <PortalPeekSheet v-if="shouldMountPeekSheet" />
    <SignatureModal v-if="hasOpenedSignatureModal" />
    <ReconsentModal v-if="Boolean(consentStore.reconsentModalOpen)" />
    <PinSetupModal v-if="Boolean(sessionStore.pinSetup?.isOpen)" />
    <PinUnlockModal v-if="Boolean(sessionStore.pinUnlock?.isOpen)" />

    <!-- Activity log at the bottom for patient transparency & 21 CFR compliance review -->
    <LedgerFooter />

    <!-- Shared accessible participant toast host -->
    <ClinicalToastHost
      data-portal-toast-host="true"
      :toasts="portalToasts"
      latest-toast-id="toast-container"
      aria-label="Participant notifications"
      @dismiss="dismissToast"
    />
  </div>
</template>

<script>
import { defineComponent } from "vue";
import { defineLazyOverlay } from "ui/portal-shell";

import { isDemoBuild } from "./utils/demoMode.js";

const EmptyOverlay = defineComponent({
  name: "EmptyOverlay",
  render: () => null,
});

const SyncDrawer = defineLazyOverlay(
  () => import("./components/SyncDrawer.vue")
);
const PortalPeekSheet = defineLazyOverlay(
  () => import("./components/PortalPeekSheet.vue")
);
const SignatureModal = defineLazyOverlay(
  () => import("./components/SignatureModal.vue")
);
const ReconsentModal =
  !isDemoBuild() && import.meta.env?.MODE !== "test"
    ? EmptyOverlay
    : defineLazyOverlay(() => import("./components/ReconsentModal.vue"));
const PinSetupModal = defineLazyOverlay(
  () => import("./components/PinSetupModal.vue")
);
const PinUnlockModal = defineLazyOverlay(
  () => import("./components/PinUnlockModal.vue")
);
</script>

<script setup>
import {
  computed,
  getCurrentInstance,
  inject,
  onBeforeUnmount,
  ref,
  watch,
} from "vue";
import { routeLocationKey } from "vue-router";
import { ClinicalBreadcrumbs, ClinicalToastHost } from "ui/portal-shell";

import DemoBanner from "./components/DemoBanner.vue";
import LedgerFooter from "./components/LedgerFooter.vue";
import ParticipantCard from "./components/ParticipantCard.vue";
import PortalHeader from "./components/PortalHeader.vue";
import ReconsentBanner from "./components/ReconsentBanner.vue";
import SyncSuspendedBanner from "./components/SyncSuspendedBanner.vue";
import TabBar from "./components/TabBar.vue";
import { showView } from "./features/navigation.js";
import { isScreenAvailable, screenTitle } from "./router/availability.js";
import { routeForView, viewRoutes } from "./router/routes.js";
import { useConsentStore } from "./stores/consent.js";
import { useSessionStore } from "./stores/session.js";
import { useSyncStore } from "./stores/sync.js";
import { useTasksStore } from "./stores/tasks.js";
import { useUiStore } from "./stores/ui.js";
import {
  formatConsentEditionLabel,
  resolveFriendlyStudyTitle,
} from "./utils/participantCopy.js";
import { hasPeekQueryParam } from "./utils/peekQuery.js";
import UnavailableView from "./views/UnavailableView.vue";
import { clearToasts, dismissToast, portalToasts } from "./widgets/toast.js";

const appInstance = getCurrentInstance();
const currentRoute = inject(routeLocationKey, null);
const ui = useUiStore();
const sessionStore = useSessionStore();
const syncStore = useSyncStore();
const tasksStore = useTasksStore();
const consentStore = useConsentStore();
const isMobileSessionExpanded = ref(false);
const hasOpenedSyncDrawer = ref(false);
const hasOpenedSignatureModal = ref(false);

const isSignedOutSession = computed(
  () =>
    !sessionStore.session?.userId &&
    !sessionStore.session?.token &&
    !sessionStore.session?.isDemoMode &&
    !sessionStore.session?.isOfflineMode
);

watch(
  () => Boolean(sessionStore.session?.isDemoMode),
  (nextDemo, prevDemo) => {
    if (prevDemo && !nextDemo && !isDemoBuild()) {
      ui.mountedViewIds = ui.mountedViewIds.filter(
        (id) => id === "view-wallet" || id === "view-unavailable"
      );
    }
  },
  { flush: "sync" }
);

watch(
  () => [
    Boolean(syncStore.isSyncDrawerOpen),
    syncStore.submissions?.length ?? 0,
  ],
  ([drawerOpen, submissionCount]) => {
    if (drawerOpen || submissionCount > 0) {
      const wasMounted = hasOpenedSyncDrawer.value;
      hasOpenedSyncDrawer.value = true;
      if (
        !wasMounted &&
        import.meta.env?.MODE === "test" &&
        typeof document !== "undefined" &&
        !document.getElementById("global-sync-drawer")
      ) {
        appInstance?.update?.();
      }
    }
  },
  { immediate: true, flush: "sync" }
);

watch(
  () => [
    Boolean(ui.signatureModalOpen),
    import.meta.env?.MODE === "test" && Boolean(ui.signatureModalPrimed),
  ],
  ([modalOpen, modalPrimed]) => {
    if (modalOpen || modalPrimed) {
      const wasMounted = hasOpenedSignatureModal.value;
      hasOpenedSignatureModal.value = true;
      if (
        !wasMounted &&
        import.meta.env?.MODE === "test" &&
        typeof document !== "undefined" &&
        !document.getElementById("portal-sign-modal")
      ) {
        appInstance?.update?.();
      }
    }
  },
  { immediate: true, flush: "sync" }
);

watch(
  isSignedOutSession,
  (nextSignedOut, prevSignedOut) => {
    if (nextSignedOut && prevSignedOut === false) {
      if (
        !syncStore.isSyncDrawerOpen &&
        (syncStore.submissions?.length ?? 0) === 0
      ) {
        hasOpenedSyncDrawer.value = false;
      }
      if (!ui.signatureModalOpen) {
        hasOpenedSignatureModal.value = false;
      }
    }
  },
  { flush: "sync" }
);

if (import.meta.env?.MODE === "test") {
  watch(
    () => ui.mountedViewIds.length,
    (nextLen, prevLen) => {
      if (
        nextLen > (prevLen ?? 0) &&
        typeof document !== "undefined" &&
        document.getElementById("app")
      ) {
        appInstance?.update?.();
      }
    },
    { flush: "sync" }
  );
}

watch(
  () => [ui.currentView, isSignedOutSession.value],
  ([activeViewId, signedOut]) => {
    if (signedOut && activeViewId === "view-unavailable") {
      if (ui.mountedViewIds.length > 0) {
        ui.mountedViewIds = [];
      }
      if (ui.signatureModalPrimed) {
        ui.signatureModalPrimed = false;
      }
      return;
    }
    if (!activeViewId) return;
    const matched = routeForView(activeViewId);
    const canMountRoute =
      activeViewId === "view-unavailable" ||
      (matched &&
        (isDemoBuild() ||
          import.meta.env?.MODE === "test" ||
          matched.name === "wallet"));
    if (canMountRoute && !ui.mountedViewIds.includes(activeViewId)) {
      ui.mountedViewIds.push(activeViewId);
    }
  },
  { immediate: true }
);

const shouldMountPeekSheet = computed(
  () => Boolean(ui.peekRecord) || hasPeekQueryParam(currentRoute?.query)
);

const shouldMountUnavailableView = computed(
  () =>
    ui.currentView === "view-unavailable" ||
    ui.mountedViewIds.includes("view-unavailable")
);

function shouldMountViewRoute(viewRoute) {
  if (
    !isDemoBuild() &&
    import.meta.env?.MODE !== "test" &&
    viewRoute.name !== "wallet"
  ) {
    return false;
  }
  if (isScreenAvailable(viewRoute.name)) {
    return true;
  }
  const viewId = viewRoute.meta.viewId;
  if (ui.currentView === viewId) {
    if (viewId === "view-tasks" && !isScreenAvailable("tasks")) {
      return ui.mountedViewIds.includes(viewId);
    }
    return true;
  }
  return ui.mountedViewIds.includes(viewId);
}

function resolveRootStudyRoute() {
  if (isScreenAvailable("tasks")) {
    return { viewId: "view-tasks", href: "/" };
  }
  if (isScreenAvailable("wallet")) {
    return { viewId: "view-wallet", href: "/wallet" };
  }
  return { viewId: "view-unavailable", href: "/unavailable" };
}

function navigateToRootStudy() {
  if (ui.peekRecord) {
    void import("./features/peek.js").then(({ closePeekRecord }) =>
      closePeekRecord({ restoreFocus: false, syncUrl: true })
    );
  }
  if (
    ui.currentView === "view-questionnaire" &&
    (isDemoBuild() || import.meta.env?.MODE === "test")
  ) {
    void import("./features/questionnaire.js").then(
      ({ flushActiveQuestionnaireDraft }) =>
        flushActiveQuestionnaireDraft({ clearActive: true })
    );
  }
  showView(resolveRootStudyRoute().viewId);
}

function navigateToSection(sectionViewId) {
  if (ui.peekRecord) {
    void import("./features/peek.js").then(({ closePeekRecord }) =>
      closePeekRecord({ restoreFocus: false, syncUrl: true })
    );
  }
  if (
    ui.currentView === "view-questionnaire" &&
    sectionViewId !== "view-questionnaire" &&
    (isDemoBuild() || import.meta.env?.MODE === "test")
  ) {
    void import("./features/questionnaire.js").then(
      ({ flushActiveQuestionnaireDraft }) =>
        flushActiveQuestionnaireDraft({ clearActive: true })
    );
  }
  showView(sectionViewId);
}

const breadcrumbItems = computed(() => {
  const studyLabel = resolveFriendlyStudyTitle(sessionStore.session?.studyId);
  const rootTarget = resolveRootStudyRoute();
  const currentView = ui.currentView || rootTarget.viewId;

  let sectionLabel = "My Tasks";
  let sectionViewId = "view-tasks";
  let sectionHref = "/";

  if (currentView === "view-questionnaire") {
    sectionLabel = "My Tasks";
    sectionViewId = "view-tasks";
    sectionHref = "/";
  } else if (currentView === "view-unavailable") {
    const rawUnavailable = ui.unavailableScreen;
    sectionLabel =
      typeof rawUnavailable === "string" && rawUnavailable
        ? screenTitle(rawUnavailable)
        : rawUnavailable?.title || "Unavailable";
    sectionViewId = "view-unavailable";
    sectionHref = "";
  } else {
    const matched = routeForView(currentView);
    if (matched?.meta?.tab?.label) {
      sectionLabel = matched.meta.tab.label;
      sectionViewId = matched.meta.viewId;
      sectionHref = matched.path;
    }
  }

  let activeRecordLabel = "";
  if (ui.peekRecord?.title) {
    activeRecordLabel = ui.peekRecord.title;
  } else if (currentView === "view-questionnaire") {
    activeRecordLabel =
      tasksStore.activeQuestionnaire?.instrument?.name ||
      tasksStore.activeQuestionnaire?.assignment?.instrument_name ||
      "Active Survey";
  } else if (
    currentView === "view-consent" &&
    consentStore.consentContent?.version_index
  ) {
    activeRecordLabel = formatConsentEditionLabel(
      consentStore.consentContent.version_index
    );
  }

  const items = [
    {
      id: "study",
      label: studyLabel,
      href: rootTarget.href,
      current: false,
      onSelect: () => navigateToRootStudy(),
    },
    {
      id: `section-${sectionViewId}`,
      label: sectionLabel,
      href: activeRecordLabel ? sectionHref : undefined,
      current: !activeRecordLabel,
      onSelect: activeRecordLabel
        ? () => navigateToSection(sectionViewId)
        : undefined,
    },
  ];

  if (activeRecordLabel) {
    items.push({
      id: "record",
      label: activeRecordLabel,
      current: true,
    });
  }

  return items;
});

onBeforeUnmount(() => {
  clearToasts();
});
</script>
