import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import CtmsView from "../src/views/CtmsView.vue";
import MdrView from "../src/views/MdrView.vue";
import ClinicalSoAMatrix from "../src/components/clinical/ClinicalSoAMatrix.vue";
import { apiClient } from "../src/api/apiClient";
import { ctmsApi } from "../src/api/ctmsApi";
import { executionApi } from "../src/api/executionApi";

describe("CtmsView.vue native list rendering migration", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  beforeEach(() => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const pinia = createPinia();
    setActivePinia(pinia);

    vi.spyOn(executionApi, "getSiteComplianceStatus").mockResolvedValue({
      study_id: "STUDY-USDM-001",
      site_id: "SITE-001",
      milestone: "SITE_ACTIVATION",
      is_complete: true,
      missing_documents: [],
    });
    vi.spyOn(ctmsApi, "listSiteMilestones").mockResolvedValue([
      {
        id: "1",
        site_id: "SITE-001",
        study_id: "STUDY-USDM-001",
        milestone_type: "SITE_SELECTION",
        planned_date: "2026-08-01T00:00:00",
        actual_date: "2026-08-05T00:00:00",
        status: "ACHIEVED",
        created_at: "2026-08-01T00:00:00Z",
        created_by: "cra_fderuiter",
        reason_for_change: "Initial milestone",
        version_index: 1,
      },
    ]);
    vi.spyOn(ctmsApi, "listMonitoringVisits").mockResolvedValue([
      {
        id: "1",
        site_id: "SITE-001",
        study_id: "STUDY-USDM-001",
        visit_type: "SIV",
        scheduled_date: "2026-08-01T00:00:00",
        actual_date: "2026-08-02T00:00:00",
        cra_id: "cra_fderuiter",
        status: "SIGNED_OFF",
        findings: [],
        created_at: "2026-08-01T00:00:00Z",
        created_by: "cra_fderuiter",
        reason_for_change: "Initial visit",
        version_index: 1,
      },
    ]);
    vi.spyOn(ctmsApi, "getSiteDoaLog").mockResolvedValue({
      site_id: "SITE-001",
      delegated_staff: [],
      audit_history: [],
      pi_name: "Dr. Arthur Pendragon",
    });

    vi.spyOn(apiClient, "get").mockResolvedValue([]);
    vi.spyOn(apiClient, "post").mockResolvedValue({});
    vi.spyOn(apiClient, "put").mockResolvedValue({});
    vi.spyOn(apiClient, "delete").mockResolvedValue({});
  });

  it("renders milestone and visits tables with correct headers and classes", async () => {
    const wrapper = mount(CtmsView);
    await flushPromises();
    await wrapper.vm.$nextTick();

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

    const milestoneBadges = milestonesContainer.findAll(".badge");
    expect(milestoneBadges.length).toBeGreaterThan(0);
    expect(wrapper.text()).toContain("SITE_SELECTION");
    const achievedBadge = milestoneBadges.find(
      (el) => el.text() === "ACHIEVED"
    );
    expect(achievedBadge.classes()).toContain("gxp");

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

    const soaMatrix = wrapper.findComponent(ClinicalSoAMatrix);
    expect(soaMatrix.exists()).toBe(true);

    expect(soaMatrix.text()).toContain("Informed Consent & Demographics");
    expect(soaMatrix.text()).toContain("Vital Signs (BP & Pulse)");
  });
});
