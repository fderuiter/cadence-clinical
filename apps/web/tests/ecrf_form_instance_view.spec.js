import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { reactive } from "vue";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import EcrfView from "../src/views/EcrfView.vue";
import EcrfFormPanel from "../src/components/ecrf/EcrfFormPanel.vue";
import SubjectEnrollmentForm from "../src/components/clinical/SubjectEnrollmentForm.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
import DesignerSchemaPanel from "../src/components/persona/DesignerSchemaPanel.vue";
import EhrReconciliationDrawer from "../src/components/clinical/EhrReconciliationDrawer.vue";
import { ecrfFormsApi } from "../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../src/api/ecrfQueriesApi";
import { executionApi } from "../src/api/executionApi";
import { kernelApi } from "../src/api/kernelApi";
import { useAuthStore } from "../src/stores/auth";
import { useClinicalStore } from "../src/stores/clinical";
import { useToastStore } from "../src/stores/toasts";

let leaveGuard = null;
let updateGuard = null;
const pushSpy = vi.fn();
const replaceSpy = vi.fn();
const routeQuery = reactive({
  formId: "0192f3c4-5e6a-7b8c-9d0e-00000000f001",
  enrollmentId: "",
});

function setRouteQuery(next) {
  for (const key of Object.keys(routeQuery)) {
    delete routeQuery[key];
  }
  Object.assign(routeQuery, next);
}
vi.mock("vue-router", () => ({
  onBeforeRouteLeave: (guard) => {
    leaveGuard = guard;
  },
  onBeforeRouteUpdate: (guard) => {
    updateGuard = guard;
  },
  useRouter: () => ({ push: pushSpy, replace: replaceSpy }),
  useRoute: () => ({ query: routeQuery }),
}));

vi.mock("../src/api/executionApi", () => ({
  executionApi: { listSubjects: vi.fn() },
}));

vi.mock("../src/api/ecrfQueriesApi", async (importOriginal) => ({
  ...(await importOriginal()),
  ecrfQueriesApi: {
    raise: vi.fn(),
    listForForm: vi.fn(async () => []),
    listForEnrollment: vi.fn(async () => []),
    listForStudy: vi.fn(async () => []),
    transition: vi.fn(),
  },
}));

vi.mock("../src/api/ecrfFormsApi", async (importOriginal) => ({
  ...(await importOriginal()),
  ecrfFormsApi: {
    listForEnrollment: vi.fn(async () => []),
    openForm: vi.fn(),
    getForm: vi.fn(),
    saveValues: vi.fn(),
  },
}));

function formFixture(formId) {
  return {
    form_id: formId,
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
  };
}

// ADR-2622: the eCRF screen enters values in a kernel form instance, never in the deleted FormSubmission endpoints.
describe("eCRF screen: the participant's case report form", () => {
  const FIRST_FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f001";
  const SECOND_FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f002";
  let wrapper;
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    leaveGuard = null;
    updateGuard = null;
    pushSpy.mockReset();
    replaceSpy.mockReset();
    setRouteQuery({
      formId: FIRST_FORM_ID,
      enrollmentId: "",
    });
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [],
      total: 0,
      limit: 200,
      offset: 0,
    });
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);
    vi.mocked(ecrfFormsApi.listForEnrollment).mockResolvedValue([]);
    vi.mocked(ecrfFormsApi.getForm).mockImplementation(async (id) =>
      formFixture(id)
    );
    vi.spyOn(kernelApi, "search").mockResolvedValue([]);
    vi.spyOn(kernelApi, "listProtocolVersions").mockResolvedValue([]);
    vi.spyOn(kernelApi, "getScheduleOfActivities").mockResolvedValue({
      version: "",
      title: "",
      epochs: [],
      columns: [],
      rows: [],
    });
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  function mountView() {
    return mount(EcrfView, {
      global: {
        plugins: [pinia],
        stubs: { "router-link": true },
      },
    });
  }

  it("outside demo mode renders only role badge, StudySubjectsBar, participant selector, and EcrfFormPanel while guarding leaving or switching forms", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Data Manager"];

    wrapper = mountView();
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
    expect(panel.props("formId")).toBe(FIRST_FORM_ID);
    expect(panel.props("enrollmentId")).toBeNull();
    expect(ecrfFormsApi.getForm).toHaveBeenCalledWith(FIRST_FORM_ID);
    expect(wrapper.find("#btn-pi-signoff").exists()).toBe(false);

    await wrapper.find("#ecrf-item-COMMENT").setValue("note");
    await flushPromises();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(leaveGuard()).toBe(false);
    expect(confirm).toHaveBeenCalledTimes(1);

    // Same-route formId switch or clear confirms via onBeforeRouteUpdate.
    expect(typeof updateGuard).toBe("function");
    expect(
      updateGuard(
        { query: { formId: SECOND_FORM_ID } },
        { query: { formId: FIRST_FORM_ID } }
      )
    ).toBe(false);
    expect(confirm).toHaveBeenCalledTimes(2);

    expect(
      updateGuard({ query: {} }, { query: { formId: FIRST_FORM_ID } })
    ).toBe(false);
    expect(confirm).toHaveBeenCalledTimes(3);

    // Unrelated query updates keeping the same formId do not prompt.
    expect(
      updateGuard(
        { query: { formId: FIRST_FORM_ID, itemId: "COMMENT" } },
        { query: { formId: FIRST_FORM_ID } }
      )
    ).toBeUndefined();
    expect(confirm).toHaveBeenCalledTimes(3);

    confirm.mockReturnValue(true);
    expect(
      updateGuard(
        { query: { formId: SECOND_FORM_ID } },
        { query: { formId: FIRST_FORM_ID } }
      )
    ).toBeUndefined();

    const event = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });

  it("resolves participant enrollment via kernelStudy, populates #ecrf-enrollment-id, clears stale openError, and handles missing enrollments", async () => {
    setRouteQuery({ studyId: "CAD-101" });
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

    // First listed participant resolves its kernel enrollment automatically and binds enrollmentContext
    const selector = wrapper.find("#ecrf-subject-selector");
    expect(selector.element.value).toBe("017-0001");
    expect(wrapper.find("#ecrf-enrollment-id").exists()).toBe(false);
    expect(
      wrapper.findComponent(EcrfFormPanel).props("enrollmentContext")
    ).toMatchObject({
      enrollmentId: "0192f3c4-0000-7000-8000-00000000e001",
      subjectCode: "017-0001",
    });
    expect(wrapper.find(".ecrf-form-hint").text()).toContain("017-0001");

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

  it("populates #ecrf-enrollment-id immediately when arriving from Clinical Canvas with ?enrollmentId= and syncs same-route participant updates", async () => {
    setRouteQuery({
      studyId: "CAD-101",
      subjectId: "017-0001",
      enrollmentId: "0192f3c4-0000-7000-8000-00000000e777",
    });
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
          status: "ENROLLED",
        },
      ],
      total: 2,
      limit: 200,
      offset: 0,
    });
    const childrenSpy = vi.spyOn(kernelApi, "getChildren");

    wrapper = mount(EcrfView, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(wrapper.find("#ecrf-enrollment-id").exists()).toBe(false);
    expect(
      wrapper.findComponent(EcrfFormPanel).props("enrollmentContext")
    ).toMatchObject({
      enrollmentId: "0192f3c4-0000-7000-8000-00000000e777",
      subjectCode: "017-0001",
    });
    // Enrollment came directly from ?enrollmentId= without walking study_site/enrollment children
    expect(childrenSpy).not.toHaveBeenCalled();

    // Navigating on the same /ecrf route to another participant updates selector and enrollmentContext
    setRouteQuery({
      studyId: "CAD-101",
      subjectId: "017-0002",
      enrollmentId: "0192f3c4-0000-7000-8000-00000000e888",
    });
    await flushPromises();

    expect(wrapper.find("#ecrf-subject-selector").element.value).toBe(
      "017-0002"
    );
    expect(
      wrapper.findComponent(EcrfFormPanel).props("enrollmentContext")
    ).toMatchObject({
      enrollmentId: "0192f3c4-0000-7000-8000-00000000e888",
      subjectCode: "017-0002",
    });
    expect(childrenSpy).not.toHaveBeenCalled();
  });

  it("renders legacy persona panels in demo mode without calling window.alert during uploads, SDV toggles, or sample queries", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    setRouteQuery({});
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

  it("syncs route.query.formId with router.replace on @form-loaded and @closed without duplicate prompts", async () => {
    setRouteQuery({ formId: "", enrollmentId: "" });
    replaceSpy.mockImplementation(async ({ query }) => {
      setRouteQuery({ ...query, formId: query.formId ?? "" });
    });

    wrapper = mountView();
    await flushPromises();

    await wrapper.find("#ecrf-form-id").setValue(SECOND_FORM_ID);
    await wrapper.find("#ecrf-form-load").trigger("submit");
    await flushPromises();

    expect(replaceSpy).toHaveBeenCalledWith({
      query: expect.objectContaining({ formId: SECOND_FORM_ID }),
    });
    expect(routeQuery.formId).toBe(SECOND_FORM_ID);

    // Syncing URL to the already-loaded form does not trigger confirm even if dirty.
    await wrapper.find("#ecrf-item-COMMENT").setValue("unsaved entry");
    await flushPromises();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    expect(
      updateGuard({ query: { formId: SECOND_FORM_ID } }, { query: {} })
    ).toBeUndefined();
    expect(confirm).not.toHaveBeenCalled();

    // Closing the form confirms once in closeForm and removes formId from route.query via router.replace.
    replaceSpy.mockClear();
    await wrapper.find("#ecrf-form-close").trigger("click");
    await flushPromises();

    expect(confirm).toHaveBeenCalledTimes(1);
    expect(replaceSpy).toHaveBeenCalledTimes(1);
    expect(replaceSpy.mock.calls[0][0].query.formId).toBeUndefined();
    expect(
      updateGuard({ query: {} }, { query: { formId: SECOND_FORM_ID } })
    ).toBeUndefined();
    expect(confirm).toHaveBeenCalledTimes(1);
  });

  it("pre-populates collectionSpecId and plannedActivityId (including visitId fallback) on Clinical Canvas handoff", async () => {
    setRouteQuery({
      studyId: "CAD-101",
      siteId: "SITE-01",
      subjectId: "017-0001",
      enrollmentId: "0192f3c4-0000-7000-8000-00000000e777",
      collectionSpecId: "VS",
      visitId: "SCR",
    });
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

    wrapper = mountView();
    await flushPromises();

    expect(wrapper.find("#ecrf-enrollment-id").exists()).toBe(false);
    expect(
      wrapper.findComponent(EcrfFormPanel).props("enrollmentContext")
    ).toMatchObject({
      enrollmentId: "0192f3c4-0000-7000-8000-00000000e777",
      subjectCode: "017-0001",
    });
    expect(wrapper.find("#ecrf-collection-spec").element.value).toBe("VS");
    expect(wrapper.find("#ecrf-planned-activity").element.value).toBe("SCR");

    // Updating query to another cell reactively updates spec & planned activity
    setRouteQuery({
      studyId: "CAD-101",
      siteId: "SITE-01",
      subjectId: "017-0001",
      enrollmentId: "0192f3c4-0000-7000-8000-00000000e777",
      collectionSpecId: "AE",
      plannedActivityId: "",
    });
    await flushPromises();

    expect(wrapper.find("#ecrf-collection-spec").element.value).toBe("AE");
    expect(wrapper.find("#ecrf-planned-activity").element.value).toBe("");
  });

  it("renders protocol <select> schedule pickers, lists ParticipantFormsPanel forms, and preserves observation return links outside demo mode", async () => {
    const ENR_ID = "0192f3c4-0000-7000-8000-00000000e777";
    const OBS_ID = "0192f3c4-0000-7000-8000-00000000a001";
    setRouteQuery({
      studyId: "CAD-101",
      siteId: "SITE-01",
      subjectId: "017-0001",
      enrollmentId: ENR_ID,
    });
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
    vi.mocked(ecrfFormsApi.listForEnrollment).mockResolvedValue([
      {
        form_id: FIRST_FORM_ID,
        enrollment_id: ENR_ID,
        protocol_version_id: "pv-1",
        collection_spec_id: "VS",
        collection_title: "Vital Signs",
        planned_activity_id: "WEEK2",
        collected_at: "2027-03-14T09:00:00Z",
        opened_at: "2027-03-14T09:00:00Z",
        opened_by: "crc1",
        complete: false,
        item_count: 2,
        recorded_count: 1,
        version: 1,
      },
    ]);
    vi.spyOn(kernelApi, "search").mockResolvedValue([
      {
        ref: "study:0192f3c4-0000-7000-8000-00000000c101",
        kind: "study",
        code: "CAD-101",
        exact_code: true,
      },
    ]);
    vi.spyOn(kernelApi, "listProtocolVersions").mockResolvedValue([
      {
        ref: "protocol_version:0192f3c4-0000-7000-8000-00000000b001@1",
        version: "1.0",
        status: "Approved",
        mask: null,
      },
    ]);
    vi.spyOn(kernelApi, "getScheduleOfActivities").mockResolvedValue({
      version: "protocol_version:0192f3c4-0000-7000-8000-00000000b001@1",
      title: "CAD-101 Schedule",
      epochs: [
        {
          epoch_id: "TREAT",
          name: "Treatment",
          order: 1,
          first_column: 0,
          column_count: 1,
        },
      ],
      columns: [
        {
          activity_id: "WEEK2",
          name: "Week 2",
          epoch_id: "TREAT",
          study_day: 14,
          window_before: 2,
          window_after: 2,
          locations: [],
        },
      ],
      rows: [
        {
          collection_spec_id: "VS",
          name: "Vital Signs",
          item_ids: ["SYSBP", "DIABP"],
          columns: [0],
        },
      ],
    });

    wrapper = mount(EcrfView, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    // Protocol schedule pickers render as <select> elements instead of raw text inputs
    const specSelect = wrapper.find("#ecrf-collection-spec");
    const visitSelect = wrapper.find("#ecrf-planned-activity");
    expect(specSelect.element.tagName).toBe("SELECT");
    expect(visitSelect.element.tagName).toBe("SELECT");
    expect(specSelect.text()).toContain("Vital Signs");
    expect(visitSelect.text()).toContain("Week 2");

    // ParticipantFormsPanel lists existing forms for the resolved enrollment
    expect(ecrfFormsApi.listForEnrollment).toHaveBeenCalledWith(ENR_ID);
    const formLink = wrapper.find(`#record-form-link-${FIRST_FORM_ID}`);
    expect(formLink.exists()).toBe(true);
    expect(formLink.text()).toContain("Vital Signs · WEEK2");
    expect(formLink.attributes("href")).toContain(`formId=${FIRST_FORM_ID}`);

    // Opening the form wires observationLocation with form, item, and returnTo back to /ecrf
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValueOnce({
      ...formFixture(FIRST_FORM_ID),
      collection_title: "Vital Signs",
      planned_activity_id: "WEEK2",
      items: [
        {
          item_id: "SYSBP",
          concept: "concept:systolic@1",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          value: { type: "quantity", value: 120, unit: "mm[Hg]" },
          observation_id: OBS_ID,
          revision: 1,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:05:00Z",
        },
      ],
    });
    setRouteQuery({
      studyId: "CAD-101",
      siteId: "SITE-01",
      subjectId: "017-0001",
      enrollmentId: ENR_ID,
      formId: FIRST_FORM_ID,
    });
    await flushPromises();

    const panel = wrapper.findComponent(EcrfFormPanel);
    const obsLocationFn = panel.props("observationLocation");
    expect(typeof obsLocationFn).toBe("function");
    const obsRoute = obsLocationFn(OBS_ID, FIRST_FORM_ID, "SYSBP");
    expect(obsRoute).toEqual({
      name: "observation",
      params: { observationRef: `observation:${OBS_ID}` },
      query: {
        studyId: "CAD-101",
        form: `collection_instance:${FIRST_FORM_ID}`,
        item: "SYSBP",
        returnTo: `/ecrf?studyId=CAD-101&siteId=SITE-01&subjectId=017-0001&enrollmentId=${ENR_ID}&formId=${FIRST_FORM_ID}&itemId=SYSBP`,
        returnLabel: "Back to Vital Signs · WEEK2",
      },
    });
  });
});
