import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import CtmsView from "../src/views/CtmsView.vue";
import MdrView from "../src/views/MdrView.vue";
import ClinicalSoAMatrix from "../src/components/clinical/ClinicalSoAMatrix.vue";
import { apiClient } from "../src/api/apiClient";

describe("CtmsView.vue native list rendering migration", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);

    // Default mock implementations for apiClient.get using spyOn to prevent global registry pollution
    vi.spyOn(apiClient, "get").mockImplementation((url) => {
      if (url.includes("/site-milestones")) {
        return Promise.resolve([
          {
            id: "1",
            milestone_type: "SITE_SELECTION",
            planned_date: "2026-08-01T00:00:00",
            actual_date: "2026-08-05T00:00:00",
            status: "ACHIEVED",
          },
        ]);
      }
      if (url.includes("/monitoring-visits")) {
        return Promise.resolve([
          {
            id: "1",
            visit_type: "SIV",
            scheduled_date: "2026-08-01T00:00:00",
            actual_date: "2026-08-02T00:00:00",
            cra_id: "cra_fderuiter",
            status: "SIGNED_OFF",
          },
        ]);
      }
      if (url.includes("/doa/sites/")) {
        return Promise.resolve({
          delegated_staff: [],
          audit_history: [],
          pi_name: "Dr. Arthur Pendragon",
        });
      }
      return Promise.resolve([]);
    });

    vi.spyOn(apiClient, "post").mockResolvedValue({});
    vi.spyOn(apiClient, "put").mockResolvedValue({});
    vi.spyOn(apiClient, "delete").mockResolvedValue({});
  });

  it("renders milestone and visits tables with correct headers and classes", async () => {
    const wrapper = mount(CtmsView);

    // Wait for the async loads to resolve
    let retries = 5;
    while (apiClient.get.mock.calls.length === 0 && retries > 0) {
      await new Promise((resolve) => setTimeout(resolve, 50));
      retries--;
    }
    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 100)); // extra wait for reactivity
    await wrapper.vm.$nextTick();

    // Assert milestones container and table structure
    const milestonesContainer = wrapper.find("#ctms-milestones-container");
    expect(milestonesContainer.exists()).toBe(true);

    const milestoneHeaders = milestonesContainer
      .findAll("th")
      .map((el) => el.text());
    expect(milestoneHeaders).toEqual([
      "Milestone Type",
      "Planned Date",
      "Actual Date",
      "Status",
    ]);

    // Assert dynamic milestone elements and gxp class application
    const milestoneBadges = milestonesContainer.findAll(".badge");
    expect(milestoneBadges.length).toBeGreaterThan(0);
    // Find milestone type site selection which is achieved in initial CTMS mock data
    expect(wrapper.text()).toContain("SITE_SELECTION");
    const achievedBadge = milestoneBadges.find(
      (el) => el.text() === "ACHIEVED"
    );
    expect(achievedBadge.classes()).toContain("gxp");

    // Assert visits container and table structure
    const visitsContainer = wrapper.find("#ctms-visits-container");
    expect(visitsContainer.exists()).toBe(true);

    const visitHeaders = visitsContainer.findAll("th").map((el) => el.text());
    expect(visitHeaders).toEqual([
      "Visit Type",
      "Scheduled Date",
      "Actual Date",
      "CRA Assigned",
      "Status",
      "Actions",
    ]);

    // Assert dynamic visit elements and gxp class application
    expect(wrapper.text()).toContain("SIV");
    const visitBadges = visitsContainer.findAll(".badge");
    const signedOffBadge = visitBadges.find((el) => el.text() === "SIGNED_OFF");
    expect(signedOffBadge.classes()).toContain("gxp");
  });
});

describe("MdrView.vue ClinicalSoAMatrix migration integration", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
  });

  it("renders ClinicalSoAMatrix correctly inside MdrView wrapper with parsed soaData", () => {
    const wrapper = mount(MdrView);

    // Assert ClinicalSoAMatrix component is loaded and mounted
    const soaMatrix = wrapper.findComponent(ClinicalSoAMatrix);
    expect(soaMatrix.exists()).toBe(true);

    // Verify correct elements rendered inside the SoA matrix
    expect(soaMatrix.text()).toContain("Informed Consent & Demographics");
    expect(soaMatrix.text()).toContain("Vital Signs (BP & Pulse)");
  });
});
