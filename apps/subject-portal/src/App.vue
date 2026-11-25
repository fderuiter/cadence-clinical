<template>
  <div id="app">
    <!-- Accessible Skip to Content Link -->
    <a href="#main-content" class="skip-link">Skip to main content</a>

    <!-- Persistent Demo Mode Synthetic Data Banner -->
    <DemoBanner />

    <!-- Portal Header with session trigger (< 1280px) & sync status button -->
    <PortalHeader />

    <!-- Global Floating Sync Drawer -->
    <SyncDrawer />

    <!-- Main Layout -->
    <div class="portal-container">
      <!-- Patient Navigation Tabs / 60px Tablet Icon Rail -->
      <PortalNavigationRail />

      <!-- Main Viewport -->
      <main id="main-content" class="portal-main" tabindex="-1">
        <!-- Inline expired-session / auth-error alert when session sheet is closed below 1280px -->
        <PortalSessionAlertBanner />

        <!-- Accessible study -> section -> record breadcrumb trail -->
        <ClinicalBreadcrumbs
          v-if="shouldShowBreadcrumbs"
          id="portal-breadcrumbs"
          :items="breadcrumbItems"
          compact-mobile
        />

        <!-- Urgent Re-Consent Notification Banner -->
        <ReconsentBanner />

        <!-- Version mismatch banner -->
        <SyncSuspendedBanner />

        <!--
          Every view stays mounted (the feature modules render into their
          elements); the ui store's currentView, which the router follows,
          picks the visible one.
        -->
        <component
          :is="route.component"
          v-for="route in viewRoutes"
          :key="route.name"
        />
        <UnavailableView />
      </main>
    </div>

    <!-- Session Drawer below 1280px (right slide-over on 768-1279px, bottom sheet < 768px) -->
    <PortalSessionSheet />

    <!-- Shared peek drawer & modals host -->
    <PortalPeekSheet />
    <SignatureModal />
    <ReconsentModal />
    <PinSetupModal />
    <PinUnlockModal />

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

<script setup>
import { ClinicalBreadcrumbs, ClinicalToastHost } from "ui";
import { computed, onBeforeUnmount } from "vue";

import DemoBanner from "./components/DemoBanner.vue";
import LedgerFooter from "./components/LedgerFooter.vue";
import PinSetupModal from "./components/PinSetupModal.vue";
import PinUnlockModal from "./components/PinUnlockModal.vue";
import PortalHeader from "./components/PortalHeader.vue";
import PortalNavigationRail from "./components/PortalNavigationRail.vue";
import PortalPeekSheet from "./components/PortalPeekSheet.vue";
import PortalSessionAlertBanner from "./components/PortalSessionAlertBanner.vue";
import PortalSessionSheet from "./components/PortalSessionSheet.vue";
import ReconsentBanner from "./components/ReconsentBanner.vue";
import ReconsentModal from "./components/ReconsentModal.vue";
import SignatureModal from "./components/SignatureModal.vue";
import SyncDrawer from "./components/SyncDrawer.vue";
import SyncSuspendedBanner from "./components/SyncSuspendedBanner.vue";
import { usePortalViewport } from "./composables/usePortalViewport.js";
import { showView } from "./features/navigation.js";
import { closePeekRecord } from "./features/peek.js";
import { flushActiveQuestionnaireDraft } from "./features/questionnaire.js";
import { isScreenAvailable, screenTitle } from "./router/availability.js";
import { routeForView, viewRoutes } from "./router/routes.js";
import { useConsentStore } from "./stores/consent.js";
import { useSessionStore } from "./stores/session.js";
import { useTasksStore } from "./stores/tasks.js";
import { useUiStore } from "./stores/ui.js";
import {
  formatConsentEditionLabel,
  resolveFriendlyStudyTitle,
} from "./utils/participantCopy.js";
import UnavailableView from "./views/UnavailableView.vue";
import { clearToasts, dismissToast, portalToasts } from "./widgets/toast.js";

const ui = useUiStore();
const sessionStore = useSessionStore();
const tasksStore = useTasksStore();
const consentStore = useConsentStore();
const { isNarrowBreadcrumbViewport } = usePortalViewport();

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
    closePeekRecord({ restoreFocus: false, syncUrl: true });
  }
  if (ui.currentView === "view-questionnaire") {
    void flushActiveQuestionnaireDraft({ clearActive: true });
  }
  showView(resolveRootStudyRoute().viewId);
}

function navigateToSection(sectionViewId) {
  if (ui.peekRecord) {
    closePeekRecord({ restoreFocus: false, syncUrl: true });
  }
  if (
    ui.currentView === "view-questionnaire" &&
    sectionViewId !== "view-questionnaire"
  ) {
    void flushActiveQuestionnaireDraft({ clearActive: true });
  }
  showView(sectionViewId);
}

const activeRecordLabel = computed(() => {
  if (ui.peekRecord?.title) {
    return ui.peekRecord.title;
  }
  if (ui.currentView === "view-questionnaire") {
    return (
      tasksStore.activeQuestionnaire?.instrument?.name ||
      tasksStore.activeQuestionnaire?.assignment?.instrument_name ||
      "Active Survey"
    );
  }
  if (
    ui.currentView === "view-consent" &&
    consentStore.consentContent?.version_index
  ) {
    return formatConsentEditionLabel(consentStore.consentContent.version_index);
  }
  return "";
});

const shouldShowBreadcrumbs = computed(
  () => !isNarrowBreadcrumbViewport.value || Boolean(activeRecordLabel.value)
);

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

  const recordLabel = activeRecordLabel.value;

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
      href: recordLabel ? sectionHref : undefined,
      current: !recordLabel,
      onSelect: recordLabel
        ? () => navigateToSection(sectionViewId)
        : undefined,
    },
  ];

  if (recordLabel) {
    items.push({
      id: "record",
      label: recordLabel,
      current: true,
    });
  }

  return items;
});

onBeforeUnmount(() => {
  clearToasts();
});
</script>
