import { defineStore } from "pinia";
import { useAuthStore } from "./auth";

export const DEFAULT_SETUP_TASKS = [
  {
    id: "task_protocol",
    title: "Digitize & Author Protocol",
    description:
      "Upload protocol document or author study metadata and objectives",
    route: "/digitization",
    completed: false,
    keywords: ["digitization", "digitize", "protocol", "welcome"],
  },
  {
    id: "task_soa",
    title: "Configure Schedule of Activities",
    description: "Define study arms, epochs, and visit schedule matrix",
    route: "/mdr",
    completed: false,
    keywords: ["soa", "schedule", "arm", "epoch"],
  },
  {
    id: "task_crf",
    title: "Design eCRF Visual Forms",
    description: "Build clinical data collection forms in the visual canvas",
    route: "/mdr?tab=canvas",
    completed: false,
    keywords: ["canvas", "crf", "form"],
  },
  {
    id: "task_rules",
    title: "Configure Study Rules & Queries",
    description: "Set up automated univariate and multivariate edit checks",
    route: "/rules",
    completed: false,
    keywords: ["rule", "rules", "query", "queries"],
  },
  {
    id: "task_exports",
    title: "Review & Export Study Package",
    description: "Validate dataset definitions and export ODM / USDM packages",
    route: "/exports",
    completed: false,
    keywords: ["export", "exports", "package", "odm"],
  },
];

function getActivePersona() {
  if (typeof window !== "undefined") {
    try {
      const authStore = useAuthStore();
      if (authStore && authStore.currentPersona) {
        return authStore.currentPersona;
      }
    } catch {
      // Pinia or auth store may not be initialized yet
    }
  }
  return "super_admin";
}

function migrateLegacyKeys() {
  if (typeof window === "undefined" || !window.localStorage) return;
  try {
    const legacyStep = window.localStorage.getItem("onboarding_current_step");
    const legacyIsActive = window.localStorage.getItem("onboarding_is_active");
    const legacyDisabled = window.localStorage.getItem("onboarding_disabled");
    const legacyEvents = window.localStorage.getItem("onboarding_events");
    const legacyTasks = window.localStorage.getItem("onboarding_tasks");

    if (
      legacyStep !== null ||
      legacyIsActive !== null ||
      legacyDisabled !== null ||
      legacyEvents !== null ||
      legacyTasks !== null
    ) {
      if (
        legacyStep !== null &&
        window.localStorage.getItem("onboarding_super_admin_current_step") ===
          null
      ) {
        window.localStorage.setItem(
          "onboarding_super_admin_current_step",
          legacyStep
        );
      }
      if (
        legacyIsActive !== null &&
        window.localStorage.getItem("onboarding_super_admin_is_active") === null
      ) {
        window.localStorage.setItem(
          "onboarding_super_admin_is_active",
          legacyIsActive
        );
      }
      if (
        legacyDisabled !== null &&
        window.localStorage.getItem("onboarding_super_admin_disabled") === null
      ) {
        window.localStorage.setItem(
          "onboarding_super_admin_disabled",
          legacyDisabled
        );
      }
      if (
        legacyEvents !== null &&
        window.localStorage.getItem("onboarding_super_admin_events") === null
      ) {
        window.localStorage.setItem(
          "onboarding_super_admin_events",
          legacyEvents
        );
      }
      if (
        legacyTasks !== null &&
        window.localStorage.getItem("onboarding_super_admin_tasks") === null
      ) {
        window.localStorage.setItem(
          "onboarding_super_admin_tasks",
          legacyTasks
        );
      }

      window.localStorage.removeItem("onboarding_current_step");
      window.localStorage.removeItem("onboarding_is_active");
      window.localStorage.removeItem("onboarding_disabled");
      window.localStorage.removeItem("onboarding_events");
      window.localStorage.removeItem("onboarding_tasks");
    }
  } catch (e) {
    console.error("Failed to migrate legacy onboarding storage keys", e);
  }
}

function readStorageForPersona(personaKey) {
  let currentStep = 1;
  let isActive = true;
  let disabled = false;
  let bypassOverview = false;
  let completedTasks = [];
  let events = [];
  let tasks = JSON.parse(JSON.stringify(DEFAULT_SETUP_TASKS));

  if (typeof window !== "undefined" && window.localStorage) {
    try {
      migrateLegacyKeys();

      const stepKey = `onboarding_${personaKey}_current_step`;
      const activeKey = `onboarding_${personaKey}_is_active`;
      const disabledKey = `onboarding_${personaKey}_disabled`;
      const bypassKey = `onboarding_${personaKey}_bypass_overview`;
      const completedTasksKey = `onboarding_${personaKey}_completed_tasks`;
      const eventsKey = `onboarding_${personaKey}_events`;
      const setupTasksKey = `onboarding_${personaKey}_tasks`;

      const storedStep = window.localStorage.getItem(stepKey);
      if (storedStep !== null) {
        const parsed = parseInt(storedStep, 10);
        if (!isNaN(parsed) && parsed > 0) {
          currentStep = parsed;
        }
      }

      const storedDisabled = window.localStorage.getItem(disabledKey);
      disabled = storedDisabled === "true";

      const storedBypass = window.localStorage.getItem(bypassKey);
      bypassOverview = storedBypass === "true";

      const storedIsActive = window.localStorage.getItem(activeKey);
      if (disabled) {
        isActive = false;
      } else if (storedIsActive !== null) {
        isActive = storedIsActive === "true";
      } else {
        isActive = true;
      }

      const storedCompletedTasks =
        window.localStorage.getItem(completedTasksKey);
      if (storedCompletedTasks !== null) {
        try {
          const parsedTasks = JSON.parse(storedCompletedTasks);
          if (Array.isArray(parsedTasks)) {
            completedTasks = parsedTasks;
          }
        } catch (e) {
          console.error(
            "Failed to parse onboarding completed tasks from localStorage",
            e
          );
        }
      }

      const storedEvents = window.localStorage.getItem(eventsKey);
      if (storedEvents !== null) {
        try {
          const parsedEvents = JSON.parse(storedEvents);
          if (Array.isArray(parsedEvents)) {
            events = parsedEvents;
          }
        } catch (e) {
          console.error(
            "Failed to parse onboarding events state from localStorage",
            e
          );
        }
      }

      const storedSetupTasks = window.localStorage.getItem(setupTasksKey);
      if (storedSetupTasks !== null) {
        try {
          const parsedTasks = JSON.parse(storedSetupTasks);
          if (Array.isArray(parsedTasks) && parsedTasks.length > 0) {
            tasks = DEFAULT_SETUP_TASKS.map((defTask) => {
              const found = parsedTasks.find((t) => t.id === defTask.id);
              return found
                ? { ...defTask, completed: Boolean(found.completed) }
                : defTask;
            });
          }
        } catch (e) {
          console.error(
            "Failed to parse onboarding tasks state from localStorage",
            e
          );
        }
      }
    } catch (e) {
      console.error("Failed to parse onboarding state from localStorage", e);
    }
  }

  return {
    currentStep,
    isActive,
    disabled,
    bypassOverview,
    completedTasks,
    events,
    tasks,
  };
}

export const ROLE_STEP_SEQUENCES = {
  sponsor_designer: [
    {
      step: 1,
      routeName: "mdr",
      query: { tab: "soa" },
      targetSelector: null,
      title: "Welcome to the Sandbox",
      description:
        "This interactive sandbox guides you through designing clinical trial protocol metadata. Let's explore the workspace together step-by-step!",
      prompt:
        "Welcome, Designer! Ready to architect clinical protocols and CDISC USDM data structures with maximum accuracy?",
    },
    {
      step: 2,
      routeName: "mdr",
      query: { tab: "soa" },
      targetSelector: ".tab-btn-soa",
      title: "Schedule of Activities",
      description:
        "This tab shows the Schedule of Activities (SoA). Here you can build trial cohorts, epochs, and define study visits. Try adding an Arm or Epoch to see updates in real-time.",
      prompt:
        "Design the perfect Schedule of Activities with clinical precision for your study arms and epochs.",
    },
    {
      step: 3,
      routeName: "mdr",
      query: { tab: "mdr" },
      targetSelector: ".tab-btn-mdr",
      title: "MDR Concept Browse",
      description:
        "This tab allows you to browse concepts from standard clinical controlled vocabularies. Align your variables with CDISC definitions directly in the sandbox.",
      prompt:
        "Browse and bind CDISC terminology concepts directly into your study parameters.",
    },
    {
      step: 4,
      routeName: "mdr",
      query: { tab: "diff" },
      targetSelector: ".tab-btn-diff",
      title: "Alignment & Differences",
      description:
        "This tab shows real-time changes compared to standard CDISC templates, allowing you to track protocol deviations and structural changes as you edit.",
      prompt:
        "Audit and verify that your clinical data elements are strictly aligned with GxP and regulatory standards.",
    },
    {
      step: 5,
      routeName: "mdr",
      query: { tab: "soa" },
      targetSelector: null,
      title: "Tour Complete!",
      description:
        "Your trial design and onboarding events are safely saved in your browser's persistent storage. Turn on Onboarding Telemetry in the footer to inspect captured user interactions!",
      prompt:
        "All set! Your clinical metadata schema is fully authored, mapped, and persisted.",
    },
  ],

  data_manager: [
    {
      step: 1,
      routeName: "rules",
      query: { tab: "rules" },
      targetSelector: null,
      title: "Welcome to Data Management",
      description:
        "Welcome to the Rules & Data Quality engine. Here you can configure automated edit checks, manage discrepancies, and inspect clinical logic.",
      prompt:
        "Welcome, Data Manager! You can map concepts, curate eCRF templates, and inspect data collection rules.",
    },
    {
      step: 2,
      routeName: "rules",
      query: { tab: "rules" },
      targetSelector: ".tab-btn-rules",
      title: "Edit Checks & Logic Rules",
      description:
        "Configure univariate and multivariate edit checks to validate incoming eCRF submissions in real time.",
      prompt:
        "Validate that all scheduled study activities map directly to clean data collection points.",
    },
    {
      step: 3,
      routeName: "rules",
      query: { tab: "queries" },
      targetSelector: ".tab-btn-queries",
      title: "Discrepancy & Query Dispatch",
      description:
        "Monitor and manage automated queries raised by the auto-query engine during participant data capture.",
      prompt:
        "Enforce standard terminology to prevent data entry inconsistencies during downstream capture.",
    },
    {
      step: 4,
      routeName: "mdr",
      query: { tab: "mdr" },
      targetSelector: ".tab-btn-mdr",
      title: "MDR Concept Alignment",
      description:
        "Cross-reference edit check variables with standardized CDISC controlled terminology in the metadata repository.",
      prompt:
        "Generate alignment metrics and difference reports to ensure complete database lock compliance.",
    },
    {
      step: 5,
      routeName: "rules",
      query: { tab: "rules" },
      targetSelector: null,
      title: "Tour Complete!",
      description:
        "Your data validation configuration is complete and saved. Inspect telemetry events in the diagnostic drawer at any time.",
      prompt:
        "Fantastic! Your clinical metadata definitions are securely stored and ready for electronic capture mapping.",
    },
  ],

  cra_monitor: [
    {
      step: 1,
      routeName: "ctms",
      targetSelector: null,
      title: "Welcome to Clinical Operations",
      description:
        "Welcome to CTMS & Monitoring. Here you can oversee site startup, operational milestones, monitoring visits, and subject metrics.",
      prompt:
        "Welcome, CRA! Oversee site operations, monitoring visits, and subject tracking across all active clinical sites.",
    },
    {
      step: 2,
      routeName: "ctms",
      targetSelector: "#kpi-open-queries",
      title: "Operational Key Metrics",
      description:
        "Track open queries, recruitment progress, and protocol compliance across study sites in real-time.",
      prompt:
        "Review site open query volume and operational milestone progress across all trial locations.",
    },
    {
      step: 3,
      routeName: "ctms",
      targetSelector: ".tabs-container",
      title: "Site & Delegation Logs",
      description:
        "Manage site startup documents, investigator delegation of authority, and monitoring visit reports.",
      prompt:
        "Manage site operations, investigator delegation logs, and financial disbursement milestones.",
    },
    {
      step: 4,
      routeName: "ecrf",
      targetSelector: ".ecrf-container",
      title: "Source Data Verification",
      description:
        "Navigate to participant eCRF records to perform source data verification (SDV) and manage clinical queries.",
      prompt:
        "Perform source data verification on eCRF forms to ensure complete Part 11 trial compliance.",
    },
    {
      step: 5,
      routeName: "ctms",
      targetSelector: null,
      title: "Tour Complete!",
      description:
        "Your monitoring dashboard and operational workflows are fully setup. Access telemetry logs in the footer anytime.",
      prompt:
        "All set! Your monitoring dashboard is fully ready for site management and source data verification.",
    },
  ],

  auditor: [
    {
      step: 1,
      routeName: "audit",
      targetSelector: null,
      title: "Welcome to Regulatory & Audit",
      description:
        "Welcome to the GxP & Part 11 Audit Trail viewer. Reconstruct point-in-time system state and verify cryptographic hashes.",
      prompt:
        "Welcome, Auditor! Inspect bitemporal audit logs, electronic signatures, and regulatory compliance state.",
    },
    {
      step: 2,
      routeName: "audit",
      targetSelector: ".audit-filter-bar",
      title: "Bitemporal Audit Trail Search",
      description:
        "Filter system audit entries by entity, user identity, action, and valid/recorded time intervals.",
      prompt:
        "Filter cryptographic audit records by entity, user, or timestamp range for 21 CFR Part 11 inspections.",
    },
    {
      step: 3,
      routeName: "audit",
      targetSelector: ".audit-ledger-card",
      title: "Cryptographic Hash Chain",
      description:
        "Verify SHA-256 Merkle block hashes and predecessor linkages to ensure tamper-proof audit log integrity.",
      prompt:
        "Verify SHA-256 Merkle block hashes for gap-free 21 CFR Part 11 electronic record compliance.",
    },
    {
      step: 4,
      routeName: "amendment-diff",
      targetSelector: ".diff-summary-card",
      title: "Protocol Version Differences",
      description:
        "Review automated protocol amendment difference reports to verify metadata changes across study versions.",
      prompt:
        "Review schema differences and protocol amendments to guarantee full regulatory change traceability.",
    },
    {
      step: 5,
      routeName: "audit",
      targetSelector: null,
      title: "Tour Complete!",
      description:
        "Audit trail inspection tools and compliance records are verified. Check telemetry events in the footer drawer.",
      prompt:
        "Excellent! Complete regulatory audit logs and compliance state are verified and inspection-ready.",
    },
  ],

  site_crc: [
    {
      step: 1,
      routeName: "ecrf",
      targetSelector: null,
      title: "Welcome to Electronic Data Capture",
      description:
        "Welcome to EDC & Participant Management. Screen subjects, record visit observations, and manage eCRF forms.",
      prompt:
        "Welcome, Site Coordinator! Manage participant data entry, visits, and eCRF forms for your study site.",
    },
    {
      step: 2,
      routeName: "ecrf",
      targetSelector: ".subject-selector-card",
      title: "Participant Selection & Screening",
      description:
        "Select study participants, view visit progress, and initiate new subject screening workflows.",
      prompt:
        "Select participants and navigate study visit encounters for accurate data capture.",
    },
    {
      step: 3,
      routeName: "ecrf",
      targetSelector: ".ecrf-form-renderer",
      title: "eCRF Data Entry Forms",
      description:
        "Enter vital signs, lab results, and clinical observations with automated real-time validation checks.",
      prompt:
        "Enter clinical trial observations with real-time edit checks and immediate query alerts.",
    },
    {
      step: 4,
      routeName: "ctms",
      targetSelector: ".tabs-container",
      title: "Site Operational Milestones",
      description:
        "View site operational milestones, delegation logs, and subject stipend disbursement statuses.",
      prompt:
        "Check delegation logs and site operational startup tasks to maintain site readiness.",
    },
    {
      step: 5,
      routeName: "ecrf",
      targetSelector: null,
      title: "Tour Complete!",
      description:
        "Your EDC workspace orientation is complete. View telemetry events in the footer diagnostic drawer anytime.",
      prompt:
        "Great job! Participant data capture and visit schedules are ready for trial operations.",
    },
  ],

  super_admin: [
    {
      step: 1,
      routeName: "mdr",
      query: { tab: "soa" },
      targetSelector: null,
      title: "Welcome to the Sandbox",
      description:
        "This interactive sandbox guides you through designing clinical trial protocol metadata. Let's explore the workspace together step-by-step!",
      prompt:
        "Welcome, Administrator! Oversee the full digital data flow, audit trails, and multi-tenant compliance settings.",
    },
    {
      step: 2,
      routeName: "mdr",
      query: { tab: "soa" },
      targetSelector: ".tab-btn-soa",
      title: "Schedule of Activities",
      description:
        "This tab shows the Schedule of Activities (SoA). Here you can build trial cohorts, epochs, and define study visits. Try adding an Arm or Epoch to see updates in real-time.",
      prompt:
        "Review and approve the global Schedule of Activities to ensure operational feasibility.",
    },
    {
      step: 3,
      routeName: "mdr",
      query: { tab: "mdr" },
      targetSelector: ".tab-btn-mdr",
      title: "MDR Concept Browse",
      description:
        "This tab allows you to browse concepts from standard clinical controlled vocabularies. Align your variables with CDISC definitions directly in the sandbox.",
      prompt:
        "Browse controlled vocabularies and manage organization-wide metadata catalogs.",
    },
    {
      step: 4,
      routeName: "mdr",
      query: { tab: "diff" },
      targetSelector: ".tab-btn-diff",
      title: "Alignment & Differences",
      description:
        "This tab shows real-time changes compared to standard CDISC templates, allowing you to track protocol deviations and structural changes as you edit.",
      prompt:
        "Analyze difference reports to ensure zero schema drift across all study versions.",
    },
    {
      step: 5,
      routeName: "mdr",
      query: { tab: "soa" },
      targetSelector: null,
      title: "Tour Complete!",
      description:
        "Your trial design and onboarding events are safely saved in your browser's persistent storage. Turn on Onboarding Telemetry in the footer to inspect captured user interactions!",
      prompt:
        "Excellent! The study configuration is fully locked, logged, and securely backed up.",
    },
  ],
};

function getSequenceForPersona(personaKey) {
  let authStore = null;
  try {
    authStore = useAuthStore();
  } catch {
    // Pinia or auth store not ready
  }

  const roles = authStore?.normalizedRoles || [];

  if (
    personaKey &&
    personaKey !== "super_admin" &&
    ROLE_STEP_SEQUENCES[personaKey]
  ) {
    return ROLE_STEP_SEQUENCES[personaKey];
  }

  if (personaKey === "designer" || personaKey === "study_designer") {
    return ROLE_STEP_SEQUENCES.sponsor_designer;
  }
  if (personaKey === "crc") {
    return ROLE_STEP_SEQUENCES.site_crc;
  }
  if (personaKey === "cra" || personaKey === "monitor") {
    return ROLE_STEP_SEQUENCES.cra_monitor;
  }
  if (personaKey === "tmf_auditor") {
    return ROLE_STEP_SEQUENCES.auditor;
  }
  if (personaKey === "sponsor_admin") {
    return ROLE_STEP_SEQUENCES.super_admin;
  }

  if (roles.length > 0) {
    if (
      roles.includes("sponsor_designer") &&
      !roles.includes("sponsor_admin")
    ) {
      return ROLE_STEP_SEQUENCES.sponsor_designer;
    }
    if (roles.includes("data_manager") && !roles.includes("sponsor_admin")) {
      return ROLE_STEP_SEQUENCES.data_manager;
    }
    if (roles.includes("site_investigator") || roles.includes("crc")) {
      return ROLE_STEP_SEQUENCES.site_crc;
    }
    if (roles.includes("cra") || roles.includes("monitor")) {
      return ROLE_STEP_SEQUENCES.cra_monitor;
    }
    if (roles.includes("auditor") || roles.includes("tmf_auditor")) {
      return ROLE_STEP_SEQUENCES.auditor;
    }
  }

  if (ROLE_STEP_SEQUENCES[personaKey]) {
    return ROLE_STEP_SEQUENCES[personaKey];
  }

  return (
    ROLE_STEP_SEQUENCES.super_admin || ROLE_STEP_SEQUENCES.sponsor_designer
  );
}

export const useOnboardingStore = defineStore("onboarding", {
  state: () => {
    const activePersona = getActivePersona();
    const initial = readStorageForPersona(activePersona);

    return {
      currentPersona: activePersona,
      currentStep: initial.currentStep,
      isActive: initial.isActive,
      disabled: initial.disabled,
      bypassOverview: initial.bypassOverview,
      completedTasks: initial.completedTasks,
      events: initial.events,
      tasks: initial.tasks,
      isDrawerOpen: false,
    };
  },
  getters: {
    completedTasksCount(state) {
      return state.tasks.filter((t) => t.completed).length;
    },
    totalTasksCount(state) {
      return state.tasks.length;
    },
    progressPercentage(state) {
      if (!state.tasks || !state.tasks.length) return 0;
      const completed = state.tasks.filter((t) => t.completed).length;
      return Math.round((completed / state.tasks.length) * 100);
    },
    isChecklistComplete(state) {
      return (
        state.tasks &&
        state.tasks.length > 0 &&
        state.tasks.every((t) => t.completed)
      );
    },
    personaSteps(state) {
      return getSequenceForPersona(state.currentPersona);
    },
    currentStepConfig(state) {
      const steps = getSequenceForPersona(state.currentPersona);
      const stepIdx = Math.max(0, state.currentStep - 1);
      return steps[stepIdx] || steps[0] || null;
    },
    totalSteps(state) {
      const steps = getSequenceForPersona(state.currentPersona);
      return steps.length || 5;
    },
  },
  actions: {
    loadPersonaState(personaKey) {
      const targetPersona = personaKey || getActivePersona();
      this.currentPersona = targetPersona;
      const loaded = readStorageForPersona(targetPersona);
      this.currentStep = loaded.currentStep;
      this.isActive = loaded.isActive;
      this.disabled = loaded.disabled;
      this.bypassOverview = loaded.bypassOverview;
      this.completedTasks = loaded.completedTasks;
      this.events = loaded.events;
      this.tasks = loaded.tasks;
    },
    persist() {
      if (typeof window !== "undefined" && window.localStorage) {
        try {
          const targetPersona = this.currentPersona || getActivePersona();
          window.localStorage.setItem(
            `onboarding_${targetPersona}_current_step`,
            this.currentStep
          );
          window.localStorage.setItem(
            `onboarding_${targetPersona}_is_active`,
            this.isActive
          );
          window.localStorage.setItem(
            `onboarding_${targetPersona}_disabled`,
            this.disabled
          );
          window.localStorage.setItem(
            `onboarding_${targetPersona}_bypass_overview`,
            this.bypassOverview
          );
          window.localStorage.setItem(
            `onboarding_${targetPersona}_completed_tasks`,
            JSON.stringify(this.completedTasks)
          );
          window.localStorage.setItem(
            `onboarding_${targetPersona}_events`,
            JSON.stringify(this.events)
          );
          window.localStorage.setItem(
            `onboarding_${targetPersona}_tasks`,
            JSON.stringify(this.tasks)
          );
        } catch (e) {
          console.error(
            "Failed to persist onboarding state to localStorage",
            e
          );
        }
      }
    },
    setBypassOverview(value) {
      this.bypassOverview = Boolean(value);
      this.addEvent(
        "preference_changed",
        "Bypass Overview Landing Preference",
        this.bypassOverview ? "Enabled bypass" : "Disabled bypass"
      );
      this.persist();
    },
    toggleTaskCompletion(taskId) {
      if (!taskId) return;
      const index = this.completedTasks.indexOf(taskId);
      if (index > -1) {
        this.completedTasks.splice(index, 1);
        this.addEvent(
          "task_uncompleted",
          taskId,
          `Marked task ${taskId} as incomplete`
        );
      } else {
        this.completedTasks.push(taskId);
        this.addEvent(
          "task_completed",
          taskId,
          `Marked task ${taskId} as completed`
        );
      }
      this.persist();
    },
    toggleDrawer(forceState) {
      this.isDrawerOpen =
        typeof forceState === "boolean" ? forceState : !this.isDrawerOpen;
    },
    openDrawer() {
      this.isDrawerOpen = true;
    },
    closeDrawer() {
      this.isDrawerOpen = false;
    },
    toggleTask(taskId, isCompleted) {
      const task = this.tasks.find((t) => t.id === taskId);
      if (task) {
        task.completed =
          typeof isCompleted === "boolean" ? isCompleted : !task.completed;
        this.addEvent(
          "task_status_changed",
          `Task ${task.title}`,
          `Task set to ${task.completed ? "completed" : "incomplete"}`
        );
        this.persist();
      }
    },
    completeTask(taskId) {
      this.toggleTask(taskId, true);
    },
    resetChecklist() {
      this.tasks = JSON.parse(JSON.stringify(DEFAULT_SETUP_TASKS));
      this.addEvent(
        "checklist_reset",
        "Onboarding Checklist Reset",
        "Checklist task completion reset"
      );
      this.persist();
    },
    addEvent(type, target, details = "") {
      const event = {
        id: `ev-${Date.now()}-${Math.random().toString(36).substr(2, 4)}`,
        timestamp: new Date().toISOString(),
        type,
        target,
        details,
      };
      this.events.push(event);

      // Match telemetry event against incomplete setup tasks
      this.matchTelemetryToTasks(event);

      this.persist();
    },
    matchTelemetryToTasks(event) {
      if (!this.tasks || !event || !event.type) return;
      if (
        event.type.startsWith("checklist_") ||
        event.type.startsWith("task_status_") ||
        event.type.startsWith("tour_")
      ) {
        return;
      }
      const searchStr =
        `${event.type} ${event.target} ${event.details}`.toLowerCase();
      this.tasks.forEach((task) => {
        if (
          !task.completed &&
          task.keywords &&
          task.keywords.some((kw) => {
            const regex = new RegExp(`\\b${kw}\\b`, "i");
            return regex.test(searchStr);
          })
        ) {
          task.completed = true;
        }
      });
    },
    startTour() {
      this.currentStep = 1;
      this.isActive = true;
      this.disabled = false;
      this.addEvent(
        "tour_started",
        "Onboarding Guided Tour Started",
        "User initiated tour"
      );
      this.persist();
    },
    nextStep(targetDescription) {
      this.addEvent(
        "step_completed",
        targetDescription,
        `Completed step ${this.currentStep}`
      );
      this.currentStep++;
      this.persist();
    },
    prevStep() {
      if (this.currentStep > 1) {
        this.currentStep--;
        this.addEvent(
          "step_backed",
          `Step ${this.currentStep + 1}`,
          `Returned to step ${this.currentStep}`
        );
        this.persist();
      }
    },
    dismissTour() {
      this.isActive = false;
      this.addEvent(
        "tour_dismissed",
        "Onboarding Tour Dismissed",
        "User dismissed popover"
      );
      this.persist();
    },
    resumeTour() {
      this.isActive = true;
      this.addEvent(
        "tour_resumed",
        "Onboarding Tour Resumed",
        "User resumed popover"
      );
      this.persist();
    },
    disableTour() {
      this.disabled = true;
      this.isActive = false;
      this.addEvent(
        "tour_disabled",
        "Onboarding Tour Disabled",
        "User completely disabled onboarding"
      );
      this.persist();
    },
    resetTour() {
      if (typeof window !== "undefined" && window.localStorage) {
        try {
          const keysToRemove = [];
          for (let i = 0; i < window.localStorage.length; i++) {
            const key = window.localStorage.key(i);
            if (key && key.startsWith("onboarding_")) {
              keysToRemove.push(key);
            }
          }
          keysToRemove.forEach((key) => window.localStorage.removeItem(key));
        } catch (e) {
          console.error("Failed to purge onboarding storage keys", e);
        }
      }

      this.currentStep = 1;
      this.isActive = true;
      this.disabled = false;
      this.events = [];
      this.addEvent(
        "tour_reset",
        "Onboarding Tour Reset",
        "Tour state was fully reset"
      );
      this.persist();
    },
    clearEvents() {
      this.events = [];
      this.persist();
    },
  },
});
