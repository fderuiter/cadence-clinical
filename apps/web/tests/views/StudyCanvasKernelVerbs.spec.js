import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import CanvasInspector from "../../src/components/canvas/CanvasInspector.vue";
import CanvasPalette from "../../src/components/canvas/CanvasPalette.vue";

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
    lookupEnrollment: vi.fn(),
    getChildren: vi.fn(),
    getCommands: vi.fn(),
    getNode: vi.fn(),
    search: vi.fn(),
    trace: vi.fn(),
    dependents: vi.fn(),
  },
  isMasked: (object) => object.mask !== null,
}));

const ID = "0192f3c4-5e6a-7b8c-9d0e-000000000001";
const REF = `participant:${ID}`;
const ENROLLMENT_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000e001";
const ENROLLMENT_REF = `enrollment:${ENROLLMENT_ID}`;
const PARTICIPANT = {
  id: ID,
  subject_id: "017-0001",
  study_id: "CAD-101",
  site_id: "017",
  status: "ACTIVE",
  active_protocol_version: "3.0",
  enrollment_index: 1,
};

// The authority evaluator's answer without a grant: the command is offered, denied, and explained.
function denied(command, capability) {
  return {
    command,
    decision: {
      capability,
      verb_class: "Execute",
      resource: REF,
      permitted: false,
      reason: `crc-7 holds no grant of ${capability}.`,
      explanation: {
        subject: REF,
        claim: `You may not ${capability} on this participant.`,
        producer: "kernel.authorization",
        steps: [{ claim: "No grant covers this participant.", evidence: [] }],
        conditions: [
          {
            code: "grant.active",
            description: `Obtain a grant or delegation of ${capability} covering this participant`,
            met: false,
            evidence: [REF],
          },
        ],
        next_actions: [
          {
            description: "Ask a study administrator for a grant.",
            capability: null,
            target: null,
          },
        ],
      },
    },
  };
}

// A grant in effect covers it: permitted, with the satisfied condition naming the grant.
function permitted(command, capability) {
  const decision = denied(command, capability).decision;
  return {
    command,
    decision: {
      ...decision,
      permitted: true,
      reason: null,
      explanation: {
        ...decision.explanation,
        claim: `You may ${capability} on this participant.`,
        conditions: [
          {
            code: "grant.active",
            description: `Holds ${capability} for Study`,
            met: true,
            evidence: ["grant:0192f3c4-5e6a-7b8c-9d0e-00000000000a"],
          },
        ],
        next_actions: [],
      },
    },
  };
}

describe("Clinical Canvas kernel verbs: Trace, Impact, Act and Explain (ADR-2540)", () => {
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
      items: [PARTICIPANT],
      total: 1,
      limit: 50,
      offset: 0,
    });
    vi.mocked(executionApi.getSubjectCounts).mockResolvedValue({
      total: 1,
      counts: { ACTIVE: 1 },
    });
    vi.mocked(executionApi.getSiteComplianceStatus).mockReset();
    vi.mocked(kernelApi.lookupEnrollment).mockReset();
    vi.mocked(kernelApi.getChildren).mockReset();
    vi.mocked(kernelApi.getCommands).mockReset();
    vi.mocked(kernelApi.getNode).mockReset();
    vi.mocked(kernelApi.search).mockReset();
    vi.mocked(kernelApi.trace).mockReset();
    vi.mocked(kernelApi.dependents).mockReset();
    vi.mocked(kernelApi.getNode).mockImplementation(async (ref) => {
      if (ref.startsWith("study_site:")) {
        return {
          ref,
          kind: "study_site",
          code: "017",
          title: "Boston General",
          status: "Active",
          mask: null,
        };
      }
      if (ref.startsWith("observation:")) {
        return {
          ref,
          kind: "observation",
          code: "SYSBP",
          title: "Systolic Blood Pressure",
          status: "Recorded",
          mask: null,
        };
      }
      if (ref.startsWith("grant:")) {
        return {
          ref,
          kind: "grant",
          code: null,
          title: "Study Monitor Grant",
          status: "Active",
          mask: null,
        };
      }
      return {
        ref,
        kind: "participant",
        code: "017-0001",
        title: "017-0001",
        status: "Active",
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

  async function openParticipant() {
    await router.push(
      `/studies/CAD-101?stage=conduct&section=participants&node=${REF}`
    );
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
  }

  it("asks the kernel only when Act is chosen, then lists each command with its decision and reason", async () => {
    vi.mocked(kernelApi.getCommands).mockResolvedValue([
      denied("ChangeParticipantState", "participant.change_state"),
      permitted("WithdrawParticipant", "participant.withdraw"),
    ]);
    await openParticipant();
    expect(kernelApi.getCommands).not.toHaveBeenCalled();

    await wrapper.find("#verb-act").trigger("click");
    await flushPromises();

    expect(kernelApi.getCommands).toHaveBeenCalledWith(REF);
    expect(wrapper.find("#verb-act").attributes("aria-pressed")).toBe("true");
    expect(wrapper.find("#verb-details").attributes("aria-pressed")).toBe(
      "false"
    );
    const items = wrapper.findAll("#kernel-act-list .act-item");
    expect(items.map((i) => i.attributes("data-command"))).toEqual([
      "ChangeParticipantState",
      "WithdrawParticipant",
    ]);
    expect(items[0].text()).toContain("Not permitted");
    expect(items[0].text()).toContain(
      "crc-7 holds no grant of participant.change_state."
    );
    expect(items[1].text()).toContain("Permitted");
    expect(items[1].text()).not.toContain("Not permitted");
    expect(wrapper.find("#inspector-state").exists()).toBe(false);
  });

  it("renders the kernel's own explanation of a command's decision under Explain", async () => {
    vi.mocked(kernelApi.getCommands).mockResolvedValue([
      denied("ChangeParticipantState", "participant.change_state"),
    ]);
    await openParticipant();

    await wrapper.find("#verb-explain").trigger("click");
    expect(wrapper.find("#kernel-explain-empty").exists()).toBe(true);

    await wrapper.find("#verb-act").trigger("click");
    await flushPromises();
    await wrapper.find(".act-explain").trigger("click");

    expect(wrapper.find("#verb-explain").attributes("aria-pressed")).toBe(
      "true"
    );
    const explain = wrapper.find("#kernel-explain");
    expect(explain.find("#kernel-explain-title").text()).toBe(
      "Why: Change participant state"
    );
    expect(explain.text()).toContain(
      "You may not participant.change_state on this participant."
    );
    expect(explain.find(".condition-unmet").text()).toContain(
      "Obtain a grant or delegation of participant.change_state covering this participant"
    );
    expect(explain.find(".condition-unmet .node-ref-chip").text()).toBe(
      "017-0001"
    );
    expect(explain.text()).toContain("Ask a study administrator for a grant.");

    await wrapper.find("#verb-details").trigger("click");
    expect(wrapper.find("#inspector-state").exists()).toBe(true);
  });

  it("presents a plain status message and collapsible support details when commands cannot be read", async () => {
    vi.mocked(kernelApi.getCommands).mockRejectedValue(
      new ApiError("Your sign-in's tenant is not an active kernel tenant.", 403)
    );
    await openParticipant();

    await wrapper.find("#verb-act").trigger("click");
    await flushPromises();

    expect(wrapper.find("#kernel-act-status").text()).toContain(
      "Available actions couldn't be loaded right now."
    );
    expect(wrapper.find("#kernel-act details").text()).toContain(
      "Your sign-in's tenant is not an active kernel tenant."
    );
    expect(wrapper.find("#kernel-act-list").exists()).toBe(false);
  });

  it("says in plain clinical terms when no action is available", async () => {
    vi.mocked(kernelApi.getCommands).mockResolvedValue([]);
    await openParticipant();

    await wrapper.find("#verb-act").trigger("click");
    await flushPromises();

    expect(wrapper.find("#kernel-act-status").text()).toBe(
      "No actions are available for this record."
    );
  });

  const SITE = "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000000c@2";
  const node = (handle, kind, distance, mask = null) => ({
    handle,
    kind,
    ref: mask ? null : handle,
    mask,
    distance,
  });

  it("traces where the participant came from, showing hidden objects in place", async () => {
    vi.mocked(kernelApi.trace).mockResolvedValue({
      start: REF,
      direction: "Backward",
      depth: 10,
      nodes: [
        node(REF, "participant", 0),
        node(SITE, "study_site", 1),
        node("masked:1", "consent", 1, "Blinded"),
      ],
      edges: [
        { from: REF, to: SITE, kind: "GovernedBy" },
        { from: REF, to: "masked:1", kind: "RequiredBy" },
      ],
      truncated: true,
    });
    await openParticipant();
    expect(kernelApi.trace).not.toHaveBeenCalled();

    await wrapper.find("#verb-trace").trigger("click");
    await flushPromises();

    expect(kernelApi.trace).toHaveBeenCalledWith(REF);
    expect(wrapper.find("#verb-trace").attributes("aria-pressed")).toBe("true");
    const edges = wrapper.findAll("#kernel-trace-list .lineage-edge");
    expect(edges.map((e) => e.text().replace(/\s+/g, " "))).toEqual([
      "017-0001 governed by Site 017 · Boston General",
      "017-0001 required by Hidden consent (blinded)",
    ]);
    expect(wrapper.find("#kernel-trace").text()).toContain(
      "The lineage goes further than the 10 steps shown."
    );

    // Clicking a referenced study object in lineage selects it in CanvasInspector
    const siteLink = wrapper
      .findAll("#kernel-trace-list .node-ref-chip")
      .find((link) => link.text().includes("Site 017"));
    await siteLink.trigger("click");
    await flushPromises();
    expect(router.currentRoute.value.query.node).toBe(SITE);
    expect(wrapper.find("#canvas-inspector-title").text()).toBe(
      "Site 017 · Boston General"
    );
  });

  it("counts what depends on the participant by kind, hidden objects included", async () => {
    vi.mocked(kernelApi.dependents).mockResolvedValue({
      start: REF,
      depth: 10,
      groups: [
        {
          kind: "observation",
          count: 2,
          objects: [
            node(
              "observation:0192f3c4-5e6a-7b8c-9d0e-00000000000d",
              "observation",
              1
            ),
            node("masked:1", "observation", 1, "NotPermitted"),
          ],
        },
      ],
      total: 2,
      truncated: false,
    });
    await openParticipant();

    await wrapper.find("#verb-impact").trigger("click");
    await flushPromises();

    expect(kernelApi.dependents).toHaveBeenCalledWith(REF);
    expect(wrapper.find("#kernel-impact-total").text()).toBe(
      "2 dependent objects"
    );
    const group = wrapper.find('#kernel-impact-list [data-kind="observation"]');
    expect(group.find("summary").text().replace(/\s+/g, " ")).toBe(
      "observation: 2"
    );
    expect(group.text()).toContain("Hidden observation (not permitted)");
  });

  it("says the count is a lower bound when the kernel cut the walk short", async () => {
    vi.mocked(kernelApi.dependents).mockResolvedValue({
      start: REF,
      depth: 10,
      groups: [{ kind: "observation", count: 1, objects: [] }],
      total: 1,
      truncated: true,
    });
    await openParticipant();

    await wrapper.find("#verb-impact").trigger("click");
    await flushPromises();

    expect(wrapper.find("#kernel-impact-total").text()).toBe(
      "At least 1 dependent object"
    );
  });

  it.each([
    ["trace", "No upstream source records are recorded for this item."],
    ["impact", "No downstream records depend on this item."],
  ])("says when %s finds nothing", async (verb, message) => {
    vi.mocked(kernelApi.trace).mockResolvedValue({
      nodes: [node(REF, "participant", 0)],
      edges: [],
      truncated: false,
    });
    vi.mocked(kernelApi.dependents).mockResolvedValue({
      groups: [],
      total: 0,
      truncated: false,
    });
    await openParticipant();

    await wrapper.find(`#verb-${verb}`).trigger("click");
    await flushPromises();

    expect(wrapper.find(`#kernel-${verb}`).text()).toContain(message);
  });

  it("presents a plain status message and support disclosure when lineage fails", async () => {
    vi.mocked(kernelApi.trace).mockRejectedValue(
      new ApiError("Lineage reads name their reader.", 403)
    );
    await openParticipant();

    await wrapper.find("#verb-trace").trigger("click");
    await flushPromises();

    expect(wrapper.find("#kernel-trace [role=alert]").text()).toContain(
      "Related records couldn't be loaded right now."
    );
    expect(wrapper.find("#kernel-trace details").text()).toContain(
      "Lineage reads name their reader."
    );
    expect(wrapper.find("#kernel-trace-list").exists()).toBe(false);
  });

  it("goes back to Details when another object is selected", async () => {
    vi.mocked(kernelApi.getCommands).mockResolvedValue([]);
    await openParticipant();
    await wrapper.find("#verb-act").trigger("click");
    await flushPromises();

    await router.push(`/studies/CAD-101?stage=conduct&section=participants`);
    await flushPromises();
    await router.push(
      `/studies/CAD-101?stage=conduct&section=participants&node=${REF}`
    );
    await flushPromises();

    expect(wrapper.find("#verb-details").attributes("aria-pressed")).toBe(
      "true"
    );
    expect(wrapper.find("#kernel-act").exists()).toBe(false);
  });

  it("binds enrollment:<id> across Act, Trace, and Impact when the participant has a visible enrollment", async () => {
    const studyRef = "study:0192f3c4-5e6a-7b8c-9d0e-000000000010";
    vi.mocked(kernelApi.lookupEnrollment).mockResolvedValueOnce({
      enrollment_ref: `${ENROLLMENT_REF}@1`,
      study_ref: `${studyRef}@1`,
      study_code: "CAD-101",
      site_code: "017",
      subject_code: "017-0001",
    });
    vi.mocked(kernelApi.getCommands).mockResolvedValue([
      permitted("OpenCollectionInstance", "collection.open"),
    ]);
    vi.mocked(kernelApi.trace).mockResolvedValue({
      nodes: [
        {
          ref: `${ENROLLMENT_REF}@1`,
          kind: "enrollment",
          depth: 0,
          mask: null,
          recorded_at: "2026-04-01T10:00:00Z",
          actor_id: "crc-7",
          action: "enrollment.screen",
        },
      ],
      edges: [],
      truncated: false,
    });
    vi.mocked(kernelApi.dependents).mockResolvedValue({
      start: ENROLLMENT_REF,
      depth: 10,
      groups: [],
      total: 0,
      truncated: false,
    });

    await openParticipant();

    await wrapper.find("#verb-act").trigger("click");
    await flushPromises();
    expect(kernelApi.getCommands).toHaveBeenCalledWith(ENROLLMENT_REF);

    await wrapper.find("#verb-trace").trigger("click");
    await flushPromises();
    expect(kernelApi.trace).toHaveBeenCalledWith(ENROLLMENT_REF);

    await wrapper.find("#verb-impact").trigger("click");
    await flushPromises();
    expect(kernelApi.dependents).toHaveBeenCalledWith(ENROLLMENT_REF);
  });
});

describe("⌘K palette kernel search (ADR-2484)", () => {
  let wrapper;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.mocked(kernelApi.search).mockReset();
  });

  afterEach(() => {
    wrapper?.unmount();
    vi.useRealTimers();
  });

  function mountPalette() {
    wrapper = mount(CanvasPalette, {
      props: { studyCode: "CAD-101", participants: [] },
      attachTo: document.body,
    });
  }

  async function type(text) {
    await wrapper.find("#canvas-palette-input").setValue(text);
    await vi.advanceTimersByTimeAsync(300);
    await flushPromises();
  }

  it("finds studies the kernel lets the reader see and opens one by its code", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: "study:0192f3c4-5e6a-7b8c-9d0e-00000000000b@2",
        kind: "study",
        title: "Oncology 401",
        code: "ONC-401",
        detail: null,
        exact_code: false,
        rank: 0.8,
      },
    ]);
    mountPalette();

    await type("onc");

    expect(kernelApi.search).toHaveBeenCalledTimes(1);
    expect(kernelApi.search).toHaveBeenCalledWith({
      text: "onc",
      kinds: ["study"],
      limit: 5,
    });
    const options = wrapper.findAll("#canvas-palette-list [role=option]");
    const study = options.find((o) => o.text().includes("Oncology 401"));
    expect(study.text()).toContain("ONC-401");
    await study.trigger("click");
    expect(wrapper.emitted("go")).toEqual([[{ studyCode: "ONC-401" }]]);
  });

  it("offers the kernel's study rather than a second 'Open study' row for the same code", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: "study:0192f3c4-5e6a-7b8c-9d0e-00000000000b@2",
        kind: "study",
        title: "Oncology 401",
        code: "ONC-401",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    mountPalette();

    await type("ONC-401");

    const labels = wrapper
      .findAll("#canvas-palette-list [role=option]")
      .map((o) => o.find(".palette-group").text());
    expect(labels.filter((g) => g === "Open study")).toHaveLength(0);
    expect(labels).toContain("Study");
  });

  it("waits for typing to pause and skips one-character queries", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([]);
    mountPalette();

    await wrapper.find("#canvas-palette-input").setValue("o");
    await wrapper.find("#canvas-palette-input").setValue("on");
    await wrapper.find("#canvas-palette-input").setValue("onc");
    await vi.advanceTimersByTimeAsync(300);
    await flushPromises();
    await type("x");

    expect(kernelApi.search).toHaveBeenCalledTimes(1);
    expect(kernelApi.search.mock.calls[0][0].text).toBe("onc");
  });

  it("says when studies can't be searched, and still offers opening by code", async () => {
    vi.mocked(kernelApi.search).mockRejectedValue(
      new ApiError("Your sign-in's tenant is not an active kernel tenant.", 403)
    );
    mountPalette();

    await type("onc-401");

    expect(wrapper.find("#canvas-palette-kernel-note").text()).toBe(
      "Studies can't be searched right now: Your sign-in's tenant is not an active kernel tenant."
    );
    const groups = wrapper
      .findAll("#canvas-palette-list [role=option]")
      .map((o) => o.find(".palette-group").text());
    expect(groups).toContain("Open study");
  });
});
