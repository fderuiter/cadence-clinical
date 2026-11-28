import { defineComponent, h, nextTick } from "vue";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import axe from "axe-core";

vi.mock("../../src/auth/keycloak.js", () => ({
  getKeycloakClient: () => null,
}));

const STUDY_ID = "01960000-0000-7000-8000-000000000501";
const STUDY_REF = `study:${STUDY_ID}`;
const SITE_ID = "01960000-0000-7000-8000-000000000502";
const ENROLLMENT_ID = "01960000-0000-7000-8000-000000000503";
const ENROLLMENT_REF = `enrollment:${ENROLLMENT_ID}`;
const FORM_ID = "01960000-0000-7000-8000-000000000504";
const FORM_REF = `collection_instance:${FORM_ID}`;
const OBS_CLEAN_ID = "01960000-0000-7000-8000-000000000505";
const OBS_CLEAN_REF = `observation:${OBS_CLEAN_ID}`;
const OBS_QUERIED_ID = "01960000-0000-7000-8000-000000000506";
const OBS_QUERIED_REF = `observation:${OBS_QUERIED_ID}`;
const OBS_NON_SDV_ID = "01960000-0000-7000-8000-000000000507";
const OBS_STANDALONE_ID = "01960000-0000-7000-8000-000000000508";
const OBS_STANDALONE_REF = `observation:${OBS_STANDALONE_ID}`;
const QUERY_ID = "01960000-0000-7000-8000-000000000509";
const QUERY_REF = `assertion:${QUERY_ID}`;
const FINDING_ID = "01960000-0000-7000-8000-000000000510";
const FINDING_REF = `validation_finding:${FINDING_ID}`;
const VERIFY_ASSERTION_ID = "01960000-0000-7000-8000-000000000511";
const REVIEW_ASSERTION_ID = "01960000-0000-7000-8000-000000000512";

const mockCanI = vi.fn();
const mockSearch = vi.fn();
const mockGetChildren = vi.fn();
const mockGetNode = vi.fn();
const mockGetZoomPath = vi.fn();
const mockListStudyLocks = vi.fn();
const mockGetStudyLockReadiness = vi.fn();
const mockGetStudyAttention = vi.fn();
const mockListWorkQueues = vi.fn();
const mockGetObservation = vi.fn();
const mockVerifyObservation = vi.fn();
const mockReviewObservation = vi.fn();
const mockRetractAssertion = vi.fn();

vi.mock("../../src/api/kernelApi", async () => {
  const actual = await vi.importActual<
    typeof import("../../src/api/kernelApi")
  >("../../src/api/kernelApi");
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      canI: (...args: unknown[]) => mockCanI(...args),
      search: (...args: unknown[]) => mockSearch(...args),
      getChildren: (...args: unknown[]) => mockGetChildren(...args),
      getNode: (...args: unknown[]) => mockGetNode(...args),
      getZoomPath: (...args: unknown[]) => mockGetZoomPath(...args),
      listStudyLocks: (...args: unknown[]) => mockListStudyLocks(...args),
      getStudyLockReadiness: (...args: unknown[]) =>
        mockGetStudyLockReadiness(...args),
      getStudyAttention: (...args: unknown[]) => mockGetStudyAttention(...args),
      listWorkQueues: (...args: unknown[]) => mockListWorkQueues(...args),
      getObservation: (...args: unknown[]) => mockGetObservation(...args),
      verifyObservation: (...args: unknown[]) => mockVerifyObservation(...args),
      reviewObservation: (...args: unknown[]) => mockReviewObservation(...args),
      retractAssertion: (...args: unknown[]) => mockRetractAssertion(...args),
    },
  };
});

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfFormsApi")>();
  return {
    ...actual,
    ecrfFormsApi: {
      ...actual.ecrfFormsApi,
      getForm: vi.fn(),
      saveValues: vi.fn(),
      previewValues: vi.fn(async () => ({
        form_id: FORM_ID,
        evaluated_rule_packs: 0,
        warnings: [],
      })),
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
      ...actual.ecrfQueriesApi,
      listForForm: vi.fn(async () => []),
      listSdvAlerts: vi.fn(async () => []),
    },
  };
});

import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { clearKernelStudyCache } from "../../src/canvas/kernelStudy";
import NeedsAttention from "../../src/components/ai/NeedsAttention.vue";
import CanvasInspector from "../../src/components/canvas/CanvasInspector.vue";
import LocksSection from "../../src/components/canvas/LocksSection.vue";
import ScopeLockControls from "../../src/components/canvas/ScopeLockControls.vue";
import WorkQueuesPanel from "../../src/components/canvas/WorkQueuesPanel.vue";
import EcrfFormPanel from "../../src/components/ecrf/EcrfFormPanel.vue";
import { useAuthStore } from "../../src/stores/auth.js";
import ObservationView from "../../src/views/ObservationView.vue";

const RouteStub = defineComponent({
  name: "RouteStub",
  render: () => h("div", { id: "route-stub" }),
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

async function expectNoAxeViolations(element: Element) {
  const results = await axe.run(element, {
    rules: {
      region: { enabled: false },
    },
  });
  expect(results.violations).toEqual([]);
}

const mountedWrappers: Array<{ unmount: () => void }> = [];

function mountAttached<T>(component: T, options: Record<string, unknown>) {
  const wrapper = mount(
    component as never,
    {
      attachTo: document.body,
      ...options,
    } as never
  );
  mountedWrappers.push(wrapper);
  return wrapper;
}

describe("Targeted SDV flags, medical review (SDR), open-query exclusion, and cross-functional closeout", () => {
  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
    clearKernelStudyCache();
    vi.clearAllMocks();

    mockSearch.mockResolvedValue([
      {
        ref: STUDY_REF,
        kind: "study",
        code: "CADENCE-101",
        title: "Cadence Cardiovascular Trial",
        exact_code: true,
      },
    ]);
    mockGetChildren.mockResolvedValue([]);
    mockGetZoomPath.mockResolvedValue([]);
    mockGetNode.mockImplementation(async (ref: string) => ({
      ref,
      kind: ref.split(":")[0] ?? "observation",
      title: `Node ${ref}`,
      code: null,
      status: "Active",
      mask: null,
    }));
    mockCanI.mockImplementation(
      async (capability: string, resource: string) => ({
        capability,
        verb_class: "Execute",
        resource,
        permitted: true,
        reason: null,
        explanation: {
          subject: resource,
          claim: "Permitted",
          producer: "test",
          steps: [],
          conditions: [],
          next_actions: [],
        },
      })
    );
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);
  });

  afterEach(() => {
    while (mountedWrappers.length > 0) {
      mountedWrappers.pop()?.unmount();
    }
    localStorage.clear();
  });

  it("routes form-backed SDV alerts in NeedsAttention to CanvasFormWorkspace (omitting observation) and renders inline Verify / Review / Withdraw controls in ObservationView", async () => {
    mockGetStudyAttention.mockResolvedValue({
      study_ref: STUDY_REF,
      proposals: [],
      findings: [],
      sdv_alerts: [
        {
          alert_id: "01960000-0000-7000-8000-000000000601",
          study_id: STUDY_ID,
          study_site_id: SITE_ID,
          enrollment_id: ENROLLMENT_ID,
          form_id: FORM_ID,
          observation_id: OBS_CLEAN_ID,
          study_code: "CADENCE-101",
          site_code: "SITE-017",
          subject_code: "017-001",
          collection_spec_id: "VS",
          planned_activity_id: "WEEK2",
          item_id: "SYSBP",
          superseded_revision: 1,
          current_revision: 2,
          verified_by: "cra@cadence.test",
          verified_at: "2026-08-10T09:00:00Z",
          modified_by: "crc@site017.test",
          modified_at: "2026-08-10T10:00:00Z",
          reason_for_change: "Corrected cuff reading",
          message: "Previously verified SYSBP edited on 017-001",
        },
        {
          alert_id: "01960000-0000-7000-8000-000000000602",
          study_id: STUDY_ID,
          study_site_id: SITE_ID,
          enrollment_id: ENROLLMENT_ID,
          form_id: null,
          observation_id: OBS_STANDALONE_ID,
          study_code: "CADENCE-101",
          site_code: "SITE-017",
          subject_code: "017-001",
          collection_spec_id: null,
          planned_activity_id: null,
          item_id: null,
          superseded_revision: 1,
          current_revision: 2,
          verified_by: "cra@cadence.test",
          verified_at: "2026-08-10T09:00:00Z",
          modified_by: "crc@site017.test",
          modified_at: "2026-08-10T10:05:00Z",
          reason_for_change: "Telemetry re-import",
          message: "Standalone pulse edited on 017-001",
        },
      ],
    });

    const router = makeRouter();
    await router.push("/studies/CADENCE-101");
    await router.isReady();

    const pinia = createPinia();
    setActivePinia(pinia);
    const attention = mountAttached(NeedsAttention, {
      props: { studyCode: "CADENCE-101" },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    const formAlertHref = attention
      .find("#needs-attention-sdv-alert-0")
      .attributes("href");
    expect(formAlertHref).toContain(`form=${FORM_REF}`);
    expect(formAlertHref).toContain("item=SYSBP");
    expect(formAlertHref).not.toContain("observation=");

    await attention.find("#needs-attention-sdv-alert-0").trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.query.form).toBe(FORM_REF);
    expect(router.currentRoute.value.query.item).toBe("SYSBP");
    expect(router.currentRoute.value.query.observation).toBeUndefined();

    const standaloneAlertHref = attention
      .find("#needs-attention-sdv-alert-1")
      .attributes("href");
    expect(standaloneAlertHref).toContain(`observation=${OBS_STANDALONE_REF}`);
    expect(standaloneAlertHref).not.toContain("form=");

    // Standalone ObservationView exposes inline Verify, Review, Withdraw verification, and Withdraw review
    const baseObservation = {
      ref: `${OBS_STANDALONE_REF}@1`,
      enrollment_ref: ENROLLMENT_REF,
      concept: "concept:pulse@1",
      value: { type: "quantity", value: 108, unit: "/min", comparator: null },
      observed_at: "2026-08-10T09:00:00Z",
      version: 1,
      sdv_required: true,
      medical_review_required: true,
      locks: [],
      revisions: [
        {
          number: 1,
          value: {
            type: "quantity",
            value: 108,
            unit: "/min",
            comparator: null,
          },
          reason_for_change: "Initial entry",
          recorded_by: "crc@site017.test",
          recorded_at: "2026-08-10T09:01:00Z",
          recorded_until: null,
        },
      ],
      assertions: [],
      audit: [],
    };

    const verifiedAndReviewedObservation = {
      ...baseObservation,
      assertions: [
        {
          assertion_id: VERIFY_ASSERTION_ID,
          kind: "Verified",
          target: OBS_STANDALONE_REF,
          revision: 1,
          text: null,
          asserted_at: "2026-08-10T10:10:00Z",
          asserted_by: "cra@cadence.test",
          reason_for_change: "Verified against source chart",
          retracted_at: null,
          retracted_by: null,
          masked: false,
        },
        {
          assertion_id: REVIEW_ASSERTION_ID,
          kind: "Reviewed",
          target: OBS_STANDALONE_REF,
          revision: 1,
          text: null,
          asserted_at: "2026-08-10T10:12:00Z",
          asserted_by: "mm@cadence.test",
          reason_for_change: "Clinically consistent with screening ECG",
          retracted_at: null,
          retracted_by: null,
          masked: false,
        },
      ],
    };

    mockGetObservation
      .mockResolvedValueOnce(baseObservation)
      .mockResolvedValueOnce({
        ...baseObservation,
        assertions: [verifiedAndReviewedObservation.assertions[0]],
      })
      .mockResolvedValueOnce(verifiedAndReviewedObservation)
      .mockResolvedValueOnce(baseObservation);

    mockVerifyObservation.mockResolvedValue({
      ref: `assertion:${VERIFY_ASSERTION_ID}`,
      kind: "Verified",
      target: OBS_STANDALONE_REF,
      revision: 1,
      retracts: null,
      asserted_at: "2026-08-10T10:10:00Z",
      asserted_by: "cra@cadence.test",
    });
    mockReviewObservation.mockResolvedValue({
      ref: `assertion:${REVIEW_ASSERTION_ID}`,
      kind: "Reviewed",
      target: OBS_STANDALONE_REF,
      revision: 1,
      retracts: null,
      asserted_at: "2026-08-10T10:12:00Z",
      asserted_by: "mm@cadence.test",
    });
    mockRetractAssertion.mockResolvedValue({
      ref: "assertion:01960000-0000-7000-8000-000000000699",
      kind: "Reviewed",
      target: OBS_STANDALONE_REF,
      revision: 1,
      retracts: `assertion:${REVIEW_ASSERTION_ID}`,
      asserted_at: "2026-08-10T10:15:00Z",
      asserted_by: "mm@cadence.test",
    });

    const auth = useAuthStore();
    auth.setAuth({
      authenticated: true,
      token: "tok-cra-mm",
      tokenParsed: {
        sub: "reviewer-1",
        tenant_id: "tenant-1",
        realm_access: { roles: ["CRA", "SPONSOR_MM"] },
      },
    });

    const obsView = mountAttached(ObservationView, {
      props: {
        observationRef: OBS_STANDALONE_REF,
        studyCode: "CADENCE-101",
        embedded: true,
      },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(
      obsView.find('[data-testid="observation-review-badge"]').text()
    ).toBe("Medical review required");
    expect(obsView.find("#observation-verify").exists()).toBe(true);
    expect(obsView.find("#observation-review").exists()).toBe(true);

    // 1. Verify standalone observation
    await obsView.find("#observation-verify").trigger("click");
    await obsView
      .find("#observation-assertion-reason")
      .setValue("Verified against source chart");
    await obsView.find("form.observation-assertion-form").trigger("submit");
    await flushPromises();

    expect(mockVerifyObservation).toHaveBeenCalledWith(
      expect.objectContaining({
        observationRef: OBS_STANDALONE_REF,
        enrollmentRef: ENROLLMENT_REF,
        revision: 1,
        reason: "Verified against source chart",
      })
    );
    expect(obsView.find("#observation-unverify").exists()).toBe(true);

    // 2. Medical review standalone observation
    await obsView.find("#observation-review").trigger("click");
    await obsView
      .find("#observation-assertion-reason")
      .setValue("Clinically consistent with screening ECG");
    await obsView.find("form.observation-assertion-form").trigger("submit");
    await flushPromises();

    expect(mockReviewObservation).toHaveBeenCalledWith(
      expect.objectContaining({
        observationRef: OBS_STANDALONE_REF,
        enrollmentRef: ENROLLMENT_REF,
        revision: 1,
        reason: "Clinically consistent with screening ECG",
      })
    );
    expect(
      obsView.find('[data-testid="observation-review-badge"]').text()
    ).toBe("Reviewed (rev 1)");
    expect(obsView.find("#observation-unreview").exists()).toBe(true);

    // 3. Withdraw medical review
    await obsView.find("#observation-unreview").trigger("click");
    await obsView
      .find("#observation-assertion-reason")
      .setValue("Re-evaluating post-dose vitals");
    await obsView.find("form.observation-assertion-form").trigger("submit");
    await flushPromises();

    expect(mockRetractAssertion).toHaveBeenCalledWith(
      expect.objectContaining({
        assertionRef: `assertion:${REVIEW_ASSERTION_ID}`,
        enrollmentRef: ENROLLMENT_REF,
        reason: "Re-evaluating post-dose vitals",
      })
    );

    await expectNoAxeViolations(obsView.element);
  });

  it("skips open-queried and sdv_required:false items during Select all unverified, shows explanation badge on queried rows, completes Verify selected with zero K.Assertion.QueryOpen errors, and supports inline medical review", async () => {
    const baseForm = {
      form_id: FORM_ID,
      enrollment_id: ENROLLMENT_ID,
      participant_id: "01960000-0000-7000-8000-000000000599",
      study_id: STUDY_ID,
      study_site_id: SITE_ID,
      protocol_version_id: "01960000-0000-7000-8000-000000000588",
      collection_spec_id: "VS",
      planned_activity_id: "WEEK2",
      collected_at: "2026-08-10T09:00:00Z",
      opened_by: "crc1",
      opened_at: "2026-08-10T09:00:00Z",
      version: 2,
      complete: true,
      missing_required: [],
      items: [
        {
          item_id: "SYSBP",
          concept: "C25298@1",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          sdv_required: true,
          medical_review_required: true,
          value: { type: "quantity", value: 124, unit: "mm[Hg]" },
          observation_id: OBS_CLEAN_ID,
          revision: 2,
          recorded_by: "crc1",
          recorded_at: "2026-08-10T09:05:00Z",
        },
        {
          item_id: "DIABP",
          concept: "C25299@1",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          sdv_required: true,
          medical_review_required: false,
          value: { type: "quantity", value: 118, unit: "mm[Hg]" },
          observation_id: OBS_QUERIED_ID,
          revision: 1,
          recorded_by: "crc1",
          recorded_at: "2026-08-10T09:06:00Z",
        },
        {
          item_id: "NOTES",
          concept: "C42614@1",
          unit: null,
          value_kind: "Text",
          required: false,
          sdv_required: false,
          medical_review_required: false,
          value: { type: "text", value: "Seated 5 min" },
          observation_id: OBS_NON_SDV_ID,
          revision: 1,
          recorded_by: "crc1",
          recorded_at: "2026-08-10T09:07:00Z",
        },
      ],
      locks: [],
      assertions: [
        {
          assertion_id: QUERY_ID,
          kind: "Queried",
          target: OBS_QUERIED_REF,
          revision: 1,
          text: "Confirm elevated diastolic BP.",
          asserted_at: "2026-08-10T09:30:00Z",
          asserted_by: "cra1",
          reason: "Out of expected window",
          retracted_at: null,
        },
      ],
      signatures: [],
      content_hash: "sha256:form-targeted-sdv",
    };

    vi.mocked(ecrfFormsApi.getForm)
      .mockResolvedValueOnce(baseForm as never)
      .mockResolvedValueOnce({
        ...baseForm,
        assertions: [
          ...baseForm.assertions,
          {
            assertion_id: VERIFY_ASSERTION_ID,
            kind: "Verified",
            target: OBS_CLEAN_REF,
            revision: 2,
            text: null,
            asserted_at: "2026-08-10T10:00:00Z",
            asserted_by: "cra1",
            reason: "Verified clean items against chart",
            retracted_at: null,
          },
        ],
      } as never)
      .mockResolvedValueOnce({
        ...baseForm,
        assertions: [
          ...baseForm.assertions,
          {
            assertion_id: VERIFY_ASSERTION_ID,
            kind: "Verified",
            target: OBS_CLEAN_REF,
            revision: 2,
            text: null,
            asserted_at: "2026-08-10T10:00:00Z",
            asserted_by: "cra1",
            reason: "Verified clean items against chart",
            retracted_at: null,
          },
          {
            assertion_id: REVIEW_ASSERTION_ID,
            kind: "Reviewed",
            target: OBS_CLEAN_REF,
            revision: 2,
            text: null,
            asserted_at: "2026-08-10T10:05:00Z",
            asserted_by: "mm1",
            reason: "Medically reviewed vitals",
            retracted_at: null,
          },
        ],
      } as never);

    mockVerifyObservation.mockImplementation(
      async (req: { observationRef: string }) => {
        if (req.observationRef.includes(OBS_QUERIED_ID)) {
          throw Object.assign(new Error("Query open"), {
            status: 409,
            data: { code: "K.Assertion.QueryOpen" },
          });
        }
        return {
          ref: `assertion:${VERIFY_ASSERTION_ID}`,
          kind: "Verified",
          target: OBS_CLEAN_REF,
          revision: 2,
          retracts: null,
          asserted_at: "2026-08-10T10:00:00Z",
          asserted_by: "cra1",
        };
      }
    );
    mockReviewObservation.mockResolvedValue({
      ref: `assertion:${REVIEW_ASSERTION_ID}`,
      kind: "Reviewed",
      target: OBS_CLEAN_REF,
      revision: 2,
      retracts: null,
      asserted_at: "2026-08-10T10:05:00Z",
      asserted_by: "mm1",
    });

    const pinia = createPinia();
    setActivePinia(pinia);
    const panel = mountAttached(EcrfFormPanel, {
      props: {
        formId: FORM_ID,
        canVerifySource: true,
        canReviewSource: true,
      },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    // Non-SDV item shows "SDV not required" and no SDV checkbox
    expect(
      panel.find('[data-testid="ecrf-sdv-status-NOTES"]').text()
    ).toContain("SDV not required");
    expect(panel.find("#ecrf-sdv-select-NOTES").exists()).toBe(false);

    // Queried item shows explanation badge and disabled checkbox
    const queryBadge = panel.find(
      '[data-testid="ecrf-sdv-query-blocked-DIABP"]'
    );
    expect(queryBadge.exists()).toBe(true);
    expect(queryBadge.text()).toContain("Open query — close before SDV");
    expect(
      (panel.find("#ecrf-sdv-select-DIABP").element as HTMLInputElement)
        .disabled
    ).toBe(true);

    // Clicking "Select all unverified" selects ONLY clean SYSBP (skipping DIABP and NOTES)
    await panel.find("#ecrf-sdv-select-all").trigger("click");
    await nextTick();

    expect(
      (panel.find("#ecrf-sdv-select-SYSBP").element as HTMLInputElement).checked
    ).toBe(true);
    expect(
      (panel.find("#ecrf-sdv-select-DIABP").element as HTMLInputElement).checked
    ).toBe(false);
    expect(panel.find('[data-testid="ecrf-bulk-sdv"]').text()).toContain(
      "1 selected"
    );

    // Submit bulk SDV -> zero K.Assertion.QueryOpen errors
    await panel
      .find("#ecrf-bulk-sdv-reason")
      .setValue("Verified clean items against chart");
    await panel.find("#ecrf-bulk-sdv-submit").trigger("click");
    await flushPromises();

    expect(mockVerifyObservation).toHaveBeenCalledTimes(1);
    expect(mockVerifyObservation).toHaveBeenCalledWith(
      expect.objectContaining({
        observationRef: OBS_CLEAN_REF,
        revision: 2,
      })
    );
    expect(
      panel.find('[data-testid="ecrf-bulk-sdv-results"]').text()
    ).toContain("SYSBP:");
    expect(panel.find('[data-sdv-result="SYSBP"]').attributes("data-ok")).toBe(
      "true"
    );
    expect(
      panel.find('[data-testid="ecrf-bulk-sdv-results"]').text()
    ).not.toContain("open query");

    // Inline medical review on SYSBP
    expect(
      panel.find('[data-testid="ecrf-review-status-SYSBP"]').text()
    ).toContain("Medical review required");
    await panel.find("#ecrf-review-SYSBP").trigger("click");
    await panel.find("#ecrf-sdv-reason").setValue("Medically reviewed vitals");
    await panel.find("#ecrf-sdv-confirm").trigger("click");
    await flushPromises();

    expect(mockReviewObservation).toHaveBeenCalledWith(
      expect.objectContaining({
        observationRef: OBS_CLEAN_REF,
        enrollmentRef: ENROLLMENT_REF,
        revision: 2,
        reason: "Medically reviewed vitals",
      })
    );
    expect(
      panel.find('[data-testid="ecrf-review-status-SYSBP"]').text()
    ).toContain("Reviewed (rev 2)");
    expect(panel.find("#ecrf-unreview-SYSBP").exists()).toBe(true);
  });

  it("filters medical_reviews and source_verifications queues by site, participant, and form in WorkQueuesPanel", async () => {
    mockListWorkQueues.mockResolvedValue({
      queues: [
        {
          definition:
            "work_queue_definition:01960000-0000-7000-8000-000000000701@1",
          code: "medical_reviews",
          version: 1,
          title: "Medical reviews due",
          capability: "observation.review",
          truncated: false,
          items: [
            {
              subject: `${OBS_CLEAN_REF}@2`,
              subject_kind: "observation",
              title: "Review Systolic BP for 017-001",
              since: "2026-08-10T09:05:00Z",
              masked: null,
              target: {
                study_ref: STUDY_REF,
                study_id: STUDY_ID,
                site_code: "SITE-017",
                enrollment_id: ENROLLMENT_ID,
                subject_code: "017-001",
                form_id: FORM_ID,
                collection_spec_id: "VS",
                form_title: "Vital Signs",
                item_id: "SYSBP",
                observation_ref: `${OBS_CLEAN_REF}@2`,
                finding_ref: null,
                protocol_version_ref: null,
                proposal_ref: null,
              },
            },
            {
              subject: `${OBS_STANDALONE_REF}@1`,
              subject_kind: "observation",
              title: "Review Troponin I for 022-004",
              since: "2026-08-10T09:15:00Z",
              masked: null,
              target: {
                study_ref: STUDY_REF,
                study_id: STUDY_ID,
                site_code: "SITE-022",
                enrollment_id: "01960000-0000-7000-8000-000000000799",
                subject_code: "022-004",
                form_id: null,
                collection_spec_id: "LB",
                form_title: "Central Labs",
                item_id: null,
                observation_ref: `${OBS_STANDALONE_REF}@1`,
                finding_ref: null,
                protocol_version_ref: null,
                proposal_ref: null,
              },
            },
          ],
        },
      ],
    });

    const router = makeRouter();
    await router.push("/studies/CADENCE-101");
    await router.isReady();

    const pinia = createPinia();
    setActivePinia(pinia);
    const queues = mountAttached(WorkQueuesPanel, {
      props: { studyCode: "CADENCE-101" },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(queues.find('[data-testid="work-queues-filters"]').exists()).toBe(
      true
    );
    expect(
      queues.find('[data-testid="work-queue-count-medical_reviews"]').text()
    ).toBe("2");

    // Form-backed item omits observation= so CanvasFormWorkspace opens
    const firstHref = queues
      .find('[data-testid="work-item-link-medical_reviews-0"]')
      .attributes("href");
    expect(firstHref).toContain(`form=${FORM_REF}`);
    expect(firstHref).toContain("item=SYSBP");
    expect(firstHref).not.toContain("observation=");

    // Filter by site SITE-022
    await queues.find("#work-queues-filter-site").setValue("SITE-022");
    await nextTick();
    expect(
      queues.find('[data-testid="work-queue-count-medical_reviews"]').text()
    ).toBe("1");
    expect(queues.text()).toContain("Review Troponin I for 022-004");
    expect(queues.text()).not.toContain("Review Systolic BP for 017-001");

    // Clear filters and filter by form VS
    await queues.find("#work-queues-filter-clear").trigger("click");
    await queues.find("#work-queues-filter-form").setValue("VS");
    await nextTick();
    expect(
      queues.find('[data-testid="work-queue-count-medical_reviews"]').text()
    ).toBe("1");
    expect(queues.text()).toContain("Review Systolic BP for 017-001");

    await expectNoAxeViolations(queues.element);
  });

  it("loads Closeout Readiness for cra_monitor and site_investigator when lock.apply is denied, filters blockers by role, preserves K-AUTH-4 masking, and navigates every unmasked blocker to its workspace", async () => {
    // Deny lock.apply / lock.release so caller is a read-only study closeout participant (CRA or PI)
    mockCanI.mockImplementation(
      async (capability: string, resource: string) => ({
        capability,
        verb_class: "Kernel",
        resource,
        permitted: capability !== "lock.apply" && capability !== "lock.release",
        reason:
          capability === "lock.apply" || capability === "lock.release"
            ? "Only data managers may sign study locks."
            : null,
        explanation: {
          subject: resource,
          claim: "Evaluated",
          producer: "test",
          steps: [],
          conditions: [],
          next_actions: [],
        },
      })
    );

    mockListStudyLocks.mockResolvedValue({
      study_ref: STUDY_REF,
      study_code: "CADENCE-101",
      valid_at: "2026-08-10T12:00:00Z",
      known_at: "2026-08-10T12:00:00Z",
      locks: [],
      hidden: { more_exist: false, reasons: [] },
    });

    mockGetStudyLockReadiness.mockResolvedValue({
      study_ref: STUDY_REF,
      study_code: "CADENCE-101",
      scope_ref: STUDY_REF,
      scope_kind: "study",
      ready_for_lock: false,
      can_freeze: true,
      open_query_count: 1,
      missing_required_item_count: 0,
      unverified_value_count: 1,
      unreviewed_value_count: 2,
      unsigned_form_count: 1,
      unopened_visit_count: 0,
      open_error_finding_count: 1,
      valid_at: "2026-08-10T12:00:00Z",
      known_at: "2026-08-10T12:00:00Z",
      blockers: [
        {
          kind: "UnverifiedValue",
          owner: "Clinical research associate / data manager",
          target_ref: OBS_CLEAN_REF,
          target_kind: "observation",
          form_ref: FORM_REF,
          subject_code: "017-001",
          collection_spec_id: "VS",
          collection_title: "Vital Signs",
          item_id: "SYSBP",
          item_label: "Systolic BP",
          query_ref: null,
          query_text: null,
          answered: false,
          current_revision: 2,
          next_action: `Verify revision 2 of ${OBS_CLEAN_REF}.`,
          next_capability: "observation.verify",
          masked: false,
          mask: null,
        },
        {
          kind: "UnreviewedValue",
          owner: "Medical monitor / data manager",
          target_ref: OBS_STANDALONE_REF,
          target_kind: "observation",
          form_ref: null,
          subject_code: "017-001",
          collection_spec_id: null,
          collection_title: null,
          item_id: null,
          item_label: "Pulse Rate",
          query_ref: null,
          query_text: null,
          answered: false,
          current_revision: 1,
          next_action: `Complete medical review on revision 1 of ${OBS_STANDALONE_REF}.`,
          next_capability: "observation.review",
          masked: false,
          mask: null,
        },
        {
          kind: "UnsignedForm",
          owner: "Principal investigator",
          target_ref: FORM_REF,
          target_kind: "collection_instance",
          subject_code: "017-001",
          collection_spec_id: "VS",
          collection_title: "Vital Signs",
          item_id: null,
          item_label: null,
          query_ref: null,
          query_text: null,
          answered: false,
          signature_superseded: false,
          next_action: `Sign completed form ${FORM_REF}.`,
          next_capability: "collection.sign",
          masked: false,
          mask: null,
        },
        {
          kind: "ErrorFinding",
          owner: "Data manager",
          target_ref: FORM_REF,
          target_kind: "collection_instance",
          subject_code: "017-001",
          collection_spec_id: "VS",
          collection_title: "Vital Signs",
          item_id: "SYSBP",
          item_label: "Systolic BP",
          query_ref: null,
          query_text: null,
          finding_ref: FINDING_REF,
          rule_id: "VS_SYSBP_HIGH",
          detail: "Error finding [VS_SYSBP_HIGH]",
          answered: false,
          next_action: "Resolve validation finding.",
          next_capability: "validation_finding.investigate",
          masked: false,
          mask: null,
        },
        {
          kind: "UnreviewedValue",
          owner: "Unblinded medical monitor",
          target_ref: null,
          target_kind: "observation",
          subject_code: null,
          collection_spec_id: null,
          collection_title: null,
          item_id: null,
          item_label: null,
          query_ref: null,
          query_text: null,
          answered: false,
          next_action: "Unblinded medical monitor must review masked value.",
          next_capability: "observation.review",
          masked: true,
          mask: "Blinded",
        },
      ],
    });

    for (const personaRole of ["CRA", "INVESTIGATOR"] as const) {
      const pinia = createPinia();
      setActivePinia(pinia);
      const auth = useAuthStore();
      auth.setAuth({
        authenticated: true,
        token: `tok-${personaRole}`,
        tokenParsed: {
          sub: `${personaRole.toLowerCase()}-1`,
          tenant_id: "tenant-1",
          realm_access: { roles: [personaRole] },
        },
      });

      const router = makeRouter();
      await router.push("/studies/CADENCE-101?stage=data&section=locks");
      await router.isReady();

      const locks = mountAttached(LocksSection, {
        props: { studyCode: "CADENCE-101" },
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      // Readiness card loads even though lock.apply is denied, and apply form stays hidden
      expect(mockGetStudyLockReadiness).toHaveBeenCalled();
      expect(locks.find('[data-testid="study-locks-readiness"]').exists()).toBe(
        true
      );
      expect(locks.find("#btn-apply-study-lock").exists()).toBe(false);
      expect(
        locks.find('[data-testid="readiness-gate-unreviewed_values"]').text()
      ).toContain("2 unreviewed values");

      // Blinded blocker preserves K-AUTH-4 masking with no workspace button
      expect(locks.text()).toContain("Masked (Blinded)");

      // Filter by Medical Monitor (SDR) role pill -> shows only the 2 UnreviewedValue blockers (1 unmasked + 1 masked)
      await locks
        .find('[data-testid="blocker-role-filter-medical"]')
        .trigger("click");
      await nextTick();
      const mmRows = locks.findAll(".readiness-blocker-item");
      expect(mmRows).toHaveLength(2);
      expect(locks.find('[data-testid="blocker-open-0"]').text()).toBe(
        "Review observation"
      );

      // Clicking the unmasked Medical Monitor blocker opens the standalone observation in conduct workspace
      await locks.find('[data-testid="blocker-open-0"]').trigger("click");
      await flushPromises();
      expect(router.currentRoute.value.query.observation).toBe(
        OBS_STANDALONE_REF
      );

      // Switch to All and click the form-backed UnverifiedValue blocker -> opens CanvasFormWorkspace on SYSBP
      await locks
        .find('[data-testid="blocker-role-filter-all"]')
        .trigger("click");
      await nextTick();
      expect(locks.find('[data-testid="blocker-open-0"]').text()).toBe(
        "Open form to verify"
      );
      await locks.find('[data-testid="blocker-open-0"]').trigger("click");
      await flushPromises();
      expect(router.currentRoute.value.query.form).toBe(FORM_REF);
      expect(router.currentRoute.value.query.item).toBe("SYSBP");
      expect(router.currentRoute.value.query.observation).toBeUndefined();

      await expectNoAxeViolations(locks.element);
      locks.unmount();
    }

    // Also verify ScopeLockControls renders readiness & :to blocker chips when canApply is false,
    // and CanvasInspector genericDisplay renders #inspector-open-workspace for blocker NodeRefs!
    const pinia = createPinia();
    setActivePinia(pinia);
    const router = makeRouter();
    await router.push(`/studies/CADENCE-101?node=${FORM_REF}`);
    await router.isReady();

    const scopeControls = mountAttached(ScopeLockControls, {
      props: {
        studyRef: STUDY_REF,
        studyCode: "CADENCE-101",
        scopeRef: FORM_REF,
        scopeLabel: "form",
        idPrefix: "cra-scope",
      },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(
      scopeControls.find('[data-testid="cra-scope-readiness"]').exists()
    ).toBe(true);
    const scopeBlockerLink = scopeControls.find(
      '[data-testid="cra-scope-readiness-blockers"] a.node-ref-chip'
    );
    expect(scopeBlockerLink.exists()).toBe(true);
    expect(scopeBlockerLink.attributes("href")).toContain(`form=${FORM_REF}`);

    const inspector = mountAttached(CanvasInspector, {
      props: {
        studyCode: "CADENCE-101",
        participant: null,
        nodeRef: FORM_REF,
        missing: false,
        genericNodeRef: FORM_REF,
        recordLocation: null,
        dataEntryLocation: null,
      },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    const inspectorWorkspaceBtn = inspector.find(
      '[data-testid="inspector-open-workspace"]'
    );
    expect(inspectorWorkspaceBtn.exists()).toBe(true);
    expect(inspectorWorkspaceBtn.text()).toBe("Open form workspace");
    expect(inspectorWorkspaceBtn.attributes("href")).toContain(
      `form=${FORM_REF}`
    );
  });
});
