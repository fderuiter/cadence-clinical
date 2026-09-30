import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    search: vi.fn(),
    activity: vi.fn(),
    workQueues: vi.fn(),
    listProtocolVersions: vi.fn(),
    getProtocolVersion: vi.fn(),
    getCommands: vi.fn(),
  },
  isMasked: (object) => object.mask !== null,
}));

const STUDY = "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1@2";
const VERSION = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000b1@3";

const REVIEW = {
  definition: "work_queue_definition:0192f3c4-5e6a-7b8c-9d0e-0000000000d1@1",
  code: "protocol_review",
  version: 1,
  title: "Protocol versions to review",
  capability: "protocol.approve",
  items: [
    {
      subject: VERSION,
      subject_kind: "protocol_version",
      title: null,
      since: "2026-09-29T08:00:00Z",
      mask: null,
    },
    {
      subject: null,
      subject_kind: "protocol_version",
      title: null,
      since: "2026-09-29T09:00:00Z",
      mask: "NotPermitted",
    },
  ],
  truncated: true,
};

const EMPTY = {
  definition: "work_queue_definition:0192f3c4-5e6a-7b8c-9d0e-0000000000d2@1",
  code: "query_answers",
  version: 1,
  title: "Queries to answer",
  capability: "query.answer",
  items: [],
  truncated: false,
};

describe("Clinical Canvas work queues (PX-15, ADR-2486)", () => {
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
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    });
    for (const method of Object.values(kernelApi))
      vi.mocked(method).mockReset();
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: STUDY,
        kind: "study",
        title: "Cadence 101",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.activity).mockResolvedValue({
      entries: [],
      next: null,
    });
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([]);
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  async function open(path = "/studies/CAD-101") {
    await router.push(path);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
  }

  const queue = (code) => wrapper.find(`[data-queue="${code}"]`);

  it("lists the caller's queues within the study, a hidden item in place", async () => {
    vi.mocked(kernelApi.workQueues).mockResolvedValue([REVIEW, EMPTY]);

    await open();

    expect(kernelApi.search).toHaveBeenCalledTimes(1);
    expect(kernelApi.workQueues).toHaveBeenCalledWith({
      within: STUDY,
      limit: 20,
    });
    const review = queue("protocol_review");
    expect(review.find(".queue-title").text()).toBe(
      "Protocol versions to review 2+"
    );
    expect(review.findAll(".queue-subject").map((s) => s.text())).toEqual([
      "protocol version 000000b1 version 3",
      "Hidden protocol version (not permitted)",
    ]);
    expect(review.find(".queue-more").text()).toBe(
      "Showing the oldest 2; more are waiting."
    );
    expect(queue("query_answers").find(".queue-empty").text()).toBe(
      "Nothing waiting."
    );
    expect(wrapper.find("#work-queues-status").exists()).toBe(false);
  });

  it("opens a protocol version from its queue item", async () => {
    vi.mocked(kernelApi.workQueues).mockResolvedValue([REVIEW]);
    vi.mocked(kernelApi.getProtocolVersion).mockResolvedValue({
      ref: VERSION,
      study: STUDY,
      number: 1,
      parent_version: null,
      status: "InReview",
      content_hash: null,
      amendment_intent: null,
      approval_attestation: null,
      withdrawal_reason: null,
      mask: null,
    });
    await open();

    await queue("protocol_review").find("a").trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.query).toMatchObject({
      stage: "design",
      section: "protocol-versions",
      node: VERSION,
    });
  });

  it("says when nothing is waiting for the caller", async () => {
    vi.mocked(kernelApi.workQueues).mockResolvedValue([EMPTY]);

    await open();

    expect(wrapper.find("#work-queues-status").text()).toBe(
      "Nothing in CAD-101 is waiting for you."
    );
  });

  it("reports the kernel's refusal as it is", async () => {
    vi.mocked(kernelApi.workQueues).mockRejectedValue(
      new ApiError(
        `You can't see ${STUDY} (NotPermitted), so its work queues are not read.`,
        403
      )
    );

    await open();

    expect(wrapper.find("#work-queues-status").text()).toBe(
      `The kernel couldn't answer: You can't see ${STUDY} (NotPermitted), so its work queues are not read.`
    );
    expect(wrapper.find(".queue").exists()).toBe(false);
  });

  it("says so when the kernel has no study with that code the reader may see", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([]);

    await open();

    expect(wrapper.find("#work-queues-status").text()).toBe(
      "The kernel has no study CAD-101 that you may see."
    );
    expect(kernelApi.workQueues).not.toHaveBeenCalled();
  });
});
