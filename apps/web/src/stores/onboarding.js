import { defineStore } from "pinia";
import { useAuthStore } from "./auth";
import {
  getOnboardingPreferences,
  saveOnboardingPreferences,
} from "../api/onboarding.js";

let syncDebounceTimer = null;

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

    if (
      legacyStep !== null ||
      legacyIsActive !== null ||
      legacyDisabled !== null ||
      legacyEvents !== null
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

      window.localStorage.removeItem("onboarding_current_step");
      window.localStorage.removeItem("onboarding_is_active");
      window.localStorage.removeItem("onboarding_disabled");
      window.localStorage.removeItem("onboarding_events");
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
  let updatedAt = "1970-01-01T00:00:00.000Z";

  if (typeof window !== "undefined" && window.localStorage) {
    try {
      migrateLegacyKeys();

      const stepKey = `onboarding_${personaKey}_current_step`;
      const activeKey = `onboarding_${personaKey}_is_active`;
      const disabledKey = `onboarding_${personaKey}_disabled`;
      const bypassKey = `onboarding_${personaKey}_bypass_overview`;
      const tasksKey = `onboarding_${personaKey}_completed_tasks`;
      const eventsKey = `onboarding_${personaKey}_events`;
      const updatedKey = `onboarding_${personaKey}_updated_at`;

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

      const storedTasks = window.localStorage.getItem(tasksKey);
      if (storedTasks !== null) {
        try {
          const parsedTasks = JSON.parse(storedTasks);
          if (Array.isArray(parsedTasks)) {
            completedTasks = parsedTasks;
          }
        } catch (e) {
          console.error(
            "Failed to parse onboarding tasks from localStorage",
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
            "Failed to parse onboarding state from localStorage",
            e
          );
        }
      }

      const storedUpdatedAt = window.localStorage.getItem(updatedKey);
      if (storedUpdatedAt) {
        updatedAt = storedUpdatedAt;
      } else if (events.length > 0 && events[events.length - 1]?.timestamp) {
        updatedAt = events[events.length - 1].timestamp;
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
    updatedAt,
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
      updatedAt: initial.updatedAt,
      isSyncing: false,
    };
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
      this.updatedAt = loaded.updatedAt;

      // Asynchronously sync with backend API
      this.syncWithBackend(targetPersona);
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
            `onboarding_${targetPersona}_updated_at`,
            this.updatedAt
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
    touchTimestamp() {
      this.updatedAt = new Date().toISOString();
      this.persist();
      this.scheduleSyncToBackend();
    },
    scheduleSyncToBackend() {
      if (syncDebounceTimer) {
        clearTimeout(syncDebounceTimer);
      }
      syncDebounceTimer = setTimeout(() => {
        this.saveToBackend(this.currentPersona);
      }, 300);
    },
    async saveToBackend(personaKey) {
      const targetPersona =
        personaKey || this.currentPersona || getActivePersona();
      try {
        const payload = {
          currentStep: this.currentStep,
          isActive: this.isActive,
          disabled: this.disabled,
          events: this.events,
          updatedAt: this.updatedAt,
        };
        const response = await saveOnboardingPreferences(
          targetPersona,
          payload
        );
        if (response && response.updatedAt) {
          this.updatedAt = response.updatedAt;
          this.persist();
        }
      } catch (e) {
        console.warn("Failed to save onboarding state to backend API:", e);
      }
    },
    async syncWithBackend(personaKey) {
      const targetPersona =
        personaKey || this.currentPersona || getActivePersona();
      this.isSyncing = true;
      try {
        const remote = await getOnboardingPreferences(targetPersona);
        if (!remote) return;

        let remoteEvents = [];
        if (remote.eventsJson) {
          try {
            remoteEvents = JSON.parse(remote.eventsJson);
            if (!Array.isArray(remoteEvents)) remoteEvents = [];
          } catch {
            remoteEvents = [];
          }
        }

        const localTime = new Date(this.updatedAt || 0).getTime();
        const remoteTime = new Date(remote.updatedAt || 0).getTime();

        if (remoteTime > localTime) {
          // Backend is strictly newer: adopt remote state
          this.currentStep = remote.currentStep;
          this.isActive = remote.isActive;
          this.disabled = remote.disabled;
          this.events = remoteEvents;
          this.updatedAt = remote.updatedAt;
          this.persist();
        } else if (localTime > remoteTime) {
          // Local changes are newer: push local state to backend
          await this.saveToBackend(targetPersona);
        }
      } catch (e) {
        console.warn(
          "Failed to fetch onboarding preferences from backend API:",
          e
        );
      } finally {
        this.isSyncing = false;
      }
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
      this.touchTimestamp();
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
      this.addEvent(
        "step_completed",
        targetDescription,
        `Completed step ${this.currentStep}`
      );
      this.currentStep++;
      this.touchTimestamp();
    },
    prevStep() {
      if (this.currentStep > 1) {
        this.currentStep--;
        this.addEvent(
          "step_backed",
          `Step ${this.currentStep + 1}`,
          `Returned to step ${this.currentStep}`
        );
        this.touchTimestamp();
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
      this.isActive = true;
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
      this.currentStep = 1;
      this.isActive = true;
      this.disabled = false;
      this.addEvent(
        "tour_reset",
        "Onboarding Tour Reset",
        "Tour state was fully reset"
      );
    },
    clearEvents() {
      this.events = [];
      this.touchTimestamp();
    },
  },
});
