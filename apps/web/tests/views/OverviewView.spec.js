import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import OverviewView from "../../src/views/OverviewView.vue";
import { router } from "../../src/router";
import { useAuthStore } from "../../src/stores/auth";
import { useOnboardingStore } from "../../src/stores/onboarding";
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

    // Verify localStorage persistence
    const saved = window.localStorage.getItem(
      "onboarding_site_crc_bypass_overview"
    );
    expect(saved).toBe("true");
  });

  it("navigates only to live authorized operational views when clicking workflow launcher cards and checklist links across all six personas", async () => {
    const personaExpectations = [
      {
        roles: ["Sponsor Admin"],
        persona: "super_admin",
        expectedLaunchers: ["/studies", "/ecrf", "/ctms"],
        expectedChecklistRoutes: ["/studies", "/ctms"],
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
        expectedLaunchers: ["/studies", "/ecrf"],
        expectedChecklistRoutes: ["/ecrf", "/studies", "/ecrf"],
      },
      {
        roles: ["CRC"],
        persona: "site_crc",
        expectedLaunchers: ["/studies", "/ecrf", "/ssu"],
        expectedChecklistRoutes: ["/ssu", "/ecrf", "/studies"],
      },
      {
        roles: ["CRA"],
        persona: "cra_monitor",
        expectedLaunchers: ["/studies", "/ecrf", "/ctms"],
        expectedChecklistRoutes: ["/ctms", "/ecrf", "/studies"],
      },
      {
        roles: ["Auditor"],
        persona: "auditor",
        expectedLaunchers: ["/studies"],
        expectedChecklistRoutes: ["/studies", "/studies", "/studies"],
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
  });
});
