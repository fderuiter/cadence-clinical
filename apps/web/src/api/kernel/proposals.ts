import { lazyKernelAction, type KernelSchemas } from "./shared";

const loadRouteTransport = () => import("./proposals.transport");

export type KernelProposal = KernelSchemas["Kernel_KernelProposalResponse"];
export type KernelProposalState =
  KernelSchemas["Kernel_KernelProposalStateResponse"];

/** A proposal's status as the kernel derives it: an open proposal past its expiry reads as Expired (ADR-2489). */
export type KernelProposalStatus =
  "Open" | "Approved" | "Rejected" | "Withdrawn" | "Expired";

/** A rejection of one proposal; the reason is stored as the decision reason and in the audit trail. */
export interface KernelProposalRejection {
  proposalRef: string;
  reason: string;
  /** One key per rejection the user confirms, reused if that request is retried. */
  idempotencyKey: string;
}

export const kernelProposalsApi = {
  /**
   * A study's proposals, newest first (GET /api/v1/kernel/studies/{studyRef}/proposals). A proposal the reader may
   * not see comes back masked; an affected object they may not see keeps its place with its mask (ADR-2471).
   */
  listProposals: lazyKernelAction(loadRouteTransport, "executeListProposals"),

  /** One proposal with its plan, affected objects, requested capabilities, model, content hash and decision. */
  getProposal: lazyKernelAction(loadRouteTransport, "executeGetProposal"),

  /**
   * Rejects an open proposal with a reason (POST /api/v1/kernel/proposals/{proposalRef}/rejection). Rejects with an
   * ApiError of status 409 when the proposal is already decided or expired, 422 without a reason and 403 without
   * proposal.reject in its study.
   */
  rejectProposal: lazyKernelAction(loadRouteTransport, "executeRejectProposal"),
};
