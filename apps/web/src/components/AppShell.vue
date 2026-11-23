<template>
  <div
    class="app-shell"
    :style="{ '--shell-header-height': `${headerHeight}px` }"
  >
    <a href="#main-content" class="skip-link">Skip to main content</a>

    <header
      ref="headerRef"
      class="app-header"
      :class="{ 'has-study-context': isStudyContextRoute }"
    >
      <div class="header-left">
        <router-link
          :to="authStore.isAuthenticated ? studiesPath : '/login'"
          class="brand-link"
        >
          <span class="brand-mark" aria-hidden="true">
            <Activity :size="18" :stroke-width="1.75" aria-hidden="true" />
          </span>
          <div
            class="header-title-area"
            :class="{ 'compact-on-study-mobile': isStudyContextRoute }"
          >
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

        <div
          id="shell-study-context"
          ref="shellStudyContextRef"
          class="shell-study-context"
        ></div>
      </div>

      <div class="header-controls">
        <template v-if="authStore.isAuthenticated">
          <router-link
            v-if="authStore.activePlanBadge"
            id="shell-plan-badge"
            to="/plans"
            class="plan-status-badge shell-desktop-only"
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
            <Search :size="15" :stroke-width="1.75" aria-hidden="true" />
            <span class="palette-trigger-text">Search</span>
            <kbd class="palette-kbd" aria-hidden="true">Ctrl+K</kbd>
          </button>

          <div
            v-if="availablePersonas.length > 0"
            class="persona-switcher-wrapper shell-desktop-only"
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
                {{ formatPersonaLabel(persona.label) }}
              </option>
            </select>
          </div>

          <button
            v-if="onboardingStore.totalTasksCount > 0"
            id="btn-onboarding-checklist-trigger"
            type="button"
            class="shell-control-btn shell-desktop-only"
            title="Open workspace orientation and setup checklist"
            @click="onboardingStore.toggleDrawer()"
          >
            <ListChecks :size="15" :stroke-width="1.75" aria-hidden="true" />
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
            class="shell-control-btn shell-tablet-up"
            :aria-expanded="knowledgeStore.isOpen"
            title="Open contextual help and SOP guidance"
            @click="knowledgeStore.toggleDrawer()"
          >
            <HelpCircle :size="15" :stroke-width="1.75" aria-hidden="true" />
            <span class="shell-btn-label">Help &amp; SOPs</span>
            <span class="sr-only">
              — Open contextual help and SOP guidance
            </span>
          </button>

          <div class="user-session-controls shell-tablet-up">
            <span class="user-badge">
              {{ authStore.identity?.username || "Signed in" }}
            </span>
            <button
              id="btn-logout"
              type="button"
              class="shell-control-btn shell-logout-btn"
              @click="handleLogout"
            >
              <LogOut :size="15" :stroke-width="1.75" aria-hidden="true" />
              <span class="logout-btn-label">Sign out</span>
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
            class="btn btn-sm btn-secondary shell-public-btn"
            @click="handleRegister"
          >
            Start trial
          </button>
          <button
            id="btn-login"
            type="button"
            class="btn btn-sm btn-primary shell-public-btn"
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
            :size="18"
            :stroke-width="1.75"
            aria-hidden="true"
          />
          <Menu v-else :size="18" :stroke-width="1.75" aria-hidden="true" />
        </button>
      </div>

      <div
        v-if="authStore.isAuthenticated"
        id="mobile-nav-menu"
        class="mobile-nav-drawer"
        :class="{ 'is-open': isMobileMenuOpen }"
        aria-label="Mobile navigation"
      >
        <div class="mobile-nav-links">
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

        <div class="mobile-nav-utilities">
          <button
            type="button"
            class="mobile-utility-btn"
            @click="
              isMobileMenuOpen = false;
              knowledgeStore.toggleDrawer();
            "
          >
            <HelpCircle :size="16" :stroke-width="1.75" aria-hidden="true" />
            <span>Help &amp; SOPs</span>
          </button>
          <button
            v-if="onboardingStore.totalTasksCount > 0"
            type="button"
            class="mobile-utility-btn"
            @click="
              isMobileMenuOpen = false;
              onboardingStore.toggleDrawer();
            "
          >
            <ListChecks :size="16" :stroke-width="1.75" aria-hidden="true" />
            <span>
              Orientation ({{ onboardingStore.completedTasksCount }}/{{
                onboardingStore.totalTasksCount
              }})
            </span>
          </button>
          <button
            type="button"
            class="mobile-utility-btn"
            @click="
              isMobileMenuOpen = false;
              handleLogout();
            "
          >
            <LogOut :size="16" :stroke-width="1.75" aria-hidden="true" />
            <span>
              Sign out ({{ authStore.identity?.username || "Signed in" }})
            </span>
          </button>
        </div>
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
      :class="{ 'shell-main-canvas': isFullBleedWorkspaceRoute }"
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
  HelpCircle,
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

const shellStudyContextRef = ref(null);
provide("inAppShell", true);
provide("shellStudyContextEl", shellStudyContextRef);

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
const headerHeight = ref(48);
let headerResizeObserver = null;

function syncHeaderHeight() {
  const el = headerRef.value;
  if (!el || typeof el.getBoundingClientRect !== "function") return;
  const measured = Math.round(el.getBoundingClientRect().height);
  if (measured > 0 && measured <= 52) {
    headerHeight.value = measured;
  } else if (measured > 52) {
    headerHeight.value = 52;
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

const isStudyContextRoute = computed(() => isStudiesRoute.value);

const isFullBleedWorkspaceRoute = computed(
  () =>
    isStudiesRoute.value ||
    route?.name === "etmf" ||
    route?.name === "documents" ||
    route?.name === "knowledge" ||
    Boolean(
      route?.path &&
      (route.path.startsWith("/etmf") ||
        route.path.startsWith("/documents") ||
        route.path.startsWith("/knowledge"))
    )
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

function formatPersonaLabel(label) {
  return String(label || "")
    .replace(/[\p{Extended_Pictographic}\uFE0F\u200D]+/gu, "")
    .replace(/\s+/g, " ")
    .trim();
}

function handlePersonaChange(event) {
  const nextPersona = event?.target?.value;
  if (nextPersona) {
    authStore.switchPersona(nextPersona);
    const matched = availablePersonas.value.find((p) => p.key === nextPersona);
    statusAnnouncement.value = `Switched workspace view to ${formatPersonaLabel(matched?.label || nextPersona)}`;
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
    route?.fullPath,
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
  top: 6px;
  left: 12px;
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
  height: var(--shell-header-height, 48px);
  max-height: var(--shell-header-max-height, 52px);
  min-height: 44px;
  padding: 0 12px;
  background: var(--semantic-color-surface);
  border-bottom: 1px solid var(--semantic-color-border);
  position: sticky;
  top: 0;
  z-index: 30;
  box-sizing: border-box;
}

.header-left {
  display: flex;
  align-items: center;
  flex-wrap: nowrap;
  gap: 10px;
  flex: 1 1 auto;
  min-width: 0;
}

.brand-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: var(--touch-hit-area-min, 44px);
  text-decoration: none;
  color: var(--semantic-color-text);
  flex-shrink: 0;
}

.brand-mark {
  color: var(--semantic-color-interactive-primary);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 6px;
  background: var(--semantic-color-info-bg);
  flex-shrink: 0;
}

.header-title-area h1 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--semantic-color-text);
  white-space: nowrap;
}

.primary-nav {
  display: flex;
  align-items: center;
  flex-wrap: nowrap;
  gap: 2px;
  flex-shrink: 0;
}

.primary-nav-link {
  display: inline-flex;
  align-items: center;
  min-height: var(--touch-hit-area-min, 44px);
  padding: 0 9px;
  border-radius: 6px;
  font-size: 0.83rem;
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

.shell-study-context {
  display: flex;
  align-items: center;
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
}

.shell-study-context:empty {
  display: none;
}

.header-controls {
  display: flex;
  align-items: center;
  flex-wrap: nowrap;
  gap: 6px;
  flex-shrink: 0;
}

.plan-status-badge {
  display: inline-flex;
  align-items: center;
  min-height: 32px;
  max-width: 130px;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.74rem;
  font-weight: 700;
  text-decoration: none;
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
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
  padding: 8px 20px;
  background: var(--semantic-color-error-bg);
  border-bottom: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-dark);
  font-size: 0.84rem;
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
  min-height: var(--touch-hit-area-min, 44px);
  height: 36px;
  padding: 0 9px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-body);
  font-size: 0.82rem;
  font-weight: 500;
  cursor: pointer;
  white-space: nowrap;
  box-sizing: border-box;
}

.command-palette-trigger:hover {
  background: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.palette-kbd {
  font-family: var(--font-mono);
  font-size: 0.7rem;
  padding: 1px 4px;
  border-radius: 4px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
}

.persona-switcher-wrapper {
  display: inline-flex;
  align-items: center;
  flex-wrap: nowrap;
  gap: 5px;
  min-width: 0;
}

.persona-label {
  font-size: 0.76rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  white-space: nowrap;
}

.persona-select {
  height: 36px;
  min-height: var(--touch-hit-area-min, 44px);
  padding: 0 6px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: 0.78rem;
  max-width: 128px;
  min-width: 0;
  box-sizing: border-box;
}

.shell-control-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 5px;
  min-height: var(--touch-hit-area-min, 44px);
  height: 36px;
  padding: 0 9px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-dark);
  font-size: 0.8rem;
  font-weight: 500;
  cursor: pointer;
  white-space: nowrap;
  text-decoration: none;
  box-sizing: border-box;
}

.shell-control-btn:hover {
  background: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
}

.shell-public-btn {
  min-height: var(--touch-hit-area-min, 44px) !important;
  height: 36px;
  display: inline-flex;
  align-items: center;
  white-space: nowrap;
}

.setup-progress-pill {
  font-size: 0.72rem;
  font-weight: 600;
  padding: 1px 5px;
  border-radius: 999px;
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
  font-variant-numeric: tabular-nums;
}

.user-session-controls {
  display: inline-flex;
  align-items: center;
  flex-wrap: nowrap;
  gap: 6px;
  padding-left: 6px;
  border-left: 1px solid var(--semantic-color-border);
  min-width: 0;
}

.user-badge {
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
  max-width: 110px;
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
  min-width: var(--touch-hit-area-min, 44px);
  min-height: var(--touch-hit-area-min, 44px);
  padding: 0;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  cursor: pointer;
  flex-shrink: 0;
}

.mobile-nav-drawer {
  display: none;
  position: fixed;
  top: var(--shell-header-height, 48px);
  left: 0;
  right: 0;
  max-height: calc(100dvh - var(--shell-header-height, 48px));
  overflow-y: auto;
  background: var(--semantic-color-surface);
  border-bottom: 1px solid var(--semantic-color-border);
  box-shadow: var(--shadow-lg);
  padding: 12px 16px;
  flex-direction: column;
  gap: 10px;
  z-index: 40;
  box-sizing: border-box;
}

.mobile-nav-drawer.is-open {
  display: flex;
}

.mobile-nav-links,
.mobile-nav-utilities {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.mobile-nav-utilities {
  padding-top: 8px;
  border-top: 1px solid var(--semantic-color-border);
}

.mobile-nav-link,
.mobile-utility-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: var(--touch-hit-area-min, 44px);
  padding: 8px 12px;
  border-radius: 6px;
  border: 1px solid transparent;
  background: transparent;
  color: var(--semantic-color-text-dark);
  text-decoration: none;
  font: inherit;
  font-size: 0.9rem;
  font-weight: 500;
  text-align: left;
  cursor: pointer;
}

.mobile-nav-link:hover,
.mobile-utility-btn:hover {
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

@media (max-width: 1400px) {
  .persona-label,
  .user-badge,
  .shell-btn-label {
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

  .shell-control-btn {
    min-width: var(--touch-hit-area-min, 44px);
    padding: 0 8px;
  }
}

@media (max-width: 1180px) {
  .primary-nav,
  .shell-desktop-only {
    display: none;
  }

  .mobile-menu-toggle {
    display: inline-flex;
  }
}

@media (max-width: 900px) {
  .palette-kbd,
  .palette-trigger-text,
  .logout-btn-label {
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

  .command-palette-trigger,
  .shell-logout-btn {
    min-width: var(--touch-hit-area-min, 44px);
    padding: 0;
  }
}

@media (max-width: 640px) {
  .app-header {
    padding: 0 8px;
    gap: 4px;
  }

  .shell-tablet-up {
    display: none;
  }

  .header-title-area.compact-on-study-mobile {
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

  .shell-expired-banner {
    padding: 8px 12px;
  }

  .shell-expired-cta {
    white-space: normal;
  }

  .shell-main:not(.shell-main-canvas) {
    padding: 14px 12px;
  }
}
</style>
