import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { useAuthStore } from "../../src/stores/auth";
import { useToastStore } from "../../src/stores/toasts.js";
import { isScreenAvailable } from "../../src/router/availability";
import { router as appRouter } from "../../src/router";
import App from "../../src/App.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    listProposals: vi.fn(async () => []),
    search: vi.fn(async () => []),
    getChildren: vi.fn(async () => []),
    listProtocolVersions: vi.fn(async () => []),
    getScheduleOfActivities: vi.fn(),
    getCommands: vi.fn(),
    getObservation: vi.fn(),
    getZoomPath: vi.fn(async () => []),
  },
}));

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfFormsApi: {
      listForEnrollment: vi.fn(async () => []),
      openForm: vi.fn(),
      getForm: vi.fn(),
      saveValues: vi.fn(),
      signForm: vi.fn(),
    },
  };
});

vi.mock("../../src/api/ecrfQueriesApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfQueriesApi: {
      raise: vi.fn(),
      listForForm: vi.fn(async () => []),
      listForEnrollment: vi.fn(async () => []),
      transition: vi.fn(),
    },
  };
});

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

const IDS = [
  "0192f3c4-5e6a-7b8c-9d0e-000000000001",
  "0192f3c4-5e6a-7b8c-9d0e-000000000002",
  "0192f3c4-5e6a-7b8c-9d0e-000000000003",
];

function participant(index, overrides = {}) {
  return {
    id: IDS[index],
    subject_id: `017-000${index + 1}`,
    study_id: "CAD-101",
    site_id: "017",
    status: "ACTIVE",
    active_protocol_version: "3.0",
    enrollment_index: index + 1,
    ...overrides,
  };
}

const PARTICIPANTS = [
  participant(0),
  participant(1, { status: "SCREENING", site_id: "022" }),
  participant(2, { status: "RECONSENT_REQUIRED" }),
];

// Answers the list endpoint like Execution does: filtered by state and site, paged by limit and offset.
function serveParticipants(all = PARTICIPANTS) {
  vi.mocked(executionApi.listSubjects).mockImplementation(
    async ({ status, siteId, limit = 50, offset = 0 }) => {
      const matching = all.filter(
        (p) =>
          (!status || p.status === status) && (!siteId || p.site_id === siteId)
      );
      return {
        items: matching.slice(offset, offset + limit),
        total: matching.length,
        limit,
        offset,
      };
    }
  );
}

function listCalls() {
  return vi.mocked(executionApi.listSubjects).mock.calls.map(([q]) => q);
}

describe("Clinical Canvas (StudyCanvasView)", () => {
  let pinia;
  let router;
  let wrapper;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
          meta: { layout: "canvas" },
        },
        { path: "/ecrf", name: "ecrf", component: { template: "<div />" } },
      ],
    });
    vi.mocked(executionApi.listSubjects).mockReset();
    vi.mocked(executionApi.getSiteComplianceStatus).mockReset();
    serveParticipants();
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  async function open(url) {
    await router.push(url);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
    return wrapper;
  }

  it("wraps the study canvas and workflow views in one unified shell without a product sidebar (UX-1)", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    router.addRoute({
      path: "/ecrf-shell",
      name: "shell",
      component: { template: "<p id='workflow-screen'>screen</p>" },
    });
    await router.push("/studies/CAD-101");
    await router.isReady();
    wrapper = mount(App, { global: { plugins: [pinia, router] } });
    await flushPromises();

    expect(wrapper.find("#study-canvas").exists()).toBe(true);
    expect(wrapper.find(".app-sidebar").exists()).toBe(false);
    expect(wrapper.find("#nav-studies").attributes("href")).toBe(
      "/studies/CAD-101"
    );
    expect(wrapper.find("#btn-open-palette").exists()).toBe(true);
    expect(wrapper.find("#persona-select").exists()).toBe(true);
    expect(wrapper.find("#btn-onboarding-checklist-trigger").exists()).toBe(
      true
    );
    expect(wrapper.find("#btn-contextual-help-trigger").exists()).toBe(true);

    await router.push("/ecrf-shell");
    await flushPromises();
    expect(wrapper.find("#workflow-screen").exists()).toBe(true);
    expect(wrapper.find(".app-sidebar").exists()).toBe(false);
    expect(wrapper.find("#nav-studies").attributes("href")).toBe(
      "/studies/CAD-101"
    );
    expect(wrapper.find("#btn-open-palette").exists()).toBe(true);
    expect(wrapper.find("#persona-select").exists()).toBe(true);
  });

  it("is an available screen", () => {
    expect(isScreenAvailable("study")).toBe(true);
    const resolved = appRouter.resolve("/studies/CAD-101");
    expect(resolved.name).toBe("study");
    expect(resolved.params.studyCode).toBe("CAD-101");
    expect(resolved.meta).toMatchObject({
      layout: "canvas",
      requiresAuth: true,
    });
  });

  it("asks for a study code when no study is open, and opens the one typed", async () => {
    await open("/studies");

    expect(wrapper.find("#canvas-study-title").text()).toBe("Open a study");
    expect(wrapper.find(".lifecycle-rail").exists()).toBe(false);
    expect(executionApi.listSubjects).not.toHaveBeenCalled();

    await wrapper.find("#open-study-code").setValue("  CAD-101 ");
    await wrapper.find("#open-study-form").trigger("submit");
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe("/studies/CAD-101");
    expect(wrapper.find("#canvas-study-title").text()).toBe("CAD-101");
  });

  it("shows the lifecycle, three panes and participant counts on study home, naming no product", async () => {
    await open("/studies/CAD-101");

    expect(wrapper.findAll(".lifecycle-rail a").map((a) => a.text())).toEqual([
      "Design",
      "Start",
      "Conduct",
      "Data",
      "Analyze",
      "Submit",
    ]);
    expect(wrapper.find("#canvas-navigator").exists()).toBe(true);
    expect(wrapper.find("#canvas-workspace").exists()).toBe(true);
    expect(wrapper.find("#canvas-inspector").exists()).toBe(true);

    // One count for the study and one per state, each Execution's own total.
    expect(listCalls()).toEqual(
      expect.arrayContaining([
        { studyId: "CAD-101", limit: 1 },
        { studyId: "CAD-101", status: "ACTIVE", limit: 1 },
        { studyId: "CAD-101", status: "WITHDRAWN", limit: 1 },
      ])
    );
    expect(wrapper.find("#pulse-total").text()).toContain("3");
    expect(wrapper.find("#pulse-active").text()).toContain("1");
    expect(wrapper.find("#pulse-reconsent_required").text()).toContain(
      "Re-consent required"
    );
    // States with nobody in them are left out, not shown as zeros.
    expect(wrapper.find("#pulse-withdrawn").exists()).toBe(false);

    // UX-1: navigation names the study, the stage and clinical objects, never a product.
    const chrome = [
      wrapper.find(".topbar").text(),
      wrapper.find(".study-header").text(),
      wrapper.find("#canvas-navigator").text(),
    ].join(" ");
    expect(chrome).not.toMatch(/\b(EDC|CTMS|eTMF|RTSM|P21|QMS)\b/);
  });

  it("drills from a count to exactly the participants it counts (UX-7)", async () => {
    await open("/studies/CAD-101");

    await wrapper.find("#pulse-reconsent_required").trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      section: "participants",
      status: "RECONSENT_REQUIRED",
    });
    expect(listCalls()).toContainEqual({
      studyId: "CAD-101",
      status: "RECONSENT_REQUIRED",
      siteId: undefined,
      limit: 50,
      offset: 0,
    });
    const rows = wrapper.findAll("#participants-section tbody tr");
    expect(rows).toHaveLength(1);
    expect(rows[0].text()).toContain("017-0003");
    expect(wrapper.find("#participants-status").element.value).toBe(
      "RECONSENT_REQUIRED"
    );
  });

  it("fills the inspector with the selected participant and addresses it by NodeRef", async () => {
    await open("/studies/CAD-101?stage=conduct&section=participants");

    expect(wrapper.find("#canvas-inspector").text()).toContain(
      "Select a participant"
    );

    await wrapper.find("#participant-017-0002").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(router.currentRoute.value.query.node).toBe(`participant:${IDS[1]}`);
    const inspector = wrapper.find("#canvas-inspector");
    expect(inspector.find("#canvas-inspector-title").text()).toBe("017-0002");
    expect(inspector.find("#canvas-inspector-ref").text()).toBe(
      `participant:${IDS[1]}`
    );
    expect(inspector.find("#inspector-state").text()).toBe("Screening");
    expect(inspector.find("#inspector-site").text()).toBe("022");
    expect(
      inspector.find("#inspector-open-data-entry").attributes("href")
    ).toBe(
      `/studies/CAD-101?stage=conduct&participant=participant:${IDS[1]}&form=new`
    );

    // The zoom path shows study, site and participant, and the site is addressable.
    expect(wrapper.find("#zoom-participant").text()).toBe("017-0002");
    expect(wrapper.find("#zoom-site").text()).toBe("Site 022");
    await wrapper.find("#zoom-site").trigger("click");
    await router.isReady();
    await flushPromises();
    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      section: "participants",
      site: "022",
    });
    expect(listCalls()).toContainEqual(
      expect.objectContaining({ siteId: "022", offset: 0 })
    );
    expect(wrapper.findAll("#participants-section tbody tr")).toHaveLength(1);
  });

  it("opens the participant's forms workspace from the inspector inside the three-pane Canvas without legacy acronyms (UX-1, UX-2, UX-3)", async () => {
    const studyNodeRef = "study:0192f3c4-5e6a-7b8c-9d0e-000000009001";
    const siteNodeRef = "study_site:0192f3c4-5e6a-7b8c-9d0e-000000009002";
    const enrollmentId = "0192f3c4-5e6a-7b8c-9d0e-000000009003";
    const versionRef =
      "protocol_version:0192f3c4-5e6a-7b8c-9d0e-000000009004@1";

    vi.mocked(kernelApi.search).mockResolvedValue([
      { ref: studyNodeRef, exact_code: true, kind: "study", code: "CAD-101" },
    ]);
    vi.mocked(kernelApi.getChildren).mockImplementation(async (ref) => {
      if (ref === studyNodeRef) {
        return [
          { node: { kind: "study_site", ref: siteNodeRef, code: "017" } },
        ];
      }
      if (ref === siteNodeRef) {
        return [
          {
            node: {
              kind: "enrollment",
              ref: `enrollment:${enrollmentId}`,
              code: "017-0001",
            },
          },
        ];
      }
      return [];
    });
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([
      { ref: versionRef, status: "Approved", mask: null },
    ]);
    vi.mocked(kernelApi.getScheduleOfActivities).mockResolvedValue({
      version: versionRef,
      columns: [
        { activity_id: "WEEK2", name: "Week 2", study_day: 14, epoch_id: "E1" },
      ],
      rows: [
        {
          collection_spec_id: "VS",
          name: "Vital Signs",
          concept: "vital_signs",
          columns: [0],
        },
      ],
    });

    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${IDS[0]}`
    );

    await wrapper.find("#inspector-open-data-entry").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe(
      `/studies/CAD-101?stage=conduct&participant=participant:${IDS[0]}&form=new`
    );
    expect(router.currentRoute.value.fullPath).not.toMatch(/ecrf|edc/i);

    // Stays in the three-pane Canvas layout (UX-3).
    expect(wrapper.find("#canvas-navigator").exists()).toBe(true);
    expect(wrapper.find("#canvas-form-workspace").exists()).toBe(true);
    expect(wrapper.find("#canvas-inspector").exists()).toBe(true);

    // Zoom path shows study, site, participant, and Forms without product acronyms (UX-1, UX-2).
    const zoomText = wrapper.find("#canvas-zoom-path").text();
    expect(zoomText).toContain("CAD-101");
    expect(zoomText).toContain("Site 017");
    expect(zoomText).toContain("017-0001");
    expect(zoomText).toContain("Forms");
    expect(zoomText).not.toMatch(/\b(eCRF|EDC)\b/i);

    // Raw UUID inputs are hidden and structured protocol selectors are shown.
    expect(wrapper.find("#ecrf-form-id").exists()).toBe(false);
    expect(wrapper.find("#ecrf-enrollment-id").exists()).toBe(false);
    expect(wrapper.find("#ecrf-collection-spec").element.tagName).toBe(
      "SELECT"
    );
    expect(wrapper.find("#ecrf-planned-activity").element.tagName).toBe(
      "SELECT"
    );

    // One-click return navigation back to participant workspace.
    await wrapper.find("#canvas-form-back").trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      participant: `participant:${IDS[0]}`,
    });
  });

  it("shows Simulate disabled, saying what it waits on, and Trace, Impact, Explain and Act live (UX-5)", async () => {
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${IDS[0]}`
    );

    for (const verb of ["trace", "impact", "explain", "act"]) {
      expect(wrapper.find(`#verb-${verb}`).attributes("aria-disabled")).toBe(
        undefined
      );
    }
    for (const verb of ["simulate"]) {
      const button = wrapper.find(`#verb-${verb}`);
      expect(button.attributes("aria-disabled")).toBe("true");
      await button.trigger("click");
      expect(wrapper.find("#verb-note").text()).toMatch(/#48\d\d/);
      expect(button.attributes("aria-describedby")).toBe("verb-note");
    }
    expect(wrapper.find("#verb-details").attributes("aria-pressed")).toBe(
      "true"
    );
  });

  it("says so when the selected participant isn't loaded, rather than guessing", async () => {
    await open(`/studies/CAD-101?node=participant:${IDS[2]}`);

    expect(wrapper.find("#canvas-inspector-title").text()).toBe(
      "Participant not loaded"
    );
    expect(wrapper.find("#verb-trace").exists()).toBe(false);
  });

  it("pages through participants with Show more", async () => {
    const many = Array.from({ length: 60 }, (_, i) =>
      participant(0, {
        id: `0192f3c4-5e6a-7b8c-9d0e-${String(i + 1).padStart(12, "0")}`,
        subject_id: `017-${String(i + 1).padStart(4, "0")}`,
        enrollment_index: i + 1,
      })
    );
    serveParticipants(many);
    await open("/studies/CAD-101?section=participants");

    expect(wrapper.findAll("#participants-section tbody tr")).toHaveLength(50);
    expect(wrapper.find("#participants-status-line").text()).toBe(
      "Showing 50 of 60 participants."
    );

    await wrapper.find("#btn-more-participants").trigger("click");
    await flushPromises();

    expect(listCalls()).toContainEqual(
      expect.objectContaining({ limit: 50, offset: 50 })
    );
    expect(wrapper.findAll("#participants-section tbody tr")).toHaveLength(60);
    expect(wrapper.find("#btn-more-participants").exists()).toBe(false);
  });

  it("reports a failed load instead of showing an empty or sample list", async () => {
    const err = Object.assign(new Error("Service unavailable"), {
      status: 503,
    });
    vi.mocked(executionApi.listSubjects).mockRejectedValue(err);
    await open("/studies/CAD-101?section=participants");

    expect(wrapper.find("#participants-status-line").text()).toBe(
      "Participants couldn't be loaded."
    );
    expect(wrapper.find("#participants-section tbody").exists()).toBe(false);
    expect(useToastStore().toasts[0]).toMatchObject({
      tone: "error",
      title: "Couldn't load the study's participants",
    });
  });

  it("drops a response for a study the user has already left", async () => {
    let answerOld;
    vi.mocked(executionApi.listSubjects).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          answerOld = resolve;
        })
    );
    await open("/studies/OLD-1?section=participants");

    await router.push("/studies/CAD-101?section=participants");
    await flushPromises();
    answerOld({
      items: [participant(0, { subject_id: "OLD-0001", study_id: "OLD-1" })],
      total: 1,
      limit: 50,
      offset: 0,
    });
    await flushPromises();

    const text = wrapper.find("#participants-section").text();
    expect(text).not.toContain("OLD-0001");
    expect(text).toContain("017-0001");
  });

  it("shows a stage with no service as empty, with where it is tracked, not as sample content", async () => {
    await open("/studies/CAD-101?stage=analyze");

    expect(wrapper.find("#stage-analyze").attributes("aria-current")).toBe(
      "step"
    );
    expect(wrapper.find("#stage-empty").text()).toContain(
      "No part of Analyze has a service yet."
    );
    expect(wrapper.find("#stage-empty a").attributes("href")).toContain(
      "/plans"
    );
    expect(wrapper.html()).not.toContain("4829");
    expect(executionApi.listSubjects).not.toHaveBeenCalled();
  });

  it("runs site activation for the open study without asking for the study again", async () => {
    await open("/studies/CAD-101?section=site-activation");

    expect(wrapper.find("#site-activation-panel").exists()).toBe(true);
    expect(wrapper.find("#site-activation-study").exists()).toBe(false);

    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValue({
      study_id: "CAD-101",
      site_id: "017",
      milestone: "SITE_ACTIVATION",
      is_complete: false,
      missing_documents: ["FDA 1572"],
    });
    await wrapper.find("#site-activation-site").setValue("017");
    await wrapper.find("#site-activation-scope").trigger("submit");
    await flushPromises();

    expect(executionApi.getSiteComplianceStatus).toHaveBeenCalledWith(
      "017",
      "CAD-101"
    );
    expect(wrapper.find("#site-missing-documents").text()).toContain(
      "FDA 1572"
    );
  });

  describe("⌘K palette", () => {
    async function pressCtrlK() {
      document.dispatchEvent(
        new KeyboardEvent("keydown", { key: "k", ctrlKey: true, bubbles: true })
      );
      await flushPromises();
    }

    it("opens on Ctrl+K and Escape closes it", async () => {
      await open("/studies/CAD-101");

      await pressCtrlK();
      expect(wrapper.find("#canvas-palette").exists()).toBe(true);
      expect(document.activeElement?.id).toBe("canvas-palette-input");

      await wrapper.find("#canvas-palette-input").trigger("keydown", {
        key: "Escape",
      });
      expect(wrapper.find("#canvas-palette").exists()).toBe(false);
    });

    it("goes to a loaded participant by code, selecting it", async () => {
      await open("/studies/CAD-101?section=participants");
      await wrapper.find("#btn-open-palette").trigger("click");

      await wrapper.find("#canvas-palette-input").setValue("017-0003");
      const options = wrapper.findAll("#canvas-palette-list [role=option]");
      expect(options[0].text()).toContain("017-0003");
      await wrapper.find("#canvas-palette-input").trigger("keydown", {
        key: "Enter",
      });
      await flushPromises();

      expect(router.currentRoute.value.query).toEqual({
        stage: "conduct",
        section: "participants",
        node: `participant:${IDS[2]}`,
      });
      expect(wrapper.find("#canvas-palette").exists()).toBe(false);
      expect(wrapper.find("#canvas-inspector-title").text()).toBe("017-0003");
    });

    it("moves through results with the arrow keys and goes to a stage", async () => {
      await open("/studies/CAD-101");
      await wrapper.find("#btn-open-palette").trigger("click");

      await wrapper.find("#canvas-palette-input").setValue("conduct");
      const input = wrapper.find("#canvas-palette-input");
      expect(input.attributes("aria-activedescendant")).toBe(
        "canvas-palette-item-0"
      );
      await input.trigger("keydown", { key: "ArrowDown" });
      expect(input.attributes("aria-activedescendant")).toBe(
        "canvas-palette-item-1"
      );
      await input.trigger("keydown", { key: "ArrowUp" });
      await input.trigger("keydown", { key: "Enter" });
      await flushPromises();

      expect(router.currentRoute.value.query).toEqual({ stage: "conduct" });
    });

    it("opens another study by the code typed, exactly as typed", async () => {
      await open("/studies/CAD-101");
      await wrapper.find("#btn-open-palette").trigger("click");

      await wrapper.find("#canvas-palette-input").setValue("onc-401");
      const open401 = wrapper
        .findAll("#canvas-palette-list [role=option]")
        .find((o) => o.text().includes("Open study"));
      await open401.trigger("click");
      await flushPromises();

      expect(router.currentRoute.value.fullPath).toBe("/studies/onc-401");
      expect(listCalls()).toContainEqual({ studyId: "onc-401", limit: 1 });
    });
  });
});
