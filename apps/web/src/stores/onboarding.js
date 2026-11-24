import { defineStore, getActivePinia } from "pinia";
import {
  registerLogoutCleanup,
  registerOnboardingSessionBridge,
} from "../auth/sessionCoordinator.js";
import { resolvePersonaCatalogKey } from "../catalog/staffPersonaCore.js";
import { PERSONA_TOUR_SEQUENCES } from "../catalog/staffPersonaOnboarding.js";
import {
  demoNowIso,
  demoNowMs,
  demoRandomToken,
  nextDemoStep,
} from "../utils/demoMode";
import { scopePrefix, storageScopeOf } from "../utils/userScopedStorage.js";
import {
  DEFAULT_SETUP_TASKS,
  cloneCatalogChecklist,
  effectiveRolesForPersona,
  effectiveRolesForPersonaLens,
  getAuthStoreSafe,
  hasRequiredRole,
  isTourRouteAuthorized,
  readOnboardingJsonArray,
  resolveAuthorizedTaskRoute,
  resolveOnboardingStorageKey,
} from "./onboardingSummary";

export {
  DEFAULT_SETUP_TASKS,
  effectiveRolesForPersona,
  effectiveRolesForPersonaLens,
  hasRequiredRole,
  isTourRouteAuthorized,
  resolveAuthorizedTaskRoute,
};

export const ROLE_STEP_SEQUENCES = PERSONA_TOUR_SEQUENCES;

const LEGACY_FIELDS = [
  "current_step",
  "is_active",
  "disabled",
  "events",
  "tasks",
];

function getActivePersona() {
  return getAuthStoreSafe()?.currentPersona || "super_admin";
}

function migrateLegacyKeysIfUnscoped() {
  if (typeof window === "undefined" || !window.localStorage) return;
  const ls = window.localStorage;
  if (storageScopeOf(getAuthStoreSafe())) {
    for (const field of LEGACY_FIELDS) {
      ls.removeItem(`onboarding_${field}`);
    }
    return;
  }
  try {
    let hasLegacy = false;
    for (const field of LEGACY_FIELDS) {
      const val = ls.getItem(`onboarding_${field}`);
      if (val !== null) {
        hasLegacy = true;
        const dest = `onboarding_super_admin_${field}`;
        if (ls.getItem(dest) === null) {
          ls.setItem(dest, val);
        }
      }
    }
    if (hasLegacy) {
      for (const field of LEGACY_FIELDS) {
        ls.removeItem(`onboarding_${field}`);
      }
    }
  } catch (e) {
    console.error("Failed to migrate legacy onboarding storage keys", e);
  }
}

function readStorageForPersona(personaKey) {
  let currentStep = 1;
  let isActive = false;
  let disabled = false;
  let bypassOverview = false;
  let events = [];
  const authStore = getAuthStoreSafe();
  let tasks = cloneCatalogChecklist(personaKey, authStore);

  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const ls = window.localStorage;
      migrateLegacyKeysIfUnscoped();

      const storedStep = ls.getItem(
        resolveOnboardingStorageKey(personaKey, "current_step", authStore)
      );
      if (storedStep !== null) {
        const parsed = parseInt(storedStep, 10);
        if (!isNaN(parsed) && parsed > 0) {
          currentStep = parsed;
        }
      }

      disabled =
        ls.getItem(
          resolveOnboardingStorageKey(personaKey, "disabled", authStore)
        ) === "true";
      bypassOverview =
        ls.getItem(
          resolveOnboardingStorageKey(personaKey, "bypass_overview", authStore)
        ) === "true";
      const storedIsActive = ls.getItem(
        resolveOnboardingStorageKey(personaKey, "is_active", authStore)
      );
      isActive = disabled ? false : storedIsActive === "true";

      const parsedCompleted = readOnboardingJsonArray(
        ls,
        resolveOnboardingStorageKey(personaKey, "completed_tasks", authStore)
      );
      const storedCompletedIds = parsedCompleted
        ? parsedCompleted.filter((id) => typeof id === "string")
        : [];

      const parsedEvents = readOnboardingJsonArray(
        ls,
        resolveOnboardingStorageKey(personaKey, "events", authStore)
      );
      if (parsedEvents) {
        events = parsedEvents;
      }

      const parsedTasks = readOnboardingJsonArray(
        ls,
        resolveOnboardingStorageKey(personaKey, "tasks", authStore)
      );
      if (parsedTasks && parsedTasks.length > 0) {
        tasks = tasks.map((defTask) => {
          const found = parsedTasks.find((t) => t.id === defTask.id);
          const completed = found
            ? Boolean(found.completed)
            : storedCompletedIds.includes(defTask.id);
          return { ...defTask, completed };
        });
      } else if (storedCompletedIds.length > 0) {
        tasks = tasks.map((defTask) => ({
          ...defTask,
          completed: storedCompletedIds.includes(defTask.id),
        }));
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
    events,
    tasks,
  };
}

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
  const authStore = getAuthStoreSafe();
  const roles = authStore?.normalizedRoles || [];
  const rawRoles = authStore?.rawRoles || [];
  let baseSequence;

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
  } else {
    const resolvedKey = resolvePersonaCatalogKey(
      personaKey === "super_admin" ? null : personaKey,
      rawRoles
    );
    baseSequence =
      ROLE_STEP_SEQUENCES[resolvedKey] ||
      ROLE_STEP_SEQUENCES[personaKey] ||
      ROLE_STEP_SEQUENCES.super_admin;
  }

  const effectiveRoles = effectiveRolesForPersonaLens(personaKey, authStore);
  const filtered = (baseSequence || [])
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
      events: initial.events,
      tasks: initial.tasks,
      isDrawerOpen: false,
    };
  },
  getters: {
    visibleTasks(state) {
      const roles = effectiveRolesForPersonaLens(state.currentPersona);
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
    completedTasks(state) {
      return (state.tasks || []).filter((t) => t.completed).map((t) => t.id);
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
      this.events = loaded.events;
      this.tasks = loaded.tasks;
      if (clampedStep !== loaded.currentStep) {
        this.persist();
      }
    },
    clearSessionState() {
      this.currentPersona = "super_admin";
      this.currentStep = 1;
      this.isActive = false;
      this.disabled = false;
      this.bypassOverview = false;
      this.events = [];
      this.tasks = cloneCatalogChecklist("super_admin");
      this.isDrawerOpen = false;
    },
    persist() {
      if (typeof window !== "undefined" && window.localStorage) {
        try {
          const authStore = getAuthStoreSafe();
          const targetPersona = this.currentPersona || getActivePersona();
          const entries = [
            ["current_step", String(this.currentStep)],
            ["is_active", String(this.isActive)],
            ["disabled", String(this.disabled)],
            ["bypass_overview", String(this.bypassOverview)],
            ["completed_tasks", JSON.stringify(this.completedTasks)],
            ["events", JSON.stringify(this.events)],
            ["tasks", JSON.stringify(this.tasks)],
          ];
          for (const [field, value] of entries) {
            const key = resolveOnboardingStorageKey(
              targetPersona,
              field,
              authStore
            );
            if (key) {
              window.localStorage.setItem(key, value);
            }
          }
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
    },
    toggleTaskCompletion(taskId) {
      if (taskId) this.toggleTask(taskId);
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
      const task = (this.tasks || []).find((t) => t.id === taskId);
      if (task) {
        task.completed =
          typeof isCompleted === "boolean" ? isCompleted : !task.completed;
        this.addEvent(
          "task_status_changed",
          `Task ${task.title}`,
          `Task set to ${task.completed ? "completed" : "incomplete"}`
        );
      }
    },
    completeTask(taskId) {
      this.toggleTask(taskId, true);
    },
    resetChecklist() {
      const targetPersona = this.currentPersona || getActivePersona();
      this.tasks = cloneCatalogChecklist(targetPersona);
      this.addEvent(
        "checklist_reset",
        "Onboarding Checklist Reset",
        "Checklist task completion reset"
      );
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
    },
    nextStep(targetDescription) {
      const completedStep = this.currentStep;
      this.currentStep++;
      this.addEvent(
        "step_completed",
        targetDescription,
        `Completed step ${completedStep}`
      );
    },
    prevStep() {
      if (this.currentStep > 1) {
        this.currentStep--;
        this.addEvent(
          "step_backed",
          `Step ${this.currentStep + 1}`,
          `Returned to step ${this.currentStep}`
        );
      }
    },
    dismissTour() {
      this.isActive = false;
      this.addEvent(
        "tour_dismissed",
        "Onboarding Tour Dismissed",
        "User dismissed popover"
      );
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
    },
    disableTour() {
      this.disabled = true;
      this.isActive = false;
      this.addEvent(
        "tour_disabled",
        "Onboarding Tour Disabled",
        "User completely disabled onboarding"
      );
    },
    resetTour() {
      if (typeof window !== "undefined" && window.localStorage) {
        try {
          const authStore = getAuthStoreSafe();
          const currentPrefix = scopePrefix(storageScopeOf(authStore));
          const keysToRemove = [];
          for (let i = 0; i < window.localStorage.length; i++) {
            const key = window.localStorage.key(i);
            if (!key) continue;
            if (
              key.startsWith("onboarding_") ||
              (currentPrefix && key.startsWith(`${currentPrefix}onboarding:`))
            ) {
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

registerLogoutCleanup(() => {
  const activePinia = getActivePinia();
  const onboardingStore = activePinia?._s?.get("onboarding");
  if (
    onboardingStore &&
    typeof onboardingStore.clearSessionState === "function"
  ) {
    onboardingStore.clearSessionState();
  }
});

registerOnboardingSessionBridge({
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
