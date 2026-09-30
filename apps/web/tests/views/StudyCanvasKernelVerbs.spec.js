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
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: { getCommands: vi.fn(), search: vi.fn() },
}));

const ID = "0192f3c4-5e6a-7b8c-9d0e-000000000001";
const REF = `participant:${ID}`;
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

describe("Clinical Canvas kernel verbs: Act and Explain (ADR-2540)", () => {
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
    vi.mocked(executionApi.getSiteComplianceStatus).mockReset();
    vi.mocked(kernelApi.getCommands).mockReset();
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
      "Why: ChangeParticipantState"
    );
    expect(explain.text()).toContain(
      "You may not participant.change_state on this participant."
    );
    expect(explain.find(".condition-unmet").text()).toContain(
      "Obtain a grant or delegation of participant.change_state covering this participant"
    );
    expect(explain.find(".condition-unmet code").text()).toBe(REF);
    expect(explain.text()).toContain("Ask a study administrator for a grant.");

    await wrapper.find("#verb-details").trigger("click");
    expect(wrapper.find("#inspector-state").exists()).toBe(true);
  });

  it("reports the kernel's refusal as it is rather than listing nothing", async () => {
    vi.mocked(kernelApi.getCommands).mockRejectedValue(
      new ApiError("Your sign-in's tenant is not an active kernel tenant.", 403)
    );
    await openParticipant();

    await wrapper.find("#verb-act").trigger("click");
    await flushPromises();

    expect(wrapper.find("#kernel-act-status").text()).toBe(
      "The kernel couldn't answer: Your sign-in's tenant is not an active kernel tenant."
    );
    expect(wrapper.find("#kernel-act-list").exists()).toBe(false);
  });

  it("says when no kernel command applies", async () => {
    vi.mocked(kernelApi.getCommands).mockResolvedValue([]);
    await openParticipant();

    await wrapper.find("#verb-act").trigger("click");
    await flushPromises();

    expect(wrapper.find("#kernel-act-status").text()).toBe(
      "No kernel command applies to this object."
    );
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
});

describe("CanvasInspector on an observation", () => {
  it("keeps Act and Explain disabled, since an observation has no kernel reference yet", async () => {
    const wrapper = mount(CanvasInspector, {
      props: {
        studyCode: "CAD-101",
        participant: PARTICIPANT,
        nodeRef: REF,
        missing: false,
        observation: {
          id: "obs-1",
          test_name: "Hemoglobin",
          test_code: "HGB",
          domain: "LB",
          observation_date: "2026-09-01",
          value: 13.2,
          unit: "g/dL",
          is_sdv_verified: false,
        },
      },
      global: { stubs: { RouterLink: true } },
    });

    for (const verb of ["act", "explain"]) {
      const button = wrapper.find(`#verb-${verb}`);
      expect(button.attributes("aria-disabled")).toBe("true");
      await button.trigger("click");
      expect(wrapper.find("#verb-note").text()).toContain(
        "an observation has no kernel reference yet"
      );
    }
    expect(kernelApi.getCommands).not.toHaveBeenCalled();
    wrapper.unmount();
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
