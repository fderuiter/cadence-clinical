import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { kernelGovernanceApi } from "../../src/api/kernelGovernanceApi";
import { useAuthStore } from "../../src/stores/auth";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSubjectCounts: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      listProposals: vi.fn(async () => []),
      search: vi.fn(),
      listProtocolVersions: vi.fn(),
      getProtocolVersion: vi.fn(),
      getScheduleOfActivities: vi.fn(),
      getProtocolDesignSummary: vi.fn(async () => null),
      listProtocolTemplates: vi.fn(async () => []),
      getCommands: vi.fn(async () => []),
      getNode: vi.fn(),
      trace: vi.fn(),
      dependents: vi.fn(),
      amendmentImpact: vi.fn(),
      simulate: vi.fn(),
    },
  };
});

vi.mock("../../src/api/kernelGovernanceApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/kernelGovernanceApi")>();
  return {
    ...actual,
    kernelGovernanceApi: {
      ...actual.kernelGovernanceApi,
      listStudyArtifacts: vi.fn(async () => []),
      listStudyEffectivities: vi.fn(async () => []),
      listStudySites: vi.fn(async () => []),
      getProtocolApprovalReadiness: vi.fn(),
    },
  };
});

const STUDY_REF = "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1@1";
const V1_REF = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000b1@2";
const V2_DRAFT_REF = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000b2@1";
const EPOCH_SCREEN_EL =
  "protocol_element:0192f3c4-5e6a-7b8c-9d0e-000000001001@1";
const VISIT_SCREEN_EL =
  "protocol_element:0192f3c4-5e6a-7b8c-9d0e-000000001011@1";
const FORM_VS_EL = "protocol_element:0192f3c4-5e6a-7b8c-9d0e-000000001021@1";
const FORM_VS_SNAP =
  "form_version_snapshot:0192f3c4-5e6a-7b8c-9d0e-000000002021@1";
const CONCEPT_SYSBP = "concept:0192f3c4-5e6a-7b8c-9d0e-000000003001@1";

const V1 = {
  ref: V1_REF,
  study: STUDY_REF,
  number: 1,
  parent_version: null,
  status: "Approved",
  content_hash: "sha256:v1-frozen",
  amendment_intent: null,
  approval_attestation: "attestation:0192f3c4-5e6a-7b8c-9d0e-000000004001",
  withdrawal_reason: null,
  mask: null,
};

const V2_DRAFT = {
  ref: V2_DRAFT_REF,
  study: STUDY_REF,
  number: 2,
  parent_version: "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000b1",
  status: "Draft",
  content_hash: null,
  amendment_intent: "Add Week 4 home telehealth check",
  approval_attestation: null,
  withdrawal_reason: null,
  mask: null,
};

const SOA_GOVERNED = {
  version: V1_REF,
  title: "CAD-101 Schedule of Activities",
  content_hash: "sha256:v1-frozen",
  hash: "sha256:v1-matrix",
  epochs: [
    {
      epoch_id: "epoch.screening",
      name: "Screening",
      order: 1,
      first_column: 0,
      column_count: 1,
      protocol_element: EPOCH_SCREEN_EL,
    },
    {
      epoch_id: "epoch.followup",
      name: "Follow-up",
      order: 2,
      first_column: 1,
      column_count: 1,
      protocol_element: null,
    },
  ],
  columns: [
    {
      activity_id: "visit.screening",
      name: "Screening Visit",
      epoch_id: "epoch.screening",
      study_day: -7,
      window_before: 7,
      window_after: 0,
      locations: ["Clinic"],
      repeating: false,
      footnotes: [1],
      protocol_element: VISIT_SCREEN_EL,
    },
    {
      activity_id: "visit.telehealth",
      name: "Unscheduled Safety Call",
      epoch_id: "epoch.followup",
      study_day: null,
      window_before: 0,
      window_after: 0,
      locations: ["Telehealth / Home"],
      repeating: true,
      footnotes: [],
      protocol_element: null,
    },
  ],
  rows: [
    {
      collection_spec_id: "FORM-VS",
      name: "Vital Signs",
      item_ids: ["ITEM-SYSBP"],
      repeating: false,
      frequency: 1,
      concepts: [CONCEPT_SYSBP],
      columns: [0],
      footnotes: [1],
      protocol_element: FORM_VS_EL,
      form_version_snapshot: FORM_VS_SNAP,
    },
  ],
  footnotes: [
    {
      number: 1,
      kind: "Clinical",
      text: "Supine blood pressure after 5 minutes rest.",
      applies_to: ["visit.screening", "FORM-VS"],
    },
  ],
};

const SOA_UNGOVERNED_DRAFT = {
  ...SOA_GOVERNED,
  version: V2_DRAFT_REF,
  content_hash: null,
  hash: "sha256:v2-draft-matrix",
  epochs: [
    {
      epoch_id: "epoch.screening",
      name: "Screening",
      order: 1,
      first_column: 0,
      column_count: 1,
      protocol_element: null,
    },
  ],
  columns: [
    {
      activity_id: "visit.screening",
      name: "Screening Visit",
      epoch_id: "epoch.screening",
      study_day: -7,
      window_before: 7,
      window_after: 0,
      locations: ["Clinic"],
      repeating: false,
      footnotes: [],
      protocol_element: null,
    },
  ],
  rows: [
    {
      collection_spec_id: "FORM-VS",
      name: "Vital Signs",
      item_ids: ["ITEM-SYSBP"],
      repeating: false,
      frequency: 1,
      concepts: [CONCEPT_SYSBP],
      columns: [0],
      footnotes: [],
      protocol_element: null,
      form_version_snapshot: null,
    },
  ],
  footnotes: [],
};

describe("Design stage & Schedule of Activities matrix (ADR-2540)", () => {
  let pinia: ReturnType<typeof createPinia>;
  let router: ReturnType<typeof createRouter>;
  let wrapper: ReturnType<typeof mount> | undefined;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Sponsor Designer"];

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
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    });
    vi.mocked(executionApi.getSubjectCounts).mockResolvedValue({
      total: 0,
      counts: {},
    });
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: STUDY_REF,
        kind: "study",
        title: "Cadence 101",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([V2_DRAFT, V1]);
    vi.mocked(kernelApi.getProtocolVersion).mockImplementation(async (ref) =>
      String(ref).includes("0000000000b2") ? V2_DRAFT : V1
    );
    vi.mocked(kernelApi.getScheduleOfActivities).mockImplementation(
      async (versionRef) =>
        String(versionRef).includes("0000000000b2")
          ? SOA_UNGOVERNED_DRAFT
          : SOA_GOVERNED
    );
    vi.mocked(kernelApi.getNode).mockImplementation(async (ref) => {
      if (String(ref).startsWith("concept:")) {
        return {
          ref: CONCEPT_SYSBP,
          kind: "concept",
          code: "SYSBP",
          title: "Systolic Blood Pressure",
          status: "Active",
          mask: null,
        };
      }
      if (String(ref).startsWith("protocol_element:")) {
        return {
          ref: String(ref),
          kind: "protocol_element",
          code: "FORM-VS",
          title: "Vital Signs",
          status: "Governed",
          mask: null,
        };
      }
      if (String(ref).startsWith("form_version_snapshot:")) {
        return {
          ref: FORM_VS_SNAP,
          kind: "form_version_snapshot",
          code: "FORM-VS",
          title: "Vital Signs form",
          status: "Frozen",
          mask: null,
        };
      }
      return {
        ref: String(ref),
        kind: "protocol_version",
        code: "v1",
        title: "Version 1",
        status: "Approved",
        mask: null,
      };
    });
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  async function open(path: string) {
    await router.push(path);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
  }

  it("lists Protocol versions and Schedule of activities on ?stage=design without rendering #stage-empty", async () => {
    await open("/studies/CAD-101?stage=design");

    expect(wrapper!.find("#nav-protocol-versions").exists()).toBe(true);
    expect(wrapper!.find("#nav-schedule-of-activities").exists()).toBe(true);
    expect(wrapper!.find("#stage-empty").exists()).toBe(false);
  });

  it("renders SoA epochs, visit windows, locations, CollectionSpec rows, concepts, footnotes, and phone cards, and focuses protocol_element or protocol_version on cell click", async () => {
    await open("/studies/CAD-101?stage=design&section=schedule-of-activities");

    await vi.waitFor(() => {
      expect(wrapper!.find("#soa-matrix-table").exists()).toBe(true);
    });

    const tableText = wrapper!.find("#soa-matrix-table").text();
    expect(tableText).toContain("Screening");
    expect(tableText).toContain("Follow-up");
    expect(tableText).toContain("Screening Visit");
    expect(tableText).toContain("Day -7 (-7/+0d)");
    expect(tableText).toContain("Clinic");
    expect(tableText).toContain("Event-driven");
    expect(tableText).toContain("Telehealth / Home");
    expect(tableText).toContain("Repeating");
    expect(tableText).toContain("Vital Signs");
    expect(tableText).toContain("FORM-VS");
    expect(tableText).toContain("Systolic Blood Pressure");

    // Footnotes and responsive mobile cards are rendered from the same matrix payload
    expect(wrapper!.find("#soa-footnotes").text()).toContain(
      "Supine blood pressure after 5 minutes rest."
    );
    expect(wrapper!.find("#soa-mobile-cards").exists()).toBe(true);
    expect(wrapper!.find("#soa-mobile-cards").text()).toContain(
      "Screening Visit"
    );

    // Clicking a governed matrix cell focuses the inspector on its protocol_element NodeRef
    await wrapper!.find("#soa-cell-FORM-VS-visit\\.screening").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(router.currentRoute.value.query.node).toBe(FORM_VS_EL);
    expect(wrapper!.find("#canvas-inspector-title").text()).toContain(
      "Vital Signs"
    );

    // Selecting ungoverned Draft Version 2 renders Amends parent chip and falls back to protocol_version on cell click
    await wrapper!.find("#soa-version-select").setValue(V2_DRAFT_REF);
    await router.isReady();
    await flushPromises();

    expect(wrapper!.find(".soa-parent-chip").text()).toContain("Amends:");
    expect(router.currentRoute.value.query.node).toBe(V2_DRAFT_REF);

    await wrapper!.find("#soa-cell-FORM-VS-visit\\.screening").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(router.currentRoute.value.query.node).toBe(V2_DRAFT_REF);
  });
});
