import { defineComponent, h } from "vue";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import axe from "axe-core";

const STUDY_ID = "01960000-0000-7000-8000-000000000101";
const STUDY_REF = `study:${STUDY_ID}`;
const ENROLLMENT_ID = "01960000-0000-7000-8000-000000000102";
const FORM_ID = "01960000-0000-7000-8000-000000000103";
const OBS_ID = "01960000-0000-7000-8000-000000000104";
const FINDING_ID = "01960000-0000-7000-8000-000000000105";
const FINDING_REF = `validation_finding:${FINDING_ID}`;
const PROPOSAL_REF = "proposal:01960000-0000-7000-8000-000000000106";
const PROTOCOL_REF = "protocol_version:01960000-0000-7000-8000-000000000107@1";

const mockCanI = vi.fn();
const mockSearch = vi.fn();
const mockGetChildren = vi.fn();
const mockListStudyLocks = vi.fn();
const mockGetLockScopeReadiness = vi.fn();
const mockApplyLock = vi.fn();
const mockListStudyRulePacks = vi.fn();
const mockListStudyValidationFindings = vi.fn();
const mockInvestigateValidationFinding = vi.fn();
const mockClassifyValidationFinding = vi.fn();
const mockAcceptValidationFinding = vi.fn();
const mockExplainValidationFinding = vi.fn();
const mockReopenValidationFinding = vi.fn();
const mockListProposals = vi.fn();
const mockListWorkQueues = vi.fn();

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
      listStudyLocks: (...args: unknown[]) => mockListStudyLocks(...args),
      getLockScopeReadiness: (...args: unknown[]) =>
        mockGetLockScopeReadiness(...args),
      applyLock: (...args: unknown[]) => mockApplyLock(...args),
      listStudyRulePacks: (...args: unknown[]) =>
        mockListStudyRulePacks(...args),
      listStudyValidationFindings: (...args: unknown[]) =>
        mockListStudyValidationFindings(...args),
      investigateValidationFinding: (...args: unknown[]) =>
        mockInvestigateValidationFinding(...args),
      classifyValidationFinding: (...args: unknown[]) =>
        mockClassifyValidationFinding(...args),
      acceptValidationFinding: (...args: unknown[]) =>
        mockAcceptValidationFinding(...args),
      explainValidationFinding: (...args: unknown[]) =>
        mockExplainValidationFinding(...args),
      reopenValidationFinding: (...args: unknown[]) =>
        mockReopenValidationFinding(...args),
      listProposals: (...args: unknown[]) => mockListProposals(...args),
      listWorkQueues: (...args: unknown[]) => mockListWorkQueues(...args),
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
      signForm: vi.fn(),
      listForEnrollment: vi.fn(),
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
    },
  };
});

vi.mock("../../src/components/SignatureCaptureModal.vue", () => ({
  default: {
    name: "SignatureCaptureModal",
    props: [
      "isOpen",
      "username",
      "signerName",
      "signerRole",
      "reasons",
      "actionUrl",
      "semanticAction",
      "onSign",
    ],
    emits: ["cancel", "success"],
    template: `
      <div
        v-if="isOpen"
        data-testid="mock-sig-modal"
        :data-action-url="actionUrl"
        :data-semantic="semanticAction"
        :data-role="signerRole"
      >
        <button type="button" data-testid="confirm-step-up" @click="confirm">
          Confirm signature
        </button>
      </div>
    `,
    methods: {
      async confirm(this: {
        reasons: string[];
        onSign: (tok: string, reason: string) => Promise<unknown>;
        $emit: (e: string) => void;
      }) {
        try {
          await this.onSign(
            "sig-tok-verified",
            this.reasons?.[0] ?? "Approved"
          );
          this.$emit("success");
        } catch {
          // Real SignatureCaptureModal catches onSign errors to render inline modal feedback
        }
      },
    },
  },
}));

import {
  ASSERTION_SCOPE_NOT_READY,
  describeLockError,
  FINDING_ACCEPT_SEMANTIC_ACTION,
  FINDING_EXPLAIN_SEMANTIC_ACTION,
  findingAcceptAction,
  findingExplainAction,
} from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import { ecrfFormsApi, NOT_READY_TO_SIGN } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import EcrfFormPanel from "../../src/components/ecrf/EcrfFormPanel.vue";
import LocksSection from "../../src/components/canvas/LocksSection.vue";
import ScopeLockControls from "../../src/components/canvas/ScopeLockControls.vue";
import ParticipantFormsPanel from "../../src/components/canvas/ParticipantFormsPanel.vue";
import ValidationSection from "../../src/components/canvas/ValidationSection.vue";
import WorkQueuesPanel from "../../src/components/canvas/WorkQueuesPanel.vue";
import NeedsAttention from "../../src/components/ai/NeedsAttention.vue";

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

describe("Kernel readiness gates, validation finding lifecycle, and capability work queues", () => {
  afterEach(() => {
    while (mountedWrappers.length > 0) {
      mountedWrappers.pop()?.unmount();
    }
  });

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
    mockSearch.mockResolvedValue([
      {
        ref: STUDY_REF,
        kind: "study",
        code: "CAD-101",
        title: "Cardio Trial",
        exact_code: true,
      },
    ]);
    mockGetChildren.mockResolvedValue([]);
    mockCanI.mockResolvedValue({
      capability: "any",
      verb_class: "Execute",
      resource: STUDY_REF,
      permitted: true,
      reason: null,
      explanation: {
        subject: STUDY_REF,
        claim: "Permitted",
        producer: "test",
        steps: [],
        conditions: [],
        next_actions: [],
      },
    });
  });

  it("drives ValidationSection finding lifecycle (Investigate, Classify, signed Accept, signed Explain, Reopen, Explained filter, site-scoped canI) with zero axe violations", async () => {
    // Simulate a site-scoped reviewer where study-level canI is false for explain/investigate,
    // and finding-level canI(cap, FINDING_REF) returns true.
    mockCanI.mockImplementation(
      async (capability: string, resource: string) => ({
        capability,
        verb_class: "Execute",
        resource,
        permitted:
          resource === FINDING_REF ||
          capability.includes("accept") ||
          capability.includes("reopen"),
        reason: null,
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

    let currentFinding = {
      ref: FINDING_REF,
      study_ref: STUDY_REF,
      stage: "Collection",
      pack_key: "cadence-edit-checks",
      pack_item_ref: "library_item:01960000-0000-7000-8000-000000000199@1",
      rule_id: "VS_SYSBP_RANGE",
      rule_version: 1,
      severity: "Error",
      status: "Open",
      blocks: false,
      classification: null as string | null,
      decision: null as {
        status: string;
        decided_by: string;
        decided_at: string;
        text: string | null;
        decided_on_run_ref?: string | null;
        attestation_ref?: string | null;
      } | null,
      target_kind: "DatasetRecord",
      dataset: "VS",
      record_key: FORM_ID,
      variable: "SYSBP",
      message: "Systolic BP 245 exceeds upper limit 220 mm[Hg].",
      expected: "<= 220",
      observed: [{ variable: "SYSBP", value: "245" }],
      standard: null,
      standard_version: null,
      agency: null,
      first_detected_run_ref:
        "validation_run:01960000-0000-7000-8000-000000000180",
      last_detected_run_ref:
        "validation_run:01960000-0000-7000-8000-000000000180",
      resolved_by_run_ref: null,
    };

    mockListStudyRulePacks.mockResolvedValue({
      study_ref: STUDY_REF,
      profile_ref: null,
      pinned_rule_pack_refs: [],
      rule_packs: [],
    });
    mockListStudyValidationFindings.mockImplementation(async () => ({
      total: 1,
      findings: [{ ...currentFinding }],
    }));

    mockInvestigateValidationFinding.mockImplementation(async () => {
      currentFinding = { ...currentFinding, status: "Investigating" };
      return currentFinding;
    });
    mockClassifyValidationFinding.mockImplementation(
      async ({ classification }: { classification: string }) => {
        currentFinding = { ...currentFinding, classification };
        return currentFinding;
      }
    );
    mockAcceptValidationFinding.mockImplementation(
      async ({ rationale }: { rationale: string }) => {
        currentFinding = {
          ...currentFinding,
          status: "Accepted",
          decision: {
            status: "Accepted",
            decided_by: "dm@cadence.test",
            decided_at: "2026-06-01T10:15:00Z",
            text: rationale,
            attestation_ref: "attestation:01960000-0000-7000-8000-000000000200",
          },
        };
        return currentFinding;
      }
    );
    mockReopenValidationFinding.mockImplementation(async () => {
      currentFinding = {
        ...currentFinding,
        status: "Open",
        decision: null,
      };
      return currentFinding;
    });
    mockExplainValidationFinding.mockImplementation(
      async ({ explanation }: { explanation: string }) => {
        currentFinding = {
          ...currentFinding,
          status: "Explained",
          decision: {
            status: "Explained",
            decided_by: "cra@cadence.test",
            decided_at: "2026-06-01T10:30:00Z",
            text: explanation,
            attestation_ref: "attestation:01960000-0000-7000-8000-000000000201",
          },
        };
        return currentFinding;
      }
    );

    const router = makeRouter();
    await router.push("/studies/CAD-101?stage=data&section=validation");
    await router.isReady();

    const wrapper = mountAttached(ValidationSection, {
      props: { studyCode: "CAD-101", canManageRules: true },
      global: { plugins: [router] },
    });
    await flushPromises();

    // Explained option exists in status filter and DatasetRecord link renders
    expect(
      wrapper
        .find("#validation-status-filter option[value='Explained']")
        .exists()
    ).toBe(true);
    expect(wrapper.find(".form-link").attributes("href")).toContain(
      `form=collection_instance:${FORM_ID}`
    );

    // 1. Investigate (enabled via per-finding site-scoped canI)
    await wrapper
      .find(`[data-testid="btn-investigate-finding-${FINDING_REF}"]`)
      .trigger("click");
    await wrapper
      .find(`#finding-action-text-${CSS.escape(FINDING_REF)}`)
      .setValue("Checking site vitals log");
    await wrapper
      .find("[data-testid='btn-confirm-finding-investigate']")
      .trigger("click");
    await flushPromises();
    expect(mockInvestigateValidationFinding).toHaveBeenCalledWith(
      expect.objectContaining({
        findingRef: FINDING_REF,
        reason: "Checking site vitals log",
      })
    );
    expect(
      wrapper
        .find("[data-testid='finding-VS_SYSBP_RANGE-Investigating']")
        .exists()
    ).toBe(true);

    // 2. Classify (using real kernel FindingClassification enum value)
    await wrapper
      .find(`[data-testid="btn-classify-finding-${FINDING_REF}"]`)
      .trigger("click");
    await wrapper
      .find(`#finding-classification-${CSS.escape(FINDING_REF)}`)
      .setValue("NotAnIssue");
    await wrapper
      .find(`#finding-action-text-${CSS.escape(FINDING_REF)}`)
      .setValue("Confirmed baseline hypertension on source worksheet");
    await wrapper
      .find("[data-testid='btn-confirm-finding-classify']")
      .trigger("click");
    await flushPromises();
    expect(mockClassifyValidationFinding).toHaveBeenCalledWith(
      expect.objectContaining({
        findingRef: FINDING_REF,
        classification: "NotAnIssue",
      })
    );
    expect(wrapper.find("[data-testid='finding-classification']").text()).toBe(
      "NotAnIssue"
    );

    // 3. Signed Accept
    await wrapper
      .find(`[data-testid="btn-accept-finding-${FINDING_REF}"]`)
      .trigger("click");
    await wrapper
      .find(`#finding-action-text-${CSS.escape(FINDING_REF)}`)
      .setValue("Accepted per medical monitor review");
    await wrapper
      .find("[data-testid='btn-confirm-finding-accept']")
      .trigger("click");
    await flushPromises();

    const acceptModal = wrapper.find("[data-testid='mock-sig-modal']");
    expect(acceptModal.attributes("data-action-url")).toBe(
      findingAcceptAction(FINDING_REF)
    );
    expect(acceptModal.attributes("data-semantic")).toBe(
      FINDING_ACCEPT_SEMANTIC_ACTION
    );
    await wrapper.find("[data-testid='confirm-step-up']").trigger("click");
    await flushPromises();

    expect(mockAcceptValidationFinding).toHaveBeenCalledWith(
      expect.objectContaining({
        findingRef: FINDING_REF,
        rationale: "Accepted per medical monitor review",
        sigToken: "sig-tok-verified",
      })
    );
    expect(wrapper.find("[data-testid='finding-decision']").text()).toContain(
      "Accepted per medical monitor review"
    );

    // 4. Reopen
    await wrapper
      .find(`[data-testid="btn-reopen-finding-${FINDING_REF}"]`)
      .trigger("click");
    await wrapper
      .find(`#finding-action-text-${CSS.escape(FINDING_REF)}`)
      .setValue("Reopening to attach CRA source verification note");
    await wrapper
      .find("[data-testid='btn-confirm-finding-reopen']")
      .trigger("click");
    await flushPromises();
    expect(mockReopenValidationFinding).toHaveBeenCalledTimes(1);

    // 5. Signed Explain
    await wrapper
      .find(`[data-testid="btn-explain-finding-${FINDING_REF}"]`)
      .trigger("click");
    await wrapper
      .find(`#finding-action-text-${CSS.escape(FINDING_REF)}`)
      .setValue("Verified against triage monitor printout");
    await wrapper
      .find("[data-testid='btn-confirm-finding-explain']")
      .trigger("click");
    await flushPromises();

    const explainModal = wrapper.find("[data-testid='mock-sig-modal']");
    expect(explainModal.attributes("data-action-url")).toBe(
      findingExplainAction(FINDING_REF)
    );
    expect(explainModal.attributes("data-semantic")).toBe(
      FINDING_EXPLAIN_SEMANTIC_ACTION
    );
    await wrapper.find("[data-testid='confirm-step-up']").trigger("click");
    await flushPromises();

    expect(mockExplainValidationFinding).toHaveBeenCalledWith(
      expect.objectContaining({
        findingRef: FINDING_REF,
        explanation: "Verified against triage monitor printout",
        sigToken: "sig-tok-verified",
      })
    );
    expect(
      wrapper.find("[data-testid='finding-VS_SYSBP_RANGE-Explained']").exists()
    ).toBe(true);

    await expectNoAxeViolations(wrapper.element);
  });

  it("blocks form signing when readiness_blockers are present and surfaces K.Collection.NotReadyToSign on 409", async () => {
    const blockedForm = {
      form_id: FORM_ID,
      study_id: STUDY_ID,
      enrollment_id: ENROLLMENT_ID,
      participant_id: "01960000-0000-7000-8000-000000000190",
      collection_spec_id: "VS",
      collection_title: "Vital signs",
      planned_activity_id: "V1",
      collected_at: "2026-06-01T10:00:00Z",
      version: 2,
      content_hash: "sha256-form-hash",
      can_sign: true,
      complete: false,
      missing_required: [],
      open_query_count: 1,
      answered_query_count: 0,
      open_error_finding_count: 1,
      readiness_blockers: [
        "1 open query",
        "1 unresolved error validation finding",
      ],
      items: [
        {
          item_id: "SYSBP",
          concept: "Systolic BP",
          value_kind: "Quantity",
          unit: "mm[Hg]",
          required: true,
          observation_id: OBS_ID,
          revision: 1,
          value: { type: "quantity", magnitude: 245, unit: "mm[Hg]" },
          recorded_by: "crc@cadence.test",
          recorded_at: "2026-06-01T10:00:00Z",
        },
      ],
      locks: [],
      assertions: [],
      signatures: [],
    };

    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue(blockedForm as never);
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);

    const router = makeRouter();
    const wrapper = mountAttached(EcrfFormPanel, {
      props: { formId: FORM_ID },
      global: { plugins: [router] },
    });
    await flushPromises();

    const statusText = wrapper.find("[data-testid='ecrf-form-status']").text();
    expect(statusText).toContain("1 open query");
    expect(statusText).toContain("1 unresolved error validation finding");
    expect(statusText).not.toContain("0 required items");

    const signBtn = wrapper.find("#ecrf-form-sign");
    expect(signBtn.attributes("disabled")).toBeDefined();
    expect(wrapper.find("[data-testid='ecrf-sign-hint']").text()).toContain(
      "Cannot sign until readiness blockers are resolved"
    );

    // Now load a form that appears ready on initial read, then fails server-side with K.Collection.NotReadyToSign
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue({
      ...blockedForm,
      complete: true,
      open_query_count: 0,
      open_error_finding_count: 0,
      readiness_blockers: [],
    } as never);
    vi.mocked(ecrfFormsApi.signForm).mockRejectedValue(
      new ApiError(
        "Cannot sign form: 1 unresolved error validation finding (VS_SYSBP_RANGE).",
        409,
        "Conflict",
        {
          code: NOT_READY_TO_SIGN,
          detail:
            "Cannot sign form: 1 unresolved error validation finding (VS_SYSBP_RANGE).",
        }
      )
    );

    const wrapperReady = mountAttached(EcrfFormPanel, {
      props: { formId: FORM_ID },
      global: { plugins: [router] },
    });
    await flushPromises();

    await wrapperReady.find("#ecrf-form-sign").trigger("click");
    await flushPromises();
    await wrapperReady.find("[data-testid='confirm-step-up']").trigger("click");
    await flushPromises();

    const outcome = wrapperReady.find("[data-testid='ecrf-sign-outcome']");
    expect(outcome.text()).toContain("VS_SYSBP_RANGE");
    expect(wrapperReady.find("#ecrf-sign-reload").exists()).toBe(true);
  });

  it("displays lock scope readiness blockers in LocksSection and ScopeLockControls and disables apply buttons", async () => {
    mockListStudyLocks.mockResolvedValue({
      study_ref: STUDY_REF,
      study_code: "CAD-101",
      valid_at: "2026-06-01T10:00:00Z",
      known_at: "2026-06-01T10:00:00Z",
      locks: [],
    });
    mockGetLockScopeReadiness.mockResolvedValue({
      study_ref: STUDY_REF,
      scope_ref: STUDY_REF,
      scope_kind: "study",
      ready: false,
      blockers: [
        "1 form instance has missing required items",
        "1 unresolved error validation finding",
      ],
    });

    const router = makeRouter();
    const locksWrapper = mountAttached(LocksSection, {
      props: { studyCode: "CAD-101" },
      global: { plugins: [router] },
    });
    await flushPromises();

    const readinessBanner = locksWrapper.find(
      "[data-testid='study-lock-readiness']"
    );
    expect(readinessBanner.exists()).toBe(true);
    expect(readinessBanner.text()).toContain(
      "1 unresolved error validation finding"
    );
    await locksWrapper
      .find("#lock-apply-reason")
      .setValue("Attempt study lock");
    expect(
      locksWrapper.find("#btn-apply-study-lock").attributes("disabled")
    ).toBeDefined();
    await expectNoAxeViolations(locksWrapper.element);

    const inlineWrapper = mountAttached(ScopeLockControls, {
      props: {
        studyRef: STUDY_REF,
        scopeRef: `collection_instance:${FORM_ID}`,
        scopeLabel: "form",
        idPrefix: "ecrf-lock",
      },
      global: { plugins: [router] },
    });
    await flushPromises();

    expect(
      inlineWrapper.find("[data-testid='ecrf-lock-readiness']").text()
    ).toContain("Cannot freeze or lock form yet");
    await inlineWrapper.find("#ecrf-lock-reason").setValue("Freeze form");
    expect(
      inlineWrapper.find("#ecrf-lock-freeze").attributes("disabled")
    ).toBeDefined();
    expect(
      inlineWrapper.find("#ecrf-lock-lock").attributes("disabled")
    ).toBeDefined();

    expect(
      describeLockError(
        new ApiError("Scope has 1 open query.", 409, "Conflict", {
          code: ASSERTION_SCOPE_NOT_READY,
          detail: "Scope has 1 open query.",
        })
      )
    ).toBe("Scope has 1 open query.");
  });

  it("marks scheduled casebook cells incomplete when error findings or active queries persist in ParticipantFormsPanel", async () => {
    vi.mocked(ecrfFormsApi.listForEnrollment).mockResolvedValue([
      {
        form_id: FORM_ID,
        study_id: STUDY_ID,
        enrollment_id: ENROLLMENT_ID,
        collection_spec_id: "VS",
        collection_title: "Vital signs",
        planned_activity_id: "V1",
        collected_at: "2026-06-01T10:00:00Z",
        version: 1,
        complete: false,
        item_count: 2,
        recorded_count: 2,
        missing_required_count: 0,
        open_query_count: 0,
        answered_query_count: 0,
        open_error_finding_count: 1,
        verified_count: 0,
        unverified_count: 2,
        signed: false,
        signature_superseded: false,
        frozen: false,
        locked: false,
      },
    ]);

    const router = makeRouter();
    const wrapper = mountAttached(ParticipantFormsPanel, {
      props: {
        studyCode: "CAD-101",
        subjectCode: "SUBJ-001",
        enrollmentId: ENROLLMENT_ID,
        schedule: {
          versionRef: PROTOCOL_REF,
          title: "Protocol v1",
          epochs: [{ epochId: "E1", name: "Screening", columnCount: 1 }],
          columns: [
            {
              activityId: "V1",
              name: "Visit 1",
              studyDay: 1,
              windowBefore: 0,
              windowAfter: 2,
            },
          ],
          scheduledRows: [
            {
              collectionSpecId: "VS",
              name: "Vital signs",
              itemCount: 2,
              columnIndices: [0],
            },
          ],
          unscheduledRows: [],
        },
        formLocation: (id: string) =>
          `/studies/CAD-101?form=collection_instance:${id}`,
        newFormCellLocation: (specId: string, visitId?: string | null) =>
          `/studies/CAD-101?form=new&spec=${specId}&visit=${visitId ?? ""}`,
      },
      global: { plugins: [router] },
    });
    await flushPromises();

    expect(wrapper.find("[data-testid='casebook-summary']").text()).toContain(
      "0/1 scheduled complete"
    );
    expect(wrapper.find("[data-testid='casebook-summary']").text()).toContain(
      "1 incomplete"
    );
    expect(
      wrapper.find("[data-testid='casebook-error-findings']").text()
    ).toContain("1 error finding");
    expect(wrapper.find("[data-cell='VS:V1']").classes()).toContain(
      "matrix-cell--incomplete"
    );
    expect(wrapper.find("[data-testid='badge-error-findings']").text()).toBe(
      "1 error finding"
    );
    expect(wrapper.text()).toContain(
      "Incomplete (2/2 recorded, 1 error finding)"
    );
    await expectNoAxeViolations(wrapper.element);
  });

  it("renders capability-keyed WorkQueuesPanel (truncation, Blinded/NotPermitted masking, deep-links) and NeedsAttention validation findings with zero axe violations", async () => {
    mockListWorkQueues.mockResolvedValue({
      queues: [
        {
          definition:
            "work_queue_definition:01960000-0000-7000-8000-000000000301@1",
          code: "source_verifications",
          version: 1,
          title: "Source data verifications due",
          capability: "observation.verify",
          truncated: true,
          items: [
            {
              subject: `observation:${OBS_ID}@1`,
              subject_kind: "observation",
              title: "VS · SYSBP (SUBJ-001)",
              since: "2026-06-01T10:00:00Z",
              masked: null,
              target: {
                study_ref: STUDY_REF,
                study_id: STUDY_ID,
                enrollment_id: ENROLLMENT_ID,
                form_id: FORM_ID,
                observation_ref: `observation:${OBS_ID}@1`,
                finding_ref: null,
                protocol_version_ref: null,
                proposal_ref: null,
              },
            },
            {
              subject: null,
              subject_kind: "observation",
              title: null,
              since: "2026-06-01T10:02:00Z",
              masked: "Blinded",
              target: null,
            },
            {
              subject: null,
              subject_kind: "observation",
              title: null,
              since: "2026-06-01T10:05:00Z",
              masked: "NotPermitted",
              target: null,
            },
          ],
        },
        {
          definition:
            "work_queue_definition:01960000-0000-7000-8000-000000000302@1",
          code: "ai_proposals",
          version: 1,
          title: "Proposals awaiting approval",
          capability: "proposal.approve",
          truncated: false,
          items: [
            {
              subject: PROPOSAL_REF,
              subject_kind: "proposal",
              title: "Draft query for out-of-window visit",
              since: "2026-06-01T09:50:00Z",
              masked: null,
              target: {
                study_ref: STUDY_REF,
                study_id: STUDY_ID,
                enrollment_id: null,
                form_id: null,
                observation_ref: null,
                finding_ref: null,
                protocol_version_ref: null,
                proposal_ref: PROPOSAL_REF,
              },
            },
          ],
        },
        {
          definition:
            "work_queue_definition:01960000-0000-7000-8000-000000000303@1",
          code: "protocol_reviews",
          version: 1,
          title: "Protocol versions in review",
          capability: "protocol.approve",
          truncated: false,
          items: [
            {
              subject: PROTOCOL_REF,
              subject_kind: "protocol_version",
              title: "CAD-101 Protocol v1.0",
              since: "2026-06-01T09:00:00Z",
              masked: null,
              target: {
                study_ref: STUDY_REF,
                study_id: STUDY_ID,
                enrollment_id: null,
                form_id: null,
                observation_ref: null,
                finding_ref: null,
                protocol_version_ref: PROTOCOL_REF,
                proposal_ref: null,
              },
            },
          ],
        },
      ],
    });

    mockListProposals.mockResolvedValue([
      {
        ref: PROPOSAL_REF,
        study_ref: STUDY_REF,
        proposed_by: "agent:protocol-copilot",
        model_id: "gemini-2.5-pro",
        intent: "Draft query for out-of-window visit",
        status: "Open",
        created_at: "2026-06-01T09:50:00Z",
        expires_at: "2026-06-08T09:50:00Z",
        masked: false,
        mask: null,
      },
    ]);
    mockListStudyValidationFindings.mockImplementation(
      async (_studyRef: string, opts?: { status?: string }) => ({
        total: opts?.status === "Open" ? 1 : 0,
        findings:
          opts?.status === "Open"
            ? [
                {
                  ref: FINDING_REF,
                  study_ref: STUDY_REF,
                  stage: "Collection",
                  pack_key: "cadence-edit-checks",
                  pack_item_ref: "library_item:pack@1",
                  rule_id: "VS_SYSBP_RANGE",
                  rule_version: 1,
                  severity: "Error",
                  status: "Open",
                  blocks: false,
                  classification: null,
                  decision: null,
                  target_kind: "DatasetRecord",
                  dataset: "VS",
                  record_key: FORM_ID,
                  variable: "SYSBP",
                  message: "Systolic BP 245 exceeds upper limit 220 mm[Hg].",
                  expected: "<= 220",
                  observed: [{ variable: "SYSBP", value: "245" }],
                  standard: null,
                  standard_version: null,
                  agency: null,
                  first_detected_run_ref:
                    "validation_run:01960000-0000-7000-8000-000000000180",
                  last_detected_run_ref:
                    "validation_run:01960000-0000-7000-8000-000000000180",
                  resolved_by_run_ref: null,
                },
              ]
            : [],
      })
    );

    const router = makeRouter();
    await router.push("/studies/CAD-101");
    await router.isReady();

    const queuesWrapper = mountAttached(WorkQueuesPanel, {
      props: { studyCode: "CAD-101" },
      global: { plugins: [router] },
    });
    await flushPromises();

    expect(
      queuesWrapper
        .find("[data-testid='work-queue-source_verifications']")
        .exists()
    ).toBe(true);
    expect(
      queuesWrapper
        .find("[data-testid='work-queue-count-source_verifications']")
        .text()
    ).toBe("3+");
    expect(
      queuesWrapper.find("[data-testid='work-queue-truncated']").text()
    ).toContain("Truncated (200+)");
    expect(
      queuesWrapper
        .find("[data-testid='work-item-masked-source_verifications-1']")
        .text()
    ).toContain("Masked (blinded)");
    expect(
      queuesWrapper
        .find("[data-testid='work-item-masked-source_verifications-2']")
        .text()
    ).toContain("Masked (not permitted)");

    const sdvHref = queuesWrapper
      .find("[data-testid='work-item-link-source_verifications-0']")
      .attributes("href");
    expect(sdvHref).toContain(`form=collection_instance:${FORM_ID}`);

    const proposalHref = queuesWrapper
      .find("[data-testid='work-item-link-ai_proposals-0']")
      .attributes("href");
    expect(proposalHref).toContain(`proposal=${PROPOSAL_REF}`);

    const protocolHref = queuesWrapper
      .find("[data-testid='work-item-link-protocol_reviews-0']")
      .attributes("href");
    expect(protocolHref).toContain("section=protocol-versions");

    await expectNoAxeViolations(queuesWrapper.element);

    const attentionWrapper = mountAttached(NeedsAttention, {
      props: { studyCode: "CAD-101" },
      global: { plugins: [router] },
    });
    await flushPromises();

    expect(attentionWrapper.find("#needs-attention-proposal-0").exists()).toBe(
      true
    );
    const findingLink = attentionWrapper.find("#needs-attention-finding-0");
    expect(findingLink.exists()).toBe(true);
    expect(findingLink.text()).toContain("[VS_SYSBP_RANGE]");
    expect(findingLink.attributes("href")).toContain(
      `form=collection_instance:${FORM_ID}`
    );
    expect(findingLink.attributes("href")).toContain("item=SYSBP");

    await expectNoAxeViolations(attentionWrapper.element);
  });
});
