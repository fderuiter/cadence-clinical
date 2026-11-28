import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { DOMWrapper, mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { useAuthStore } from "../../src/stores/auth";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      listProposals: vi.fn(async () => []),
      search: vi.fn(async () => []),
      lookupEnrollment: vi.fn(),
      getChildren: vi.fn(async () => []),
      listProtocolVersions: vi.fn(async () => []),
      getScheduleOfActivities: vi.fn(),
      listStudyLocks: vi.fn(async () => ({ locks: [] })),
      canI: vi.fn(async () => ({ permitted: false })),
      activity: vi.fn(async () => ({ entries: [], next: null })),
      getCommands: vi.fn(async () => []),
      getNode: vi.fn(async () => {
        throw new Error("Not found");
      }),
      getObservation: vi.fn(),
      getZoomPath: vi.fn(async () => []),
      trace: vi.fn(),
      dependents: vi.fn(),
    },
  };
});

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfFormsApi")>();
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
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfQueriesApi")>();
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
    updateSubjectState: vi.fn(),
  },
}));

const SUBJECT_UUID = "0192f3c4-5e6a-7b8c-9d0e-000000000101";
const ENROLLMENT_UUID = "0192f3c4-5e6a-7b8c-9d0e-00000000e101";
const ENROLLMENT_REF = `enrollment:${ENROLLMENT_UUID}`;
const STUDY_REF = "study:0192f3c4-5e6a-7b8c-9d0e-000000009001";
const SITE_REF = "study_site:0192f3c4-5e6a-7b8c-9d0e-000000009002";

const CANONICAL_PARTICIPANT = {
  id: SUBJECT_UUID,
  subject_id: "SUBJ-101-001",
  study_id: "CADENCE-101",
  site_id: "SITE-101",
  status: "SCREENING",
  active_protocol_version: "1.0",
  enrollment_index: 0,
  enrollment_ref: ENROLLMENT_REF,
};

describe("Clinical Canvas canonical enrollment:<uuid> NodeRefs", () => {
  let pinia: ReturnType<typeof createPinia>;
  let router: ReturnType<typeof createRouter>;
  let wrapper: ReturnType<typeof mount> | undefined;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["CRC"];
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

    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [CANONICAL_PARTICIPANT],
      total: 1,
      limit: 50,
      offset: 0,
    });
    vi.mocked(executionApi.getSubjectCounts).mockResolvedValue({
      total: 1,
      counts: { SCREENING: 1 },
    });
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValue({
      id: SUBJECT_UUID,
      subject_id: "SUBJ-101-001",
      study_id: "CADENCE-101",
      status: "SCREENING",
      site_id: "SITE-101",
      active_protocol_version: "1.0",
      enrollment_index: 0,
      enrollment_ref: ENROLLMENT_REF,
      treatment_group: null,
      randomization_seed: null,
      investigational_product_id: null,
    });
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: STUDY_REF,
        kind: "study",
        title: "CADENCE-101 Study",
        code: "CADENCE-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.getNode).mockImplementation(async (ref: string) => {
      if (ref.startsWith("enrollment:")) {
        return {
          ref: `${ENROLLMENT_REF}@1`,
          kind: "enrollment",
          code: "SUBJ-101-001",
          title: "SUBJ-101-001",
          status: "Screening",
          mask: null,
        };
      }
      if (ref.startsWith("study_site:")) {
        return {
          ref: `${SITE_REF}@1`,
          kind: "study_site",
          code: "SITE-101",
          title: "Boston General",
          status: "Active",
          mask: null,
        };
      }
      throw new Error(`Unexpected node lookup: ${ref}`);
    });
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  async function open(url: string) {
    await router.push(url);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
    return wrapper;
  }

  it("emits canonical enrollment:<uuid> links from participant table, palette, and inspector", async () => {
    await open("/studies/CADENCE-101?stage=conduct&section=participants");

    await wrapper!.find("#participant-SUBJ-101-001").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(router.currentRoute.value.query.node).toBe(ENROLLMENT_REF);
    const inspector = wrapper!.find("#canvas-inspector");
    expect(inspector.find("#canvas-inspector-title").text()).toBe(
      "SUBJ-101-001"
    );
    expect(inspector.find("#canvas-inspector-ref").text()).toBe(ENROLLMENT_REF);
    expect(inspector.find("#inspector-open-record").attributes("href")).toBe(
      `/studies/CADENCE-101?stage=conduct&participant=${ENROLLMENT_REF}`
    );
    expect(
      inspector.find("#inspector-open-data-entry").attributes("href")
    ).toBe(
      `/studies/CADENCE-101?stage=conduct&participant=${ENROLLMENT_REF}&form=new`
    );

    const body = new DOMWrapper(document.body);
    await wrapper!.find("#btn-open-palette").trigger("click");
    await body.find("#canvas-palette-input").setValue("SUBJ-101-001");
    await flushPromises();
    await body.find("#canvas-palette-input").trigger("keydown", {
      key: "Enter",
    });
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      site: "SITE-101",
      participant: ENROLLMENT_REF,
    });
  });

  it("clicking an enrollment:<uuid> chip in Recent Activity opens the participant inspector with status actions, casebook links, commands, and lineage", async () => {
    vi.mocked(kernelApi.activity).mockResolvedValue({
      entries: [
        {
          event_id: "0192f3c4-5e6a-7b8c-9d0e-00000000aa01",
          type: "ParticipantScreened",
          family: "Execution",
          recorded: "2026-11-22T10:00:00Z",
          occurred_valid: "2026-11-22T10:00:00Z",
          actor_id: "usr-crc-01",
          subject: `${ENROLLMENT_REF}@1`,
          subject_kind: "enrollment",
          mask: null,
          correlation_id: "corr-101",
        },
      ],
      next: null,
    });
    vi.mocked(kernelApi.getCommands).mockResolvedValue([
      {
        command: "ChangeParticipantState",
        decision: {
          capability: "participant.change_state",
          verb_class: "Execute",
          resource: ENROLLMENT_REF,
          permitted: true,
          reason: null,
          explanation: {
            subject: ENROLLMENT_REF,
            claim: "You may advance enrollment state.",
            producer: "kernel.authorization",
            steps: [],
            conditions: [],
            next_actions: [],
          },
        },
      },
    ]);
    vi.mocked(kernelApi.trace).mockResolvedValue({
      start: ENROLLMENT_REF,
      direction: "Backward",
      depth: 10,
      valid_at: "2026-11-22T10:00:00Z",
      known_at: "2026-11-22T10:00:00Z",
      nodes: [
        {
          handle: ENROLLMENT_REF,
          kind: "enrollment",
          ref: ENROLLMENT_REF,
          mask: null,
          distance: 0,
        },
        {
          handle: `${SITE_REF}@1`,
          kind: "study_site",
          ref: `${SITE_REF}@1`,
          mask: null,
          distance: 1,
        },
      ],
      edges: [
        {
          from: ENROLLMENT_REF,
          to: `${SITE_REF}@1`,
          kind: "GovernedBy",
          activity: null,
          recorded_at: "2026-11-22T10:00:00Z",
          valid_from: null,
        },
      ],
      truncated: false,
    });

    await open("/studies/CADENCE-101");

    const chip = wrapper!
      .findAll("#study-activity .node-ref-chip")
      .find((el) => el.text().includes("SUBJ-101-001"));
    expect(chip).toBeDefined();
    await chip!.trigger("click");
    await router.isReady();
    await flushPromises();

    expect(router.currentRoute.value.query.node).toBe(`${ENROLLMENT_REF}@1`);
    const inspector = wrapper!.find("#canvas-inspector");
    expect(inspector.find("#canvas-inspector-title").text()).toBe(
      "SUBJ-101-001"
    );
    expect(inspector.find("#canvas-inspector-ref").text()).toBe(ENROLLMENT_REF);

    // Status actions + unified in-canvas casebook links in Details tab:
    expect(inspector.find("#inspector-state").text()).toBe("Screening");
    expect(inspector.find("#inspector-state-change").exists()).toBe(true);
    expect(inspector.find("#state-change-target").exists()).toBe(true);
    expect(inspector.find("#inspector-open-record").exists()).toBe(true);
    expect(inspector.find("#inspector-open-data-entry").exists()).toBe(true);

    // Commands in Act tab:
    await inspector.find("#verb-act").trigger("click");
    await flushPromises();
    expect(kernelApi.getCommands).toHaveBeenCalledWith(ENROLLMENT_REF);
    expect(inspector.find("#kernel-act-list").text()).toContain("Permitted");

    // Lineage in Trace tab:
    await inspector.find("#verb-trace").trigger("click");
    await flushPromises();
    expect(kernelApi.trace).toHaveBeenCalledWith(ENROLLMENT_REF);
    expect(inspector.find("#kernel-trace-list").text()).toContain(
      "Boston General"
    );
  });

  it("supports legacy participant:<uuid> URLs as a fallback and upgrades inspector kernelRef to canonical enrollment:<uuid>", async () => {
    await open(
      `/studies/CADENCE-101?stage=conduct&section=participants&node=participant:${SUBJECT_UUID}`
    );

    const inspector = wrapper!.find("#canvas-inspector");
    expect(inspector.find("#canvas-inspector-title").text()).toBe(
      "SUBJ-101-001"
    );
    expect(inspector.find("#canvas-inspector-ref").text()).toBe(ENROLLMENT_REF);

    await inspector.find("#verb-act").trigger("click");
    await flushPromises();
    expect(kernelApi.getCommands).toHaveBeenCalledWith(ENROLLMENT_REF);
  });
});
