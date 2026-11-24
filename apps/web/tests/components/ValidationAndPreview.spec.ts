import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { setupServer } from "msw/node";
import { defineComponent, h } from "vue";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createMemoryHistory, RouterView } from "vue-router";
import EcrfFormPanel from "../../src/components/ecrf/EcrfFormPanel.vue";
import QueriesSection from "../../src/components/canvas/QueriesSection.vue";
import ValidationSection from "../../src/components/canvas/ValidationSection.vue";
import ObservationView from "../../src/views/ObservationView.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import { kernelNavigationApi } from "../../src/api/kernel/navigation";
import type { FindingTargetKind } from "../../src/api/kernel/validation";
import {
  FINDING_ACCEPT_SEMANTIC_ACTION,
  FINDING_EXPLAIN_SEMANTIC_ACTION,
  RULE_PACK_APPROVE_SEMANTIC_ACTION,
  findingAcceptAction,
  findingExplainAction,
  kernelValidationApi,
  rulePackApproveAction,
} from "../../src/api/kernel/validation";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { clearNodeResolutionCache } from "../../src/canvas/nodeResolver";
import { createWebDemoStore } from "../../src/mocks/demoStore.js";
import { buildWebGatewayHandlers } from "../../src/mocks/handlers.js";
import { useAuthStore } from "../../src/stores/auth";
import { useConfirmStore } from "../../src/stores/confirm";

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfFormsApi")>();
  return {
    ...actual,
    ecrfFormsApi: {
      ...actual.ecrfFormsApi,
      getForm: vi.fn(),
      previewValues: vi.fn(),
      saveValues: vi.fn(),
    },
  };
});

vi.mock("../../src/api/ecrfQueriesApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfQueriesApi")>();
  return {
    ...actual,
    ecrfQueriesApi: {
      ...actual.ecrfQueriesApi,
      listForForm: vi.fn(),
      listForStudy: vi.fn(),
    },
  };
});

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      search: vi.fn(),
      listStudyRulePacks: vi.fn(),
      previewStudyRulePack: vi.fn(),
      declareStudyRulePacks: vi.fn(),
      listStudyValidationFindings: vi.fn(),
      getValidationFinding: vi.fn(),
      getObservation: vi.fn(),
    },
  };
});

const RouteStub = defineComponent({
  name: "RouteStub",
  render: () => h("div"),
});

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/studies/:studyCode?", name: "study", component: RouteStub },
      { path: "/ecrf", name: "ecrf", component: RouteStub },
      {
        path: "/observations/:observationRef",
        name: "observation",
        component: RouteStub,
      },
    ],
  });
}

describe("Outbox edit checks, pre-commit preview, UX-2 links, and role capabilities", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
  });

  it("surfaces pre-commit preview warnings, highlights queried item, and hides Sign button for non-signers", async () => {
    vi.useFakeTimers();
    const formId = "01950000-0000-7000-8000-000000000001";
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue({
      form_id: formId,
      study_id: "01950000-0000-7000-8000-000000000010",
      study_site_id: "01950000-0000-7000-8000-000000000015",
      protocol_version_id: "01950000-0000-7000-8000-000000000016",
      enrollment_id: "01950000-0000-7000-8000-000000000020",
      participant_id: "01950000-0000-7000-8000-000000000030",
      collection_spec_id: "VS",
      collection_title: "Vital signs",
      planned_activity_id: "V1",
      collected_at: "2026-03-01T10:00:00Z",
      version: 1,
      content_hash: "abcd1234",
      can_sign: false,
      complete: false,
      missing_required: ["SYSBP"],
      items: [
        {
          item_id: "SYSBP",
          concept: "Systolic BP",
          value_kind: "Quantity",
          unit: "mm[Hg]",
          required: true,
          observation_id: null,
          revision: 0,
          value: null,
          recorded_by: null,
          recorded_at: null,
        },
      ],
      locks: [],
      assertions: [],
      signatures: [],
    } as unknown as Awaited<ReturnType<typeof ecrfFormsApi.getForm>>);
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);
    vi.mocked(ecrfFormsApi.previewValues).mockResolvedValue({
      form_id: formId,
      warnings: [
        {
          pack_key: "cadence-101-edit-checks",
          rule_id: "VS_SYSBP_MAX",
          rule_version: 1,
          severity: "Warning",
          dataset: "VS",
          item_id: "SYSBP",
          message: "Systolic BP exceeds 220 mm[Hg].",
          remediation_guidance: null,
        },
      ],
    });

    const router = makeRouter();
    const wrapper = mount(EcrfFormPanel, {
      props: {
        formId,
        highlightItemId: "SYSBP",
      },
      global: { plugins: [router] },
    });
    await flushPromises();

    expect(wrapper.find("#ecrf-row-SYSBP").attributes("data-highlighted")).toBe(
      "true"
    );
    expect(wrapper.find("#ecrf-form-sign").exists()).toBe(false);
    expect(
      wrapper.find("[data-testid='ecrf-sign-unauthorized']").exists()
    ).toBe(true);

    await wrapper.find("#ecrf-item-SYSBP").setValue("245");
    vi.advanceTimersByTime(300);
    await flushPromises();

    expect(ecrfFormsApi.previewValues).toHaveBeenCalledWith(
      formId,
      expect.arrayContaining([expect.objectContaining({ item_id: "SYSBP" })])
    );
    expect(wrapper.find("[data-testid='ecrf-preview-banner']").exists()).toBe(
      true
    );
    expect(
      wrapper.find("[data-testid='ecrf-preview-warnings-SYSBP']").text()
    ).toContain("VS_SYSBP_MAX");
    vi.useRealTimers();
  });

  it("keeps query links inside /studies/:studyCode with item query param and hides Answer button for monitors", async () => {
    const studyUuid = "01950000-0000-7000-8000-000000000010";
    const formUuid = "01950000-0000-7000-8000-000000000001";
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: `study:${studyUuid}@1`,
        kind: "study",
        code: "CADENCE-101",
        title: "Demo study",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([
      {
        query_id: "01950000-0000-7000-8000-000000000099",
        form_id: formUuid,
        enrollment_id: "01950000-0000-7000-8000-000000000020",
        site_code: "SITE-101",
        subject_code: "SUBJ-101-001",
        item_id: "SYSBP",
        observation_id: "01950000-0000-7000-8000-000000000088",
        revision: 1,
        current_revision: 1,
        value_changed: false,
        status: "open",
        text: "Out of range",
        messages: [],
        raised_by: "svc:execution-edit-checks",
        raised_at: "2026-03-01T10:05:00Z",
        version: 1,
      },
    ]);

    const router = makeRouter();
    await router.push("/studies/CADENCE-101?stage=data&section=queries");
    await router.isReady();

    const wrapper = mount(QueriesSection, {
      props: {
        studyCode: "CADENCE-101",
        canManage: true,
        canAnswer: false,
      },
      global: { plugins: [router] },
    });
    await flushPromises();

    const formLink = wrapper.find(
      "[data-testid='query-form-link-01950000-0000-7000-8000-000000000099']"
    );
    expect(formLink.exists()).toBe(true);
    expect(formLink.attributes("href")).toContain("/studies/CADENCE-101");
    expect(formLink.attributes("href")).toContain("item=SYSBP");
    expect(
      wrapper.find("#btn-answer-01950000-0000-7000-8000-000000000099").exists()
    ).toBe(false);
    expect(
      wrapper.find("#btn-close-01950000-0000-7000-8000-000000000099").exists()
    ).toBe(true);
  });

  it("renders ValidationSection rule packs, previews in memory, and inspects findings", async () => {
    const studyUuid = "01950000-0000-7000-8000-000000000010";
    const packUuid = "01950000-0000-7000-8000-000000000055";
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: `study:${studyUuid}@1`,
        kind: "study",
        code: "CADENCE-101",
        title: "Demo study",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.listStudyRulePacks).mockResolvedValue({
      study_ref: `study:${studyUuid}@1`,
      profile_ref: `study_standards_profile:01950000-0000-7000-8000-000000000066@1`,
      pinned_rule_pack_refs: [`library_item:${packUuid}`],
      rule_packs: [
        {
          ref: `library_item:${packUuid}@2`,
          key: "cadence-101-edit-checks",
          title: "Cardiovascular Edit Checks",
          description: "Cardiovascular plausibility checks.",
          version: 1,
          status: "Approved",
          pinned: true,
          content_hash: "abcd",
          rule_count: 1,
          rules: [
            {
              id: "VS_SYSBP_MAX",
              version: 1,
              severity: "Warning",
              stage: "Collection",
              dataset: "VS",
              explanation: "Systolic BP exceeds 220 mm[Hg].",
              remediation_guidance: "Verify against source cuff reading.",
              condition: '{"op":"gt","field":"SYSBP","value":220}',
            },
          ],
        },
      ],
    });
    vi.mocked(kernelApi.listStudyValidationFindings).mockResolvedValue({
      total: 0,
      findings: [],
    });
    vi.mocked(kernelApi.previewStudyRulePack).mockResolvedValue({
      item_ref: `library_item:${packUuid}`,
      forms_evaluated: 2,
      rules_evaluated: 2,
      violation_count: 0,
      violations: [],
    });

    const router = makeRouter();
    const wrapper = mount(ValidationSection, {
      props: { studyCode: "CADENCE-101", canManageRules: true },
      global: { plugins: [router] },
    });
    await flushPromises();

    expect(wrapper.text()).toContain("Cardiovascular Edit Checks");
    expect(
      wrapper.find("[data-testid='rule-pack-pinned-badge']").exists()
    ).toBe(true);
    await wrapper
      .find("#btn-preview-pack-cadence-101-edit-checks-1")
      .trigger("click");
    await flushPromises();
    expect(kernelApi.previewStudyRulePack).toHaveBeenCalled();
    expect(
      wrapper
        .find("[data-testid='pack-preview-cadence-101-edit-checks']")
        .text()
    ).toContain("0 discrepancies");
  });

  it("allows investigators to correct NotAsked observations using expected item unit", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["investigator"];

    vi.mocked(kernelApi.getObservation).mockResolvedValue({
      ref: "observation:01950000-0000-7000-8000-000000000088@1",
      version: 1,
      enrollment_ref: "enrollment:01950000-0000-7000-8000-000000000020",
      form_ref: "collection_instance:01950000-0000-7000-8000-000000000001",
      item_id: "SYSBP",
      expected_value_kind: "Quantity",
      expected_unit: "mm[Hg]",
      expected_code_system_version_id: null,
      concept: "concept:01950000-0000-7000-8000-000000000077@1",
      value: { type: "missing", reason: "NotAsked" },
      observed_at: "2026-03-01T10:00:00Z",
      revisions: [],
      locks: [],
      assertions: [],
      audit: [],
    });

    const router = makeRouter();
    const wrapper = mount(ObservationView, {
      props: {
        observationRef: "observation:01950000-0000-7000-8000-000000000088",
        embedded: true,
      },
      global: { plugins: [router] },
    });
    await flushPromises();

    expect(wrapper.find("#observation-new-value").exists()).toBe(true);
    expect(wrapper.find("[data-testid='observation-unit']").text()).toBe(
      "mm[Hg]"
    );
  });

  it("renders a codelist <select> in ObservationView when expected_codelist_options is non-empty and submits coded corrections", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["investigator"];

    const csVersion = "01950000-0000-7000-8000-000000000099";
    const obsPayload = {
      ref: "observation:01950000-0000-7000-8000-000000000089@1",
      version: 1,
      enrollment_ref: "enrollment:01950000-0000-7000-8000-000000000020",
      form_ref: "collection_instance:01950000-0000-7000-8000-000000000001",
      item_id: "VSPOS",
      expected_value_kind: "Coded",
      expected_unit: null,
      expected_code_system_version_id: csVersion,
      expected_codelist: "C71148",
      expected_codelist_options: [
        { code: "SITTING", display: "Sitting" },
        { code: "SUPINE", display: "Supine" },
      ],
      concept: "concept:01950000-0000-7000-8000-000000000078@1",
      value: {
        type: "coded" as const,
        code_system_version_id: csVersion,
        code: "SITTING",
      },
      observed_at: "2026-03-01T10:00:00Z",
      revisions: [],
      locks: [],
      assertions: [],
      audit: [],
    };
    vi.mocked(kernelApi.getObservation)
      .mockResolvedValueOnce(obsPayload)
      .mockResolvedValueOnce({
        ...obsPayload,
        version: 2,
        value: {
          type: "coded",
          code_system_version_id: csVersion,
          code: "SUPINE",
        },
      });
    vi.spyOn(kernelApi, "correctObservation").mockResolvedValue({
      ref: "observation:01950000-0000-7000-8000-000000000089@2",
      enrollment_ref: "enrollment:01950000-0000-7000-8000-000000000020",
      value: {
        type: "coded",
        code_system_version_id: csVersion,
        code: "SUPINE",
      },
      version: 2,
      revision: 2,
    });

    const router = makeRouter();
    const wrapper = mount(ObservationView, {
      props: {
        observationRef: "observation:01950000-0000-7000-8000-000000000089",
        embedded: true,
      },
      global: { plugins: [router] },
    });
    await flushPromises();

    const select = wrapper.find("#observation-new-value");
    expect(select.exists()).toBe(true);
    expect(select.element.tagName).toBe("SELECT");
    expect(
      select.findAll("option").map((opt) => opt.attributes("value"))
    ).toEqual(["", "SITTING", "SUPINE"]);

    await select.setValue("SUPINE");
    expect(
      (wrapper.find("#observation-save").element as HTMLButtonElement).disabled
    ).toBe(false);
    await wrapper.find("form.observation-correction").trigger("submit");
    await flushPromises();

    const reasonModal = wrapper.findComponent({ name: "ReasonForChangeModal" });
    expect(reasonModal.exists()).toBe(true);
    reasonModal.vm.$emit("confirm", "Position verified on source worksheet");
    await flushPromises();

    expect(kernelApi.correctObservation).toHaveBeenCalledWith(
      expect.objectContaining({
        observationRef: "observation:01950000-0000-7000-8000-000000000089@1",
        value: {
          type: "coded",
          code_system_version_id: csVersion,
          code: "SUPINE",
        },
        expectedVersion: 1,
        reason: "Position verified on source worksheet",
      })
    );
  });
});

describe("Hydrated clinical NodeRef links, scope preservation, masking, and MSW validation routes", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    clearNodeResolutionCache();
    vi.clearAllMocks();
  });

  it("enforces strongly-typed FindingTargetKind enum values at compile time", () => {
    const validKinds: FindingTargetKind[] = [
      "DatasetRecord",
      "Observation",
      "DesignElement",
    ];
    // @ts-expect-error Legacy "Record" literal must be rejected by generated FindingTargetKind schema
    const invalidLegacyKind: FindingTargetKind = "Record";
    expect(validKinds).toEqual([
      "DatasetRecord",
      "Observation",
      "DesignElement",
    ]);
    expect(invalidLegacyKind).toBe("Record");
  });

  it("renders DatasetRecord, Observation, and DesignElement links with 6-dimension clinical context, scope preservation, Explain NodeRefChips, and blinded masking", async () => {
    const studyUuid = "01950000-0000-7000-8000-000000000010"; // deid-ignore
    const formUuid = "01950000-0000-7000-8000-000000002002"; // deid-ignore
    const obsUuid = "01950000-0000-7000-8000-000000003005"; // deid-ignore
    const designUuid = "01950000-0000-7000-8000-000000004001"; // deid-ignore
    const findingDatasetRef =
      "validation_finding:01950000-0000-7000-8000-000000006001"; // deid-ignore
    const findingObsRef =
      "validation_finding:01950000-0000-7000-8000-000000006002"; // deid-ignore
    const findingDesignRef =
      "validation_finding:01950000-0000-7000-8000-000000006003"; // deid-ignore
    const findingBlindedRef =
      "validation_finding:01950000-0000-7000-8000-000000006004"; // deid-ignore

    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: `study:${studyUuid}@1`,
        kind: "study",
        code: "CADENCE-101",
        title: "Demo study",
        detail: null,
        exact_code: true,
        rank: 0,
      },
    ]);
    vi.mocked(kernelApi.listStudyRulePacks).mockResolvedValue({
      study_ref: `study:${studyUuid}@1`,
      profile_ref: null,
      pinned_rule_pack_refs: [],
      rule_packs: [],
    });
    vi.mocked(kernelApi.listStudyValidationFindings).mockResolvedValue({
      total: 4,
      findings: [
        {
          ref: findingDatasetRef,
          study_ref: `study:${studyUuid}`,
          stage: "Collection",
          pack_key: "cadence-vital-signs-checks",
          rule_pack_ref: "library_item:01950000-0000-7000-8000-000000005001", // deid-ignore
          rule_pack_label: "Vital Signs Plausibility Checks (v1)",
          rule_id: "RULE-HR-RANGE",
          rule_version: 1,
          severity: "Error",
          status: "Open",
          blocks: true,
          classification: null,
          target_kind: "DatasetRecord",
          dataset: "VS",
          record_key: formUuid,
          variable: "item-hr",
          target_node_ref: `collection_instance:${formUuid}`,
          target_label: "Vital Signs (CDASH VS) · Baseline Visit",
          message: "Resting heart rate exceeds 100 /min.",
          expected: "50 to 100",
          observed: [{ variable: "HR", value: "108" }],
          first_detected_run_ref:
            "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
          last_detected_run_ref:
            "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
          resolved_by_run_ref: null,
          decision: null,
          site_ref: "study_site:01950000-0000-7000-8000-000000000202", // deid-ignore
          site_label: "SITE-102 · Lakeshore Medical Center",
          participant_ref: "enrollment:01950000-0000-7000-8000-000000001002", // deid-ignore
          participant_id: "SUBJ-1002",
          participant_label: "Participant SUBJ-1002",
          planned_activity_id: "visit-baseline",
          visit_label: "Baseline Visit (Day 1)",
          form_ref: `collection_instance:${formUuid}`,
          form_id: formUuid,
          form_label: "Vital Signs (CDASH VS) · Baseline Visit",
          collection_spec_id: "form-vital-signs",
          item_id: "item-hr",
          item_label: "Resting Heart Rate",
          observation_ref: `observation:${obsUuid}@1`,
          observation_label: "Resting Heart Rate (HR): 108 /min",
          query_ref: "form_query:01950000-0000-7000-8000-000000004099", // deid-ignore
          query_id: "01950000-0000-7000-8000-000000004099", // deid-ignore
          query_label: "Open system query on Resting Heart Rate",
        },
        {
          ref: findingObsRef,
          study_ref: `study:${studyUuid}`,
          stage: "Standardization",
          pack_key: "sdtm-vs",
          rule_pack_ref: "library_item:01950000-0000-7000-8000-000000005002", // deid-ignore
          rule_pack_label: "SDTM VS Core (v1)",
          rule_id: "SDTM_VS_UNIT",
          rule_version: 1,
          severity: "Warning",
          status: "Open",
          blocks: false,
          classification: null,
          target_kind: "Observation",
          dataset: "VS",
          record_key: `observation:${obsUuid}@1`,
          variable: "VSORRESU",
          target_node_ref: `observation:${obsUuid}`,
          target_label: "Vital Signs · VSORRESU",
          message: "Check original unit mapping.",
          expected: "mm[Hg]",
          observed: [],
          first_detected_run_ref:
            "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
          last_detected_run_ref:
            "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
          resolved_by_run_ref: null,
          decision: null,
          observation_ref: `observation:${obsUuid}@1`,
          observation_label: "VSORRESU: bpm",
        },
        {
          ref: findingDesignRef,
          study_ref: `study:${studyUuid}`,
          stage: "Design",
          pack_key: "usdm-design",
          rule_pack_ref: "library_item:01950000-0000-7000-8000-000000005003", // deid-ignore
          rule_pack_label: "Protocol Schedule Checks (v1)",
          rule_id: "SOA_WINDOW_MISSING",
          rule_version: 1,
          severity: "Warning",
          status: "Open",
          blocks: false,
          classification: null,
          target_kind: "DesignElement",
          dataset: "SOA",
          record_key: `protocol_element:${designUuid}`,
          variable: null,
          target_node_ref: `protocol_element:${designUuid}`,
          target_label: "Baseline Visit Schedule Element",
          message: "Visit window should specify +/- days.",
          expected: null,
          observed: [],
          first_detected_run_ref:
            "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
          last_detected_run_ref:
            "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
          resolved_by_run_ref: null,
          decision: null,
        },
        {
          ref: findingBlindedRef,
          study_ref: `study:${studyUuid}`,
          stage: "Collection",
          pack_key: "rtsm-blinded",
          rule_pack_ref: "library_item:01950000-0000-7000-8000-000000005004", // deid-ignore
          rule_pack_label: "masked",
          rule_id: "RTSM_ARM_CHECK",
          rule_version: 1,
          severity: "Warning",
          status: "Open",
          blocks: false,
          classification: null,
          target_kind: "DatasetRecord",
          dataset: "EX",
          record_key: "masked",
          variable: "masked",
          target_node_ref: null,
          target_label: "masked",
          message: "masked",
          expected: null,
          observed: [],
          first_detected_run_ref:
            "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
          last_detected_run_ref:
            "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
          resolved_by_run_ref: null,
          decision: null,
          site_label: "masked",
          participant_label: "masked",
          visit_label: "masked",
          form_label: "masked",
          observation_label: "masked",
          query_label: "masked",
        },
      ],
    });
    vi.mocked(kernelApi.getValidationFinding).mockResolvedValue({
      finding: {
        ref: findingDatasetRef,
        study_ref: `study:${studyUuid}`,
        stage: "Collection",
        pack_key: "cadence-vital-signs-checks",
        rule_pack_ref: "library_item:01950000-0000-7000-8000-000000005001", // deid-ignore
        rule_pack_label: "Vital Signs Plausibility Checks (v1)",
        rule_id: "RULE-HR-RANGE",
        rule_version: 1,
        severity: "Error",
        status: "Open",
        blocks: true,
        classification: null,
        target_kind: "DatasetRecord",
        dataset: "VS",
        record_key: formUuid,
        variable: "item-hr",
        target_node_ref: `collection_instance:${formUuid}`,
        target_label: "Vital Signs (CDASH VS) · Baseline Visit",
        message: "Resting heart rate exceeds 100 /min.",
        expected: "50 to 100",
        observed: [{ variable: "HR", value: "108" }],
        first_detected_run_ref:
          "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
        last_detected_run_ref:
          "validation_run:01950000-0000-7000-8000-000000007001", // deid-ignore
        resolved_by_run_ref: null,
        decision: null,
      },
      explanation: {
        subject: findingDatasetRef,
        claim:
          "Error finding RULE-HR-RANGE on Vital Signs (CDASH VS) · Baseline Visit is Open and blocks Collection.",
        producer: "quality.finding/RULE-HR-RANGE@1",
        steps: [
          {
            claim:
              "Rule RULE-HR-RANGE v1 in Vital Signs Plausibility Checks evaluated Vital Signs (CDASH VS) · Baseline Visit.",
            evidence: [
              "library_item:01950000-0000-7000-8000-000000005001", // deid-ignore
              `collection_instance:${formUuid}`,
            ],
          },
          {
            claim: "Observed HR = 108 on Vital Signs (CDASH VS).",
            evidence: [`observation:${obsUuid}@1`],
          },
        ],
        conditions: [
          {
            code: "finding.detected",
            description: "Detected on latest run",
            met: true,
            evidence: [],
          },
        ],
        next_actions: [
          {
            description:
              "Correct Resting Heart Rate in Vital Signs (CDASH VS) and re-validate",
            capability: "observation.correct",
            target: `collection_instance:${formUuid}`,
          },
        ],
      },
    });

    const router = makeRouter();
    await router.push(
      "/studies/CADENCE-101?stage=data&section=validation&site=SITE-102&participant=SUBJ-1002"
    );
    await router.isReady();

    const wrapper = mount(ValidationSection, {
      props: {
        studyCode: "CADENCE-101",
        site: "SITE-102",
        participant: "SUBJ-1002",
        canManageRules: true,
      },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    // DatasetRecord, Observation, and DesignElement target links all preserve Study, Site, and Participant scope
    const datasetTargetLink = wrapper.find(
      `[data-testid='finding-target-link-${findingDatasetRef}']`
    );
    expect(datasetTargetLink.exists()).toBe(true);
    expect(datasetTargetLink.attributes("href")).toContain(
      "/studies/CADENCE-101"
    );
    expect(datasetTargetLink.attributes("href")).toContain("site=SITE-102");
    expect(datasetTargetLink.attributes("href")).toContain(
      "participant=SUBJ-1002"
    );
    expect(datasetTargetLink.attributes("href")).toContain(
      `form=collection_instance:${formUuid}`
    );
    expect(datasetTargetLink.attributes("href")).toContain("item=item-hr");

    const obsTargetLink = wrapper.find(
      `[data-testid='finding-target-link-${findingObsRef}']`
    );
    expect(obsTargetLink.exists()).toBe(true);
    expect(obsTargetLink.attributes("href")).toContain(
      `observation=observation:${obsUuid}`
    );
    expect(obsTargetLink.attributes("href")).toContain("site=SITE-102");
    expect(obsTargetLink.attributes("href")).toContain("participant=SUBJ-1002");

    const designTargetLink = wrapper.find(
      `[data-testid='finding-target-link-${findingDesignRef}']`
    );
    expect(designTargetLink.exists()).toBe(true);
    expect(designTargetLink.attributes("href")).toContain("stage=design");
    expect(designTargetLink.attributes("href")).toContain(
      "section=protocol-versions"
    );
    expect(designTargetLink.attributes("href")).toContain("site=SITE-102");
    expect(designTargetLink.attributes("href")).toContain(
      "participant=SUBJ-1002"
    );
    expect(designTargetLink.attributes("href")).toContain(
      `node=protocol_element:${designUuid}`
    );

    // Hydrated 6-dimension clinical context links + Query link on DatasetRecord finding
    const contextRow = wrapper.find(
      `[data-testid='finding-context-${findingDatasetRef}']`
    );
    expect(contextRow.text()).toContain("SITE-102 · Lakeshore Medical Center");
    expect(contextRow.text()).toContain("Participant SUBJ-1002");
    expect(contextRow.text()).toContain("Baseline Visit (Day 1)");
    expect(contextRow.text()).toContain(
      "Vital Signs (CDASH VS) · Baseline Visit"
    );
    expect(contextRow.text()).toContain("Resting Heart Rate (HR): 108 /min");
    const queryLinkWrapper = wrapper.find(
      `[data-testid='finding-query-link-${findingDatasetRef}'] a`
    );
    expect(queryLinkWrapper.exists()).toBe(true);
    expect(queryLinkWrapper.attributes("href")).toContain("section=queries");
    expect(queryLinkWrapper.attributes("href")).toContain("site=SITE-102");
    expect(queryLinkWrapper.attributes("href")).toContain(
      "participant=SUBJ-1002"
    );

    // Blinded finding renders non-interactive masked placeholders
    const blindedContext = wrapper.find(
      `[data-testid='finding-context-${findingBlindedRef}']`
    );
    expect(
      blindedContext.findAll("[data-masked='true']").length
    ).toBeGreaterThanOrEqual(5);

    // Explain finding renders NodeRefChips for step.evidence and next_actions[].target with zero raw UUIDs
    await wrapper
      .find(`[data-testid='btn-inspect-finding-${findingDatasetRef}']`)
      .trigger("click");
    await flushPromises();

    const explanationBox = wrapper.find("[data-testid='finding-explanation']");
    expect(explanationBox.exists()).toBe(true);
    expect(explanationBox.text()).not.toContain(formUuid);
    expect(explanationBox.text()).not.toContain(obsUuid);
    expect(explanationBox.findAll("a.node-ref-chip").length).toBeGreaterThan(0);
  });

  it("prompts StudyCanvasView unsaved-draft guard before navigating away from a dirty eCRF form via finding target route", async () => {
    const studyUuid = "01950000-0000-7000-8000-000000000010"; // deid-ignore
    const openFormId = "01950000-0000-7000-8000-000000002001"; // deid-ignore
    const otherFormId = "01950000-0000-7000-8000-000000002002"; // deid-ignore
    const enrollmentId = "01950000-0000-7000-8000-000000001001"; // deid-ignore
    const participantId = "01950000-0000-7000-8000-000000005001"; // deid-ignore

    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: `study:${studyUuid}@1`,
        kind: "study",
        code: "CADENCE-101",
        title: "Demo study",
        detail: null,
        exact_code: true,
        rank: 0,
      },
    ]);
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue({
      form_id: openFormId,
      study_id: studyUuid,
      study_site_id: "01950000-0000-7000-8000-000000000201", // deid-ignore
      protocol_version_id: "01950000-0000-7000-8000-000000000101", // deid-ignore
      enrollment_id: enrollmentId,
      participant_id: participantId,
      collection_spec_id: "VS",
      collection_title: "Vital Signs (CDASH VS)",
      planned_activity_id: "visit-baseline",
      collected_at: "2026-08-01T10:00:00Z",
      opened_by: "usr-crc-01",
      opened_at: "2026-08-01T09:55:00Z",
      version: 1,
      content_hash: "hash-open-form",
      can_sign: false,
      open_query_count: 0,
      answered_query_count: 0,
      open_error_finding_count: 0,
      complete: false,
      missing_required: [],
      items: [
        {
          item_id: "SYSBP",
          concept: "Systolic BP",
          value_kind: "Quantity",
          unit: "mm[Hg]",
          required: true,
          value: {
            type: "quantity",
            value: "128",
            unit: "mm[Hg]",
            comparator: null,
          },
          observation_id: "01950000-0000-7000-8000-000000003001", // deid-ignore
          revision: 1,
          recorded_by: "usr-crc-01",
          recorded_at: "2026-08-01T10:00:00Z",
        },
      ],
      locks: [],
      assertions: [],
      signatures: [],
    });
    vi.mocked(ecrfFormsApi.previewValues).mockResolvedValue({
      form_id: openFormId,
      warnings: [],
    });
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);

    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.accessToken = "demo-token-001";
    auth.rawRoles = ["crc", "data_manager"];

    const confirmStore = useConfirmStore();
    const canvasRouter = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
        },
        { path: "/ecrf", name: "ecrf", component: RouteStub },
      ],
    });
    await canvasRouter.push(
      `/studies/CADENCE-101?stage=data&section=validation&site=SITE-101&participant=SUBJ-1001&form=collection_instance:${openFormId}&item=SYSBP`
    );
    await canvasRouter.isReady();

    const wrapper = mount(
      defineComponent({
        render: () => h(RouterView),
      }),
      {
        global: { plugins: [pinia, canvasRouter] },
      }
    );
    await flushPromises();

    const sysbpInput = wrapper.find("#ecrf-item-SYSBP");
    expect(sysbpInput.exists()).toBe(true);
    await sysbpInput.setValue("134");
    await flushPromises();

    // Navigating to a different finding's form triggers Unsaved Form Changes confirmation
    const navPromise = canvasRouter.push(
      `/studies/CADENCE-101?stage=data&section=validation&site=SITE-102&participant=SUBJ-1002&form=collection_instance:${otherFormId}&item=item-hr`
    );
    await flushPromises();
    const getActiveConfirmTitle = () =>
      (confirmStore.activeRequest as { title?: string } | null)?.title;
    expect(getActiveConfirmTitle()).toBe("Unsaved Form Changes");

    // Rejecting stays on the open dirty form
    confirmStore.cancelConfirm();
    await navPromise.catch(() => {});
    await flushPromises();
    expect(canvasRouter.currentRoute.value.query.form).toBe(
      `collection_instance:${openFormId}`
    );

    // Confirming allows leaving to the target finding form
    const secondNav = canvasRouter.push(
      `/studies/CADENCE-101?stage=data&section=validation&site=SITE-102&participant=SUBJ-1002&form=collection_instance:${otherFormId}&item=item-hr`
    );
    await flushPromises();
    expect(getActiveConfirmTitle()).toBe("Unsaved Form Changes");
    confirmStore.resolveConfirm();
    await secondNav;
    await flushPromises();
    expect(canvasRouter.currentRoute.value.query.form).toBe(
      `collection_instance:${otherFormId}`
    );
  });

  describe("MSW validation and query endpoints end-to-end", () => {
    let mswStore = createWebDemoStore();
    let mswServer = setupServer(
      ...buildWebGatewayHandlers({ forceDemo: true, store: mswStore })
    );

    beforeEach(() => {
      mswStore = createWebDemoStore();
      mswServer = setupServer(
        ...buildWebGatewayHandlers({ forceDemo: true, store: mswStore })
      );
      mswServer.listen({ onUnhandledFrame: "error" });
    });

    afterEach(() => {
      mswServer.resetHandlers();
      mswServer.close();
    });

    it("drives end-to-end MSW validation and query handlers for populated findings, rule-pack preview, approval, pinning, decisions, and bidirectional finding-query links", async () => {
      // Delegate mocked API facades to real fetch-backed implementations against MSW
      vi.mocked(kernelApi.search).mockImplementation(
        kernelNavigationApi.search
      );
      vi.mocked(kernelApi.listStudyRulePacks).mockImplementation(
        kernelValidationApi.listStudyRulePacks
      );
      vi.mocked(kernelApi.previewStudyRulePack).mockImplementation(
        kernelValidationApi.previewStudyRulePack
      );
      vi.mocked(kernelApi.declareStudyRulePacks).mockImplementation(
        kernelValidationApi.declareStudyRulePacks
      );
      vi.mocked(kernelApi.listStudyValidationFindings).mockImplementation(
        kernelValidationApi.listStudyValidationFindings
      );
      vi.mocked(kernelApi.getValidationFinding).mockImplementation(
        kernelValidationApi.getValidationFinding
      );
      vi.mocked(ecrfQueriesApi.listForStudy).mockImplementation(
        async (studyId) => {
          const resp = await fetch(
            `http://localhost:8000/api/v1/execution/forms/queries?study_id=${encodeURIComponent(studyId)}`,
            {
              headers: { Authorization: "Bearer demo-token-001" },
            }
          );
          return resp.json();
        }
      );

      const auth = useAuthStore();
      auth.isAuthenticated = true;
      auth.accessToken = "demo-token-001";
      auth.rawRoles = ["data_manager"];

      const router = makeRouter();
      await router.push(
        "/studies/DEMO-01?stage=data&section=validation&site=SITE-102&participant=SUBJ-1002"
      );
      await router.isReady();

      // 1. Mount ValidationSection against MSW and verify populated hydrated findings + bidirectional query link
      const validationWrapper = mount(ValidationSection, {
        props: {
          studyCode: "DEMO-01",
          site: "SITE-102",
          participant: "SUBJ-1002",
          canManageRules: true,
        },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      expect(validationWrapper.text()).toContain(
        "Cardiovascular Vital Signs Edit Checks"
      );
      expect(validationWrapper.text()).toContain(
        "Hemodynamic Pulse Pressure Plausibility Checks"
      );
      expect(validationWrapper.text()).toContain("RULE-HR-RANGE");
      expect(validationWrapper.text()).toContain(
        "SITE-102 · Lakeshore Medical Center"
      );
      expect(validationWrapper.text()).toContain("Participant SUBJ-1002");
      expect(validationWrapper.text()).toContain(
        "Open system query on Resting Heart Rate"
      );

      // 2. Preview Draft rule pack via MSW and verify hydrated violations
      await validationWrapper
        .find("#btn-preview-pack-cadence-hemodynamic-checks-1")
        .trigger("click");
      await flushPromises();
      const previewBox = validationWrapper.find(
        "[data-testid='pack-preview-cadence-hemodynamic-checks']"
      );
      expect(previewBox.exists()).toBe(true);
      expect(previewBox.text()).toContain("VS_PULSE_PRESSURE_WIDE");
      expect(previewBox.text()).toContain(
        "SITE-101 · Riverside University Hospital"
      );

      // 3. Verify step-up signature verification + Draft rule pack approval + standards pinning via MSW
      const rulesBefore =
        await kernelValidationApi.listStudyRulePacks("DEMO-01");
      const draftPack = rulesBefore.rule_packs.find(
        (p) => p.status === "Draft"
      );
      expect(draftPack).toBeTruthy();

      const sigResp = await fetch(
        "http://localhost:8000/api/v1/auth/signature-verification",
        {
          method: "POST",
          headers: {
            Authorization: "Bearer demo-token-001",
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            password: "demo-password", // pragma: allowlist secret
            reason: "Approve hemodynamic edit checks for study use",
            action: rulePackApproveAction(draftPack!.ref),
            semantic_action: RULE_PACK_APPROVE_SEMANTIC_ACTION,
          }),
        }
      );
      expect(sigResp.status).toBe(200);
      const { sig_token: approveToken } = await sigResp.json();

      const approvedPack = await kernelValidationApi.approveLibraryItem({
        itemRef: draftPack!.ref,
        sigToken: approveToken,
        reason: "Approve hemodynamic edit checks for study use",
        idempotencyKey: "idem-approve-pack-1",
      });
      expect(approvedPack.status).toBe("Approved");

      const updatedPinList = await kernelValidationApi.declareStudyRulePacks({
        studyRef: rulesBefore.study_ref,
        rulePackRefs: [
          ...rulesBefore.pinned_rule_pack_refs,
          approvedPack.ref.split("@")[0]!,
        ],
        reason: "Pin newly approved hemodynamic checks",
        idempotencyKey: "idem-pin-packs-1",
      });
      expect(updatedPinList.pinned_rule_pack_refs.length).toBe(2);

      // 4. Verify finding lifecycle transitions (investigate -> classify -> accept -> reopen -> explain -> reopen) via MSW
      const findingsPage =
        await kernelValidationApi.listStudyValidationFindings(
          rulesBefore.study_ref
        );
      const openFinding = findingsPage.findings.find(
        (f) => f.status === "Open"
      )!;
      expect(openFinding).toBeTruthy();

      const investigating =
        await kernelValidationApi.investigateValidationFinding({
          findingRef: openFinding.ref,
          reason: "Open data management review",
          idempotencyKey: "idem-inv-1",
        });
      expect(investigating.status).toBe("Investigating");

      const classified = await kernelValidationApi.classifyValidationFinding({
        findingRef: openFinding.ref,
        classification: "DataIssue",
        reason: "Confirmed bedside tachycardia",
        idempotencyKey: "idem-cls-1",
      });
      expect(classified.classification).toBe("DataIssue");

      const acceptSigResp = await fetch(
        "http://localhost:8000/api/v1/auth/signature-verification",
        {
          method: "POST",
          headers: {
            Authorization: "Bearer demo-token-001",
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            password: "demo-password", // pragma: allowlist secret
            reason: "Accept documented protocol deviation",
            action: findingAcceptAction(openFinding.ref),
            semantic_action: FINDING_ACCEPT_SEMANTIC_ACTION,
          }),
        }
      );
      expect(acceptSigResp.status).toBe(200);
      const { sig_token: acceptToken } = await acceptSigResp.json();
      const accepted = await kernelValidationApi.acceptValidationFinding({
        findingRef: openFinding.ref,
        rationale: "Accepted per medical monitor review.",
        sigToken: acceptToken,
        reason: "Accept documented protocol deviation",
        idempotencyKey: "idem-acc-1",
      });
      expect(accepted.status).toBe("Accepted");
      expect(accepted.decision?.text).toBe(
        "Accepted per medical monitor review."
      );

      const reopenedAfterAccept =
        await kernelValidationApi.reopenValidationFinding({
          findingRef: openFinding.ref,
          reason: "Re-open for ECG explanation",
          idempotencyKey: "idem-reo-1",
        });
      expect(reopenedAfterAccept.status).toBe("Open");

      const explainSigResp = await fetch(
        "http://localhost:8000/api/v1/auth/signature-verification",
        {
          method: "POST",
          headers: {
            Authorization: "Bearer demo-token-001",
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            password: "demo-password", // pragma: allowlist secret
            reason: "Document transient sinus tachycardia",
            action: findingExplainAction(openFinding.ref),
            semantic_action: FINDING_EXPLAIN_SEMANTIC_ACTION,
          }),
        }
      );
      expect(explainSigResp.status).toBe(200);
      const { sig_token: explainToken } = await explainSigResp.json();
      const explained = await kernelValidationApi.explainValidationFinding({
        findingRef: openFinding.ref,
        explanation: "Verified against bedside ECG strip.",
        sigToken: explainToken,
        reason: "Document transient sinus tachycardia",
        idempotencyKey: "idem-exp-1",
      });
      expect(explained.status).toBe("Explained");

      const detail = await kernelValidationApi.getValidationFinding(
        openFinding.ref
      );
      expect(detail.explanation.steps.length).toBeGreaterThanOrEqual(2);

      const reopened = await kernelValidationApi.reopenValidationFinding({
        findingRef: openFinding.ref,
        reason: "Re-open after source amendment",
        idempotencyKey: "idem-reo-2",
      });
      expect(reopened.status).toBe("Open");

      // 5. Mount QueriesSection against MSW and verify reverse Query -> Finding & RulePack deep links
      await router.push(
        "/studies/DEMO-01?stage=data&section=queries&site=SITE-102&participant=SUBJ-1002"
      );
      const queriesWrapper = mount(QueriesSection, {
        props: {
          studyCode: "DEMO-01",
          site: "SITE-102",
          participantScope: "SUBJ-1002",
          canManage: true,
          canAnswer: true,
        },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const systemQueryId = "00000000-0000-0000-0000-000000003003"; // deid-ignore
      await vi.waitFor(() => {
        expect(
          queriesWrapper
            .find(`[data-testid='query-finding-link-${systemQueryId}'] a`)
            .exists()
        ).toBe(true);
      });
      const findingLinkOnQuery = queriesWrapper.find(
        `[data-testid='query-finding-link-${systemQueryId}'] a`
      );
      expect(findingLinkOnQuery.exists()).toBe(true);
      expect(findingLinkOnQuery.attributes("href")).toContain(
        "section=validation"
      );
      expect(findingLinkOnQuery.attributes("href")).toContain("site=SITE-102");
      expect(findingLinkOnQuery.attributes("href")).toContain(
        "participant=SUBJ-1002"
      );

      const rulePackLinkOnQuery = queriesWrapper.find(
        `[data-testid='query-rule-pack-link-${systemQueryId}'] a`
      );
      expect(rulePackLinkOnQuery.exists()).toBe(true);
      expect(rulePackLinkOnQuery.attributes("href")).toContain(
        "pack=cadence-vital-signs-checks"
      );
    });
  });
});
