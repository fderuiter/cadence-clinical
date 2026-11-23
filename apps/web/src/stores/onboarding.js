import { defineStore } from "pinia";
import { PERSONA_PRESETS, ROLE_ALIASES, useAuthStore } from "./auth";
import {
  DATA_COLLECTION_ROLES,
  SITE_READINESS_SITE_ROLES,
  SITE_READINESS_SPONSOR_ROLES,
  isScreenAvailable,
} from "../router/availability";
import {
  demoNowIso,
  demoNowMs,
  demoRandomToken,
  isDemoBuild,
  nextDemoStep,
} from "../utils/demoMode";

const NON_ONBOARDING_ROUTES = new Set([
  "landing",
  "login",
  "forbidden",
  "unavailable",
]);

export function hasRequiredRole(userRoles, requiredRoles) {
  if (!requiredRoles || requiredRoles.length === 0) return true;
  return (userRoles || []).some((uRole) => {
    if (requiredRoles.includes(uRole)) return true;
    const aliases = ROLE_ALIASES[uRole] || [];
    return aliases.some((alias) => requiredRoles.includes(alias));
  });
}

function effectiveRolesForStore(personaKey) {
  try {
    const authStore = useAuthStore();
    if (authStore?.rawRoles?.length > 0) {
      return authStore.normalizedRoles || [];
    }
  } catch {
    // Pinia or auth store may not be initialized yet
  }
  const preset = PERSONA_PRESETS.find((p) => p.key === personaKey);
  return preset ? [...preset.requires] : ["sponsor_admin"];
}

function normalizeTaskRouteName(task) {
  if (task?.routeName) return task.routeName;
  const rawRoute = typeof task?.route === "string" ? task.route.trim() : "";
  if (!rawRoute) return null;
  if (rawRoute === "/studies" || rawRoute.startsWith("/studies/")) {
    return "study";
  }
  if (rawRoute === "/overview") return "overview";
  if (rawRoute === "/ecrf") return "ecrf";
  if (rawRoute === "/ctms") return "ctms";
  if (rawRoute === "/ssu") return "ssu";
  return rawRoute.startsWith("/") ? rawRoute.slice(1) : rawRoute;
}

export function resolveAuthorizedTaskRoute(task, userRoles) {
  if (!task) return null;
  const routeName = normalizeTaskRouteName(task);
  if (!routeName || NON_ONBOARDING_ROUTES.has(routeName)) return null;

  if (routeName === "study") {
    return isScreenAvailable("study") ? "/studies" : null;
  }
  if (routeName === "overview") {
    return isScreenAvailable("overview") ? "/overview" : null;
  }
  if (routeName === "ecrf") {
    if (isDemoBuild()) {
      return isScreenAvailable("ecrf") &&
        hasRequiredRole(userRoles, DATA_COLLECTION_ROLES)
        ? "/ecrf"
        : null;
    }
    return isScreenAvailable("study") &&
      hasRequiredRole(userRoles, DATA_COLLECTION_ROLES)
      ? "/studies"
      : null;
  }
  if (routeName === "site-readiness") {
    if (
      isScreenAvailable("ctms") &&
      hasRequiredRole(userRoles, SITE_READINESS_SPONSOR_ROLES)
    ) {
      return "/ctms";
    }
    if (
      isScreenAvailable("ssu") &&
      hasRequiredRole(userRoles, SITE_READINESS_SITE_ROLES)
    ) {
      return "/ssu";
    }
    return null;
  }
  if (routeName === "ctms") {
    return isScreenAvailable("ctms") &&
      hasRequiredRole(userRoles, SITE_READINESS_SPONSOR_ROLES)
      ? "/ctms"
      : null;
  }
  if (routeName === "ssu") {
    return isScreenAvailable("ssu") &&
      hasRequiredRole(userRoles, [
        ...SITE_READINESS_SPONSOR_ROLES,
        ...SITE_READINESS_SITE_ROLES,
      ])
      ? "/ssu"
      : null;
  }
  return null;
}

export function isTourRouteAuthorized(routeName, userRoles) {
  if (!routeName) return true;
  if (NON_ONBOARDING_ROUTES.has(routeName)) return false;
  if (!isScreenAvailable(routeName)) return false;
  if (routeName === "study" || routeName === "overview") return true;
  if (routeName === "ecrf") {
    return hasRequiredRole(userRoles, DATA_COLLECTION_ROLES);
  }
  if (routeName === "observation") {
    return hasRequiredRole(userRoles, [
      "site_investigator",
      "crc",
      "data_manager",
    ]);
  }
  if (routeName === "ctms") {
    return hasRequiredRole(userRoles, SITE_READINESS_SPONSOR_ROLES);
  }
  if (routeName === "ssu") {
    return hasRequiredRole(userRoles, [
      ...SITE_READINESS_SPONSOR_ROLES,
      ...SITE_READINESS_SITE_ROLES,
    ]);
  }
  return false;
}

export const DEFAULT_SETUP_TASKS = [
  {
    id: "task_protocol",
    title: "Open a Study Workspace",
    description:
      "Open a study by code to inspect lifecycle stages, participants, and activity",
    routeName: "study",
    route: "/studies",
    completed: false,
    keywords: ["study", "studies", "protocol", "welcome"],
  },
  {
    id: "task_soa",
    title: "Review Study Lifecycle & Protocol Versions",
    description:
      "Navigate Design through Submit stages and inspect recorded protocol versions",
    routeName: "study",
    route: "/studies",
    completed: false,
    keywords: [
      "lifecycle",
      "design",
      "version",
      "soa",
      "schedule",
      "arm",
      "epoch",
    ],
  },
  {
    id: "task_crf",
    title: "Verify Data Collection Forms",
    description:
      "Inspect clinical case report forms, field validations, and discrepancy queries",
    routeName: "ecrf",
    route: "/ecrf",
    completed: false,
    keywords: ["data", "collection", "crf", "ecrf", "form"],
  },
  {
    id: "task_rules",
    title: "Check Site Readiness",
    description:
      "Review site activation milestones and operational startup readiness",
    routeName: "site-readiness",
    route: "/ctms",
    completed: false,
    keywords: [
      "site",
      "readiness",
      "activation",
      "milestone",
      "rule",
      "rules",
      "query",
      "queries",
    ],
  },
  {
    id: "task_exports",
    title: "Configure Workspace Orientation",
    description:
      "Set your default landing preference and explore contextual SOP guidance",
    routeName: "overview",
    route: "/overview",
    completed: false,
    keywords: ["orientation", "overview", "preference", "help"],
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
  // Guided tours are strictly opt-in on sign-in unless explicitly started.
  let isActive = false;
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
        isActive = false;
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
      routeName: "study",
      targetSelector: null,
      title: "Welcome to Protocol Design",
      description:
        "Explore the study-centric workspace where protocol versions, lifecycle stages, and governance live together.",
      prompt:
        "Welcome, Designer! Navigate study lifecycle stages and inspect recorded protocol versions.",
    },
    {
      step: 2,
      routeName: "study",
      targetSelector: "#nav-studies",
      title: "Study Workspace",
      description:
        "Open any study by code to navigate its lifecycle across Design, Start, Conduct, Data, Analyze, and Submit.",
      prompt:
        "Structure and review study lifecycle sections from one unified workspace.",
    },
    {
      step: 3,
      routeName: "study",
      targetSelector: "#btn-open-palette",
      title: "Global Search & Jump",
      description:
        "Press ⌘K to search studies by code or title and jump straight to Design protocol versions.",
      prompt:
        "Jump directly to any study, lifecycle stage, or workspace orientation view.",
    },
    {
      step: 4,
      routeName: "overview",
      targetSelector: "#btn-onboarding-checklist-trigger",
      title: "Workspace Orientation",
      description:
        "Review role-tailored setup milestones and customize your default sign-in landing preference.",
      prompt:
        "Track setup progress and manage orientation preferences anytime from the header.",
    },
    {
      step: 5,
      routeName: "study",
      targetSelector: "#btn-contextual-help-trigger",
      title: "Tour Complete!",
      description:
        "Your protocol design workspace orientation is complete. Open Help anytime for standard operating procedure guidance.",
      prompt:
        "All set! Use Studies, Search (⌘K), Orientation, and Help from the persistent top bar.",
    },
  ],

  data_manager: [
    {
      step: 1,
      routeName: "ecrf",
      targetSelector: null,
      title: "Welcome to Data Management",
      description:
        "Review clinical case report forms, real-time field validation rules, and discrepancy management.",
      prompt:
        "Welcome, Data Manager! Inspect clinical form validations, queries, and study participant progress.",
    },
    {
      step: 2,
      routeName: "ecrf",
      targetSelector: "#nav-data-collection",
      title: "Data Collection & Validation",
      description:
        "Verify range checks, required clinical observations, and discrepancy query transitions in real time.",
      prompt:
        "Validate scheduled visit forms and monitor discrepancy resolution.",
    },
    {
      step: 3,
      routeName: "study",
      targetSelector: "#nav-studies",
      title: "Study Conduct & Data Stages",
      description:
        "Open a study workspace to inspect participant counts, review proposals, and trace study activity.",
      prompt:
        "Cross-reference form capture with live participant status in the study workspace.",
    },
    {
      step: 4,
      routeName: "study",
      targetSelector: "#btn-open-palette",
      title: "Global Command Palette",
      description:
        "Use ⌘K to search studies, navigate lifecycle stages, or switch between Studies and Data collection.",
      prompt:
        "Move between study lifecycle sections and data collection in a keystroke.",
    },
    {
      step: 5,
      routeName: "ecrf",
      targetSelector: "#btn-contextual-help-trigger",
      title: "Tour Complete!",
      description:
        "Your data management orientation is complete. Access SOP guidance via Help in the top bar anytime.",
      prompt:
        "Ready for clinical data review across Studies and Data collection.",
    },
  ],

  cra_monitor: [
    {
      step: 1,
      routeName: "ctms",
      targetSelector: null,
      title: "Welcome to Clinical Operations",
      description:
        "Oversee site activation readiness, startup milestones, participant status, and source data verification.",
      prompt:
        "Welcome, CRA! Coordinate site readiness and clinical monitoring across active study sites.",
    },
    {
      step: 2,
      routeName: "ctms",
      targetSelector: "#nav-site-readiness",
      title: "Site Readiness & Activation",
      description:
        "Track site activation requirements, operational readiness, and site startup progress.",
      prompt:
        "Review site activation state and readiness milestones across clinical locations.",
    },
    {
      step: 3,
      routeName: "ecrf",
      targetSelector: "#nav-data-collection",
      title: "Source Data Verification",
      description:
        "Navigate to participant case report forms to verify clinical observations and manage queries.",
      prompt:
        "Perform source data verification on clinical forms with full audit traceability.",
    },
    {
      step: 4,
      routeName: "study",
      targetSelector: "#nav-studies",
      title: "Study Lifecycle Overview",
      description:
        "Open a study workspace to inspect participant state counts, site progress, and open proposals.",
      prompt:
        "Drill from study-wide participant counts directly to site-filtered participants.",
    },
    {
      step: 5,
      routeName: "ctms",
      targetSelector: "#btn-contextual-help-trigger",
      title: "Tour Complete!",
      description:
        "Your clinical monitoring orientation is complete. Use Search (⌘K), Orientation, and Help anytime.",
      prompt:
        "All set! Your site readiness and monitoring workflows are ready.",
    },
  ],

  auditor: [
    {
      step: 1,
      routeName: "study",
      targetSelector: null,
      title: "Welcome to Regulatory & Audit",
      description:
        "Inspect study activity records, recorded protocol versions, and human review decisions in the study workspace.",
      prompt:
        "Welcome, Auditor! Inspect immutable study activity, protocol provenance, and governance decisions.",
    },
    {
      step: 2,
      routeName: "study",
      targetSelector: "#nav-studies",
      title: "Study Activity & Provenance",
      description:
        "Open a study by code to review its activity stream, protocol version history, and proposal decisions.",
      prompt:
        "Verify chronological clinical activity and protocol version provenance.",
    },
    {
      step: 3,
      routeName: "study",
      targetSelector: "#btn-open-palette",
      title: "Search Authorized Studies",
      description:
        "Press ⌘K to search studies the kernel authorizes you to inspect and jump across lifecycle stages.",
      prompt:
        "Navigate directly to Design, Conduct, or Submit stages for any authorized study.",
    },
    {
      step: 4,
      routeName: "overview",
      targetSelector: "#btn-onboarding-checklist-trigger",
      title: "Inspection Orientation",
      description:
        "Review inspection readiness tasks and configure your workspace landing preferences.",
      prompt:
        "Keep track of inspection milestones in the opt-in orientation checklist.",
    },
    {
      step: 5,
      routeName: "study",
      targetSelector: "#btn-contextual-help-trigger",
      title: "Tour Complete!",
      description:
        "Your regulatory audit orientation is complete. Open Help anytime to consult compliance SOPs.",
      prompt:
        "Excellent! Your inspection workspace and contextual SOP guidance are ready.",
    },
  ],

  site_crc: [
    {
      step: 1,
      routeName: "ecrf",
      targetSelector: null,
      title: "Welcome to Data Collection",
      description:
        "Screen participants, record visit observations, and track site readiness from one workspace.",
      prompt:
        "Welcome, Site Coordinator! Manage participant visits, case report forms, and site readiness.",
    },
    {
      step: 2,
      routeName: "ecrf",
      targetSelector: ".subject-selector-card",
      title: "Participant Selection & Visits",
      description:
        "Select study participants, review visit progress, and enroll new subjects for your site.",
      prompt:
        "Select participants and navigate scheduled visit encounters for accurate capture.",
    },
    {
      step: 3,
      routeName: "ecrf",
      targetSelector: ".ecrf-form-renderer",
      title: "Case Report Form Entry",
      description:
        "Enter vital signs and clinical observations with real-time validation checks and query flags.",
      prompt:
        "Record clinical observations with immediate range checks and discrepancy notes.",
    },
    {
      step: 4,
      routeName: "ssu",
      targetSelector: "#nav-site-readiness",
      title: "Site Readiness",
      description:
        "Check site activation tasks and operational readiness status for your clinical site.",
      prompt:
        "Verify site startup milestones to maintain clinical site readiness.",
    },
    {
      step: 5,
      routeName: "study",
      targetSelector: "#nav-studies",
      title: "Tour Complete!",
      description:
        "Your site coordinator orientation is complete. Move seamlessly across Studies, Data collection, and Site readiness.",
      prompt:
        "Great job! Participant data collection and site readiness workflows are ready.",
    },
  ],

  super_admin: [
    {
      step: 1,
      routeName: "study",
      targetSelector: null,
      title: "Welcome to Cadence Clinical",
      description:
        "Navigate studies, clinical data collection, and site readiness in one unified study-centric workspace.",
      prompt:
        "Welcome, Administrator! Oversee studies, data collection, site readiness, and governance from one shell.",
    },
    {
      step: 2,
      routeName: "study",
      targetSelector: "#nav-studies",
      title: "Studies & Lifecycle",
      description:
        "Open any study by code to follow its lifecycle from Design through Submit in three panes.",
      prompt:
        "Inspect participant counts, protocol versions, and governance proposals per study.",
    },
    {
      step: 3,
      routeName: "ecrf",
      targetSelector: "#nav-data-collection",
      title: "Data Collection",
      description:
        "Review clinical case report forms, field validations, and discrepancy management.",
      prompt:
        "Verify structured clinical data capture and real-time validation checks.",
    },
    {
      step: 4,
      routeName: "ctms",
      targetSelector: "#nav-site-readiness",
      title: "Site Readiness",
      description:
        "Monitor site activation checklists, startup progress, and site operational status.",
      prompt:
        "Oversee site readiness and activation workflows across clinical sites.",
    },
    {
      step: 5,
      routeName: "study",
      targetSelector: "#btn-contextual-help-trigger",
      title: "Tour Complete!",
      description:
        "Use Search (⌘K), View switcher, Orientation, and Help anytime from the persistent header.",
      prompt: "Excellent! Your unified clinical workspace is ready.",
    },
  ],
};

function buildOverviewFallbackSteps(baseSequence) {
  const firstStep = baseSequence?.[0];
  const lastStep = baseSequence?.[baseSequence.length - 1];
  return [
    {
      step: 1,
      routeName: "overview",
      targetSelector: null,
      title: firstStep?.title || "Welcome to Workspace Orientation",
      description:
        firstStep?.description ||
        "Review role-tailored orientation guidance and workspace preferences.",
      prompt:
        firstStep?.prompt ||
        "Explore your workspace orientation and contextual SOP resources.",
    },
    {
      step: 2,
      routeName: "overview",
      targetSelector: "#btn-contextual-help-trigger",
      title: lastStep?.title || "Tour Complete!",
      description:
        lastStep?.description ||
        "Your workspace orientation is complete. Open Help anytime for standard operating procedure guidance.",
      prompt:
        lastStep?.prompt ||
        "Use Search (⌘K) and Help from the persistent top bar anytime.",
    },
  ];
}

function clampStepToTotal(step, totalSteps) {
  const maxStep = Math.max(1, totalSteps || 1);
  const parsed = Number.isFinite(step) && step > 0 ? step : 1;
  return Math.min(parsed, maxStep);
}

function getSequenceForPersona(personaKey) {
  let authStore = null;
  try {
    authStore = useAuthStore();
  } catch {
    // Pinia or auth store not ready
  }

  const roles = authStore?.normalizedRoles || [];
  let baseSequence = null;

  if (
    personaKey === "super_admin" &&
    authStore?.currentPersona === "super_admin" &&
    roles.includes("sponsor_admin")
  ) {
    baseSequence = ROLE_STEP_SEQUENCES.super_admin;
  } else if (
    personaKey &&
    personaKey !== "super_admin" &&
    ROLE_STEP_SEQUENCES[personaKey]
  ) {
    baseSequence = ROLE_STEP_SEQUENCES[personaKey];
  } else if (personaKey === "designer" || personaKey === "study_designer") {
    baseSequence = ROLE_STEP_SEQUENCES.sponsor_designer;
  } else if (personaKey === "crc") {
    baseSequence = ROLE_STEP_SEQUENCES.site_crc;
  } else if (personaKey === "cra" || personaKey === "monitor") {
    baseSequence = ROLE_STEP_SEQUENCES.cra_monitor;
  } else if (personaKey === "tmf_auditor") {
    baseSequence = ROLE_STEP_SEQUENCES.auditor;
  } else if (personaKey === "sponsor_admin") {
    baseSequence = ROLE_STEP_SEQUENCES.super_admin;
  } else if (roles.length > 0) {
    if (
      roles.includes("sponsor_designer") &&
      !roles.includes("sponsor_admin")
    ) {
      baseSequence = ROLE_STEP_SEQUENCES.sponsor_designer;
    } else if (
      roles.includes("data_manager") &&
      !roles.includes("sponsor_admin")
    ) {
      baseSequence = ROLE_STEP_SEQUENCES.data_manager;
    } else if (roles.includes("site_investigator") || roles.includes("crc")) {
      baseSequence = ROLE_STEP_SEQUENCES.site_crc;
    } else if (roles.includes("cra") || roles.includes("monitor")) {
      baseSequence = ROLE_STEP_SEQUENCES.cra_monitor;
    } else if (roles.includes("auditor") || roles.includes("tmf_auditor")) {
      baseSequence = ROLE_STEP_SEQUENCES.auditor;
    }
  }

  if (!baseSequence) {
    baseSequence =
      ROLE_STEP_SEQUENCES[personaKey] ||
      ROLE_STEP_SEQUENCES.super_admin ||
      ROLE_STEP_SEQUENCES.sponsor_designer;
  }

  const effectiveRoles =
    authStore?.rawRoles?.length > 0
      ? roles
      : effectiveRolesForStore(personaKey);
  const filtered = baseSequence
    .filter((step) => isTourRouteAuthorized(step.routeName, effectiveRoles))
    .map((step, idx) => ({ ...step, step: idx + 1 }));

  if (filtered.length > 0) {
    return filtered;
  }

  return buildOverviewFallbackSteps(baseSequence);
}

export const useOnboardingStore = defineStore("onboarding", {
  state: () => {
    const activePersona = getActivePersona();
    const initial = readStorageForPersona(activePersona);
    const initialSteps = getSequenceForPersona(activePersona);

    return {
      currentPersona: activePersona,
      currentStep: clampStepToTotal(initial.currentStep, initialSteps.length),
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
    visibleTasks(state) {
      const roles = effectiveRolesForStore(state.currentPersona);
      return (state.tasks || [])
        .map((task) => {
          const resolvedRoute = resolveAuthorizedTaskRoute(task, roles);
          return resolvedRoute ? { ...task, route: resolvedRoute } : null;
        })
        .filter(Boolean);
    },
    completedTasksCount() {
      return this.visibleTasks.filter((t) => t.completed).length;
    },
    totalTasksCount() {
      return this.visibleTasks.length;
    },
    progressPercentage() {
      if (!this.visibleTasks.length) return 0;
      const completed = this.visibleTasks.filter((t) => t.completed).length;
      return Math.round((completed / this.visibleTasks.length) * 100);
    },
    isChecklistComplete() {
      return (
        this.visibleTasks.length > 0 &&
        this.visibleTasks.every((t) => t.completed)
      );
    },
    personaSteps(state) {
      return getSequenceForPersona(state.currentPersona);
    },
    currentStepConfig(state) {
      const steps = getSequenceForPersona(state.currentPersona);
      const clampedStep = clampStepToTotal(state.currentStep, steps.length);
      const stepIdx = Math.max(0, clampedStep - 1);
      return steps[stepIdx] || steps[0] || null;
    },
    totalSteps(state) {
      const steps = getSequenceForPersona(state.currentPersona);
      return steps.length || 1;
    },
  },
  actions: {
    loadPersonaState(personaKey) {
      const targetPersona = personaKey || getActivePersona();
      this.currentPersona = targetPersona;
      const loaded = readStorageForPersona(targetPersona);
      const steps = getSequenceForPersona(targetPersona);
      const clampedStep = clampStepToTotal(loaded.currentStep, steps.length);
      this.currentStep = clampedStep;
      this.isActive = loaded.isActive;
      this.disabled = loaded.disabled;
      this.bypassOverview = loaded.bypassOverview;
      this.completedTasks = loaded.completedTasks;
      this.events = loaded.events;
      this.tasks = loaded.tasks;
      if (clampedStep !== loaded.currentStep) {
        this.persist();
      }
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
      const step = nextDemoStep("onboarding-event");
      const event = {
        id: `ev-${demoNowMs(step)}-${demoRandomToken(4, 36, "onboarding-event")}`,
        timestamp: demoNowIso(step),
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
      this.currentStep = clampStepToTotal(this.currentStep, this.totalSteps);
      this.isActive = true;
      this.disabled = false;
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
