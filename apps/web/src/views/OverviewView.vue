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
              :title="'Open ' + task.title"
              @click="navigateToRoute(task.targetRoute)"
            >
              Go <span aria-hidden="true">↗</span
              ><span class="sr-only">to {{ task.title }}</span>
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
import { computed, onMounted } from "vue";
import { getActivePinia } from "pinia";
import { useRoute, useRouter } from "vue-router";
import {
  buildDataCollectionPath,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  resolveSiteReadinessPath,
  resolveWorkspaceNavigationScope,
} from "../canvas/palette";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/productCatalog.js";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import { PERSONA_PRESETS, useAuthStore } from "../stores/auth";
import { useOnboardingStore } from "../stores/onboarding";
import { useStudyCanvasStore } from "../stores/studyCanvas";
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
  const roles = authStore.normalizedRoles || [];
  const preset = PERSONA_PRESETS.find((p) => p.key === currentPersonaKey.value);
  if (
    preset &&
    currentPersonaKey.value !== "super_admin" &&
    roles.includes("sponsor_admin")
  ) {
    return [...preset.requires];
  }
  if (authStore.rawRoles?.length > 0) {
    return roles;
  }
  return preset ? [...preset.requires] : ["sponsor_admin"];
});

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
    return buildStudiesWorkspacePath({
      studyCode,
      siteId,
      subjectCode,
      participantRef,
    });
  }
  if (routeSpec === "ecrf" || routeSpec === "/ecrf") {
    if (isDemoBuild()) {
      return isScreenAvailable("ecrf") &&
        hasRequiredRole(effectiveRoles.value, [
          "site_investigator",
          "crc",
          "data_manager",
          "sponsor_admin",
          "cra",
          "monitor",
        ])
        ? buildDataCollectionPath({
            studyCode,
            siteId: explicitSiteId,
            subjectCode,
            enrollmentId,
          })
        : null;
    }
    return isScreenAvailable("study") &&
      hasRequiredRole(effectiveRoles.value, [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
        "cra",
        "monitor",
      ])
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
      hasRequiredRole(effectiveRoles.value, [
        "cra",
        "monitor",
        "data_manager",
        "sponsor_admin",
      ])
      ? buildScopedWorkflowPath("/ctms", { studyCode, siteId, subjectCode })
      : null;
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
      ? buildScopedWorkflowPath("/ssu", { studyCode, siteId, subjectCode })
      : null;
  }
  if (routeSpec === "etmf" || routeSpec === "/etmf") {
    return isScreenAvailable("etmf") &&
      hasRequiredRole(effectiveRoles.value, [
        "cra",
        "monitor",
        "data_manager",
        "site_investigator",
        "crc",
        "auditor",
        "tmf_auditor",
        "sponsor_admin",
      ])
      ? buildScopedWorkflowPath("/etmf", { studyCode, siteId, subjectCode })
      : null;
  }
  if (routeSpec === "training" || routeSpec === "/training") {
    return isScreenAvailable("training")
      ? buildScopedWorkflowPath("/training", { studyCode, siteId, subjectCode })
      : null;
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
  site_investigator: {
    icon: "PI",
    title: "Principal Investigator Workspace",
    roleName: "Principal Investigator (PI)",
    description:
      "Oversee site clinical conduct, review participant case report forms, and apply 21 CFR Part 11 investigator signatures.",
  },
  cra_monitor: {
    icon: "📊",
    title: "Clinical Monitoring & Site Readiness",
    roleName: "CRA / Clinical Monitor",
    description:
      "Perform source data verification, track site activation readiness, and monitor study conduct.",
  },
  safety_officer: {
    icon: "PV",
    title: "Pharmacovigilance & Safety Review Workspace",
    roleName: "Safety Officer / QPPV",
    description:
      "Monitor serious adverse event narratives, unblinded safety signals, and aggregate pharmacovigilance governance.",
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
  site_investigator: [
    {
      id: "pi-1",
      title: "Case Report Form & Signature Review",
      description:
        "Review clinical visit observations and sign completed case report forms.",
      targetRoute: "/ecrf",
    },
    {
      id: "pi-2",
      title: "Site Readiness & Investigator Oversight",
      description:
        "Confirm site startup obligations and delegation readiness for your site.",
      targetRoute: "site-readiness",
    },
    {
      id: "pi-3",
      title: "Study Conduct & Participant Oversight",
      description:
        "Inspect enrolled participants and governance activity in the study workspace.",
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
  safety_officer: [
    {
      id: "pv-1",
      title: "Study Safety & Conduct Overview",
      description:
        "Inspect participant conduct status, adverse event signals, and protocol versions.",
      targetRoute: "/studies",
    },
    {
      id: "pv-2",
      title: "Unblinded Safety Allocation Governance",
      description:
        "Verify safety governance proposals and audit provenance in the study workspace.",
      targetRoute: "/studies",
    },
    {
      id: "pv-3",
      title: "Workspace Orientation Preferences",
      description:
        "Configure landing preferences and review pharmacovigilance SOP guidance.",
      targetRoute: null,
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
  font-size: 3rem;
  background: var(--semantic-color-inverse-tint);
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

.btn-task-launch:hover {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-text-on-primary);
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
  color: var(--semantic-color-text-on-primary);
  border: none;
  transition: background 0.2s ease;
}

.btn-start-tour:hover {
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
}
</style>
