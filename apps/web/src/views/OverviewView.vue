<template>
  <div class="overview-hub-container" id="overview-hub-root">
    <!-- Welcome Header & Persona Banner -->
    <header class="overview-header-card">
      <div class="welcome-main">
        <div class="persona-icon-badge" aria-hidden="true">
          {{ personaConfig.icon }}
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
          <span class="pref-icon">⚙️</span>
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
              ? "⚡ Direct workspace navigation active"
              : "🏠 Orientation Hub set as default landing"
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
          class="overview-countdown-text"
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
            <span class="section-icon">📋</span> Quick-Start Task Checklist
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
            :class="{ 'is-completed': isTaskCompleted(task.id) }"
          >
            <label :for="'task-' + task.id" class="checklist-item-label">
              <input
                type="checkbox"
                :id="'task-' + task.id"
                class="task-checkbox"
                :checked="isTaskCompleted(task.id)"
                @change="onboardingStore.toggleTaskCompletion(task.id)"
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
              @click="navigateToRoute(task.targetRoute)"
              :title="'Open ' + task.title"
            >
              Go ↗
            </button>
          </li>
        </ul>

        <div class="tour-action-footer">
          <button
            type="button"
            class="btn btn-primary btn-start-tour"
            @click="startGuidedTour"
          >
            🚀 Launch Interactive Guided Tour
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
            <span class="section-icon">🚀</span> Core Workflow Launchers
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
              <span class="launcher-icon">{{ launcher.icon }}</span>
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
                Launch {{ launcher.title }} ↗
              </button>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted } from "vue";
import { useRouter } from "vue-router";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/productCatalog.js";
import { PERSONA_PRESETS, useAuthStore } from "../stores/auth";
import { useOnboardingStore } from "../stores/onboarding";
import { isScreenAvailable } from "../router/availability";
import { hasRequiredRole } from "../router";

const router = useRouter();
const authStore = useAuthStore();
const onboardingStore = useOnboardingStore();

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

const currentPersonaKey = computed(
  () => authStore.currentPersona || "super_admin"
);

const effectiveRoles = computed(() => {
  if (authStore.rawRoles?.length > 0) {
    return authStore.normalizedRoles || [];
  }
  const preset = PERSONA_PRESETS.find((p) => p.key === currentPersonaKey.value);
  return preset ? [...preset.requires] : ["sponsor_admin"];
});

function resolveAuthorizedRoute(routeSpec) {
  if (!routeSpec) return null;
  if (routeSpec === "study" || routeSpec === "/studies") {
    return isScreenAvailable("study") ? "/studies" : null;
  }
  if (routeSpec === "ecrf" || routeSpec === "/ecrf") {
    return isScreenAvailable("ecrf") &&
      hasRequiredRole(effectiveRoles.value, [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
        "cra",
        "monitor",
      ])
      ? "/ecrf"
      : null;
  }
  if (
    routeSpec === "site-readiness" ||
    routeSpec === "ctms" ||
    routeSpec === "/ctms"
  ) {
    if (
      isScreenAvailable("ctms") &&
      hasRequiredRole(effectiveRoles.value, [
        "cra",
        "monitor",
        "data_manager",
        "sponsor_admin",
      ])
    ) {
      return "/ctms";
    }
    if (
      routeSpec === "site-readiness" &&
      isScreenAvailable("ssu") &&
      hasRequiredRole(effectiveRoles.value, ["site_investigator", "crc"])
    ) {
      return "/ssu";
    }
    return null;
  }
  if (routeSpec === "ssu" || routeSpec === "/ssu") {
    return isScreenAvailable("ssu") &&
      hasRequiredRole(effectiveRoles.value, [
        "cra",
        "monitor",
        "data_manager",
        "sponsor_admin",
        "site_investigator",
        "crc",
      ])
      ? "/ssu"
      : null;
  }
  return null;
}

const PERSONA_DETAILS = {
  super_admin: {
    icon: "👑",
    title: "Super Admin Workspace",
    roleName: "Super Admin",
    description:
      "Full oversight across study workspaces, clinical data collection, site readiness, and regulatory governance.",
  },
  sponsor_designer: {
    icon: "📋",
    title: "Sponsor Protocol & Design Workspace",
    roleName: "Sponsor Protocol Designer",
    description:
      "Structure study lifecycle stages, inspect recorded protocol versions, and review clinical schedules.",
  },
  data_manager: {
    icon: "⚙️",
    title: "Clinical Data Management Workspace",
    roleName: "Clinical Data Manager",
    description:
      "Review clinical case report forms, field validation rules, and discrepancy query resolution.",
  },
  site_crc: {
    icon: "🩺",
    title: "Site Investigator & Coordinator Workspace",
    roleName: "Site Coordinator / CRC",
    description:
      "Manage participant enrollment, record clinical visit observations, and track site operational readiness.",
  },
  cra_monitor: {
    icon: "📊",
    title: "Clinical Monitoring & Site Readiness",
    roleName: "CRA / Clinical Monitor",
    description:
      "Perform source data verification, track site activation readiness, and monitor study conduct.",
  },
  auditor: {
    icon: "🔒",
    title: "GxP & Part 11 Regulatory Workspace",
    roleName: "GxP Auditor",
    description:
      "Inspect immutable study activity, protocol version provenance, and 21 CFR Part 11 governance decisions.",
  },
};

const personaConfig = computed(() => {
  return (
    PERSONA_DETAILS[currentPersonaKey.value] || PERSONA_DETAILS.super_admin
  );
});

const CHECKLISTS = {
  super_admin: [
    {
      id: "sa-1",
      title: "Open a Study Workspace",
      description:
        "Open a study by code to inspect lifecycle stages, participants, and activity.",
      targetRoute: "/studies",
    },
    {
      id: "sa-2",
      title: "Review Site Readiness",
      description:
        "Check site activation checklists, startup progress, and readiness status.",
      targetRoute: "site-readiness",
    },
    {
      id: "sa-3",
      title: "Customize Landing Preferences",
      description:
        "Set your default landing behavior or bypass option for future sign-ins.",
      targetRoute: null,
    },
  ],
  sponsor_designer: [
    {
      id: "sd-1",
      title: "Open Study Design Stage",
      description:
        "Open a study workspace to inspect its Design stage and recorded protocol versions.",
      targetRoute: "/studies",
    },
    {
      id: "sd-2",
      title: "Navigate Study Lifecycle",
      description:
        "Verify lifecycle progression across Design, Start, Conduct, Data, Analyze, and Submit.",
      targetRoute: "/studies",
    },
    {
      id: "sd-3",
      title: "Review Protocol Governance",
      description:
        "Inspect open proposals and protocol amendment provenance in the study workspace.",
      targetRoute: "/studies",
    },
  ],
  data_manager: [
    {
      id: "dm-1",
      title: "Validation Rules & Queries",
      description:
        "Inspect real-time field checks and discrepancy notes in Data collection.",
      targetRoute: "/ecrf",
    },
    {
      id: "dm-2",
      title: "Study Conduct & Participant Status",
      description:
        "Review participant counts and open governance proposals in the study workspace.",
      targetRoute: "/studies",
    },
    {
      id: "dm-3",
      title: "Case Report Form Completeness",
      description:
        "Verify participant visit entries and discrepancy resolution status.",
      targetRoute: "/ecrf",
    },
    {
      id: "dm-4",
      title: "Site Readiness & Activation",
      description:
        "Certify startup greenlight readiness and activate compliant study sites.",
      targetRoute: "site-readiness",
    },
  ],
  site_crc: [
    {
      id: "crc-1",
      title: "Site Readiness Check",
      description:
        "Verify site activation readiness and startup requirements for your site.",
      targetRoute: "site-readiness",
    },
    {
      id: "crc-2",
      title: "Participant Enrollment & Data Collection",
      description:
        "Register participants and complete clinical visit case report forms.",
      targetRoute: "/ecrf",
    },
    {
      id: "crc-3",
      title: "Study Participant Overview",
      description:
        "Inspect participant statuses and conduct activity in the study workspace.",
      targetRoute: "/studies",
    },
  ],
  cra_monitor: [
    {
      id: "cra-1",
      title: "Site Readiness & Activation Review",
      description:
        "Track site onboarding milestones and operational activation state.",
      targetRoute: "site-readiness",
    },
    {
      id: "cra-2",
      title: "Source Data Verification (SDV)",
      description:
        "Review and verify clinical form entries at the participant level.",
      targetRoute: "/ecrf",
    },
    {
      id: "cra-3",
      title: "Monitor Study Conduct",
      description:
        "Inspect study-wide participant counts and site status in the study workspace.",
      targetRoute: "/studies",
    },
  ],
  auditor: [
    {
      id: "aud-1",
      title: "Study Activity Trail Verification",
      description:
        "Open a study workspace to inspect chronological activity and provenance.",
      targetRoute: "/studies",
    },
    {
      id: "aud-2",
      title: "Protocol Version Provenance",
      description:
        "Verify recorded protocol versions and effective lifecycle dates.",
      targetRoute: "/studies",
    },
    {
      id: "aud-3",
      title: "Governance & Decision Review",
      description:
        "Inspect proposal review records and 21 CFR Part 11 accountability.",
      targetRoute: "/studies",
    },
  ],
};

const currentChecklist = computed(() => {
  const raw = CHECKLISTS[currentPersonaKey.value] || CHECKLISTS.super_admin;
  return raw.map((item) => ({
    ...item,
    targetRoute: resolveAuthorizedRoute(item.targetRoute),
  }));
});

function isTaskCompleted(taskId) {
  return (onboardingStore.completedTasks || []).includes(taskId);
}

const completedCount = computed(() => {
  return currentChecklist.value.filter((t) => isTaskCompleted(t.id)).length;
});

const progressPercent = computed(() => {
  if (currentChecklist.value.length === 0) return 0;
  return Math.round(
    (completedCount.value / currentChecklist.value.length) * 100
  );
});

const ALL_LAUNCHERS = [
  {
    id: "study",
    title: "Studies",
    icon: "📐",
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
    icon: "🩺",
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
    icon: "📊",
    description:
      "Track site activation requirements, startup milestones, and operational site readiness.",
    routeSpec: "site-readiness",
    badgeText: "Orbit · Live",
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
}

.btn-task-launch:hover {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-interactive-primary);
}

.tour-action-footer {
  padding-top: 12px;
  border-top: 1px solid var(--semantic-color-surface-header);
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
}

.launcher-card:hover .btn-launcher-action {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-interactive-primary);
}
</style>
