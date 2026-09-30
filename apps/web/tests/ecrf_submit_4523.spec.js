import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth";
import { useToastStore } from "../src/stores/toasts.js";
import { executionService } from "../src/api/execution";
import EcrfView from "../src/views/EcrfView.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
import SubjectEnrollmentForm from "../src/components/clinical/SubjectEnrollmentForm.vue";

vi.mock("vue-router", () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({
    query: {
      studyId: "TEST-STUDY",
      siteId: "TEST-SITE",
      subjectId: "SUBJ-001",
      visitId: "Week2",
    },
  }),
}));

vi.mock("../src/api/execution", () => ({
  executionService: {
    submitForm: vi.fn(),
    screenSubject: vi.fn(),
    listLabAlerts: vi.fn().mockResolvedValue([]),
    listQueries: vi.fn().mockResolvedValue([]),
    syncQueries: vi.fn().mockResolvedValue({}),
  },
}));

// Outside a demo build the eCRF lists the study's subjects from Execution (#4828).
vi.mock("../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn().mockResolvedValue({
      items: [
        {
          id: "0198c6f2-0000-7000-8000-000000000001",
          subject_id: "SUBJ-001",
          study_id: "TEST-STUDY",
          site_id: "TEST-SITE",
          status: "ENROLLED",
          active_protocol_version: "2.0.0",
          enrollment_index: 1,
        },
      ],
      total: 1,
      limit: 200,
      offset: 0,
    }),
  },
}));

function httpError(status) {
  const err = new Error(`Request failed with status ${status}`);
  err.status = status;
  return err;
}

// #4523: a failed eCRF submission must never be recorded or announced as submitted.
describe("EcrfView submitEcrf API failure handling (#4523)", () => {
  let pinia;
  let store;
  let alertSpy;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    store = useClinicalStore();
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Site Investigator"];
    store.ecrfFields = [];
    vi.mocked(executionService.submitForm).mockReset();
    vi.mocked(executionService.screenSubject).mockReset();
    alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
  });

  afterEach(() => {
    alertSpy.mockRestore();
  });

  function mountView() {
    return mount(EcrfView, { global: { plugins: [pinia] } });
  }

  it.each([500, 502, 504])(
    "HTTP %i: no SESSION_SUBMIT ledger block, no success alert, error toast shown",
    async (status) => {
      vi.mocked(executionService.submitForm).mockRejectedValueOnce(
        httpError(status)
      );
      const wrapper = mountView();
      await flushPromises();
      const ledgerBefore = store.ledgerBlocks.length;

      wrapper.findComponent(CrcFormRenderer).vm.$emit("submit-ecrf");
      await flushPromises();

      expect(executionService.submitForm).toHaveBeenCalledTimes(1);
      expect(executionService.submitForm).toHaveBeenCalledWith(
        expect.objectContaining({
          study_id: "TEST-STUDY",
          site_id: "TEST-SITE",
          subject_id: "SUBJ-001",
          protocol_version: "2.0.0",
        })
      );
      expect(
        store.ledgerBlocks.some((b) => b.action === "SESSION_SUBMIT")
      ).toBe(false);
      expect(store.ledgerBlocks.length).toBe(ledgerBefore);
      expect(alertSpy).not.toHaveBeenCalled();
      const toasts = useToastStore().toasts;
      expect(toasts).toHaveLength(1);
      expect(toasts[0]).toMatchObject({
        tone: "error",
        title: "eCRF submission failed",
      });
      expect(toasts[0].message).toContain(`HTTP ${status}`);
    }
  );

  it("records and announces a successful submission", async () => {
    vi.mocked(executionService.submitForm).mockResolvedValueOnce({});
    const wrapper = mountView();
    await flushPromises();

    wrapper.findComponent(CrcFormRenderer).vm.$emit("submit-ecrf");

    // EcrfView hashes the submission with crypto.subtle, which can outlast one
    // flushPromises() when the whole suite runs in parallel.
    await vi.waitFor(
      () => {
        expect(
          store.ledgerBlocks.some((b) => b.action === "SESSION_SUBMIT")
        ).toBe(true);
        expect(alertSpy).toHaveBeenCalledWith(
          expect.stringContaining("successfully submitted")
        );
      },
      { timeout: 2000 }
    );
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it("a failed screening leaves no screening result and shows an error toast", async () => {
    vi.mocked(executionService.screenSubject).mockRejectedValueOnce(
      httpError(502)
    );
    const wrapper = mountView();
    await flushPromises();
    const form = wrapper.findComponent(SubjectEnrollmentForm);
    expect(form.exists()).toBe(true);

    form.vm.$emit("screen", { subjectId: "SUBJ-001", studyId: "TEST-STUDY" });
    await flushPromises();

    expect(form.props("screeningResult")).toBe(null);
    expect(form.props("isScreening")).toBe(false);
    expect(store.ledgerBlocks.some((b) => b.action === "SUBJECT_SCREEN")).toBe(
      false
    );
    expect(useToastStore().toasts[0]).toMatchObject({
      tone: "error",
      title: "Subject screening failed",
    });
  });
});
