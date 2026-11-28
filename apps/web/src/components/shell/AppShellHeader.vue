<template>
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
          v-for="item in visibleNavItems"
          :id="item.id"
          :key="item.id"
          :to="item.to"
          class="primary-nav-link"
          :class="{ active: item.active }"
          :aria-current="item.active ? 'page' : undefined"
        >
          {{ item.label }}
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
          @click="emit('action', 'palette')"
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
            @change="emit('action', 'persona', $event)"
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
              v-if="onboardingSummary.totalTasksCount > 0"
              class="setup-progress-pill tabular-nums"
            >
              {{ onboardingSummary.completedTasksCount }}/{{
                onboardingSummary.totalTasksCount
              }}
            </span>
          </button>

          <div
            id="header-secondary-menu"
            ref="secondaryMenuPanelRef"
            class="header-secondary-actions"
            :class="{ 'is-open': isSecondaryMenuOpen }"
            :style="secondaryMenuStyle"
          >
            <button
              v-if="onboardingSummary.totalTasksCount > 0"
              id="btn-onboarding-checklist-trigger"
              type="button"
              class="shell-control-btn"
              :aria-expanded="
                onboardingSummary.isDrawerOpen || pendingChecklistOpen
                  ? 'true'
                  : 'false'
              "
              aria-controls="onboarding-checklist-drawer"
              title="Open workspace orientation and setup checklist"
              @click="runMenuAction('checklist')"
            >
              <span>Orientation</span>
              <span class="setup-progress-pill tabular-nums">
                {{ onboardingSummary.completedTasksCount }}/{{
                  onboardingSummary.totalTasksCount
                }}
              </span>
            </button>

            <button
              id="btn-contextual-help-trigger"
              type="button"
              class="shell-control-btn"
              :aria-expanded="isContextualHelpOpen"
              aria-label="Help & SOPs — Open contextual help and SOP guidance"
              @click="runMenuAction('help')"
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
                @reset="(msg) => runMenuAction('reset', msg)"
              />
              <button
                id="btn-logout"
                type="button"
                class="btn btn-sm btn-secondary"
                @click="runMenuAction('logout')"
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
          @click="emit('action', 'register')"
        >
          Start trial
        </button>
        <button
          id="btn-login"
          type="button"
          class="btn btn-sm btn-primary"
          @click="emit('action', 'login')"
        >
          Sign in
        </button>
      </template>

      <slot name="theme-toggle" />

      <button
        v-if="authStore.isAuthenticated"
        id="btn-mobile-menu-toggle"
        ref="mobileMenuToggleRef"
        type="button"
        class="mobile-menu-toggle"
        aria-label="Toggle navigation menu"
        aria-controls="mobile-nav-menu"
        :aria-expanded="isMobileMenuOpen ? 'true' : 'false'"
        @click="toggleMobileMenu"
      >
        <svg
          class="clinical-icon"
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.75"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
          focusable="false"
        >
          <path v-if="isMobileMenuOpen" d="M18 6 6 18M6 6l12 12" />
          <path v-else d="M4 6h16M4 12h16M4 18h16" />
        </svg>
      </button>
    </div>

    <nav
      v-if="authStore.isAuthenticated"
      id="mobile-nav-menu"
      ref="mobileMenuPanelRef"
      class="mobile-nav-drawer"
      :class="{ 'is-open': isMobileMenuOpen }"
      :style="mobileMenuStyle"
      :hidden="!isMobileMenuOpen || undefined"
      aria-label="Mobile navigation"
    >
      <router-link
        v-for="item in visibleNavItems"
        :key="item.id"
        :to="item.to"
        class="mobile-nav-link"
        @click="closeMobileMenu()"
      >
        {{ item.label }}
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
</template>

<script setup>
import { computed, defineAsyncComponent, ref, watch } from "vue";
import { useRoute } from "vue-router";
import {
  buildDataCollectionPath,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  canAccessDataCollection,
  canAccessKnowledgeHub,
  canAccessSupportHub,
  resolveSiteReadinessPath,
} from "../../canvas/palette";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../../catalog/environmentNotice.js";
import { useHeaderMenuCoordination } from "../../composables/useHeaderMenuCoordination.js";
import { isScreenAvailable } from "../../router/availability";
import { useAuthStore } from "../../stores/auth";
import { isDemoBuild } from "../../utils/demoMode.js";
import { hasShellBlockingModalOverlay } from "./appShellScope.js";

const props = defineProps({
  workspaceScope: { type: Object, required: true },
  activeStudyCode: { type: String, default: "" },
  isCanvasRoute: { type: Boolean, default: false },
  isConductCanvasRoute: { type: Boolean, default: false },
  onboardingSummary: { type: Object, required: true },
  pendingChecklistOpen: { type: Boolean, default: false },
  isContextualHelpOpen: { type: Boolean, default: false },
  statusAnnouncement: { type: String, default: "" },
});

const emit = defineEmits(["action"]);

const DemoResetBtn = isDemoBuild()
  ? defineAsyncComponent(() => import("../../views/demo/DemoResetButton.vue"))
  : null;

const route = useRoute();
const authStore = useAuthStore();
const normalizedRoles = computed(() => authStore.normalizedRoles || []);
const availablePersonas = computed(() => authStore.availablePersonas || []);

const studiesPath = computed(() =>
  buildStudiesWorkspacePath({
    studyCode: props.activeStudyCode,
    siteId: props.workspaceScope.siteId,
    subjectCode: props.workspaceScope.subjectCode,
    participantRef: props.workspaceScope.participantRef,
  })
);
const visibleNavItems = computed(() => {
  const roles = normalizedRoles.value;
  const scope = {
    studyCode: props.activeStudyCode,
    siteId: props.workspaceScope.siteId,
    subjectCode: props.workspaceScope.subjectCode,
  };
  const readinessTo = resolveSiteReadinessPath(roles, scope);
  const name = route?.name;
  const path = route?.path || "";
  return [
    {
      id: "nav-studies",
      label: "Studies",
      show: isScreenAvailable("study"),
      to: studiesPath.value,
      active: props.isCanvasRoute && !props.isConductCanvasRoute,
    },
    {
      id: "nav-data-collection",
      label: "Data collection",
      show: canAccessDataCollection(roles),
      to: buildDataCollectionPath({
        studyCode: props.activeStudyCode,
        siteId: props.workspaceScope.explicitSiteId,
        participantRef: props.workspaceScope.participantRef,
        subjectCode: props.workspaceScope.subjectCode,
        enrollmentId: props.workspaceScope.enrollmentId,
      }),
      active:
        name === "ecrf" ||
        name === "observation" ||
        path === "/ecrf" ||
        props.isConductCanvasRoute,
    },
    {
      id: "nav-site-readiness",
      label: "Site readiness",
      show: Boolean(readinessTo),
      to: readinessTo,
      active:
        name === "ctms" ||
        name === "ssu" ||
        path === "/ctms" ||
        path === "/ssu",
    },
    {
      id: "nav-support",
      label: "Support",
      show: canAccessSupportHub(roles),
      to: buildScopedWorkflowPath("/tickets", scope),
      active: name === "tickets" || path.startsWith("/tickets"),
    },
    {
      id: "nav-knowledge",
      label: "Knowledge",
      show: canAccessKnowledgeHub(roles),
      to: buildScopedWorkflowPath("/knowledge", scope),
      active: name === "knowledge" || path.startsWith("/knowledge"),
    },
  ].filter((item) => item.show);
});

const headerRef = ref(null);
const secondaryMenuWrapperRef = ref(null);
const secondaryMenuToggleRef = ref(null);
const secondaryMenuPanelRef = ref(null);
const mobileMenuToggleRef = ref(null);
const mobileMenuPanelRef = ref(null);

const {
  isMobileMenuOpen,
  isSecondaryMenuOpen,
  secondaryMenuStyle,
  mobileMenuStyle,
  closeMobileMenu,
  closeSecondaryMenu,
  closeAllHeaderMenus,
  toggleMobileMenu,
  toggleSecondaryMenu,
  handleEscapeKey,
} = useHeaderMenuCoordination({
  refs: {
    secondaryWrapper: secondaryMenuWrapperRef,
    secondaryToggle: secondaryMenuToggleRef,
    secondaryPanel: secondaryMenuPanelRef,
    mobileToggle: mobileMenuToggleRef,
    mobilePanel: mobileMenuPanelRef,
  },
  hasBlockingModalOverlay: () => hasShellBlockingModalOverlay(),
});

function runMenuAction(kind, payload) {
  if (isSecondaryMenuOpen.value) {
    const toggleBtn =
      secondaryMenuToggleRef.value ||
      (typeof document !== "undefined"
        ? document.getElementById("btn-header-actions-menu")
        : null);
    toggleBtn?.focus?.();
  }
  closeSecondaryMenu();
  emit("action", kind, payload);
}

watch(
  () => route?.fullPath,
  () => {
    closeAllHeaderMenus();
  }
);

watch([isMobileMenuOpen, isSecondaryMenuOpen], () => {
  emit("action", "menu");
});

defineExpose({
  getHeaderElement: () => headerRef.value,
  handleEscapeKey,
  closeAllHeaderMenus,
});
</script>

<style scoped src="./appShellChrome.css"></style>
