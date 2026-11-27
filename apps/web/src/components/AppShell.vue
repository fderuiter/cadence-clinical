<template>
  <div
    class="app-shell"
    :style="{ '--shell-header-height': `${effectiveHeaderHeight}px` }"
  >
    <a
      v-if="hasStudyWorkspace"
      id="skip-to-workspace"
      href="#canvas-workspace"
      class="skip-link"
      @click.prevent="focusSkipTarget('canvas-workspace')"
      >Skip to workspace</a
    >
    <a
      href="#main-content"
      class="skip-link"
      @click.prevent="focusSkipTarget('main-content')"
      >Skip to main content</a
    >

    <div
      v-if="isDemoBuild()"
      id="staff-demo-banner"
      class="shell-demo-banner"
      role="region"
      aria-label="Interactive demo navigation"
    >
      <p class="shell-demo-banner-copy">
        <strong>Demo Mode:</strong> Synthetic clinical workspace data. Changes
        stay in this browser session.
      </p>
      <nav class="shell-demo-banner-links" aria-label="Co-hosted demo surfaces">
        <a
          id="staff-demo-link-docs"
          href="/cadence-clinical/"
          class="shell-demo-banner-link"
        >
          Documentation Portal
        </a>
        <span class="shell-demo-banner-sep" aria-hidden="true">&middot;</span>
        <a
          id="staff-demo-link-participant"
          href="/cadence-clinical/subject-portal/"
          class="shell-demo-banner-link"
        >
          Participant Demo
        </a>
      </nav>
    </div>

    <AppShellHeader
      ref="shellHeaderCompRef"
      :workspace-scope="workspaceScope"
      :active-study-code="activeStudyCode"
      :is-canvas-route="isCanvasRoute"
      :is-conduct-canvas-route="isConductCanvasRoute"
      :onboarding-summary="onboardingSummary"
      :pending-checklist-open="pendingChecklistOpen"
      :is-contextual-help-open="isContextualHelpOpen"
      :status-announcement="statusAnnouncement"
      @action="onHeaderAction"
    >
      <template #theme-toggle>
        <ClinicalThemeToggle
          id="btn-theme-toggle"
          density="compact"
          :show-label="false"
        />
      </template>
    </AppShellHeader>

    <div
      v-if="
        authStore.isAuthenticated &&
        (authStore.isTrialExpired || authStore.subscriptionExpiredNotice)
      "
      id="shell-expired-subscription-banner"
      class="shell-expired-banner"
      role="region"
      aria-label="Workspace subscription expiry notice"
    >
      <div class="shell-expired-copy">
        <strong>Read &amp; Export mode active:</strong>
        <span>
          {{
            authStore.subscriptionExpiredNotice ||
            "Your 30-day workspace trial has ended. Clinical reads, regulated exports, and electronic signatures remain available while new mutations are paused."
          }}
        </span>
      </div>
      <router-link
        id="shell-expired-upgrade-link"
        to="/plans#scoped-upgrade-intake"
        class="btn btn-sm btn-primary shell-expired-cta"
      >
        Complete Study Scoping &amp; Activate
      </router-link>
    </div>

    <ClinicalContextBar
      v-if="showContextBar"
      id="shell-context-bar"
      aria-label="Clinical context"
      :study-code="activeStudyCode"
      :workspace-scope="workspaceScope"
      :route-name="String(route?.name || '')"
      :route-path="String(route?.path || '')"
    />

    <main
      id="main-content"
      class="shell-main"
      :class="{ 'shell-main-canvas': isCanvasRoute }"
      tabindex="-1"
    >
      <slot />
    </main>

    <OnboardingTour
      v-if="showOnboardingTour && hasActivatedOnboardingTour"
      :active-tab="activeTab"
    />
    <OnboardingChecklistDrawer
      v-if="authStore.isAuthenticated && hasActivatedChecklistDrawer"
      :open-on-mount="pendingChecklistOpen"
      @mounted-open="pendingChecklistOpen = false"
    />
    <CommandPaletteOverlay
      v-if="authStore.isAuthenticated && hasActivatedCommandPalette"
      :is-open="isCommandPaletteOpen && authStore.isAuthenticated"
      @close="isCommandPaletteOpen = false"
    />
    <SignOutConfirmModal
      v-if="hasActivatedSignOutModal"
      id="shell-signout-confirm-modal"
      v-model="isSignOutConfirmOpen"
      @confirm="confirmSignOut"
      @cancel="cancelSignOut"
    />
  </div>
</template>

<script>
import { defineLazyOverlay } from "../utils/lazyOverlay.js";

const ClinicalContextBar = defineLazyOverlay(
  () => import("./shell/LazyClinicalContextBar.js")
);
const SignOutConfirmModal = defineLazyOverlay(
  () => import("./shell/LazySignOutConfirmModal.js")
);
const OnboardingTour = defineLazyOverlay(() => import("./OnboardingTour.vue"));
const OnboardingChecklistDrawer = defineLazyOverlay(
  () => import("./OnboardingChecklistDrawer.vue")
);
const CommandPaletteOverlay = defineLazyOverlay(
  () => import("./CommandPaletteOverlay.vue")
);
</script>

<script setup>
import { computed, defineAsyncComponent, provide, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { useAuthStore } from "../stores/auth";
import {
  readPassiveOnboardingSummary,
  readPassiveOnboardingTourActive,
  resolveStorePinia,
} from "../stores/onboardingSummary.js";
import { isDemoBuild } from "../utils/demoMode.js";
import AppShellHeader from "./shell/AppShellHeader.vue";
import { useAppShellWorkspaceNavigation } from "./shell/appShellScope.js";
import { useAppShellChromeAndSession } from "./shell/useAppShellChromeAndSession.js";

const ClinicalThemeToggle = defineAsyncComponent(
  () => import("./ShellThemeToggle.vue")
);

provide("inAppShell", true);

const authStore = useAuthStore();
const pinia = resolveStorePinia(authStore);
const route = useRoute();
const router = useRouter();
const shellHeaderCompRef = ref(null);

const onboardingSummary = computed(() =>
  readPassiveOnboardingSummary(resolveStorePinia(authStore) || pinia, authStore)
);
const isOnboardingTourActive = computed(() =>
  readPassiveOnboardingTourActive(
    resolveStorePinia(authStore) || pinia,
    authStore
  )
);
const isContextualHelpOpen = computed(() =>
  Boolean(
    (resolveStorePinia(authStore) || pinia)?.state?.value?.knowledge?.isOpen
  )
);
const passiveCanvasState = computed(
  () =>
    (resolveStorePinia(authStore) || pinia)?.state?.value?.studyCanvas || null
);

const {
  normalizedRoles,
  workspaceScope,
  activeStudyCode,
  isCanvasRoute,
  hasStudyWorkspace,
  isConductCanvasRoute,
  showContextBar,
  activeTab,
  showOnboardingTour,
} = useAppShellWorkspaceNavigation({
  route,
  authStore,
  passiveCanvasState,
  isOnboardingTourActive,
});

const {
  cancelSignOut,
  confirmSignOut,
  effectiveHeaderHeight,
  focusSkipTarget,
  handleDemoReset,
  handleLogin,
  handleLogout,
  handlePersonaChange,
  handleRegister,
  handleToggleChecklist,
  handleToggleContextualHelp,
  hasActivatedChecklistDrawer,
  hasActivatedCommandPalette,
  hasActivatedOnboardingTour,
  hasActivatedSignOutModal,
  isCommandPaletteOpen,
  isSignOutConfirmOpen,
  onMenuStateChange,
  pendingChecklistOpen,
  statusAnnouncement,
} = useAppShellChromeAndSession({
  authStore,
  pinia,
  route,
  router,
  shellHeaderCompRef,
  normalizedRoles,
  onboardingSummary,
  isOnboardingTourActive,
  showOnboardingTour,
});

function onHeaderAction(kind, payload) {
  if (kind === "palette") isCommandPaletteOpen.value = true;
  else if (kind === "persona") void handlePersonaChange(payload);
  else if (kind === "checklist") handleToggleChecklist();
  else if (kind === "help") handleToggleContextualHelp();
  else if (kind === "logout") handleLogout();
  else if (kind === "reset") handleDemoReset(payload);
  else if (kind === "register") void handleRegister();
  else if (kind === "login") void handleLogin();
  else if (kind === "menu") onMenuStateChange();
}
</script>

<style scoped>
.app-shell {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--semantic-color-surface-muted);
  color: var(--text-main);
}

.skip-link {
  position: absolute;
  left: -9999px;
  top: auto;
  width: 1px;
  height: 1px;
  overflow: hidden;
}

.skip-link:focus {
  position: fixed;
  top: 8px;
  left: 16px;
  width: auto;
  height: auto;
  padding: 8px 14px;
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border-radius: 6px;
  font-weight: 600;
  text-decoration: none;
  z-index: 100;
}

.shell-demo-banner {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px 16px;
  padding: 6px 20px;
  background-color: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
  border-bottom: 1px solid var(--semantic-color-warning);
  font-size: 0.8125rem;
  font-weight: 600;
}

.shell-demo-banner-copy {
  margin: 0;
}

.shell-demo-banner-links {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.shell-demo-banner-link {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 2px 6px;
  border-radius: var(--semantic-radius-sm, 6px);
  color: var(--semantic-color-warning);
  font-weight: 700;
  text-decoration: underline;
  text-underline-offset: 2px;
  white-space: nowrap;
}

.shell-demo-banner-link:hover {
  background-color: var(--semantic-color-warning-bg-badge);
}

.shell-demo-banner-sep {
  color: var(--semantic-color-warning);
}

.shell-expired-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
  padding: 10px 24px;
  background: var(--semantic-color-error-bg);
  border-bottom: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-dark);
  font-size: 0.86rem;
}

.shell-expired-copy {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: baseline;
}

.shell-expired-cta {
  text-decoration: none;
  white-space: nowrap;
}

.shell-main {
  flex: 1;
  width: 100%;
  min-width: 0;
  max-width: 1440px;
  margin: 0 auto;
  padding: 20px 24px;
  box-sizing: border-box;
}

.shell-main:focus:not(:focus-visible) {
  outline: none;
}

.shell-main.shell-main-canvas {
  max-width: none;
  padding: 0;
  overflow: visible;
}

@media (max-width: 640px) {
  .shell-expired-banner {
    padding: 10px 12px;
  }

  .shell-expired-cta {
    white-space: normal;
  }

  .shell-main:not(.shell-main-canvas) {
    padding: 16px 12px;
  }
}
</style>
