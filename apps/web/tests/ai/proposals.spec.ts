import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import type { KernelProposal } from "../../src/api/kernelApi";
import {
  approvalBlockers,
  capabilityWords,
  isAgentProposal,
  proposalSources,
  reviewRisk,
} from "../../src/ai/proposals";
import AiLabel from "../../src/components/ai/AiLabel.vue";

const PLAN = "artifact_version:0192f3c4-5e6a-7b8c-9d0e-0000000000d1@1";
const NODE = "participant:0192f3c4-5e6a-7b8c-9d0e-000000000001@4";

function proposal(overrides: Partial<KernelProposal> = {}): KernelProposal {
  return {
    ref: "proposal:0192f3c4-5e6a-7b8c-9d0e-0000000000e1@1",
    study: "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1@2",
    intent: "Raise a query",
    plan: {
      ref: PLAN,
      title: "Plan",
      number: 1,
      media_type: "text/markdown",
      content_hash: "sha256:plan",
      mask: null,
    },
    affected: [{ ref: NODE, mask: null }],
    simulated_impact: null,
    requested_capabilities: ["query.raise"],
    proposed_at: "2026-10-20T09:00:00Z",
    expires_at: "2026-10-27T09:00:00Z",
    proposed_by: "data-reviewer",
    proposed_by_kind: "AutomationAgent",
    model_id: "model",
    confidence_score: null,
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
  } as KernelProposal;
}

const ids = (p: KernelProposal, opened: string[] = [], signing = true) =>
  approvalBlockers(p, {
    reviewer: "reviewer-1",
    opened: new Set(opened),
    signingAvailable: signing,
  }).map((b) => b.id);

describe("proposal review rules (ADR-2635)", () => {
  it("lists the plan first, then the affected objects", () => {
    expect(proposalSources(proposal()).map((s) => [s.role, s.ref])).toEqual([
      ["plan", PLAN],
      ["affected", NODE],
    ]);
  });

  it("reviews an agent's proposal as high risk and a person's as standard", () => {
    expect(isAgentProposal(proposal())).toBe(true);
    expect(reviewRisk(proposal())).toBe("high");
    const person = proposal({ proposed_by_kind: "Person" });
    expect(isAgentProposal(person)).toBe(false);
    expect(reviewRisk(person)).toBe("standard");
  });

  it("asks for every visible source to be opened on a high-risk proposal", () => {
    expect(ids(proposal())).toEqual(["unopened-sources"]);
    expect(ids(proposal(), [PLAN])).toEqual(["unopened-sources"]);
    expect(ids(proposal(), [PLAN, NODE])).toEqual([]);
    expect(ids(proposal({ proposed_by_kind: "Person" }))).toEqual([]);
  });

  it("refuses the proposer, hidden sources and a deployment that can't sign", () => {
    const own = proposal({
      proposed_by: "reviewer-1",
      proposed_by_kind: "Person",
      affected: [{ ref: "masked:1", mask: "NotPermitted" }],
    });
    expect(ids(own, [], false)).toEqual([
      "own-proposal",
      "hidden-sources",
      "signing-unavailable",
    ]);
  });

  it("offers nothing on a decided or expired proposal", () => {
    expect(ids(proposal({ status: "Expired" }), [], false)).toEqual([
      "not-open",
    ]);
  });

  it("puts a capability in plain words", () => {
    expect(capabilityWords("query.raise")).toBe("Raise query");
    expect(capabilityWords("protocol_version.approve")).toBe(
      "Approve protocol version"
    );
    expect(capabilityWords("bare")).toBe("bare");
  });
});

describe("AI label (UX-8)", () => {
  it("says AI in words, names the model and configuration, and is accessible", async () => {
    const wrapper = mount(AiLabel, {
      props: {
        label: "Proposed by an AI agent",
        model: "model-x",
        configuration: "data-review@3",
      },
    });

    expect(wrapper.find(".ai-mark").text()).toBe("AI");
    expect(wrapper.text()).toContain(
      "Proposed by an AI agent · model-x · configuration data-review@3"
    );
    expect(wrapper.attributes("title")).toContain("A person reviews");
    await expect(wrapper.element).toBeAccessible();
  });
});
