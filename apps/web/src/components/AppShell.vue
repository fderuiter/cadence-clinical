<template>
  <div
    class="app-shell"
    :style="{ '--shell-header-height': `${headerHeight}px` }"
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
            :to="supportPath"
            class="primary-nav-link"
            :class="{ active: isSupportRoute }"
            :aria-current="isSupportRoute ? 'page' : undefined"
          >
            Support
          </router-link>
          <router-link
            v-if="showKnowledgeNav"
            id="nav-knowledge"
            :to="knowledgePath"
            class="primary-nav-link"
            :class="{ active: isKnowledgeRoute }"
            :aria-current="isKnowledgeRoute ? 'page' : undefined"
          >
            Knowledge
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
            :aria-expanded="onboardingStore.isDrawerOpen ? 'true' : 'false'"
            aria-controls="onboarding-checklist-drawer"
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

          <button
            id="btn-shell-theme-toggle"
            type="button"
            class="shell-control-btn shell-theme-toggle"
            :aria-label="
              isDarkTheme ? 'Switch to light theme' : 'Switch to dark theme'
            "
            :aria-pressed="isDarkTheme ? 'true' : 'false'"
            @click="handleThemeToggle"
          >
            <Sun v-if="isDarkTheme" :size="15" aria-hidden="true" />
            <Moon v-else :size="15" aria-hidden="true" />
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
            id="btn-shell-theme-toggle"
            type="button"
            class="shell-control-btn shell-theme-toggle"
            :aria-label="
              isDarkTheme ? 'Switch to light theme' : 'Switch to dark theme'
            "
            :aria-pressed="isDarkTheme ? 'true' : 'false'"
            @click="handleThemeToggle"
          >
            <Sun v-if="isDarkTheme" :size="15" aria-hidden="true" />
            <Moon v-else :size="15" aria-hidden="true" />
          </button>
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
          <Menu :size="18" aria-hidden="true" />
        </button>
      </div>

      <nav
        v-if="authStore.isAuthenticated"
        id="mobile-nav-menu"
        class="mobile-nav-drawer"
        :class="{ 'is-open': isMobileMenuOpen }"
        :hidden="!isMobileMenuOpen || undefined"
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
          :to="supportPath"
          class="mobile-nav-link"
          @click="isMobileMenuOpen = false"
        >
          Support
        </router-link>
        <router-link
          v-if="showKnowledgeNav"
          :to="knowledgePath"
          class="mobile-nav-link"
          @click="isMobileMenuOpen = false"
        >
          Knowledge
        </router-link>
      </nav>

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

    <ClinicalContextBar
      v-if="showContextBar"
      id="shell-context-bar"
      aria-label="Clinical context"
      studies-to="/studies"
      :study-code="activeStudyCode"
      :study-to="contextStudyTo"
      :site-code="workspaceScope.siteId"
      :site-to="contextSiteTo"
      :participant-code="workspaceScope.subjectCode"
      :participant-to="contextParticipantTo"
      :current-label="contextCurrentLabel"
    />

    <main
      id="main-content"
      class="shell-main"
      :class="{ 'shell-main-canvas': isCanvasRoute }"
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
  nextTick,
  onMounted,
  onUnmounted,
  provide,
  ref,
  watch,
} from "vue";
import { useRoute, useRouter } from "vue-router";
import { Menu, Moon, Sun } from "lucide-vue-next";
import {
  ClinicalContextBar,
  SURFACE_THEME_EVENT,
  SignOutConfirmModal,
  hasActiveModalFocusTrap,
  isEffectiveDarkTheme,
  resolveSurfaceTheme,
  toggleSurfaceTheme,
  useEscapeClose,
  useSignOutGuard,
} from "ui";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/productCatalog.js";
import { useAuthStore } from "../stores/auth";
import { hasUserScopedDrafts } from "../utils/userScopedStorage.js";
import { useKnowledgeStore } from "../stores/knowledge";
import { useOnboardingStore } from "../stores/onboarding";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import { isScreenAvailable } from "../router/availability";
import { isDemoBuild } from "../utils/demoMode.js";
import {
  buildDataCollectionPath,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  canAccessDataCollection,
  canAccessKnowledgeHub,
  canAccessSupportHub,
  resolveActiveWorkspaceScope,
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
const headerHeight = ref(52);
const isDarkTheme = ref(
  typeof document !== "undefined"
    ? document.documentElement.classList.contains("dark") ||
        isEffectiveDarkTheme(resolveSurfaceTheme())
    : false
);
let headerResizeObserver = null;

function focusSkipTarget(targetId) {
  const target = document.getElementById(targetId);
  if (!target) return;
  target.focus();
  target.scrollIntoView?.({ block: "nearest" });
}

function refreshThemeState() {
  if (typeof document === "undefined") return;
  isDarkTheme.value =
    document.documentElement.classList.contains("dark") ||
    document.documentElement.getAttribute("data-theme") === "dark";
}

function handleThemeToggle() {
  const next = toggleSurfaceTheme({ density: "staff" });
  isDarkTheme.value = Boolean(next.isDark);
}

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

const { activeStudyCode: syncedStudyCode } = useActiveStudyScope();
const workspaceScope = computed(() =>
  resolveActiveWorkspaceScope(route, canvasStore)
);
const activeStudyCode = computed(
  () => workspaceScope.value.studyCode || syncedStudyCode.value
);

const studiesPath = computed(() =>
  buildStudiesWorkspacePath({
    studyCode: activeStudyCode.value,
    siteId: workspaceScope.value.siteId,
    subjectCode: workspaceScope.value.subjectCode,
    participantRef: workspaceScope.value.participantRef,
  })
);

const dataCollectionPath = computed(() =>
  buildDataCollectionPath({
    studyCode: activeStudyCode.value,
    siteId: workspaceScope.value.explicitSiteId,
    participantRef: workspaceScope.value.participantRef,
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
    studyCode: activeStudyCode.value,
    siteId: workspaceScope.value.siteId,
    subjectCode: workspaceScope.value.subjectCode,
  })
);
const supportPath = computed(() =>
  buildScopedWorkflowPath("/tickets", {
    studyCode: activeStudyCode.value,
    siteId: workspaceScope.value.siteId,
    subjectCode: workspaceScope.value.subjectCode,
  })
);
const knowledgePath = computed(() =>
  buildScopedWorkflowPath("/knowledge", {
    studyCode: activeStudyCode.value,
    siteId: workspaceScope.value.siteId,
    subjectCode: workspaceScope.value.subjectCode,
  })
);
const showSupportNav = computed(() =>
  canAccessSupportHub(normalizedRoles.value)
);
const showKnowledgeNav = computed(() =>
  canAccessKnowledgeHub(normalizedRoles.value)
);

const isCanvasRoute = computed(
  () =>
    route?.name === "study" ||
    Boolean(route?.path && route.path.startsWith("/studies"))
);
const hasStudyWorkspace = computed(
  () =>
    route?.name === "study" &&
    typeof route?.params?.studyCode === "string" &&
    Boolean(route.params.studyCode.trim())
);
const isConductCanvasRoute = computed(
  () =>
    !isDemoBuild() && isCanvasRoute.value && route?.query?.stage === "conduct"
);
const isStudiesRoute = computed(
  () => isCanvasRoute.value && !isConductCanvasRoute.value
);

const NON_WORKFLOW_ROUTE_NAMES = new Set([
  "landing",
  "home",
  "overview",
  "login",
  "plans",
  "trial-onboarding",
  "forbidden",
  "unavailable",
]);
const NON_WORKFLOW_PATHS = new Set([
  "/",
  "/overview",
  "/login",
  "/plans",
  "/onboarding/trial",
  "/forbidden",
  "/unavailable",
]);
const WORKFLOW_CONTEXT_LABELS = Object.freeze({
  ecrf: "Data collection",
  observation: "Observation",
  ctms: "Site readiness",
  ssu: "Site startup",
  etmf: "Regulatory documents",
  documents: "Controlled documents",
  training: "Protocol training",
  tickets: "Support",
  knowledge: "Knowledge",
  mdr: "Protocol design",
  digitization: "Protocol digitization",
  "icf-builder": "Informed consent",
  "econsent-authoring": "Consent authoring",
  "amendment-diff": "Protocol amendments",
  imaging: "Medical imaging",
  devices: "Medical devices",
  "device-investigation": "Device investigation",
  adjudication: "Endpoint adjudication",
  cec: "Endpoint adjudication",
  audit: "Audit ledger",
  auditor: "Inspection review",
  rules: "Validation rules",
  coding: "Medical coding",
  lakehouse: "Clinical analytics",
  sensors: "Connected sensors",
  labs: "Laboratory governance",
  dct: "Decentralized visits",
  safety: "Safety review",
  quality: "Quality management",
  notifications: "Notifications",
  "participant-wallet": "Participant reimbursements",
  "lay-summaries": "Plain-language summaries",
});

const showContextBar = computed(() => {
  if (!authStore.isAuthenticated || isCanvasRoute.value) {
    return false;
  }
  const routeName = String(route?.name || "");
  const routePath = String(route?.path || "");
  if (
    NON_WORKFLOW_ROUTE_NAMES.has(routeName) ||
    NON_WORKFLOW_PATHS.has(routePath)
  ) {
    return false;
  }
  return Boolean(routeName || routePath);
});

const contextCurrentLabel = computed(() => {
  const routeName = String(route?.name || "");
  if (WORKFLOW_CONTEXT_LABELS[routeName]) {
    return WORKFLOW_CONTEXT_LABELS[routeName];
  }
  const routePath = String(route?.path || "").replace(/^\//, "");
  return WORKFLOW_CONTEXT_LABELS[routePath] || "";
});

const contextStudyTo = computed(() =>
  activeStudyCode.value
    ? `/studies/${encodeURIComponent(activeStudyCode.value)}`
    : ""
);

const contextSiteTo = computed(() => {
  const studyCode = activeStudyCode.value;
  const siteId = workspaceScope.value.siteId;
  if (!siteId) return "";
  if (!studyCode) return `/studies?site=${encodeURIComponent(siteId)}`;
  return {
    path: `/studies/${encodeURIComponent(studyCode)}`,
    query: {
      stage: "conduct",
      section: "participants",
      site: siteId,
    },
  };
});

const contextParticipantTo = computed(() => {
  const studyCode = activeStudyCode.value;
  const siteId = workspaceScope.value.siteId;
  const participant =
    workspaceScope.value.participantRef || workspaceScope.value.subjectCode;
  if (!participant) return "";
  if (!studyCode) {
    return `/studies?participant=${encodeURIComponent(participant)}`;
  }
  return {
    path: `/studies/${encodeURIComponent(studyCode)}`,
    query: {
      stage: "conduct",
      ...(siteId ? { site: siteId } : {}),
      participant,
    },
  };
});
const isDataCollectionRoute = computed(
  () =>
    route?.name === "ecrf" ||
    route?.name === "observation" ||
    route?.path === "/ecrf" ||
    isConductCanvasRoute.value
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

function closeMobileMenu({ restoreFocus = false } = {}) {
  if (!isMobileMenuOpen.value) return;
  isMobileMenuOpen.value = false;
  if (restoreFocus && typeof document !== "undefined") {
    nextTick(() => {
      document.getElementById("btn-mobile-menu-toggle")?.focus?.();
    });
  }
}

useEscapeClose(() => closeMobileMenu({ restoreFocus: true }), {
  isOpen: () => isMobileMenuOpen.value,
});

function handleGlobalKeydown(event) {
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "e") {
    canvasStore.handleExplainShortcut(event);
    return;
  }
  if (!authStore.isAuthenticated) return;
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    if (hasActiveModalFocusTrap({ excludeId: "canvas-palette" })) {
      return;
    }
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
  window.addEventListener(SURFACE_THEME_EVENT, refreshThemeState);
  window.addEventListener("storage", refreshThemeState);
  refreshThemeState();
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
  window.removeEventListener(SURFACE_THEME_EVENT, refreshThemeState);
  window.removeEventListener("storage", refreshThemeState);
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
  min-height: var(--density-top-chrome-height, 52px);
  padding: 6px 20px;
  box-sizing: border-box;
  background: var(--semantic-color-surface);
  border-bottom: var(--density-border-width, 1px) solid
    var(--semantic-color-border);
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
  flex-wrap: wrap;
  gap: 6px;
  max-width: 100%;
  min-width: 0;
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
  font-size: var(--density-input-font-size, 0.83rem);
  max-width: min(220px, 100%);
  min-width: 0;
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
  flex-wrap: wrap;
  gap: 8px;
  padding-left: 6px;
  border-left: 1px solid var(--semantic-color-border);
  max-width: 100%;
  min-width: 0;
}

.user-badge {
  font-size: 0.83rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
  overflow-wrap: anywhere;
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

@media (min-width: 768px) and (max-width: 1280px) {
  .app-header {
    padding: 6px 14px;
    gap: 8px;
    flex-wrap: nowrap;
  }

  .header-left {
    gap: 10px;
    flex-wrap: nowrap;
    min-width: 0;
  }

  .primary-nav {
    gap: 4px;
  }

  .primary-nav-link {
    padding: 5px 8px;
    font-size: 0.82rem;
    white-space: nowrap;
  }

  .header-controls {
    gap: 6px;
    flex-wrap: nowrap;
    min-width: 0;
  }

  .palette-kbd {
    display: none;
  }

  .persona-switcher-wrapper {
    flex-wrap: nowrap;
    gap: 4px;
  }

  .persona-label {
    font-size: 0.76rem;
    white-space: nowrap;
  }

  .persona-select {
    max-width: 118px;
    padding: 4px 6px;
    font-size: 0.78rem;
  }

  .shell-control-btn,
  .command-palette-trigger {
    padding: 5px 8px;
    font-size: 0.8rem;
    white-space: nowrap;
  }

  .user-badge {
    max-width: 96px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
}

@media (max-width: 860px) {
  .primary-nav {
    display: none;
  }

  .mobile-menu-toggle {
    display: inline-flex;
  }
}

@media (max-width: 640px) {
  .app-header {
    padding: 10px 12px;
    gap: 10px;
  }

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
