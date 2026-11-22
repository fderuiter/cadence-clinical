import type { KernelProposal } from "../api/kernelApi";

/**
 * Review rules for a kernel Proposal on the Canvas (ADR-2489, ADR-2630 §4.6, ADR-2635). The kernel decides; these
 * rules only decide what the screen asks of the reviewer before it offers a decision, and say why a control is
 * disabled (UX-9). They never relax a kernel rule: the kernel refuses self-approval, a stale hash and an expired
 * proposal whatever the screen shows.
 */

/** True when an automation agent proposed it, so it carries the AI colour and label (UX-8). */
export function isAgentProposal(proposal: KernelProposal): boolean {
  return proposal.proposed_by_kind === "AutomationAgent";
}

/** A source the reviewer can open: the plan or an object the proposal names. */
export interface ProposalSource {
  ref: string;
  role: "plan" | "affected";
  /** Why the reviewer may not see it, or null when they may. */
  mask: string | null;
}

/** The plan first, then the affected objects in the order proposed. */
export function proposalSources(proposal: KernelProposal): ProposalSource[] {
  const sources: ProposalSource[] = [];
  if (proposal.plan) {
    sources.push({
      ref: proposal.plan.ref,
      role: "plan",
      mask: proposal.plan.mask,
    });
  }
  for (const item of proposal.affected ?? []) {
    sources.push({ ref: item.ref, role: "affected", mask: item.mask });
  }
  return sources;
}

/**
 * How closely the reviewer must check before approving. A high-risk task configuration requires each source to be
 * opened (ADR-2630 §4.6). Task configurations and their risk class arrive with #5385; until a proposal names one, an
 * agent's proposal is reviewed as high risk, so the screen never asks less than the strictest configuration would.
 */
export type ReviewRisk = "high" | "standard";

export function reviewRisk(proposal: KernelProposal): ReviewRisk {
  return isAgentProposal(proposal) ? "high" : "standard";
}

/** One reason the screen cannot offer approval yet, in words the reviewer can act on. */
export interface ApprovalBlocker {
  id:
    | "not-open"
    | "own-proposal"
    | "hidden-sources"
    | "unopened-sources"
    | "signing-unavailable";
  text: string;
}

export interface ApprovalContext {
  /** The signed-in person's id, as the kernel records actors. */
  reviewer: string;
  /** Sources the reviewer has opened on this screen. */
  opened: ReadonlySet<string>;
  /** Whether this deployment can sign a proposal approval (#5552). */
  signingAvailable: boolean;
}

/** Every reason approval can't be offered now; empty when the reviewer may sign. */
export function approvalBlockers(
  proposal: KernelProposal,
  { reviewer, opened, signingAvailable }: ApprovalContext
): ApprovalBlocker[] {
  const blockers: ApprovalBlocker[] = [];
  if (proposal.status !== "Open") {
    blockers.push({
      id: "not-open",
      text: `This proposal is ${(proposal.status ?? "not open").toLowerCase()}, so it can no longer be approved.`,
    });
    return blockers;
  }
  if (proposal.proposed_by === reviewer) {
    blockers.push({
      id: "own-proposal",
      text: "You made this proposal, so another person must approve it.",
    });
  }
  const sources = proposalSources(proposal);
  const hidden = sources.filter((s) => s.mask !== null).length;
  if (hidden > 0) {
    blockers.push({
      id: "hidden-sources",
      text: `You may not see ${hidden} of the objects it names, so you can't review all of it. Ask someone who can see them to approve it.`,
    });
  }
  if (reviewRisk(proposal) === "high") {
    const left = sources.filter(
      (s) => s.mask === null && !opened.has(s.ref)
    ).length;
    if (left > 0) {
      blockers.push({
        id: "unopened-sources",
        text: `Open each source it reasoned over before approving: ${left} still to open.`,
      });
    }
  }
  if (!signingAvailable) {
    blockers.push({
      id: "signing-unavailable",
      text: "Approving requires an electronic signature, which isn't available in the web workspace yet. You can still reject this proposal below.",
    });
  }
  return blockers;
}

/** "proposal.reject" or "protocol.approve" in plain words: "Approve protocol". */
export function capabilityWords(token: string): string {
  const [resource, verb] = token.split(".");
  if (!verb) return token;
  const action = verb.replace(/_/g, " ");
  const subject = resource.replace(/_/g, " ");
  return `${action.charAt(0).toUpperCase()}${action.slice(1)} ${subject}`;
}
