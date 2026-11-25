import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import OverviewView from "../../src/views/OverviewView.vue";
import { router } from "../../src/router";
import { useAuthStore } from "../../src/stores/auth";
import { useOnboardingStore } from "../../src/stores/onboarding";
import { signInAs } from "../helpers/signIn.js";

describe("OverviewView Orientation Hub Component", () => {
  let pinia;

  beforeEach(async () => {
    pinia = createPinia();
    setActivePinia(pinia);
    if (typeof window !== "undefined") {
      window.localStorage.clear();
    }
    await router.push("/login");
  });

  it("renders welcome header and active persona details without product acronyms (UX-1)", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.user = { firstName: "Jane", lastName: "Doe" };
    authStore.rawRoles = ["Sponsor Designer"];
    authStore.currentPersona = "sponsor_designer";

    const wrapper = mount(OverviewView, {
      global: { plugins: [router] },
    });

    expect(wrapper.text()).toContain("Welcome, Jane Doe!");
    expect(wrapper.text()).toContain("Sponsor Protocol & Design Workspace");
    expect(wrapper.text()).toContain("inspect recorded protocol versions");
    expect(wrapper.text()).not.toContain("EDC");
    expect(wrapper.text()).not.toContain("CTMS");
    expect(wrapper.text()).not.toContain("eTMF");
  });

  it("renders role-tailored quick-start checklist items managed by onboarding store", async () => {
    const authStore = useAuthStore();
    const onboardingStore = useOnboardingStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Designer"];
    authStore.currentPersona = "sponsor_designer";

    const wrapper = mount(OverviewView, {
      global: { plugins: [pinia, router] },
    });

    // Verify task items exist
    const checkboxes = wrapper.findAll(".task-checkbox");
    expect(checkboxes.length).toBeGreaterThan(0);

    // Toggle a task checkbox
    await checkboxes[0].setValue(true);
    expect(onboardingStore.completedTasks.length).toBe(1);

    // Progress percentage should update
    expect(wrapper.find(".progress-badge").text()).toContain("1 of");
  });

  it("allows setting and persisting the preference to bypass overview hub", async () => {
    const authStore = useAuthStore();
    const onboardingStore = useOnboardingStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["CRC"];
    authStore.currentPersona = "site_crc";
    onboardingStore.loadPersonaState("site_crc");

    const wrapper = mount(OverviewView, {
      global: { plugins: [pinia, router] },
    });
    const toggle = wrapper.find("#toggle-bypass-overview");

    expect(toggle.exists()).toBe(true);
    expect(onboardingStore.bypassOverview).toBe(false);

    // Toggle preference ON
    await toggle.setValue(true);
    expect(onboardingStore.bypassOverview).toBe(true);

    // Verify localStorage persistence
    const saved = window.localStorage.getItem(
      "onboarding_site_crc_bypass_overview"
    );
    expect(saved).toBe("true");
  });

  it("retires commercial Blueprint, Capture, and Orbit launcher cards while routing checklist links to authorized Canvas and demo views across all six personas", async () => {
    const personaExpectations = [
      {
        roles: ["Sponsor Admin"],
        persona: "super_admin",
        expectedChecklistRoutes: [
          "/studies",
          "/studies?stage=start&section=startup-greenlight",
        ],
      },
      {
        roles: ["Sponsor Designer"],
        persona: "sponsor_designer",
        expectedChecklistRoutes: ["/studies", "/studies", "/studies"],
      },
      {
        roles: ["Data Manager"],
        persona: "data_manager",
        expectedChecklistRoutes: [
          "/studies",
          "/studies",
          "/studies",
          "/studies?stage=start&section=startup-greenlight",
        ],
      },
      {
        roles: ["CRC"],
        persona: "site_crc",
        expectedChecklistRoutes: [
          "/studies?stage=start&section=site-activation",
          "/studies",
          "/studies",
        ],
      },
      {
        roles: ["CRA"],
        persona: "cra_monitor",
        expectedChecklistRoutes: [
          "/studies?stage=start&section=startup-greenlight",
          "/studies",
          "/studies",
        ],
      },
      {
        roles: ["Auditor"],
        persona: "auditor",
        expectedChecklistRoutes: ["/studies", "/studies", "/studies"],
      },
    ];

    for (const {
      roles,
      persona,
      expectedChecklistRoutes,
    } of personaExpectations) {
      const authStore = signInAs(useAuthStore(), roles);
      authStore.isDemoMode = true;
      authStore.currentPersona = persona;
      await router.push("/login");

      const pushSpy = vi.spyOn(router, "push");
      const wrapper = mount(OverviewView, {
        global: { plugins: [router] },
      });

      expect(wrapper.find(".launchers-section").exists()).toBe(false);
      expect(wrapper.findAll(".launcher-card")).toHaveLength(0);
      const gridText = wrapper.find(".overview-grid").text();
      expect(gridText).not.toContain("Blueprint");
      expect(gridText).not.toContain("Capture");
      expect(gridText).not.toContain("Orbit");

      const taskLaunchBtns = wrapper.findAll(".btn-task-launch");
      expect(taskLaunchBtns.length, `${persona} checklist links`).toBe(
        expectedChecklistRoutes.length
      );

      for (let i = 0; i < taskLaunchBtns.length; i++) {
        await router.push("/login");
        pushSpy.mockClear();
        await taskLaunchBtns[i].trigger("click");
        expect(pushSpy, `${persona} checklist ${i}`).toHaveBeenCalledWith(
          expectedChecklistRoutes[i]
        );
        await pushSpy.mock.results[0].value;
        expect(router.currentRoute.value.fullPath).toBe(
          expectedChecklistRoutes[i]
        );
      }

      // Guard blocks direct unauthorized/unavailable route invocation
      await router.push("/login");
      pushSpy.mockClear();
      wrapper.vm.$.setupState.navigateToRoute("/mdr");
      if (persona === "auditor") {
        wrapper.vm.$.setupState.navigateToRoute("/ctms");
      }
      expect(pushSpy).not.toHaveBeenCalled();

      pushSpy.mockRestore();
      wrapper.unmount();
    }

    // Static demo builds preserve standalone /overview, /ssu, and /ecrf checklist routes without launcher cards
    vi.stubEnv("VITE_DEMO_MODE", "true");
    try {
      const authStore = signInAs(useAuthStore(), ["CRC"]);
      authStore.isDemoMode = true;
      authStore.currentPersona = "site_crc";
      await router.push("/overview");

      const pushSpy = vi.spyOn(router, "push");
      const wrapper = mount(OverviewView, {
        global: { plugins: [router] },
      });
      expect(wrapper.find(".launchers-section").exists()).toBe(false);
      expect(wrapper.find("#overview-demo-catalog").exists()).toBe(true);
      const demoChecklistBtns = wrapper.findAll(".btn-task-launch");
      await demoChecklistBtns[0].trigger("click");
      expect(pushSpy).toHaveBeenCalledWith("/ssu");
      await pushSpy.mock.results[0].value;
      expect(router.currentRoute.value.path).toBe("/ssu");

      await router.push("/overview");
      pushSpy.mockClear();
      await demoChecklistBtns[1].trigger("click");
      expect(pushSpy).toHaveBeenCalledWith("/ecrf");
      await pushSpy.mock.results[0].value;
      expect(router.currentRoute.value.path).toBe("/ecrf");
      pushSpy.mockRestore();
      wrapper.unmount();
    } finally {
      vi.unstubAllEnvs();
    }
  });

  it("renders role-authorized #overview-demo-catalog family cards in demo builds without banned UX-1 acronyms", async () => {
    const stdAuth = signInAs(useAuthStore(), ["Sponsor Admin"]);
    stdAuth.currentPersona = "super_admin";
    const stdWrapper = mount(OverviewView, {
      global: { plugins: [router] },
    });
    expect(stdWrapper.find("#overview-demo-catalog").exists()).toBe(false);
    stdWrapper.unmount();

    vi.stubEnv("VITE_DEMO_MODE", "true");
    try {
      const authStore = useAuthStore();
      authStore.startDemoSession();
      authStore.switchPersona("sponsor_designer");
      await router.push("/overview");

      const wrapper = mount(OverviewView, {
        global: { plugins: [router] },
      });

      const catalog = wrapper.find("#overview-demo-catalog");
      expect(catalog.exists()).toBe(true);
      const designerText = catalog.text();
      expect(designerText).toContain("Blueprint");
      expect(designerText).toContain("Protocol & schedule designer");
      expect(designerText).not.toContain("EDC");
      expect(designerText).not.toContain("CTMS");
      expect(designerText).not.toContain("eTMF");
      expect(designerText).not.toContain("RTSM");
      expect(designerText).not.toContain("P21");
      expect(designerText).not.toContain("QMS");
      expect(designerText).not.toContain("Safety system");

      const mdrBtn = wrapper.find("#demo-launch-mdr");
      expect(mdrBtn.exists()).toBe(true);
      const pushSpy = vi.spyOn(router, "push");
      await mdrBtn.trigger("click");
      expect(pushSpy).toHaveBeenCalledWith("/mdr");
      await pushSpy.mock.results[0].value;
      expect(router.currentRoute.value.path).toBe("/mdr");
      pushSpy.mockRestore();

      await router.push("/overview");
      authStore.switchPersona("safety_officer");
      await wrapper.vm.$nextTick();
      expect(wrapper.find("#demo-launch-safety").exists()).toBe(true);
      expect(wrapper.find("#demo-launch-mdr").exists()).toBe(false);

      wrapper.unmount();
    } finally {
      vi.unstubAllEnvs();
    }
  });
});
