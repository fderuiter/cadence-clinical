<template>
  <div id="app">
    <!-- Accessible Skip to Content Link -->
    <a href="#main-content" class="skip-link">Skip to main content</a>

    <!-- Persistent Demo Mode Synthetic Data Banner -->
    <DemoBanner />

    <!-- Portal Header with the persistent sync status button -->
    <PortalHeader />

    <!-- Global Floating Sync Drawer -->
    <SyncDrawer />

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

    <!-- Shared 90dvh peek bottom sheet & modals host -->
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
import { computed, onBeforeUnmount, ref } from "vue";
import { ClinicalBreadcrumbs, ClinicalToastHost } from "ui";

import DemoBanner from "./components/DemoBanner.vue";
import LedgerFooter from "./components/LedgerFooter.vue";
import ParticipantCard from "./components/ParticipantCard.vue";
import PinSetupModal from "./components/PinSetupModal.vue";
import PinUnlockModal from "./components/PinUnlockModal.vue";
import PortalHeader from "./components/PortalHeader.vue";
import PortalPeekSheet from "./components/PortalPeekSheet.vue";
import ReconsentBanner from "./components/ReconsentBanner.vue";
import ReconsentModal from "./components/ReconsentModal.vue";
import SignatureModal from "./components/SignatureModal.vue";
import SyncDrawer from "./components/SyncDrawer.vue";
import SyncSuspendedBanner from "./components/SyncSuspendedBanner.vue";
import TabBar from "./components/TabBar.vue";
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
const isMobileSessionExpanded = ref(false);

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
