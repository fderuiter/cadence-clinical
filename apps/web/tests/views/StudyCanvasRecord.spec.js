import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { useAuthStore } from "../../src/stores/auth";
import { useToastStore } from "../../src/stores/toasts.js";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    listObservations: vi.fn(),
    listFormSubmissions: vi.fn(),
    verifyObservationSdv: vi.fn(),
    flagObservationSdv: vi.fn(),
    listQueries: vi.fn(),
    raiseQuery: vi.fn(),
    respondQuery: vi.fn(),
    closeQuery: vi.fn(),
    reopenQuery: vi.fn(),
    cancelQuery: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

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

function observation(id, overrides = {}) {
  return {
    id,
    subject_id: "017-0001",
    study_id: "CAD-101",
    domain: "VS",
    test_code: "SYSBP",
    test_name: "Systolic blood pressure",
    observation_date: "2027-03-14",
    visit_id: "Week 12",
    value: 128,
    value_string: null,
    unit: "mmHg",
    is_outlier: false,
    is_sdv_verified: false,
    sdv_verified_by: null,
    sdv_verified_at: null,
    ...overrides,
  };
}

const OBS_ID = "6b1f0a52-4c1d-4f0e-9a51-0000000000a1";
const OBSERVATIONS = [
  observation(OBS_ID),
  observation("6b1f0a52-4c1d-4f0e-9a51-0000000000a2", {
    domain: "LB",
    test_code: "K",
    test_name: "Potassium",
    visit_id: "Screening",
    observation_date: "2027-01-02",
    value: 5.9,
    unit: "mmol/L",
    is_out_of_range: true,
    range_indicator: "HIGH",
    is_sdv_verified: true,
    sdv_verified_by: "monitor1",
    sdv_verified_at: "2027-01-05T10:00:00Z",
  }),
];
const FORMS = [
  {
    id: "f1",
    study_id: "CAD-101",
    subject_id: "017-0001",
    form_id: "VITALS",
    status: "COMPLETED",
    visit_id: "Week 12",
    protocol_version: "3.0",
    is_active: true,
    is_readonly: true,
    version: 2,
  },
];

const QUERY_ID = "9a2d6f10-1111-4c2b-8d3e-0000000000b1";

function query(overrides = {}) {
  return {
    id: QUERY_ID,
    study_id: "CAD-101",
    subject_id: "017-0001",
    test_code: "SYSBP",
    observation_id: OBS_ID,
    visit_id: "Week 12",
    explanation: "Value differs from the source note",
    status: "OPEN",
    response: null,
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
    vi.mocked(executionApi.listObservations).mockResolvedValue(
      structuredClone(OBSERVATIONS)
    );
    vi.mocked(executionApi.listFormSubmissions).mockResolvedValue(
      structuredClone(FORMS)
    );
    vi.mocked(executionApi.listQueries).mockResolvedValue([query()]);
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

  it("opens a participant's observations by visit and its form submissions from Execution", async () => {
    await openRecord();

    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      participant: `participant:${PARTICIPANT_ID}`,
    });
    expect(executionApi.listObservations).toHaveBeenCalledWith(
      "CAD-101",
      "017-0001"
    );
    expect(executionApi.listFormSubmissions).toHaveBeenCalledWith(
      "CAD-101",
      "017-0001"
    );

    const record = wrapper.find("#participant-record");
    expect(record.findAll(".visit-title").map((h) => h.text())).toEqual([
      "Screening",
      "Week 12",
    ]);
    expect(record.text()).toContain("5.9 mmol/L");
    expect(record.text()).toContain("Out of range");
    expect(wrapper.find("#record-forms").text()).toContain("VITALS");
    expect(wrapper.find("#record-forms").text()).toContain("Completed, locked");
    // The zoom path now ends at the participant, and the inspector shows it.
    expect(wrapper.find("#zoom-participant").attributes("aria-current")).toBe(
      "page"
    );
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("017-0001");
    expect(wrapper.find("#inspector-open-record").exists()).toBe(false);
  });

  it("fills the inspector with a selected observation, without inventing a kernel reference", async () => {
    await openRecord();

    await wrapper.find(`#observation-${OBS_ID}`).trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.query.observation).toBe(OBS_ID);
    const inspector = wrapper.find("#canvas-inspector");
    expect(inspector.find("#canvas-inspector-title").text()).toBe(
      "Systolic blood pressure"
    );
    expect(inspector.find("#canvas-inspector-ref").text()).toContain(
      "no kernel reference yet"
    );
    expect(inspector.find("#inspector-result").text()).toBe("128 mmHg");
    expect(inspector.find("#inspector-sdv-status").text()).toBe(
      "Not verified."
    );
    expect(wrapper.find("#zoom-observation").text()).toBe(
      "Systolic blood pressure"
    );
    expect(wrapper.find("#zoom-participant").element.tagName).toBe("A");
  });

  it("verifies an observation for a monitor and shows who Execution recorded", async () => {
    signInWith(["cra"]);
    await openRecord();
    await wrapper.find(`#observation-${OBS_ID}`).trigger("click");
    await flushPromises();
    vi.mocked(executionApi.verifyObservationSdv).mockResolvedValue(
      observation(OBS_ID, {
        is_sdv_verified: true,
        sdv_verified_by: "monitor1",
        sdv_verified_at: "2027-03-20T09:00:00Z",
      })
    );

    await wrapper.find("#btn-verify-sdv").trigger("click");
    await flushPromises();

    expect(executionApi.verifyObservationSdv).toHaveBeenCalledWith(OBS_ID);
    expect(wrapper.find("#inspector-sdv-status").text()).toBe(
      "Verified by monitor1 on 2027-03-20T09:00:00Z."
    );
    expect(wrapper.find("#btn-verify-sdv").exists()).toBe(false);
    expect(useToastStore().toasts.at(-1)).toMatchObject({ tone: "success" });
  });

  it("flags an observation for re-verification only with a reason", async () => {
    signInWith(["data_manager"]);
    await openRecord();
    const verifiedId = OBSERVATIONS[1].id;
    await wrapper.find(`#observation-${verifiedId}`).trigger("click");
    await flushPromises();
    vi.mocked(executionApi.flagObservationSdv).mockResolvedValue(
      observation(verifiedId, { test_name: "Potassium" })
    );

    expect(wrapper.find("#btn-flag-sdv").attributes()).toHaveProperty(
      "disabled"
    );
    await wrapper
      .find("#sdv-flag-reason")
      .setValue("  Unit differs from lab report ");
    await wrapper.find("#btn-flag-sdv").trigger("click");
    await flushPromises();

    expect(executionApi.flagObservationSdv).toHaveBeenCalledWith(
      verifiedId,
      "Unit differs from lab report"
    );
    expect(wrapper.find("#inspector-sdv-status").text()).toBe("Not verified.");
    expect(wrapper.find("#sdv-flag-reason").element.value).toBe("");
  });

  it("reports a refused verification and keeps the observation as it was", async () => {
    signInWith(["cra"]);
    await openRecord();
    await wrapper.find(`#observation-${OBS_ID}`).trigger("click");
    await flushPromises();
    vi.mocked(executionApi.verifyObservationSdv).mockRejectedValue(
      Object.assign(new Error("Forbidden"), { status: 403 })
    );

    await wrapper.find("#btn-verify-sdv").trigger("click");
    await flushPromises();

    expect(wrapper.find("#inspector-sdv-status").text()).toBe("Not verified.");
    expect(useToastStore().toasts.at(-1)).toMatchObject({
      tone: "error",
      title: "Systolic blood pressure was not verified",
    });
  });

  it("offers no verification to a site user", async () => {
    signInWith(["crc"]);
    await openRecord();
    await wrapper.find(`#observation-${OBS_ID}`).trigger("click");
    await flushPromises();

    expect(wrapper.find("#btn-verify-sdv").exists()).toBe(false);
    expect(wrapper.find("#btn-flag-sdv").exists()).toBe(false);
    expect(wrapper.find("#inspector-sdv-note").text()).toContain(
      "monitor or data manager"
    );
  });

  it("says so when the participant's data can't be loaded, and retries", async () => {
    vi.mocked(executionApi.listObservations).mockRejectedValueOnce(
      Object.assign(new Error("Unavailable"), { status: 503 })
    );
    await openRecord();

    expect(wrapper.find("#record-error").text()).toContain(
      "017-0001's data couldn't be loaded."
    );
    expect(wrapper.find("#participant-record table").exists()).toBe(false);

    await wrapper.find("#btn-retry-record").trigger("click");
    await flushPromises();
    expect(wrapper.find("#record-error").exists()).toBe(false);
    expect(wrapper.findAll("#participant-record tbody tr").length).toBe(3);
  });

  it("says a participant opened by link isn't loaded instead of guessing its code", async () => {
    await mountAt(RECORD_URL);

    expect(wrapper.find("#record-not-loaded").text()).toContain(
      "isn't among those loaded for CAD-101"
    );
    expect(executionApi.listObservations).not.toHaveBeenCalled();
    expect(executionApi.listSubjects).not.toHaveBeenCalled();
  });

  describe("queries", () => {
    it("lists the participant's queries against their observation", async () => {
      await openRecord();

      expect(executionApi.listQueries).toHaveBeenCalledWith(
        "CAD-101",
        "017-0001"
      );
      const item = wrapper.find(`#query-${QUERY_ID}`);
      expect(item.text()).toContain("Open");
      expect(item.text()).toContain("Systolic blood pressure · Week 12");
      expect(item.text()).toContain("Value differs from the source note");
    });

    it("lets a site user answer an open query, and nothing more", async () => {
      signInWith(["crc"]);
      await openRecord();
      vi.mocked(executionApi.respondQuery).mockResolvedValue(
        query({ status: "ANSWERED", response: "Corrected per source" })
      );

      expect(wrapper.find(`#btn-close-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find(`#btn-cancel-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find("#record-queries-note").exists()).toBe(true);

      await wrapper.find(`#btn-respond-${QUERY_ID}`).trigger("click");
      await wrapper.vm.$nextTick();
      expect(wrapper.find(`#btn-confirm-respond-${QUERY_ID}`).attributes()).toHaveProperty("disabled");
      await wrapper
        .find(`#query-text-${QUERY_ID}`)
        .setValue(" Corrected per source ");
      await wrapper.vm.$nextTick();
      await wrapper.find(`#btn-confirm-respond-${QUERY_ID}`).trigger("click");
      await flushPromises();

      expect(executionApi.respondQuery).toHaveBeenCalledWith(
        QUERY_ID,
        "Corrected per source"
      );
      const item = wrapper.find(`#query-${QUERY_ID}`);
      expect(item.text()).toContain("Answered");
      expect(item.text()).toContain("Answer: Corrected per source");
      expect(wrapper.find(`#btn-respond-${QUERY_ID}`).exists()).toBe(false);
    });

    it("offers a CRA the transitions Execution allows from each state, each with a reason", async () => {
      signInWith(["cra"]);
      vi.mocked(executionApi.listQueries).mockResolvedValue([
        query({ status: "ANSWERED", response: "Checked" }),
      ]);
      await openRecord();

      expect(wrapper.find(`#btn-respond-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find(`#btn-close-${QUERY_ID}`).exists()).toBe(true);
      expect(wrapper.find(`#btn-reopen-${QUERY_ID}`).exists()).toBe(true);
      expect(wrapper.find(`#btn-cancel-${QUERY_ID}`).exists()).toBe(true);

      vi.mocked(executionApi.closeQuery).mockResolvedValue(
        query({ status: "CLOSED", response: "Checked" })
      );
      await wrapper.find(`#btn-close-${QUERY_ID}`).trigger("click");
      await wrapper.find(`#query-text-${QUERY_ID}`).setValue("Answer accepted");
      await wrapper.find(`#btn-confirm-close-${QUERY_ID}`).trigger("click");
      await flushPromises();

      expect(executionApi.closeQuery).toHaveBeenCalledWith(
        QUERY_ID,
        "Answer accepted"
      );
      // A closed query can only be reopened.
      expect(wrapper.find(`#btn-close-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find(`#btn-cancel-${QUERY_ID}`).exists()).toBe(false);
      expect(wrapper.find(`#btn-reopen-${QUERY_ID}`).exists()).toBe(true);
    });

    it("keeps the query as it was and says why when Execution refuses", async () => {
      signInWith(["cra"]);
      await openRecord();
      vi.mocked(executionApi.cancelQuery).mockRejectedValue(
        Object.assign(new Error("Forbidden"), { status: 403 })
      );

      await wrapper.find(`#btn-cancel-${QUERY_ID}`).trigger("click");
      await wrapper.find(`#query-text-${QUERY_ID}`).setValue("Raised in error");
      await wrapper.find(`#btn-confirm-cancel-${QUERY_ID}`).trigger("click");
      await flushPromises();

      expect(executionApi.cancelQuery).toHaveBeenCalledWith(
        QUERY_ID,
        "Raised in error"
      );
      expect(wrapper.find(`#query-${QUERY_ID}`).text()).toContain("Open");
      // The typed reason stays so the user can try again.
      expect(wrapper.find(`#query-text-${QUERY_ID}`).element.value).toBe(
        "Raised in error"
      );
      expect(useToastStore().toasts.at(-1)).toMatchObject({
        tone: "error",
        title: "The query was not cancelled",
      });
    });

    it("raises a query on the selected observation for a CRA", async () => {
      signInWith(["cra"]);
      await openRecord();
      await wrapper.find(`#observation-${OBS_ID}`).trigger("click");
      await flushPromises();

      expect(wrapper.find("#inspector-queries-status").text()).toBe(
        "1 query, 1 still open or awaiting review."
      );
      const raised = query({
        id: "9a2d6f10-1111-4c2b-8d3e-0000000000c2",
        explanation: "Confirm the cuff size",
      });
      vi.mocked(executionApi.raiseQuery).mockResolvedValue(raised);

      await wrapper
        .find("#query-explanation")
        .setValue("Confirm the cuff size");
      await wrapper.find("#btn-raise-query").trigger("click");
      await flushPromises();

      expect(executionApi.raiseQuery).toHaveBeenCalledWith({
        studyId: "CAD-101",
        subjectCode: "017-0001",
        observation: expect.objectContaining({ id: OBS_ID }),
        explanation: "Confirm the cuff size",
      });
      expect(wrapper.find("#inspector-queries-status").text()).toBe(
        "2 queries, 2 still open or awaiting review."
      );
      expect(wrapper.find(`#query-${raised.id}`).exists()).toBe(true);
      expect(wrapper.find("#query-explanation").element.value).toBe("");
    });

    it("offers a site user no way to raise a query", async () => {
      signInWith(["crc"]);
      await openRecord();
      await wrapper.find(`#observation-${OBS_ID}`).trigger("click");
      await flushPromises();

      expect(wrapper.find("#btn-raise-query").exists()).toBe(false);
    });
  });
});
