<template>
  <div
    class="app-shell"
    :style="{ '--shell-header-height': `${headerHeight}px` }"
  >
    <a href="#main-content" class="skip-link">Skip to main content</a>

    <header
      ref="headerRef"
      class="app-header"
      :class="{ 'has-canvas-context': isStudiesRoute }"
    >
      <div class="header-left">
        <router-link
          :to="authStore.isAuthenticated ? studiesPath : '/login'"
          class="brand-link"
        >
          <span class="brand-mark" aria-hidden="true">
            <Activity :size="20" :stroke-width="1.75" aria-hidden="true" />
          </span>
          <div class="header-title-area">
            <h1>Cadence Clinical</h1>
          </div>
        </router-link>

        <nav
          v-if="authStore.isAuthenticated"
          id="primary-navigation"
          class="primary-nav"
          aria-label="Primary navigation"
        >
          <router-link
            v-if="showStudiesNav"
            id="nav-studies"
            :to="studiesPath"
            class="primary-nav-link"
            :class="{ active: isStudiesRoute }"
            :aria-current="isStudiesRoute ? 'page' : undefined"
          >
            Studies
          </router-link>
          <router-link
            v-if="showDataCollectionNav"
            id="nav-data-collection"
            :to="dataCollectionPath"
            class="primary-nav-link"
            :class="{ active: isDataCollectionRoute }"
            :aria-current="isDataCollectionRoute ? 'page' : undefined"
          >
            Data collection
          </router-link>
          <router-link
            v-if="siteReadinessPath"
            id="nav-site-readiness"
            :to="siteReadinessPath"
            class="primary-nav-link"
            :class="{ active: isSiteReadinessRoute }"
            :aria-current="isSiteReadinessRoute ? 'page' : undefined"
          >
            Site readiness
          </router-link>
          <router-link
            v-if="showSupportNav"
            id="nav-support"
            to="/tickets"
            class="primary-nav-link"
            :class="{ active: isSupportRoute }"
            :aria-current="isSupportRoute ? 'page' : undefined"
          >
            Support
          </router-link>
          <router-link
            v-if="showKnowledgeNav"
            id="nav-knowledge"
            to="/knowledge"
            class="primary-nav-link"
            :class="{ active: isKnowledgeRoute }"
            :aria-current="isKnowledgeRoute ? 'page' : undefined"
          >
            Knowledge
          </router-link>
        </nav>
      </div>

      <div
        ref="canvasContextHostRef"
        id="app-shell-canvas-context"
        class="shell-canvas-context"
      />

      <div class="header-controls">
        <div
          ref="canvasActionsHostRef"
          id="app-shell-canvas-actions"
          class="shell-canvas-actions"
        />

        <template v-if="authStore.isAuthenticated">
          <router-link
            v-if="authStore.activePlanBadge"
            id="shell-plan-badge"
            to="/plans"
            class="plan-status-badge"
            :class="{
              'is-expired': authStore.isTrialExpired,
              'is-trial': authStore.subscriptionStatus === 'Trialing',
            }"
            :title="PRE_RELEASE_ENVIRONMENT_NOTICE"
          >
            {{ authStore.activePlanBadge }}
          </router-link>

          <button
            id="btn-open-palette"
            type="button"
            class="command-palette-trigger"
            title="Open global search and command palette (Ctrl+K or Cmd+K)"
            aria-keyshortcuts="Control+K Meta+K"
            @click="isCommandPaletteOpen = true"
          >
            <Search :size="16" :stroke-width="1.75" aria-hidden="true" />
            <span class="palette-trigger-text">Search</span>
            <kbd class="palette-kbd" aria-hidden="true">⌘K</kbd>
          </button>

          <div
            v-if="availablePersonas.length > 0"
            class="persona-switcher-wrapper"
          >
            <label for="persona-select" class="persona-label">View</label>
            <select
              id="persona-select"
              :value="authStore.currentPersona"
              class="persona-select"
              title="Switch workspace view orientation and help emphasis"
              @change="handlePersonaChange"
            >
              <option
                v-for="persona in availablePersonas"
                :key="persona.key"
                :value="persona.key"
              >
                {{ persona.label }}
              </option>
            </select>
          </div>

          <button
            v-if="onboardingStore.totalTasksCount > 0"
            id="btn-onboarding-checklist-trigger"
            type="button"
            class="shell-control-btn"
            title="Open workspace orientation and setup checklist"
            @click="onboardingStore.toggleDrawer()"
          >
            <ListChecks :size="16" :stroke-width="1.75" aria-hidden="true" />
            <span class="shell-btn-label">Orientation</span>
            <span class="setup-progress-pill">
              {{ onboardingStore.completedTasksCount }}/{{
                onboardingStore.totalTasksCount
              }}
            </span>
          </button>

          <button
            id="btn-contextual-help-trigger"
            type="button"
            class="shell-control-btn"
            :aria-expanded="knowledgeStore.isOpen"
            title="Help & SOPs — Open contextual help and SOP guidance"
            aria-label="Help & SOPs — Open contextual help and SOP guidance"
            @click="knowledgeStore.toggleDrawer()"
          >
            <CircleHelp :size="16" :stroke-width="1.75" aria-hidden="true" />
            <span class="shell-btn-label">Help &amp; SOPs</span>
          </button>

          <div class="user-session-controls">
            <span class="user-badge">
              {{ authStore.identity?.username || "Signed in" }}
            </span>
            <button
              id="btn-logout"
              type="button"
              class="btn btn-sm btn-secondary shell-logout-btn"
              title="Sign out"
              @click="handleLogout"
            >
              <LogOut :size="16" :stroke-width="1.75" aria-hidden="true" />
              <span class="logout-label">Sign out</span>
            </button>
          </div>
        </template>

        <template v-else>
          <router-link
            id="nav-public-plans"
            to="/plans"
            class="shell-control-btn"
          >
            Plans
          </router-link>
          <button
            v-if="!authStore.isDemoMode"
            id="btn-register"
            type="button"
            class="btn btn-sm btn-secondary shell-auth-btn"
            @click="handleRegister"
          >
            Start trial
          </button>
          <button
            id="btn-login"
            type="button"
            class="btn btn-sm btn-primary shell-auth-btn"
            @click="handleLogin"
          >
            Sign in
          </button>
        </template>

        <button
          v-if="authStore.isAuthenticated"
          id="btn-mobile-menu-toggle"
          type="button"
          class="mobile-menu-toggle"
          aria-label="Toggle navigation menu"
          aria-controls="mobile-nav-menu"
          :aria-expanded="isMobileMenuOpen ? 'true' : 'false'"
          @click="isMobileMenuOpen = !isMobileMenuOpen"
        >
          <X
            v-if="isMobileMenuOpen"
            :size="20"
            :stroke-width="1.75"
            aria-hidden="true"
          />
          <Menu v-else :size="20" :stroke-width="1.75" aria-hidden="true" />
        </button>
      </div>

      <div
        v-if="authStore.isAuthenticated"
        id="mobile-nav-menu"
        class="mobile-nav-drawer"
        :class="{ 'is-open': isMobileMenuOpen }"
        aria-label="Mobile navigation"
      >
        <router-link
          v-if="showStudiesNav"
          :to="studiesPath"
          class="mobile-nav-link"
          @click="isMobileMenuOpen = false"
        >
          Studies
        </router-link>
        <router-link
          v-if="showDataCollectionNav"
          :to="dataCollectionPath"
          class="mobile-nav-link"
          @click="isMobileMenuOpen = false"
        >
          Data collection
        </router-link>
        <router-link
          v-if="siteReadinessPath"
          :to="siteReadinessPath"
          class="mobile-nav-link"
          @click="isMobileMenuOpen = false"
        >
          Site readiness
        </router-link>
        <router-link
          v-if="showSupportNav"
          to="/tickets"
          class="mobile-nav-link"
          @click="isMobileMenuOpen = false"
        >
          Support
        </router-link>
        <router-link
          v-if="showKnowledgeNav"
          to="/knowledge"
          class="mobile-nav-link"
          @click="isMobileMenuOpen = false"
        >
          Knowledge
        </router-link>
      </div>

      <div
        class="sr-only-status-alerts"
        role="status"
        aria-live="polite"
        aria-atomic="true"
      >
        {{ statusAnnouncement }}
      </div>
    </header>

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

    <main
      id="main-content"
      class="shell-main"
      :class="{ 'shell-main-canvas': isStudiesRoute }"
      tabindex="-1"
    >
      <slot />
    </main>

    <OnboardingTour v-if="showOnboardingTour" :active-tab="activeTab" />
    <OnboardingChecklistDrawer v-if="authStore.isAuthenticated" />
    <CommandPaletteOverlay
      :is-open="isCommandPaletteOpen && authStore.isAuthenticated"
      @close="isCommandPaletteOpen = false"
    />
    <SignOutConfirmModal
      id="shell-signout-confirm-modal"
      v-model="isSignOutConfirmOpen"
      @confirm="confirmSignOut"
      @cancel="cancelSignOut"
    />
  </div>
</template>

<script setup>
import {
  computed,
  defineAsyncComponent,
  nextTick,
  onMounted,
  onUnmounted,
  provide,
  ref,
  watch,
} from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  Activity,
  CircleHelp,
  ListChecks,
  LogOut,
  Menu,
  Search,
  X,
} from "lucide-vue-next";
import { SignOutConfirmModal, useSignOutGuard } from "ui";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/productCatalog.js";
import { useAuthStore } from "../stores/auth";
import { hasUserScopedDrafts } from "../utils/userScopedStorage.js";
import { useKnowledgeStore } from "../stores/knowledge";
import { useOnboardingStore } from "../stores/onboarding";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { isScreenAvailable } from "../router/availability";
import {
  buildDataCollectionPath,
  canAccessDataCollection,
  canAccessKnowledgeHub,
  canAccessSupportHub,
  resolveActiveWorkspaceScope,
  resolveSiteReadinessPath,
} from "../canvas/palette";
import OnboardingChecklistDrawer from "./OnboardingChecklistDrawer.vue";
import CommandPaletteOverlay from "./CommandPaletteOverlay.vue";

const OnboardingTour = defineAsyncComponent(
  () => import("./OnboardingTour.vue")
);

const canvasContextHostRef = ref(null);
const canvasActionsHostRef = ref(null);

provide("inAppShell", true);
provide("appShellCanvasContextHost", canvasContextHostRef);
provide("appShellCanvasActionsHost", canvasActionsHostRef);

const authStore = useAuthStore();
const knowledgeStore = useKnowledgeStore();
const onboardingStore = useOnboardingStore();
const canvasStore = useStudyCanvasStore();
const route = useRoute();
const router = useRouter();

const isCommandPaletteOpen = ref(false);
const isMobileMenuOpen = ref(false);
const statusAnnouncement = ref("");
const headerRef = ref(null);
const headerHeight = ref(52);
let headerResizeObserver = null;

function syncHeaderHeight() {
  const el = headerRef.value;
  if (!el || typeof el.getBoundingClientRect !== "function") return;
  const measured = Math.round(el.getBoundingClientRect().height);
  if (measured > 0) {
    headerHeight.value = measured;
  }
}

const availablePersonas = computed(() => authStore.availablePersonas || []);
const normalizedRoles = computed(() => authStore.normalizedRoles || []);

const workspaceScope = computed(() =>
  resolveActiveWorkspaceScope(route, canvasStore)
);
const activeStudyCode = computed(() => workspaceScope.value.studyCode);

const studiesPath = computed(() =>
  activeStudyCode.value
    ? `/studies/${encodeURIComponent(activeStudyCode.value)}`
    : "/studies"
);

const dataCollectionPath = computed(() =>
  buildDataCollectionPath({
    studyCode: workspaceScope.value.studyCode,
    siteId: workspaceScope.value.explicitSiteId,
    subjectCode: workspaceScope.value.subjectCode,
    enrollmentId: workspaceScope.value.enrollmentId,
  })
);

const showStudiesNav = computed(() => isScreenAvailable("study"));
const showDataCollectionNav = computed(() =>
  canAccessDataCollection(normalizedRoles.value)
);
const siteReadinessPath = computed(() =>
  resolveSiteReadinessPath(normalizedRoles.value, {
    studyCode: workspaceScope.value.studyCode,
    siteId: workspaceScope.value.siteId,
  })
);
const showSupportNav = computed(() =>
  canAccessSupportHub(normalizedRoles.value)
);
const showKnowledgeNav = computed(() =>
  canAccessKnowledgeHub(normalizedRoles.value)
);

const isStudiesRoute = computed(
  () =>
    route?.name === "study" ||
    Boolean(route?.path && route.path.startsWith("/studies"))
);
const isDataCollectionRoute = computed(
  () =>
    route?.name === "ecrf" ||
    route?.name === "observation" ||
    route?.path === "/ecrf"
);
const isSiteReadinessRoute = computed(
  () =>
    route?.name === "ctms" ||
    route?.name === "ssu" ||
    route?.path === "/ctms" ||
    route?.path === "/ssu"
);
const isSupportRoute = computed(
  () =>
    route?.name === "tickets" ||
    Boolean(route?.path && route.path.startsWith("/tickets"))
);
const isKnowledgeRoute = computed(
  () =>
    route?.name === "knowledge" ||
    Boolean(route?.path && route.path.startsWith("/knowledge"))
);

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
    route?.name !== "forbidden" &&
    route?.path !== "/forbidden" &&
    route?.name !== "unavailable" &&
    route?.path !== "/unavailable"
);

function handlePersonaChange(event) {
  const nextPersona = event?.target?.value;
  if (nextPersona) {
    authStore.switchPersona(nextPersona);
    const matched = availablePersonas.value.find((p) => p.key === nextPersona);
    statusAnnouncement.value = `Switched workspace view to ${matched?.label || nextPersona}`;
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
  },
});

function handleGlobalKeydown(event) {
  if (!authStore.isAuthenticated) return;
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    isCommandPaletteOpen.value = !isCommandPaletteOpen.value;
  }
}

watch(
  () => [authStore.isAuthenticated, authStore.identity?.tenantId],
  ([authenticated]) => {
    if (authenticated && typeof authStore.loadEntitlements === "function") {
      authStore.loadEntitlements();
    }
  }
);

watch(
  () => [
    authStore.isAuthenticated,
    authStore.activePlanBadge,
    isMobileMenuOpen.value,
  ],
  () => {
    nextTick(syncHeaderHeight);
  }
);

onMounted(() => {
  window.addEventListener("keydown", handleGlobalKeydown);
  syncHeaderHeight();
  if (typeof ResizeObserver !== "undefined" && headerRef.value) {
    headerResizeObserver = new ResizeObserver(() => syncHeaderHeight());
    headerResizeObserver.observe(headerRef.value);
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
  if (headerResizeObserver) {
    headerResizeObserver.disconnect();
    headerResizeObserver = null;
  }
});
</script>

<style scoped>
.app-shell {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--semantic-color-surface-muted);
  color: var(--primary);
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

.app-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: nowrap;
  gap: 8px;
  height: var(--semantic-shell-chrome-height);
  min-height: var(--semantic-shell-chrome-height);
  max-height: var(--semantic-shell-chrome-height);
  padding: 0 12px;
  background: var(--semantic-color-surface);
  border-bottom: 1px solid var(--semantic-color-border);
  box-shadow: none;
  position: sticky;
  top: 0;
  z-index: 30;
  box-sizing: border-box;
}

.header-left {
  display: flex;
  align-items: center;
  flex-wrap: nowrap;
  flex: 0 1 auto;
  gap: 8px;
  min-width: 0;
}

.brand-link {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: var(--semantic-touch-target-staff);
  min-width: var(--semantic-touch-target-staff);
  padding: 0 4px;
  border-radius: 6px;
  text-decoration: none;
  color: var(--semantic-color-text);
  white-space: nowrap;
  box-sizing: border-box;
}

.brand-mark {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--semantic-color-interactive-primary);
  line-height: 1;
  flex-shrink: 0;
}

.header-title-area h1 {
  margin: 0;
  font-size: 0.98rem;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--semantic-color-text);
  white-space: nowrap;
}

.primary-nav {
  display: flex;
  align-items: center;
  flex-wrap: nowrap;
  flex: 0 1 auto;
  min-width: 0;
  overflow-x: auto;
  scrollbar-width: none;
  gap: 4px;
}

.primary-nav::-webkit-scrollbar {
  display: none;
}

.primary-nav-link {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-touch-target-staff);
  height: var(--semantic-touch-target-staff);
  padding: 0 10px;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--semantic-color-text-body);
  text-decoration: none;
  border: 1px solid transparent;
  white-space: nowrap;
  box-sizing: border-box;
  transition:
    background-color 0.15s ease,
    color 0.15s ease,
    border-color 0.15s ease;
}

.primary-nav-link:hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.primary-nav-link.active {
  background: var(--semantic-color-info-bg);
  border-color: var(--semantic-color-info-border);
  color: var(--semantic-color-info);
  font-weight: 600;
}

.shell-canvas-context {
  display: flex;
  align-items: center;
  flex: 1 1 auto;
  min-width: 0;
  overflow-x: auto;
  scrollbar-width: none;
}

.shell-canvas-context::-webkit-scrollbar {
  display: none;
}

.shell-canvas-context:empty {
  display: none;
}

.shell-canvas-context :deep(.zoom-path) {
  flex: 1 1 auto;
  min-width: 0;
}

.shell-canvas-context :deep(.zoom-path ol) {
  display: flex;
  flex-wrap: nowrap;
  align-items: center;
  gap: 4px;
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.84rem;
  white-space: nowrap;
}

.shell-canvas-context :deep(.zoom-path li) {
  display: inline-flex;
  align-items: center;
  min-width: 0;
}

.shell-canvas-context :deep(.zoom-path li + li::before) {
  content: "/";
  margin-right: 4px;
  color: var(--semantic-color-text-secondary);
}

.shell-canvas-context :deep(.zoom-path a),
.shell-canvas-context :deep(.zoom-path [aria-current="page"]) {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-touch-target-staff);
  height: var(--semantic-touch-target-staff);
  padding: 0 4px;
  border-radius: 4px;
  color: var(--semantic-color-text);
  text-decoration: none;
  box-sizing: border-box;
}

.shell-canvas-context :deep(.zoom-path a:hover) {
  background: var(--semantic-color-surface-header);
  text-decoration: underline;
}

.shell-canvas-context :deep(.zoom-path [aria-current="page"]) {
  font-weight: 600;
}

.shell-canvas-actions {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

.shell-canvas-actions:empty {
  display: none;
}

.shell-canvas-actions :deep(.ai-toggle) {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-touch-target-staff);
  height: var(--semantic-touch-target-staff);
  padding: 0 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  font-size: 0.8rem;
  font-weight: 500;
  white-space: nowrap;
  cursor: pointer;
  box-sizing: border-box;
}

.shell-canvas-actions :deep(.ai-toggle[aria-pressed="true"]) {
  border-color: var(--semantic-color-ai-border);
  background: var(--semantic-color-ai-bg);
  color: var(--semantic-color-ai);
}

.header-controls {
  display: flex;
  align-items: center;
  flex-wrap: nowrap;
  flex-shrink: 0;
  gap: 6px;
  margin-left: auto;
}

.plan-status-badge {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-touch-target-staff);
  height: var(--semantic-touch-target-staff);
  padding: 0 10px;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 700;
  text-decoration: none;
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
  white-space: nowrap;
  box-sizing: border-box;
}

.plan-status-badge:hover {
  background: var(--semantic-color-info-light);
  border-color: var(--semantic-color-divider-accent);
}

.plan-status-badge.is-trial {
  background: var(--semantic-color-success-bg);
  border-color: var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.plan-status-badge.is-expired {
  background: var(--semantic-color-error-bg);
  border-color: var(--semantic-color-error-border);
  color: var(--semantic-color-error-dark);
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

.command-palette-trigger {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-height: var(--semantic-touch-target-staff);
  min-width: var(--semantic-touch-target-staff);
  height: var(--semantic-touch-target-staff);
  padding: 0 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-body);
  font-size: 0.84rem;
  font-weight: 500;
  white-space: nowrap;
  cursor: pointer;
  box-sizing: border-box;
}

.command-palette-trigger:hover {
  background: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.palette-kbd {
  font-family: var(--font-mono);
  font-size: 0.75rem;
  padding: 1px 5px;
  border-radius: 4px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
}

.persona-switcher-wrapper {
  display: inline-flex;
  align-items: center;
  flex-wrap: nowrap;
  gap: 6px;
  max-width: 100%;
  min-width: 0;
}

.persona-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  white-space: nowrap;
}

.persona-select {
  min-height: var(--semantic-touch-target-staff);
  height: var(--semantic-touch-target-staff);
  padding: 0 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: 1rem;
  max-width: 176px;
  min-width: 0;
  box-sizing: border-box;
}

.shell-control-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-height: var(--semantic-touch-target-staff);
  min-width: var(--semantic-touch-target-staff);
  height: var(--semantic-touch-target-staff);
  padding: 0 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-dark);
  font-size: 0.84rem;
  font-weight: 500;
  text-decoration: none;
  white-space: nowrap;
  cursor: pointer;
  box-sizing: border-box;
}

.shell-control-btn:hover {
  background: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
}

.setup-progress-pill {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 1px 6px;
  border-radius: 999px;
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
}

.user-session-controls {
  display: inline-flex;
  align-items: center;
  flex-wrap: nowrap;
  gap: 6px;
  padding-left: 6px;
  border-left: 1px solid var(--semantic-color-border);
  max-width: 100%;
  min-width: 0;
}

.shell-logout-btn,
.shell-auth-btn {
  min-height: var(--semantic-touch-target-staff);
  min-width: var(--semantic-touch-target-staff);
  height: var(--semantic-touch-target-staff);
  padding: 0 10px;
  white-space: nowrap;
}

.user-badge {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
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
  outline: none;
}

.shell-main.shell-main-canvas {
  max-width: none;
  padding: 0;
  overflow: visible;
}

.mobile-menu-toggle {
  display: none;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-touch-target-staff);
  min-width: var(--semantic-touch-target-staff);
  height: var(--semantic-touch-target-staff);
  width: var(--semantic-touch-target-staff);
  padding: 0;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  cursor: pointer;
  box-sizing: border-box;
}

.mobile-nav-drawer {
  display: none;
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  z-index: 40;
  flex-direction: column;
  gap: 4px;
  padding: 8px 12px;
  background: var(--semantic-color-surface);
  border-bottom: 1px solid var(--semantic-color-border);
  box-shadow: var(--shadow-md);
  box-sizing: border-box;
}

.mobile-nav-drawer.is-open {
  display: flex;
}

.mobile-nav-link {
  display: flex;
  align-items: center;
  min-height: var(--semantic-touch-target-staff);
  padding: 8px 12px;
  border-radius: 6px;
  color: var(--semantic-color-text-dark);
  text-decoration: none;
  font-weight: 500;
  box-sizing: border-box;
}

.mobile-nav-link:hover {
  background: var(--semantic-color-surface-header);
}

.sr-only-status-alerts {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

@media (max-width: 1439px) {
  .user-badge,
  .palette-kbd,
  .app-header.has-canvas-context .plan-status-badge {
    display: none;
  }

  .persona-label,
  .shell-btn-label,
  .palette-trigger-text,
  .logout-label,
  .app-header.has-canvas-context .header-title-area {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
  }

  .primary-nav-link {
    padding: 0 7px;
    font-size: 0.82rem;
  }

  .persona-select {
    max-width: 132px;
  }
}

@media (max-width: 1279px) {
  .primary-nav {
    display: none;
  }

  .mobile-menu-toggle {
    display: inline-flex;
  }
}

@media (max-width: 1179px) {
  .palette-trigger-text,
  .app-header.has-canvas-context .header-title-area {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
  }

  .persona-select {
    max-width: 132px;
  }
}

@media (max-width: 959px) {
  .header-title-area,
  .logout-label {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
  }

  .persona-select {
    max-width: 118px;
  }
}

@media (max-width: 767px) {
  .app-header {
    flex-wrap: wrap;
    height: auto;
    max-height: none;
    padding: 6px 8px;
    gap: 6px;
  }

  .header-controls {
    flex-wrap: wrap;
    min-width: 0;
    max-width: 100%;
    gap: 4px;
  }

  .shell-canvas-context {
    order: 3;
    flex: 1 1 100%;
    max-width: 100%;
  }

  .plan-status-badge {
    display: none;
  }

  .persona-select {
    max-width: 104px;
  }

  .user-session-controls {
    padding-left: 0;
    border-left: 0;
  }
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
