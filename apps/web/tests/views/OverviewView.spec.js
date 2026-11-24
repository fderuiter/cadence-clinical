import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import OverviewView from "../../src/views/OverviewView.vue";
import { router } from "../../src/router";
import { useAuthStore } from "../../src/stores/auth";
import {
  useOnboardingStore,
  getOnboardingStorageKey,
} from "../../src/stores/onboarding";
import { signInAs } from "../helpers/signIn.js";

describe("OverviewView Orientation Hub Component", () => {
  beforeEach(async () => {
    setActivePinia(createPinia());
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
      global: { plugins: [router] },
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
    authStore.user = { id: "crc-1", tenantId: "tenant-1" };
    authStore.rawRoles = ["CRC"];
    authStore.currentPersona = "site_crc";
    onboardingStore.loadPersonaState("site_crc");

    const wrapper = mount(OverviewView, {
      global: { plugins: [router] },
    });
    const toggle = wrapper.find("#toggle-bypass-overview");

    expect(toggle.exists()).toBe(true);
    expect(onboardingStore.bypassOverview).toBe(false);

    // Toggle preference ON
    await toggle.setValue(true);
    expect(onboardingStore.bypassOverview).toBe(true);

    // Verify account-scoped localStorage persistence
    const saved = window.localStorage.getItem(
      getOnboardingStorageKey("bypass_overview", "site_crc")
    );
    expect(saved).toBe("true");
  });

  it("navigates only to live authorized operational views when clicking workflow launcher cards and checklist links across all eight personas", async () => {
    const personaExpectations = [
      {
        roles: ["Sponsor Admin"],
        persona: "super_admin",
        expectedLaunchers: ["/studies", "/studies", "/ctms"],
        expectedChecklistRoutes: [
          "/studies",
          "/studies",
          "/studies",
          "/ctms",
          "/studies",
        ],
      },
      {
        roles: ["Sponsor Designer"],
        persona: "sponsor_designer",
        expectedLaunchers: ["/studies"],
        expectedChecklistRoutes: ["/studies", "/studies", "/studies"],
      },
      {
        roles: ["Data Manager"],
        persona: "data_manager",
        expectedLaunchers: ["/studies", "/studies", "/ctms"],
        expectedChecklistRoutes: ["/studies", "/studies", "/studies", "/ctms"],
      },
      {
        roles: ["CRC"],
        persona: "site_crc",
        expectedLaunchers: ["/studies", "/studies", "/ssu"],
        expectedChecklistRoutes: ["/ssu", "/studies", "/studies"],
      },
      {
        roles: ["Principal Investigator"],
        persona: "site_investigator",
        expectedLaunchers: ["/studies", "/studies", "/ssu"],
        expectedChecklistRoutes: ["/ssu", "/studies", "/studies", "/training"],
      },
      {
        roles: ["CRA"],
        persona: "cra_monitor",
        expectedLaunchers: ["/studies", "/studies", "/ctms"],
        expectedChecklistRoutes: ["/ctms", "/ssu", "/studies", "/etmf"],
      },
      {
        roles: ["Safety Reviewer"],
        persona: "safety_officer",
        expectedLaunchers: ["/studies"],
        expectedChecklistRoutes: ["/studies", "/studies", "/training", "/etmf"],
      },
      {
        roles: ["Auditor"],
        persona: "auditor",
        expectedLaunchers: ["/studies"],
        expectedChecklistRoutes: ["/studies", "/studies", "/etmf", "/studies"],
      },
    ];

    for (const {
      roles,
      persona,
      expectedLaunchers,
      expectedChecklistRoutes,
    } of personaExpectations) {
      const authStore = signInAs(useAuthStore(), roles);
      authStore.isDemoMode = true;
      authStore.currentPersona = persona;
      useOnboardingStore().loadPersonaState(persona);
      await router.push("/overview");

      const pushSpy = vi.spyOn(router, "push");
      const wrapper = mount(OverviewView, {
        global: { plugins: [router] },
      });

      const launcherBtns = wrapper.findAll(".btn-launcher-action");
      expect(launcherBtns.length, `${persona} launchers`).toBe(
        expectedLaunchers.length
      );

      for (let i = 0; i < launcherBtns.length; i++) {
        await router.push("/overview");
        pushSpy.mockClear();
        await launcherBtns[i].trigger("click");
        expect(pushSpy, `${persona} launcher ${i}`).toHaveBeenCalledWith(
          expectedLaunchers[i]
        );
        await pushSpy.mock.results[0].value;
        expect(router.currentRoute.value.path).toBe(expectedLaunchers[i]);
      }

      const taskLaunchBtns = wrapper.findAll(".btn-task-launch");
      expect(taskLaunchBtns.length, `${persona} checklist links`).toBe(
        expectedChecklistRoutes.length
      );

      for (let i = 0; i < taskLaunchBtns.length; i++) {
        await router.push("/overview");
        pushSpy.mockClear();
        await taskLaunchBtns[i].trigger("click");
        expect(pushSpy, `${persona} checklist ${i}`).toHaveBeenCalledWith(
          expectedChecklistRoutes[i]
        );
        await pushSpy.mock.results[0].value;
        expect(router.currentRoute.value.path).toBe(expectedChecklistRoutes[i]);
      }

      // Guard blocks direct unauthorized/unavailable route invocation
      await router.push("/overview");
      pushSpy.mockClear();
      wrapper.vm.$.setupState.navigateToRoute("/mdr");
      if (persona === "auditor") {
        wrapper.vm.$.setupState.navigateToRoute("/ctms");
      }
      expect(pushSpy).not.toHaveBeenCalled();

      pushSpy.mockRestore();
      wrapper.unmount();
    }

    // Static demo builds preserve standalone /ecrf launchers and checklist routes
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
      await wrapper.findAll(".btn-launcher-action")[1].trigger("click");
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
