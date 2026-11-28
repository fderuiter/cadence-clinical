import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { setupServer } from "msw/node";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { kernelGovernanceApi } from "../../src/api/kernelGovernanceApi";
import { requestStepUpSignature } from "../../src/api/signatureVerification";
import { createWebDemoStore } from "../../src/mocks/demoStore.js";
import { buildWebGatewayHandlers } from "../../src/mocks/handlers.js";
import { useAuthStore } from "../../src/stores/auth";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/signatureVerification", () => ({
  requestStepUpSignature: vi.fn(),
}));

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
      getProtocolDesignSummary: vi.fn(),
      listProtocolTemplates: vi.fn(async () => []),
      importProtocolDraft: vi.fn(),
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
      forkProtocolVersion: vi.fn(),
      getProtocolApprovalReadiness: vi.fn(),
      submitProtocolVersionForReview: vi.fn(),
      returnProtocolVersionToDraft: vi.fn(),
      approveProtocolVersion: vi.fn(),
      withdrawProtocolVersion: vi.fn(),
      saveSimulationReport: vi.fn(),
      reviewArtifactVersion: vi.fn(),
      listStudyArtifacts: vi.fn(async () => []),
      registerStudyArtifact: vi.fn(),
      listStudyEffectivities: vi.fn(async () => []),
      declareProtocolEffectivity: vi.fn(),
      changeEffectivityStatus: vi.fn(),
      listStudySites: vi.fn(async () => []),
    },
  };
});

const STUDY_REF = "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1@1";
const V1_ID = "0192f3c4-5e6a-7b8c-9d0e-0000000000b1";
const V2_ID = "0192f3c4-5e6a-7b8c-9d0e-0000000000b2";
const V1_REF = `protocol_version:${V1_ID}@2`;
const V2_REF = `protocol_version:${V2_ID}@1`;
const RULE_PACK_REF = "library_item:0192f3c4-5e6a-7b8c-9d0e-000000005001@1";
const MAPPING_REF = "library_item:0192f3c4-5e6a-7b8c-9d0e-000000005002@1";
const FORM_SNAP_REF =
  "form_version_snapshot:0192f3c4-5e6a-7b8c-9d0e-000000002001@1";
const FORM_EL_REF = "protocol_element:0192f3c4-5e6a-7b8c-9d0e-000000001001@1";
const ITEM_EL_REF = "protocol_element:0192f3c4-5e6a-7b8c-9d0e-000000001002@1";
const VISIT_WK4_EL_REF =
  "protocol_element:0192f3c4-5e6a-7b8c-9d0e-000000001003@2";
const CONCEPT_REF = "concept:0192f3c4-5e6a-7b8c-9d0e-000000003001@1";
const ARTIFACT_VER_REF =
  "artifact_version:0192f3c4-5e6a-7b8c-9d0e-000000006001@1";
const SITE_REF = "study_site:0192f3c4-5e6a-7b8c-9d0e-000000007001@1";
const ENROLLMENT_REF = "enrollment:0192f3c4-5e6a-7b8c-9d0e-000000008001@1";

const V1 = {
  ref: V1_REF,
  study: STUDY_REF,
  number: 1,
  parent_version: null,
  status: "Approved",
  content_hash: "sha256:v1-frozen",
  amendment_intent: null,
  approval_attestation: "attestation:0192f3c4-5e6a-7b8c-9d0e-000000009001",
  withdrawal_reason: null,
  mask: null,
};

const V2_AMENDMENT = {
  ref: V2_REF,
  study: STUDY_REF,
  number: 2,
  parent_version: `protocol_version:${V1_ID}`,
  status: "InReview",
  content_hash: "sha256:v2-frozen",
  amendment_intent: "Add Week 4 biomarker panel",
  approval_attestation: null as string | null,
  withdrawal_reason: null as string | null,
  mask: null,
};

const DESIGN_SUMMARY = {
  study: STUDY_REF,
  version: V2_AMENDMENT,
  title: "CAD-101 Protocol v2",
  form_count: 1,
  item_count: 1,
  amendment: null,
  schema: "cadence.usdm.design/v1",
  imported_rule_pack: {
    ref: RULE_PACK_REF,
    code: "CAD-101-RULES-V2",
    version: "2.0",
    rule_count: 4,
  },
  visits: [
    {
      id: "visit.week4",
      name: "Week 4 Biomarker Visit",
      epoch_id: "epoch.treatment",
      study_day: 28,
      window_before: 2,
      window_after: 3,
      repeating: false,
      protocol_element: VISIT_WK4_EL_REF,
    },
  ],
  forms: [
    {
      id: "FORM-BIO",
      name: "Cardiometabolic Biomarkers",
      repeating: false,
      visit_ids: ["visit.week4"],
      protocol_element: FORM_EL_REF,
      form_version_snapshot: FORM_SNAP_REF,
      items: [
        {
          id: "ITEM-TROP",
          prompt: "High-Sensitivity Troponin I",
          value_kind: "Quantity",
          unit: "ng/L",
          required: true,
          concept: CONCEPT_REF,
          protocol_element: ITEM_EL_REF,
        },
      ],
    },
  ],
};

describe("Design RBAC gating, NodeRef deep-links, 7-section Simulate, 6-metric Impact, and Part 11 attestation", () => {
  let pinia: ReturnType<typeof createPinia>;
  let router: ReturnType<typeof createRouter>;
  let wrapper: ReturnType<typeof mount> | undefined;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Sponsor Designer", "CRA", "Auditor"];
    auth.currentPersona = "sponsor_designer";
    auth.user = {
      id: "0192f3c4-5e6a-7b8c-9d0e-000000000001",
      username: "designer.lead",
      email: "designer.lead@cadence.clinical",
      firstName: "Elena",
      lastName: "Vance",
      tenantId: "0192f3c4-5e6a-7b8c-9d0e-000000000010",
    };

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
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([
      V2_AMENDMENT,
      V1,
    ]);
    vi.mocked(kernelApi.getProtocolVersion).mockImplementation(async (ref) =>
      String(ref).includes(V2_ID) ? V2_AMENDMENT : V1
    );
    vi.mocked(kernelApi.getProtocolDesignSummary).mockResolvedValue(
      DESIGN_SUMMARY as never
    );
    vi.mocked(kernelApi.getScheduleOfActivities).mockResolvedValue({
      study: STUDY_REF,
      version: V2_REF,
      content_hash: "sha256:v2-frozen",
      hash: "sha256:v2-soa",
      epochs: [
        {
          epoch_id: "epoch.treatment",
          name: "Treatment",
          order: 1,
          protocol_element: null,
        },
      ],
      columns: [
        {
          activity_id: "visit.week4",
          name: "Week 4 Biomarker Visit",
          epoch_id: "epoch.treatment",
          study_day: 28,
          window_before: 2,
          window_after: 3,
          locations: ["Clinic"],
          repeating: false,
          footnotes: [],
          protocol_element: VISIT_WK4_EL_REF,
        },
      ],
      rows: [
        {
          collection_spec_id: "FORM-BIO",
          name: "Cardiometabolic Biomarkers",
          repeating: false,
          frequency: 1,
          concepts: [CONCEPT_REF],
          columns: [0],
          footnotes: [],
          protocol_element: FORM_EL_REF,
          form_version_snapshot: FORM_SNAP_REF,
        },
      ],
      footnotes: [],
    } as never);
    vi.mocked(kernelGovernanceApi.listStudyArtifacts).mockResolvedValue([
      {
        artifact_ref: "artifact:0192f3c4-5e6a-7b8c-9d0e-000000006000",
        artifact_id: "0192f3c4-5e6a-7b8c-9d0e-000000006000",
        artifact_version_ref: ARTIFACT_VER_REF,
        artifact_version_id: "0192f3c4-5e6a-7b8c-9d0e-000000006001",
        version_number: 1,
        kind: "EthicsApproval",
        study_ref: STUDY_REF,
        title: "Central IRB Amendment Approval Letter",
        content_hash: "sha256:irb-v2",
        size_bytes: 2048,
        media_type: "application/pdf",
        storage_ref: "s3://etmf/irb-v2.pdf",
        recorded_at: "2026-03-01T10:00:00Z",
      },
    ]);
    vi.mocked(kernelGovernanceApi.listStudySites).mockResolvedValue([
      {
        study_site_ref: SITE_REF,
        study_site_id: "0192f3c4-5e6a-7b8c-9d0e-000000007001",
        study_ref: STUDY_REF,
        site_ref: "site:0192f3c4-5e6a-7b8c-9d0e-000000007099",
        site_code: "SITE-101",
        site_name: "Boston General Hospital",
        country: "US",
        status: "Active",
        effective_versions: [],
      },
    ]);
    vi.mocked(kernelGovernanceApi.listStudyEffectivities).mockResolvedValue([
      {
        effectivity_ref:
          "protocol_effectivity:0192f3c4-5e6a-7b8c-9d0e-000000007100",
        effectivity_id: "0192f3c4-5e6a-7b8c-9d0e-000000007100",
        study_ref: STUDY_REF,
        protocol_version_ref: V1_REF,
        protocol_version_number: 1,
        scope_level: "StudySite",
        country: null,
        study_site_ref: SITE_REF,
        status: "Effective",
        status_since: "2026-01-15T00:00:00Z",
        evidence_artifact_version_refs: [ARTIFACT_VER_REF],
      },
    ]);
    vi.mocked(kernelApi.getNode).mockImplementation(async (ref) => ({
      ref: String(ref),
      kind: String(ref).split(":")[0],
      code: "RESOLVED",
      title: `Resolved ${String(ref).split(":")[0]}`,
      status: "Active",
      mask: null,
    }));
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

  it("hides #protocol-import-form and #protocol-import-submit for cra_monitor and auditor while keeping versions, schedule, and #protocol-import-summary accessible", async () => {
    const auth = useAuthStore();
    await open(
      `/studies/CAD-101?stage=design&section=protocol-versions&node=${V2_REF}`
    );

    expect(wrapper!.find("#protocol-import-form").exists()).toBe(true);
    expect(wrapper!.find("#protocol-import-submit").exists()).toBe(true);
    expect(wrapper!.find("#protocol-import-summary").exists()).toBe(true);

    // Switch to CRA Monitor
    expect(auth.switchPersona("cra_monitor")).toBe(true);
    await flushPromises();

    expect(wrapper!.find("#protocol-import-form").exists()).toBe(false);
    expect(wrapper!.find("#protocol-import-submit").exists()).toBe(false);
    expect(wrapper!.find("#protocol-versions-section").exists()).toBe(true);
    expect(wrapper!.find("#protocol-import-summary").exists()).toBe(true);

    // Switch to Auditor
    expect(auth.switchPersona("auditor")).toBe(true);
    await flushPromises();

    expect(wrapper!.find("#protocol-import-form").exists()).toBe(false);
    expect(wrapper!.find("#protocol-import-submit").exists()).toBe(false);
    expect(wrapper!.find("#protocol-import-summary").exists()).toBe(true);

    // Navigate to Schedule of activities as Auditor -> read-only schedule stays accessible
    await wrapper!.find("#nav-schedule-of-activities").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(wrapper!.find("#soa-matrix-table").exists()).toBe(true);
  });

  it("renders parent versions, imported_rule_pack.ref, governed forms/items, concepts, artifact_version_ref, and study_site_ref as NodeRefChips without 404s", async () => {
    await open(
      `/studies/CAD-101?stage=design&section=protocol-versions&node=${V2_REF}`
    );

    const chips = wrapper!.findAllComponents({ name: "NodeRefChip" });
    const renderedRefs = chips.map((c) => String(c.props("nodeRef")));

    expect(
      renderedRefs.some((r) => r.startsWith(`protocol_version:${V1_ID}`))
    ).toBe(true);
    expect(renderedRefs).toContain(RULE_PACK_REF);
    expect(renderedRefs).toContain(FORM_EL_REF);
    expect(renderedRefs).toContain(FORM_SNAP_REF);
    expect(renderedRefs).toContain(ITEM_EL_REF);
    expect(renderedRefs).toContain(CONCEPT_REF);
    expect(renderedRefs).toContain(ARTIFACT_VER_REF);
    expect(renderedRefs).toContain(SITE_REF);
  });

  it("resolves every NodeRef emitted by MSW protocolDesignHandlers via MSW kernelGraphHandlers getNode with HTTP 200", async () => {
    const store = createWebDemoStore();
    const server = setupServer(
      ...buildWebGatewayHandlers({ forceDemo: true, store })
    );
    server.listen();
    const origin = window.location.origin;
    const headers = { Authorization: "Bearer demo-token-001" };

    try {
      const searchRes = await fetch(
        `${origin}/api/v1/kernel/search?q=DEMO-01`,
        { headers }
      );
      expect(searchRes.status).toBe(200);
      const searchHits = await searchRes.json();
      const studyRef = searchHits.find(
        (h: { kind: string }) => h.kind === "study"
      )?.ref;
      expect(studyRef).toBeTruthy();

      const versionsRes = await fetch(
        `${origin}/api/v1/kernel/studies/${encodeURIComponent(studyRef)}/protocol-versions`,
        { headers }
      );
      expect(versionsRes.status).toBe(200);
      const versions = await versionsRes.json();
      const v2 = versions.find((v: { number: number }) => v.number === 2);
      expect(v2?.parent_version).toBeTruthy();

      const soaRes = await fetch(
        `${origin}/api/v1/kernel/protocol-versions/${encodeURIComponent(v2.ref)}/schedule-of-activities`,
        { headers }
      );
      expect(soaRes.status).toBe(200);
      const soa = await soaRes.json();

      const summaryRes = await fetch(
        `${origin}/api/v1/kernel/protocol-versions/${encodeURIComponent(v2.ref)}/design-summary`,
        { headers }
      );
      expect(summaryRes.status).toBe(200);
      const summary = await summaryRes.json();

      const refsToVerify = new Set<string>(
        [
          v2.ref,
          v2.parent_version,
          summary.imported_rule_pack?.ref,
          ...soa.epochs.map(
            (e: { protocol_element?: string }) => e.protocol_element
          ),
          ...soa.columns.map(
            (c: { protocol_element?: string }) => c.protocol_element
          ),
          ...soa.rows.flatMap(
            (r: {
              protocol_element?: string;
              form_version_snapshot?: string;
              concepts?: string[];
            }) => [
              r.protocol_element,
              r.form_version_snapshot,
              ...(r.concepts ?? []),
            ]
          ),
          ...summary.forms.flatMap(
            (f: {
              protocol_element?: string;
              form_version_snapshot?: string;
              items?: Array<{ protocol_element?: string; concept?: string }>;
            }) => [
              f.protocol_element,
              f.form_version_snapshot,
              ...(f.items ?? []).flatMap((i) => [
                i.protocol_element,
                i.concept,
              ]),
            ]
          ),
        ].filter(Boolean)
      );

      expect(refsToVerify.size).toBeGreaterThanOrEqual(8);

      for (const ref of refsToVerify) {
        const nodeRes = await fetch(
          `${origin}/api/v1/kernel/nodes/${encodeURIComponent(ref)}`,
          { headers }
        );
        expect(nodeRes.status, `Expected 200 for NodeRef ${ref}`).toBe(200);
      }
    } finally {
      server.close();
    }
  });

  it("renders all 7 SimulationResult sections, executes MappingSpecification, RulePack, and draft candidate runs, and renders all 6 KernelAmendmentImpact operational counts", async () => {
    vi.mocked(kernelApi.simulate).mockResolvedValue({
      scope: STUDY_REF,
      as_of: {
        valid_at: "2027-03-14T10:00:00Z",
        known_at: "2027-03-14T10:00:00Z",
      },
      candidate: {
        kind: "ProtocolVersion",
        version: V2_REF,
        base_version: V1_REF,
        content_hash: "sha256:v2-frozen",
        base_content_hash: "sha256:v1-frozen",
      },
      engine_versions: [{ name: "protocol.simulate", version: "2" }],
      input_hash: "sha256:sim-input-all7",
      result_hash: "sha256:sim-result-all7",
      structural_diff: [
        {
          element_id: "visit.week4",
          protocol_element: VISIT_WK4_EL_REF,
          instance_type: "PlannedVisit",
          change: "Changed",
          properties: ["/window_after"],
        },
      ],
      schedule_changes: [
        {
          activity_id: "visit.week4",
          protocol_element: VISIT_WK4_EL_REF,
          change: "Changed",
          before: {
            activity_id: "visit.week4",
            activity_name: "Week 4",
            epoch_id: "epoch.treatment",
            epoch_order: 2,
            study_day: 28,
            window_before: 2,
            window_after: 2,
          },
          after: {
            activity_id: "visit.week4",
            activity_name: "Week 4",
            epoch_id: "epoch.treatment",
            epoch_order: 2,
            study_day: 28,
            window_before: 2,
            window_after: 3,
          },
        },
      ],
      dependents: {
        start: V1_REF,
        depth: 10,
        valid_at: "2027-03-14T10:00:00Z",
        known_at: "2027-03-14T10:00:00Z",
        groups: [
          {
            kind: "enrollment",
            count: 1,
            objects: [
              {
                handle: ENROLLMENT_REF,
                kind: "enrollment",
                ref: ENROLLMENT_REF,
                mask: null,
                distance: 1,
              },
            ],
          },
        ],
        total: 1,
        truncated: false,
      },
      matrix_gaps: [
        {
          element_id: "FORM-BIO",
          protocol_element: FORM_EL_REF,
          kind: "UnboundCollection",
          severity: "Warning",
          json_pointer_path: "/forms/0",
          description:
            "Cardiometabolic Biomarkers requires fasting window note.",
          recommendation: "Attach footnote 2 to FORM-BIO.",
        },
      ],
      operational_impact: {
        active_site_count: 4,
        pending_site_count: 2,
        total_participant_count: 18,
        affected_participant_count: 11,
        reconsent_participant_count: 7,
        participant_impacts: [
          {
            enrollment: ENROLLMENT_REF,
            subject_code: "SUBJ-101-001",
            status: "OnStudy",
            cohort: "MidStudy",
            requires_reconsent: true,
            schedule_shifts: [
              {
                activity_id: "visit.week4",
                protocol_element: VISIT_WK4_EL_REF,
                change: "Changed",
                before: {
                  visit_id: "visit.week4",
                  target_date: "2027-04-01",
                  window_start: "2027-03-30",
                  window_end: "2027-04-03",
                },
                after: {
                  visit_id: "visit.week4",
                  target_date: "2027-04-02",
                  window_start: "2027-03-31",
                  window_end: "2027-04-05",
                },
              },
            ],
          },
        ],
        hidden: {
          more_exist: true,
          reasons: ["OutOfScope"],
        },
      },
      projection_changes: [
        {
          dataset: "LB",
          added_count: 3,
          removed_count: 0,
          changed_count: 1,
          samples: [
            {
              record_key: "CAD-101/SUBJ-101-001/LB/TROP",
              change: "Added",
            },
          ],
        },
      ],
      validation_delta: {
        opening: [
          {
            rule_id: "SD1042",
            severity: "Warning",
            dataset: "LB",
            subject: ENROLLMENT_REF,
            explanation: "Troponin reference range missing at SITE-102.",
          },
        ],
        resolving: [
          {
            rule_id: "SD1008",
            severity: "Error",
            dataset: "SV",
            subject: ENROLLMENT_REF,
            explanation: "Week 4 visit window mismatch resolved.",
          },
        ],
        unchanged_count: 14,
      },
      masked: [],
    } as never);

    vi.mocked(kernelApi.amendmentImpact).mockResolvedValue({
      version: V2_REF,
      parent_version: V1_REF,
      active_site_count: 4,
      pending_site_count: 2,
      total_participant_count: 18,
      affected_participant_count: 11,
      reconsent_participant_count: 7,
      hidden: {
        more_exist: true,
        reasons: ["OutOfScope"],
      },
      changed_elements: [
        {
          element_id: "visit.week4",
          protocol_element: VISIT_WK4_EL_REF,
          instance_type: "PlannedVisit",
          change: "Changed",
          properties: ["/window_after"],
        },
      ],
      protocol_elements: {
        "visit.week4": VISIT_WK4_EL_REF,
      },
      schedule: {
        changes_schedule: true,
        changes_eligibility: false,
        directives: [
          {
            kind: "ModifyVisitWindow",
            element_id: "visit.week4",
            protocol_element: VISIT_WK4_EL_REF,
            impacted_concepts: [CONCEPT_REF],
          },
        ],
        undeclared: [],
        protocol_elements: {
          "visit.week4": VISIT_WK4_EL_REF,
        },
      },
      dependents: {
        start: V1_REF,
        depth: 10,
        valid_at: "2027-03-14T10:00:00Z",
        known_at: "2027-03-14T10:00:00Z",
        groups: [],
        total: 0,
        truncated: false,
      },
    } as never);

    await open(
      `/studies/CAD-101?stage=design&section=protocol-versions&node=${V2_REF}`
    );

    // 1. Run Simulate -> verify all 7 SimulationResult sections
    await wrapper!.find("#verb-simulate").trigger("click");
    await flushPromises();

    expect(wrapper!.find("#kernel-simulate-diff").exists()).toBe(true);
    expect(wrapper!.find("#kernel-simulate-schedule").exists()).toBe(true);
    expect(wrapper!.find("#kernel-simulate-dependents").exists()).toBe(true);
    expect(wrapper!.find("#kernel-simulate-gaps").exists()).toBe(true);
    expect(wrapper!.find("#kernel-simulate-gaps").text()).toContain("FORM-BIO");
    expect(wrapper!.find("#kernel-simulate-operational").exists()).toBe(true);
    expect(wrapper!.find("#sim-op-active-sites").text()).toBe("4");
    expect(wrapper!.find("#sim-op-pending-sites").text()).toBe("2");
    expect(wrapper!.find("#sim-op-total-participants").text()).toBe("18");
    expect(wrapper!.find("#sim-op-affected-participants").text()).toBe("11");
    expect(wrapper!.find("#sim-op-reconsent-participants").text()).toBe("7");
    expect(
      wrapper!.find("#kernel-simulate-operational-hidden").text()
    ).toContain("out of scope");
    expect(wrapper!.find("#kernel-simulate-projections").text()).toContain(
      "LB"
    );
    expect(wrapper!.find("#kernel-simulate-validation").text()).toContain(
      "SD1042"
    );

    // 2. Candidate runner: MappingSpecification, RulePack, and custom USDM draft
    await wrapper!
      .find("#simulate-candidate-kind")
      .setValue("MappingSpecification");
    await wrapper!.find("#simulate-library-item-ref").setValue(MAPPING_REF);
    await wrapper!.find("#kernel-simulate-candidate-form").trigger("submit");
    await flushPromises();

    expect(kernelApi.simulate).toHaveBeenLastCalledWith(
      expect.objectContaining({
        studyRef: STUDY_REF,
        kind: "MappingSpecification",
        libraryItemRef: MAPPING_REF,
      })
    );

    await wrapper!.find("#simulate-candidate-kind").setValue("RulePack");
    await wrapper!.find("#simulate-library-item-ref").setValue(RULE_PACK_REF);
    await wrapper!.find("#kernel-simulate-candidate-form").trigger("submit");
    await flushPromises();

    expect(kernelApi.simulate).toHaveBeenLastCalledWith(
      expect.objectContaining({
        studyRef: STUDY_REF,
        kind: "RulePack",
        libraryItemRef: RULE_PACK_REF,
      })
    );

    await wrapper!.find("#simulate-candidate-kind").setValue("ProtocolVersion");
    await wrapper!.find("#simulate-base-version-ref").setValue(V1_REF);
    await wrapper!
      .find("#simulate-candidate-content")
      .setValue('{"schema":"cadence.usdm.design/v1","visits":[]}');
    await wrapper!.find("#kernel-simulate-candidate-form").trigger("submit");
    await flushPromises();

    expect(kernelApi.simulate).toHaveBeenLastCalledWith(
      expect.objectContaining({
        studyRef: STUDY_REF,
        kind: "ProtocolVersion",
        baseVersionRef: V1_REF,
        candidateContent: '{"schema":"cadence.usdm.design/v1","visits":[]}',
      })
    );

    // 3. Run Impact -> verify all 6 KernelAmendmentImpact operational counts & hidden summary
    await wrapper!.find("#verb-impact").trigger("click");
    await flushPromises();

    expect(wrapper!.find("#kernel-amendment-operational").exists()).toBe(true);
    expect(wrapper!.find("#amendment-active-sites").text()).toBe("4");
    expect(wrapper!.find("#amendment-pending-sites").text()).toBe("2");
    expect(wrapper!.find("#amendment-total-participants").text()).toBe("18");
    expect(wrapper!.find("#amendment-affected-participants").text()).toBe("11");
    expect(wrapper!.find("#amendment-reconsent-participants").text()).toBe("7");
    expect(wrapper!.find("#amendment-hidden-summary").text()).toContain(
      "Yes (out of scope)"
    );
  });

  it("completes SaveSimulationReport -> Reviewed X-Sig-Token -> ApproveProtocolVersion with reviewAttestationRef when simulation carries additive protocol_element NodeRefs", async () => {
    const SIM_REPORT_VER_REF =
      "artifact_version:0192f3c4-5e6a-7b8c-9d0e-000000009901";
    const REVIEW_ATT_REF = "attestation:0192f3c4-5e6a-7b8c-9d0e-000000009902";
    let reportSaved = false;
    let reviewSigned = false;
    let currentV2 = { ...V2_AMENDMENT };

    vi.mocked(kernelApi.listProtocolVersions).mockImplementation(async () => [
      { ...currentV2 },
      V1,
    ]);
    vi.mocked(kernelApi.getProtocolVersion).mockImplementation(async (ref) =>
      String(ref).includes(V2_ID) ? { ...currentV2 } : V1
    );
    vi.mocked(
      kernelGovernanceApi.getProtocolApprovalReadiness
    ).mockImplementation(async () => ({
      protocol_version_ref: `protocol_version:${V2_ID}`,
      status: currentV2.status,
      is_amendment: true,
      parent_version_ref: `protocol_version:${V1_ID}`,
      ready: reportSaved && reviewSigned,
      simulation_report_saved: reportSaved,
      review_signed: reviewSigned,
      simulation_report_artifact_version_ref: reportSaved
        ? SIM_REPORT_VER_REF
        : null,
      review_attestation_ref: reviewSigned ? REVIEW_ATT_REF : null,
      blocking_code:
        reportSaved && reviewSigned
          ? null
          : "K.Protocol.SimulationReviewRequired",
      explanation: {
        subject: `protocol_version:${V2_ID}`,
        claim: "Amendment readiness status",
        producer: "kernel.protocol.approval_readiness",
        steps: [],
        conditions: [],
        next_actions: [],
      },
    }));
    vi.mocked(kernelApi.simulate).mockResolvedValue({
      scope: STUDY_REF,
      as_of: {
        valid_at: "2026-03-01T12:00:00Z",
        known_at: "2026-03-01T12:00:00Z",
      },
      candidate: {
        kind: "ProtocolVersion",
        version: `protocol_version:${V2_ID}`,
        base_version: `protocol_version:${V1_ID}`,
        content_hash: currentV2.content_hash,
        base_content_hash: V1.content_hash,
      },
      engine_versions: [{ name: "protocol.structural_diff", version: "1" }],
      input_hash: "sha256:sim-in-elem",
      result_hash: "sha256:sim-out-elem",
      structural_diff: [
        {
          element_id: "visit.week4",
          protocol_element: VISIT_WK4_EL_REF,
          instance_type: "PlannedVisit",
          change: "Changed",
          properties: ["/window_after"],
        },
      ],
      schedule_changes: [],
      masked: [],
      dependents: {
        start: V1_REF,
        depth: 1,
        valid_at: "2026-03-01T12:00:00Z",
        known_at: "2026-03-01T12:00:00Z",
        groups: [],
        total: 0,
        truncated: false,
      },
      matrix_gaps: [],
    } as never);
    vi.mocked(kernelGovernanceApi.saveSimulationReport).mockImplementation(
      async (payload) => {
        expect(payload.result).toMatchObject({
          structural_diff: [
            expect.objectContaining({
              element_id: "visit.week4",
              protocol_element: VISIT_WK4_EL_REF,
            }),
          ],
        });
        reportSaved = true;
        return {
          artifact_ref: "artifact:0192f3c4-5e6a-7b8c-9d0e-000000009900",
          artifact_id: "0192f3c4-5e6a-7b8c-9d0e-000000009900",
          artifact_version_ref: SIM_REPORT_VER_REF,
          artifact_version_id: "0192f3c4-5e6a-7b8c-9d0e-000000009901",
          version_number: 1,
          kind: "SimulationReport",
          study_ref: STUDY_REF,
          title: "Protocol Version 2 Simulation Report",
          content_hash: "sha256:sim-report-elem",
          size_bytes: 512,
          media_type: "application/vnd.cadence.simulation-report+json",
          storage_ref: "urn:cadence:sim:9901",
          recorded_at: "2026-03-01T12:01:00Z",
        };
      }
    );
    vi.mocked(kernelGovernanceApi.reviewArtifactVersion).mockImplementation(
      async () => {
        reviewSigned = true;
        return {
          attestation_ref: REVIEW_ATT_REF,
          attestation_id: "0192f3c4-5e6a-7b8c-9d0e-000000009902",
          artifact_ref: "artifact:0192f3c4-5e6a-7b8c-9d0e-000000009900",
          artifact_version_ref: SIM_REPORT_VER_REF,
          study_ref: STUDY_REF,
          meaning: "Reviewed",
          content_hash: "sha256:sim-report-elem",
          recorded_at: "2026-03-01T12:02:00Z",
        };
      }
    );
    vi.mocked(kernelGovernanceApi.approveProtocolVersion).mockImplementation(
      async () => {
        currentV2 = {
          ...currentV2,
          status: "Approved",
          approval_attestation:
            "attestation:0192f3c4-5e6a-7b8c-9d0e-000000009903",
        };
        return { ...currentV2 };
      }
    );
    vi.mocked(requestStepUpSignature).mockResolvedValue({
      sig_token: "step-up-token-elem",
      expires_in: 60,
    });

    await open(
      `/studies/CAD-101?stage=design&section=protocol-versions&node=${V2_REF}`
    );

    await wrapper!.find("#btn-open-amendment-simulate").trigger("click");
    await flushPromises();

    await vi.waitFor(() => {
      expect(wrapper!.find("#btn-save-simulation-report").exists()).toBe(true);
    });
    await wrapper!.find("#btn-save-simulation-report").trigger("click");
    await flushPromises();

    await wrapper!.find("#btn-sign-simulation-review").trigger("click");
    await flushPromises();

    const modal = wrapper!.find("#signature-capture-modal");
    await modal.find("#sig-password").setValue("ClinicalPass!123");
    await modal
      .find("#sig-reason")
      .setValue("Reviewed protocol amendment simulation and schedule impact");
    await modal.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(kernelGovernanceApi.reviewArtifactVersion).toHaveBeenCalledWith(
      expect.objectContaining({
        artifactVersionRef: SIM_REPORT_VER_REF,
        meaning: "Reviewed",
        sigToken: "step-up-token-elem",
      })
    );

    await wrapper!.find("#btn-approve-protocol-version").trigger("click");
    await flushPromises();

    await wrapper!.find("#sig-password").setValue("ClinicalPass!123");
    await wrapper!
      .find("#sig-reason")
      .setValue("Approve frozen protocol version for clinical conduct");
    await wrapper!.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(kernelGovernanceApi.approveProtocolVersion).toHaveBeenCalledWith(
      expect.objectContaining({
        versionRef: V2_REF,
        reviewAttestationRef: REVIEW_ATT_REF,
        expectedContentHash: "sha256:v2-frozen",
      })
    );
  });
});
