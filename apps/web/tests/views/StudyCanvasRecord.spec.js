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
    listQueries: vi.fn(),
    respondQuery: vi.fn(),
    closeQuery: vi.fn(),
    reopenQuery: vi.fn(),
    cancelQuery: vi.fn(),
    batchCloseQueries: vi.fn(),
    batchCancelQueries: vi.fn(),
    batchRespondQueries: vi.fn(),
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

const OBS_ID = "6b1f0a52-4c1d-4f0e-9a51-0000000000a1";
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

  it("opens a participant's queries and points to the eCRF screen for its forms (ADR-2622)", async () => {
    await openRecord();

    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      participant: `participant:${PARTICIPANT_ID}`,
    });
    expect(executionApi.listQueries).toHaveBeenCalledWith(
      "CAD-101",
      "017-0001"
    );
    const link = wrapper.find("#record-open-ecrf");
    expect(link.attributes("href")).toBe(
      "/ecrf?studyId=CAD-101&subjectId=017-0001"
    );
    // The zoom path now ends at the participant, and the inspector shows it.
    expect(wrapper.find("#zoom-participant").attributes("aria-current")).toBe(
      "page"
    );
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("017-0001");
    expect(wrapper.find("#inspector-open-record").exists()).toBe(false);
  });

  it("says so when the participant's data can't be loaded, and retries", async () => {
    vi.mocked(executionApi.listQueries).mockRejectedValueOnce(
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

  it("says a participant opened by link isn't loaded instead of guessing its code", async () => {
    await mountAt(RECORD_URL);

    expect(wrapper.find("#record-not-loaded").text()).toContain(
      "isn't among those loaded for CAD-101"
    );
    expect(executionApi.listQueries).not.toHaveBeenCalled();
    expect(executionApi.listSubjects).not.toHaveBeenCalled();
  });

  describe("queries", () => {
    it("lists the participant's queries with their test and visit", async () => {
      await openRecord();

      expect(executionApi.listQueries).toHaveBeenCalledWith(
        "CAD-101",
        "017-0001"
      );
      const item = wrapper.find(`#query-${QUERY_ID}`);
      expect(item.text()).toContain("Open");
      expect(item.text()).toContain("SYSBP · Week 12");
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
      expect(
        wrapper.find(`#btn-confirm-respond-${QUERY_ID}`).attributes()
      ).toHaveProperty("disabled");
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

    it("allows CRA to batch close multiple answered queries with a reason", async () => {
      signInWith(["cra"]);
      const q1 = query({
        id: "q-1",
        status: "ANSWERED",
        response: "Checked 1",
      });
      const q2 = query({
        id: "q-2",
        status: "ANSWERED",
        response: "Checked 2",
      });
      vi.mocked(executionApi.listQueries).mockResolvedValue([q1, q2]);
      await openRecord();

      expect(wrapper.find("#select-all-queries").exists()).toBe(true);
      expect(wrapper.find("#btn-batch-close").attributes()).toHaveProperty(
        "disabled"
      );

      await wrapper.find("#select-all-queries").setValue(true);
      await wrapper.vm.$nextTick();

      expect(
        wrapper.find("#btn-batch-close").attributes("disabled")
      ).toBeUndefined();

      vi.mocked(executionApi.batchCloseQueries).mockResolvedValue({
        queries: [
          { ...q1, status: "CLOSED" },
          { ...q2, status: "CLOSED" },
        ],
        count: 2,
      });

      await wrapper.find("#btn-batch-close").trigger("click");
      await wrapper.vm.$nextTick();

      await wrapper
        .find("#batch-query-reason")
        .setValue("Verified batch resolution");
      await wrapper.find("#btn-confirm-batch-close").trigger("click");
      await flushPromises();

      expect(executionApi.batchCloseQueries).toHaveBeenCalledWith(
        ["q-1", "q-2"],
        "Verified batch resolution"
      );
      expect(useToastStore().toasts.at(-1)).toMatchObject({
        tone: "success",
        title: "2 queries closed",
      });
      expect(wrapper.find("#query-q-1").text()).toContain("Closed");
      expect(wrapper.find("#query-q-2").text()).toContain("Closed");
    });

    it("allows CRA to batch cancel multiple queries with a reason", async () => {
      signInWith(["cra"]);
      const q1 = query({ id: "q-1", status: "OPEN" });
      const q2 = query({ id: "q-2", status: "ANSWERED" });
      vi.mocked(executionApi.listQueries).mockResolvedValue([q1, q2]);
      await openRecord();

      await wrapper.find("#select-query-q-1").setValue(true);
      await wrapper.find("#select-query-q-2").setValue(true);
      await wrapper.vm.$nextTick();

      vi.mocked(executionApi.batchCancelQueries).mockResolvedValue({
        queries: [
          { ...q1, status: "CANCELLED", cancellation_reason: "Obsolete" },
          { ...q2, status: "CANCELLED", cancellation_reason: "Obsolete" },
        ],
        count: 2,
      });

      await wrapper.find("#btn-batch-cancel").trigger("click");
      await wrapper.vm.$nextTick();

      await wrapper.find("#batch-query-reason").setValue("Obsolete");
      await wrapper.find("#btn-confirm-batch-cancel").trigger("click");
      await flushPromises();

      expect(executionApi.batchCancelQueries).toHaveBeenCalledWith(
        ["q-1", "q-2"],
        "Obsolete"
      );
      expect(wrapper.find("#query-q-1").text()).toContain("Cancelled");
      expect(wrapper.find("#query-q-2").text()).toContain("Cancelled");
    });

    it("allows clinical coordinator to batch answer multiple open or reopened queries", async () => {
      signInWith(["crc"]);
      const q1 = query({ id: "q-1", status: "OPEN" });
      const q2 = query({ id: "q-2", status: "REOPENED" });
      vi.mocked(executionApi.listQueries).mockResolvedValue([q1, q2]);
      await openRecord();

      expect(wrapper.find("#select-all-queries").exists()).toBe(true);
      expect(wrapper.find("#btn-batch-respond").attributes()).toHaveProperty(
        "disabled"
      );

      await wrapper.find("#select-all-queries").setValue(true);
      await wrapper.vm.$nextTick();

      expect(
        wrapper.find("#btn-batch-respond").attributes("disabled")
      ).toBeUndefined();

      vi.mocked(executionApi.batchRespondQueries).mockResolvedValue({
        queries: [
          { ...q1, status: "ANSWERED", response: "Updated per protocol v2" },
          { ...q2, status: "ANSWERED", response: "Updated per protocol v2" },
        ],
        count: 2,
      });

      await wrapper.find("#btn-batch-respond").trigger("click");
      await wrapper.vm.$nextTick();

      await wrapper
        .find("#batch-query-reason")
        .setValue("Updated per protocol v2");
      await wrapper.find("#btn-confirm-batch-respond").trigger("click");
      await flushPromises();

      expect(executionApi.batchRespondQueries).toHaveBeenCalledWith(
        ["q-1", "q-2"],
        "Updated per protocol v2",
        undefined
      );
      expect(useToastStore().toasts.at(-1)).toMatchObject({
        tone: "success",
        title: "2 queries answered",
      });
      expect(wrapper.find("#query-q-1").text()).toContain("Answered");
      expect(wrapper.find("#query-q-2").text()).toContain("Answered");
    });
  });
});
