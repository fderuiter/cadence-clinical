import ProposalReview from "../../components/ai/ProposalReview.vue";
import type { CanvasWorkspaceDescriptor } from "./types";

const proposalWorkspace: CanvasWorkspaceDescriptor = {
  id: "proposal",
  order: 10,
  matches: (ctx) => Boolean(ctx.proposalRef),
  resolveLabel: () => "Proposal review",
  resolveBranch: (ctx) => ({
    key: `study:${ctx.studyCode}:proposal:${ctx.proposalRef}`,
    groupKey: `study:${ctx.studyCode}:proposal`,
    depth: 4,
    targets: ["#proposal-review-title", "#proposal-review-status"],
  }),
  resolveRender: (ctx) => ({
    component: ProposalReview,
    key: ctx.proposalRef,
    props: {
      studyCode: ctx.studyCode,
      proposalRef: ctx.proposalRef,
      reviewer: ctx.reviewer,
    },
  }),
};

export default proposalWorkspace;
