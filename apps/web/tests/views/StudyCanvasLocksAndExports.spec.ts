import { readFileSync } from "node:fs";
import { join } from "node:path";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it, vi } from "vitest";
import axe from "axe-core";
import { findSection, STAGES } from "../../src/canvas/lifecycle";
import { AVAILABLE_SCREENS } from "../../src/router/availability.js";

const STUDY_ID = "01960000-0000-7000-8000-000000000001";
const STUDY_REF = `study:${STUDY_ID}`;
const ENROLLMENT_ID = "01960000-0000-7000-8000-000000000002";
const ENROLLMENT_REF = `enrollment:${ENROLLMENT_ID}`;
const FORM_ID = "01960000-0000-7000-8000-000000000003";
const OBS_ID = "01960000-0000-7000-8000-000000000004";
const LOCK_ID = "01960000-0000-7000-8000-000000000005";

const mockCanI = vi.fn();
const mockListStudyLocks = vi.fn();
const mockApplyLock = vi.fn();
const mockReleaseLock = vi.fn();
const mockStreamStudyExport = vi.fn();
const mockStreamEnrollmentCasebook = vi.fn();
const mockTriggerDownload = vi.fn();
const mockSearch = vi.fn();
const mockGetChildren = vi.fn();
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
      listStudyLocks: (...args: unknown[]) => mockListStudyLocks(...args),
      applyLock: (...args: unknown[]) => mockApplyLock(...args),
      releaseLock: (...args: unknown[]) => mockReleaseLock(...args),
      streamStudyExport: (...args: unknown[]) => mockStreamStudyExport(...args),
      streamEnrollmentCasebook: (...args: unknown[]) =>
        mockStreamEnrollmentCasebook(...args),
      search: (...args: unknown[]) => mockSearch(...args),
      getChildren: (...args: unknown[]) => mockGetChildren(...args),
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
      <div v-if="isOpen" data-testid="mock-sig-modal" :data-action-url="actionUrl" :data-semantic="semanticAction">
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
import ExportsSection from "../../src/components/canvas/ExportsSection.vue";
import LocksSection from "../../src/components/canvas/LocksSection.vue";
import ParticipantRecord from "../../src/components/canvas/ParticipantRecord.vue";
import ScopeLockControls from "../../src/components/canvas/ScopeLockControls.vue";
import ObservationView from "../../src/views/ObservationView.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import ExportWizardView from "../../src/views/ExportWizardView.vue";
import { useStudyCanvasStore } from "../../src/stores/studyCanvas";
import { useToastStore } from "../../src/stores/toasts.js";

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

  it("streams study snapshots at a selected lock timestamp in ExportsSection and passes WCAG axe checks", async () => {
    mockStreamStudyExport.mockResolvedValue({
      blob: new Blob(["<ODM/>"], { type: "application/xml" }),
      filename: "CAD-101-locked_only.odm.xml",
      contentType: "application/xml",
      contentSha256: null,
    });

    const wrapper = mount(ExportsSection, {
      props: { studyCode: "CAD-101" },
      attachTo: document.body,
    });
    await flushPromises();
    await expectNoAxeViolations(wrapper.element);

    await wrapper
      .find("#study-export-cut-preset")
      .setValue("2026-06-01T09:30:00Z");
    await wrapper.find("#study-exports-form").trigger("submit");
    await flushPromises();

    expect(mockStreamStudyExport).toHaveBeenCalledWith({
      studyRef: STUDY_REF,
      format: "odm_xml",
      lockFilter: "locked_only",
      asOf: "2026-06-01T09:30:00Z",
    });
    expect(mockTriggerDownload).toHaveBeenCalledTimes(1);
    expect(wrapper.find("#study-exports-outcome").text()).toContain(
      "CAD-101-locked_only.odm.xml"
    );
    wrapper.unmount();
  });

  it("renders inline participant lock controls and streams casebook with X-Content-Sha256 verification", async () => {
    const sha = "a".repeat(64);
    mockStreamEnrollmentCasebook.mockResolvedValue({
      blob: new Blob(["<html></html>"], { type: "text/html" }),
      filename: "CAD-101-SUBJ-A.casebook.html",
      contentType: "text/html",
      contentSha256: sha,
    });

    const store = useStudyCanvasStore();
    store.studyCode = "CAD-101";
    store.record.enrollmentId = ENROLLMENT_ID;
    store.record.loading = false;
    store.record.error = false;

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
    await wrapper.find("#btn-export-casebook").trigger("click");
    await flushPromises();

    expect(mockStreamEnrollmentCasebook).toHaveBeenCalledWith({
      enrollmentRef: ENROLLMENT_REF,
      format: "html",
    });
    expect(
      wrapper.find("[data-testid='participant-casebook-sha256']").text()
    ).toContain(sha);
    wrapper.unmount();
  });

  it("supports inline freeze, lock, and unlock on form and observation scopes via ScopeLockControls and ObservationView", async () => {
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

    // Form-level ScopeLockControls check
    const formLockWrapper = mount(ScopeLockControls, {
      props: {
        studyRef: STUDY_REF,
        scopeRef: `collection_instance:${FORM_ID}`,
        scopeLabel: "form",
        idPrefix: "ecrf-lock",
      },
      attachTo: document.body,
    });
    await flushPromises();
    expect(formLockWrapper.find("#ecrf-lock-freeze").exists()).toBe(true);
    expect(formLockWrapper.find("#ecrf-lock-lock").exists()).toBe(true);
    formLockWrapper.unmount();
  });

  it("removes fake SUBJ-101 export generators from ExportWizardView and keeps /data-lock and /exports hidden", async () => {
    const source = readFileSync(
      join(__dirname, "..", "..", "src", "views", "ExportWizardView.vue"),
      "utf8"
    );
    expect(source).not.toContain("SUBJ-101");
    expect(source).not.toContain("generateValidOdmXml");
    expect(source).not.toContain("generateValidCsv");
    expect(source).toContain("refuseUnbackedAction");

    expect(AVAILABLE_SCREENS.has("data-lock")).toBe(false);
    expect(AVAILABLE_SCREENS.has("exports")).toBe(false);

    const toastStore = useToastStore();
    const wizard = mount(ExportWizardView);
    await wizard.find(".btn-fast-export-odm").trigger("click");
    await wizard.find(".btn-fast-export-csv").trigger("click");
    expect(toastStore.toasts.map((t) => t.title)).toEqual([
      "Exporting CDISC ODM-XML isn't available yet",
      "Exporting CSV dataset isn't available yet",
    ]);
    expect(mockTriggerDownload).not.toHaveBeenCalled();
    wizard.unmount();
  });
});
