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
      <nav
        class="portal-navigation"
        :class="{
          'is-tablet-rail': isTabletRailViewport,
          'is-rail-expanded': isTabletRailViewport && ui.railOverlayExpanded,
        }"
        aria-label="Participant navigation"
      >
        <div
          v-if="isTabletRailViewport && ui.railOverlayExpanded"
          class="portal-rail-scrim"
          aria-hidden="true"
          @click="ui.railOverlayExpanded = false"
        />

        <div class="portal-rail-surface">
          <button
            v-if="isTabletRailViewport"
            id="btn-portal-rail-toggle"
            type="button"
            class="btn btn-secondary portal-rail-toggle-btn"
            :aria-expanded="ui.railOverlayExpanded ? 'true' : 'false'"
            :aria-label="
              ui.railOverlayExpanded
                ? 'Collapse navigation rail'
                : 'Expand navigation rail'
            "
            :title="
              ui.railOverlayExpanded
                ? 'Collapse navigation rail'
                : 'Expand navigation rail'
            "
            @click="ui.railOverlayExpanded = !ui.railOverlayExpanded"
          >
            <component
              :is="ui.railOverlayExpanded ? ChevronsLeft : ChevronsRight"
              :size="16"
              :stroke-width="1.75"
              aria-hidden="true"
            />
            <span v-if="ui.railOverlayExpanded" class="rail-toggle-label">
              Collapse
            </span>
          </button>

          <TabBar
            :rail-collapsed="isTabletRailViewport && !ui.railOverlayExpanded"
            @select-tab="ui.railOverlayExpanded = false"
          />

          <!-- Inline Participant Card on wide desktop (>= 1280px) -->
          <ParticipantCard v-if="isWideDesktopViewport" mobile-expanded />
        </div>
      </nav>

      <!-- Main Viewport -->
      <main id="main-content" class="portal-main" tabindex="-1">
        <!-- Inline expired-session / auth-error alert when session sheet is closed below 1280px -->
        <div
          v-if="showInlineSessionAlert"
          class="inline-session-alert-banner"
          role="alert"
        >
          <div class="inline-session-alert-copy">
            <ClinicalIcon name="alert-circle" :size="18" />
            <span
              v-if="sessionStore.authError"
              id="session-auth-error"
              class="inline-session-alert-text"
            >
              {{ sessionStore.authError }}
            </span>
            <span
              v-else
              id="session-expired-banner"
              class="inline-session-alert-text"
            >
              Your session has expired. Sign in again to continue — your local
              drafts are preserved on this device.
            </span>
          </div>
          <button
            type="button"
            class="btn btn-primary inline-session-alert-btn"
            :data-testid="
              isSessionExpired ? 'reauthenticate-btn' : 'signin-btn'
            "
            @click="login"
          >
            {{ isSessionExpired ? "Sign In to Continue" : "Sign In" }}
          </button>
        </div>

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
    <ClinicalDrawer
      v-if="!isWideDesktopViewport && ui.sessionDrawerOpen"
      id="participant-session-drawer"
      v-model="ui.sessionDrawerOpen"
      overlay-id="participant-session-overlay"
      :position="isMobileViewport ? 'bottom' : 'right'"
      size="sm"
      mobile-bottom-sheet
      title="Participant Session"
      close-label="Close participant session"
    >
      <ParticipantCard mobile-expanded in-drawer />
    </ClinicalDrawer>
    <div
      v-else-if="!isWideDesktopViewport"
      id="participant-session-drawer"
      hidden
      inert
      aria-hidden="true"
    >
      <ParticipantCard
        mobile-expanded
        in-drawer
        :suppress-inline-alerts="showInlineSessionAlert"
      />
    </div>

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
import { ChevronsLeft, ChevronsRight } from "lucide-vue-next";
import {
  ClinicalBreadcrumbs,
  ClinicalDrawer,
  ClinicalIcon,
  ClinicalToastHost,
  useEscapeClose,
} from "ui";
import { computed, onBeforeUnmount, watch } from "vue";

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
import { usePortalViewport } from "./composables/usePortalViewport.js";
import { login } from "./features/auth.js";
import { showView } from "./features/navigation.js";
import { closePeekRecord } from "./features/peek.js";
import { flushActiveQuestionnaireDraft } from "./features/questionnaire.js";
import { isScreenAvailable, screenTitle } from "./router/availability.js";
import { routeForView, viewRoutes } from "./router/routes.js";
import { useConsentStore } from "./stores/consent.js";
import { useSessionStore } from "./stores/session.js";
import { useTasksStore } from "./stores/tasks.js";
import { useUiStore } from "./stores/ui.js";
import { isDemoModeActive } from "./utils/demoMode.js";
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

const {
  isNarrowBreadcrumbViewport,
  isMobileViewport,
  isTabletRailViewport,
  isWideDesktopViewport,
} = usePortalViewport();

useEscapeClose(
  () => {
    ui.railOverlayExpanded = false;
  },
  {
    isOpen: () => isTabletRailViewport.value && ui.railOverlayExpanded,
  }
);

watch(isTabletRailViewport, (isRail) => {
  if (!isRail && ui.railOverlayExpanded) {
    ui.railOverlayExpanded = false;
  }
});

watch(isWideDesktopViewport, (isWide) => {
  if (isWide && ui.sessionDrawerOpen) {
    ui.sessionDrawerOpen = false;
  }
});

const isDemo = computed(() => {
  void sessionStore.session?.isDemoMode;
  return isDemoModeActive();
});

const isSessionExpired = computed(
  () =>
    !sessionStore.session?.token &&
    !isDemo.value &&
    Boolean(sessionStore.session?.sessionExpired)
);

const showInlineSessionAlert = computed(
  () =>
    !isWideDesktopViewport.value &&
    !ui.sessionDrawerOpen &&
    (Boolean(sessionStore.authError) || isSessionExpired.value)
);

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
