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
  nextDemoStep,
} from "../utils/demoMode";
import { scopePrefix, scopedKey } from "../utils/userScopedStorage.js";
import { useAuthStore } from "./auth";
import {
  DATA_COLLECTION_ROLES,
  ETMF_ROLES,
  ONBOARDING_STORAGE_NAMESPACE,
  SAFETY_ROLES,
  SITE_READINESS_SITE_ROLES,
  SITE_READINESS_SPONSOR_ROLES,
  currentOnboardingScope,
  effectiveRolesForPersona,
  getActivePersona,
  getOnboardingStorageKey,
  hasRequiredRole,
  isTourRouteAuthorized,
  resolveAuthorizedTaskRoute,
  resolveCatalogKeyForPersona,
  resolveEffectivePersonaKey,
} from "./onboardingSummary";

export function cloneCatalogTasks(personaKey) {
  const catalogKey = resolveCatalogKeyForPersona(personaKey) || "super_admin";
  const entry =
    getPersonaCatalogEntry(catalogKey) || PERSONA_CATALOG_BY_KEY.super_admin;
  return (entry?.tasks || []).map((task) => ({
    ...task,
    keywords: Array.isArray(task.keywords) ? [...task.keywords] : [],
    completed: false,
  }));
}

export const DEFAULT_SETUP_TASKS = cloneCatalogTasks("super_admin");

export {
  DATA_COLLECTION_ROLES,
  ETMF_ROLES,
  ONBOARDING_STORAGE_NAMESPACE,
  SAFETY_ROLES,
  SITE_READINESS_SITE_ROLES,
  SITE_READINESS_SPONSOR_ROLES,
  getOnboardingStorageKey,
  hasRequiredRole,
  isTourRouteAuthorized,
  resolveAuthorizedTaskRoute,
};

function effectiveRolesForStore(personaKey) {
  return effectiveRolesForPersona(personaKey);
}

function resolveTourRouteName(routeName) {
  if (routeName === "safety" && !isScreenAvailable("safety")) {
    return isScreenAvailable("study") ? "study" : "safety";
  }
  return routeName;
}

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
      const storePinia =
        /** @type {{ _p?: import("pinia").Pinia } | undefined} */ (this)?._p;
      /** @type {typeof DEFAULT_SETUP_TASKS} */
      const visible = [];
      for (const task of state.tasks || []) {
        const resolvedRoute = resolveAuthorizedTaskRoute(
          task,
          roles,
          undefined,
          storePinia
        );
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
    if (!targetPinia?._s?.has("onboarding")) {
      return;
    }
    const store = useOnboardingStore(targetPinia);
    if (typeof store.resetSessionState === "function") {
      store.resetSessionState();
    }
  },
  onRehydratePersona(personaKey, pinia) {
    const targetPinia = pinia || getActivePinia();
    if (!targetPinia?._s?.has("onboarding")) {
      return;
    }
    const store = useOnboardingStore(targetPinia);
    if (typeof store.loadPersonaState === "function") {
      store.loadPersonaState(personaKey);
    }
  },
});
