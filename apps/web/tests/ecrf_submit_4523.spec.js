import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth";
import { useToastStore } from "../src/stores/toasts.js";
import EcrfView from "../src/views/EcrfView.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
import SubjectEnrollmentForm from "../src/components/clinical/SubjectEnrollmentForm.vue";
import { executionApi } from "../src/api/executionApi";

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

// Mock Execution API endpoints for subject listing and form submission
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
    createFormSubmission: vi.fn(),
    completeFormSubmission: vi.fn(),
  },
}));

function httpError(status) {
  const err = new Error(`Request failed with status ${status}`);
  err.status = status;
  return err;
}

// #4523: eCRF submission integration with Execution API & failure handling
describe("EcrfView submitEcrf API integration and failure handling (#4523)", () => {
  let pinia;
  let store;
  let alertSpy;

  beforeEach(() => {
    vi.useRealTimers();
    pinia = createPinia();
    setActivePinia(pinia);
    store = useClinicalStore();
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Site Investigator"];
    store.ecrfFields = [];
    alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
    vi.clearAllMocks();
  });

  afterEach(() => {
    alertSpy.mockRestore();
    vi.unstubAllEnvs();
  });

  function mountView() {
    return mount(EcrfView, { global: { plugins: [pinia] } });
  }

  it("dispatches createFormSubmission and completeFormSubmission on submit, adds ledger block with submission ID and synced: true, and shows success toast", async () => {
    vi.mocked(executionApi.createFormSubmission).mockResolvedValueOnce({
      id: "form-sub-xyz-123",
      study_id: "TEST-STUDY",
      subject_id: "SUBJ-001",
      form_id: "VS_DEMO",
      status: "DRAFT",
    });
    vi.mocked(executionApi.completeFormSubmission).mockResolvedValueOnce({
      id: "form-sub-xyz-123",
      study_id: "TEST-STUDY",
      subject_id: "SUBJ-001",
      form_id: "VS_DEMO",
      status: "COMPLETED",
    });

    const pushSpy = vi.spyOn(useToastStore(pinia), "push");
    const wrapper = mountView();
    await flushPromises();

    wrapper.findComponent(CrcFormRenderer).vm.$emit("submit-ecrf");
    await flushPromises();

    expect(executionApi.createFormSubmission).toHaveBeenCalledWith({
      studyId: "TEST-STUDY",
      subjectId: "SUBJ-001",
      formId: "VS_DEMO",
      siteId: "TEST-SITE",
      visitId: "Week2",
      protocolVersion: "1.0.0",
      payload: "{}",
    });

    expect(executionApi.completeFormSubmission).toHaveBeenCalledWith(
      "form-sub-xyz-123"
    );

    expect(alertSpy).not.toHaveBeenCalled();

    await vi.waitFor(() => {
      expect(pushSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          tone: "success",
          title: "eCRF Form Submitted",
        })
      );
    });

    const submitBlock = store.ledgerBlocks.find(
      (b) => b.action === "SESSION_SUBMIT"
    );
    expect(submitBlock).toBeDefined();
    expect(submitBlock.synced).toBe(true);
    expect(submitBlock.submissionId).toBe("form-sub-xyz-123");
    expect(submitBlock.details.submissionId).toBe("form-sub-xyz-123");
    expect(submitBlock.details.synced).toBe(true);
  });

  it("handles failed network/API request: triggers error toast, records no ledger block, and preserves form fields", async () => {
    vi.mocked(executionApi.createFormSubmission).mockRejectedValueOnce(
      httpError(500)
    );

    const wrapper = mountView();
    await flushPromises();

    store.formValues.VSSBP = "120";

    const ledgerBefore = store.ledgerBlocks.length;

    wrapper.findComponent(CrcFormRenderer).vm.$emit("submit-ecrf");
    await flushPromises();

    expect(store.ledgerBlocks.length).toBe(ledgerBefore);
    expect(store.formValues.VSSBP).toBe("120");
    expect(alertSpy).not.toHaveBeenCalled();

    expect(useToastStore(pinia).toasts).toContainEqual(
      expect.objectContaining({
        tone: "error",
        title: "eCRF Form Submission Failed",
      })
    );
  });

  it("refuses screening, which no service evaluates, and shows no result", async () => {
    const wrapper = mountView();
    await flushPromises();
    const form = wrapper.findComponent(SubjectEnrollmentForm);
    expect(form.exists()).toBe(true);

    form.vm.$emit("screen", { subjectId: "SUBJ-001", studyId: "TEST-STUDY" });
    await flushPromises();

    expect(form.props("screeningResult")).toBe(null);
    expect(store.ledgerBlocks.some((b) => b.action === "SUBJECT_SCREEN")).toBe(
      false
    );
    expect(useToastStore(pinia).toasts[0]).toMatchObject({
      tone: "error",
      title: "Screening a subject isn't available yet",
    });
  });
});
