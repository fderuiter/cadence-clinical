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
import { computed } from "vue";
import { useRouter } from "vue-router";
import { useAuthStore } from "../stores/auth";
import { useOnboardingStore } from "../stores/onboarding";

const router = useRouter();
const authStore = useAuthStore();
const onboardingStore = useOnboardingStore();

const userName = computed(() => {
  if (authStore.user?.firstName) {
    return `${authStore.user.firstName} ${authStore.user.lastName}`.trim();
  }
  return authStore.identity?.username || "Clinical Researcher";
});

const currentPersonaKey = computed(
  () => authStore.currentPersona || "super_admin"
);

const PERSONA_DETAILS = {
  super_admin: {
    icon: "👑",
    title: "Super Admin Workspace",
    roleName: "Super Admin",
    description:
      "Full oversight across protocol authoring, eCRF capture, CTMS monitoring, and GxP regulatory audit trails.",
  },
  sponsor_designer: {
    icon: "📋",
    title: "Sponsor Protocol & SoA Design Hub",
    roleName: "Sponsor Protocol Designer",
    description:
      "Digitize USDM protocols, structure Schedule of Activities (SoA), and author eCRF form templates.",
  },
  data_manager: {
    icon: "⚙️",
    title: "Clinical Data Management Cockpit",
    roleName: "Clinical Data Manager",
    description:
      "Configure study validation rules, review MedDRA/WHODrug coding discrepancies, and manage data locks.",
  },
  site_crc: {
    icon: "🩺",
    title: "Site Investigator & CRC Workspace",
    roleName: "Site Coordinator / CRC",
    description:
      "Capture participant eCRF data, verify eConsent signatures, and upload central DICOM imaging.",
  },
  cra_monitor: {
    icon: "📊",
    title: "CRA Clinical Monitoring Operations",
    roleName: "CRA / Clinical Monitor",
    description:
      "Perform Source Data Verification (SDV), track site activation greenlights, and audit eTMF binders.",
  },
  auditor: {
    icon: "🔒",
    title: "GxP & Part 11 Regulatory Vault",
    roleName: "GxP Auditor",
    description:
      "Inspect Part 11 immutable audit ledgers, cryptographic signature chains, and regulatory compliance matrices.",
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
      title: "Verify System Genesis Ledger",
      description:
        "Confirm cryptographic ledger initialization and Part 11 audit status.",
      targetRoute: "ecrf",
    },
    {
      id: "sa-2",
      title: "Review Operational Tools",
      description:
        "Explore eCRF data capture, CTMS monitoring, and audit log suites.",
      targetRoute: "ctms",
    },
    {
      id: "sa-3",
      title: "Customize Landing Preferences",
      description:
        "Set your default landing behavior or bypass option for future logins.",
      targetRoute: null,
    },
  ],
  sponsor_designer: [
    {
      id: "sd-1",
      title: "Protocol AI Ingestion",
      description: "Upload protocol specification for USDM v3/v4 digitization.",
      targetRoute: "ecrf",
    },
    {
      id: "sd-2",
      title: "Schedule of Activities (SoA)",
      description: "Define visit structure and procedure matrix.",
      targetRoute: "ecrf",
    },
    {
      id: "sd-3",
      title: "Author eConsent & ICF",
      description: "Build informed consent forms and comprehension quizzes.",
      targetRoute: "ecrf",
    },
  ],
  data_manager: [
    {
      id: "dm-1",
      title: "Validation Rules & Queries",
      description: "Set automated edit checks and query triggers.",
      targetRoute: "ecrf",
    },
    {
      id: "dm-2",
      title: "Medical Coding Review",
      description: "Review verbatim terms with MedDRA / WHODrug dictionaries.",
      targetRoute: "ecrf",
    },
    {
      id: "dm-3",
      title: "Data Lock Preparation",
      description:
        "Verify subject form completeness prior to locking database.",
      targetRoute: "ecrf",
    },
  ],
  site_crc: [
    {
      id: "crc-1",
      title: "Site Readiness Check",
      description: "Verify site activation greenlight and delegation log.",
      targetRoute: "ecrf",
    },
    {
      id: "crc-2",
      title: "Participant Enrollment & eCRF",
      description: "Register subject and complete visit eCRF forms.",
      targetRoute: "ecrf",
    },
    {
      id: "crc-3",
      title: "Central DICOM Upload",
      description: "Upload medical imaging files for dual-reader adjudication.",
      targetRoute: "ecrf",
    },
  ],
  cra_monitor: [
    {
      id: "cra-1",
      title: "Site Greenlight Review",
      description: "Track CTMS site onboarding milestones and approval state.",
      targetRoute: "ctms",
    },
    {
      id: "cra-2",
      title: "Source Data Verification (SDV)",
      description: "Review and verify eCRF data points at participant level.",
      targetRoute: "ecrf",
    },
    {
      id: "cra-3",
      title: "eTMF Document Vault Audit",
      description: "Confirm essential document completeness and signatures.",
      targetRoute: "ctms",
    },
  ],
  auditor: [
    {
      id: "aud-1",
      title: "Part 11 Audit Trail Verification",
      description: "Inspect tamper-evident cryptographic block hashes.",
      targetRoute: "ecrf",
    },
    {
      id: "aud-2",
      title: "eTMF Binder Compliance",
      description: "Verify regulatory filing compliance and signature logs.",
      targetRoute: "ctms",
    },
    {
      id: "aud-3",
      title: "Export GxP Certificate",
      description: "Generate regulatory compliance verification report.",
      targetRoute: "ctms",
    },
  ],
};

const currentChecklist = computed(() => {
  return CHECKLISTS[currentPersonaKey.value] || CHECKLISTS.super_admin;
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
    id: "ecrf",
    title: "eCRF Data Capture",
    icon: "🩺",
    description:
      "Capture clinical study observations, view subject records, and perform SDV.",
    route: "/ecrf",
    badgeText: "Active .NET",
    badgeClass: "badge-active",
    featured: true,
    roles: [
      "site_investigator",
      "crc",
      "data_manager",
      "sponsor_admin",
      "cra",
      "monitor",
    ],
  },
  {
    id: "ctms",
    title: "CTMS Operations",
    icon: "📊",
    description:
      "Manage site onboarding, greenlight readiness, and eTMF compliance status.",
    route: "/ctms",
    badgeText: "Active .NET",
    badgeClass: "badge-active",
    featured: true,
    roles: ["cra", "monitor", "sponsor_admin", "site_investigator", "crc"],
  },
  {
    id: "study",
    title: "Clinical Canvas Workspace",
    icon: "🎨",
    description:
      "Study-centric lifecycle workspace holding participant counts and site status.",
    route: "/studies",
    badgeText: "Active .NET",
    badgeClass: "badge-active",
    featured: false,
    roles: [],
  },
  {
    id: "digitization",
    title: "Protocol AI Digitizer",
    icon: "⚡",
    description:
      "Digitize protocol documents into CDISC USDM v3/v4 structured specifications.",
    route: "/digitization",
    badgeText: "Protocol Suite",
    badgeClass: "badge-neutral",
    featured: false,
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
  },
  {
    id: "mdr",
    title: "Protocol & SoA Designer",
    icon: "📋",
    description:
      "Author Schedule of Activities (SoA) matrices and standardized study metadata.",
    route: "/mdr",
    badgeText: "Designer",
    badgeClass: "badge-neutral",
    featured: false,
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
  },
  {
    id: "icf-builder",
    title: "eConsent & ICF Builder",
    icon: "📝",
    description:
      "Design informed consent forms, comprehension quizzes, and translation tabs.",
    route: "/icf-builder",
    badgeText: "Consent",
    badgeClass: "badge-neutral",
    featured: false,
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
  },
  {
    id: "rules",
    title: "Study Rules & Queries",
    icon: "⚙️",
    description:
      "Configure automated edit checks, validation rules, and discrepancy query rules.",
    route: "/rules",
    badgeText: "Validation",
    badgeClass: "badge-neutral",
    featured: false,
    roles: ["sponsor_designer", "data_manager", "sponsor_admin"],
  },
  {
    id: "audit",
    title: "Part 11 Audit Ledger",
    icon: "🔒",
    description:
      "Review GxP 21 CFR Part 11 audit trails, signature records, and hash chains.",
    route: "/audit",
    badgeText: "Compliance",
    badgeClass: "badge-neutral",
    featured: false,
    roles: ["auditor", "tmf_auditor", "sponsor_admin"],
  },
];

const personaLaunchers = computed(() => {
  return ALL_LAUNCHERS;
});

function handleBypassToggle(event) {
  const isChecked = event.target.checked;
  onboardingStore.setBypassOverview(isChecked);
}

function navigateToRoute(routePathOrName) {
  if (!routePathOrName) return;
  if (routePathOrName.startsWith("/")) {
    router.push(routePathOrName);
  } else {
    router.push({ name: routePathOrName });
  }
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
  font-family: var(--font, system-ui, -apple-system, sans-serif);
  color: var(--primary, #0f172a);
}

.overview-header-card {
  background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
  color: #ffffff;
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
  color: #ffffff;
}

.welcome-subtitle {
  font-size: 0.95rem;
  color: #94a3b8;
  margin: 0 0 10px 0;
  font-weight: 500;
}

.persona-description {
  font-size: 0.9rem;
  color: #cbd5e1;
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
  color: #f1f5f9; /* deid-ignore */
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
  width: 18px;
  height: 18px;
  margin-top: 2px;
  cursor: pointer;
  accent-color: #38bdf8;
}

.toggle-text {
  font-size: 0.8rem;
  color: #e2e8f0; /* deid-ignore */
  line-height: 1.4;
}

.pref-status-badge {
  margin-top: 12px;
  padding: 6px 10px;
  border-radius: 6px;
  font-size: 0.75rem;
  font-weight: 600;
  background: rgba(56, 189, 248, 0.15);
  color: #38bdf8;
  border: 1px solid rgba(56, 189, 248, 0.3);
}

.pref-status-badge.is-bypassed {
  background: rgba(245, 158, 11, 0.15);
  color: #fbbf24;
  border-color: rgba(245, 158, 11, 0.3);
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
  background: #ffffff;
  border: 1px solid #e2e8f0;
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
  color: #0f172a;
}

.progress-badge {
  background: #f1f5f9;
  color: #0f172a;
  font-size: 0.75rem;
  font-weight: 600;
  padding: 4px 8px;
  border-radius: 12px;
}

.subtitle-tag {
  font-size: 0.8rem;
  color: #64748b;
  font-weight: 500;
}

.section-intro {
  font-size: 0.85rem;
  color: #64748b;
  margin: 0 0 16px 0;
}

.progress-bar-container {
  height: 6px;
  background: #e2e8f0;
  border-radius: 3px;
  overflow: hidden;
  margin-bottom: 20px;
}

.progress-bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #3b82f6, #10b981);
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
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  transition: all 0.2s ease;
}

.checklist-item-row:hover {
  background: #f1f5f9;
  border-color: #cbd5e1;
}

.checklist-item-row.is-completed {
  background: #f0fdf4;
  border-color: #bbf7d0;
}

.checklist-item-label {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  cursor: pointer;
  flex: 1;
}

.task-checkbox {
  width: 18px;
  height: 18px;
  margin-top: 2px;
  cursor: pointer;
  accent-color: #10b981;
}

.task-content {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.task-title {
  font-size: 0.9rem;
  font-weight: 600;
  color: #1e293b;
}

.is-completed .task-title {
  text-decoration: line-through;
  color: #15803d;
}

.task-desc {
  font-size: 0.78rem;
  color: #64748b;
  line-height: 1.3;
}

.btn-task-launch {
  padding: 4px 8px;
  font-size: 0.75rem;
  background: #ffffff;
  border: 1px solid #cbd5e1;
  color: #3b82f6;
  border-radius: 4px;
  cursor: pointer;
  font-weight: 600;
  white-space: nowrap;
  margin-left: 8px;
}

.btn-task-launch:hover {
  background: #3b82f6;
  color: #ffffff;
  border-color: #3b82f6;
}

.tour-action-footer {
  padding-top: 12px;
  border-top: 1px solid #f1f5f9;
}

.btn-start-tour {
  width: 100%;
  padding: 10px;
  font-size: 0.88rem;
  font-weight: 600;
  border-radius: 8px;
  cursor: pointer;
  background: #0284c7;
  color: #ffffff;
  border: none;
  transition: background 0.2s ease;
}

.btn-start-tour:hover {
  background: #0369a1;
}

.launchers-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 16px;
}

.launcher-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
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
  border-color: #3b82f6;
}

.launcher-card.is-featured {
  border-color: #93c5fd;
  background: #eff6ff;
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
  background: #dcfce7;
  color: #15803d;
}

.badge-neutral {
  background: #f1f5f9;
  color: #475569;
}

.launcher-title {
  font-size: 1rem;
  font-weight: 700;
  color: #0f172a;
  margin: 0 0 6px 0;
}

.launcher-description {
  font-size: 0.8rem;
  color: #64748b;
  margin: 0 0 16px 0;
  line-height: 1.4;
  flex: 1;
}

.btn-launcher-action {
  width: 100%;
  padding: 8px 12px;
  font-size: 0.82rem;
  font-weight: 600;
  background: #ffffff;
  color: #2563eb;
  border: 1px solid #bfdbfe;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.launcher-card:hover .btn-launcher-action {
  background: #2563eb;
  color: #ffffff;
  border-color: #2563eb;
}
</style>
