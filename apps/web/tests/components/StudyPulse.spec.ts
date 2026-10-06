import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import StudyPulse from "../../src/components/canvas/StudyPulse.vue";
import { useStudyCanvasStore } from "../../src/stores/studyCanvas";

describe("StudyPulse.vue", () => {
  let store: ReturnType<typeof useStudyCanvasStore>;

  beforeEach(() => {
    setActivePinia(createPinia());
    store = useStudyCanvasStore();
    store.studyCode = "STUDY-ONC-2026";
    store.pulse = {
      total: 42,
      byState: [
        { code: "SCREENING", label: "Screening", count: 10 },
        { code: "ENROLLED", label: "Enrolled", count: 25 },
        { code: "COMPLETED", label: "Completed", count: 7 },
      ],
    };
  });

  it("renders participants tab by default with pulse metrics", () => {
    const wrapper = mount(StudyPulse);
    expect(wrapper.find("#tab-participants").attributes("aria-selected")).toBe("true");
    expect(wrapper.find("#pulse-total").text()).toContain("42");
    expect(wrapper.findAll(".pulse-states li").length).toBe(3);
  });

  it("switches to predictive velocity tab and loads velocity data", async () => {
    const loadVelocitySpy = vi
      .spyOn(store, "loadEnrollmentVelocity")
      .mockImplementation(async () => {
        store.enrollmentVelocity = {
          studyId: "STUDY-ONC-2026",
          status: "ON_TRACK",
          currentVelocityWeekly: 4.5,
          targetVelocityWeekly: 4.0,
          forecastedCompletionDate: "2027-04-15",
          daysAheadOrBehind: 12,
          targetSubjects: 100,
          currentlyEnrolled: 42,
          parameters: {
            targetSubjects: 100,
            targetCompletionDate: "2027-04-15",
            historicalWeeklyRate: 4.5,
            currentlyEnrolled: 42,
            activeSiteCount: 5,
            simulationHorizonWeeks: 24,
          },
          weeklyProjections: [
            {
              weekIndex: 1,
              weekStartDate: "2026-10-12",
              projectedCumulativeEnrolled: 46,
              lowerBoundCI: 44,
              upperBoundCI: 48,
              targetCumulative: 45,
              weeklyRate: 4.5,
            },
          ],
          siteVelocities: [
            {
              siteId: "SITE-101",
              siteName: "Boston Medical Center",
              activeSubjects: 15,
              historicalWeeklyRate: 2.0,
              projectedWeeklyRate: 2.2,
              velocityRiskLevel: "LOW",
            },
          ],
        };
        return true;
      });

    const wrapper = mount(StudyPulse);
    const velocityTab = wrapper.find("#tab-velocity");
    await velocityTab.trigger("click");

    expect(loadVelocitySpy).toHaveBeenCalledTimes(1);
    expect(wrapper.find("#panel-velocity").exists()).toBe(true);
    expect(wrapper.text()).toContain("ON_TRACK");
    expect(wrapper.text()).toContain("Weekly Enrollment Trajectory Projections");
    expect(wrapper.text()).toContain("Boston Medical Center");
  });

  it("recalculates velocity simulation when simulation form is submitted", async () => {
    store.enrollmentVelocity = {
      studyId: "STUDY-ONC-2026",
      status: "ON_TRACK",
      currentVelocityWeekly: 4.5,
      targetVelocityWeekly: 4.0,
      forecastedCompletionDate: "2027-04-15",
      daysAheadOrBehind: 12,
      targetSubjects: 100,
      currentlyEnrolled: 42,
      parameters: {
        targetSubjects: 100,
        targetCompletionDate: "2027-04-15",
        historicalWeeklyRate: 4.5,
        currentlyEnrolled: 42,
        activeSiteCount: 5,
        simulationHorizonWeeks: 24,
      },
      weeklyProjections: [],
      siteVelocities: [],
    };

    const simulateSpy = vi
      .spyOn(store, "simulateEnrollmentVelocity")
      .mockResolvedValue(true);

    const wrapper = mount(StudyPulse);
    await wrapper.find("#tab-velocity").trigger("click");

    const simBtn = wrapper.find("#btn-recalculate-sim");
    expect(simBtn.exists()).toBe(true);

    await wrapper.find("form.simulator-form").trigger("submit.prevent");
    expect(simulateSpy).toHaveBeenCalledTimes(1);
  });

  it("switches to site risk heatmap tab and loads matrix grid", async () => {
    const loadHeatmapSpy = vi
      .spyOn(store, "loadSiteRiskHeatmap")
      .mockImplementation(async () => {
        store.siteRiskHeatmap = {
          studyId: "STUDY-ONC-2026",
          evaluatedAtUtc: "2026-10-06T12:00:00Z",
          totalSites: 3,
          highRiskSitesCount: 1,
          mediumRiskSitesCount: 1,
          lowRiskSitesCount: 1,
          categories: ["Safety", "Consent", "DataQuality", "Enrollment", "Supply"],
          rows: [
            {
              siteId: "SITE-101",
              siteName: "Metro Hospital",
              compositeRiskScore: 0.82,
              riskTier: "HIGH",
              rank: 1,
              categoryScores: [
                {
                  categoryKey: "Safety",
                  categoryName: "Safety",
                  riskScore: 0.85,
                  riskTier: "HIGH",
                  triggeredMetricsCount: 2,
                  details: "2 open SAE queries pending review",
                },
              ],
              activeQueriesCount: 5,
              activeDeviationsCount: 1,
              recommendedActions: ["Schedule urgent CRA monitoring visit"],
            },
            {
              siteId: "SITE-102",
              siteName: "City Clinic",
              compositeRiskScore: 0.25,
              riskTier: "LOW",
              rank: 2,
              categoryScores: [
                {
                  categoryKey: "Safety",
                  categoryName: "Safety",
                  riskScore: 0.20,
                  riskTier: "LOW",
                  triggeredMetricsCount: 0,
                  details: "No active safety alerts",
                },
              ],
              activeQueriesCount: 0,
              activeDeviationsCount: 0,
              recommendedActions: ["Maintain routine monitoring schedule"],
            },
          ],
        };
        return true;
      });

    const wrapper = mount(StudyPulse);
    await wrapper.find("#tab-heatmap").trigger("click");

    expect(loadHeatmapSpy).toHaveBeenCalledTimes(1);
    expect(wrapper.find("#panel-heatmap").exists()).toBe(true);
    expect(wrapper.find("#heatmap-matrix-grid").exists()).toBe(true);
    expect(wrapper.findAll(".heatmap-row").length).toBe(2);

    // Test risk tier filter pill
    await wrapper.find("#pill-tier-high").trigger("click");
    expect(wrapper.findAll(".heatmap-row").length).toBe(1);
    expect(wrapper.text()).toContain("Metro Hospital");

    // Test resetting filter pill to ALL
    await wrapper.find("#pill-tier-all").trigger("click");
    expect(wrapper.findAll(".heatmap-row").length).toBe(2);

    // Test compute heatmap button
    const computeSpy = vi.spyOn(store, "computeSiteRiskHeatmap").mockResolvedValue(true);
    await wrapper.find("#btn-compute-heatmap").trigger("click");
    expect(computeSpy).toHaveBeenCalledTimes(1);
  });

  it("opens and closes site drilldown modal when a heatmap row is clicked", async () => {
    store.siteRiskHeatmap = {
      studyId: "STUDY-ONC-2026",
      evaluatedAtUtc: "2026-10-06T12:00:00Z",
      totalSites: 1,
      highRiskSitesCount: 1,
      mediumRiskSitesCount: 0,
      lowRiskSitesCount: 0,
      categories: ["Safety"],
      rows: [
        {
          siteId: "SITE-101",
          siteName: "Metro Hospital",
          compositeRiskScore: 0.82,
          riskTier: "HIGH",
          rank: 1,
          categoryScores: [
            {
              categoryKey: "Safety",
              categoryName: "Safety",
              riskScore: 0.85,
              riskTier: "HIGH",
              triggeredMetricsCount: 2,
              details: "2 open SAE queries",
            },
          ],
          activeQueriesCount: 5,
          activeDeviationsCount: 1,
          recommendedActions: ["Schedule urgent CRA monitoring visit"],
        },
      ],
    };

    const wrapper = mount(StudyPulse);
    await wrapper.find("#tab-heatmap").trigger("click");

    expect(wrapper.find("#site-drilldown-modal").exists()).toBe(false);

    // Click heatmap row
    await wrapper.find(".heatmap-row").trigger("click");
    expect(wrapper.find("#site-drilldown-modal").exists()).toBe(true);
    expect(wrapper.text()).toContain("Recommended CRA Action Items");
    expect(wrapper.text()).toContain("Schedule urgent CRA monitoring visit");

    // Close modal
    await wrapper.find("#btn-close-modal").trigger("click");
    expect(wrapper.find("#site-drilldown-modal").exists()).toBe(false);
  });
});
