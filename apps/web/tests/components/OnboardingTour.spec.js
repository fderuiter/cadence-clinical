import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import OnboardingTour from "../../src/components/OnboardingTour.vue";
import AppShell from "../../src/components/AppShell.vue";
import { useOnboardingStore } from "../../src/stores/onboarding";
import { useAuthStore } from "../../src/stores/auth";
import { signInAs } from "../helpers/signIn.js";
import { createRouter, createWebHistory } from "vue-router";

const DummyView = { template: "<div>Dummy</div>" };
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "home", component: DummyView },
    { path: "/mdr", name: "mdr", component: DummyView },
    { path: "/ecrf", name: "ecrf", component: DummyView },
    { path: "/ctms", name: "ctms", component: DummyView },
    { path: "/rules", name: "rules", component: DummyView },
    { path: "/audit", name: "audit", component: DummyView },
    { path: "/etmf", name: "etmf", component: DummyView },
    { path: "/notifications", name: "notifications", component: DummyView },
  ],
});

describe("Onboarding Guided Tour & Event Buffer Spec", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    window.localStorage.clear();
  });

  describe("useOnboardingStore", () => {
    it("initializes with default values", () => {
      const store = useOnboardingStore();
      expect(store.currentStep).toBe(1);
      expect(store.isActive).toBe(true);
      expect(store.disabled).toBe(false);
      expect(store.events).toEqual([]);
    });

    it("logs events and persists them to localStorage", () => {
      const store = useOnboardingStore();
      store.addEvent("test_type", "test_target", "test_details");
      expect(store.events.length).toBe(1);
      expect(store.events[0].type).toBe("test_type");
      expect(store.events[0].target).toBe("test_target");
      expect(store.events[0].details).toBe("test_details");
      expect(store.events[0].timestamp).toBeDefined();

      const savedEvents = JSON.parse(
        window.localStorage.getItem(`onboarding_${store.currentPersona}_events`)
      );
      expect(savedEvents.length).toBe(1);
      expect(savedEvents[0].type).toBe("test_type");
    });

    it("handles standard step transitions", () => {
      const store = useOnboardingStore();
      store.nextStep("Step 1 Complete");
      expect(store.currentStep).toBe(2);
      expect(store.events.length).toBe(1);
      expect(store.events[0].type).toBe("step_completed");
      expect(store.events[0].target).toBe("Step 1 Complete");

      store.prevStep();
      expect(store.currentStep).toBe(1);
      expect(store.events.length).toBe(2);
      expect(store.events[1].type).toBe("step_backed");
    });

    it("handles tour dismiss, resume, and complete disablement", () => {
      const store = useOnboardingStore();
      store.dismissTour();
      expect(store.isActive).toBe(false);
      expect(
        store.events.find((e) => e.type === "tour_dismissed")
      ).toBeDefined();

      store.resumeTour();
      expect(store.isActive).toBe(true);
      expect(store.events.find((e) => e.type === "tour_resumed")).toBeDefined();

      store.disableTour();
      expect(store.disabled).toBe(true);
      expect(store.isActive).toBe(false);
      expect(
        store.events.find((e) => e.type === "tour_disabled")
      ).toBeDefined();
    });

    it("maintains distinct step progress and event history per persona", () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Sponsor Admin"]);

      // Start in super_admin
      expect(store.currentPersona).toBe("super_admin");
      store.nextStep("Step 1 for super_admin");
      store.nextStep("Step 2 for super_admin");
      expect(store.currentStep).toBe(3);
      expect(store.events.length).toBe(2);

      expect(
        window.localStorage.getItem("onboarding_super_admin_current_step")
      ).toBe("3");

      // Switch to site_crc persona
      authStore.switchPersona("site_crc");
      store.loadPersonaState("site_crc");

      expect(store.currentPersona).toBe("site_crc");
      expect(store.currentStep).toBe(1); // Default initial step for new persona
      expect(store.events).toEqual([]); // Empty events for new persona

      // Advance site_crc to step 2 and log event
      store.nextStep("Step 1 for site_crc");
      expect(store.currentStep).toBe(2);
      expect(store.events.length).toBe(1);

      expect(
        window.localStorage.getItem("onboarding_site_crc_current_step")
      ).toBe("2");

      // Switch back to super_admin
      authStore.switchPersona("super_admin");
      store.loadPersonaState("super_admin");

      expect(store.currentPersona).toBe("super_admin");
      expect(store.currentStep).toBe(3); // Restored step progress
      expect(store.events.length).toBe(2); // Restored event history
    });

    it("migrates legacy unscoped localStorage keys to super_admin without errors", () => {
      // Set legacy unscoped keys in localStorage
      window.localStorage.setItem("onboarding_current_step", "4");
      window.localStorage.setItem("onboarding_is_active", "false");
      window.localStorage.setItem("onboarding_disabled", "false");
      window.localStorage.setItem(
        "onboarding_events",
        JSON.stringify([
          { id: "ev-legacy-1", type: "legacy_event", target: "Legacy Target" },
        ])
      );

      const store = useOnboardingStore();
      store.loadPersonaState("super_admin");

      expect(store.currentStep).toBe(4);
      expect(store.isActive).toBe(false);
      expect(store.events.length).toBe(1);
      expect(store.events[0].type).toBe("legacy_event");

      // Legacy keys should be removed and migrated to super_admin scoped keys
      expect(window.localStorage.getItem("onboarding_current_step")).toBeNull();
      expect(window.localStorage.getItem("onboarding_events")).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_super_admin_current_step")
      ).toBe("4");
      expect(
        window.localStorage.getItem("onboarding_super_admin_events")
      ).not.toBeNull();
    });

    it("gracefully handles invalid JSON in legacy storage during migration", () => {
      window.localStorage.setItem("onboarding_current_step", "2");
      window.localStorage.setItem("onboarding_events", "corrupted-json{");

      const store = useOnboardingStore();
      expect(() => store.loadPersonaState("super_admin")).not.toThrow();

      expect(store.currentStep).toBe(2);
      expect(store.events).toEqual([]); // Falls back to empty array on parse error
    });

    it("persists persona dismissal and disabled preference keys and isolates tour popover across personas", () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Sponsor Admin"]);

      expect(store.currentPersona).toBe("super_admin");
      expect(store.isActive).toBe(true);

      // Dismiss tour in super_admin
      store.dismissTour();
      expect(store.isActive).toBe(false);
      expect(
        window.localStorage.getItem("onboarding_global_dismissed")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_super_admin_is_active")
      ).toBe("false");

      // Switch to unvisited persona site_crc
      authStore.switchPersona("site_crc");
      store.loadPersonaState("site_crc");

      expect(store.currentPersona).toBe("site_crc");
      expect(store.isActive).toBe(true); // Isolated active state for unvisited persona
      expect(store.currentStep).toBe(1); // Step position initialized for persona

      // Advance step in site_crc
      store.nextStep("Step 1 for site_crc");
      expect(store.currentStep).toBe(2);
      expect(
        window.localStorage.getItem("onboarding_site_crc_current_step")
      ).toBe("2");

      // Switch back to super_admin
      authStore.switchPersona("super_admin");
      store.loadPersonaState("super_admin");
      expect(store.currentPersona).toBe("super_admin");
      expect(store.isActive).toBe(false);
    });

    it("prevents future prompts for a persona when disabled for that persona", () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Sponsor Admin"]);

      store.disableTour();
      expect(store.disabled).toBe(true);
      expect(store.isActive).toBe(false);
      expect(
        window.localStorage.getItem("onboarding_global_disabled")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_super_admin_disabled")
      ).toBe("true");

      // Switch to unvisited persona site_crc
      authStore.switchPersona("site_crc");
      store.loadPersonaState("site_crc");

      expect(store.disabled).toBe(false);
      expect(store.isActive).toBe(true);
    });

    it("clears dismissal and disabled flags when tour is reset or restarted", () => {
      const store = useOnboardingStore();
      store.dismissTour();
      expect(
        window.localStorage.getItem("onboarding_global_dismissed")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_super_admin_is_active")
      ).toBe("false");

      store.resetTour();
      expect(store.isActive).toBe(true);
      expect(store.disabled).toBe(false);
      expect(
        window.localStorage.getItem("onboarding_global_dismissed")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_global_disabled")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_super_admin_is_active")
      ).toBe("true");
    });
  });

  describe("OnboardingTour.vue component", () => {
    it("renders popover correctly when active and matches role-based prompts", async () => {
      const store = useOnboardingStore();
      const authStore = useAuthStore();
      authStore.rawRoles = ["Sponsor Designer"]; // maps to sponsor_designer

      const wrapper = mount(OnboardingTour, {
        props: {
          activeTab: "soa",
        },
      });

      // Step 1 Welcome popover
      expect(wrapper.find(".onboarding-popover-card").exists()).toBe(true);
      expect(wrapper.text()).toContain("Welcome to the Sandbox");
      expect(wrapper.text()).toContain(
        "Welcome, Designer! Ready to architect clinical protocols and CDISC USDM data structures with maximum accuracy?"
      );

      // Click Next
      await wrapper.find(".btn-tour-next").trigger("click");
      expect(store.currentStep).toBe(2);
      expect(wrapper.emitted("update:activeTab")).toBeTruthy();
      expect(wrapper.emitted("update:activeTab")[0]).toEqual(["soa"]);
    });

    it("displays appropriate prompts for Data Manager", () => {
      const authStore = useAuthStore();
      authStore.rawRoles = ["Data Manager"]; // maps to data_manager

      const wrapper = mount(OnboardingTour, {
        props: {
          activeTab: "soa",
        },
      });

      expect(wrapper.text()).toContain("Welcome, Data Manager!");
    });

    it("displays appropriate prompts for Sponsor Admin", () => {
      const authStore = useAuthStore();
      authStore.rawRoles = ["Sponsor Admin"]; // maps to sponsor_admin

      const wrapper = mount(OnboardingTour, {
        props: {
          activeTab: "soa",
        },
      });

      expect(wrapper.text()).toContain("Welcome, Administrator!");
    });

    it("supports dismissing and completely disabling from UI buttons", async () => {
      const store = useOnboardingStore();
      const wrapper = mount(OnboardingTour, {
        props: {
          activeTab: "soa",
        },
      });

      await wrapper.find(".btn-tour-dismiss").trigger("click");
      expect(store.isActive).toBe(false);

      // Re-enable for testing disable
      store.resumeTour();
      await wrapper.vm.$nextTick();
      await wrapper.find(".btn-tour-disable").trigger("click");
      expect(store.disabled).toBe(true);
      expect(store.isActive).toBe(false);
    });

    it("automatically reloads persona onboarding state on authStore persona change", async () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Sponsor Admin"]);

      // Set up existing state for site_crc in localStorage
      window.localStorage.setItem("onboarding_site_crc_current_step", "3");
      window.localStorage.setItem("onboarding_site_crc_is_active", "true");

      const wrapper = mount(OnboardingTour, {
        props: {
          activeTab: "soa",
        },
      });

      expect(store.currentStep).toBe(1);

      // Switch persona to site_crc
      authStore.switchPersona("site_crc");
      await wrapper.vm.$nextTick();

      expect(store.currentPersona).toBe("site_crc");
      expect(store.currentStep).toBe(3);
    });

    it("does not render popover card when switching to an unvisited persona after dismissal", async () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Sponsor Admin"]);

      const wrapper = mount(OnboardingTour, {
        props: {
          activeTab: "soa",
        },
      });

      expect(wrapper.find(".onboarding-popover-card").exists()).toBe(true);

      // Dismiss tour
      await wrapper.find(".btn-tour-dismiss").trigger("click");
      expect(store.isActive).toBe(false);
      expect(wrapper.find(".onboarding-popover-card").exists()).toBe(false);

      // Switch persona to unvisited site_crc
      authStore.switchPersona("site_crc");
      await wrapper.vm.$nextTick();

      expect(store.currentPersona).toBe("site_crc");
      expect(store.isActive).toBe(true);
      expect(wrapper.find(".onboarding-popover-card").exists()).toBe(true);
    });
  });

  describe("AppShell.vue footer and diagnostic integration", () => {
    it("toggles the telemetry diagnostic panel and handles clear/reset", async () => {
      const store = useOnboardingStore();
      const authStore = useAuthStore();

      const wrapper = mount(AppShell, {
        global: {
          plugins: [router],
        },
      });

      // Verify the toggle button is present in the footer
      const toggleBtn = wrapper.find("#toggle-diagnostic-panel");
      expect(toggleBtn.exists()).toBe(true);
      expect(wrapper.find(".diagnostic-panel").exists()).toBe(false);

      // Toggle panel open
      await toggleBtn.trigger("click");
      expect(wrapper.find(".diagnostic-panel").exists()).toBe(true);
      expect(wrapper.text()).toContain("Telemetry Diagnostic Log");

      // Log a mock event and verify it displays
      store.addEvent("test_click", "MDR Tab Button");
      await wrapper.vm.$nextTick();

      expect(wrapper.find(".diagnostic-panel").text()).toContain(
        "MDR Tab Button"
      );

      // Verify reset tour button calls resetTour
      const resetSpy = vi.spyOn(store, "resetTour");
      await wrapper.find(".btn-reset-tour").trigger("click");
      expect(resetSpy).toHaveBeenCalled();

      // Verify clear events button calls clearEvents
      const clearSpy = vi.spyOn(store, "clearEvents");
      await wrapper.find(".btn-clear-events").trigger("click");
      expect(clearSpy).toHaveBeenCalled();
    });
  });
});
