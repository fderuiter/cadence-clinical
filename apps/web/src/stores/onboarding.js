import { defineStore, getActivePinia } from "pinia";
import {
  PERSONA_CATALOG,
  PERSONA_CATALOG_BY_KEY,
  getPersonaCatalogEntry,
} from "ui";
import { registerOnboardingSessionBridge } from "../auth/sessionCoordinator.js";
import { isScreenAvailable } from "../router/availability";
import {
  demoNowIso,
  demoNowMs,
  demoRandomToken,
  isDemoBuild,
  nextDemoStep,
} from "../utils/demoMode";
import {
  scopePrefix,
  scopedKey,
  storageScopeOf,
} from "../utils/userScopedStorage.js";
import {
  ROLE_ALIASES,
  defaultPersonaKeyForRoles,
  resolveEffectivePersonaRoles,
  useAuthStore,
} from "./auth";

export const ONBOARDING_STORAGE_NAMESPACE = "onboarding";

const DATA_COLLECTION_ROLES = [
  "principal_investigator",
  "site_investigator",
  "crc",
  "data_manager",
  "sponsor_admin",
  "cra",
  "monitor",
];

const SITE_READINESS_SPONSOR_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "sponsor_admin",
];

const SITE_READINESS_SITE_ROLES = [
  "principal_investigator",
  "site_investigator",
  "crc",
];

const ETMF_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "principal_investigator",
  "site_investigator",
  "crc",
  "safety_reviewer",
  "safety_officer",
  "safety_physician",
  "medical_monitor",
  "sponsor_mm",
  "auditor",
  "tmf_auditor",
  "sponsor_admin",
];

const SAFETY_ROLES = [
  "safety_reviewer",
  "safety_officer",
  "safety_physician",
  "medical_monitor",
  "sponsor_mm",
  "sponsor_pv_officer",
  "qppv",
  "qppv_officer",
  "qualified_person_pv",
  "data_manager",
  "sponsor_admin",
];

const AUDIT_ROLES = ["auditor", "tmf_auditor", "sponsor_admin"];
const MDR_ROLES = [
  "sponsor_designer",
  "protocol_architect",
  "data_manager",
  "sponsor_admin",
];
const RULES_ROLES = ["sponsor_designer", "data_manager", "sponsor_admin"];

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

function getActivePersona() {
  try {
    const authStore = useAuthStore();
    if (authStore && authStore.currentPersona) {
      return authStore.currentPersona;
    }
  } catch {
    // Pinia or auth store may not be initialized yet
  }
  return "super_admin";
}

function currentOnboardingScope() {
  try {
    return storageScopeOf(useAuthStore());
  } catch {
    return null;
  }
}

/**
 * Resolves the account-scoped storage key
 * `cadence_user:<tenant>:<user>:onboarding:<persona>:<baseKey>`, or `null`
 * when no authenticated account with both `tenantId` and `userId` is active.
 */
export function getOnboardingStorageKey(baseKey, personaKey) {
  const persona = personaKey || getActivePersona();
  return scopedKey(
    currentOnboardingScope(),
    ONBOARDING_STORAGE_NAMESPACE,
    persona,
    baseKey
  );
}

function effectiveRolesForStore(personaKey) {
  try {
    const authStore = useAuthStore();
    return resolveEffectivePersonaRoles(
      authStore?.rawRoles,
      personaKey || authStore?.currentPersona || "super_admin",
      { isAuthenticated: Boolean(authStore?.isAuthenticated) }
    );
  } catch {
    const entry = getPersonaCatalogEntry(personaKey);
    return entry ? [...entry.requires] : ["sponsor_admin"];
  }
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
  if (rawRoute === "/etmf") return "etmf";
  if (rawRoute === "/training") return "training";
  if (rawRoute === "/safety") return "safety";
  if (rawRoute === "/audit") return "audit";
  if (rawRoute === "/mdr") return "mdr";
  if (rawRoute === "/rules") return "rules";
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
  if (routeName === "training") {
    return isScreenAvailable("training") ? "/training" : null;
  }
  if (routeName === "etmf") {
    return isScreenAvailable("etmf") && hasRequiredRole(userRoles, ETMF_ROLES)
      ? "/etmf"
      : null;
  }
  if (routeName === "ecrf") {
    if (!hasRequiredRole(userRoles, DATA_COLLECTION_ROLES)) return null;
    if (isDemoBuild()) {
      return isScreenAvailable("ecrf") ? "/ecrf" : null;
    }
    return isScreenAvailable("study") ? "/studies" : null;
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
  if (routeName === "safety") {
    if (!hasRequiredRole(userRoles, SAFETY_ROLES)) return null;
    if (isScreenAvailable("safety")) return "/safety";
    return isScreenAvailable("study") ? "/studies" : null;
  }
  if (routeName === "audit") {
    if (!hasRequiredRole(userRoles, AUDIT_ROLES)) return null;
    if (isScreenAvailable("audit")) return "/audit";
    return isScreenAvailable("study") ? "/studies" : null;
  }
  if (routeName === "mdr") {
    if (!hasRequiredRole(userRoles, MDR_ROLES)) return null;
    if (isScreenAvailable("mdr")) return "/mdr";
    return isScreenAvailable("study") ? "/studies" : null;
  }
  if (routeName === "rules") {
    if (!hasRequiredRole(userRoles, RULES_ROLES)) return null;
    if (isScreenAvailable("rules")) return "/rules";
    return isScreenAvailable("study") ? "/studies" : null;
  }
  return null;
}

export function isTourRouteAuthorized(routeName, userRoles) {
  if (!routeName) return true;
  if (NON_ONBOARDING_ROUTES.has(routeName)) return false;
  if (routeName === "safety") {
    if (!hasRequiredRole(userRoles, SAFETY_ROLES)) return false;
    return isScreenAvailable("safety") || isScreenAvailable("study");
  }
  if (!isScreenAvailable(routeName)) return false;
  if (
    routeName === "study" ||
    routeName === "overview" ||
    routeName === "training"
  ) {
    return true;
  }
  if (routeName === "etmf") {
    return hasRequiredRole(userRoles, ETMF_ROLES);
  }
  if (routeName === "ecrf") {
    return hasRequiredRole(userRoles, DATA_COLLECTION_ROLES);
  }
  if (routeName === "observation") {
    return hasRequiredRole(userRoles, [
      "principal_investigator",
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

function resolveTourRouteName(routeName) {
  if (routeName === "safety" && !isScreenAvailable("safety")) {
    return isScreenAvailable("study") ? "study" : "safety";
  }
  return routeName;
}

function cloneCatalogTasks(personaKey) {
  const entry =
    getPersonaCatalogEntry(personaKey) || PERSONA_CATALOG_BY_KEY.super_admin;
  return (entry?.tasks || []).map((task) => ({
    ...task,
    keywords: Array.isArray(task.keywords) ? [...task.keywords] : [],
    completed: false,
  }));
}

export const DEFAULT_SETUP_TASKS = cloneCatalogTasks("super_admin");

export const ROLE_STEP_SEQUENCES = Object.freeze(
  Object.fromEntries(
    PERSONA_CATALOG.map((entry) => [entry.key, entry.tourSteps])
  )
);

function clampStepToTotal(step, total) {
  const safeTotal = Math.max(1, Number(total) || 1);
  const numeric = Number(step);
  if (!Number.isFinite(numeric) || numeric < 1) return 1;
  return Math.min(Math.floor(numeric), safeTotal);
}

function buildOverviewFallbackSteps(baseSequence) {
  const firstStep = Array.isArray(baseSequence) ? baseSequence[0] : null;
  const lastStep = Array.isArray(baseSequence)
    ? baseSequence[baseSequence.length - 1]
    : null;
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

function resolveCatalogKeyForPersona(personaKey) {
  if (personaKey && ROLE_STEP_SEQUENCES[personaKey]) {
    return personaKey;
  }
  if (personaKey === "designer" || personaKey === "study_designer") {
    return "sponsor_designer";
  }
  if (personaKey === "crc") return "site_crc";
  if (personaKey === "principal_investigator") return "site_investigator";
  if (personaKey === "cra" || personaKey === "monitor") return "cra_monitor";
  if (
    personaKey === "safety_reviewer" ||
    personaKey === "safety_physician" ||
    personaKey === "medical_monitor" ||
    personaKey === "sponsor_mm"
  ) {
    return "safety_officer";
  }
  if (personaKey === "tmf_auditor") return "auditor";
  if (personaKey === "sponsor_admin") return "super_admin";
  return null;
}

function resolveEffectivePersonaKey(personaKey, authStore) {
  const mappedKey = resolveCatalogKeyForPersona(personaKey);
  const rawRoles = authStore?.rawRoles || [];
  const normalizedRoles = authStore?.normalizedRoles || [];

  if (
    (!mappedKey || mappedKey === "super_admin") &&
    rawRoles.length > 0 &&
    !normalizedRoles.includes("sponsor_admin")
  ) {
    return defaultPersonaKeyForRoles(rawRoles);
  }
  return mappedKey || "super_admin";
}

function getSequenceForPersona(personaKey) {
  let authStore = null;
  try {
    authStore = useAuthStore();
  } catch {
    // Pinia may not be initialized yet
  }

  const resolvedKey = resolveEffectivePersonaKey(personaKey, authStore);
  const baseSequence =
    ROLE_STEP_SEQUENCES[resolvedKey] || ROLE_STEP_SEQUENCES.super_admin;

  const effectiveRoles = effectiveRolesForStore(resolvedKey);
  const filtered = baseSequence
    .filter((step) => isTourRouteAuthorized(step.routeName, effectiveRoles))
    .map((step, idx) => ({
      ...step,
      routeName: resolveTourRouteName(step.routeName),
      step: idx + 1,
    }));

  if (filtered.length > 0) {
    return filtered;
  }

  return buildOverviewFallbackSteps(baseSequence);
}

function personaSnapshotKey(personaKey) {
  const catalogKey = resolveCatalogKeyForPersona(personaKey) || "super_admin";
  const prefix = scopePrefix(currentOnboardingScope()) || "__ephemeral__:";
  return `${prefix}${catalogKey}`;
}

function cloneSnapshotState(entry, catalogKey) {
  return {
    currentStep: entry.currentStep,
    isActive: entry.isActive,
    disabled: entry.disabled,
    bypassOverview: entry.bypassOverview,
    events: Array.isArray(entry.events)
      ? entry.events.map((ev) => ({ ...ev }))
      : [],
    tasks: Array.isArray(entry.tasks)
      ? entry.tasks.map((task) => ({
          ...task,
          keywords: Array.isArray(task.keywords) ? [...task.keywords] : [],
        }))
      : cloneCatalogTasks(catalogKey),
  };
}

function readStorageForPersona(personaKey, fallbackSnapshot = null) {
  const catalogKey = resolveCatalogKeyForPersona(personaKey) || "super_admin";
  if (fallbackSnapshot) {
    const base = cloneSnapshotState(fallbackSnapshot, catalogKey);
    const stepKey = getOnboardingStorageKey("current_step", personaKey);
    if (!stepKey || typeof window === "undefined" || !window.localStorage) {
      return base;
    }
  }
  let currentStep = fallbackSnapshot?.currentStep ?? 1;
  let isActive = fallbackSnapshot?.isActive ?? false;
  let disabled = fallbackSnapshot?.disabled ?? false;
  let bypassOverview = fallbackSnapshot?.bypassOverview ?? false;
  let events = fallbackSnapshot?.events
    ? fallbackSnapshot.events.map((ev) => ({ ...ev }))
    : [];
  let tasks = fallbackSnapshot?.tasks
    ? fallbackSnapshot.tasks.map((task) => ({
        ...task,
        keywords: Array.isArray(task.keywords) ? [...task.keywords] : [],
      }))
    : cloneCatalogTasks(catalogKey);

  if (typeof window === "undefined" || !window.localStorage) {
    return {
      currentStep,
      isActive,
      disabled,
      bypassOverview,
      events,
      tasks,
    };
  }

  const stepKey = getOnboardingStorageKey("current_step", personaKey);
  if (!stepKey) {
    return {
      currentStep,
      isActive,
      disabled,
      bypassOverview,
      events,
      tasks,
    };
  }

  try {
    const activeKey = getOnboardingStorageKey("is_active", personaKey);
    const disabledKey = getOnboardingStorageKey("disabled", personaKey);
    const bypassKey = getOnboardingStorageKey("bypass_overview", personaKey);
    const eventsKey = getOnboardingStorageKey("events", personaKey);
    const setupTasksKey = getOnboardingStorageKey("tasks", personaKey);

    const savedStep = window.localStorage.getItem(stepKey);
    if (savedStep !== null) {
      const parsed = parseInt(savedStep, 10);
      if (!Number.isNaN(parsed) && parsed >= 1) {
        currentStep = parsed;
      }
    }

    const savedActive = activeKey
      ? window.localStorage.getItem(activeKey)
      : null;
    if (savedActive !== null) {
      isActive = savedActive === "true";
    }

    const savedDisabled = disabledKey
      ? window.localStorage.getItem(disabledKey)
      : null;
    if (savedDisabled !== null) {
      disabled = savedDisabled === "true";
    }

    const savedBypass = bypassKey
      ? window.localStorage.getItem(bypassKey)
      : null;
    if (savedBypass !== null) {
      bypassOverview = savedBypass === "true";
    }

    const savedEvents = eventsKey
      ? window.localStorage.getItem(eventsKey)
      : null;
    if (savedEvents) {
      try {
        const parsedEvents = JSON.parse(savedEvents);
        if (Array.isArray(parsedEvents)) {
          events = parsedEvents;
        }
      } catch {
        events = [];
      }
    }

    const savedTasks = setupTasksKey
      ? window.localStorage.getItem(setupTasksKey)
      : null;
    if (savedTasks) {
      try {
        const parsedTasks = JSON.parse(savedTasks);
        if (Array.isArray(parsedTasks)) {
          const completedById = new Map(
            parsedTasks
              .filter((item) => item && typeof item.id === "string")
              .map((item) => [item.id, Boolean(item.completed)])
          );
          tasks = cloneCatalogTasks(catalogKey).map((defTask) => ({
            ...defTask,
            completed: completedById.has(defTask.id)
              ? Boolean(completedById.get(defTask.id))
              : false,
          }));
        }
      } catch {
        // Keep fresh persona tasks when stored JSON is malformed
      }
    }
  } catch (e) {
    console.error("Failed to read onboarding state from localStorage", e);
  }

  return {
    currentStep,
    isActive,
    disabled,
    bypassOverview,
    events,
    tasks,
  };
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
      events: initial.events,
      tasks: initial.tasks,
      isDrawerOpen: false,
      personaSnapshots: {},
    };
  },
  getters: {
    completedTasks(state) {
      return (state.tasks || [])
        .filter((task) => task.completed)
        .map((task) => task.id);
    },
    visibleTasks(state) {
      const roles = effectiveRolesForStore(state.currentPersona);
      /** @type {typeof DEFAULT_SETUP_TASKS} */
      const visible = [];
      for (const task of state.tasks || []) {
        const resolvedRoute = resolveAuthorizedTaskRoute(task, roles);
        if (resolvedRoute) {
          visible.push({ ...task, route: resolvedRoute });
        }
      }
      return visible;
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
      const snapshot =
        this.personaSnapshots?.[personaSnapshotKey(targetPersona)] || null;
      const loaded = readStorageForPersona(targetPersona, snapshot);
      const steps = getSequenceForPersona(targetPersona);
      const clampedStep = clampStepToTotal(loaded.currentStep, steps.length);
      this.currentStep = clampedStep;
      this.isActive = loaded.isActive;
      this.disabled = loaded.disabled;
      this.bypassOverview = loaded.bypassOverview;
      this.events = loaded.events;
      this.tasks = loaded.tasks;
      if (clampedStep !== loaded.currentStep) {
        this.persist();
      }
    },
    resetSessionState() {
      this.currentPersona = "super_admin";
      this.currentStep = 1;
      this.isActive = false;
      this.disabled = false;
      this.bypassOverview = false;
      this.events = [];
      this.tasks = cloneCatalogTasks("super_admin");
      this.isDrawerOpen = false;
      this.personaSnapshots = {};
    },
    persist() {
      const targetPersona = this.currentPersona || getActivePersona();
      const catalogKey =
        resolveCatalogKeyForPersona(targetPersona) || "super_admin";
      if (!this.personaSnapshots) {
        this.personaSnapshots = {};
      }
      this.personaSnapshots[personaSnapshotKey(targetPersona)] =
        cloneSnapshotState(this, catalogKey);

      if (typeof window === "undefined" || !window.localStorage) return;
      const stepKey = getOnboardingStorageKey("current_step", targetPersona);
      if (!stepKey) return;

      try {
        const activeKey = getOnboardingStorageKey("is_active", targetPersona);
        const disabledKey = getOnboardingStorageKey("disabled", targetPersona);
        const bypassKey = getOnboardingStorageKey(
          "bypass_overview",
          targetPersona
        );
        const completedTasksKey = getOnboardingStorageKey(
          "completed_tasks",
          targetPersona
        );
        const eventsKey = getOnboardingStorageKey("events", targetPersona);
        const setupTasksKey = getOnboardingStorageKey("tasks", targetPersona);

        window.localStorage.setItem(stepKey, String(this.currentStep));
        if (activeKey) {
          window.localStorage.setItem(activeKey, String(this.isActive));
        }
        if (disabledKey) {
          window.localStorage.setItem(disabledKey, String(this.disabled));
        }
        if (bypassKey) {
          window.localStorage.setItem(bypassKey, String(this.bypassOverview));
        }
        if (completedTasksKey) {
          window.localStorage.setItem(
            completedTasksKey,
            JSON.stringify(this.completedTasks)
          );
        }
        if (eventsKey) {
          window.localStorage.setItem(eventsKey, JSON.stringify(this.events));
        }
        if (setupTasksKey) {
          window.localStorage.setItem(
            setupTasksKey,
            JSON.stringify(this.tasks)
          );
        }
      } catch (e) {
        console.error("Failed to persist onboarding state to localStorage", e);
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
      this.toggleTask(taskId);
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
      this.tasks = cloneCatalogTasks(this.currentPersona);
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
          const accountNamespace = scopedKey(
            currentOnboardingScope(),
            ONBOARDING_STORAGE_NAMESPACE
          );
          const keysToRemove = [];
          for (let i = 0; i < window.localStorage.length; i++) {
            const key = window.localStorage.key(i);
            if (!key) continue;
            if (
              key.startsWith("onboarding_") ||
              (accountNamespace && key.startsWith(`${accountNamespace}:`))
            ) {
              keysToRemove.push(key);
            }
          }
          keysToRemove.forEach((key) => window.localStorage.removeItem(key));
        } catch (e) {
          console.error("Failed to purge onboarding storage keys", e);
        }
      }

      this.personaSnapshots = {};
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

registerOnboardingSessionBridge({
  onReset: (pinia) => {
    const targetPinia = pinia || getActivePinia();
    if (!targetPinia) return;
    if (targetPinia._s?.has("onboarding")) {
      useOnboardingStore(targetPinia).resetSessionState();
    }
  },
});
