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
    getSubjectDetail: vi.fn(),
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
    getChildren: vi.fn(async () => []),
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
      "No enrollment record is visible for 017-0001"
    );
    expect(ecrfQueriesApi.listForEnrollment).not.toHaveBeenCalled();
  });

  it("hydrates a participant opened by cold link, showing a loading message before resolving", async () => {
    let resolveSubjects;
    vi.mocked(executionApi.listSubjects).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveSubjects = resolve;
        })
    );

    await router.push(RECORD_URL);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await wrapper.vm.$nextTick();

    expect(wrapper.find("#record-loading").text()).toContain(
      "Loading participant"
    );
    expect(wrapper.find("#record-not-loaded").exists()).toBe(false);

    resolveSubjects({
      items: [PARTICIPANT],
      total: 1,
      limit: 200,
      offset: 0,
    });
    await flushPromises();

    expect(executionApi.listSubjects).toHaveBeenCalledWith({
      studyId: "CAD-101",
      limit: 200,
      offset: 0,
    });
    expect(findKernelEnrollmentRef).toHaveBeenCalledWith(
      "CAD-101",
      "017",
      "017-0001"
    );
    expect(wrapper.find("#participant-record-title").text()).toBe("017-0001");
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("017-0001");
    expect(wrapper.find("#record-not-loaded").exists()).toBe(false);
  });

  it("says a participant opened by link isn't loaded when absent or forbidden instead of guessing its code", async () => {
    const unknownId = "0192f3c4-5e6a-7b8c-9d0e-000000000099";
    await mountAt(
      `/studies/CAD-101?stage=conduct&participant=participant:${unknownId}`
    );

    expect(wrapper.find("#record-not-loaded").text()).toContain(
      "isn't among those loaded for CAD-101"
    );
    expect(findKernelEnrollmentRef).not.toHaveBeenCalled();

    wrapper.unmount();
    vi.mocked(executionApi.listSubjects).mockRejectedValueOnce(
      new ApiError("Forbidden", 403)
    );
    await mountAt(
      `/studies/CAD-102?stage=conduct&participant=participant:${PARTICIPANT_ID}`
    );
    expect(wrapper.find("#record-not-loaded").text()).toContain(
      "isn't among those loaded for CAD-102"
    );
  });

  it("hydrates a cold form link via ensureParticipantById", async () => {
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
      missing_required: [],
      content_hash: "sha256:abc",
      version: 1,
      locks: [],
      assertions: [],
      signatures: [],
      items: [],
    });
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([]);

    await mountAt(
      `/studies/CAD-101?stage=conduct&form=collection_instance:${FORM_ID}`
    );

    expect(wrapper.find("#zoom-participant").text()).toBe("017-0001");
    expect(wrapper.find("#canvas-form-back").text()).toBe("Back to 017-0001");
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("017-0001");
  });

  it("hydrates a cold observation link via resolveParticipantByCode", async () => {
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValue({
      subject_id: "017-0001",
      study_id: "CAD-101",
      status: "ACTIVE",
      site_id: "017",
      treatment_group: null,
      randomization_seed: null,
      investigational_product_id: null,
    });
    vi.mocked(kernelApi.getObservation).mockResolvedValue({
      ref: `observation:${OBSERVATION_ID}@1`,
      enrollment_ref: `enrollment:${ENROLLMENT_ID}`,
      concept: "concept:systolic@1",
      value: {
        type: "quantity",
        value: 120,
        unit: "mm[Hg]",
        comparator: null,
      },
      observed_at: "2027-03-14T09:00:00Z",
      version: 1,
      locks: [],
      revisions: [],
      assertions: [],
      audit: [],
    });
    vi.mocked(kernelApi.getZoomPath).mockResolvedValue([
      { level: "study_site", mask: null, label: "017" },
      { level: "enrollment", mask: null, label: "017-0001" },
    ]);

    await mountAt(
      `/studies/CAD-101?stage=conduct&observation=observation:${OBSERVATION_ID}`
    );

    expect(executionApi.getSubjectDetail).toHaveBeenCalledWith("017-0001");
    expect(wrapper.find("#zoom-participant").text()).toBe("017-0001");
    expect(wrapper.find("#observation-back").text()).toBe("Back to 017-0001");
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("017-0001");
  });

  describe("queries", () => {
    it("lists the participant's queries with their item, form and messages", async () => {
      await openRecord();

      const item = wrapper.find(`#query-${QUERY_ID}`);
      expect(item.text()).toContain("Open");
      expect(item.text()).toContain("SYSBP, revision 1");
      expect(item.text()).toContain("Vital Signs · WEEK2");
      expect(item.text()).not.toContain(`form ${FORM_ID}`);
      expect(item.text()).toContain("Value differs from the source note");
      expect(
        wrapper.find(`#query-open-form-${QUERY_ID}`).attributes("href")
      ).toBe(
        `/studies/CAD-101?stage=conduct&participant=participant:${PARTICIPANT_ID}&form=collection_instance:${FORM_ID}&item=SYSBP`
      );
    });

    it("links a query directly to its target form and observation inside Clinical Canvas with item highlight, standalone handoff, zoom path, and return navigation", async () => {
      const scrollSpy = vi.fn();
      const origScroll = HTMLElement.prototype.scrollIntoView;
      HTMLElement.prototype.scrollIntoView = scrollSpy;
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

      try {
        await openRecord();

        await wrapper.find(`#query-open-form-${QUERY_ID}`).trigger("click");
        await flushPromises();

        expect(router.currentRoute.value.query).toEqual({
          stage: "conduct",
          participant: `participant:${PARTICIPANT_ID}`,
          form: `collection_instance:${FORM_ID}`,
          item: "SYSBP",
        });
        expect(
          wrapper.find("#ecrf-row-SYSBP").attributes("data-highlighted")
        ).toBe("true");
        expect(scrollSpy).toHaveBeenCalledWith({ block: "center" });
        expect(document.activeElement).toBe(
          wrapper.find("#ecrf-item-SYSBP").element
        );
        expect(wrapper.find("#zoom-form").text()).toBe("Vital Signs · WEEK2");
        expect(wrapper.find("#zoom-form").attributes("aria-current")).toBe(
          "page"
        );

        const standaloneHref = wrapper
          .find("#workspace-handoff-standalone")
          .attributes("href");
        const standaloneUrl = new URL(standaloneHref, "http://localhost");
        expect(standaloneUrl.pathname).toBe("/ecrf");
        expect(
          Object.fromEntries(standaloneUrl.searchParams.entries())
        ).toEqual({
          studyId: "CAD-101",
          siteId: "017",
          subjectId: "017-0001",
          enrollmentId: ENROLLMENT_ID,
          formId: FORM_ID,
          collectionSpecId: "VS",
          plannedActivityId: "WEEK2",
          visitId: "WEEK2",
          itemId: "SYSBP",
        });

        await wrapper.find("#ecrf-item-observation-SYSBP").trigger("click");
        await flushPromises();

        expect(router.currentRoute.value.query).toEqual({
          stage: "conduct",
          participant: `participant:${PARTICIPANT_ID}`,
          form: `collection_instance:${FORM_ID}`,
          item: "SYSBP",
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
          item: "SYSBP",
        });
        expect(
          wrapper.find("#ecrf-row-SYSBP").attributes("data-highlighted")
        ).toBe("true");
      } finally {
        HTMLElement.prototype.scrollIntoView = origScroll;
      }
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

  describe("form launcher schedule caching and skeleton", () => {
    it("renders a skeleton while schedule options load, mounts <select> without <input> swap, caches per studyCode, and retries on failure", async () => {
      let resolveSchedule;
      vi.mocked(findStudyScheduleOptions).mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            resolveSchedule = resolve;
          })
      );

      await openRecord();
      await wrapper.find("#record-open-form").trigger("click");
      await flushPromises();

      // While scheduleLoading is true, skeleton renders and <input> is never mounted first.
      expect(wrapper.find("#ecrf-schedule-skeleton").exists()).toBe(true);
      expect(wrapper.find("#ecrf-collection-spec").exists()).toBe(false);
      expect(wrapper.find("#ecrf-planned-activity").exists()).toBe(false);

      resolveSchedule({
        protocolVersionRef:
          "protocol_version:0192f3c4-5e6a-7b8c-9d0e-00000000b001@1",
        collectionSpecs: [
          { id: "VS", name: "Vital Signs", plannedActivityIds: ["WEEK2"] },
        ],
        plannedActivities: [{ id: "WEEK2", name: "Week 2", studyDay: 14 }],
      });
      await flushPromises();

      expect(wrapper.find("#ecrf-schedule-skeleton").exists()).toBe(false);
      expect(wrapper.find("#ecrf-collection-spec").element.tagName).toBe(
        "SELECT"
      );
      expect(wrapper.find("#ecrf-planned-activity").element.tagName).toBe(
        "SELECT"
      );
      expect(findStudyScheduleOptions).toHaveBeenCalledTimes(1);

      // Navigating away and back to ?form=new for the same study reuses the cached schedule without refetching.
      await wrapper.find("#canvas-form-back").trigger("click");
      await flushPromises();
      await wrapper.find("#record-open-form").trigger("click");
      await flushPromises();

      expect(findStudyScheduleOptions).toHaveBeenCalledTimes(1);
      expect(wrapper.find("#ecrf-collection-spec").element.tagName).toBe(
        "SELECT"
      );

      // On another study, a failed schedule fetch renders an alert with Retry that recovers on click.
      const secondStudyParticipant = {
        ...PARTICIPANT,
        study_id: "CAD-202",
      };
      vi.mocked(executionApi.listSubjects).mockResolvedValue({
        items: [secondStudyParticipant],
        total: 1,
        limit: 50,
        offset: 0,
      });
      vi.mocked(findStudyScheduleOptions)
        .mockRejectedValueOnce(new Error("Kernel timeout"))
        .mockResolvedValueOnce({
          protocolVersionRef:
            "protocol_version:0192f3c4-5e6a-7b8c-9d0e-00000000b002@1",
          collectionSpecs: [
            { id: "LB", name: "Laboratory", plannedActivityIds: ["DAY1"] },
          ],
          plannedActivities: [{ id: "DAY1", name: "Day 1", studyDay: 1 }],
        });

      await router.push("/studies/CAD-202?section=participants");
      await flushPromises();
      await wrapper.find("#participant-017-0001").trigger("click");
      await flushPromises();
      await wrapper.find("#inspector-open-data-entry").trigger("click");
      await flushPromises();

      expect(wrapper.find("#ecrf-schedule-error").exists()).toBe(true);
      expect(wrapper.find("#ecrf-schedule-error").text()).toContain(
        "Kernel timeout"
      );

      await wrapper.find("#btn-retry-schedule").trigger("click");
      await flushPromises();

      expect(wrapper.find("#ecrf-schedule-error").exists()).toBe(false);
      expect(wrapper.find("#ecrf-collection-spec").element.tagName).toBe(
        "SELECT"
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

  it("resolves a cold deep-linked participant UUID in O(1) via getSubjectDetail without paging listSubjects, and rejects cross-study participants", async () => {
    const deepId = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d79";
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValueOnce({
      id: deepId,
      subject_id: "SUBJ-01",
      study_id: "CAD-101",
      site_id: "SITE-17",
      status: "ACTIVE",
      active_protocol_version: "3.0",
      enrollment_index: 12,
      treatment_group: null,
      randomization_seed: null,
      investigational_product_id: null,
    });

    await mountAt(
      `/studies/CAD-101?stage=conduct&participant=participant:${deepId}`
    );

    expect(executionApi.getSubjectDetail).toHaveBeenCalledWith(deepId);
    expect(executionApi.listSubjects).not.toHaveBeenCalled();
    expect(wrapper.find("#participant-record-title").text()).toBe("SUBJ-01");
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("SUBJ-01");

    wrapper.unmount();
    const crossStudyId = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d80";
    vi.mocked(executionApi.getSubjectDetail).mockResolvedValueOnce({
      id: crossStudyId,
      subject_id: "SUBJ-99",
      study_id: "OTHER-STUDY",
      site_id: "SITE-99",
      status: "ACTIVE",
      active_protocol_version: "1.0",
      enrollment_index: 1,
      treatment_group: null,
      randomization_seed: null,
      investigational_product_id: null,
    });

    await mountAt(
      `/studies/CAD-101?stage=conduct&participant=participant:${crossStudyId}`
    );

    expect(wrapper.find("#record-not-loaded").text()).toContain(
      "isn't among those loaded for CAD-101"
    );
    expect(executionApi.listSubjects).not.toHaveBeenCalled();
  });

  it("filters participants by server-backed search (?q=) and study site dropdown (?site=) while preserving filters on selection", async () => {
    vi.useFakeTimers();
    try {
      const siteParticipant = {
        id: "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d81",
        subject_id: "SUBJ-02",
        study_id: "CAD-101",
        site_id: "SITE-22",
        status: "ACTIVE",
        active_protocol_version: "3.0",
        enrollment_index: 2,
      };
      vi.mocked(executionApi.listSubjects).mockImplementation(
        async ({ q, siteId, limit = 50, offset = 0 }) => {
          const all = [PARTICIPANT, siteParticipant];
          const items = all.filter(
            (p) =>
              (!siteId || p.site_id === siteId) &&
              (!q || p.subject_id.toLowerCase().includes(q.toLowerCase()))
          );
          return { items, total: items.length, limit, offset };
        }
      );

      await mountAt("/studies/CAD-101?stage=conduct&section=participants");

      const searchInput = wrapper.find("#participants-search");
      await searchInput.setValue("SUBJ-02");
      vi.advanceTimersByTime(180);
      await flushPromises();

      expect(router.currentRoute.value.query).toEqual({
        stage: "conduct",
        section: "participants",
        q: "SUBJ-02",
      });
      expect(executionApi.listSubjects).toHaveBeenLastCalledWith({
        studyId: "CAD-101",
        status: undefined,
        siteId: undefined,
        q: "SUBJ-02",
        limit: 50,
        offset: 0,
      });

      const siteSelect = wrapper.find("#participants-site");
      await router.push(
        "/studies/CAD-101?stage=conduct&section=participants&site=SITE-22&q=SUBJ-02"
      );
      await flushPromises();
      expect(siteSelect.element.value).toBe("SITE-22");

      await wrapper.find("#participant-SUBJ-02").trigger("click");
      await flushPromises();

      expect(router.currentRoute.value.query).toEqual({
        stage: "conduct",
        section: "participants",
        site: "SITE-22",
        q: "SUBJ-02",
        node: `participant:${siteParticipant.id}`,
      });
    } finally {
      vi.useRealTimers();
    }
  });
});
