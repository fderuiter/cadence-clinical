import { readFileSync } from "node:fs";
import { join } from "node:path";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it, vi } from "vitest";
import axe from "axe-core";
import { findSection, STAGES } from "../../src/canvas/lifecycle";
import { clearKernelStudyCache } from "../../src/canvas/kernelStudy";
import { AVAILABLE_SCREENS } from "../../src/router/availability.js";

const STUDY_ID = "01960000-0000-7000-8000-000000000001";
const STUDY_REF = `study:${STUDY_ID}`;
const SITE_ID = "01960000-0000-7000-8000-000000000011";
const SITE_REF = `study_site:${SITE_ID}`;
const ENROLLMENT_ID = "01960000-0000-7000-8000-000000000002";
const ENROLLMENT_REF = `enrollment:${ENROLLMENT_ID}`;
const FORM_ID = "01960000-0000-7000-8000-000000000003";
const FORM_REF = `collection_instance:${FORM_ID}`;
const OBS_ID = "01960000-0000-7000-8000-000000000004";
const OBS_REF = `observation:${OBS_ID}`;
const LOCK_ID = "01960000-0000-7000-8000-000000000005";
const SITE_A_ID = "01960000-0000-7000-8000-000000000006";
const SITE_A_REF = `study_site:${SITE_A_ID}`;

const mockCanI = vi.fn();
const mockListStudies = vi.fn();
const mockListStudyLocks = vi.fn();
const mockGetStudyLockReadiness = vi.fn();
const mockApplyLock = vi.fn();
const mockReleaseLock = vi.fn();
const mockStreamStudyExport = vi.fn();
const mockStreamEnrollmentCasebook = vi.fn();
const mockTriggerDownload = vi.fn();
const mockSearch = vi.fn();
const mockGetChildren = vi.fn();
const mockGetNode = vi.fn();
const mockGetObservation = vi.fn();
const mockGetZoomPath = vi.fn();

vi.mock("../../src/api/kernelApi", async () => {
  const actual = await vi.importActual<
    typeof import("../../src/api/kernelApi")
  >("../../src/api/kernelApi");
  return {
    ...actual,
    triggerBrowserDownload: (...args: unknown[]) =>
      mockTriggerDownload(...args),
    kernelApi: {
      ...actual.kernelApi,
      canI: (...args: unknown[]) => mockCanI(...args),
      listStudies: (...args: unknown[]) => mockListStudies(...args),
      listStudyLocks: (...args: unknown[]) => mockListStudyLocks(...args),
      getStudyLockReadiness: (...args: unknown[]) =>
        mockGetStudyLockReadiness(...args),
      applyLock: (...args: unknown[]) => mockApplyLock(...args),
      releaseLock: (...args: unknown[]) => mockReleaseLock(...args),
      streamStudyExport: (...args: unknown[]) => mockStreamStudyExport(...args),
      streamEnrollmentCasebook: (...args: unknown[]) =>
        mockStreamEnrollmentCasebook(...args),
      search: (...args: unknown[]) => mockSearch(...args),
      getChildren: (...args: unknown[]) => mockGetChildren(...args),
      getNode: (...args: unknown[]) => mockGetNode(...args),
      getObservation: (...args: unknown[]) => mockGetObservation(...args),
      getZoomPath: (...args: unknown[]) => mockGetZoomPath(...args),
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
        :data-username="username"
        :data-signer-name="signerName"
      >
        <button type="button" data-testid="confirm-step-up" @click="confirm">Confirm signature</button>
      </div>
    `,
    methods: {
      async confirm(this: {
        onSign: (tok: string, reason: string) => Promise<unknown>;
        $emit: (e: string) => void;
      }) {
        await this.onSign("sig-tok-verified", "Approved");
        this.$emit("success");
      },
    },
  },
}));

import { createMemoryHistory, createRouter } from "vue-router";
import {
  ASSERTION_SCOPE_NOT_READY,
  describeLockError,
} from "../../src/api/kernelApi";
import ExportsSection from "../../src/components/canvas/ExportsSection.vue";
import LocksSection from "../../src/components/canvas/LocksSection.vue";
import ParticipantRecord from "../../src/components/canvas/ParticipantRecord.vue";
import ScopeLockControls from "../../src/components/canvas/ScopeLockControls.vue";
import DataLockView from "../../src/views/DataLockView.vue";
import ExportWizardView from "../../src/views/ExportWizardView.vue";
import ObservationView from "../../src/views/ObservationView.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import { useAuthStore } from "../../src/stores/auth.js";
import { useStudyCanvasStore } from "../../src/stores/studyCanvas";

async function expectNoAxeViolations(element: Element) {
  const results = await axe.run(element, {
    rules: {
      region: { enabled: false },
    },
  });
  expect(results.violations).toEqual([]);
}

describe("Clinical Canvas locks, streaming exports, and inline controls (#5466, #5592, #5042)", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    clearKernelStudyCache();
    vi.clearAllMocks();
    mockListStudies.mockResolvedValue([
      {
        ref: STUDY_REF,
        code: "CAD-101",
        title: "Cardio Trial",
        phase: "Phase 2",
        status: "Active",
        sponsor_name: "Cadence Bio",
        sponsor_code: "CAD",
      },
    ]);
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
    mockGetNode.mockRejectedValue(new Error("unmocked getNode"));
    mockGetStudyLockReadiness.mockResolvedValue({
      study_ref: STUDY_REF,
      scope_ref: STUDY_REF,
      scope_kind: "study",
      ready_for_lock: true,
      open_query_count: 0,
      missing_required_item_count: 0,
      blockers: [],
      valid_at: "2026-06-01T10:00:00Z",
      known_at: "2026-06-01T10:00:00Z",
    });
    mockCanI.mockResolvedValue({
      capability: "lock.apply",
      verb_class: "Kernel",
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
    mockListStudyLocks.mockResolvedValue({
      study_ref: STUDY_REF,
      study_code: "CAD-101",
      valid_at: "2026-06-01T10:00:00Z",
      known_at: "2026-06-01T10:00:00Z",
      locks: [
        {
          ref: `assertion:${LOCK_ID}`,
          study_ref: STUDY_REF,
          scope_ref: ENROLLMENT_REF,
          scope_kind: "enrollment",
          subject_code: "SUBJ-A",
          kind: "Frozen",
          attestation_ref: "attestation:01960000-0000-7000-8000-000000000099",
          attestation_meaning: "Approved",
          locked_at: "2026-06-01T09:30:00Z",
          locked_by: "dm@cadence.test",
          reason_for_change: "Interim analysis freeze",
          released: false,
          release_ref: null,
          released_at: null,
          released_by: null,
          release_reason: null,
          masked: false,
          mask: null,
        },
        {
          ref: null,
          study_ref: STUDY_REF,
          scope_ref: null,
          scope_kind: "enrollment",
          subject_code: null,
          kind: "Locked",
          attestation_ref: null,
          attestation_meaning: null,
          locked_at: null,
          locked_by: null,
          reason_for_change: null,
          released: false,
          release_ref: null,
          released_at: null,
          released_by: null,
          release_reason: null,
          masked: true,
          mask: "NotPermitted",
        },
      ],
    });
  });

  it("places Locks in the Data stage and Exports in the Submit stage and renders both in StudyCanvasView", async () => {
    expect(findSection("locks")?.stage.id).toBe("data");
    expect(findSection("exports")?.stage.id).toBe("submit");
    const dataIds = STAGES.find((s) => s.id === "data")!.sections.map(
      (s) => s.id
    );
    const submitIds = STAGES.find((s) => s.id === "submit")!.sections.map(
      (s) => s.id
    );
    expect(dataIds).toContain("locks");
    expect(submitIds).toContain("exports");

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
        },
      ],
    });
    await router.push("/studies/CAD-101?stage=data&section=locks");
    await router.isReady();

    const canvasWrapper = mount(StudyCanvasView, {
      global: {
        plugins: [router],
        stubs: {
          StudyPulse: true,
          NeedsAttention: true,
          StudyActivity: true,
          CanvasInspector: true,
        },
      },
      attachTo: document.body,
    });
    await flushPromises();
    expect(canvasWrapper.find("#study-locks-section").exists()).toBe(true);

    await router.push("/studies/CAD-101?stage=submit&section=exports");
    await flushPromises();
    expect(canvasWrapper.find("#study-exports-section").exists()).toBe(true);
    canvasWrapper.unmount();
  });

  it("renders LocksSection with masked placeholders, applies and releases signed locks, and passes WCAG axe checks", async () => {
    mockApplyLock.mockResolvedValue({
      ref: `assertion:${LOCK_ID}`,
      study_ref: STUDY_REF,
      scope_ref: STUDY_REF,
      scope_kind: "study",
      subject_code: null,
      kind: "Locked",
      masked: false,
      mask: null,
      released: false,
    });
    mockReleaseLock.mockResolvedValue({
      ref: `assertion:${LOCK_ID}`,
      study_ref: STUDY_REF,
      scope_ref: ENROLLMENT_REF,
      scope_kind: "enrollment",
      subject_code: "SUBJ-A",
      kind: "Frozen",
      masked: false,
      mask: null,
      released: true,
    });

    const wrapper = mount(LocksSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();

    expect(wrapper.find("#lock-apply-scope-ref").exists()).toBe(false);
    expect(wrapper.text()).toContain("SUBJ-A");
    expect(wrapper.text()).toContain("Masked (NotPermitted)");
    await expectNoAxeViolations(wrapper.element);

    // Apply a study lock via step-up modal
    await wrapper.find("#lock-apply-kind").setValue("Locked");
    await wrapper
      .find("#lock-apply-reason")
      .setValue("Final database lock after query resolution");
    await wrapper.find("#study-locks-apply-form").trigger("submit");
    expect(
      wrapper
        .find("[data-testid='mock-sig-modal']")
        .attributes("data-action-url")
    ).toBe("/api/v1/kernel/locks");
    await wrapper.find("[data-testid='confirm-step-up']").trigger("click");
    await flushPromises();

    expect(mockApplyLock).toHaveBeenCalledWith(
      expect.objectContaining({
        studyRef: STUDY_REF,
        scopeRef: STUDY_REF,
        kind: "Locked",
        sigToken: "sig-tok-verified",
        reason: "Final database lock after query resolution",
      })
    );

    // Release an active lock via step-up modal
    await wrapper
      .find("#lock-release-reason")
      .setValue("Reopen enrollment for source chart addendum");
    await wrapper
      .find(`[data-testid='release-lock-assertion:${LOCK_ID}']`)
      .trigger("click");
    expect(
      wrapper
        .find("[data-testid='mock-sig-modal']")
        .attributes("data-action-url")
    ).toBe(`/api/v1/kernel/locks/assertion:${LOCK_ID}/release`);
    await wrapper.find("[data-testid='confirm-step-up']").trigger("click");
    await flushPromises();

    expect(mockReleaseLock).toHaveBeenCalledWith(
      expect.objectContaining({
        lockRef: `assertion:${LOCK_ID}`,
        studyRef: STUDY_REF,
        sigToken: "sig-tok-verified",
        reason: "Reopen enrollment for source chart addendum",
      })
    );
    wrapper.unmount();
  });

  it("navigates cascading site, enrollment, form, and observation pickers with K-AUTH-4 masking and enforces readiness blockers", async () => {
    mockGetChildren.mockImplementation(async (ref: string) => {
      if (ref === STUDY_REF) {
        return [
          {
            relation: "study_site",
            node: {
              ref: SITE_REF,
              kind: "study_site",
              code: "SITE-01",
              title: "Boston Cardiology",
              status: "Active",
              mask: null,
            },
          },
          {
            relation: "study_site",
            node: {
              ref: null,
              kind: "study_site",
              code: null,
              title: null,
              status: null,
              mask: "NotPermitted",
            },
          },
        ];
      }
      if (ref === SITE_REF) {
        return [
          {
            relation: "enrollment",
            node: {
              ref: ENROLLMENT_REF,
              kind: "enrollment",
              code: "SUBJ-A",
              title: "SUBJ-A",
              status: "Enrolled",
              mask: null,
            },
          },
        ];
      }
      if (ref === ENROLLMENT_REF) {
        return [
          {
            relation: "collection_instance",
            node: {
              ref: FORM_REF,
              kind: "collection_instance",
              code: "VITALS",
              title: "Vital Signs · Screening",
              status: "InProgress",
              mask: null,
            },
          },
        ];
      }
      if (ref === FORM_REF) {
        return [
          {
            relation: "observation",
            node: {
              ref: OBS_REF,
              kind: "observation",
              code: "concept:sbp",
              title: "concept:sbp · v1",
              status: "Current",
              mask: null,
            },
          },
          {
            relation: "observation",
            node: {
              ref: null,
              kind: "observation",
              code: null,
              title: null,
              status: null,
              mask: "Blinded",
            },
          },
        ];
      }
      return [];
    });

    mockGetStudyLockReadiness.mockResolvedValue({
      study_ref: STUDY_REF,
      scope_ref: OBS_REF,
      scope_kind: "observation",
      ready_for_lock: false,
      open_query_count: 1,
      missing_required_item_count: 1,
      blockers: [
        {
          kind: "open_query",
          owner: "Site SITE-01 investigator or coordinator",
          target_ref: OBS_REF,
          detail: "Open discrepancy query on systolic BP.",
          next_action: `Answer and close open query on ${OBS_REF} before applying Locked.`,
          masked: false,
          mask: null,
        },
        {
          kind: "missing_required_item",
          owner: null,
          target_ref: null,
          detail: null,
          next_action: "Authorized site team must resolve masked blocker.",
          masked: true,
          mask: "NotPermitted",
        },
      ],
      valid_at: "2026-06-01T10:00:00Z",
      known_at: "2026-06-01T10:00:00Z",
    });
    mockApplyLock.mockResolvedValue({
      ref: `assertion:${LOCK_ID}`,
      study_ref: STUDY_REF,
      scope_ref: OBS_REF,
      scope_kind: "observation",
      subject_code: "SUBJ-A",
      kind: "Frozen",
      masked: false,
      mask: null,
      released: false,
    });

    const wrapper = mount(LocksSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();

    // Select observation scope -> cascades site -> enrollment -> form -> observation automatically
    await wrapper.find("#lock-apply-scope-kind").setValue("observation");
    await flushPromises();

    expect(wrapper.find("[data-testid='lock-picker-site']").exists()).toBe(
      true
    );
    expect(
      wrapper.find("[data-testid='lock-picker-enrollment']").exists()
    ).toBe(true);
    expect(wrapper.find("[data-testid='lock-picker-form']").exists()).toBe(
      true
    );
    expect(
      wrapper.find("[data-testid='lock-picker-observation']").exists()
    ).toBe(true);

    // Masked site and blinded observation appear as disabled options (K-AUTH-4)
    const siteOptions = wrapper
      .find("[data-testid='lock-picker-site']")
      .findAll("option");
    expect(
      siteOptions.some(
        (opt) =>
          opt.text().includes("Masked (NotPermitted): site") &&
          opt.element.disabled
      )
    ).toBe(true);
    const obsOptions = wrapper
      .find("[data-testid='lock-picker-observation']")
      .findAll("option");
    expect(
      obsOptions.some(
        (opt) =>
          opt.text().includes("Masked (Blinded): observation") &&
          opt.element.disabled
      )
    ).toBe(true);

    // Readiness panel displays blocker counts, owner, target ref, next action, and masked blocker
    const readinessPanel = wrapper.find(
      "[data-testid='study-locks-readiness']"
    );
    expect(readinessPanel.exists()).toBe(true);
    expect(readinessPanel.text()).toContain("1 open query");
    expect(readinessPanel.text()).toContain("1 missing required item");
    expect(readinessPanel.text()).toContain(
      "Site SITE-01 investigator or coordinator"
    );
    expect(readinessPanel.text()).toContain("Masked (NotPermitted)");

    await wrapper
      .find("#lock-apply-reason")
      .setValue("Freeze while site answers open discrepancy");

    // Locked is disabled when readiness has blockers, while Frozen remains enabled (Req 1 & Req 2)
    await wrapper.find("#lock-apply-kind").setValue("Locked");
    expect(
      (wrapper.find("#btn-apply-study-lock").element as HTMLButtonElement)
        .disabled
    ).toBe(true);

    await wrapper.find("#lock-apply-kind").setValue("Frozen");
    expect(
      (wrapper.find("#btn-apply-study-lock").element as HTMLButtonElement)
        .disabled
    ).toBe(false);

    await wrapper.find("#study-locks-apply-form").trigger("submit");
    await wrapper.find("[data-testid='confirm-step-up']").trigger("click");
    await flushPromises();

    expect(mockApplyLock).toHaveBeenCalledWith(
      expect.objectContaining({
        studyRef: STUDY_REF,
        scopeRef: OBS_REF,
        kind: "Frozen",
      })
    );

    // Verify K.Assertion.ScopeNotReady structured explanation formatting
    const explanationMsg = describeLockError({
      status: 409,
      data: {
        code: ASSERTION_SCOPE_NOT_READY,
        detail: "Scope has 1 open query.",
        explanation: {
          subject: OBS_REF,
          claim: "Scope is not ready for Locked.",
          producer: "LockReadinessEvaluator",
          steps: [],
          conditions: [
            {
              code: "open_query",
              description: `Owner: Site SITE-01 investigator · Target: ${OBS_REF}`,
              met: false,
              evidence: [OBS_REF],
            },
          ],
          next_actions: [
            {
              description: `Close open query on ${OBS_REF}.`,
              capability: null,
              target: OBS_REF,
            },
          ],
        },
      },
    });
    expect(explanationMsg).toContain("Owner: Site SITE-01 investigator");
    expect(explanationMsg).toContain(`Close open query on ${OBS_REF}.`);

    wrapper.unmount();
  });

  it("streams study snapshots and SAS, R, and Study Archive ZIP bundles with SHA-256 digests in ExportsSection", async () => {
    const zipSha = "b".repeat(64);
    mockStreamStudyExport.mockResolvedValue({
      blob: new Blob(["PK\x03\x04"], { type: "application/zip" }),
      filename: "CAD-101-locked_only.study-archive.zip",
      contentType: "application/zip",
      contentSha256: zipSha,
    });

    const wrapper = mount(ExportsSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();
    await expectNoAxeViolations(wrapper.element);

    const formatValues = wrapper
      .find("#study-export-format")
      .findAll("option")
      .map((o) => (o.element as HTMLOptionElement).value);
    expect(formatValues).toEqual([
      "odm_xml",
      "dataset_json",
      "csv",
      "sas_bundle",
      "r_bundle",
      "study_archive",
    ]);

    await wrapper.find("#study-export-format").setValue("study_archive");
    await wrapper
      .find("#study-export-cut-preset")
      .setValue("2026-06-01T09:30:00Z");
    await wrapper.find("#study-exports-form").trigger("submit");
    await flushPromises();

    expect(mockStreamStudyExport).toHaveBeenCalledWith({
      studyRef: STUDY_REF,
      format: "study_archive",
      lockFilter: "locked_only",
      asOf: "2026-06-01T09:30:00Z",
    });
    expect(mockTriggerDownload).toHaveBeenCalledTimes(1);
    expect(wrapper.find("#study-exports-outcome").text()).toContain(
      "CAD-101-locked_only.study-archive.zip"
    );
    expect(
      wrapper.find("[data-testid='study-exports-sha256']").text()
    ).toContain(zipSha);
    wrapper.unmount();
  });

  it("renders inline participant lock readiness and streams certified PDF casebook with X-Content-Sha256 verification", async () => {
    const sha = "a".repeat(64);
    mockStreamEnrollmentCasebook.mockResolvedValue({
      blob: new Blob(["%PDF-1.7\n"], { type: "application/pdf" }),
      filename: "CAD-101-SUBJ-A.casebook.pdf",
      contentType: "application/pdf",
      contentSha256: sha,
    });

    const store = useStudyCanvasStore();
    store.studyCode = "CAD-101";
    store.record.enrollmentId = ENROLLMENT_ID;
    store.record.loading = false;
    store.record.error = null;

    const wrapper = mount(ParticipantRecord, {
      props: {
        participant: {
          id: "01960000-0000-7000-8000-000000000010",
          subject_id: "SUBJ-A",
          status: "ACTIVE",
          site_id: "SITE-A",
        } as never,
        canManage: true,
        formLocation: (id: string) => `/forms/${id}`,
        newFormLocation: "/forms/new",
        observationLocation: (obsId: string) => `/observations/${obsId}`,
      },
      global: {
        stubs: {
          ParticipantFormsPanel: true,
          QueriesPanel: true,
        },
      },
      attachTo: document.body,
    });
    await flushPromises();

    expect(
      wrapper.find("[data-testid='participant-lock-controls']").exists()
    ).toBe(true);
    await wrapper.find("#participant-casebook-format").setValue("pdf");
    await wrapper.find("#btn-export-casebook").trigger("click");
    await flushPromises();

    expect(mockStreamEnrollmentCasebook).toHaveBeenCalledWith({
      enrollmentRef: ENROLLMENT_REF,
      format: "pdf",
    });
    expect(
      wrapper.find("[data-testid='participant-casebook-sha256']").text()
    ).toContain(sha);
    wrapper.unmount();
  });

  it("supports inline freeze, lock readiness blocking, and unlock on form and observation scopes via ScopeLockControls and ObservationView", async () => {
    mockGetObservation.mockResolvedValue({
      ref: `observation:${OBS_ID}@1`,
      enrollment_ref: ENROLLMENT_REF,
      concept: "concept:sbp",
      value: { type: "quantity", value: 120.5, unit: "mm[Hg]" },
      observed_at: "2026-06-01T08:00:00Z",
      version: 1,
      locks: [
        {
          lock_id: LOCK_ID,
          kind: "Frozen",
          scope: `observation:${OBS_ID}`,
          attestation_id: "01960000-0000-7000-8000-000000000088",
          locked_at: "2026-06-01T08:30:00Z",
          locked_by: "dm@cadence.test",
          reason_for_change: "Monitor freeze",
        },
      ],
      revisions: [],
      assertions: [],
      audit: [],
    });
    mockGetZoomPath.mockResolvedValue([
      { level: "study", label: "CAD-101", node: STUDY_REF, mask: null },
      {
        level: "enrollment",
        label: "SUBJ-A",
        node: ENROLLMENT_REF,
        mask: null,
      },
    ]);
    mockReleaseLock.mockResolvedValue({
      ref: `assertion:${LOCK_ID}`,
      study_ref: STUDY_REF,
      scope_ref: `observation:${OBS_ID}`,
      scope_kind: "observation",
      subject_code: "SUBJ-A",
      kind: "Frozen",
      masked: false,
      mask: null,
      released: true,
    });

    const obsWrapper = mount(ObservationView, {
      props: {
        observationRef: `observation:${OBS_ID}@1`,
        studyCode: "CAD-101",
        embedded: true,
      },
      attachTo: document.body,
    });
    await flushPromises();

    expect(
      obsWrapper.find("[data-testid='observation-lock-controls']").exists()
    ).toBe(true);
    await obsWrapper
      .find("#observation-lock-reason")
      .setValue("Release observation freeze");
    await obsWrapper
      .find("[data-testid='observation-lock-unlock-frozen']")
      .trigger("click");
    await obsWrapper.find("[data-testid='confirm-step-up']").trigger("click");
    await flushPromises();

    expect(mockReleaseLock).toHaveBeenCalledWith(
      expect.objectContaining({
        lockRef: `assertion:${LOCK_ID}`,
        studyRef: STUDY_REF,
      })
    );
    obsWrapper.unmount();

    // Form-level ScopeLockControls check with open blocker disabling Lock while keeping Freeze enabled
    mockGetStudyLockReadiness.mockResolvedValueOnce({
      study_ref: STUDY_REF,
      scope_ref: FORM_REF,
      scope_kind: "collection_instance",
      ready_for_lock: false,
      open_query_count: 0,
      missing_required_item_count: 2,
      blockers: [
        {
          kind: "missing_required_item",
          owner: "Site SITE-01 coordinator",
          target_ref: FORM_REF,
          detail: "Required item HR missing on Vital Signs.",
          next_action: `Record required item HR on ${FORM_REF}.`,
          masked: false,
          mask: null,
        },
      ],
      valid_at: "2026-06-01T10:00:00Z",
      known_at: "2026-06-01T10:00:00Z",
    });

    const formLockWrapper = mount(ScopeLockControls, {
      props: {
        studyRef: STUDY_REF,
        scopeRef: FORM_REF,
        scopeLabel: "form",
        idPrefix: "ecrf-lock",
      },
      attachTo: document.body,
    });
    await flushPromises();
    await formLockWrapper
      .find("#ecrf-lock-reason")
      .setValue("Freeze form pending missing HR entry");
    expect(
      (formLockWrapper.find("#ecrf-lock-freeze").element as HTMLButtonElement)
        .disabled
    ).toBe(false);
    expect(
      (formLockWrapper.find("#ecrf-lock-lock").element as HTMLButtonElement)
        .disabled
    ).toBe(true);
    expect(
      formLockWrapper.find("[data-testid='ecrf-lock-readiness']").text()
    ).toContain("2 missing required items");
    formLockWrapper.unmount();
  });

  it("consolidates /data-lock and /exports into Clinical Canvas and removes legacy execution lock and SUBJ-001 blob stubs", async () => {
    const dataLockSource = readFileSync(
      join(__dirname, "..", "..", "src", "views", "DataLockView.vue"),
      "utf8"
    );
    const exportWizardSource = readFileSync(
      join(__dirname, "..", "..", "src", "views", "ExportWizardView.vue"),
      "utf8"
    );

    expect(dataLockSource).not.toContain("/api/v1/execution/locks");
    expect(exportWizardSource).not.toContain("SUBJ-001");
    expect(exportWizardSource).not.toContain("SUBJ-101");
    expect(exportWizardSource).not.toContain("new Blob");

    expect(AVAILABLE_SCREENS.has("data-lock")).toBe(true);
    expect(AVAILABLE_SCREENS.has("exports")).toBe(true);

    const stubRouter = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
      ],
    });
    await stubRouter.push("/studies/CAD-101");
    await stubRouter.isReady();

    const lockView = mount(DataLockView, {
      global: { plugins: [stubRouter] },
      attachTo: document.body,
    });
    await flushPromises();
    expect(lockView.find("#study-locks-section").exists()).toBe(true);
    lockView.unmount();

    const wizardView = mount(ExportWizardView, {
      global: { plugins: [stubRouter] },
      attachTo: document.body,
    });
    await flushPromises();
    expect(wizardView.find("#study-exports-section").exists()).toBe(true);
    wizardView.unmount();
  });

  it("resolves step-up username from preferred_username instead of email and signerName from firstName + lastName", async () => {
    const authStore = useAuthStore();
    authStore.user = {
      id: "47a9d88f-73d7-4aca-9fd6-14b8e6e9284f",
      username: "dm.lead",
      email: "dm.lead@cadence.local",
      firstName: "Dana",
      lastName: "Mercer",
      tenantId: "dev",
    };

    const locksWrapper = mount(LocksSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();

    await locksWrapper
      .find("#lock-apply-reason")
      .setValue("Verify signer binding");
    await locksWrapper.find("#study-locks-apply-form").trigger("submit");
    const locksModal = locksWrapper.find("[data-testid='mock-sig-modal']");
    expect(locksModal.attributes("data-username")).toBe("dm.lead");
    expect(locksModal.attributes("data-signer-name")).toBe("Dana Mercer");
    locksWrapper.unmount();

    const controlsWrapper = mount(ScopeLockControls, {
      props: {
        studyRef: STUDY_REF,
        scopeRef: ENROLLMENT_REF,
        scopeLabel: "participant",
        idPrefix: "participant-lock",
      },
      attachTo: document.body,
    });
    await flushPromises();

    await controlsWrapper
      .find("#participant-lock-reason")
      .setValue("Verify inline signer binding");
    await controlsWrapper.find("#participant-lock-freeze").trigger("click");
    const controlsModal = controlsWrapper.find(
      "[data-testid='mock-sig-modal']"
    );
    expect(controlsModal.attributes("data-username")).toBe("dm.lead");
    expect(controlsModal.attributes("data-signer-name")).toBe("Dana Mercer");
    controlsWrapper.unmount();
  });

  it("cascades parent-to-child scope pickers across study_site -> enrollment -> collection_instance -> observation and strips @version", async () => {
    mockGetChildren.mockImplementation(async (nodeRef: string) => {
      if (nodeRef === STUDY_REF) {
        return [
          {
            node: {
              ref: `${SITE_A_REF}@1`,
              kind: "study_site",
              code: "SITE-101",
              title: "Riverside University Hospital",
              status: "Active",
              mask: null,
            },
            child_count: 1,
            group: "US",
          },
          {
            node: {
              ref: null,
              kind: "study_site",
              code: null,
              title: null,
              status: null,
              mask: "NotPermitted",
            },
            child_count: null,
            group: null,
          },
        ];
      }
      if (nodeRef === SITE_A_REF) {
        return [
          {
            node: {
              ref: `${ENROLLMENT_REF}@1`,
              kind: "enrollment",
              code: "SUBJ-A",
              title: "SUBJ-A",
              status: "Active",
              mask: null,
            },
            child_count: null,
            group: null,
          },
        ];
      }
      if (nodeRef === ENROLLMENT_REF) {
        return [
          {
            node: {
              ref: `collection_instance:${FORM_ID}@2`,
              kind: "collection_instance",
              code: "cs.vitals",
              title: "Vital Signs · act.screening",
              status: null,
              mask: null,
            },
            child_count: 2,
            group: null,
          },
        ];
      }
      if (nodeRef === `collection_instance:${FORM_ID}`) {
        return [
          {
            node: {
              ref: null,
              kind: "observation",
              code: null,
              title: null,
              status: null,
              mask: "Blinded",
            },
            child_count: null,
            group: null,
          },
          {
            node: {
              ref: `observation:${OBS_ID}@1`,
              kind: "observation",
              code: "it.sbp",
              title: "Systolic Blood Pressure",
              status: null,
              mask: null,
            },
            child_count: 0,
            group: null,
          },
        ];
      }
      return [];
    });

    const wrapper = mount(LocksSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();

    // Select Observation scope: cascades site -> enrollment -> form -> observation automatically, skipping masked options
    await wrapper.find("#lock-apply-scope-kind").setValue("observation");
    await flushPromises();

    expect(wrapper.find("[data-testid='lock-picker-site']").exists()).toBe(
      true
    );
    expect(
      wrapper.find("[data-testid='lock-picker-enrollment']").exists()
    ).toBe(true);
    expect(wrapper.find("[data-testid='lock-picker-form']").exists()).toBe(
      true
    );
    expect(
      wrapper.find("[data-testid='lock-picker-observation']").exists()
    ).toBe(true);

    const obsOptions = wrapper
      .findAll("[data-testid='lock-picker-observation'] option")
      .map((o) => ({
        text: o.text(),
        disabled: (o.element as HTMLOptionElement).disabled,
      }));
    expect(obsOptions).toContainEqual({
      text: "Masked (Blinded): observation",
      disabled: true,
    });

    expect(
      (
        wrapper.find("[data-testid='lock-picker-observation']")
          .element as HTMLSelectElement
      ).value
    ).toBe(`observation:${OBS_ID}`);
    expect(mockGetStudyLockReadiness).toHaveBeenCalledWith(
      STUDY_REF,
      `observation:${OBS_ID}`
    );
    await expectNoAxeViolations(wrapper.element);
    wrapper.unmount();
  });

  it("renders inline Try again retry buttons in LocksSection and ExportsSection error banners while keeping SupportDetails", async () => {
    // 1. Study lookup failure + retry in LocksSection
    mockSearch.mockRejectedValueOnce(new Error("Gateway timeout"));
    const locksWrapper = mount(LocksSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();

    expect(locksWrapper.find("#study-locks-error").exists()).toBe(true);
    expect(locksWrapper.find("#study-locks-error").text()).toContain(
      "Gateway timeout"
    );
    expect(locksWrapper.find("#btn-retry-study-locks").exists()).toBe(true);

    // Next reload fails on listStudyLocks to test #study-locks-load-error retry too
    mockListStudyLocks.mockRejectedValueOnce(new Error("Read replica offline"));
    await locksWrapper.find("#btn-retry-study-locks").trigger("click");
    await flushPromises();

    expect(locksWrapper.find("#study-locks-load-error").exists()).toBe(true);
    expect(locksWrapper.find("#btn-retry-locks-list").exists()).toBe(true);
    await expectNoAxeViolations(locksWrapper.element);

    await locksWrapper.find("#btn-retry-locks-list").trigger("click");
    await flushPromises();
    expect(locksWrapper.find("#study-locks-load-error").exists()).toBe(false);
    expect(
      locksWrapper.find("[data-testid='study-locks-table']").exists()
    ).toBe(true);
    locksWrapper.unmount();

    // 2. Study lookup failure + retry in ExportsSection
    clearKernelStudyCache();
    mockSearch.mockRejectedValueOnce(new Error("Search service unavailable"));
    const exportsWrapper = mount(ExportsSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();

    expect(exportsWrapper.find("#study-exports-error").exists()).toBe(true);
    expect(exportsWrapper.find("#btn-retry-study-exports").exists()).toBe(true);
    await expectNoAxeViolations(exportsWrapper.element);

    await exportsWrapper.find("#btn-retry-study-exports").trigger("click");
    await flushPromises();
    expect(exportsWrapper.find("#study-exports-form").exists()).toBe(true);
    exportsWrapper.unmount();
  });

  it("renders parent covering lock banner in ParticipantRecord and hides covered Freeze and Lock buttons while keeping direct Unlock enabled", async () => {
    mockGetZoomPath.mockResolvedValue([
      { level: "study", label: "CAD-101", node: `${STUDY_REF}@1`, mask: null },
      {
        level: "study_site",
        label: "SITE-101",
        node: `${SITE_A_REF}@1`,
        mask: null,
      },
      {
        level: "enrollment",
        label: "SUBJ-A",
        node: `${ENROLLMENT_REF}@1`,
        mask: null,
      },
    ]);
    mockListStudyLocks.mockResolvedValue({
      study_ref: STUDY_REF,
      study_code: "CAD-101",
      valid_at: "2026-06-01T10:00:00Z",
      known_at: "2026-06-01T10:00:00Z",
      locks: [
        {
          ref: "assertion:01960000-0000-7000-8000-000000000090",
          study_ref: STUDY_REF,
          scope_ref: SITE_A_REF,
          scope_kind: "study_site",
          study_site_ref: SITE_A_REF,
          enrollment_ref: null,
          subject_code: null,
          kind: "Locked",
          attestation_ref: "attestation:01960000-0000-7000-8000-000000000091",
          attestation_meaning: "Approved",
          locked_at: "2026-06-01T09:00:00Z",
          locked_by: "dm.lead",
          reason_for_change: "Site-wide audit lock",
          released: false,
          masked: false,
          mask: null,
        },
        {
          ref: `assertion:${LOCK_ID}`,
          study_ref: STUDY_REF,
          scope_ref: ENROLLMENT_REF,
          scope_kind: "enrollment",
          study_site_ref: SITE_A_REF,
          enrollment_ref: ENROLLMENT_REF,
          subject_code: "SUBJ-A",
          kind: "Frozen",
          attestation_ref: "attestation:01960000-0000-7000-8000-000000000099",
          attestation_meaning: "Approved",
          locked_at: "2026-06-01T09:30:00Z",
          locked_by: "dm.lead",
          reason_for_change: "Participant review freeze",
          released: false,
          masked: false,
          mask: null,
        },
      ],
    });

    const store = useStudyCanvasStore();
    store.studyCode = "CAD-101";
    store.record.enrollmentId = ENROLLMENT_ID;
    store.record.loading = false;
    store.record.error = null;

    const wrapper = mount(ParticipantRecord, {
      props: {
        participant: {
          id: "01960000-0000-7000-8000-000000000010",
          subject_id: "SUBJ-A",
          status: "ACTIVE",
          site_id: "SITE-101",
        } as never,
        canManage: true,
        formLocation: (id: string) => `/forms/${id}`,
        newFormLocation: "/forms/new",
        observationLocation: (obsId: string) => `/observations/${obsId}`,
      },
      global: {
        stubs: {
          ParticipantFormsPanel: true,
          QueriesPanel: true,
        },
      },
      attachTo: document.body,
    });
    await flushPromises();

    const banner = wrapper.find("[data-testid='participant-locks-banner']");
    expect(banner.exists()).toBe(true);
    expect(banner.find("strong").text()).toBe("Locked.");
    expect(banner.text()).toContain("Site-wide audit lock");
    expect(banner.text()).toContain(SITE_A_REF);
    expect(
      wrapper.find("[data-testid='participant-lock-scope-note']").text()
    ).toContain("also blocked while a Locked assertion is in force");

    // Covered Freeze and Lock buttons are hidden because parent site has Locked, while direct Unlock (Frozen) remains visible and enabled!
    expect(wrapper.find("#participant-lock-freeze").exists()).toBe(false);
    expect(wrapper.find("#participant-lock-lock").exists()).toBe(false);
    await wrapper
      .find("#participant-lock-reason")
      .setValue("Release direct freeze while parent site lock holds");
    expect(
      (
        wrapper.find("[data-testid='participant-lock-unlock-frozen']")
          .element as HTMLButtonElement
      ).disabled
    ).toBe(false);

    await expectNoAxeViolations(wrapper.element);
    wrapper.unmount();

    // Also verify parent study Frozen banner without direct locks: hides Freeze, keeps Lock
    mockListStudyLocks.mockResolvedValueOnce({
      study_ref: STUDY_REF,
      study_code: "CAD-101",
      valid_at: "2026-06-01T10:00:00Z",
      known_at: "2026-06-01T10:00:00Z",
      locks: [
        {
          ref: "assertion:01960000-0000-7000-8000-000000000095",
          study_ref: STUDY_REF,
          scope_ref: STUDY_REF,
          scope_kind: "study",
          study_site_ref: null,
          enrollment_ref: null,
          subject_code: null,
          kind: "Frozen",
          attestation_ref: "attestation:01960000-0000-7000-8000-000000000096",
          attestation_meaning: "Approved",
          locked_at: "2026-06-01T08:00:00Z",
          locked_by: "dm.lead",
          reason_for_change: "Study interim freeze",
          released: false,
          masked: false,
          mask: null,
        },
      ],
    });
    const frozenWrapper = mount(ParticipantRecord, {
      props: {
        participant: {
          id: "01960000-0000-7000-8000-000000000010",
          subject_id: "SUBJ-A",
          status: "ACTIVE",
          site_id: "SITE-101",
        } as never,
        canManage: true,
        formLocation: (id: string) => `/forms/${id}`,
        newFormLocation: "/forms/new",
        observationLocation: (obsId: string) => `/observations/${obsId}`,
      },
      global: {
        stubs: {
          ParticipantFormsPanel: true,
          QueriesPanel: true,
        },
      },
      attachTo: document.body,
    });
    await flushPromises();
    const frozenBanner = frozenWrapper.find(
      "[data-testid='participant-locks-banner']"
    );
    expect(frozenBanner.exists()).toBe(true);
    expect(frozenBanner.find("strong").text()).toBe("Frozen.");
    expect(frozenBanner.text()).toContain("Study interim freeze");
    expect(frozenWrapper.find("#participant-lock-freeze").exists()).toBe(false);
    expect(frozenWrapper.find("#participant-lock-lock").exists()).toBe(true);
    frozenWrapper.unmount();
  });

  it("resolves person locked_by references to human labels, offers cascading scope pickers without raw kind:uuid inputs, and collapses load diagnostics", async () => {
    const siteRef = "study_site:01960000-0000-7000-8000-000000000017";
    const dmPersonRef = "person:01960000-0000-7000-8000-000000000099";
    mockGetChildren.mockImplementation(async (parentRef: string) => {
      if (parentRef === STUDY_REF) {
        return [
          {
            node: {
              ref: siteRef,
              kind: "study_site",
              code: "017",
              title: "Boston General",
              status: "Active",
              mask: null,
            },
            edge_kind: "Contains",
          },
        ];
      }
      if (parentRef === siteRef) {
        return [
          {
            node: {
              ref: ENROLLMENT_REF,
              kind: "enrollment",
              code: "SUBJ-017-01",
              title: "SUBJ-017-01",
              status: "Enrolled",
              mask: null,
            },
            edge_kind: "Contains",
          },
        ];
      }
      return [];
    });
    mockGetNode.mockImplementation(async (ref: string) => {
      if (ref === dmPersonRef) {
        return {
          ref,
          kind: "person",
          code: "DM-01",
          title: "Dr. Morgan Vance",
          status: "Active",
          mask: null,
        };
      }
      return {
        ref,
        kind: "study",
        code: "CAD-101",
        title: "CAD-101",
        status: "Active",
        mask: null,
      };
    });
    mockListStudyLocks
      .mockRejectedValueOnce(new Error("Upstream lock projection timeout"))
      .mockResolvedValueOnce({
        study_ref: STUDY_REF,
        as_of: null,
        locks: [
          {
            ref: `assertion:${LOCK_ID}`,
            study_ref: STUDY_REF,
            scope_ref: ENROLLMENT_REF,
            scope_kind: "enrollment",
            subject_code: "SUBJ-017-01",
            kind: "Frozen",
            masked: false,
            mask: null,
            locked_by: dmPersonRef,
            locked_at: "2026-10-15T10:00:00Z",
            reason_for_change: "Interim review cut",
            attestation_meaning: "Approved",
            released: false,
            released_at: null,
          },
        ],
      });

    const failedWrapper = mount(LocksSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();
    const errorDisclosure = failedWrapper.find(
      "#study-locks-load-error details.support-details"
    );
    expect(errorDisclosure.exists()).toBe(true);
    expect(errorDisclosure.attributes("open")).toBeUndefined();
    expect(errorDisclosure.text()).toContain(
      "Upstream lock projection timeout"
    );
    failedWrapper.unmount();

    const wrapper = mount(LocksSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();

    expect(wrapper.find("#lock-apply-scope-ref").exists()).toBe(false);
    expect(wrapper.html()).not.toContain('placeholder="kind:uuid"');
    expect(wrapper.find('[data-testid="study-locks-table"]').text()).toContain(
      "DM-01 · Dr. Morgan Vance"
    );

    await wrapper.find("#lock-apply-scope-kind").setValue("enrollment");
    await flushPromises();

    const siteSelect = wrapper.find("#lock-picker-site");
    expect(siteSelect.exists()).toBe(true);
    expect(siteSelect.text()).toContain("017 — Boston General");

    const enrollmentSelect = wrapper.find("#lock-picker-enrollment");
    expect(enrollmentSelect.exists()).toBe(true);
    expect(enrollmentSelect.text()).toContain("SUBJ-017-01");

    await expectNoAxeViolations(wrapper.element);
    wrapper.unmount();
  });
});
