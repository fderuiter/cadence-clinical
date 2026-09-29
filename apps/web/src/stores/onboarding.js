import { defineStore } from "pinia";
import { useAuthStore } from "./auth";

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
  let events = [];

  if (typeof window !== "undefined" && window.localStorage) {
    try {
      migrateLegacyKeys();

      const stepKey = `onboarding_${personaKey}_current_step`;
      const activeKey = `onboarding_${personaKey}_is_active`;
      const disabledKey = `onboarding_${personaKey}_disabled`;
      const eventsKey = `onboarding_${personaKey}_events`;

      const storedStep = window.localStorage.getItem(stepKey);
      if (storedStep !== null) {
        const parsed = parseInt(storedStep, 10);
        if (!isNaN(parsed) && parsed > 0) {
          currentStep = parsed;
        }
      }

      const storedIsActive = window.localStorage.getItem(activeKey);
      if (storedIsActive !== null) {
        isActive = storedIsActive === "true";
      }

      const storedDisabled = window.localStorage.getItem(disabledKey);
      if (storedDisabled !== null) {
        disabled = storedDisabled === "true";
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
    } catch (e) {
      console.error("Failed to parse onboarding state from localStorage", e);
    }
  }

  return { currentStep, isActive, disabled, events };
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
      events: initial.events,
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
      this.events = loaded.events;
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
            `onboarding_${targetPersona}_events`,
            JSON.stringify(this.events)
          );
        } catch (e) {
          console.error(
            "Failed to persist onboarding state to localStorage",
            e
          );
        }
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
      this.persist();
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
      this.currentStep = 1;
      this.isActive = true;
      this.disabled = false;
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
