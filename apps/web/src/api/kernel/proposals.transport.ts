import { unwrapResponse } from "../client";
import type {
  KernelProposal,
  KernelProposalRejection,
  KernelProposalState,
  KernelProposalStatus,
} from "./proposals";
import { kernelClient, mutationHeaders, unversioned } from "./shared";

export function executeListProposals(
  studyRef: string,
  query: { status?: KernelProposalStatus; limit?: number } = {}
): Promise<KernelProposal[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/proposals", {
      params: {
        path: { studyRef },
        query: { status: query.status, limit: query.limit },
      },
    })
  );
}

export function executeGetProposal(
  proposalRef: string
): Promise<KernelProposal> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/proposals/{proposalRef}", {
      params: { path: { proposalRef: unversioned(proposalRef) } },
    })
  );
}

export function executeRejectProposal({
  proposalRef,
  reason,
  idempotencyKey,
}: KernelProposalRejection): Promise<KernelProposalState> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/proposals/{proposalRef}/rejection", {
      params: { path: { proposalRef: unversioned(proposalRef) } },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}
