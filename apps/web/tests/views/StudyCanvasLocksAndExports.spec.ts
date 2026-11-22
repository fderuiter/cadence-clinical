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
const SITE_A_ID = "01960000-0000-7000-8000-000000000006";
const SITE_A_REF = `study_site:${SITE_A_ID}`;

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
import ExportsSection from "../../src/components/canvas/ExportsSection.vue";
import LocksSection from "../../src/components/canvas/LocksSection.vue";
import ParticipantRecord from "../../src/components/canvas/ParticipantRecord.vue";
import ScopeLockControls from "../../src/components/canvas/ScopeLockControls.vue";
import ObservationView from "../../src/views/ObservationView.vue";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import ExportWizardView from "../../src/views/ExportWizardView.vue";
import { useAuthStore } from "../../src/stores/auth.js";
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
    expect(toastStore.toasts.map((t: { title: string }) => t.title)).toEqual([
      "Exporting CDISC ODM-XML isn't available yet",
      "Exporting CSV dataset isn't available yet",
    ]);
    expect(mockTriggerDownload).not.toHaveBeenCalled();
    wizard.unmount();
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

    expect(wrapper.find("#lock-apply-site").exists()).toBe(true);
    expect(wrapper.find("#lock-apply-enrollment").exists()).toBe(true);
    expect(wrapper.find("#lock-apply-collection").exists()).toBe(true);
    expect(wrapper.find("#lock-apply-observation").exists()).toBe(true);

    const obsOptions = wrapper
      .findAll("#lock-apply-observation option")
      .map((o) => ({
        text: o.text(),
        disabled: (o.element as HTMLOptionElement).disabled,
      }));
    expect(obsOptions).toContainEqual({
      text: "Masked (Blinded): observation",
      disabled: true,
    });

    expect(
      (wrapper.find("#lock-apply-scope-ref").element as HTMLInputElement).value
    ).toBe(`observation:${OBS_ID}`);
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
});
