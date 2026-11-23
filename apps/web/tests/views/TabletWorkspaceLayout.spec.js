import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { useAuthStore } from "../../src/stores/auth";
import App from "../../src/App.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import EcrfLiveWorkspace from "../../src/views/EcrfLiveWorkspace.vue";
import CtmsLiveWorkspace from "../../src/views/CtmsLiveWorkspace.vue";
import EcrfItemTable from "../../src/components/ecrf/EcrfItemTable.vue";
import ParticipantFormsPanel from "../../src/components/canvas/ParticipantFormsPanel.vue";

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    kernelApi: {
      listProposals: vi.fn(async () => []),
      search: vi.fn(async () => []),
      lookupEnrollment: vi.fn(async () => ({
        enrollment_id: "0192f3c4-5e6a-7b8c-9d0e-000000000099",
      })),
      getChildren: vi.fn(async () => []),
      listProtocolVersions: vi.fn(async () => []),
      getScheduleOfActivities: vi.fn(async () => ({
        collection_specs: [
          {
            id: "vitals",
            name: "Vital Signs",
            planned_activity_ids: ["wk1"],
          },
        ],
        planned_activities: [{ id: "wk1", name: "Week 1", study_day: 1 }],
      })),
      listStudyLocks: vi.fn(async () => ({ locks: [] })),
      canI: vi.fn(async () => ({ permitted: false })),
      activity: vi.fn(async () => ({ entries: [], next: null })),
      getCommands: vi.fn(async () => []),
      getNode: vi.fn(async () => {
        throw new Error("Not found");
      }),
      getObservation: vi.fn(),
      getZoomPath: vi.fn(async () => []),
      lineage: vi.fn(async () => ({ nodes: [], edges: [] })),
      dependents: vi.fn(async () => ({ groups: [] })),
    },
  };
});

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfFormsApi: {
      listForEnrollment: vi.fn(async () => [
        {
          form_id: "0192f3c4-5e6a-7b8c-9d0e-000000000111",
          collection_spec_id: "vitals",
          collection_title: "Vital Signs",
          planned_activity_id: "wk1",
          collected_at: "2026-11-20T10:00:00Z",
          version: 1,
          complete: true,
          missing_required: [],
        },
      ]),
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
    getSubjectCounts: vi.fn(),
    getSubjectDetail: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

const PARTICIPANT_ONE = {
  id: "0192f3c4-5e6a-7b8c-9d0e-000000000001",
  subject_id: "017-0001",
  study_id: "CAD-101",
  site_id: "017",
  status: "ACTIVE",
  active_protocol_version: "3.0",
  enrollment_index: 1,
};

function evaluateMediaQuery(query, width) {
  const minMatch = query.match(/\(min-width:\s*([\d.]+)px\)/);
  const maxMatch = query.match(/\(max-width:\s*([\d.]+)px\)/);
  if (minMatch && width < Number(minMatch[1])) return false;
  if (maxMatch && width > Number(maxMatch[1])) return false;
  return Boolean(minMatch || maxMatch);
}

function setViewportWidth(width) {
  Object.defineProperty(window, "innerWidth", {
    configurable: true,
    writable: true,
    value: width,
  });
  window.matchMedia = vi.fn().mockImplementation((query) => ({
    matches: evaluateMediaQuery(query, width),
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }));
  window.dispatchEvent(new Event("resize"));
}

describe("Tablet & Desktop Adaptive Workspace Layout (ADR-2651)", () => {
  let pinia;
  let router;
  let wrapper;
  const initialInnerWidth = window.innerWidth;

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
        { path: "/ctms", name: "ctms", component: { template: "<div />" } },
        { path: "/plans", name: "plans", component: { template: "<div />" } },
        {
          path: "/tickets",
          name: "tickets",
          component: { template: "<div />" },
        },
        {
          path: "/knowledge",
          name: "knowledge",
          component: { template: "<div />" },
        },
      ],
    });
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [PARTICIPANT_ONE],
      total: 1,
      limit: 50,
      offset: 0,
    });
    vi.mocked(executionApi.getSubjectCounts).mockResolvedValue({
      total: 1,
      counts: { ACTIVE: 1 },
    });
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValue(PARTICIPANT_ONE);
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    setViewportWidth(initialInnerWidth);
    vi.restoreAllMocks();
  });

  it("teleports #canvas-zoom-path and #btn-ai-assistance into AppShell without rendering a second .topbar (UX-2)", async () => {
    setViewportWidth(1024);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    await router.push("/studies/CAD-101?stage=conduct&section=participants");
    await router.isReady();
    wrapper = mount(App, {
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });
    await flushPromises();

    expect(wrapper.find(".app-header #canvas-zoom-path").exists()).toBe(true);
    expect(wrapper.find(".app-header #btn-ai-assistance").exists()).toBe(true);
    expect(wrapper.find("#study-canvas .topbar").exists()).toBe(false);
  });

  it("opens CanvasInspector inside ClinicalDrawer when selecting a NodeRef from Study Home across stages at 768px-1279px", async () => {
    setViewportWidth(1024);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    await router.push("/studies/CAD-101");
    await router.isReady();
    wrapper = mount(App, {
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });
    await flushPromises();
    expect(wrapper.find("#canvas-inspector-drawer").exists()).toBe(false);

    // Navigate from Study Home into Conduct -> Participants with a target NodeRef
    await router.push(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${PARTICIPANT_ONE.id}`
    );
    await flushPromises();

    const drawer = wrapper.find("#canvas-inspector-drawer");
    expect(drawer.exists()).toBe(true);
    expect(drawer.attributes("aria-modal")).toBe("true");
    expect(drawer.find("#canvas-inspector").exists()).toBe(true);
    expect(drawer.text()).toContain("017-0001");
  });

  it("renders a collapsible 60px navigator rail and opens CanvasInspector inside ClinicalDrawer across 768px-1279px, then docks three panes at 1280px preserving selection and verb", async () => {
    setViewportWidth(1024);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    await router.push("/studies/CAD-101?stage=conduct&section=participants");
    await router.isReady();
    wrapper = mount(App, {
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });
    await flushPromises();

    const nav = wrapper.find("#canvas-navigator");
    expect(nav.classes()).toContain("navigator--rail");
    expect(nav.classes()).not.toContain("navigator--rail-expanded");

    await wrapper.find("#btn-toggle-navigator").trigger("click");
    expect(nav.classes()).toContain("navigator--rail-expanded");

    // Select participant 017-0001 -> opens slide-over ClinicalDrawer inspector on tablet
    await wrapper.find("#participant-017-0001").trigger("click");
    await flushPromises();

    const drawer = wrapper.find("#canvas-inspector-drawer");
    expect(drawer.exists()).toBe(true);
    expect(drawer.find("#canvas-inspector").exists()).toBe(true);
    expect(drawer.text()).toContain("017-0001");

    // Switch active inspector verb to Trace while in tablet drawer
    await drawer.find("#verb-trace").trigger("click");
    await flushPromises();
    expect(drawer.find("#verb-trace").attributes("aria-pressed")).toBe("true");

    // Escape dismisses the slide-over drawer while keeping selection reopenable
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
    await flushPromises();
    expect(wrapper.find("#canvas-inspector-drawer").exists()).toBe(false);
    expect(wrapper.find("#btn-open-inspector-drawer").exists()).toBe(true);

    // Resize to 1280px wide desktop -> docks all three panes and preserves selection + active verb
    setViewportWidth(1280);
    await flushPromises();

    expect(wrapper.find(".panes").classes()).toContain("panes--desktop");
    expect(wrapper.find("#canvas-inspector-drawer").exists()).toBe(false);
    const dockedInspector = wrapper.find(".panes > #canvas-inspector");
    expect(dockedInspector.exists()).toBe(true);
    expect(dockedInspector.text()).toContain("017-0001");
    expect(dockedInspector.find("#verb-trace").attributes("aria-pressed")).toBe(
      "true"
    );
  });

  it("applies sticky primary column and sticky save action bar classes in EcrfItemTable and ParticipantFormsPanel", async () => {
    const tableWrapper = mount(EcrfItemTable, {
      props: {
        formState: {
          form: {
            form_id: "0192f3c4-5e6a-7b8c-9d0e-000000000111",
            study_id: "CAD-101",
            collection_spec_id: "vitals",
            version: 1,
            complete: false,
            missing_required: ["sysbp"],
            locks: [],
            assertions: [],
            signatures: [],
          },
          items: [
            {
              item_id: "sysbp",
              label: "Systolic BP",
              data_type: "integer",
              required: true,
              unit: "mmHg",
              codelist: [],
              observation_id: null,
              value: null,
              revision: null,
            },
          ],
          entries: {
            sysbp: { text: "120", missing: "" },
          },
          editedItems: [
            {
              item_id: "sysbp",
              label: "Systolic BP",
              data_type: "integer",
              required: true,
              unit: "mmHg",
              codelist: [],
              observation_id: null,
              value: null,
              revision: null,
            },
          ],
          itemErrors: {},
          previewWarnings: {},
          reason: "Initial bedside entry",
          locked: false,
          assertionsLocked: false,
          saving: false,
          previewing: false,
          dirty: true,
          canSave: true,
          previewCheckedSignature: "",
          currentDraftSignature: "draft-1",
          previewFindings: [],
          previewError: null,
          outcome: null,
          itemIssues: {},
          isEdited: () => true,
          fieldError: () => null,
          previewChecks: vi.fn(),
          save: vi.fn(),
          reload: vi.fn(),
        },
        sdv: {
          selectedSdvItems: [],
          sdvTarget: null,
          sdvBusy: false,
          activeVerification: () => null,
          needsReverification: () => false,
          verifiedAssertionOf: () => null,
          canItemBeBulkVerified: () => false,
          isItemSelectedForSdv: () => false,
          toggleSdvSelection: vi.fn(),
          beginSdv: vi.fn(),
          cancelPendingSdv: vi.fn(),
          confirmSdv: vi.fn(),
        },
        queries: {
          queries: [],
          busy: false,
          raiseItemId: null,
          raiseText: "",
          raisePriority: "normal",
          raiseDueAt: "",
          raiseReason: "",
          forItem: () => [],
          openQueriesOf: () => [],
          beginRaise: vi.fn(),
          cancelRaise: vi.fn(),
          submitRaise: vi.fn(),
        },
        itemObservationLink: () => null,
      },
    });

    expect(tableWrapper.find("thead th.sticky-col-corner").exists()).toBe(true);
    expect(
      tableWrapper.find("tbody th[scope='row'].sticky-col-start").exists()
    ).toBe(true);
    expect(
      tableWrapper
        .find(".ecrf-save-bar.ecrf-form-row.sticky-action-bar")
        .exists()
    ).toBe(true);
    tableWrapper.unmount();

    const matrixWrapper = mount(ParticipantFormsPanel, {
      props: {
        studyCode: "CAD-101",
        subjectCode: "017-0001",
        enrollmentId: "0192f3c4-5e6a-7b8c-9d0e-000000000099",
        schedule: {
          versionRef: "protocol_version:0192f3c4-5e6a-7b8c-9d0e-000000000010",
          title: "CAD-101 v3.0",
          epochs: [
            {
              epochId: "treatment",
              name: "Treatment",
              order: 1,
              firstColumn: 0,
              columnCount: 1,
            },
          ],
          columns: [
            {
              activityId: "wk1",
              name: "Week 1",
              epochId: "treatment",
              studyDay: 1,
              windowBefore: 0,
              windowAfter: 2,
              locations: [],
            },
          ],
          scheduledRows: [
            {
              collectionSpecId: "vitals",
              name: "Vital Signs",
              itemCount: 4,
              columnIndices: [0],
              plannedActivityIds: ["wk1"],
            },
          ],
          unscheduledRows: [],
          collectionSpecs: [
            {
              id: "vitals",
              name: "Vital Signs",
              plannedActivityIds: ["wk1"],
            },
          ],
          plannedActivities: [{ id: "wk1", name: "Week 1", studyDay: 1 }],
        },
        formLocation: (formId) => `/ecrf?formId=${formId}`,
        newFormLocation: "/ecrf",
        newFormCellLocation: (specId, visitId) =>
          `/ecrf?spec=${specId}&visit=${visitId ?? ""}`,
      },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(
      matrixWrapper.find(".matrix-corner.sticky-col-corner").exists()
    ).toBe(true);
    expect(
      matrixWrapper.find(".matrix-row-header.sticky-col-start").exists()
    ).toBe(true);
    matrixWrapper.unmount();
  });

  it("enforces 44px staff touch targets and 16px touch input text on EcrfLiveWorkspace and CtmsLiveWorkspace controls", async () => {
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValue({
      study_id: "CAD-101",
      site_id: "017",
      is_activated: true,
      is_complete: true,
      missing_documents: [],
    });

    await router.push("/ecrf");
    await router.isReady();

    const ecrfWrapper = mount(EcrfLiveWorkspace, {
      global: {
        plugins: [pinia, router],
        mocks: { $t: (key) => key },
      },
    });
    await flushPromises();

    const selector = ecrfWrapper.find("#ecrf-subject-selector");
    expect(selector.exists()).toBe(true);
    expect(selector.attributes("style")).toContain(
      "min-height: var(--semantic-touch-target-staff)"
    );
    expect(selector.attributes("style")).toContain("font-size: 1rem");
    ecrfWrapper.unmount();

    const ctmsWrapper = mount(CtmsLiveWorkspace, {
      props: { studyCode: "CAD-101", initialSiteId: "017" },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    const tabs = ctmsWrapper.findAll(".live-tab-btn");
    expect(tabs.length).toBeGreaterThanOrEqual(6);
    ctmsWrapper.unmount();
  });
});
