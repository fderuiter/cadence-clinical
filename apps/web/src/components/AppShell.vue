<template>
  <div class="app-shell">
    <a href="#main-content" class="skip-link">Skip to main content</a>

    <header class="app-header">
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
          <button
            id="btn-open-palette"
            type="button"
            class="command-palette-trigger"
            aria-label="Open global search and command palette (Ctrl+K or Cmd+K)"
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
            aria-label="Open workspace orientation and setup checklist"
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
            aria-label="Open contextual help and SOP guidance"
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
    <SignOutConfirmModal
      id="shell-signout-confirm-modal"
      v-model="isSignOutConfirmOpen"
      @confirm="confirmSignOut"
      @cancel="cancelSignOut"
    />
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, provide, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { SignOutConfirmModal, useSignOutGuard } from "ui";
import { useAuthStore } from "../stores/auth";
import { hasUserScopedDrafts } from "../utils/userScopedStorage.js";
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

onMounted(() => {
  window.addEventListener("keydown", handleGlobalKeydown);
});

onUnmounted(() => {
  window.removeEventListener("keydown", handleGlobalKeydown);
});
</script>

<style scoped>
.app-shell {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--semantic-color-surface-muted, #f8fafc);
  color: var(--primary, #0f172a);
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
  background: #0f172a;
  color: #ffffff;
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
  background: #ffffff;
  border-bottom: 1px solid #e2e8f0;
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
  color: #0f172a;
}

.brand-mark {
  color: #1d4ed8;
  font-size: 1rem;
  line-height: 1;
}

.header-title-area h1 {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: #0f172a;
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
  color: #334155;
  text-decoration: none;
  border: 1px solid transparent;
  transition:
    background-color 0.15s ease,
    color 0.15s ease,
    border-color 0.15s ease;
}

.primary-nav-link:hover {
  background: #f1f5f9;
  color: #0f172a;
}

.primary-nav-link.active {
  background: #eff6ff;
  border-color: #bfdbfe;
  color: #1e40af;
  font-weight: 600;
}

.header-controls {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.command-palette-trigger {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  padding: 6px 10px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  background: #f8fafc;
  color: #334155;
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
}

.command-palette-trigger:hover {
  background: #f1f5f9;
  border-color: #94a3b8;
  color: #0f172a;
}

.palette-kbd {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 0.75rem;
  padding: 1px 5px;
  border-radius: 4px;
  background: #ffffff;
  border: 1px solid #cbd5e1;
  color: #334155;
}

.persona-switcher-wrapper {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.persona-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: #475569;
}

.persona-select {
  padding: 5px 8px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  background: #ffffff;
  color: #0f172a;
  font-size: 0.83rem;
  max-width: 220px;
}

.shell-control-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  background: #ffffff;
  color: #1e293b;
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
}

.shell-control-btn:hover {
  background: #f1f5f9;
  border-color: #94a3b8;
}

.setup-progress-pill {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 1px 6px;
  border-radius: 999px;
  background: #eff6ff;
  color: #1e40af;
}

.user-session-controls {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding-left: 6px;
  border-left: 1px solid #e2e8f0;
}

.user-badge {
  font-size: 0.83rem;
  font-weight: 600;
  color: #334155;
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
}

.mobile-menu-toggle {
  display: none;
  align-items: center;
  justify-content: center;
  padding: 6px 10px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  background: #ffffff;
  color: #0f172a;
  font-size: 1rem;
  cursor: pointer;
}

.mobile-nav-drawer {
  display: none;
  width: 100%;
  flex-direction: column;
  gap: 6px;
  padding-top: 8px;
  border-top: 1px solid #e2e8f0;
}

.mobile-nav-drawer.is-open {
  display: flex;
}

.mobile-nav-link {
  padding: 8px 12px;
  border-radius: 6px;
  color: #1e293b;
  text-decoration: none;
  font-weight: 500;
}

.mobile-nav-link:hover {
  background: #f1f5f9;
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
