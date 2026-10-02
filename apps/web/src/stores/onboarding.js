import { defineStore } from "pinia";
import { useAuthStore } from "./auth";

export const DEFAULT_SETUP_TASKS = [
  {
    id: "task_protocol",
    title: "Digitize & Author Protocol",
    description: "Upload protocol document or author study metadata and objectives",
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

      const storedCompletedTasks = window.localStorage.getItem(completedTasksKey);
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
      const searchStr = `${event.type} ${event.target} ${event.details}`.toLowerCase();
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
