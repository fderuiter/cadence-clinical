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
    getSubjectCounts: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    listProposals: vi.fn(async () => []),
    search: vi.fn(),
    activity: vi.fn(),
    listProtocolVersions: vi.fn(),
    getProtocolVersion: vi.fn(),
    getCommands: vi.fn(),
    getNode: vi.fn(),
  },
  isMasked: (object) => object.mask !== null,
}));

const STUDY = "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1@2";
const ENROLLMENT = "enrollment:0192f3c4-5e6a-7b8c-9d0e-0000000000c1@1";
const VERSION = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000b1@3";

const entry = (overrides) => ({
  event_id: crypto.randomUUID(),
  type: "ParticipantScreened",
  family: "Execution",
  recorded: "2026-09-30T12:00:00Z",
  occurred_valid: null,
  actor_id: "crc@site.test",
  subject_kind: "enrollment",
  subject: null,
  mask: null,
  correlation_id: crypto.randomUUID(),
  ...overrides,
});

const SCREENED = entry({ subject: ENROLLMENT });
const HIDDEN = entry({
  type: "EnrollmentStatusChanged",
  mask: "NotPermitted",
});
const APPROVED = entry({
  type: "ProtocolVersionApproved",
  family: "Knowledge",
  subject_kind: "protocol_version",
  subject: VERSION,
  actor_id: "sponsor@cro.test",
});

describe("Clinical Canvas study activity (PX-11, ADR-2485)", () => {
  let pinia;
  let router;
  let wrapper;

  beforeEach(() => {
    localStorage.clear();
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
    vi.mocked(executionApi.getSubjectCounts).mockResolvedValue({
      total: 0,
      counts: {},
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
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([]);
    vi.mocked(kernelApi.getNode).mockImplementation(async (ref) => {
      if (ref === ENROLLMENT) {
        return {
          ref: ENROLLMENT,
          kind: "enrollment",
          code: "017-0001",
          title: "Enrollment 017-0001",
          status: "Screened",
          mask: null,
        };
      }
      if (ref === VERSION) {
        return {
          ref: VERSION,
          kind: "protocol_version",
          code: null,
          title: "Version 3",
          status: "Approved",
          mask: null,
        };
      }
      return {
        ref,
        kind: "record",
        code: null,
        title: null,
        status: null,
        mask: null,
      };
    });
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

  const rows = () =>
    wrapper.findAll("#activity-list li").map((li) => ({
      what: li.find(".activity-what").text(),
      subject: li.find(".activity-subject").text(),
      actor: li.find(".activity-actor").text(),
    }));

  it("lists the study's activity from the kernel, a hidden entry in place", async () => {
    vi.mocked(kernelApi.activity).mockResolvedValue({
      entries: [SCREENED, HIDDEN, APPROVED],
      next: null,
    });

    await open();

    expect(kernelApi.search).not.toHaveBeenCalled();
    expect(kernelApi.activity).toHaveBeenCalledWith({
      scope: "CAD-101",
      families: undefined,
      before: undefined,
      limit: 25,
      includeNodes: true,
    });
    expect(rows()).toEqual([
      {
        what: "Participant screened",
        subject: "017-0001 · Enrollment 017-0001",
        actor: "by crc@site.test",
      },
      {
        what: "Enrollment status changed",
        subject: "Hidden enrollment (not permitted)",
        actor: "by crc@site.test",
      },
      {
        what: "Protocol version approved",
        subject: "Version 3",
        actor: "by sponsor@cro.test",
      },
    ]);
    expect(wrapper.find("#activity-status").exists()).toBe(false);
    expect(wrapper.find("#btn-activity-older").exists()).toBe(false);
  });

  it("selects a non-version activity subject in the inspector when clicked", async () => {
    vi.mocked(kernelApi.activity).mockResolvedValue({
      entries: [SCREENED],
      next: null,
    });

    await open();
    await wrapper.find("#activity-list a").trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.query.node).toBe(ENROLLMENT);
    expect(wrapper.find("#canvas-inspector-title").text()).toBe(
      "017-0001 · Enrollment 017-0001"
    );
  });

  it("opens a protocol version from its activity entry", async () => {
    vi.mocked(kernelApi.activity).mockResolvedValue({
      entries: [APPROVED],
      next: null,
    });
    vi.mocked(kernelApi.getProtocolVersion).mockResolvedValue({
      ref: VERSION,
      study: STUDY,
      number: 1,
      parent_version: null,
      status: "Approved",
      content_hash: null,
      amendment_intent: null,
      approval_attestation: null,
      withdrawal_reason: null,
      mask: null,
    });
    await open();

    await wrapper.find("#activity-list a").trigger("click");
    await flushPromises();

    expect(router.currentRoute.value.query).toMatchObject({
      stage: "design",
      section: "protocol-versions",
      node: VERSION,
    });
  });

  it("filters by family and pages older activity with the kernel's cursor", async () => {
    vi.mocked(kernelApi.activity)
      .mockResolvedValueOnce({ entries: [SCREENED], next: null })
      .mockResolvedValueOnce({ entries: [SCREENED], next: "cursor-1" })
      .mockResolvedValueOnce({ entries: [HIDDEN], next: null });
    await open();

    await wrapper.find("#activity-family").setValue("Execution");
    await flushPromises();

    expect(kernelApi.activity).toHaveBeenLastCalledWith({
      scope: "CAD-101",
      families: ["Execution"],
      before: undefined,
      limit: 25,
      includeNodes: true,
    });

    await wrapper.find("#btn-activity-older").trigger("click");
    await flushPromises();

    expect(kernelApi.activity).toHaveBeenLastCalledWith({
      scope: "CAD-101",
      families: ["Execution"],
      before: "cursor-1",
      limit: 25,
      includeNodes: true,
    });
    expect(rows().map((r) => r.what)).toEqual([
      "Participant screened",
      "Enrollment status changed",
    ]);
    expect(wrapper.find("#btn-activity-older").exists()).toBe(false);
    expect(kernelApi.search).not.toHaveBeenCalled();
  });

  it("says when the study has no activity yet", async () => {
    vi.mocked(kernelApi.activity).mockResolvedValue({
      entries: [],
      next: null,
    });

    await open();

    expect(wrapper.find("#activity-status").text()).toBe(
      "No activity recorded in CAD-101 yet."
    );
  });

  it("presents a plain status message and collapsible support details when activity cannot be read", async () => {
    vi.mocked(kernelApi.activity).mockRejectedValue(
      new ApiError(
        `You can't see ${STUDY} (NotPermitted), so its activity is not shown.`,
        403
      )
    );

    await open();

    expect(wrapper.find("#activity-status").text()).toContain(
      "Recent activity couldn't be loaded right now."
    );
    expect(wrapper.find("#study-activity details").text()).toContain(
      "NotPermitted"
    );
    expect(wrapper.find("#activity-list").exists()).toBe(false);
  });

  it("says in plain terms when the study wasn't found or isn't visible", async () => {
    vi.mocked(kernelApi.activity).mockRejectedValue(
      new ApiError("Study 'CAD-101' was not found.", 404)
    );

    await open();

    expect(wrapper.find("#activity-status").text()).toBe(
      "Study CAD-101 wasn't found or isn't visible to your account."
    );
    expect(kernelApi.search).not.toHaveBeenCalled();
  });
});
