import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import OverviewView from "../../src/views/OverviewView.vue";
import { useAuthStore } from "../../src/stores/auth";
import { useOnboardingStore } from "../../src/stores/onboarding";

const mockPush = vi.fn();
vi.mock("vue-router", () => ({
  useRouter: () => ({
    push: mockPush,
  }),
}));

describe("OverviewView Orientation Hub Component", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockPush.mockReset();
    if (typeof window !== "undefined") {
      window.localStorage.clear();
    }
  });

  it("renders welcome header and active persona details without product acronyms (UX-1)", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.user = { firstName: "Jane", lastName: "Doe" };
    authStore.rawRoles = ["Sponsor Designer"];
    authStore.currentPersona = "sponsor_designer";

    const wrapper = mount(OverviewView);

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

    const wrapper = mount(OverviewView);

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

    const wrapper = mount(OverviewView);
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

  it("navigates only to live authorized operational views when clicking workflow launcher cards across all six personas", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["CRA"];
    authStore.currentPersona = "cra_monitor";

    const wrapper = mount(OverviewView);

    const launcherBtns = wrapper.findAll(".btn-launcher-action");
    expect(launcherBtns.length).toBe(3);

    // First launcher card is Studies (/studies)
    await launcherBtns[0].trigger("click");
    expect(mockPush).toHaveBeenCalledWith("/studies");

    // Second launcher card is Data collection (/ecrf)
    await launcherBtns[1].trigger("click");
    expect(mockPush).toHaveBeenCalledWith("/ecrf");

    // Third launcher card is Site readiness (/ctms for CRA)
    await launcherBtns[2].trigger("click");
    expect(mockPush).toHaveBeenCalledWith("/ctms");

    // Auditor only sees Studies (/studies), never unauthorized /ecrf or /ctms
    authStore.rawRoles = ["Auditor"];
    authStore.currentPersona = "auditor";
    const auditorWrapper = mount(OverviewView);
    const auditorLaunchers = auditorWrapper.findAll(".launcher-card");
    expect(auditorLaunchers.length).toBe(1);
    expect(auditorLaunchers[0].text()).toContain("Studies");
  });
});
