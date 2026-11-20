import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { ApiError } from "../../src/api/client";
import { findKernelEnrollmentRef } from "../../src/canvas/kernelStudy";
import { useAuthStore } from "../../src/stores/auth";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

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

vi.mock("../../src/canvas/kernelStudy", async (importOriginal) => {
  const actual = await importOriginal();
  return { ...actual, findKernelEnrollmentRef: vi.fn() };
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

const ENROLLMENT_ID = "enrollment-a";
const QUERY_ID = "query-a";

function query(overrides = {}) {
  return {
    query_id: QUERY_ID,
    form_id: "form-a",
    enrollment_id: ENROLLMENT_ID,
    item_id: "SYSBP",
    observation_id: "observation-a",
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
    for (const fn of Object.values(ecrfQueriesApi)) vi.mocked(fn).mockReset();
    vi.mocked(findKernelEnrollmentRef).mockReset();
    vi.mocked(findKernelEnrollmentRef).mockResolvedValue({
      kind: "enrollment",
      id: ENROLLMENT_ID,
    });
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

  it("opens a participant's queries by kernel enrollment and points to the eCRF screen for its forms (#5461)", async () => {
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
    expect(ecrfQueriesApi.listForEnrollment).toHaveBeenCalledWith(
      ENROLLMENT_ID
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
      expect(item.text()).toContain("form form-a");
      expect(item.text()).toContain("Value differs from the source note");
      expect(
        wrapper.find(`#query-open-form-${QUERY_ID}`).attributes("href")
      ).toBe(
        "/ecrf?studyId=CAD-101&subjectId=017-0001&formId=form-a&itemId=SYSBP"
      );
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
          formId: "form-a",
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
});
