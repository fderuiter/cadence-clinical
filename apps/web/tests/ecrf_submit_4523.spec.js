import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth";
import { useToastStore } from "../src/stores/toasts.js";
import EcrfView from "../src/views/EcrfView.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
import SubjectEnrollmentForm from "../src/components/clinical/SubjectEnrollmentForm.vue";

vi.mock("vue-router", () => ({
  onBeforeRouteLeave: () => {},
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

// Mock Execution API endpoints for subject listing
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

  it("sends the sample form nowhere outside a demo build: refuses it, records no ledger block and keeps its fields (ADR-2622)", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    const wrapper = mountView();
    await flushPromises();
    fetchSpy.mockClear();

    store.formValues.VSSBP = "120";
    const ledgerBefore = store.ledgerBlocks.length;

    wrapper.findComponent(CrcFormRenderer).vm.$emit("submit-ecrf");
    await flushPromises();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(store.ledgerBlocks.length).toBe(ledgerBefore);
    expect(store.formValues.VSSBP).toBe("120");
    expect(alertSpy).not.toHaveBeenCalled();
    expect(useToastStore(pinia).toasts).toContainEqual(
      expect.objectContaining({
        tone: "error",
        title: "Submitting the sample form isn't available yet",
      })
    );
    fetchSpy.mockRestore();
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
