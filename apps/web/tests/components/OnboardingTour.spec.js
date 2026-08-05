import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import OnboardingTour from "../../src/components/OnboardingTour.vue";
import AppShell from "../../src/components/AppShell.vue";
import { useOnboardingStore } from "../../src/stores/onboarding";
import { useAuthStore } from "../../src/stores/auth";
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
        window.localStorage.getItem("onboarding_events")
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
  });

  describe("OnboardingTour.vue component", () => {
    it("renders popover correctly when active and matches role-based prompts", async () => {
      const store = useOnboardingStore();
      const authStore = useAuthStore();
      authStore.isDemoMode = false;
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
      authStore.isDemoMode = false;
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
      authStore.isDemoMode = false;
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
      store.isActive = true;
      await wrapper.vm.$nextTick();
      await wrapper.find(".btn-tour-disable").trigger("click");
      expect(store.disabled).toBe(true);
      expect(store.isActive).toBe(false);
    });
  });

  describe("AppShell.vue footer and diagnostic integration", () => {
    it("toggles the telemetry diagnostic panel and handles clear/reset", async () => {
      const store = useOnboardingStore();
      const authStore = useAuthStore();
      authStore.isDemoMode = true; // puts in demo mode to render standard shell headers

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
