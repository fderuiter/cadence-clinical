<template>
  <div
    class="app-shell"
    :style="{ '--shell-header-height': `${headerHeight}px` }"
  >
    <a href="#main-content" class="skip-link">Skip to main content</a>

    <header ref="headerRef" class="app-header">
      <div class="header-left">
        <router-link
          :to="authStore.isAuthenticated ? studiesPath : '/login'"
          class="brand-link"
        >
          <span class="brand-mark" aria-hidden="true">◆</span>
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
            to="/ecrf"
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
        </nav>
      </div>

      <div class="header-controls">
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
            <span class="palette-trigger-text">Search</span>
            <kbd class="palette-kbd">⌘K</kbd>
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
            <span>Orientation</span>
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
            aria-label="Help & SOPs — Open contextual help and SOP guidance"
            @click="knowledgeStore.toggleDrawer()"
          >
            Help &amp; SOPs
          </button>

          <div class="user-session-controls">
            <span class="user-badge">
              {{ authStore.identity?.username || "Signed in" }}
            </span>
            <button
              id="btn-logout"
              type="button"
              class="btn btn-sm btn-secondary"
              @click="handleLogout"
            >
              Sign out
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
            class="btn btn-sm btn-secondary"
            @click="handleRegister"
          >
            Start trial
          </button>
          <button
            id="btn-login"
            type="button"
            class="btn btn-sm btn-primary"
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
          ☰
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
          to="/ecrf"
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
  </div>
</template>

<script setup>
import {
  computed,
  nextTick,
  onMounted,
  onUnmounted,
  provide,
  ref,
  watch,
} from "vue";
import { useRoute, useRouter } from "vue-router";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/productCatalog.js";
import { useAuthStore } from "../stores/auth";
import { useKnowledgeStore } from "../stores/knowledge";
import { useOnboardingStore } from "../stores/onboarding";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { isScreenAvailable } from "../router/availability";
import {
  canAccessDataCollection,
  resolveSiteReadinessPath,
} from "../canvas/palette";
import OnboardingTour from "./OnboardingTour.vue";
import OnboardingChecklistDrawer from "./OnboardingChecklistDrawer.vue";
import CommandPaletteOverlay from "./CommandPaletteOverlay.vue";

provide("inAppShell", true);

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
const headerHeight = ref(57);
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

const activeStudyCode = computed(() => {
  const fromRoute = route?.params?.studyCode;
  if (typeof fromRoute === "string" && fromRoute.trim()) {
    return fromRoute.trim();
  }
  return canvasStore.studyCode || "";
});

const studiesPath = computed(() =>
  activeStudyCode.value
    ? `/studies/${encodeURIComponent(activeStudyCode.value)}`
    : "/studies"
);

const showStudiesNav = computed(() => isScreenAvailable("study"));
const showDataCollectionNav = computed(() =>
  canAccessDataCollection(normalizedRoles.value)
);
const siteReadinessPath = computed(() =>
  resolveSiteReadinessPath(normalizedRoles.value)
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

async function handleLogout() {
  await authStore.logout();
}

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
  flex-wrap: wrap;
  gap: 16px;
  padding: 10px 20px;
  background: var(--semantic-color-surface);
  border-bottom: 1px solid var(--semantic-color-border);
  position: sticky;
  top: 0;
  z-index: 30;
}

.header-left {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 20px;
}

.brand-link {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  text-decoration: none;
  color: var(--semantic-color-text);
}

.brand-mark {
  color: var(--semantic-color-interactive-primary);
  font-size: 1rem;
  line-height: 1;
}

.header-title-area h1 {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--semantic-color-text);
}

.primary-nav {
  display: flex;
  align-items: center;
  gap: 6px;
}

.primary-nav-link {
  display: inline-flex;
  align-items: center;
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 500;
  color: var(--semantic-color-text-body);
  text-decoration: none;
  border: 1px solid transparent;
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

.header-controls {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.plan-status-badge {
  display: inline-flex;
  align-items: center;
  padding: 5px 10px;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 700;
  text-decoration: none;
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
  white-space: nowrap;
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

.command-palette-trigger {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-body);
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
}

.command-palette-trigger:hover {
  background: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.palette-kbd {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
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
  gap: 6px;
}

.persona-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.persona-select {
  padding: 5px 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: 0.83rem;
  max-width: 220px;
}

.shell-control-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-dark);
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
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
  gap: 8px;
  padding-left: 6px;
  border-left: 1px solid var(--semantic-color-border);
}

.user-badge {
  font-size: 0.83rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
}

.shell-main {
  flex: 1;
  width: 100%;
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
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: 1rem;
  cursor: pointer;
}

.mobile-nav-drawer {
  display: none;
  width: 100%;
  flex-direction: column;
  gap: 6px;
  padding-top: 8px;
  border-top: 1px solid var(--semantic-color-border);
}

.mobile-nav-drawer.is-open {
  display: flex;
}

.mobile-nav-link {
  padding: 8px 12px;
  border-radius: 6px;
  color: var(--semantic-color-text-dark);
  text-decoration: none;
  font-weight: 500;
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

@media (max-width: 860px) {
  .primary-nav {
    display: none;
  }

  .mobile-menu-toggle {
    display: inline-flex;
  }
}
</style>
