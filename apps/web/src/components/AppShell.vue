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

          <div
            ref="secondaryMenuWrapperRef"
            class="header-secondary-menu-wrapper"
          >
            <button
              id="btn-header-actions-menu"
              ref="secondaryMenuToggleRef"
              data-testid="header-actions-menu-toggle"
              type="button"
              class="shell-control-btn header-actions-menu-toggle"
              title="Open orientation, help, and account options"
              aria-controls="header-secondary-menu"
              :aria-expanded="isSecondaryMenuOpen ? 'true' : 'false'"
              @click="toggleSecondaryMenu"
            >
              <span>Account</span>
              <span
                v-if="onboardingStore.totalTasksCount > 0"
                class="setup-progress-pill"
              >
                {{ onboardingStore.completedTasksCount }}/{{
                  onboardingStore.totalTasksCount
                }}
              </span>
            </button>

            <div
              id="header-secondary-menu"
              class="header-secondary-actions"
              :class="{ 'is-open': isSecondaryMenuOpen }"
            >
              <button
                v-if="onboardingStore.totalTasksCount > 0"
                id="btn-onboarding-checklist-trigger"
                type="button"
                class="shell-control-btn"
                :aria-expanded="onboardingStore.isDrawerOpen ? 'true' : 'false'"
                aria-controls="onboarding-checklist-drawer"
                title="Open workspace orientation and setup checklist"
                @click="handleToggleChecklist"
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
                @click="handleToggleContextualHelp"
              >
                Help &amp; SOPs
              </button>

              <div class="user-session-controls">
                <span class="user-badge">
                  {{ authStore.identity?.username || "Signed in" }}
                </span>
                <component
                  :is="DemoResetBtn"
                  v-if="DemoResetBtn"
                  @reset="handleDemoReset"
                />
                <button
                  id="btn-logout"
                  type="button"
                  class="btn btn-sm btn-secondary"
                  @click="handleLogoutClick"
                >
                  Sign out
                </button>
              </div>
            </div>
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
  ClinicalContextBar,
  SignOutConfirmModal,
  hasActiveModalFocusTrap,
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
import { isRouteAuthorizedForRoles } from "../router";
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

const DemoResetBtn = isDemoBuild()
  ? defineAsyncComponent(() => import("../views/demo/DemoResetButton.vue"))
  : null;

provide("inAppShell", true);

const authStore = useAuthStore();
const knowledgeStore = useKnowledgeStore();
const onboardingStore = useOnboardingStore();
const canvasStore = useStudyCanvasStore();
const route = useRoute();
const router = useRouter();

const isCommandPaletteOpen = ref(false);
const isMobileMenuOpen = ref(false);
const isSecondaryMenuOpen = ref(false);
const secondaryMenuWrapperRef = ref(null);
const secondaryMenuToggleRef = ref(null);
const statusAnnouncement = ref("");
const headerRef = ref(null);
const headerHeight = ref(48);
let headerResizeObserver = null;

function focusSkipTarget(targetId) {
  const target = document.getElementById(targetId);
  if (!target) return;
  target.focus();
  target.scrollIntoView?.({ block: "nearest" });
}

function syncHeaderHeight() {
  const el = headerRef.value;
  if (!el || typeof el.getBoundingClientRect !== "function") return;
  const measured = Math.round(el.getBoundingClientRect().height);
  if (measured > 0) {
    headerHeight.value = measured;
    el.closest?.(".app-shell")?.style?.setProperty(
      "--shell-header-height",
      `${measured}px`
    );
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

const checkActiveRouteAuth = () =>
  authStore.isAuthenticated &&
  route?.path !== "/forbidden" &&
  route?.path !== "/login" &&
  !isRouteAuthorizedForRoles(route, normalizedRoles.value) &&
  router?.replace?.("/forbidden");

async function handlePersonaChange(event) {
  const nextPersona = event?.target?.value;
  if (nextPersona) {
    authStore.switchPersona(nextPersona);
    onboardingStore.loadPersonaState(authStore.currentPersona || nextPersona);
    const matched = availablePersonas.value.find((p) => p.key === nextPersona);
    statusAnnouncement.value = `Switched workspace view to ${matched?.label || nextPersona}`;
    await checkActiveRouteAuth();
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
    if (route?.path !== "/login") {
      await router?.replace?.("/login");
    }
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

function closeSecondaryMenu({ restoreFocus = false } = {}) {
  if (!isSecondaryMenuOpen.value) return;
  isSecondaryMenuOpen.value = false;
  if (restoreFocus) {
    nextTick(() => {
      secondaryMenuToggleRef.value?.focus?.();
    });
  }
}

function toggleSecondaryMenu() {
  isSecondaryMenuOpen.value = !isSecondaryMenuOpen.value;
}

function handleToggleChecklist() {
  isSecondaryMenuOpen.value = false;
  onboardingStore.toggleDrawer();
}

function handleToggleContextualHelp() {
  isSecondaryMenuOpen.value = false;
  knowledgeStore.toggleDrawer();
}

function handleLogoutClick() {
  isSecondaryMenuOpen.value = false;
  handleLogout();
}

function handleDemoReset(message) {
  isSecondaryMenuOpen.value = false;
  statusAnnouncement.value = message;
}

function handleDocumentPointerDown(event) {
  if (!isSecondaryMenuOpen.value) return;
  const wrapperEl = secondaryMenuWrapperRef.value;
  if (wrapperEl && event?.target && !wrapperEl.contains(event.target)) {
    closeSecondaryMenu();
  }
}

useEscapeClose(() => closeSecondaryMenu({ restoreFocus: true }), {
  isOpen: () => isSecondaryMenuOpen.value,
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
  ([authenticated], [wasAuthenticated] = []) => {
    if (authenticated && typeof authStore.loadEntitlements === "function") {
      authStore.loadEntitlements();
    } else if (
      !authenticated &&
      wasAuthenticated &&
      (route?.meta?.requiresAuth ||
        route?.matched?.some((r) => r.meta?.requiresAuth))
    ) {
      router?.replace?.("/login");
    }
  }
);

watch(
  () => route?.fullPath,
  () => {
    isSecondaryMenuOpen.value = false;
    isMobileMenuOpen.value = false;
  }
);

watch(
  () => [authStore.currentPersona, normalizedRoles.value],
  () => {
    void checkActiveRouteAuth();
  }
);

watch(
  () => [
    authStore.isAuthenticated,
    authStore.activePlanBadge,
    isMobileMenuOpen.value,
    isSecondaryMenuOpen.value,
  ],
  () => {
    nextTick(syncHeaderHeight);
  }
);

onMounted(() => {
  window.addEventListener("keydown", handleGlobalKeydown);
  window.addEventListener("resize", syncHeaderHeight);
  document.addEventListener("pointerdown", handleDocumentPointerDown);
  syncHeaderHeight();
  if (typeof ResizeObserver !== "undefined" && headerRef.value) {
    headerResizeObserver = new ResizeObserver(() => syncHeaderHeight());
    headerResizeObserver.observe(headerRef.value, { box: "border-box" });
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
  window.removeEventListener("resize", syncHeaderHeight);
  document.removeEventListener("pointerdown", handleDocumentPointerDown);
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
  min-height: 28px;
  box-sizing: border-box;
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
  display: inline-block;
  vertical-align: middle;
  min-height: 24px;
  box-sizing: border-box;
  line-height: 1.25;
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  padding: 4px 10px;
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
  min-height: 28px;
  box-sizing: border-box;
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
  font-family: var(--semantic-font-mono);
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
  min-height: 28px;
  box-sizing: border-box;
  padding: 5px 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: 0.83rem;
  max-width: min(220px, 100%);
  min-width: 0;
}

.shell-control-btn {
  display: inline-flex;
  align-items: center;
  min-height: 28px;
  box-sizing: border-box;
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

.header-secondary-menu-wrapper {
  position: relative;
  display: inline-flex;
  align-items: center;
}

.header-actions-menu-toggle {
  display: none;
}

.header-secondary-actions {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.user-session-controls {
  display: inline-flex;
  align-items: center;
  flex-wrap: nowrap;
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
  box-sizing: border-box;
  min-width: 44px;
  min-height: 44px;
  align-items: center;
  justify-content: center;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: 1rem;
  line-height: 1;
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

@media (min-width: 961px) {
  .app-header {
    padding: 6px 16px;
    gap: 8px;
    flex-wrap: nowrap;
  }

  .header-left {
    gap: 10px;
    flex-wrap: nowrap;
    min-width: 0;
  }

  .brand-link {
    flex-shrink: 0;
  }

  .header-title-area h1 {
    font-size: 0.96rem;
    white-space: nowrap;
  }

  .primary-nav {
    gap: 3px;
    flex-wrap: nowrap;
    min-width: 0;
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

  .plan-status-badge {
    max-width: 136px;
    padding: 4px 8px;
    font-size: 0.75rem;
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

  .header-secondary-actions {
    gap: 6px;
    flex-wrap: nowrap;
  }

  .user-session-controls :deep(.btn) {
    white-space: nowrap;
    padding: 5px 8px;
    font-size: 0.78rem;
  }

  .user-badge {
    max-width: 88px;
  }
}

@media (min-width: 1281px) and (max-width: 1520px) {
  .app-header {
    padding: 6px 12px;
    gap: 6px;
  }

  .header-left {
    gap: 8px;
  }

  .header-title-area h1 {
    font-size: 0.92rem;
  }

  .primary-nav {
    gap: 2px;
  }

  .primary-nav-link {
    padding: 4px 6px;
    font-size: 0.78rem;
  }

  .header-controls {
    gap: 5px;
  }

  .plan-status-badge {
    max-width: 108px;
    padding: 4px 7px;
    font-size: 0.75rem;
  }

  .shell-control-btn,
  .command-palette-trigger {
    padding: 4px 7px;
    font-size: 0.78rem;
    gap: 5px;
  }

  .persona-select {
    max-width: 96px;
    padding: 4px 5px;
    font-size: 0.78rem;
  }

  .header-secondary-actions {
    gap: 5px;
  }

  .user-session-controls {
    gap: 5px;
    padding-left: 5px;
  }

  .user-session-controls :deep(.btn) {
    padding: 4px 7px;
    font-size: 0.78rem;
  }

  .user-badge {
    max-width: 72px;
    font-size: 0.78rem;
  }
}

@media (max-width: 1280px) {
  .header-actions-menu-toggle {
    display: inline-flex;
  }

  .header-secondary-actions {
    display: none;
  }

  .header-secondary-actions.is-open {
    display: flex;
    flex-direction: column;
    align-items: stretch;
    gap: 6px;
    position: absolute;
    right: 0;
    top: calc(100% + 6px);
    min-width: 210px;
    max-width: min(280px, calc(100vw - 24px));
    padding: 10px;
    background: var(--semantic-color-surface);
    border: 1px solid var(--semantic-color-border-control);
    border-radius: 8px;
    box-shadow: var(--semantic-shadow-xl);
    z-index: 60;
    box-sizing: border-box;
  }

  .header-secondary-actions.is-open > .shell-control-btn {
    width: 100%;
    justify-content: space-between;
    box-sizing: border-box;
    white-space: normal;
  }

  .header-secondary-actions.is-open > .user-session-controls {
    width: 100%;
    justify-content: space-between;
    flex-wrap: wrap;
    padding-left: 0;
    padding-top: 8px;
    border-left: 0;
    border-top: 1px solid var(--semantic-color-border);
    box-sizing: border-box;
  }

  .header-secondary-actions.is-open .user-badge {
    max-width: 140px;
  }
}

@media (min-width: 961px) and (max-width: 1280px) {
  .app-header {
    padding: 6px 12px;
    gap: 6px;
  }

  .header-left {
    gap: 8px;
  }

  .header-title-area h1 {
    font-size: 0.92rem;
  }

  .primary-nav {
    gap: 2px;
  }

  .primary-nav-link {
    padding: 4px 6px;
    font-size: 0.78rem;
  }

  .plan-status-badge {
    max-width: 104px;
  }

  .palette-kbd {
    display: none;
  }

  .persona-select {
    max-width: 98px;
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
    position: relative;
    padding: 10px 12px;
    gap: 10px;
  }

  .header-secondary-menu-wrapper {
    position: static;
  }

  .header-secondary-actions.is-open {
    left: 12px;
    right: 12px;
    top: calc(100% - 4px);
    min-width: 0;
    max-width: none;
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
