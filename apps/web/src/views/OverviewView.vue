<template>
  <div class="overview-hub-container" id="overview-hub-root">
    <!-- Welcome Header & Persona Banner -->
    <header class="overview-header-card">
      <div class="welcome-main">
        <div class="persona-icon-badge" aria-hidden="true">
          <ClinicalIcon
            :name="personaConfig.icon || 'shield-check'"
            :size="36"
          />
        </div>
        <div class="welcome-text-content">
          <h1 class="welcome-title">Welcome, {{ userName }}!</h1>
          <p class="welcome-subtitle">
            Cadence Clinical Research Operating System &bull;
            {{ personaConfig.title }}
          </p>
          <div class="persona-description">
            {{ personaConfig.description }}
          </div>
        </div>
      </div>

      <!-- User Preference Toggle Card -->
      <div class="landing-preference-card">
        <div class="preference-header">
          <ClinicalIcon name="settings" :size="16" class="pref-icon" />
          <span class="pref-title">Default Landing Preference</span>
        </div>
        <label class="pref-toggle-label" for="toggle-bypass-overview">
          <input
            type="checkbox"
            id="toggle-bypass-overview"
            class="pref-toggle-checkbox"
            :checked="onboardingStore.bypassOverview"
            @change="handleBypassToggle"
          />
          <span class="toggle-slider"></span>
          <span class="toggle-text">
            Bypass orientation hub on future sign-ins (route directly to primary
            workspace)
          </span>
        </label>
        <div
          class="pref-status-badge"
          :class="{ 'is-bypassed': onboardingStore.bypassOverview }"
        >
          <ClinicalIcon
            :name="onboardingStore.bypassOverview ? 'chevron-right' : 'compass'"
            :size="14"
          />
          <span>
            {{
              onboardingStore.bypassOverview
                ? "Direct workspace navigation active"
                : "Orientation Hub set as default landing"
            }}
          </span>
        </div>
      </div>
    </header>

    <section
      id="overview-plan-summary"
      class="overview-plan-banner"
      aria-label="Workspace plan and pre-release status"
    >
      <div class="overview-plan-main">
        <span
          id="overview-active-plan-badge"
          class="overview-plan-pill"
          :class="{ 'is-expired': authStore.isTrialExpired }"
        >
          {{ authStore.activePlanBadge || "Individual Plan" }}
        </span>
        <span
          v-if="
            authStore.subscriptionStatus === 'Trialing' &&
            !authStore.isTrialExpired &&
            authStore.trialDaysRemaining !== null &&
            authStore.trialEndsAtFormatted
          "
          id="overview-trial-countdown"
          class="overview-countdown-text tabular-nums"
        >
          Trial window:
          <strong
            >{{ authStore.trialDaysRemaining }}
            {{ authStore.trialDaysRemaining === 1 ? "day" : "days" }}
            remaining</strong
          >
          (ends {{ authStore.trialEndsAtFormatted }})
        </span>
        <span
          v-else-if="authStore.isTrialExpired"
          id="overview-expired-notice"
          class="overview-expired-text"
        >
          Workspace mutations paused &mdash; clinical reads, regulated exports,
          and Part 11 signatures remain enabled until study scoping activation
          is completed.
        </span>
        <span class="overview-families-text">
          Entitled families:
          <strong>{{ authStore.entitledFamilies.join(", ") }}</strong>
        </span>
        <span class="overview-prerelease-note">
          {{ PRE_RELEASE_ENVIRONMENT_NOTICE }}
        </span>
      </div>
      <router-link
        id="overview-link-plans"
        :to="
          authStore.isTrialExpired ? '/plans#scoped-upgrade-intake' : '/plans'
        "
        class="btn btn-secondary overview-plans-link"
      >
        {{
          authStore.isTrialExpired
            ? "Complete Study Scoping"
            : "Plans & entitlements"
        }}
      </router-link>
    </section>

    <div class="overview-grid">
      <!-- Left Column: Quick-Start Checklist & Guided Tour -->
      <section
        class="overview-section checklist-section"
        aria-labelledby="heading-quickstart"
      >
        <div class="section-header">
          <h2 id="heading-quickstart" class="section-title">
            <ClinicalIcon
              name="clipboard-list"
              :size="20"
              class="section-icon"
            />
            <span>Quick-Start Task Checklist</span>
          </h2>
          <span class="progress-badge">
            {{ completedCount }} of {{ currentChecklist.length }} tasks complete
            ({{ progressPercent }}%)
          </span>
        </div>

        <p class="section-intro">
          Tailored onboarding steps for your active
          <strong>{{ personaConfig.roleName }}</strong> role:
        </p>

        <!-- Progress Bar -->
        <div class="progress-bar-container" aria-hidden="true">
          <div
            class="progress-bar-fill"
            :style="{ width: progressPercent + '%' }"
          ></div>
        </div>

        <ul class="checklist-items-list">
          <li
            v-for="task in currentChecklist"
            :key="task.id"
            class="checklist-item-row"
            :class="{ 'is-completed': task.completed }"
          >
            <label :for="'task-' + task.id" class="checklist-item-label">
              <input
                type="checkbox"
                :id="'task-' + task.id"
                class="task-checkbox"
                :checked="task.completed"
                @change="
                  onboardingStore.toggleTask(task.id, $event.target.checked)
                "
              />
              <div class="task-content">
                <span class="task-title">{{ task.title }}</span>
                <span class="task-desc">{{ task.description }}</span>
              </div>
            </label>
            <button
              v-if="task.targetRoute"
              type="button"
              class="btn btn-task-launch"
              :title="'Open ' + task.title"
              @click="navigateToRoute(task)"
            >
              <span>Go</span>
              <ClinicalIcon name="external-link" :size="12" />
              <span class="sr-only">to {{ task.title }}</span>
            </button>
          </li>
        </ul>

        <div class="tour-action-footer">
          <button
            type="button"
            class="btn btn-primary btn-start-tour"
            @click="startGuidedTour"
          >
            <ClinicalIcon name="compass" :size="16" />
            <span>Launch Interactive Guided Tour</span>
          </button>
          <button
            v-if="completedCount > 0"
            type="button"
            class="btn btn-secondary btn-reset-checklist overview-reset-btn"
            @click="onboardingStore.resetChecklist()"
          >
            <ClinicalIcon name="refresh" :size="14" />
            <span>Reset Progress</span>
          </button>
        </div>
      </section>

      <!-- Right Column: Operational Workflow Launchers -->
      <section
        class="overview-section launchers-section"
        aria-labelledby="heading-launchers"
      >
        <div class="section-header">
          <h2 id="heading-launchers" class="section-title">
            <ClinicalIcon name="compass" :size="20" class="section-icon" />
            <span>Core Workflow Launchers</span>
          </h2>
          <span class="subtitle-tag">One-click deep operational access</span>
        </div>

        <p class="section-intro">
          Launch primary clinical tools and application suites in one click:
        </p>

        <div class="launchers-grid">
          <div
            v-for="launcher in personaLaunchers"
            :key="launcher.id"
            class="launcher-card"
            :class="{ 'is-featured': launcher.featured }"
          >
            <div class="launcher-header">
              <span class="launcher-icon" aria-hidden="true">
                <ClinicalIcon :name="launcher.icon || 'book-open'" :size="22" />
              </span>
              <span class="launcher-badge" :class="launcher.badgeClass">{{
                launcher.badgeText
              }}</span>
            </div>
            <h3 class="launcher-title">{{ launcher.title }}</h3>
            <p class="launcher-description">{{ launcher.description }}</p>
            <div class="launcher-action">
              <button
                type="button"
                class="btn btn-launcher-action"
                @click="navigateToRoute(launcher.routeSpec)"
              >
                <span>Launch {{ launcher.title }}</span>
                <ClinicalIcon name="external-link" :size="13" />
              </button>
            </div>
          </div>
        </div>

        <div
          v-if="demoCatalogGroups.length > 0"
          id="overview-demo-catalog"
          class="demo-catalog-groups"
        >
          <div class="demo-catalog-intro">
            <h3 class="demo-catalog-heading">
              Authorized Module Catalog Preview
            </h3>
            <p class="demo-catalog-subcopy">
              Interactive sandbox modules enabled for your active role, grouped
              by product family.
            </p>
          </div>

          <div
            v-for="group in demoCatalogGroups"
            :key="group.family"
            class="demo-family-group"
            :data-family="group.family"
          >
            <div class="demo-family-header">
              <h4 class="demo-family-title">{{ group.family }}</h4>
              <span class="demo-family-count">
                {{ group.items.length }}
                {{ group.items.length === 1 ? "module" : "modules" }}
              </span>
            </div>

            <div class="launchers-grid">
              <div
                v-for="item in group.items"
                :key="item.routeName"
                class="launcher-card demo-catalog-card"
                :data-route-name="item.routeName"
              >
                <div class="launcher-header">
                  <span class="launcher-badge badge-neutral">
                    {{ item.family }} &middot; Demo Preview
                  </span>
                </div>
                <h5 class="launcher-title">{{ item.title }}</h5>
                <p class="launcher-description">{{ item.summary }}</p>
                <div class="launcher-action">
                  <button
                    :id="`demo-launch-${item.routeName}`"
                    type="button"
                    class="btn btn-launcher-action"
                    @click="navigateToRoute(item.route)"
                  >
                    Launch {{ item.title }}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, watch } from "vue";
import { getActivePinia } from "pinia";
import { useRoute, useRouter } from "vue-router";
import { ClinicalIcon } from "ui";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/environmentNotice.js";
import { resolveLensEffectiveRoles } from "../catalog/staffPersonaCore.js";
import { getPersonaOverviewEntry as getPersonaCatalogEntry } from "../catalog/staffPersonaOverview.js";
import { resolveWorkspaceNavigationScope } from "../canvas/palette";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import { useAuthStore } from "../stores/auth";
import {
  resolveAuthorizedTaskRoute,
  useOnboardingStore,
} from "../stores/onboarding";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import "../stores/workspaceScope";
import {
  DEMO_PRODUCT_FAMILY_ORDER,
  isScreenAvailable,
  listDemoCatalogModules,
} from "../router/availability";
import { hasRequiredRole } from "../router";
import { isDemoBuild } from "../utils/demoMode";

const router = useRouter();
let activeRoute = null;
try {
  activeRoute = useRoute();
} catch {
  activeRoute = null;
}
const authStore = useAuthStore();
const onboardingStore = useOnboardingStore();
const activeCanvasStore = computed(() => {
  const pinia = getActivePinia();
  return pinia ? useStudyCanvasStore(pinia) : null;
});
const { activeStudyCode } = useActiveStudyScope();
const workspaceScope = computed(() =>
  resolveWorkspaceNavigationScope(
    activeRoute,
    activeCanvasStore.value,
    undefined,
    activeStudyCode.value
  )
);

function syncOnboardingPersona() {
  if (
    authStore.currentPersona &&
    onboardingStore.currentPersona !== authStore.currentPersona
  ) {
    onboardingStore.loadPersonaState(authStore.currentPersona);
  }
}

syncOnboardingPersona();

watch(
  () => authStore.currentPersona,
  () => {
    syncOnboardingPersona();
  }
);

onMounted(() => {
  syncOnboardingPersona();
  if (
    authStore.isAuthenticated &&
    typeof authStore.loadEntitlements === "function"
  ) {
    authStore.loadEntitlements();
  }
});

const userName = computed(() => {
  if (authStore.user?.firstName) {
    return `${authStore.user.firstName} ${authStore.user.lastName}`.trim();
  }
  return authStore.identity?.username || "Clinical Researcher";
});

const currentPersonaKey = computed(
  () =>
    authStore.currentPersona || onboardingStore.currentPersona || "super_admin"
);

const effectiveRoles = computed(() =>
  resolveLensEffectiveRoles(currentPersonaKey.value, authStore.rawRoles)
);

const catalogEntry = computed(() =>
  getPersonaCatalogEntry(currentPersonaKey.value, authStore.rawRoles)
);

const personaConfig = computed(() => catalogEntry.value.orientation);

const resolvedNavigationScope = computed(() => {
  const scope = workspaceScope.value;
  return {
    ...scope,
    studyCode: scope.studyCode || activeStudyCode.value || "",
  };
});

function resolveAuthorizedRoute(routeSpec) {
  if (!routeSpec) return null;
  return resolveAuthorizedTaskRoute(
    routeSpec,
    effectiveRoles.value,
    resolvedNavigationScope.value
  );
}

const currentChecklist = computed(() => {
  return (onboardingStore.visibleTasks || []).map((item) => ({
    ...item,
    targetRoute:
      item.routeName === "overview" ? null : resolveAuthorizedRoute(item),
  }));
});

const completedCount = computed(() => onboardingStore.completedTasksCount);
const progressPercent = computed(() => onboardingStore.progressPercentage);

const personaLaunchers = computed(() => {
  const launchers = catalogEntry.value.launchers || [];
  return launchers
    .map((launcher) => {
      const resolvedRoute = resolveAuthorizedRoute(launcher.routeSpec);
      return resolvedRoute ? { ...launcher, route: resolvedRoute } : null;
    })
    .filter(Boolean);
});

const demoCatalogGroups = computed(() => {
  if (!isDemoBuild()) {
    return [];
  }
  const featuredRouteNames = new Set(["study"]);
  if (resolveAuthorizedRoute("ecrf")) {
    featuredRouteNames.add("ecrf");
  }
  const readinessRoute = resolveAuthorizedRoute("site-readiness");
  if (readinessRoute?.startsWith("/ctms")) {
    featuredRouteNames.add("ctms");
  } else if (readinessRoute?.startsWith("/ssu")) {
    featuredRouteNames.add("ssu");
  }

  const byFamily = new Map();
  for (const mod of listDemoCatalogModules()) {
    if (featuredRouteNames.has(mod.routeName)) continue;
    const resolved = resolveAuthorizedRoute(mod.routeName);
    if (!resolved) continue;
    if (!byFamily.has(mod.family)) {
      byFamily.set(mod.family, []);
    }
    byFamily.get(mod.family).push({
      ...mod,
      route: resolved,
    });
  }

  return DEMO_PRODUCT_FAMILY_ORDER.filter((family) => byFamily.has(family)).map(
    (family) => ({
      family,
      items: byFamily.get(family),
    })
  );
});

function handleBypassToggle(event) {
  const isChecked = event.target.checked;
  onboardingStore.setBypassOverview(isChecked);
}

function navigateToRoute(routePathOrTask) {
  const resolved = resolveAuthorizedRoute(routePathOrTask);
  if (!resolved || !router) return;
  if (typeof router.resolve === "function") {
    const target = router.resolve(resolved);
    if (!target?.name || !isScreenAvailable(String(target.name))) {
      return;
    }
    const matchedRoles = (target.matched || []).flatMap(
      (record) => record.meta?.requiresRole || []
    );
    const requiredRoles =
      matchedRoles.length > 0 ? matchedRoles : target.meta?.requiresRole || [];
    if (
      requiredRoles.length > 0 &&
      !hasRequiredRole(effectiveRoles.value, requiredRoles)
    ) {
      return;
    }
  }
  router.push(resolved);
}

function startGuidedTour() {
  onboardingStore.startTour();
}
</script>

<style scoped>
.overview-hub-container {
  padding: 24px;
  max-width: 1280px;
  margin: 0 auto;
  font-family: var(--semantic-font-family);
  color: var(--semantic-color-text);
}

.overview-header-card {
  background: linear-gradient(
    135deg,
    var(--semantic-color-surface-inverse) 0%,
    var(--semantic-color-surface-elevated) 100%
  );
  color: var(--semantic-color-text-inverse);
  border-radius: 12px;
  padding: 28px;
  margin-bottom: 24px;
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 24px;
  box-shadow: 0 4px 20px rgba(15, 23, 42, 0.15);
}

.welcome-main {
  display: flex;
  align-items: flex-start;
  gap: 20px;
  flex: 1;
}

.persona-icon-badge {
  font-size: 3rem;
  background: rgba(255, 255, 255, 0.1);
  padding: 12px;
  border-radius: 12px;
  line-height: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.welcome-title {
  font-size: 1.8rem;
  font-weight: 700;
  margin: 0 0 4px 0;
  color: var(--semantic-color-text-inverse);
}

.welcome-subtitle {
  font-size: 0.95rem;
  color: var(--semantic-color-text-inverse-muted);
  margin: 0 0 10px 0;
  font-weight: 500;
}

.persona-description {
  font-size: 0.9rem;
  color: var(--semantic-color-text-inverse-muted);
  line-height: 1.5;
  max-width: 650px;
}

.landing-preference-card {
  background: rgba(255, 255, 255, 0.08);
  border: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 10px;
  padding: 16px;
  width: 320px;
  flex-shrink: 0;
  backdrop-filter: blur(4px);
}

.preference-header {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--semantic-color-surface-header);
  margin-bottom: 12px;
}

.pref-toggle-label {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  cursor: pointer;
  user-select: none;
}

.pref-toggle-checkbox {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  min-height: var(--semantic-control-target-min, 24px);
  margin-top: 2px;
  cursor: pointer;
  accent-color: var(--semantic-color-accent-inverse);
  flex-shrink: 0;
}

.toggle-text {
  font-size: 0.8rem;
  color: var(--semantic-color-border);
  line-height: 1.4;
}

.pref-status-badge {
  margin-top: 12px;
  padding: 6px 10px;
  border-radius: 6px;
  font-size: 0.75rem;
  font-weight: 600;
  background: rgba(56, 189, 248, 0.15);
  color: var(--semantic-color-accent-inverse);
  border: 1px solid rgba(56, 189, 248, 0.3);
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.pref-status-badge.is-bypassed {
  background: rgba(245, 158, 11, 0.15);
  color: var(--semantic-color-warning-border);
  border-color: rgba(245, 158, 11, 0.3);
}

.overview-plan-banner {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 12px;
  padding: 14px 20px;
  margin-bottom: 24px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 14px;
  box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04);
}

.overview-plan-main {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}

.overview-plan-pill {
  display: inline-flex;
  align-items: center;
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 0.8rem;
  font-weight: 700;
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
}

.overview-plan-pill.is-expired {
  background: var(--semantic-color-error-bg);
  border-color: var(--semantic-color-error-border);
  color: var(--semantic-color-error-dark);
}

.overview-families-text {
  font-size: 0.86rem;
  color: var(--semantic-color-text-dark);
}

.overview-countdown-text {
  font-size: 0.86rem;
  color: var(--semantic-color-text-dark);
  font-variant-numeric: var(--font-variant-numeric-tabular);
}

.overview-expired-text {
  font-size: 0.86rem;
  font-weight: 600;
  color: var(--semantic-color-error-dark);
}

.overview-prerelease-note {
  font-size: 0.8rem;
  color: var(--semantic-color-warning-text);
  background: var(--semantic-color-warning-bg-light);
  border: 1px solid var(--semantic-color-warning-border-strong);
  border-radius: 6px;
  padding: 3px 8px;
}

.overview-plans-link {
  font-size: 0.84rem;
  font-weight: 600;
  text-decoration: none;
  padding: 6px 12px;
  border-radius: 6px;
}

.overview-grid {
  display: grid;
  grid-template-columns: 380px 1fr;
  gap: 24px;
}

@media (max-width: 960px) {
  .overview-header-card {
    flex-direction: column;
  }
  .landing-preference-card {
    width: 100%;
  }
  .overview-grid {
    grid-template-columns: 1fr;
  }
}

.overview-section {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 12px;
  padding: 24px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.section-title {
  font-size: 1.2rem;
  font-weight: 700;
  margin: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--semantic-color-text);
}

.progress-badge {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
  font-size: 0.75rem;
  font-weight: 600;
  padding: 4px 8px;
  border-radius: 12px;
  font-variant-numeric: var(--font-variant-numeric-tabular);
}

.subtitle-tag {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  font-weight: 500;
}

.section-intro {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  margin: 0 0 16px 0;
}

.progress-bar-container {
  height: 6px;
  background: var(--semantic-color-border);
  border-radius: 3px;
  overflow: hidden;
  margin-bottom: 20px;
}

.progress-bar-fill {
  height: 100%;
  background: linear-gradient(
    90deg,
    var(--semantic-color-interactive-primary),
    var(--semantic-color-success)
  );
  transition: width 0.3s ease;
}

.checklist-items-list {
  list-style: none;
  padding: 0;
  margin: 0 0 20px 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.checklist-item-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  padding: 12px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  transition: all 0.2s ease;
}

.checklist-item-row:hover {
  background: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-muted);
}

.checklist-item-row.is-completed {
  background: var(--semantic-color-success-bg-soft);
  border-color: var(--semantic-color-success-border-soft);
}

.checklist-item-label {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  cursor: pointer;
  flex: 1;
}

.task-checkbox {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  min-height: var(--semantic-control-target-min, 24px);
  margin-top: 2px;
  cursor: pointer;
  accent-color: var(--semantic-color-success);
  flex-shrink: 0;
}

.task-content {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.task-title {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.is-completed .task-title {
  text-decoration: line-through;
  color: var(--semantic-color-success);
}

.task-desc {
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.3;
}

.btn-task-launch {
  padding: 4px 8px;
  font-size: 0.75rem;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-interactive-primary);
  border-radius: 4px;
  cursor: pointer;
  font-weight: 600;
  white-space: nowrap;
  margin-left: 8px;
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.btn-task-launch:hover {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-interactive-primary);
}

.tour-action-footer {
  padding-top: 12px;
  border-top: 1px solid var(--semantic-color-surface-header);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.btn-start-tour {
  width: 100%;
  padding: 10px;
  font-size: 0.88rem;
  font-weight: 600;
  border-radius: 8px;
  cursor: pointer;
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
  border: none;
  transition: background 0.2s ease;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

.overview-reset-btn {
  width: 100%;
  padding: 8px 10px;
  font-size: 0.82rem;
  font-weight: 600;
  border-radius: 8px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.btn-start-tour:hover {
  background: var(--semantic-color-primary-dark);
}

.launchers-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 16px;
}

.launcher-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 10px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  transition:
    transform 0.2s ease,
    box-shadow 0.2s ease,
    border-color 0.2s ease;
}

.launcher-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
  border-color: var(--semantic-color-interactive-primary);
}

.launcher-card.is-featured {
  border-color: var(--semantic-color-divider-accent);
  background: var(--semantic-color-info-bg);
}

.launcher-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.launcher-icon {
  font-size: 1.8rem;
  line-height: 1;
}

.launcher-badge {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 10px;
}

.badge-active {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.badge-neutral {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.launcher-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0 0 6px 0;
}

.launcher-description {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  margin: 0 0 16px 0;
  line-height: 1.4;
  flex: 1;
}

.btn-launcher-action {
  width: 100%;
  padding: 8px 12px;
  font-size: 0.82rem;
  font-weight: 600;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-interactive-primary);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s ease;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.launcher-card:hover .btn-launcher-action {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-interactive-primary);
}

.demo-catalog-groups {
  margin-top: 28px;
  padding-top: 24px;
  border-top: 1px solid var(--semantic-color-border);
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.demo-catalog-intro {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.demo-catalog-heading {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.demo-catalog-subcopy {
  margin: 0;
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
}

.demo-family-group {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.demo-family-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.demo-family-title {
  margin: 0;
  font-size: 0.92rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
}

.demo-family-count {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border);
  padding: 2px 8px;
  border-radius: var(--semantic-radius-sm, 6px);
  font-variant-numeric: var(--font-variant-numeric-tabular);
}
</style>
