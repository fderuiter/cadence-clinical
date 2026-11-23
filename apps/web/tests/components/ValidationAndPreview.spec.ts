import { describe, it, expect, vi, beforeEach } from "vitest";
import { defineComponent, h } from "vue";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createMemoryHistory } from "vue-router";
import EcrfFormPanel from "../../src/components/ecrf/EcrfFormPanel.vue";
import QueriesSection from "../../src/components/canvas/QueriesSection.vue";
import ValidationSection from "../../src/components/canvas/ValidationSection.vue";
import ObservationView from "../../src/views/ObservationView.vue";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { useAuthStore } from "../../src/stores/auth";

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
      study_site_id: "01950000-0000-7000-8000-000000000011",
      protocol_version_id: "01950000-0000-7000-8000-000000000012",
      enrollment_id: "01950000-0000-7000-8000-000000000020",
      participant_id: "01950000-0000-7000-8000-000000000030",
      collection_spec_id: "VS",
      collection_title: "Vital signs",
      planned_activity_id: "V1",
      collected_at: "2026-03-01T10:00:00Z",
      opened_by: "crc.user",
      opened_at: "2026-03-01T09:55:00Z",
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
    });
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
