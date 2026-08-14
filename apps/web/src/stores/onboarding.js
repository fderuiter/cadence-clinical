import { defineStore } from "pinia";

export const useOnboardingStore = defineStore("onboarding", {
  state: () => {
    let currentStep = 1;
    let isActive = false;
    let disabled = false;
    let events = [];

    if (typeof window !== "undefined" && window.localStorage) {
      try {
        const storedStep = window.localStorage.getItem(
          "onboarding_current_step"
        );
        if (storedStep !== null) {
          currentStep = parseInt(storedStep, 10);
        }
        const storedIsActive = window.localStorage.getItem(
          "onboarding_is_active"
        );
        if (storedIsActive !== null) {
          isActive = storedIsActive === "true";
        }
        const storedDisabled = window.localStorage.getItem(
          "onboarding_disabled"
        );
        if (storedDisabled !== null) {
          disabled = storedDisabled === "true";
        }
        const storedEvents = window.localStorage.getItem("onboarding_events");
        if (storedEvents !== null) {
          events = JSON.parse(storedEvents);
        }
      } catch (e) {
        console.error("Failed to parse onboarding state from localStorage", e);
      }
    }

    return {
      currentStep,
      isActive,
      disabled,
      events,
    };
  },
  actions: {
    persist() {
      if (typeof window !== "undefined" && window.localStorage) {
        try {
          window.localStorage.setItem(
            "onboarding_current_step",
            this.currentStep
          );
          window.localStorage.setItem("onboarding_is_active", this.isActive);
          window.localStorage.setItem("onboarding_disabled", this.disabled);
          window.localStorage.setItem(
            "onboarding_events",
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
