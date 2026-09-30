import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";
import CanvasInspector from "../../src/components/canvas/CanvasInspector.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: { getCommands: vi.fn() },
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

// The kernel's fail-closed answer today: the command is offered, denied, and explained.
function denied(command, capability) {
  return {
    command,
    decision: {
      capability,
      verb_class: "Execute",
      resource: REF,
      permitted: false,
      reason:
        "Permissions can't be evaluated yet, so no kernel action is permitted.",
      explanation: {
        subject: REF,
        claim: `You may not ${capability} on this participant.`,
        producer: "kernel.authorization",
        steps: [{ claim: "No grant covers this participant.", evidence: [] }],
        conditions: [
          {
            code: "authority.evaluator",
            description:
              "Permissions are evaluated from grants, delegations and qualifications",
            met: false,
            evidence: [],
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
      denied("WithdrawParticipant", "participant.withdraw"),
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
      "Permissions can't be evaluated yet, so no kernel action is permitted."
    );
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
      "Permissions are evaluated from grants, delegations and qualifications"
    );
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
