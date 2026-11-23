import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import OnboardingTour from "../../src/components/OnboardingTour.vue";
import AppShell from "../../src/components/AppShell.vue";
import { useOnboardingStore } from "../../src/stores/onboarding";
import { useAuthStore } from "../../src/stores/auth";
import {
  AVAILABLE_SCREENS,
  isScreenAvailable,
} from "../../src/router/availability";
import { hasRequiredRole } from "../../src/router";
import { signInAs } from "../helpers/signIn.js";
import { createRouter, createWebHistory } from "vue-router";

const DummyView = { template: "<div>Dummy</div>" };
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "landing", redirect: "/overview" },
    {
      path: "/overview",
      name: "overview",
      component: DummyView,
      meta: { requiresAuth: true },
    },
    {
      path: "/login",
      name: "login",
      component: DummyView,
      meta: { requiresAuth: false },
    },
    {
      path: "/forbidden",
      name: "forbidden",
      component: DummyView,
      meta: { requiresAuth: false },
    },
    {
      path: "/unavailable",
      name: "unavailable",
      component: DummyView,
      meta: { requiresAuth: false },
    },
    {
      path: "/studies/:studyCode?",
      name: "study",
      component: DummyView,
      meta: { requiresAuth: true, layout: "canvas" },
    },
    {
      path: "/ecrf",
      name: "ecrf",
      component: DummyView,
      meta: {
        requiresAuth: true,
        requiresRole: [
          "site_investigator",
          "crc",
          "data_manager",
          "sponsor_admin",
          "cra",
          "monitor",
        ],
      },
    },
    {
      path: "/ctms",
      name: "ctms",
      component: DummyView,
      meta: {
        requiresAuth: true,
        requiresRole: ["cra", "monitor", "sponsor_admin"],
      },
    },
    {
      path: "/ssu",
      name: "ssu",
      component: DummyView,
      meta: {
        requiresAuth: true,
        requiresRole: [
          "cra",
          "monitor",
          "sponsor_admin",
          "site_investigator",
          "crc",
        ],
      },
    },
    {
      path: "/mdr",
      name: "mdr",
      component: DummyView,
      meta: {
        requiresAuth: true,
        requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
      },
    },
    {
      path: "/audit",
      name: "audit",
      component: DummyView,
      meta: {
        requiresAuth: true,
        requiresRole: ["auditor", "tmf_auditor", "sponsor_admin"],
      },
    },
  ],
});

router.beforeEach((to) => {
  const authStore = useAuthStore();
  const requiredRoles = to.meta?.requiresRole;
  if (
    requiredRoles?.length > 0 &&
    authStore.rawRoles?.length > 0 &&
    !hasRequiredRole(authStore.normalizedRoles, requiredRoles)
  ) {
    return { path: "/forbidden" };
  }
  if (to.name && !isScreenAvailable(String(to.name))) {
    return { name: "unavailable", query: { screen: String(to.name) } };
  }
});

describe("Onboarding Guided Tour & Opt-in Live Sequences Spec", () => {
  beforeEach(async () => {
    vi.restoreAllMocks();
    const pinia = createPinia();
    setActivePinia(pinia);
    window.localStorage.clear();
    await router.push("/overview");
    await router.isReady();
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

    it("falls back to viewport center after retry attempts expire if target element never appears without locking #main-content inert", async () => {
      const mainContent = document.createElement("main");
      mainContent.id = "main-content";
      document.body.appendChild(mainContent);

      const mountNode = document.createElement("div");
      document.body.appendChild(mountNode);

      const store = useOnboardingStore();
      store.startTour();
      store.currentStep = 2;

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
        attachTo: mountNode,
      });

      await wrapper.vm.$nextTick();
      for (let i = 0; i < 7; i++) {
        await new Promise((resolve) => requestAnimationFrame(resolve));
      }

      const popoverCard = wrapper.find(".onboarding-popover-card");
      expect(popoverCard.element.style.top).toBe("50%");
      expect(popoverCard.element.style.left).toBe("50%");
      expect(popoverCard.attributes("aria-modal")).toBeUndefined();
      expect(wrapper.find(".onboarding-backdrop").exists()).toBe(false);
      expect(mainContent.hasAttribute("inert")).toBe(false);

      wrapper.unmount();
      document.body.removeChild(mountNode);
      document.body.removeChild(mainContent);
    });

    it("resolves compact #btn-mobile-menu-toggle when primary nav links are hidden and binds site_crc live/demo eCRF anchors", async () => {
      const hiddenPrimaryNav = document.createElement("nav");
      hiddenPrimaryNav.style.display = "none";
      const hiddenStudiesLink = document.createElement("a");
      hiddenStudiesLink.id = "nav-studies";
      hiddenStudiesLink.getBoundingClientRect = () => ({
        top: 0,
        bottom: 0,
        left: 0,
        right: 0,
        width: 0,
        height: 0,
      });
      hiddenPrimaryNav.appendChild(hiddenStudiesLink);
      document.body.appendChild(hiddenPrimaryNav);

      const mobileToggle = document.createElement("button");
      mobileToggle.id = "btn-mobile-menu-toggle";
      mobileToggle.getBoundingClientRect = () => ({
        top: 8,
        bottom: 52,
        left: 712,
        right: 756,
        width: 44,
        height: 44,
      });
      document.body.appendChild(mobileToggle);

      const originalInnerWidth = window.innerWidth;
      Object.defineProperty(window, "innerWidth", {
        configurable: true,
        value: 768,
      });

      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Sponsor Admin", "CRC"]);
      store.startTour();
      store.currentStep = 2; // super_admin step 2: #nav-studies, #btn-mobile-menu-toggle

      const mountNode = document.createElement("div");
      document.body.appendChild(mountNode);
      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
        attachTo: mountNode,
      });

      await flushPromises();
      await wrapper.vm.$nextTick();
      const popoverCard = wrapper.find(".onboarding-popover-card");
      // Clamped within 12px of 768px right edge: maxCenterX = 768 - 12 - 160 = 596px
      expect(popoverCard.element.style.top).toBe("64px");
      expect(popoverCard.element.style.left).toBe("596px");
      expect(popoverCard.attributes("aria-modal")).toBeUndefined();
      expect(wrapper.find(".popover-arrow").exists()).toBe(true);

      // Switch to site_crc and verify live (#ecrf-participant-bar, #ecrf-form-instance) and demo (.crc-form-renderer) anchors
      const participantBar = document.createElement("section");
      participantBar.id = "ecrf-participant-bar";
      participantBar.getBoundingClientRect = () => ({
        top: 120,
        bottom: 180,
        left: 24,
        right: 524,
        width: 500,
        height: 60,
      });
      document.body.appendChild(participantBar);

      const formInstance = document.createElement("section");
      formInstance.id = "ecrf-form-instance";
      formInstance.getBoundingClientRect = () => ({
        top: 200,
        bottom: 320,
        left: 24,
        right: 524,
        width: 500,
        height: 120,
      });
      document.body.appendChild(formInstance);

      authStore.switchPersona("site_crc");
      await flushPromises();
      store.startTour();
      await flushPromises();
      await wrapper.find(".btn-tour-next").trigger("click");
      await flushPromises();
      await new Promise((resolve) => requestAnimationFrame(resolve));

      const crcPopoverCard = wrapper.find(".onboarding-popover-card");
      expect(store.currentStepConfig.targetSelector).toBe(
        "#ecrf-participant-bar, .subject-enrollment-card"
      );
      expect(crcPopoverCard.element.style.top).toBe("192px");
      expect(crcPopoverCard.element.style.left).toBe("274px");

      await wrapper.find(".btn-tour-next").trigger("click");
      await flushPromises();
      await new Promise((resolve) => requestAnimationFrame(resolve));

      expect(store.currentStepConfig.targetSelector).toBe(
        ".crc-form-renderer, #ecrf-form-instance"
      );
      expect(crcPopoverCard.element.style.top).toBe("332px");
      expect(crcPopoverCard.element.style.left).toBe("274px");

      wrapper.unmount();
      Object.defineProperty(window, "innerWidth", {
        configurable: true,
        value: originalInnerWidth,
      });
      document.body.removeChild(mountNode);
      document.body.removeChild(formInstance);
      document.body.removeChild(participantBar);
      document.body.removeChild(mobileToggle);
      document.body.removeChild(hiddenPrimaryNav);
    });

    it("clamps coordinates within 12px of visualViewport, flips tight bottom cards, updates on scroll, and hides detached arrows", async () => {
      const originalVisualViewport = window.visualViewport;
      Object.defineProperty(window, "visualViewport", {
        configurable: true,
        value: {
          width: 360,
          height: 500,
          offsetLeft: 0,
          offsetTop: 0,
          addEventListener: vi.fn(),
          removeEventListener: vi.fn(),
        },
      });

      let anchorRect = {
        top: 420,
        bottom: 460,
        left: 8,
        right: 68,
        width: 60,
        height: 40,
      };
      const anchorEl = document.createElement("a");
      anchorEl.id = "nav-studies";
      anchorEl.getBoundingClientRect = () => anchorRect;
      document.body.appendChild(anchorEl);

      const store = useOnboardingStore();
      store.startTour();
      store.currentStep = 2;

      const mountNode = document.createElement("div");
      document.body.appendChild(mountNode);
      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
        attachTo: mountNode,
      });
      await flushPromises();
      await wrapper.vm.$nextTick();

      const popoverCard = wrapper.find(".onboarding-popover-card");
      // Left edge clamped to 12px -> center = 12 + 320 / 2 = 172px
      expect(popoverCard.element.style.left).toBe("172px");
      // Bottom space is tight (460 + 12 + 220 > 500 - 12), so card flips above anchor (420 - 12 - 220 = 188px)
      expect(popoverCard.element.style.top).toBe("188px");
      expect(wrapper.find(".popover-arrow--bottom").exists()).toBe(true);

      // Simulate scrolling the anchor completely off the top of the viewport
      anchorRect = {
        top: -120,
        bottom: -80,
        left: 8,
        right: 68,
        width: 60,
        height: 40,
      };
      window.dispatchEvent(new Event("scroll"));
      await wrapper.vm.$nextTick();

      // Top clamps to 12px margin and detached .popover-arrow hides
      expect(popoverCard.element.style.top).toBe("12px");
      expect(wrapper.find(".popover-arrow").exists()).toBe(false);

      wrapper.unmount();
      Object.defineProperty(window, "visualViewport", {
        configurable: true,
        value: originalVisualViewport,
      });
      document.body.removeChild(mountNode);
      document.body.removeChild(anchorEl);
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

    it("routes site_crc Step 4 to ssu instead of forbidden ctms", async () => {
      await router.push("/overview");
      await flushPromises();
      const store = useOnboardingStore();
      signInAs(useAuthStore(), ["CRC"]);
      store.loadPersonaState("site_crc");
      store.startTour();

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });
      await flushPromises();

      expect(store.personaSteps[3].routeName).toBe("ssu");

      // Step 1 -> 2 -> 3 -> 4
      await wrapper.find(".btn-tour-next").trigger("click");
      await flushPromises();
      await wrapper.find(".btn-tour-next").trigger("click");
      await flushPromises();
      await wrapper.find(".btn-tour-next").trigger("click");
      await flushPromises();

      expect(store.currentStep).toBe(4);
      expect(store.currentStepConfig.routeName).toBe("ssu");
      expect(router.currentRoute.value.path).toBe("/ssu");
      wrapper.unmount();
    });

    it("keeps the tour popover on /overview without opening study when AVAILABLE_SCREENS excludes every target screen", async () => {
      const saved = [...AVAILABLE_SCREENS];
      AVAILABLE_SCREENS.clear();
      try {
        await router.push("/overview");
        await flushPromises();
        const store = useOnboardingStore();
        const authStore = signInAs(useAuthStore(), ["Auditor"]);
        store.loadPersonaState("auditor");
        store.startTour();

        const pushSpy = vi.spyOn(router, "push");
        pushSpy.mockClear();
        const wrapper = mount(OnboardingTour, {
          global: { plugins: [router] },
        });
        await flushPromises();

        expect(
          store.personaSteps.every((s) => s.routeName === "overview")
        ).toBe(true);
        expect(router.currentRoute.value.path).toBe("/overview");
        expect(pushSpy).not.toHaveBeenCalled();
        wrapper.unmount();

        // Persona with zero native overview steps returns fallback steps on overview
        signInAs(authStore, ["Data Manager"]);
        store.loadPersonaState("data_manager");
        store.startTour();
        expect(store.personaSteps.length).toBeGreaterThan(0);
        expect(
          store.personaSteps.every((s) => s.routeName === "overview")
        ).toBe(true);
        pushSpy.mockRestore();
      } finally {
        saved.forEach((s) => AVAILABLE_SCREENS.add(s));
      }
    });

    it("clamps stored currentStep to totalSteps after a persona switch with fewer filtered steps", () => {
      window.localStorage.setItem("onboarding_cra_monitor_current_step", "5");
      const store = useOnboardingStore();
      const authStore = signInAs(useAuthStore(), ["Auditor"]);

      authStore.switchPersona("cra_monitor");
      store.loadPersonaState("cra_monitor");

      expect(store.totalSteps).toBe(1);
      expect(store.currentStep).toBe(1);
      expect(
        window.localStorage.getItem("onboarding_cra_monitor_current_step")
      ).toBe("1");
    });

    it("keeps the popover on /overview instead of calling router.push when syncStepRoute encounters an unavailable or non-shell route", async () => {
      await router.push("/overview");
      await flushPromises();

      const store = useOnboardingStore();
      signInAs(useAuthStore(), ["Sponsor Admin"]);
      store.startTour();

      vi.spyOn(store, "currentStepConfig", "get").mockReturnValue({
        step: 1,
        routeName: "mdr",
        targetSelector: null,
        title: "Unavailable Step",
        description: "Targets hidden MDR screen",
      });

      const pushSpy = vi.spyOn(router, "push");
      pushSpy.mockClear();

      const wrapper = mount(OnboardingTour, {
        global: { plugins: [router] },
      });
      await flushPromises();

      expect(wrapper.find(".onboarding-popover-card").exists()).toBe(true);
      expect(router.currentRoute.value.path).toBe("/overview");
      expect(pushSpy).not.toHaveBeenCalled();
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
