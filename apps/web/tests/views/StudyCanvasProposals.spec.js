import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import { useAuthStore } from "../../src/stores/auth";
import { reloadAiAssistance } from "../../src/ai/aiAssistance";
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
    listProposals: vi.fn(),
    getProposal: vi.fn(),
    rejectProposal: vi.fn(),
    getNode: vi.fn(),
    getCommands: vi.fn(),
    listProtocolVersions: vi.fn(),
  },
  isMasked: (object) => object.mask !== null,
}));

const STUDY = "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1@2";
const PROPOSAL = "proposal:0192f3c4-5e6a-7b8c-9d0e-0000000000e1";
const PLAN = "artifact_version:0192f3c4-5e6a-7b8c-9d0e-0000000000d1@1";
const PARTICIPANT_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000001";
const PARTICIPANT_REF = `participant:${PARTICIPANT_ID}`;
const PARTICIPANT = {
  id: PARTICIPANT_ID,
  subject_id: "017-0001",
  study_id: "CAD-101",
  site_id: "017",
  status: "ACTIVE",
  active_protocol_version: "3.0",
  enrollment_index: 1,
};

const proposal = (overrides = {}) => ({
  ref: `${PROPOSAL}@1`,
  study: STUDY,
  intent: "Query the out-of-range systolic pressure at visit 3",
  plan: {
    ref: PLAN,
    title: "Data review plan",
    number: 1,
    media_type: "text/markdown",
    content_hash: "sha256:plan",
    mask: null,
  },
  affected: [{ ref: `${PARTICIPANT_REF}@4`, mask: null }],
  simulated_impact: null,
  requested_capabilities: ["query.raise"],
  proposed_at: "2026-10-20T09:00:00Z",
  expires_at: "2026-10-27T09:00:00Z",
  proposed_by: "agent:data-reviewer",
  requested_by: "data-manager@sponsor.test",
  proposed_by_kind: "AutomationAgent",
  model_id: "claude-test-model",
  confidence_score: 0.82,
  content_hash: "sha256:proposal",
  status: "Open",
  decided_at: null,
  decided_by: null,
  approval_attestation: null,
  decision_reason: null,
  is_escalated: false,
  escalated_to: null,
  escalation_reason: null,
  mask: null,
  ...overrides,
});

describe("Clinical Canvas AI proposals (ADR-2635, PX-10, PX-17, UX-8)", () => {
  let pinia;
  let router;
  let wrapper;

  beforeEach(() => {
    localStorage.clear();
    reloadAiAssistance();
    pinia = createPinia();
    setActivePinia(pinia);
    useAuthStore().user = { id: "reviewer-1", username: "reviewer" };
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
    vi.mocked(kernelApi.listProposals).mockResolvedValue([proposal()]);
    vi.mocked(kernelApi.getProposal).mockResolvedValue(proposal());
    vi.mocked(kernelApi.getCommands).mockResolvedValue([]);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([]);
    vi.mocked(kernelApi.getNode).mockResolvedValue({
      ref: `${PARTICIPANT_REF}@4`,
      kind: "participant",
      title: "017-0001",
      code: "017-0001",
      status: "Active",
      mask: null,
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

  const blockers = () =>
    wrapper.findAll("#proposal-approve-blockers li").map((li) => li.text());

  describe("Needs attention on study home", () => {
    it("lists the study's open proposals from the kernel, an agent's with the AI label", async () => {
      await open();

      expect(kernelApi.listProposals).toHaveBeenCalledWith(STUDY, {
        status: "Open",
        limit: 20,
      });
      const item = wrapper.find("#needs-attention-list li");
      expect(item.find(".ai-label").text()).toContain(
        "Proposed by an AI agent"
      );
      expect(item.find(".ai-label").text()).toContain("claude-test-model");
      const link = wrapper.find("#needs-attention-proposal-0");
      expect(link.text()).toBe(
        "Query the out-of-range systolic pressure at visit 3"
      );
      expect(decodeURIComponent(link.attributes("href"))).toBe(
        `/studies/CAD-101?proposal=${PROPOSAL}@1`
      );
    });

    it("keeps a proposal the reader may not see in place as hidden", async () => {
      vi.mocked(kernelApi.listProposals).mockResolvedValue([
        proposal({ ref: null, intent: null, mask: "NotPermitted" }),
      ]);

      await open();

      expect(wrapper.find("#needs-attention-list li").text()).toContain(
        "Hidden proposal (not permitted)"
      );
      expect(wrapper.find("#needs-attention-proposal-0").exists()).toBe(false);
    });

    it("says so when nothing waits on a decision, and is accessible", async () => {
      vi.mocked(kernelApi.listProposals).mockResolvedValue([]);

      await open();

      expect(wrapper.find("#needs-attention-status").text()).toBe(
        "Nothing is waiting on a decision."
      );
      await expect(wrapper.find("#needs-attention").element).toBeAccessible();
    });
  });

  describe("Proposal review", () => {
    it("shows the intent, proposer, model, capabilities and sources, and is accessible", async () => {
      await open(`/studies/CAD-101?proposal=${PROPOSAL}`);

      expect(kernelApi.getProposal).toHaveBeenCalledWith(PROPOSAL);
      expect(wrapper.find("#proposal-review-title").text()).toBe(
        "Query the out-of-range systolic pressure at visit 3"
      );
      expect(wrapper.find("#proposal-review-ai").text()).toContain(
        "claude-test-model"
      );
      expect(wrapper.find("#proposal-review-proposer").text()).toBe(
        "AI agent data-reviewer, for data-manager@sponsor.test"
      );
      expect(wrapper.find("#proposal-review-configuration").text()).toContain(
        "#5385"
      );
      expect(wrapper.find("#proposal-review-hash").text()).toBe(
        "sha256:proposal"
      );
      expect(wrapper.find("#proposal-review-capability-list").text()).toContain(
        "Raise query"
      );
      expect(wrapper.find("#proposal-review-safety").text()).toContain(
        "Nothing"
      );
      expect(wrapper.findAll("#proposal-review-source-list li")).toHaveLength(
        2
      );
      await expect(wrapper.find("#proposal-review").element).toBeAccessible();
    });

    it("offers approval only once each source is opened, and says signing waits on #5552", async () => {
      await open(`/studies/CAD-101?proposal=${PROPOSAL}`);

      const approve = wrapper.find("#btn-approve-proposal");
      expect(approve.attributes("aria-disabled")).toBe("true");
      expect(blockers()).toEqual([
        "Open each source it reasoned over before approving: 2 still to open.",
        expect.stringContaining("#5552"),
      ]);

      await wrapper.find("#proposal-source-open-0").trigger("click");
      await flushPromises();
      expect(wrapper.text()).toContain("Data review plan");
      await wrapper.find("#proposal-source-open-1").trigger("click");
      await flushPromises();

      expect(kernelApi.getNode).toHaveBeenCalledWith(`${PARTICIPANT_REF}@4`);
      expect(wrapper.find("#proposal-review-open-rule").text()).toContain(
        "2 of 2 opened"
      );
      expect(blockers()).toEqual([expect.stringContaining("#5552")]);
      expect(approve.attributes("aria-disabled")).toBe("true");
    });

    it("counts a source the kernel has no view of as opened, saying so", async () => {
      vi.mocked(kernelApi.getNode).mockRejectedValue(
        new ApiError("No such node.", 404)
      );
      await open(`/studies/CAD-101?proposal=${PROPOSAL}`);

      await wrapper.find("#proposal-source-open-1").trigger("click");
      await flushPromises();

      expect(wrapper.find("[role=alert]").text()).toContain(
        "The kernel has no view of a participant yet"
      );
      expect(blockers()[0]).toContain("1 still to open");
    });

    it("refuses approval of the reviewer's own proposal and of one with hidden sources", async () => {
      vi.mocked(kernelApi.getProposal).mockResolvedValue(
        proposal({
          proposed_by: "reviewer-1",
          proposed_by_kind: "Person",
          affected: [{ ref: null, mask: "NotPermitted" }],
        })
      );

      await open(`/studies/CAD-101?proposal=${PROPOSAL}`);

      expect(wrapper.find("#proposal-review-ai").exists()).toBe(false);
      expect(blockers()).toEqual([
        "You made this proposal, so another person approves it (ADR-2489).",
        expect.stringContaining("You may not see 1 of the objects it names"),
        expect.stringContaining("#5552"),
      ]);
    });

    it("rejects with the reviewer's reason and shows the kernel's decision", async () => {
      vi.mocked(kernelApi.rejectProposal).mockResolvedValue({
        ref: `${PROPOSAL}@2`,
        status: "Rejected",
        content_hash: "sha256:proposal",
        decided_at: "2026-10-20T10:00:00Z",
        decided_by: "reviewer-1",
      });
      await open(`/studies/CAD-101?proposal=${PROPOSAL}`);

      const reject = wrapper.find("#btn-reject-proposal");
      expect(reject.attributes("disabled")).toBeDefined();
      await wrapper
        .find("#proposal-reject-reason")
        .setValue("The value is within the protocol's range.");
      vi.mocked(kernelApi.getProposal).mockResolvedValue(
        proposal({
          status: "Rejected",
          decided_at: "2026-10-20T10:00:00Z",
          decided_by: "reviewer-1",
          decision_reason: "The value is within the protocol's range.",
        })
      );
      await wrapper.find("#proposal-reject-form").trigger("submit");
      await flushPromises();

      expect(kernelApi.rejectProposal).toHaveBeenCalledWith({
        proposalRef: `${PROPOSAL}@1`,
        reason: "The value is within the protocol's range.",
        idempotencyKey: expect.any(String),
      });
      expect(wrapper.find("#proposal-review-state").text()).toBe("Rejected");
      expect(wrapper.find("#proposal-review-decision-text").text()).toContain(
        "Rejected by reviewer-1"
      );
      expect(wrapper.find("#btn-approve-proposal").exists()).toBe(false);
    });

    it("shows the kernel's refusal of a rejection", async () => {
      vi.mocked(kernelApi.rejectProposal).mockRejectedValue(
        new ApiError("The proposal is no longer open.", 409)
      );
      await open(`/studies/CAD-101?proposal=${PROPOSAL}`);

      await wrapper.find("#proposal-reject-reason").setValue("Not needed.");
      await wrapper.find("#proposal-reject-form").trigger("submit");
      await flushPromises();

      expect(wrapper.find("#proposal-reject-error").text()).toBe(
        "The kernel refused the rejection: The proposal is no longer open."
      );
    });
  });

  describe("Assist and the palette's Ask", () => {
    const selectParticipant = () =>
      open(
        `/studies/CAD-101?stage=conduct&section=participants&node=${PARTICIPANT_REF}`
      );

    it("lists the proposals naming the selected node and explains why Ask waits", async () => {
      await selectParticipant();

      await wrapper.find("#verb-assist").trigger("click");
      await flushPromises();

      expect(kernelApi.listProposals).toHaveBeenCalledWith(STUDY, {
        limit: 100,
      });
      expect(wrapper.find("#ai-assist-proposal-0").text()).toBe(
        "Query the out-of-range systolic pressure at visit 3"
      );
      const ask = wrapper.find("#btn-ai-assist-ask");
      expect(ask.attributes("aria-disabled")).toBe("true");
      expect(wrapper.find("#ai-assist-unavailable").text()).toContain("#5547");
      await expect(wrapper.find("#ai-assist").element).toBeAccessible();

      await wrapper.find("#btn-ai-assist-act").trigger("click");
      expect(wrapper.find("#verb-act").attributes("aria-pressed")).toBe("true");
    });

    it("leaves out proposals that don't name the selected node", async () => {
      vi.mocked(kernelApi.listProposals).mockResolvedValue([
        proposal({ affected: [] }),
      ]);
      await selectParticipant();

      await wrapper.find("#verb-assist").trigger("click");
      await flushPromises();

      expect(wrapper.find("#ai-assist-proposals").exists()).toBe(false);
      expect(wrapper.find("#ai-assist-status").text()).toBe(
        "No proposal names this yet."
      );
    });

    it("turns a sentence in the palette into a question for Assist on the selection", async () => {
      await selectParticipant();

      await wrapper.find("#btn-open-palette").trigger("click");
      await wrapper
        .find("#canvas-palette-input")
        .setValue("which visits are out of window");
      const options = wrapper.findAll("#canvas-palette-list [role=option]");
      const ask = options.find((o) => o.text().includes("Ask AI"));
      expect(ask?.text()).toContain("Nothing changes until a person decides");
      await ask.trigger("click");
      await flushPromises();

      expect(router.currentRoute.value.query.ask).toBe(
        "which visits are out of window"
      );
      expect(router.currentRoute.value.query.node).toBe(PARTICIPANT_REF);
      expect(wrapper.find("#ai-assist-question").element.value).toBe(
        "which visits are out of window"
      );
    });

    it("offers no Ask for fewer than three words", async () => {
      await open();

      await wrapper.find("#btn-open-palette").trigger("click");
      await wrapper.find("#canvas-palette-input").setValue("conduct");

      expect(wrapper.text()).not.toContain("Ask AI");
    });
  });

  describe("with AI assistance off", () => {
    it("hides Assist and Ask, keeps Needs attention and review, and remembers the choice", async () => {
      await selectAndTurnOff();

      expect(
        wrapper.find("#btn-ai-assistance").attributes("aria-pressed")
      ).toBe("false");
      expect(localStorage.getItem("cadence.ai.assistance")).toBe("off");
      expect(wrapper.find("#verb-assist").exists()).toBe(false);
      expect(wrapper.find("#ai-assist").exists()).toBe(false);

      await wrapper.find("#btn-open-palette").trigger("click");
      await wrapper
        .find("#canvas-palette-input")
        .setValue("which visits are out of window");
      expect(wrapper.text()).not.toContain("Ask AI");

      await router.push("/studies/CAD-101");
      await flushPromises();
      expect(wrapper.find("#needs-attention-proposal-0").exists()).toBe(true);

      await router.push(`/studies/CAD-101?proposal=${PROPOSAL}`);
      await flushPromises();
      expect(wrapper.find("#proposal-review-ai").exists()).toBe(true);
      expect(wrapper.find("#proposal-reject-form").exists()).toBe(true);
    });

    async function selectAndTurnOff() {
      await open(
        `/studies/CAD-101?stage=conduct&section=participants&node=${PARTICIPANT_REF}`
      );
      await wrapper.find("#verb-assist").trigger("click");
      await flushPromises();
      expect(wrapper.find("#ai-assist").exists()).toBe(true);
      await wrapper.find("#btn-ai-assistance").trigger("click");
      await flushPromises();
    }
  });
});
