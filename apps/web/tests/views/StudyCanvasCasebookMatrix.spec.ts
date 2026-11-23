import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { ecrfFormsApi, toFormSummaryCount } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { findStudyScheduleOptions } from "../../src/canvas/kernelStudy";
import { canCorrectObservations } from "../../src/canvas/authority";
import { useAuthStore } from "../../src/stores/auth";
import ConfirmationDialogHost from "../../src/components/ConfirmationDialogHost.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import ObservationView from "../../src/views/ObservationView.vue";

const ISO_T0 = new Date(Date.UTC(2026, 9, 15, 9, 0, 0)).toISOString();
const ISO_T1 = new Date(Date.UTC(2026, 9, 15, 9, 30, 0)).toISOString();

const PARTICIPANT_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000101";
const ENROLLMENT_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000e101";
const PROTOCOL_VERSION_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000b101";
const FORM_COMPLETE_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f101";
const FORM_REPEAT_1_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f102";
const FORM_REPEAT_2_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f103";
const FORM_UNSCHED_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f104";
const OBSERVATION_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000a101";

const STUDY_REF = "study:0192f3c4-5e6a-7b8c-9d0e-000000009901";
const SITE_REF = "study_site:0192f3c4-5e6a-7b8c-9d0e-000000009902";
const VERSION_REF = `protocol_version:${PROTOCOL_VERSION_ID}`;

const PARTICIPANT = {
  id: PARTICIPANT_ID,
  subject_id: "SUBJ-101",
  study_id: "CADENCE-101",
  site_id: "SITE-101",
  status: "ACTIVE",
  active_protocol_version: "1.0",
  enrollment_index: 1,
};

function makeChild(
  kind: string,
  ref: string,
  code: string,
  title: string,
  status: string | null = null
) {
  return {
    node: { ref, kind, title, code, status, mask: null },
    child_count: 0,
    group: null,
  };
}

function makeSoaResponse() {
  return {
    version: VERSION_REF,
    content_hash: "sha256:soa",
    hash: "sha256:soa",
    title: "CADENCE-101 Protocol v1.0",
    epochs: [
      {
        epoch_id: "epoch.screening",
        name: "Screening",
        order: 0,
        first_column: 0,
        column_count: 1,
      },
      {
        epoch_id: "epoch.treatment",
        name: "Treatment",
        order: 1,
        first_column: 1,
        column_count: 2,
      },
    ],
    columns: [
      {
        activity_id: "pa.screening",
        name: "Screening",
        epoch_id: "epoch.screening",
        study_day: -14,
        window_before: 7,
        window_after: 0,
        locations: [],
        footnotes: [],
      },
      {
        activity_id: "pa.baseline",
        name: "Baseline",
        epoch_id: "epoch.treatment",
        study_day: 1,
        window_before: 0,
        window_after: 1,
        locations: [],
        footnotes: [],
      },
      {
        activity_id: "pa.week4",
        name: "Week 4",
        epoch_id: "epoch.treatment",
        study_day: 28,
        window_before: 3,
        window_after: 3,
        locations: [],
        footnotes: [],
      },
    ],
    rows: [
      {
        collection_spec_id: "cs.vitals",
        name: "Vital Signs",
        item_ids: ["systolic_bp", "diastolic_bp"],
        concepts: ["concept:systolic_bp@1", "concept:diastolic_bp@1"],
        columns: [0, 1, 2],
        frequency: 3,
        footnotes: [],
      },
      {
        collection_spec_id: "cs.physical_exam",
        name: "Physical Examination",
        item_ids: ["pe_general"],
        concepts: ["concept:pe_general@1"],
        columns: [0, 2],
        frequency: 2,
        footnotes: [],
      },
      {
        collection_spec_id: "cs.adverse_event",
        name: "Adverse Event",
        item_ids: ["ae_term"],
        concepts: ["concept:ae_term@1"],
        columns: [],
        frequency: 0,
        footnotes: [],
      },
    ],
    footnotes: [],
  };
}

function makeFormSummary(overrides: Record<string, unknown> = {}) {
  return {
    form_id: FORM_COMPLETE_ID,
    enrollment_id: ENROLLMENT_ID,
    protocol_version_id: PROTOCOL_VERSION_ID,
    collection_spec_id: "cs.vitals",
    collection_title: "Vital Signs",
    planned_activity_id: "pa.screening",
    collected_at: ISO_T0,
    opened_at: ISO_T0,
    opened_by: "crc@cadence.test",
    complete: true,
    item_count: 2,
    recorded_count: 2,
    open_query_count: 0,
    answered_query_count: 0,
    verified_count: 2,
    unverified_count: 0,
    locked: true,
    frozen: false,
    lock_count: 1,
    signed: true,
    signature_superseded: false,
    signature_count: 1,
    version: 1,
    ...overrides,
  };
}

function makeFormDetail(overrides: Record<string, unknown> = {}) {
  return {
    form_id: FORM_REPEAT_1_ID,
    enrollment_id: ENROLLMENT_ID,
    participant_id: PARTICIPANT_ID,
    protocol_version_id: PROTOCOL_VERSION_ID,
    collection_spec_id: "cs.vitals",
    collection_title: "Vital Signs",
    planned_activity_id: "pa.baseline",
    collected_at: ISO_T0,
    opened_at: ISO_T0,
    opened_by: "crc@cadence.test",
    complete: false,
    missing_required: ["diastolic_bp"],
    content_hash: "sha256:demo",
    version: 1,
    locks: [],
    assertions: [],
    signatures: [],
    items: [
      {
        item_id: "systolic_bp",
        concept: "concept:systolic_bp@1",
        unit: "mm[Hg]",
        value_kind: "quantity",
        required: true,
        value: {
          type: "quantity",
          value: 122,
          unit: "mm[Hg]",
          comparator: null,
        },
        observation_id: OBSERVATION_ID,
        revision: 1,
        recorded_by: "crc@cadence.test",
        recorded_at: ISO_T1,
      },
      {
        item_id: "diastolic_bp",
        concept: "concept:diastolic_bp@1",
        unit: "mm[Hg]",
        value_kind: "quantity",
        required: true,
        value: null,
        observation_id: null,
        revision: null,
        recorded_by: null,
        recorded_at: null,
      },
    ],
    ...overrides,
  };
}

function mockKernelProtocolAndStudy() {
  vi.spyOn(kernelApi, "search").mockResolvedValue([
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
  vi.spyOn(kernelApi, "listProtocolVersions").mockResolvedValue([
    {
      ref: VERSION_REF,
      study: STUDY_REF,
      number: 1,
      parent_version: null,
      status: "Approved",
      content_hash: "sha256:v1",
      amendment_intent: null,
      approval_attestation: null,
      withdrawal_reason: null,
      mask: null,
    },
  ]);
  vi.spyOn(kernelApi, "getScheduleOfActivities").mockResolvedValue(
    makeSoaResponse()
  );
  vi.spyOn(kernelApi, "getChildren").mockImplementation(async (ref) => {
    if (ref === STUDY_REF) {
      return [
        makeChild("study_site", SITE_REF, "SITE-101", "SITE-101", "Active"),
      ];
    }
    if (ref === SITE_REF) {
      return [
        makeChild(
          "enrollment",
          `enrollment:${ENROLLMENT_ID}@1`,
          "SUBJ-101",
          "SUBJ-101",
          "Screened"
        ),
      ];
    }
    if (ref === VERSION_REF) {
      return [
        makeChild(
          "collection_spec",
          "collection_spec:1",
          "cs.vitals",
          "Vital Signs",
          "Scheduled"
        ),
        makeChild(
          "collection_spec",
          "collection_spec:2",
          "cs.physical_exam",
          "Physical Examination",
          "Scheduled"
        ),
        makeChild(
          "collection_spec",
          "collection_spec:3",
          "cs.adverse_event",
          "Adverse Event",
          "Unscheduled"
        ),
        makeChild(
          "planned_activity",
          "planned_activity:1",
          "pa.screening",
          "Screening"
        ),
        makeChild(
          "planned_activity",
          "planned_activity:2",
          "pa.baseline",
          "Baseline"
        ),
        makeChild(
          "planned_activity",
          "planned_activity:3",
          "pa.week4",
          "Week 4"
        ),
      ];
    }
    return [];
  });
}

describe("Casebook matrix, inline form workspace, context handoff, and monitor read-only observation view", () => {
  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
    vi.restoreAllMocks();
  });

  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it("normalizes summary count helpers and maps Schedule of Activities into matrix epochs, columns, scheduled rows, and unscheduled rows", async () => {
    expect(toFormSummaryCount(3)).toBe(3);
    expect(toFormSummaryCount(-2)).toBe(0);
    expect(toFormSummaryCount(undefined)).toBe(0);

    mockKernelProtocolAndStudy();

    const mapped = await findStudyScheduleOptions("CADENCE-101");
    expect(mapped).not.toBeNull();
    expect(mapped?.epochs).toHaveLength(2);
    expect(mapped?.columns.map((c) => c.activityId)).toEqual([
      "pa.screening",
      "pa.baseline",
      "pa.week4",
    ]);
    expect(mapped?.scheduledRows.map((r) => r.collectionSpecId)).toEqual([
      "cs.vitals",
      "cs.physical_exam",
    ]);
    expect(mapped?.unscheduledRows.map((r) => r.collectionSpecId)).toEqual([
      "cs.adverse_event",
    ]);
  });

  async function mountParticipantCasebook() {
    const pinia = createPinia();
    setActivePinia(pinia);
    const auth = useAuthStore();
    auth.setAuth({
      authenticated: true,
      token: "token-crc-1",
      tokenParsed: {
        sub: "crc-1",
        tenant_id: "tenant-1",
        realm_access: { roles: ["crc"] },
      },
    });

    mockKernelProtocolAndStudy();
    vi.spyOn(kernelApi, "listProposals").mockResolvedValue([]);
    vi.spyOn(executionApi, "listSubjects").mockResolvedValue({
      items: [PARTICIPANT],
      total: 1,
      limit: 50,
      offset: 0,
    } as never);

    vi.spyOn(ecrfFormsApi, "listForEnrollment").mockResolvedValue([
      makeFormSummary(),
      makeFormSummary({
        form_id: FORM_REPEAT_1_ID,
        planned_activity_id: "pa.baseline",
        complete: false,
        item_count: 2,
        recorded_count: 1,
        open_query_count: 1,
        answered_query_count: 1,
        verified_count: 1,
        unverified_count: 1,
        locked: false,
        frozen: false,
        lock_count: 0,
        signed: false,
        signature_superseded: true,
        signature_count: 1,
      }),
      makeFormSummary({
        form_id: FORM_REPEAT_2_ID,
        planned_activity_id: "pa.baseline",
        complete: true,
        item_count: 2,
        recorded_count: 2,
        open_query_count: 0,
        answered_query_count: 0,
        verified_count: 0,
        unverified_count: 2,
        locked: false,
        frozen: false,
        lock_count: 0,
        signed: false,
        signature_superseded: false,
        signature_count: 0,
      }),
      makeFormSummary({
        form_id: FORM_UNSCHED_ID,
        collection_spec_id: "cs.adverse_event",
        collection_title: "Adverse Event",
        planned_activity_id: null,
        complete: true,
        item_count: 1,
        recorded_count: 1,
        verified_count: 1,
        unverified_count: 0,
        locked: false,
        frozen: true,
        lock_count: 1,
        signed: false,
        signature_superseded: false,
        signature_count: 0,
      }),
    ] as never);
    vi.spyOn(ecrfQueriesApi, "listForEnrollment").mockResolvedValue([]);
    vi.spyOn(ecrfQueriesApi, "listForForm").mockResolvedValue([]);
    vi.spyOn(ecrfFormsApi, "getForm").mockResolvedValue(
      makeFormDetail() as never
    );

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
        },
        {
          path: "/ecrf",
          name: "ecrf",
          component: {
            template: "<div id='ecrf-standalone-stub'>Standalone</div>",
          },
        },
      ],
    });

    await router.push("/studies/CADENCE-101?section=participants");
    await router.isReady();
    const wrapper = mount(
      {
        components: { ConfirmationDialogHost },
        template: "<router-view /><ConfirmationDialogHost />",
      },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();

    await wrapper.find("#participant-SUBJ-101").trigger("click");
    await flushPromises();
    await wrapper.find("#inspector-open-record").trigger("click");
    await flushPromises();

    return { wrapper, router };
  }

  it("renders the interactive casebook matrix with live status rollups, repeating instances, unscheduled forms, context handoffs, and zero axe violations", async () => {
    const { wrapper } = await mountParticipantCasebook();

    const matrix = wrapper.find("#casebook-matrix");
    expect(matrix.exists()).toBe(true);
    expect(matrix.text()).toContain("Screening");
    expect(matrix.text()).toContain("Treatment");
    expect(matrix.text()).toContain("Day -14");
    expect(matrix.text()).toContain("Day 1");
    expect(matrix.text()).toContain("Day 28");

    const screeningCell = wrapper.find('[data-cell="cs.vitals:pa.screening"]');
    expect(screeningCell.text()).toContain("Complete");
    expect(screeningCell.text()).toContain("SDV verified");
    expect(screeningCell.text()).toContain("Locked");
    expect(screeningCell.text()).toContain("Signed");
    expect(
      screeningCell.find('[id="matrix-repeat-cs.vitals-pa.screening"]').exists()
    ).toBe(false);

    const baselineCell = wrapper.find('[data-cell="cs.vitals:pa.baseline"]');
    expect(
      baselineCell.find(`[id="record-form-link-${FORM_REPEAT_1_ID}"]`).exists()
    ).toBe(true);
    expect(
      baselineCell.find(`[id="record-form-link-${FORM_REPEAT_2_ID}"]`).exists()
    ).toBe(true);
    expect(baselineCell.text()).toContain("#1");
    expect(baselineCell.text()).toContain("#2");
    expect(baselineCell.text()).toContain("1 open");
    expect(baselineCell.text()).toContain("1 answered");
    expect(baselineCell.text()).toContain("Signature superseded");
    expect(
      baselineCell.find('[id="matrix-repeat-cs.vitals-pa.baseline"]').exists()
    ).toBe(true);

    const week4Cell = wrapper.find('[data-cell="cs.vitals:pa.week4"]');
    expect(week4Cell.text()).toContain("Not started");

    const unschedSection = wrapper.find("#casebook-unscheduled");
    expect(unschedSection.exists()).toBe(true);
    expect(unschedSection.text()).toContain("Adverse Event");
    expect(unschedSection.text()).toContain("Frozen");

    const recordHandoff = wrapper.find("#record-handoff-standalone");
    expect(recordHandoff.exists()).toBe(true);
    expect(recordHandoff.attributes("href")).toContain("/ecrf?");
    expect(recordHandoff.attributes("href")).toContain("studyId=CADENCE-101");
    expect(recordHandoff.attributes("href")).toContain("siteId=SITE-101");
    expect(recordHandoff.attributes("href")).toContain("subjectId=SUBJ-101");
    expect(recordHandoff.attributes("href")).toContain(
      `enrollmentId=${ENROLLMENT_ID}`
    );
    const inspectorHandoff = wrapper.find("#inspector-handoff-standalone");
    expect(inspectorHandoff.exists()).toBe(true);
    expect(inspectorHandoff.attributes("href")).toContain(
      `enrollmentId=${ENROLLMENT_ID}`
    );

    await expect(wrapper.element).toBeAccessible();
    wrapper.unmount();
  });

  it("opens unscheduled and existing forms inline with unsaved-entry guard, draft restoration, shared schedule caching, and zero axe violations", async () => {
    const { wrapper, router } = await mountParticipantCasebook();

    await wrapper
      .find('[id="unscheduled-open-cs.adverse_event"]')
      .trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.query).toMatchObject({
      stage: "conduct",
      participant: `participant:${PARTICIPANT_ID}`,
      form: "new",
      spec: "cs.adverse_event",
    });
    expect(wrapper.find("#canvas-navigator").exists()).toBe(true);
    expect(wrapper.find("#canvas-inspector").exists()).toBe(true);
    expect(wrapper.find("#zoom-form").text()).toBe("Forms · cs.adverse_event");
    const visitSelect = wrapper.find("#ecrf-planned-activity");
    expect(visitSelect.exists()).toBe(true);
    expect(visitSelect.element.tagName).toBe("SELECT");

    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        participant: `participant:${PARTICIPANT_ID}`,
        form: `collection_instance:${FORM_REPEAT_1_ID}`,
      },
    });
    await flushPromises();

    expect(wrapper.find("#zoom-form").text()).toBe("Vital Signs · pa.baseline");
    const workspaceHandoff = wrapper.find("#workspace-handoff-standalone");
    expect(workspaceHandoff.exists()).toBe(true);
    expect(workspaceHandoff.attributes("href")).toContain(
      `formId=${FORM_REPEAT_1_ID}`
    );
    expect(workspaceHandoff.attributes("href")).toContain(
      "collectionSpecId=cs.vitals"
    );
    expect(workspaceHandoff.attributes("href")).toContain(
      "plannedActivityId=pa.baseline"
    );

    await expect(wrapper.element).toBeAccessible();

    await wrapper.find("#ecrf-item-diastolic_bp").setValue("80");
    await flushPromises();

    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValueOnce(false);
    const backBtn = wrapper.find("#canvas-form-back").element as HTMLElement;
    backBtn.focus();
    expect(document.activeElement).toBe(backBtn);

    await wrapper.find("#canvas-form-back").trigger("click");
    await flushPromises();
    expect(confirmSpy).not.toHaveBeenCalled();

    const alertDialog = document.querySelector(
      '.clinical-modal-dialog[role="alertdialog"]'
    );
    expect(alertDialog).not.toBeNull();
    await expect(document.body).toBeAccessible();
    (alertDialog?.querySelector(".btn-cancel") as HTMLElement).click();
    await flushPromises();

    expect(document.activeElement).toBe(backBtn);
    expect(router.currentRoute.value.query.form).toBe(
      `collection_instance:${FORM_REPEAT_1_ID}`
    );

    const unloadEvent = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(unloadEvent);
    expect(unloadEvent.defaultPrevented).toBe(true);

    await wrapper.find("#canvas-form-back").trigger("click");
    await flushPromises();
    expect(confirmSpy).not.toHaveBeenCalled();
    (
      document.querySelector(
        '.clinical-modal-dialog[role="alertdialog"] .btn-confirm'
      ) as HTMLElement
    ).click();
    await flushPromises();
    expect(router.currentRoute.value.query.form).toBeUndefined();
    expect(kernelApi.getScheduleOfActivities).toHaveBeenCalledTimes(1);

    await wrapper
      .find(`[id="record-form-link-${FORM_REPEAT_1_ID}"]`)
      .trigger("click");
    await flushPromises();
    expect(
      (wrapper.find("#ecrf-item-diastolic_bp").element as HTMLInputElement)
        .value
    ).toBe("80");

    wrapper.unmount();
  });

  it("permits monitors and auditors to inspect observation history read-only with back navigation in standalone and embedded modes", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const auth = useAuthStore();
    auth.isAuthenticated = true;

    for (const role of ["cra", "monitor", "auditor", "tmf_auditor"]) {
      auth.rawRoles = [role];
      expect(canCorrectObservations(auth)).toBe(false);
    }
    auth.rawRoles = ["crc"];
    expect(canCorrectObservations(auth)).toBe(true);
    auth.rawRoles = ["cra"];

    vi.spyOn(kernelApi, "getObservation").mockResolvedValue({
      ref: `observation:${OBSERVATION_ID}@2`,
      enrollment_ref: `enrollment:${ENROLLMENT_ID}`,
      concept: "concept:systolic_bp@1",
      value: {
        type: "quantity",
        value: 124,
        unit: "mm[Hg]",
        comparator: null,
      },
      observed_at: ISO_T0,
      version: 2,
      locks: [],
      revisions: [
        {
          number: 2,
          value: {
            type: "quantity",
            value: 124,
            unit: "mm[Hg]",
            comparator: null,
          },
          reason_for_change: "Typographical Error: Source chart re-checked",
          recorded_by: "crc@cadence.test",
          recorded_at: ISO_T1,
          recorded_until: null,
        },
        {
          number: 1,
          value: {
            type: "quantity",
            value: 142,
            unit: "mm[Hg]",
            comparator: null,
          },
          reason_for_change: "Initial entry",
          recorded_by: "crc@cadence.test",
          recorded_at: ISO_T0,
          recorded_until: ISO_T1,
        },
      ],
      assertions: [],
      audit: [
        {
          sequence: 2,
          recorded_at: ISO_T1,
          actor_id: "crc@cadence.test",
          action: "Updated",
          subject: "Observation",
          version: 2,
          reason_for_change: "Typographical Error: Source chart re-checked",
        },
      ],
    } as never);
    vi.spyOn(kernelApi, "getZoomPath").mockResolvedValue([
      {
        level: "study",
        node: null,
        label: null,
        mask: { kind: "study", reason: "unauthorized" },
      },
    ] as never);

    const { router: appRouter } = await import("../../src/router/index.js");
    const obsRoute = appRouter
      .getRoutes()
      .find((r) => r.path === "/observations/:observationRef");
    expect(obsRoute?.meta?.requiresRole).toEqual(
      expect.arrayContaining([
        "cra",
        "monitor",
        "auditor",
        "tmf_auditor",
        "sponsor_admin",
      ])
    );

    const testRouter = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
        {
          path: "/observations/:observationRef",
          name: "observation",
          component: ObservationView,
          props: true,
        },
      ],
    });
    await testRouter.push(`/observations/observation:${OBSERVATION_ID}`);
    await testRouter.isReady();

    const standaloneWrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, testRouter] }, attachTo: document.body }
    );
    await flushPromises();

    expect(standaloneWrapper.find("form.observation-correction").exists()).toBe(
      false
    );
    expect(
      standaloneWrapper.find("[data-testid=observation-status]").text()
    ).toContain("Read-only");
    expect(
      standaloneWrapper.find("[data-testid=observation-audit]").text()
    ).toContain("Source chart re-checked");
    expect(standaloneWrapper.find("#observation-back").exists()).toBe(true);

    await expect(standaloneWrapper.element).toBeAccessible();
    standaloneWrapper.unmount();
  });
});
