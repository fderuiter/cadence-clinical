import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import {
  findKernelEnrollmentRef,
  findStudyScheduleOptions,
} from "../../src/canvas/kernelStudy";
import { useAuthStore } from "../../src/stores/auth";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfFormsApi: {
      listForEnrollment: vi.fn(),
      openForm: vi.fn(),
      getForm: vi.fn(),
      saveValues: vi.fn(),
      signForm: vi.fn(),
    },
  };
});

vi.mock("../../src/api/ecrfQueriesApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfQueriesApi: {
      raise: vi.fn(),
      listForForm: vi.fn(),
      listForEnrollment: vi.fn(),
      transition: vi.fn(),
    },
  };
});

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    listProposals: vi.fn(async () => []),
    search: vi.fn(async () => []),
    getCommands: vi.fn(),
    getObservation: vi.fn(),
    getZoomPath: vi.fn(async () => []),
  },
}));

vi.mock("../../src/canvas/kernelStudy", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    findKernelEnrollmentRef: vi.fn(),
    findStudyScheduleOptions: vi.fn(async () => null),
  };
});

const PARTICIPANT_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000001";
const PARTICIPANT = {
  id: PARTICIPANT_ID,
  subject_id: "017-0001",
  study_id: "CAD-101",
  site_id: "017",
  status: "ACTIVE",
  active_protocol_version: "3.0",
  enrollment_index: 1,
};

const ENROLLMENT_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000e001";
const FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f001";
const OBSERVATION_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000a001";
const QUERY_ID = "query-a";

function formSummary(overrides = {}) {
  return {
    form_id: FORM_ID,
    enrollment_id: ENROLLMENT_ID,
    protocol_version_id: "0192f3c4-5e6a-7b8c-9d0e-00000000b001",
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
    ...overrides,
  };
}

function query(overrides = {}) {
  return {
    query_id: QUERY_ID,
    form_id: FORM_ID,
    enrollment_id: ENROLLMENT_ID,
    item_id: "SYSBP",
    observation_id: OBSERVATION_ID,
    revision: 1,
    current_revision: 1,
    value_changed: false,
    status: "open",
    text: "Value differs from the source note",
    messages: [
      {
        kind: "raised",
        text: "Value differs from the source note",
        by: "monitor",
        at: "2027-03-14T10:00:00Z",
      },
    ],
    raised_by: "monitor",
    raised_at: "2027-03-14T10:00:00Z",
    version: 1,
    ...overrides,
  };
}

const RECORD_URL = `/studies/CAD-101?stage=conduct&participant=participant:${PARTICIPANT_ID}`;

describe("Clinical Canvas: a participant's data", () => {
  let pinia;
  let router;
  let wrapper;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
        },
        { path: "/ecrf", name: "ecrf", component: { template: "<div />" } },
      ],
    });
    for (const fn of Object.values(executionApi)) vi.mocked(fn).mockReset();
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [PARTICIPANT],
      total: 1,
      limit: 50,
      offset: 0,
    });
    for (const fn of Object.values(ecrfFormsApi)) vi.mocked(fn).mockReset();
    vi.mocked(ecrfFormsApi.listForEnrollment).mockResolvedValue([
      formSummary(),
    ]);
    for (const fn of Object.values(ecrfQueriesApi)) vi.mocked(fn).mockReset();
    vi.mocked(findKernelEnrollmentRef).mockReset();
    vi.mocked(findKernelEnrollmentRef).mockResolvedValue({
      kind: "enrollment",
      id: ENROLLMENT_ID,
    });
    vi.mocked(findStudyScheduleOptions).mockReset();
    vi.mocked(findStudyScheduleOptions).mockResolvedValue(null);
    vi.mocked(ecrfQueriesApi.listForEnrollment).mockResolvedValue([query()]);
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  async function mountAt(url) {
    await router.push(url);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
  }

  // Selects the participant in Participants, then opens its data from the inspector.
  async function openRecord() {
    await mountAt("/studies/CAD-101?section=participants");
    await wrapper.find("#participant-017-0001").trigger("click");
    await flushPromises();
    await wrapper.find("#inspector-open-record").trigger("click");
    await flushPromises();
  }

  function signInWith(roles) {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = roles;
  }

  it("lists opened forms and queries for the participant's enrollment and opens forms inside Clinical Canvas", async () => {
    await openRecord();

    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      participant: `participant:${PARTICIPANT_ID}`,
    });
    expect(findKernelEnrollmentRef).toHaveBeenCalledWith(
      "CAD-101",
      "017",
      "017-0001"
    );
    expect(ecrfFormsApi.listForEnrollment).toHaveBeenCalledWith(ENROLLMENT_ID);
    expect(ecrfQueriesApi.listForEnrollment).toHaveBeenCalledWith(
      ENROLLMENT_ID
    );
    expect(wrapper.find("#record-open-ecrf").exists()).toBe(false);

    const formsPanel = wrapper.find("#record-forms");
    expect(formsPanel.text()).toContain("Vital Signs");
    expect(formsPanel.text()).toContain("WEEK2");
    expect(formsPanel.text()).toContain("1/2 recorded");

    const openExistingLink = wrapper.find(`#record-form-link-${FORM_ID}`);
    expect(openExistingLink.attributes("href")).toBe(
      `/studies/CAD-101?stage=conduct&participant=participant:${PARTICIPANT_ID}&form=collection_instance:${FORM_ID}`
    );

    const openNewLink = wrapper.find("#record-open-form");
    expect(openNewLink.attributes("href")).toBe(
      `/studies/CAD-101?stage=conduct&participant=participant:${PARTICIPANT_ID}&form=new`
    );

    // The zoom path ends at the participant, and the inspector shows it.
    expect(wrapper.find("#zoom-participant").attributes("aria-current")).toBe(
      "page"
    );
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("017-0001");
    expect(wrapper.find("#inspector-open-record").exists()).toBe(false);
  });

  it("says so when the participant's enrollment can't be looked up, and retries", async () => {
    vi.mocked(findKernelEnrollmentRef).mockRejectedValueOnce(
      Object.assign(new Error("Unavailable"), { status: 503 })
    );
    await openRecord();

    expect(wrapper.find("#record-error").text()).toContain(
      "017-0001's data couldn't be loaded."
    );
    expect(wrapper.find(`#query-${QUERY_ID}`).exists()).toBe(false);

    await wrapper.find("#btn-retry-record").trigger("click");
    await flushPromises();
    await wrapper.vm.$nextTick();
    expect(wrapper.find("#record-error").exists()).toBe(false);
    expect(wrapper.find(`#query-${QUERY_ID}`).exists()).toBe(true);
  });

  it("lists no queries, and says why, when the kernel has no enrollment for the participant", async () => {
    vi.mocked(findKernelEnrollmentRef).mockResolvedValue(null);
    await openRecord();

    expect(wrapper.find("#record-queries-no-enrollment").text()).toContain(
      "no enrollment for 017-0001"
    );
    expect(ecrfQueriesApi.listForEnrollment).not.toHaveBeenCalled();
  });

  it("says a participant opened by link isn't loaded instead of guessing its code", async () => {
    await mountAt(RECORD_URL);

    expect(wrapper.find("#record-not-loaded").text()).toContain(
      "isn't among those loaded for CAD-101"
    );
    expect(findKernelEnrollmentRef).not.toHaveBeenCalled();
    expect(executionApi.listSubjects).not.toHaveBeenCalled();
  });

  describe("queries", () => {
    it("lists the participant's queries with their item, form and messages", async () => {
      await openRecord();

      const item = wrapper.find(`#query-${QUERY_ID}`);
      expect(item.text()).toContain("Open");
      expect(item.text()).toContain("SYSBP, revision 1");
      expect(item.text()).toContain(`form ${FORM_ID}`);
      expect(item.text()).toContain("Value differs from the source note");
      expect(
        wrapper.find(`#query-open-form-${QUERY_ID}`).attributes("href")
      ).toBe(
        `/studies/CAD-101?stage=conduct&participant=participant:${PARTICIPANT_ID}&form=collection_instance:${FORM_ID}`
      );
    });

    it("links a query directly to its target form and observation inside Clinical Canvas with zoom path and return navigation", async () => {
      vi.mocked(ecrfFormsApi.getForm).mockResolvedValue({
        form_id: FORM_ID,
        enrollment_id: ENROLLMENT_ID,
        participant_id: PARTICIPANT_ID,
        protocol_version_id: "0192f3c4-5e6a-7b8c-9d0e-00000000b001",
        collection_spec_id: "VS",
        collection_title: "Vital Signs",
        planned_activity_id: "WEEK2",
        collected_at: "2027-03-14T09:00:00Z",
        opened_at: "2027-03-14T09:00:00Z",
        opened_by: "crc1",
        complete: false,
        missing_required: ["DIABP"],
        content_hash: "sha256:abc",
        version: 1,
        locks: [],
        assertions: [],
        signatures: [],
        items: [
          {
            item_id: "SYSBP",
            concept: "concept:systolic@1",
            unit: "mm[Hg]",
            value_kind: "quantity",
            required: true,
            value: {
              type: "quantity",
              value: 182,
              unit: "mm[Hg]",
              comparator: null,
            },
            observation_id: OBSERVATION_ID,
            revision: 1,
            recorded_by: "crc1",
            recorded_at: "2027-03-14T09:05:00Z",
          },
        ],
      });
      vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([query()]);
      vi.mocked(kernelApi.getObservation).mockResolvedValue({
        ref: `observation:${OBSERVATION_ID}@1`,
        enrollment_ref: `enrollment:${ENROLLMENT_ID}`,
        concept: "concept:systolic@1",
        value: {
          type: "quantity",
          value: 182,
          unit: "mm[Hg]",
          comparator: null,
        },
        observed_at: "2027-03-14T09:00:00Z",
        version: 1,
        locks: [],
        revisions: [
          {
            number: 1,
            value: {
              type: "quantity",
              value: 182,
              unit: "mm[Hg]",
              comparator: null,
            },
            reason_for_change: "Initial entry",
            recorded_by: "crc1",
            recorded_at: "2027-03-14T09:05:00Z",
            recorded_until: null,
          },
        ],
        assertions: [],
        audit: [],
      });

      await openRecord();

      await wrapper.find(`#query-open-form-${QUERY_ID}`).trigger("click");
      await flushPromises();

      expect(router.currentRoute.value.query).toEqual({
        stage: "conduct",
        participant: `participant:${PARTICIPANT_ID}`,
        form: `collection_instance:${FORM_ID}`,
      });
      expect(wrapper.find("#zoom-form").text()).toBe("Vital Signs · WEEK2");
      expect(wrapper.find("#zoom-form").attributes("aria-current")).toBe(
        "page"
      );

      await wrapper.find("#ecrf-item-observation-SYSBP").trigger("click");
      await flushPromises();

      expect(router.currentRoute.value.query).toEqual({
        stage: "conduct",
        participant: `participant:${PARTICIPANT_ID}`,
        form: `collection_instance:${FORM_ID}`,
        observation: `observation:${OBSERVATION_ID}`,
      });
      expect(wrapper.find("#zoom-study").text()).toBe("CAD-101");
      expect(wrapper.find("#zoom-site").text()).toBe("Site 017");
      expect(wrapper.find("#zoom-participant").text()).toBe("017-0001");
      expect(wrapper.find("#zoom-form").text()).toBe("Vital Signs · WEEK2");
      expect(wrapper.find("#zoom-observation").text()).toBe(
        "concept:systolic@1"
      );
      expect(wrapper.find("[data-testid=observation-concept]").text()).toBe(
        "concept:systolic@1"
      );

      const backLink = wrapper.find("#observation-back");
      expect(backLink.text()).toBe("Back to Vital Signs · WEEK2");
      await backLink.trigger("click");
      await flushPromises();
      expect(router.currentRoute.value.query).toEqual({
        stage: "conduct",
        participant: `participant:${PARTICIPANT_ID}`,
        form: `collection_instance:${FORM_ID}`,
      });
    });

    it("lets a site user answer an open query, and nothing more", async () => {
      signInWith(["crc"]);
      await openRecord();
      vi.mocked(ecrfQueriesApi.transition).mockResolvedValue(
        query({ status: "answered", version: 2 })
      );

      expect(wrapper.find(`#btn-close-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find(`#btn-cancel-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find("#record-queries-note").exists()).toBe(true);

      await wrapper.find(`#btn-answer-${QUERY_ID}`).trigger("click");
      await wrapper.vm.$nextTick();
      expect(
        wrapper.find(`#btn-confirm-answer-${QUERY_ID}`).attributes()
      ).toHaveProperty("disabled");
      await wrapper
        .find(`#query-text-${QUERY_ID}`)
        .setValue(" Corrected per source ");
      await wrapper
        .find(`#query-text-${QUERY_ID}`)
        .element.form.requestSubmit();
      await flushPromises();

      expect(ecrfQueriesApi.transition).toHaveBeenCalledWith(
        "answer",
        expect.objectContaining({
          formId: FORM_ID,
          queryId: QUERY_ID,
          text: "Corrected per source",
          reason: "Corrected per source",
        })
      );
      expect(wrapper.find(`#query-${QUERY_ID}`).text()).toContain("Answered");
      expect(wrapper.find(`#btn-answer-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find("#record-queries-outcome").text()).toBe(
        "Query answered."
      );
    });

    it("offers a CRA the transitions the module allows, one request per query", async () => {
      signInWith(["cra"]);
      vi.mocked(ecrfQueriesApi.listForEnrollment).mockResolvedValue([
        query({ status: "answered", value_changed: true, current_revision: 2 }),
      ]);
      await openRecord();

      expect(wrapper.find(`#btn-answer-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find(`#btn-close-${QUERY_ID}`).exists()).toBe(true);
      expect(wrapper.find(`#btn-reopen-${QUERY_ID}`).exists()).toBe(true);
      expect(wrapper.find(`#btn-cancel-${QUERY_ID}`).exists()).toBe(true);
      expect(wrapper.find('[data-testid="query-value-changed"]').text()).toBe(
        "Value changed (now revision 2)"
      );

      vi.mocked(ecrfQueriesApi.transition).mockResolvedValue(
        query({ status: "closed", version: 3 })
      );
      await wrapper.find(`#btn-close-${QUERY_ID}`).trigger("click");
      await wrapper.find(`#query-text-${QUERY_ID}`).setValue("Answer accepted");
      await wrapper
        .find(`#query-text-${QUERY_ID}`)
        .element.form.requestSubmit();
      await flushPromises();

      expect(ecrfQueriesApi.transition).toHaveBeenCalledTimes(1);
      expect(ecrfQueriesApi.transition).toHaveBeenCalledWith(
        "close",
        expect.objectContaining({
          queryId: QUERY_ID,
          reason: "Answer accepted",
        })
      );
      // A closed query can only be reopened.
      expect(wrapper.find(`#btn-close-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find(`#btn-cancel-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find(`#btn-reopen-${QUERY_ID}`).exists()).toBe(true);
    });

    it("keeps the query as it was and says why when the module refuses", async () => {
      signInWith(["cra"]);
      await openRecord();
      vi.mocked(ecrfQueriesApi.transition).mockRejectedValue(
        new ApiError("Forbidden", 403, "Forbidden", {
          code: "K.Authorization.Denied",
        })
      );

      await wrapper.find(`#btn-cancel-${QUERY_ID}`).trigger("click");
      await wrapper.find(`#query-text-${QUERY_ID}`).setValue("Raised in error");
      await wrapper
        .find(`#query-text-${QUERY_ID}`)
        .element.form.requestSubmit();
      await flushPromises();

      expect(wrapper.find(`#query-${QUERY_ID}`).text()).toContain("Open");
      // The typed reason stays so the user can try again.
      expect(wrapper.find(`#query-text-${QUERY_ID}`).element.value).toBe(
        "Raised in error"
      );
      expect(wrapper.find("#record-queries-outcome").text()).toContain(
        "You are not allowed"
      );
    });
  });

  describe("dirty form navigation guards and draft preservation", () => {
    const SECOND_FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f002";

    function loadedFormFixture(formId = FORM_ID) {
      return {
        form_id: formId,
        enrollment_id: ENROLLMENT_ID,
        participant_id: PARTICIPANT_ID,
        protocol_version_id: "0192f3c4-5e6a-7b8c-9d0e-00000000b001",
        collection_spec_id: "VS",
        collection_title: "Vital Signs",
        planned_activity_id: "WEEK2",
        collected_at: "2027-03-14T09:00:00Z",
        opened_at: "2027-03-14T09:00:00Z",
        opened_by: "crc1",
        complete: false,
        missing_required: ["DIABP"],
        content_hash: "sha256:abc",
        version: 1,
        locks: [],
        assertions: [],
        signatures: [],
        items: [
          {
            item_id: "SYSBP",
            concept: "concept:systolic@1",
            unit: "mm[Hg]",
            value_kind: "Quantity",
            required: true,
            value: {
              type: "quantity",
              value: 120,
              unit: "mm[Hg]",
              comparator: null,
            },
            observation_id: OBSERVATION_ID,
            revision: 1,
            recorded_by: "crc1",
            recorded_at: "2027-03-14T09:05:00Z",
          },
        ],
      };
    }

    beforeEach(() => {
      localStorage.clear();
      const auth = useAuthStore();
      auth.setAuth({
        authenticated: true,
        token: "token-crc-1",
        tokenParsed: {
          sub: "crc-1",
          tenant_id: "tenant-1",
          realm_access: { roles: ["CRC"] },
        },
      });
      vi.mocked(ecrfFormsApi.getForm).mockImplementation(async (id) =>
        loadedFormFixture(id)
      );
      vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);
    });

    afterEach(() => {
      localStorage.clear();
    });

    it("guards beforeunload, route leave, and route updates that unmount, clear, or switch a dirty form while preserving drafts", async () => {
      await openRecord();
      await wrapper.find(`#record-form-link-${FORM_ID}`).trigger("click");
      await flushPromises();

      await wrapper.find("#ecrf-item-SYSBP").setValue("136");
      await flushPromises();

      // beforeunload blocks tab close while dirty.
      const unloadEvent = new Event("beforeunload", { cancelable: true });
      window.dispatchEvent(unloadEvent);
      expect(unloadEvent.defaultPrevented).toBe(true);

      const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(false);

      // Inspector query changes on the same open form do not prompt.
      await router.push({
        name: "study",
        params: { studyCode: "CAD-101" },
        query: {
          ...router.currentRoute.value.query,
          ask: "Check vitals range",
        },
      });
      await flushPromises();
      expect(confirmSpy).not.toHaveBeenCalled();
      expect(router.currentRoute.value.query.ask).toBe("Check vitals range");
      await wrapper.find("#verb-details").trigger("click");

      // Navigating to ?form=new (clearing active form) prompts and cancels when rejected.
      await wrapper.find("#inspector-open-data-entry").trigger("click");
      await flushPromises();
      expect(confirmSpy).toHaveBeenCalledTimes(1);
      expect(router.currentRoute.value.query.form).toBe(
        `collection_instance:${FORM_ID}`
      );
      expect(wrapper.find("#ecrf-item-SYSBP").element.value).toBe("136");

      // Opening an observation (unmounting CanvasFormWorkspace) prompts and cancels when rejected.
      await wrapper.find("#ecrf-item-observation-SYSBP").trigger("click");
      await flushPromises();
      expect(confirmSpy).toHaveBeenCalledTimes(2);
      expect(router.currentRoute.value.query.observation).toBeUndefined();

      // Switching to another form prompts and cancels when rejected.
      await router.push(
        `/studies/CAD-101?stage=conduct&participant=participant:${PARTICIPANT_ID}&form=collection_instance:${SECOND_FORM_ID}`
      );
      await flushPromises();
      expect(confirmSpy).toHaveBeenCalledTimes(3);
      expect(router.currentRoute.value.query.form).toBe(
        `collection_instance:${FORM_ID}`
      );

      // Leaving the route altogether prompts and cancels when rejected.
      await router.push("/ecrf");
      await flushPromises();
      expect(confirmSpy).toHaveBeenCalledTimes(4);
      expect(router.currentRoute.value.name).toBe("study");

      // Confirming navigation to ?form=new flushes the <500ms draft to localStorage without deleting it and clears dirty state.
      confirmSpy.mockReturnValue(true);
      await wrapper.find("#inspector-open-data-entry").trigger("click");
      await flushPromises();
      expect(router.currentRoute.value.query.form).toBe("new");

      const cleanUnload = new Event("beforeunload", { cancelable: true });
      window.dispatchEvent(cleanUnload);
      expect(cleanUnload.defaultPrevented).toBe(false);

      // Returning to the form restores the preserved browser draft.
      await wrapper.find(`#record-form-link-${FORM_ID}`).trigger("click");
      await flushPromises();
      expect(wrapper.find("#ecrf-item-SYSBP").element.value).toBe("136");
    });

    it("skips confirmation when @form-loaded syncs ?form=new to the URL and avoids duplicate prompts on #ecrf-form-close", async () => {
      vi.mocked(ecrfFormsApi.openForm).mockResolvedValue({ form_id: FORM_ID });
      // Seed a saved draft so the form is immediately dirty as soon as openForm loads it.
      localStorage.setItem(
        `cadence_user:tenant-1:crc-1:ecrf_form_draft:${FORM_ID}`,
        JSON.stringify({
          form_id: FORM_ID,
          items: { SYSBP: { text: "142", missing: "", revision: 1 } },
        })
      );

      const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(true);

      await openRecord();
      await wrapper.find("#record-open-form").trigger("click");
      await flushPromises();
      expect(router.currentRoute.value.query.form).toBe("new");

      await wrapper.find("#ecrf-collection-spec").setValue("VS");
      await wrapper.find("#ecrf-collected-at").setValue("2027-03-14T09:00");
      await wrapper.find("#ecrf-form-new").trigger("submit");
      await flushPromises();

      // URL synced to collection_instance:<FORM_ID> without prompting confirm.
      expect(router.currentRoute.value.query.form).toBe(
        `collection_instance:${FORM_ID}`
      );
      expect(wrapper.find("#ecrf-item-SYSBP").element.value).toBe("142");
      expect(confirmSpy).not.toHaveBeenCalled();

      // Explicit #ecrf-form-close prompts once in closeForm, deletes draft, and navigates back without a second route guard prompt.
      await wrapper.find("#ecrf-form-close").trigger("click");
      await flushPromises();

      expect(confirmSpy).toHaveBeenCalledTimes(1);
      expect(router.currentRoute.value.query).toEqual({
        stage: "conduct",
        participant: `participant:${PARTICIPANT_ID}`,
      });
      expect(
        localStorage.getItem(
          `cadence_user:tenant-1:crc-1:ecrf_form_draft:${FORM_ID}`
        )
      ).toBeNull();
    });
  });
});
