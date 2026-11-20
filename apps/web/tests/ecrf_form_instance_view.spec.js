import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import EcrfView from "../src/views/EcrfView.vue";
import EcrfFormPanel from "../src/components/ecrf/EcrfFormPanel.vue";
import SubjectEnrollmentForm from "../src/components/clinical/SubjectEnrollmentForm.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
import DesignerSchemaPanel from "../src/components/persona/DesignerSchemaPanel.vue";
import EhrReconciliationDrawer from "../src/components/clinical/EhrReconciliationDrawer.vue";
import { ecrfFormsApi } from "../src/api/ecrfFormsApi";
import { executionApi } from "../src/api/executionApi";
import { kernelApi } from "../src/api/kernelApi";
import { useAuthStore } from "../src/stores/auth";
import { useClinicalStore } from "../src/stores/clinical";
import { useToastStore } from "../src/stores/toasts";

let leaveGuard = null;
let routeQuery = {};
vi.mock("vue-router", () => ({
  onBeforeRouteLeave: (guard) => {
    leaveGuard = guard;
  },
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ query: routeQuery }),
}));

vi.mock("../src/api/executionApi", () => ({
  executionApi: { listSubjects: vi.fn() },
}));

vi.mock("../src/api/ecrfFormsApi", async (importOriginal) => ({
  ...(await importOriginal()),
  ecrfFormsApi: { openForm: vi.fn(), getForm: vi.fn(), saveValues: vi.fn() },
}));

// ADR-2622: the eCRF screen enters values in a kernel form instance, never in the deleted FormSubmission endpoints.
describe("eCRF screen: the participant's case report form", () => {
  let wrapper;
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    leaveGuard = null;
    routeQuery = {
      formId: "0192f3c4-5e6a-7b8c-9d0e-00000000f001",
      enrollmentId: "",
    };
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [],
      total: 0,
      limit: 200,
      offset: 0,
    });
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue({
      form_id: routeQuery.formId,
      collection_spec_id: "VS",
      planned_activity_id: null,
      collected_at: "2027-03-14T09:00:00Z",
      version: 1,
      complete: true,
      missing_required: [],
      items: [
        {
          item_id: "COMMENT",
          concept: null,
          unit: null,
          value_kind: "Text",
          required: false,
          value: null,
          observation_id: null,
          revision: 0,
          recorded_by: null,
          recorded_at: null,
        },
      ],
      locks: [],
      assertions: [],
      signatures: [],
    });
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it("outside demo mode renders only role badge, StudySubjectsBar, participant selector, and EcrfFormPanel while guarding unsaved edits", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Data Manager"];

    wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
        stubs: { "router-link": true },
      },
    });
    await flushPromises();

    // Focused kernel controls are present
    expect(wrapper.find("#study-subjects-bar").exists()).toBe(true);
    expect(wrapper.find("#ecrf-subject-selector").exists()).toBe(true);
    expect(wrapper.find("#ecrf-form-instance").exists()).toBe(true);
    expect(wrapper.find(".badge").text()).toBe("DATA_MANAGER");

    // Unbacked panels and demo controls are omitted
    expect(wrapper.findComponent(SubjectEnrollmentForm).exists()).toBe(false);
    expect(wrapper.findComponent(CrcFormRenderer).exists()).toBe(false);
    expect(wrapper.find(".ecrf-side-column").exists()).toBe(false);
    expect(wrapper.findComponent(DesignerSchemaPanel).exists()).toBe(false);
    expect(wrapper.findComponent(EhrReconciliationDrawer).exists()).toBe(false);
    expect(wrapper.find("#role-tester-select").exists()).toBe(false);
    expect(wrapper.find("#ecrf-manage-participants-link").exists()).toBe(false);

    const panel = wrapper.findComponent(EcrfFormPanel);
    expect(panel.props("formId")).toBe(routeQuery.formId);
    expect(panel.props("enrollmentId")).toBeNull();
    expect(ecrfFormsApi.getForm).toHaveBeenCalledWith(routeQuery.formId);
    expect(wrapper.find("#btn-pi-signoff").exists()).toBe(false);

    await wrapper.find("#ecrf-item-COMMENT").setValue("note");
    await flushPromises();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(leaveGuard()).toBe(false);
    expect(confirm).toHaveBeenCalled();

    const event = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });

  it("resolves participant enrollment via kernelStudy, populates #ecrf-enrollment-id, clears stale openError, and handles missing enrollments", async () => {
    routeQuery = { studyId: "CAD-101" };
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Site Investigator"];

    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [
        {
          id: "sub-1",
          subject_id: "017-0001",
          study_id: "CAD-101",
          site_id: "SITE-01",
          status: "ENROLLED",
        },
        {
          id: "sub-2",
          subject_id: "017-0002",
          study_id: "CAD-101",
          site_id: "SITE-01",
          status: "SCREENED",
        },
      ],
      total: 2,
      limit: 200,
      offset: 0,
    });

    vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: "study:0192f3c4-0000-7000-8000-00000000c101",
        kind: "study",
        code: "CAD-101",
        exact_code: true,
      },
    ]);
    vi.spyOn(kernelApi, "getChildren").mockImplementation(async (ref) => {
      if (ref === "study:0192f3c4-0000-7000-8000-00000000c101") {
        return [
          {
            node: {
              kind: "study_site",
              ref: "study_site:0192f3c4-0000-7000-8000-00000000a001",
              code: "SITE-01",
            },
          },
        ];
      }
      if (ref === "study_site:0192f3c4-0000-7000-8000-00000000a001") {
        return [
          {
            node: {
              kind: "enrollment",
              ref: "enrollment:0192f3c4-0000-7000-8000-00000000e001",
              code: "017-0001",
            },
          },
        ];
      }
      return [];
    });

    wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });
    await flushPromises();

    // First listed participant resolves its kernel enrollment automatically
    const selector = wrapper.find("#ecrf-subject-selector");
    expect(selector.element.value).toBe("017-0001");
    expect(wrapper.find("#ecrf-enrollment-id").element.value).toBe(
      "0192f3c4-0000-7000-8000-00000000e001"
    );

    // Clinical Canvas link is shown for staff who can manage participants
    const manageLink = wrapper.find("#ecrf-manage-participants-link");
    expect(manageLink.exists()).toBe(true);
    expect(manageLink.attributes("href")).toBe(
      "/studies/CAD-101?stage=conduct&section=participants"
    );

    // Trigger an openError on EcrfFormPanel, then switch participant
    vi.mocked(ecrfFormsApi.openForm).mockRejectedValueOnce(
      new Error("Collection spec unknown.")
    );
    await wrapper.find("#ecrf-collection-spec").setValue("MISSING");
    await wrapper.find("#ecrf-collected-at").setValue("2027-03-14T09:00");
    await wrapper.find("form#ecrf-form-new").trigger("submit");
    await flushPromises();
    expect(wrapper.find(".ecrf-form-error").exists()).toBe(true);

    // Switch to 017-0002 (who has no kernel enrollment yet)
    await selector.setValue("017-0002");
    await flushPromises();

    // Stale openError is cleared, enrollment field clears to "", and clear status message renders
    expect(wrapper.find(".ecrf-form-error").exists()).toBe(false);
    expect(wrapper.find("#ecrf-enrollment-id").element.value).toBe("");
    expect(wrapper.find("#ecrf-enrollment-status").text()).toContain(
      "No kernel enrollment found for 017-0002 in CAD-101."
    );
  });

  it("populates #ecrf-enrollment-id immediately when arriving from Clinical Canvas with ?enrollmentId=", async () => {
    routeQuery = {
      studyId: "CAD-101",
      subjectId: "017-0001",
      enrollmentId: "0192f3c4-0000-7000-8000-00000000e777",
    };
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [
        {
          id: "sub-1",
          subject_id: "017-0001",
          study_id: "CAD-101",
          site_id: "SITE-01",
          status: "ENROLLED",
        },
      ],
      total: 1,
      limit: 200,
      offset: 0,
    });
    const searchSpy = vi.spyOn(kernelApi, "search");

    wrapper = mount(EcrfView, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(wrapper.find("#ecrf-enrollment-id").element.value).toBe(
      "0192f3c4-0000-7000-8000-00000000e777"
    );
    expect(searchSpy).not.toHaveBeenCalled();
  });

  it("renders legacy persona panels in demo mode without calling window.alert during uploads, SDV toggles, or sample queries", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    routeQuery = {};
    const alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});

    wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
        stubs: {
          EhrReconciliationDrawer: true,
          "router-link": true,
        },
      },
    });
    await flushPromises();

    expect(wrapper.findComponent(SubjectEnrollmentForm).exists()).toBe(true);
    expect(wrapper.findComponent(CrcFormRenderer).exists()).toBe(true);
    expect(wrapper.find(".ecrf-side-column").exists()).toBe(true);
    expect(wrapper.findComponent(DesignerSchemaPanel).exists()).toBe(true);
    expect(wrapper.find("#role-tester-select").exists()).toBe(true);

    // 1. Trigger protocol upload -> toast instead of alert
    const clinicalStore = useClinicalStore(pinia);
    vi.spyOn(clinicalStore, "uploadProtocolDocument").mockResolvedValue({});
    wrapper
      .findComponent(DesignerSchemaPanel)
      .vm.$emit("trigger-document-upload", {
        target: {
          files: [
            new File(["pdf"], "protocol.pdf", { type: "application/pdf" }),
          ],
        },
      });
    await flushPromises();

    // 2. Trigger SDV toggle and confirm reason -> toast instead of alert
    await wrapper.find("#role-tester-select").setValue("cra");
    wrapper
      .findComponent(CrcFormRenderer)
      .vm.$emit("handle-sdv-toggle", "vssbp", true);
    await flushPromises();
    wrapper.vm.saveChange("Initial Entry");
    await flushPromises();

    // 3. Trigger empty sample query -> toast instead of alert
    wrapper
      .findComponent(CrcFormRenderer)
      .vm.$emit("create-query", "vssbp", "");
    await flushPromises();

    expect(alertSpy).not.toHaveBeenCalled();
    const titles = useToastStore(pinia).toasts.map((t) => t.title);
    expect(titles).toContain("Protocol ingested");
    expect(titles).toContain("SDV updated");
    expect(titles).toContain("Message required");
  });
});
