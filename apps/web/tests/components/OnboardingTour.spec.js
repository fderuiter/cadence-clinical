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
    { path: "/overview", name: "overview", component: DummyView },
    { path: "/studies/:studyCode?", name: "study", component: DummyView },
    { path: "/ecrf", name: "ecrf", component: DummyView },
    { path: "/ctms", name: "ctms", component: DummyView },
    { path: "/ssu", name: "ssu", component: DummyView },
  ],
});

describe("Onboarding Guided Tour & Opt-in Live Sequences Spec", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    window.localStorage.clear();
  });

  describe("useOnboardingStore", () => {
    it("initializes as opt-in (isActive = false) on sign-in by default", () => {
      const store = useOnboardingStore();
      expect(store.currentStep).toBe(1);
      expect(store.isActive).toBe(false);
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

    it("handles standard step transitions once started", () => {
      const store = useOnboardingStore();
      store.startTour();
      expect(store.isActive).toBe(true);

      store.nextStep("Step 1 Complete");
      expect(store.currentStep).toBe(2);
      expect(
        store.events.some(
          (e) => e.type === "step_completed" && e.target === "Step 1 Complete"
        )
      ).toBe(true);

      store.prevStep();
      expect(store.currentStep).toBe(1);
      expect(store.events.some((e) => e.type === "step_backed")).toBe(true);
    });

    it("handles tour start, dismiss, resume, and complete disablement", () => {
      const store = useOnboardingStore();
      store.startTour();
      expect(store.isActive).toBe(true);

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

      expect(store.currentPersona).toBe("super_admin");
      store.startTour();
      store.nextStep("Step 1 for super_admin");
      store.nextStep("Step 2 for super_admin");
      expect(store.currentStep).toBe(3);

      expect(
        window.localStorage.getItem("onboarding_super_admin_current_step")
      ).toBe("3");

      // Switch to site_crc persona
      authStore.switchPersona("site_crc");
      store.loadPersonaState("site_crc");

      expect(store.currentPersona).toBe("site_crc");
      expect(store.currentStep).toBe(1);
      expect(store.isActive).toBe(false); // Opt-in on unvisited persona
      expect(store.events).toEqual([]);

      // Advance site_crc to step 2
      store.startTour();
      store.nextStep("Step 1 for site_crc");
      expect(store.currentStep).toBe(2);
      expect(
        window.localStorage.getItem("onboarding_site_crc_current_step")
      ).toBe("2");

      // Switch back to super_admin
      authStore.switchPersona("super_admin");
      store.loadPersonaState("super_admin");

      expect(store.currentPersona).toBe("super_admin");
      expect(store.currentStep).toBe(3);
    });

    it("migrates legacy unscoped localStorage keys to super_admin without errors", () => {
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

      expect(window.localStorage.getItem("onboarding_current_step")).toBeNull();
      expect(window.localStorage.getItem("onboarding_events")).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_super_admin_current_step")
      ).toBe("4");
    });

    it("gracefully handles invalid JSON in legacy storage during migration", () => {
      window.localStorage.setItem("onboarding_current_step", "2");
      window.localStorage.setItem("onboarding_events", "corrupted-json{");

      const store = useOnboardingStore();
      expect(() => store.loadPersonaState("super_admin")).not.toThrow();

      expect(store.currentStep).toBe(2);
      expect(store.events).toEqual([]);
    });

    it("persists persona dismissal and disabled preference keys and isolates tour state across personas", () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Sponsor Admin"]);

      expect(store.currentPersona).toBe("super_admin");
      expect(store.isActive).toBe(false);

      store.startTour();
      expect(store.isActive).toBe(true);

      store.dismissTour();
      expect(store.isActive).toBe(false);
      expect(
        window.localStorage.getItem("onboarding_global_dismissed")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_super_admin_is_active")
      ).toBe("false");

      authStore.switchPersona("site_crc");
      store.loadPersonaState("site_crc");

      expect(store.currentPersona).toBe("site_crc");
      expect(store.isActive).toBe(false);
      expect(store.currentStep).toBe(1);
    });

    it("clears dismissal and disabled flags when tour is reset or restarted", () => {
      const store = useOnboardingStore();
      store.disableTour();
      expect(store.disabled).toBe(true);

      store.resetTour();
      expect(store.isActive).toBe(true);
      expect(store.disabled).toBe(false);
      expect(
        window.localStorage.getItem("onboarding_super_admin_is_active")
      ).toBe("true");
    });

    it("purges other persona storage keys in resetTour while activating the current persona", () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Sponsor Admin"]);

      window.localStorage.setItem("onboarding_super_admin_current_step", "3");
      window.localStorage.setItem("onboarding_super_admin_disabled", "true");
      window.localStorage.setItem("onboarding_site_crc_current_step", "2");
      window.localStorage.setItem("onboarding_site_crc_disabled", "true");

      store.resetTour();

      expect(
        window.localStorage.getItem("onboarding_site_crc_current_step")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_site_crc_disabled")
      ).toBeNull();

      expect(store.currentStep).toBe(1);
      expect(store.isActive).toBe(true);
      expect(store.disabled).toBe(false);

      authStore.switchPersona("site_crc");
      store.loadPersonaState("site_crc");
      expect(store.currentPersona).toBe("site_crc");
      expect(store.currentStep).toBe(1);
      expect(store.isActive).toBe(false);
      expect(store.disabled).toBe(false);
    });
  });

  describe("OnboardingTour.vue component", () => {
    it("remains hidden on mount until explicitly started (opt-in)", () => {
      const authStore = useAuthStore();
      authStore.rawRoles = ["Sponsor Designer"];

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });

      expect(wrapper.find(".onboarding-popover-card").exists()).toBe(false);
    });

    it("renders popover when started and matches UX-1 role-based prompts without product acronyms", async () => {
      const store = useOnboardingStore();
      const authStore = useAuthStore();
      authStore.rawRoles = ["Sponsor Designer"];
      store.startTour();

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });

      expect(wrapper.find(".onboarding-popover-card").exists()).toBe(true);
      expect(wrapper.text()).toContain("Welcome to Protocol Design");
      expect(wrapper.text()).toContain("Welcome, Designer!");
      expect(wrapper.text()).not.toContain("Sandbox");
      expect(wrapper.text()).not.toContain("EDC");
      expect(wrapper.text()).not.toContain("CTMS");

      await wrapper.find(".btn-tour-next").trigger("click");
      expect(store.currentStep).toBe(2);
    });

    it("displays appropriate prompts for Data Manager and Sponsor Admin when started", () => {
      const store = useOnboardingStore();
      const authStore = useAuthStore();
      authStore.rawRoles = ["Data Manager"];
      store.startTour();

      const dmWrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });
      expect(dmWrapper.text()).toContain("Welcome, Data Manager!");
      dmWrapper.unmount();

      authStore.rawRoles = ["Sponsor Admin"];
      store.startTour();
      const adminWrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });
      expect(adminWrapper.text()).toContain("Welcome, Administrator!");
      adminWrapper.unmount();
    });

    it("supports dismissing and completely disabling from UI buttons", async () => {
      const store = useOnboardingStore();
      store.startTour();

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });

      await wrapper.find(".btn-tour-dismiss").trigger("click");
      expect(store.isActive).toBe(false);

      store.resumeTour();
      await wrapper.vm.$nextTick();
      await wrapper.find(".btn-tour-disable").trigger("click");
      expect(store.disabled).toBe(true);
      expect(store.isActive).toBe(false);
    });

    it("maintains popover coordinates during step transitions when element query is delayed and retries across frames", async () => {
      const store = useOnboardingStore();
      store.startTour();
      store.currentStep = 2; // super_admin step 2 targets #nav-studies

      const targetStudies = document.createElement("a");
      targetStudies.id = "nav-studies";
      targetStudies.getBoundingClientRect = () => ({
        top: 100,
        bottom: 140,
        left: 200,
        right: 300,
        width: 100,
        height: 40,
      });
      document.body.appendChild(targetStudies);

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
        attachTo: document.body,
      });

      await wrapper.vm.$nextTick();
      await new Promise((resolve) => requestAnimationFrame(resolve));

      const popoverCard = wrapper.find(".onboarding-popover-card");
      expect(popoverCard.element.style.top).toBe("152px");
      expect(popoverCard.element.style.left).toBe("250px");

      document.body.removeChild(targetStudies);

      // Transition to step 3 (super_admin targets #nav-data-collection)
      store.currentStep = 3;
      await wrapper.vm.$nextTick();

      expect(popoverCard.element.style.top).toBe("152px");
      expect(popoverCard.element.style.left).toBe("250px");

      const targetDataCollection = document.createElement("a");
      targetDataCollection.id = "nav-data-collection";
      targetDataCollection.getBoundingClientRect = () => ({
        top: 100,
        bottom: 140,
        left: 400,
        right: 500,
        width: 100,
        height: 40,
      });
      document.body.appendChild(targetDataCollection);

      await new Promise((resolve) => requestAnimationFrame(resolve));

      expect(popoverCard.element.style.top).toBe("152px");
      expect(popoverCard.element.style.left).toBe("450px");

      document.body.removeChild(targetDataCollection);
      wrapper.unmount();
    });

    it("falls back to viewport center after retry attempts expire if target element never appears", async () => {
      const store = useOnboardingStore();
      store.startTour();
      store.currentStep = 2;

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
        attachTo: document.body,
      });

      await wrapper.vm.$nextTick();
      for (let i = 0; i < 7; i++) {
        await new Promise((resolve) => requestAnimationFrame(resolve));
      }

      const popoverCard = wrapper.find(".onboarding-popover-card");
      expect(popoverCard.element.style.top).toBe("50%");
      expect(popoverCard.element.style.left).toBe("50%");

      wrapper.unmount();
    });

    it("renders live role-tailored step sequences across all six clinical personas", async () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), [
        "Sponsor Admin",
        "Sponsor Designer",
        "Data Manager",
        "CRC",
        "CRA",
        "Auditor",
      ]);

      const expectedByPersona = [
        {
          persona: "data_manager",
          title: "Welcome to Data Management",
          routeName: "ecrf",
        },
        {
          persona: "cra_monitor",
          title: "Welcome to Clinical Operations",
          routeName: "ctms",
        },
        {
          persona: "auditor",
          title: "Welcome to Regulatory & Audit",
          routeName: "study",
        },
        {
          persona: "site_crc",
          title: "Welcome to Data Collection",
          routeName: "ecrf",
        },
        {
          persona: "sponsor_designer",
          title: "Welcome to Protocol Design",
          routeName: "study",
        },
        {
          persona: "super_admin",
          title: "Welcome to Cadence Clinical",
          routeName: "study",
        },
      ];

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });

      for (const { persona, title, routeName } of expectedByPersona) {
        authStore.switchPersona(persona);
        store.loadPersonaState(persona);
        store.startTour();
        await wrapper.vm.$nextTick();

        expect(wrapper.find(".onboarding-popover-card").exists()).toBe(true);
        expect(wrapper.text()).toContain(title);
        expect(store.currentStepConfig.routeName).toBe(routeName);
      }

      wrapper.unmount();
    });

    it("invokes router navigation only to live authorized routes when advancing across cross-view steps", async () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Data Manager"]);

      authStore.switchPersona("data_manager");
      store.loadPersonaState("data_manager");
      store.startTour();

      const pushSpy = vi.spyOn(router, "push");

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });

      // Advance step 1 (ecrf) -> step 2 (ecrf) -> step 3 (study)
      await wrapper.find(".btn-tour-next").trigger("click");
      await wrapper.find(".btn-tour-next").trigger("click");

      expect(store.currentStep).toBe(3);
      expect(store.currentStepConfig.routeName).toBe("study");
      expect(pushSpy).toHaveBeenCalledWith(
        expect.objectContaining({ name: "study" })
      );
    });

    it("never navigates an unauthorized persona view switch to /ctms or /ecrf (UX-6)", async () => {
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Auditor"]);

      authStore.switchPersona("cra_monitor");
      store.loadPersonaState("cra_monitor");
      store.startTour();

      const pushSpy = vi.spyOn(router, "push");
      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });

      // Only the authorized 'study' step remains in the sequence for an Auditor
      expect(store.currentStepConfig.routeName).toBe("study");
      expect(pushSpy).not.toHaveBeenCalledWith(
        expect.objectContaining({ name: "ctms" })
      );
      wrapper.unmount();
    });
  });

  describe("AppShell.vue clean shell & opt-in onboarding integration", () => {
    it("omits legacy telemetry diagnostic footers and keeps tour closed on initial sign-in", async () => {
      const authStore = signInAs(useAuthStore(), ["Sponsor Admin"]);
      expect(authStore.isAuthenticated).toBe(true);

      const wrapper = mount(AppShell, {
        global: { plugins: [router] },
      });

      expect(wrapper.find("#toggle-diagnostic-panel").exists()).toBe(false);
      expect(wrapper.find(".diagnostic-panel").exists()).toBe(false);
      expect(wrapper.find(".onboarding-popover-card").exists()).toBe(false);
    });
  });
});
