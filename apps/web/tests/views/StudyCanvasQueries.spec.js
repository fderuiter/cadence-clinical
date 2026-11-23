import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { ecrfQueriesApi } from "../../src/api/ecrfQueriesApi";
import { kernelApi } from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import { findKernelStudyRef } from "../../src/canvas/kernelStudy";
import { useAuthStore } from "../../src/stores/auth";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    listProposals: vi.fn(async () => []),
    search: vi.fn(async () => []),
    getCommands: vi.fn(async () => []),
    getObservation: vi.fn(),
    getZoomPath: vi.fn(async () => []),
  },
}));

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    ecrfFormsApi: {
      listForEnrollment: vi.fn(async () => []),
      openForm: vi.fn(),
      getForm: vi.fn(),
      saveValues: vi.fn(),
      signForm: vi.fn(),
    },
  };
});

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(async () => ({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    })),
    getSubjectCounts: vi.fn(async () => ({ total: 0, counts: {} })),
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
const FORM_VS_1 = "0192f3c4-5e6a-7b8c-9d0e-00000000f001";
const FORM_VS_2 = "0192f3c4-5e6a-7b8c-9d0e-00000000f002";
const OBS_1 = "0192f3c4-5e6a-7b8c-9d0e-00000000a001";

function studyQuery(overrides = {}) {
  return {
    query_id: "query-1",
    form_id: FORM_VS_1,
    enrollment_id: "enrollment-1",
    item_id: "DIABP",
    observation_id: OBS_1,
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
    vi.mocked(executionApi.getSubjectCounts).mockResolvedValue({
      total: 0,
      counts: {},
    });
    for (const fn of Object.values(ecrfQueriesApi)) vi.mocked(fn).mockReset();
    vi.mocked(findKernelStudyRef).mockReset();
    vi.mocked(findKernelStudyRef).mockResolvedValue(`study:${STUDY_UUID}@2`);
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([
      studyQuery(),
      studyQuery({
        query_id: "query-2",
        form_id: FORM_VS_2,
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
    expect(ecrfQueriesApi.listForStudy).toHaveBeenCalledWith(
      STUDY_UUID,
      {
        status: "",
        site: undefined,
        participant: undefined,
        valueChanged: null,
      },
      { signal: expect.any(AbortSignal) }
    );

    expect(wrapper.find("#study-queries-section").exists()).toBe(true);
    expect(wrapper.findAll(".form-query")).toHaveLength(2);
    expect(wrapper.find("#query-query-1").text()).toContain("Site 017");
    expect(wrapper.find("#query-query-1").text()).toContain("017-0001");
    expect(wrapper.find("#query-query-2").text()).toContain(
      "Value changed (now revision 2)"
    );

    // Monitors see no Answer button on open queries (execution.query.answer is site-only).
    expect(wrapper.find("#btn-answer-query-1").exists()).toBe(false);

    const openObsLink = wrapper.find("#query-observation-query-1");
    expect(openObsLink.attributes("href")).toBe(
      `/studies/CAD-101?stage=data&section=queries&site=017&form=collection_instance:${FORM_VS_1}&item=DIABP&observation=observation:${OBS_1}`
    );

    const openFormLink = wrapper.find("#query-open-form-query-1");
    expect(openFormLink.attributes("href")).toBe(
      `/studies/CAD-101?stage=data&section=queries&site=017&form=collection_instance:${FORM_VS_1}&item=DIABP`
    );
    await openFormLink.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/studies/CAD-101");
    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "queries",
      site: "017",
      form: `collection_instance:${FORM_VS_1}`,
      item: "DIABP",
    });
  });

  it("filters the study query worklist by status, owner, origin, priority, site, participant, and value_changed and syncs URL query parameters", async () => {
    signInWith(["cra"]);
    await open("/studies/CAD-101?stage=data&section=queries");
    vi.mocked(ecrfQueriesApi.listForStudy).mockClear();
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([
      studyQuery({
        query_id: "query-2",
        status: "answered",
        owner_role: "monitor",
        origin: "manual",
        priority: "high",
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

    await wrapper.find("#study-queries-owner").setValue("monitor");
    await flushPromises();
    await wrapper.find("#study-queries-origin").setValue("manual");
    await flushPromises();
    await wrapper.find("#study-queries-priority").setValue("high");
    await flushPromises();
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
      owner: "monitor",
      origin: "manual",
      priority: "high",
      site: "022",
      subject: "022-0004",
      changed: "true",
    });
    expect(ecrfQueriesApi.listForStudy).toHaveBeenLastCalledWith(
      STUDY_UUID,
      {
        status: "answered",
        owner: "monitor",
        origin: "manual",
        priority: "high",
        site: "022",
        participant: "022-0004",
        valueChanged: true,
      },
      { signal: expect.any(AbortSignal) }
    );

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
    expect(ecrfQueriesApi.listForStudy).toHaveBeenCalledWith(
      STUDY_UUID,
      {
        status: "",
        site: "017",
        participant: undefined,
        valueChanged: null,
      },
      { signal: expect.any(AbortSignal) }
    );
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

    // Select both open manual queries for bulk cancellation (open manual queries cannot be closed directly).
    expect(wrapper.find("#btn-close-query-1").exists()).toBe(false);
    await wrapper.find("#query-bulk-action").setValue("cancel");
    await wrapper.find("#btn-select-eligible-queries").trigger("click");
    await wrapper
      .find("#query-bulk-reason")
      .setValue("Raised on wrong visit row");

    // Data managers see no Answer or Close button on open manual queries; open a cancel draft on query-1.
    expect(wrapper.find("#btn-answer-query-1").exists()).toBe(false);
    await wrapper.find("#btn-cancel-query-1").trigger("click");
    await wrapper
      .find("#query-text-query-1")
      .setValue("Duplicate of Week 4 query");
    await wrapper
      .find("#query-reason-query-1")
      .setValue("Duplicate manual query");

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
      "Duplicate of Week 4 query"
    );
    expect(wrapper.find("#query-reason-query-1").element.value).toBe(
      "Duplicate manual query"
    );

    // Refresh completes with only query-1 remaining (query-2 filtered out).
    resolveRefresh([q1]);
    await flushPromises();

    expect(wrapper.find("#study-queries-refreshing").exists()).toBe(false);
    expect(wrapper.find(".form-query-list").element).toBe(listElementBefore);
    expect(wrapper.find("#query-text-query-1").element.value).toBe(
      "Duplicate of Week 4 query"
    );
    expect(wrapper.find("#query-reason-query-1").element.value).toBe(
      "Duplicate manual query"
    );
    expect(wrapper.find("#select-query-query-1").element.checked).toBe(true);
    expect(wrapper.find("#select-query-query-2").exists()).toBe(false);
    expect(wrapper.find("#query-bulk-reason").element.value).toBe(
      "Raised on wrong visit row"
    );
  });

  it("enforces separation of duties on answered manual queries and sends X-Change-Reason separately from closing note", async () => {
    signInWith(["data_manager"]);
    const auth = useAuthStore();
    auth.user = {
      id: "dm-self-sub",
      username: "dm.self",
      email: "dm.self@example.test",
      firstName: "Self",
      lastName: "Reviewer",
      tenantId: "t-1",
    };

    const selfAnswered = studyQuery({
      query_id: "query-self",
      status: "answered",
      origin: "manual",
      owner_role: "monitor",
      priority: "high",
      escalated_at: "2027-03-29T00:00:00Z",
      last_answered_by: "dm-self-sub",
    });
    const peerAnswered = studyQuery({
      query_id: "query-peer",
      status: "answered",
      origin: "manual",
      owner_role: "monitor",
      priority: "normal",
      last_answered_by: "crc-site-user",
    });
    vi.mocked(ecrfQueriesApi.listForStudy).mockResolvedValue([
      selfAnswered,
      peerAnswered,
    ]);
    vi.mocked(ecrfQueriesApi.transition).mockResolvedValue({
      ...peerAnswered,
      status: "closed",
      version: 3,
    });

    await open("/studies/CAD-101?stage=data&section=queries");

    expect(wrapper.find("#btn-close-query-self").exists()).toBe(false);
    expect(
      wrapper.find('[data-testid="query-sod-hint-query-self"]').text()
    ).toContain("Another monitor or data manager must close");

    expect(wrapper.find("#btn-close-query-peer").exists()).toBe(true);
    await wrapper.find("#btn-close-query-peer").trigger("click");
    await wrapper
      .find("#query-text-query-peer")
      .setValue("Verified against nursing flowsheet");
    await wrapper
      .find("#query-reason-query-peer")
      .setValue("Monitor source review complete");
    await wrapper.find("#btn-confirm-close-query-peer").trigger("click");
    await flushPromises();

    expect(ecrfQueriesApi.transition).toHaveBeenCalledWith(
      "close",
      expect.objectContaining({
        queryId: "query-peer",
        text: "Verified against nursing flowsheet",
        reason: "Monitor source review complete",
      })
    );
  });

  it("preserves stage, section (queries or validation), form, and item across form back labels and #observation-back returns", async () => {
    signInWith(["cra"]);
    vi.mocked(ecrfFormsApi.getForm).mockResolvedValue({
      form_id: FORM_VS_1,
      enrollment_id: "enrollment-1",
      participant_id: "0192f3c4-5e6a-7b8c-9d0e-000000000001",
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
      items: [
        {
          item_id: "DIABP",
          concept: "concept:diastolic@1",
          unit: "mm[Hg]",
          value_kind: "Quantity",
          required: true,
          value: {
            type: "quantity",
            value: 84,
            unit: "mm[Hg]",
            comparator: null,
          },
          observation_id: OBS_1,
          revision: 1,
          recorded_by: "crc1",
          recorded_at: "2027-03-14T09:05:00Z",
        },
      ],
    });
    vi.mocked(ecrfQueriesApi.listForForm).mockResolvedValue([studyQuery()]);
    vi.mocked(kernelApi.getObservation).mockResolvedValue({
      ref: `observation:${OBS_1}@1`,
      enrollment_ref: "enrollment:enrollment-1",
      concept: "concept:diastolic@1",
      value: {
        type: "quantity",
        value: 84,
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

    await open("/studies/CAD-101?stage=data&section=queries");

    await wrapper.find("#query-open-form-query-1").trigger("click");
    await flushPromises();

    expect(wrapper.find("#canvas-form-back").text()).toBe("Back to Queries");
    expect(wrapper.find("#ecrf-row-DIABP").attributes("data-highlighted")).toBe(
      "true"
    );

    await wrapper.find("#ecrf-item-observation-DIABP").trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "queries",
      site: "017",
      form: `collection_instance:${FORM_VS_1}`,
      item: "DIABP",
      observation: `observation:${OBS_1}`,
    });

    await wrapper.find("#observation-back").trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "queries",
      site: "017",
      form: `collection_instance:${FORM_VS_1}`,
      item: "DIABP",
    });
    expect(wrapper.find("#ecrf-row-DIABP").attributes("data-highlighted")).toBe(
      "true"
    );

    // Validation worklist section also preserves stage=data&section=validation and Back to Validation label
    await router.push(
      `/studies/CAD-101?stage=data&section=validation&form=collection_instance:${FORM_VS_1}&item=DIABP`
    );
    await flushPromises();
    expect(wrapper.find("#canvas-form-back").text()).toBe("Back to Validation");

    await wrapper.find("#ecrf-item-observation-DIABP").trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "validation",
      form: `collection_instance:${FORM_VS_1}`,
      item: "DIABP",
      observation: `observation:${OBS_1}`,
    });

    await wrapper.find("#observation-back").trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "validation",
      form: `collection_instance:${FORM_VS_1}`,
      item: "DIABP",
    });
  });
});
