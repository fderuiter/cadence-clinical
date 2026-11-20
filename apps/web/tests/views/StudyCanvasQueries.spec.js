import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { ApiError } from "../../src/api/client";
import { findKernelStudyRef } from "../../src/canvas/kernelStudy";
import { useAuthStore } from "../../src/stores/auth";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    listProposals: vi.fn(async () => []),
    search: vi.fn(async () => []),
    getCommands: vi.fn(async () => []),
  },
}));

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(async () => ({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    })),
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
      listForStudy: vi.fn(),
      transition: vi.fn(),
    },
  };
});

vi.mock("../../src/canvas/kernelStudy", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    findKernelStudyRef: vi.fn(),
  };
});

const STUDY_UUID = "0192f3c4-5e6a-7b8c-9d0e-000000000099";

function studyQuery(overrides = {}) {
  return {
    query_id: "query-1",
    form_id: "form-vs-1",
    enrollment_id: "enrollment-1",
    item_id: "DIABP",
    observation_id: "obs-1",
    revision: 1,
    current_revision: 1,
    value_changed: false,
    status: "open",
    text: "Diastolic BP is missing on source",
    messages: [
      {
        kind: "raised",
        text: "Diastolic BP is missing on source",
        by: "cra@example.test",
        at: "2027-03-14T10:00:00Z",
      },
    ],
    raised_by: "cra@example.test",
    raised_at: "2027-03-14T10:00:00Z",
    version: 1,
    study_id: STUDY_UUID,
    study_site_id: "ss-017",
    site_code: "017",
    subject_code: "017-0001",
    collection_spec_id: "VS",
    planned_activity_id: "WEEK2",
    ...overrides,
  };
}

describe("Clinical Canvas: Data -> Queries worklist", () => {
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
          meta: { layout: "canvas" },
        },
        { path: "/ecrf", name: "ecrf", component: { template: "<div />" } },
      ],
    });
    for (const fn of Object.values(executionApi)) vi.mocked(fn).mockReset();
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    });
    for (const fn of Object.values(ecrfQueriesApi)) vi.mocked(fn).mockReset();
    vi.mocked(findKernelStudyRef).mockReset();
    vi.mocked(findKernelStudyRef).mockResolvedValue(`study:${STUDY_UUID}@2`);
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([
      studyQuery(),
      studyQuery({
        query_id: "query-2",
        form_id: "form-vs-2",
        enrollment_id: "enrollment-2",
        item_id: "SYSBP",
        status: "answered",
        value_changed: true,
        current_revision: 2,
        site_code: "022",
        subject_code: "022-0004",
      }),
    ]);
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  function signInWith(roles) {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = roles;
  }

  async function open(url) {
    await router.push(url);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
    return wrapper;
  }

  it("opens Queries under Data stage, displays site/participant context, and links to target form and highlighted item", async () => {
    signInWith(["cra"]);
    await open("/studies/CAD-101?stage=data");

    const navLink = wrapper.find("#nav-queries");
    expect(navLink.exists()).toBe(true);
    expect(navLink.text()).toBe("Queries");

    await navLink.trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "queries",
    });
    expect(findKernelStudyRef).toHaveBeenCalledWith("CAD-101");
    expect(ecrfQueriesApi.listForStudy).toHaveBeenCalledWith(STUDY_UUID, {
      status: "",
      site: undefined,
      participant: undefined,
      valueChanged: null,
    });

    expect(wrapper.find("#study-queries-section").exists()).toBe(true);
    expect(wrapper.findAll(".form-query")).toHaveLength(2);
    expect(wrapper.find("#query-query-1").text()).toContain("Site 017");
    expect(wrapper.find("#query-query-1").text()).toContain("017-0001");
    expect(wrapper.find("#query-query-2").text()).toContain(
      "Value changed (now revision 2)"
    );

    const openFormLink = wrapper.find("#query-open-form-query-1");
    expect(openFormLink.attributes("href")).toBe(
      "/ecrf?studyId=CAD-101&subjectId=017-0001&enrollmentId=enrollment-1&formId=form-vs-1&itemId=DIABP"
    );
    await openFormLink.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/ecrf");
    expect(router.currentRoute.value.query).toEqual({
      studyId: "CAD-101",
      subjectId: "017-0001",
      enrollmentId: "enrollment-1",
      formId: "form-vs-1",
      itemId: "DIABP",
    });
  });

  it("filters the study query worklist by status, site, participant, and value_changed and syncs URL query parameters", async () => {
    signInWith(["cra"]);
    await open("/studies/CAD-101?stage=data&section=queries");
    vi.mocked(ecrfQueriesApi.listForStudy).mockClear();
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([
      studyQuery({
        query_id: "query-2",
        status: "answered",
        value_changed: true,
        site_code: "022",
        subject_code: "022-0004",
      }),
    ]);

    await wrapper.find("#study-queries-status").setValue("answered");
    await flushPromises();
    expect(router.currentRoute.value.query).toMatchObject({
      stage: "data",
      section: "queries",
      status: "answered",
    });

    await wrapper.find("#study-queries-site").setValue("022");
    await flushPromises();
    await wrapper.find("#study-queries-participant").setValue("022-0004");
    await flushPromises();
    await wrapper.find("#study-queries-changed").setValue("true");
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "queries",
      status: "answered",
      site: "022",
      subject: "022-0004",
      changed: "true",
    });
    expect(ecrfQueriesApi.listForStudy).toHaveBeenLastCalledWith(STUDY_UUID, {
      status: "answered",
      site: "022",
      participant: "022-0004",
      valueChanged: true,
    });

    await wrapper.find("#btn-clear-query-filters").trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "queries",
    });
  });

  it("selects multiple eligible queries, dispatches one transition per query with a shared reason, and reports per-query results", async () => {
    signInWith(["data_manager"]);
    const q1 = studyQuery({
      query_id: "query-1",
      status: "answered",
      subject_code: "017-0001",
      item_id: "DIABP",
    });
    const q2 = studyQuery({
      query_id: "query-2",
      status: "answered",
      subject_code: "022-0004",
      item_id: "SYSBP",
    });
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([q1, q2]);
    vi.mocked(ecrfQueriesApi.transition)
      .mockResolvedValueOnce({ ...q1, status: "closed", version: 2 })
      .mockRejectedValueOnce(
        new ApiError("Locked", 423, null, { code: "K.Assertion.Locked" })
      );

    await open("/studies/CAD-101?stage=data&section=queries");

    // Default bulk action for answered queries when canManage=true includes 'close' or 'reopen' — select 'close'.
    await wrapper.find("#query-bulk-action").setValue("close");
    await wrapper.find("#btn-select-eligible-queries").trigger("click");
    await wrapper
      .find("#query-bulk-reason")
      .setValue("Verified source resolutions");
    await wrapper.find("#btn-apply-bulk-queries").trigger("click");
    await flushPromises();

    expect(ecrfQueriesApi.transition).toHaveBeenCalledTimes(2);
    expect(ecrfQueriesApi.transition).toHaveBeenNthCalledWith(
      1,
      "close",
      expect.objectContaining({
        queryId: "query-1",
        reason: "Verified source resolutions",
      })
    );
    expect(ecrfQueriesApi.transition).toHaveBeenNthCalledWith(
      2,
      "close",
      expect.objectContaining({
        queryId: "query-2",
        reason: "Verified source resolutions",
      })
    );

    const results = wrapper.find('[data-testid="query-bulk-results"]');
    expect(results.exists()).toBe(true);
    expect(
      results.find('[data-query-result="query-1"]').attributes("data-ok")
    ).toBe("true");
    expect(
      results.find('[data-query-result="query-2"]').attributes("data-ok")
    ).toBe("false");
    expect(results.find('[data-query-result="query-2"]').text()).toContain(
      "locked"
    );
  });

  it("debounces site and participant text filters by 250ms, uses router.replace without history spam, and preserves spaces while focused", async () => {
    vi.useFakeTimers();
    signInWith(["cra"]);
    await open("/studies/CAD-101?stage=data&section=queries");
    vi.mocked(ecrfQueriesApi.listForStudy).mockClear();

    const pushSpy = vi.spyOn(router, "push");
    const replaceSpy = vi.spyOn(router, "replace");

    const siteInput = wrapper.find("#study-queries-site");
    siteInput.element.focus();
    await siteInput.trigger("focus");

    await siteInput.setValue("0");
    vi.advanceTimersByTime(100);
    await siteInput.setValue("01");
    vi.advanceTimersByTime(100);
    await siteInput.setValue("017 ");
    vi.advanceTimersByTime(100);

    // Still within 250ms of the last keystroke: no API request or route mutation yet.
    expect(ecrfQueriesApi.listForStudy).not.toHaveBeenCalled();
    expect(replaceSpy).not.toHaveBeenCalled();
    expect(pushSpy).not.toHaveBeenCalled();

    vi.advanceTimersByTime(160);
    await flushPromises();

    // Debounced flush emits one router.replace and one API request, while keeping the focused input's space intact.
    expect(pushSpy).not.toHaveBeenCalled();
    expect(replaceSpy).toHaveBeenCalledTimes(1);
    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "queries",
      site: "017",
    });
    expect(ecrfQueriesApi.listForStudy).toHaveBeenCalledTimes(1);
    expect(ecrfQueriesApi.listForStudy).toHaveBeenCalledWith(STUDY_UUID, {
      status: "",
      site: "017",
      participant: undefined,
      valueChanged: null,
    });
    expect(siteInput.element.value).toBe("017 ");

    const participantInput = wrapper.find("#study-queries-participant");
    participantInput.element.focus();
    await participantInput.trigger("focus");
    await participantInput.setValue("017 ");
    vi.advanceTimersByTime(250);
    await flushPromises();

    expect(pushSpy).not.toHaveBeenCalled();
    expect(replaceSpy).toHaveBeenCalledTimes(2);
    expect(participantInput.element.value).toBe("017 ");
    expect(ecrfQueriesApi.listForStudy).toHaveBeenCalledTimes(2);
  });

  it("keeps FormQueryList mounted during filter refreshes and preserves open drafts and bulk selections for remaining queries", async () => {
    signInWith(["data_manager"]);
    const q1 = studyQuery({
      query_id: "query-1",
      status: "open",
      site_code: "017",
      subject_code: "017-0001",
    });
    const q2 = studyQuery({
      query_id: "query-2",
      status: "open",
      site_code: "022",
      subject_code: "022-0004",
    });
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValueOnce([q1, q2]);

    await open("/studies/CAD-101?stage=data&section=queries");
    await expect(wrapper.element).toBeAccessible();

    // Select both queries for bulk action and type a bulk reason.
    await wrapper.find("#query-bulk-action").setValue("close");
    await wrapper.find("#btn-select-eligible-queries").trigger("click");
    await wrapper
      .find("#query-bulk-reason")
      .setValue("Source chart verified across visits");

    // Open an answer draft on query-1.
    await wrapper.find("#btn-answer-query-1").trigger("click");
    await wrapper
      .find("#query-text-query-1")
      .setValue("Confirmed 82 mmHg in nursing flowsheet");

    const listElementBefore = wrapper.find(".form-query-list").element;

    let resolveRefresh;
    vi.mocked(ecrfQueriesApi.listForStudy).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveRefresh = resolve;
        })
    );

    await wrapper.find("#study-queries-status").setValue("open");
    await flushPromises();

    // While refreshing, FormQueryList remains mounted with an inline status indicator.
    expect(wrapper.find("#study-queries-refreshing").exists()).toBe(true);
    expect(wrapper.find(".form-query-list").exists()).toBe(true);
    expect(wrapper.find(".form-query-list").element).toBe(listElementBefore);
    expect(wrapper.find("#query-text-query-1").element.value).toBe(
      "Confirmed 82 mmHg in nursing flowsheet"
    );

    // Refresh completes with only query-1 remaining (query-2 filtered out).
    resolveRefresh([q1]);
    await flushPromises();

    expect(wrapper.find("#study-queries-refreshing").exists()).toBe(false);
    expect(wrapper.find(".form-query-list").element).toBe(listElementBefore);
    expect(wrapper.find("#query-text-query-1").element.value).toBe(
      "Confirmed 82 mmHg in nursing flowsheet"
    );
    expect(wrapper.find("#select-query-query-1").element.checked).toBe(true);
    expect(wrapper.find("#select-query-query-2").exists()).toBe(false);
    expect(wrapper.find("#query-bulk-reason").element.value).toBe(
      "Source chart verified across visits"
    );
  });
});
