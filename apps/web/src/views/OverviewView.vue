<template>
  <div class="overview-hub-container" id="overview-hub-root">
    <!-- Welcome Header & Persona Banner -->
    <header class="overview-header-card">
      <div class="welcome-main">
        <div class="persona-icon-badge" aria-hidden="true">
          <svg
            class="overview-svg-icon overview-svg-icon-lg"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
            <path d="m9 12 2 2 4-4" />
          </svg>
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
          <svg
            class="overview-svg-icon"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <circle cx="12" cy="12" r="3" />
            <path
              d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"
            />
          </svg>
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
          {{
            onboardingStore.bypassOverview
              ? "Direct workspace navigation active"
              : "Orientation Hub set as default landing"
          }}
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
            <svg
              class="overview-svg-icon"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
              aria-hidden="true"
            >
              <rect x="3" y="4" width="18" height="16" rx="2" />
              <path d="m9 10 2 2 4-4" />
              <path d="M9 16h6" />
            </svg>
            <span>Quick-Start Task Checklist</span>
          </h2>
          <span class="progress-badge">
            {{ completedCount }} of {{ totalCount }} tasks complete ({{
              progressPercent
            }}%)
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
              @click="navigateToRoute(task.targetRoute)"
            >
              Go <span aria-hidden="true">&nearr;</span
              ><span class="sr-only">to {{ task.title }}</span>
            </button>
          </li>
        </ul>

        <div class="tour-action-footer">
          <button
            id="btn-overview-reset-checklist"
            type="button"
            class="btn btn-secondary btn-reset-checklist"
            @click="onboardingStore.resetChecklist()"
          >
            Reset Progress
          </button>
          <button
            type="button"
            class="btn btn-primary btn-start-tour"
            @click="startGuidedTour"
          >
            Launch Interactive Guided Tour
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
            <svg
              class="overview-svg-icon"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
              aria-hidden="true"
            >
              <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2" />
            </svg>
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
                <svg
                  class="overview-svg-icon"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <rect x="3" y="3" width="7" height="7" rx="1" />
                  <rect x="14" y="3" width="7" height="7" rx="1" />
                  <rect x="14" y="14" width="7" height="7" rx="1" />
                  <rect x="3" y="14" width="7" height="7" rx="1" />
                </svg>
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
                @click="navigateToRoute(launcher.route)"
              >
                Launch {{ launcher.title }} &nearr;
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
import { useRoute, useRouter } from "vue-router";
import { PERSONA_CATALOG_BY_KEY, getPersonaCatalogEntry } from "ui";
import {
  DATA_COLLECTION_ROLES,
  ETMF_ROLES,
  SITE_READINESS_SITE_ROLES,
  SITE_READINESS_SPONSOR_ROLES,
  buildDataCollectionPath,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  readInstantiatedStudyCanvasScope,
  resolveSiteReadinessPath,
  resolveWorkspaceNavigationScope,
} from "../canvas/palette";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/environmentNotice.js";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import {
  defaultPersonaKeyForRoles,
  resolveEffectivePersonaRoles,
  useAuthStore,
} from "../stores/auth";
import { SAFETY_ROLES, useOnboardingStore } from "../stores/onboarding";
import { CTMS_AUTHORIZED_ROLES } from "../stores/onboardingSummary.js";
import "../stores/workspaceScope";
import {
  DEMO_MODULE_CATALOG,
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
const activeCanvasStore = computed(() => readInstantiatedStudyCanvasScope());
const { activeStudyCode } = useActiveStudyScope();
const workspaceScope = computed(() =>
  resolveWorkspaceNavigationScope(
    activeRoute,
    activeCanvasStore.value,
    undefined,
    activeStudyCode.value
  )
);

onMounted(() => {
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

const currentPersonaKey = computed(() => {
  const explicit = authStore.currentPersona || "super_admin";
  if (
    explicit === "super_admin" &&
    authStore.rawRoles?.length > 0 &&
    !(authStore.normalizedRoles || []).includes("sponsor_admin")
  ) {
    return defaultPersonaKeyForRoles(authStore.rawRoles);
  }
  return explicit;
});

watch(
  currentPersonaKey,
  (personaKey) => {
    if (personaKey && onboardingStore.currentPersona !== personaKey) {
      onboardingStore.loadPersonaState(personaKey);
    }
  },
  { immediate: true }
);

const effectiveRoles = computed(() =>
  resolveEffectivePersonaRoles(authStore.rawRoles, currentPersonaKey.value, {
    isAuthenticated: Boolean(authStore.isAuthenticated),
  })
);

function normalizeSpecToRouteName(routeSpec) {
  const raw = String(routeSpec || "")
    .trim()
    .split("?")[0];
  if (!raw) return "";
  if (raw === "study" || raw === "/studies" || raw.startsWith("/studies/")) {
    return "study";
  }
  if (
    raw === "observation" ||
    raw === "/observations" ||
    raw.startsWith("/observations/")
  ) {
    return "observation";
  }
  return raw.replace(/^\//, "");
}

function resolveAuthorizedRoute(routeSpec) {
  if (!routeSpec) return null;
  if (typeof routeSpec === "string" && routeSpec.includes("?")) {
    return routeSpec;
  }
  const scope = workspaceScope.value;
  const studyCode = scope.studyCode || activeStudyCode.value;
  const siteId = scope.siteId;
  const explicitSiteId =
    scope.explicitSiteId !== undefined ? scope.explicitSiteId : siteId;
  const subjectCode = scope.subjectCode;
  const participantRef = scope.participantRef;
  const enrollmentId = scope.enrollmentId;

  if (
    routeSpec === "study" ||
    routeSpec === "/studies" ||
    (typeof routeSpec === "string" && routeSpec.startsWith("/studies/"))
  ) {
    if (!isScreenAvailable("study")) return null;
    if (typeof routeSpec === "string" && routeSpec.startsWith("/studies?")) {
      return routeSpec;
    }
    return buildStudiesWorkspacePath({
      studyCode,
      siteId,
      subjectCode,
      participantRef,
    });
  }
  if (routeSpec === "overview" || routeSpec === "/overview") {
    return isScreenAvailable("overview")
      ? buildScopedWorkflowPath("/overview", { studyCode, siteId, subjectCode })
      : null;
  }
  if (routeSpec === "ecrf" || routeSpec === "/ecrf") {
    if (isDemoBuild()) {
      return isScreenAvailable("ecrf") &&
        hasRequiredRole(effectiveRoles.value, DATA_COLLECTION_ROLES)
        ? buildDataCollectionPath({
            studyCode,
            siteId: explicitSiteId,
            subjectCode,
            enrollmentId,
          })
        : null;
    }
    return isScreenAvailable("study") &&
      hasRequiredRole(effectiveRoles.value, DATA_COLLECTION_ROLES)
      ? studyCode || explicitSiteId || participantRef || subjectCode
        ? buildDataCollectionPath({
            studyCode,
            siteId: explicitSiteId,
            participantRef,
            subjectCode,
            enrollmentId,
          })
        : "/studies"
      : null;
  }
  if (routeSpec === "site-readiness") {
    return resolveSiteReadinessPath(effectiveRoles.value, {
      studyCode,
      siteId,
      subjectCode,
    });
  }
  if (routeSpec === "ctms" || routeSpec === "/ctms") {
    return isScreenAvailable("ctms") &&
      hasRequiredRole(effectiveRoles.value, CTMS_AUTHORIZED_ROLES)
      ? buildScopedWorkflowPath("/ctms", { studyCode, siteId, subjectCode })
      : null;
  }
  if (routeSpec === "ssu" || routeSpec === "/ssu") {
    return isScreenAvailable("ssu") &&
      hasRequiredRole(effectiveRoles.value, [
        ...SITE_READINESS_SPONSOR_ROLES,
        ...SITE_READINESS_SITE_ROLES,
      ])
      ? buildScopedWorkflowPath("/ssu", { studyCode, siteId, subjectCode })
      : null;
  }
  if (routeSpec === "etmf" || routeSpec === "/etmf") {
    return isScreenAvailable("etmf") &&
      hasRequiredRole(effectiveRoles.value, ETMF_ROLES)
      ? buildScopedWorkflowPath("/etmf", { studyCode, siteId, subjectCode })
      : null;
  }
  if (routeSpec === "training" || routeSpec === "/training") {
    return isScreenAvailable("training")
      ? buildScopedWorkflowPath("/training", { studyCode, siteId, subjectCode })
      : null;
  }
  if (routeSpec === "safety" || routeSpec === "/safety") {
    if (
      !hasRequiredRole(effectiveRoles.value, SAFETY_ROLES) ||
      !isScreenAvailable("safety")
    ) {
      return null;
    }
    return buildScopedWorkflowPath("/safety", {
      studyCode,
      siteId,
      subjectCode,
    });
  }
  if (routeSpec === "audit" || routeSpec === "/audit") {
    if (
      !hasRequiredRole(effectiveRoles.value, [
        "auditor",
        "tmf_auditor",
        "sponsor_admin",
      ]) ||
      !isScreenAvailable("audit")
    ) {
      return null;
    }
    return buildScopedWorkflowPath("/audit", {
      studyCode,
      siteId,
      subjectCode,
    });
  }
  if (routeSpec === "tickets" || routeSpec === "/tickets") {
    return isScreenAvailable("tickets")
      ? buildScopedWorkflowPath("/tickets", { studyCode, siteId, subjectCode })
      : null;
  }
  if (routeSpec === "knowledge" || routeSpec === "/knowledge") {
    return isScreenAvailable("knowledge")
      ? buildScopedWorkflowPath("/knowledge", {
          studyCode,
          siteId,
          subjectCode,
        })
      : null;
  }
  const normalizedName = normalizeSpecToRouteName(routeSpec);
  const catalogEntry = isDemoBuild()
    ? DEMO_MODULE_CATALOG[normalizedName]
    : null;
  if (catalogEntry) {
    if (!isScreenAvailable(catalogEntry.routeName)) {
      return null;
    }
    if (
      catalogEntry.requiresRole.length > 0 &&
      !hasRequiredRole(effectiveRoles.value, catalogEntry.requiresRole)
    ) {
      return null;
    }
    if (catalogEntry.routeName === "observation") {
      return catalogEntry.defaultPath;
    }
    return buildScopedWorkflowPath(catalogEntry.defaultPath, {
      studyCode,
      siteId,
      subjectCode,
    });
  }
  return null;
}

const personaConfig = computed(
  () =>
    getPersonaCatalogEntry(currentPersonaKey.value) ||
    PERSONA_CATALOG_BY_KEY.super_admin
);

const currentChecklist = computed(() =>
  (onboardingStore.visibleTasks || []).map((task) => ({
    ...task,
    targetRoute: resolveAuthorizedRoute(task.route),
  }))
);

const completedCount = computed(() => onboardingStore.completedTasksCount);
const totalCount = computed(() => onboardingStore.totalTasksCount);
const progressPercent = computed(() => onboardingStore.progressPercentage);

const ALL_LAUNCHERS = [
  {
    id: "study",
    title: "Studies",
    description:
      "Open a study workspace to navigate lifecycle stages, participant status, protocol versions, and activity.",
    routeSpec: "study",
    badgeText: "Blueprint · Live",
    badgeClass: "badge-active",
    featured: true,
  },
  {
    id: "data-collection",
    title: "Data collection",
    description:
      "Capture clinical visit observations, validate case report forms, and perform source data verification.",
    routeSpec: "ecrf",
    badgeText: "Capture · Live",
    badgeClass: "badge-active",
    featured: true,
  },
  {
    id: "site-readiness",
    title: "Site readiness",
    description:
      "Track site activation requirements, startup milestones, and operational site readiness.",
    routeSpec: "site-readiness",
    badgeText: "Orbit · Live",
    badgeClass: "badge-active",
    featured: true,
  },
  {
    id: "safety-review",
    title: "Safety review",
    description:
      "Triage serious adverse events, evaluate emerging safety signals, and review medical narratives.",
    routeSpec: "safety",
    badgeText: "Vigil · Live",
    badgeClass: "badge-active",
    featured: true,
  },
];

const personaLaunchers = computed(() => {
  return ALL_LAUNCHERS.map((launcher) => {
    const resolvedRoute = resolveAuthorizedRoute(launcher.routeSpec);
    return resolvedRoute ? { ...launcher, route: resolvedRoute } : null;
  }).filter(Boolean);
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

function navigateToRoute(routePathOrName) {
  const resolved = resolveAuthorizedRoute(routePathOrName);
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
  min-width: 0;
  box-sizing: border-box;
}

.overview-svg-icon {
  width: 18px;
  height: 18px;
  flex-shrink: 0;
}

.overview-svg-icon-lg {
  width: 32px;
  height: 32px;
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
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  box-shadow: var(--shadow-lg);
}

.welcome-main {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: 20px;
  flex: 1;
  min-width: 0;
  max-width: 100%;
}

.welcome-text-content {
  min-width: 0;
  max-width: 100%;
  flex: 1 1 12rem;
}

.persona-icon-badge {
  background: var(--semantic-color-inverse-tint);
  color: var(--semantic-color-text-inverse);
  padding: 14px;
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
  overflow-wrap: anywhere;
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
  background: var(--semantic-color-inverse-tint);
  border: 1px solid var(--semantic-color-inverse-tint-strong);
  border-radius: 10px;
  padding: 16px;
  width: 320px;
  max-width: 100%;
  min-width: 0;
  box-sizing: border-box;
  flex-shrink: 0;
  backdrop-filter: blur(4px);
}

.preference-header {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--semantic-color-text-inverse);
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
  color: var(--semantic-color-text-inverse-muted);
  line-height: 1.4;
}

.pref-status-badge {
  margin-top: 12px;
  padding: 6px 10px;
  border-radius: 6px;
  font-size: 0.75rem;
  font-weight: 600;
  background: var(--semantic-badge-candidate-bg);
  color: var(--semantic-badge-candidate-text);
  border: 1px solid var(--semantic-badge-candidate-border);
}

.pref-status-badge.is-bypassed {
  background: var(--semantic-badge-queried-bg);
  color: var(--semantic-badge-queried-text);
  border-color: var(--semantic-badge-queried-border);
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
  box-shadow: var(--shadow-sm);
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
  grid-template-columns: 380px minmax(0, 1fr);
  gap: 24px;
  min-width: 0;
}

@media (max-width: 960px) {
  .overview-header-card {
    flex-direction: column;
  }
  .landing-preference-card {
    width: 100%;
  }
  .overview-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 640px) {
  .overview-hub-container {
    padding: 12px 8px;
  }
  .overview-header-card,
  .overview-section {
    padding: 16px;
  }
}

.overview-section {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 12px;
  padding: 24px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  box-shadow: var(--shadow-sm);
}

.section-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.section-title {
  font-size: 1.2rem;
  font-weight: 700;
  margin: 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
  color: var(--semantic-color-text);
}

.progress-badge {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
  font-size: 0.75rem;
  font-weight: 600;
  padding: 4px 8px;
  border-radius: 12px;
  white-space: nowrap;
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
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
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
  flex: 1 1 10rem;
  min-width: 0;
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
  min-width: 0;
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
  max-width: 100%;
  overflow-wrap: anywhere;
}

.btn-task-launch:hover:not(:disabled) {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-text-on-primary);
  border-color: var(--semantic-color-interactive-primary);
}

.tour-action-footer {
  padding-top: 12px;
  border-top: 1px solid var(--semantic-color-surface-header);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.btn-reset-checklist {
  width: 100%;
  padding: 8px 10px;
  font-size: 0.82rem;
  font-weight: 600;
  border-radius: 8px;
  cursor: pointer;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border-control);
}

.btn-reset-checklist:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.btn-start-tour {
  width: 100%;
  padding: 10px;
  font-size: 0.88rem;
  font-weight: 600;
  border-radius: 8px;
  cursor: pointer;
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border: none;
  transition: background 0.2s ease;
}

.btn-start-tour:hover:not(:disabled) {
  background: var(--semantic-color-primary-dark);
}

.launchers-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 240px), 1fr));
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
  box-shadow: var(--shadow-md);
  border-color: var(--semantic-color-interactive-primary);
}

.launcher-card.is-featured {
  border-color: var(--semantic-color-divider-accent);
  background: var(--semantic-color-info-bg);
}

.launcher-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.launcher-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border-radius: 8px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-interactive-primary);
  border: 1px solid var(--semantic-color-border-muted);
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
}

.launcher-card:hover .btn-launcher-action:not(:disabled) {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-text-on-primary);
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
