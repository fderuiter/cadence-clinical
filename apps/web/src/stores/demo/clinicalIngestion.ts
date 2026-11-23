import { ingestionClient } from "../../api/ingestionClient.js";
import type { CandidateDraft, ClinicalState } from "../clinicalScope";

export function createClinicalIngestionState(): Pick<
  ClinicalState,
  "candidateDraft" | "ingestionJobs" | "ingestionLoading" | "ingestionError"
> {
  return {
    candidateDraft: null,
    ingestionJobs: [],
    ingestionLoading: false,
    ingestionError: null,
  };
}

export function createClinicalIngestionActions() {
  return {
    async uploadProtocolDocument(
      this: ClinicalState,
      file: File,
      changeReason: string
    ) {
      const draft = (await ingestionClient.uploadProtocol(file, {
        changeReason,
      })) as CandidateDraft;
      this.candidateDraft = draft;
      this.ingestionJobs.push({
        job_id: draft.id,
        status: "COMPLETED",
        candidate_id: draft.id,
        errors: null,
      });
      return draft;
    },

    async fetchCandidateDraft(this: ClinicalState, candidateId: string) {
      this.ingestionLoading = true;
      this.ingestionError = null;
      try {
        const draft = (await ingestionClient.getCandidate(
          candidateId
        )) as CandidateDraft;
        this.candidateDraft = draft;
        this.ingestionLoading = false;
        return draft;
      } catch (err: unknown) {
        const error = err as Error;
        this.ingestionError = error.message;
        this.ingestionLoading = false;
        throw err;
      }
    },

    async transitionCandidateItemState(
      this: ClinicalState,
      candidateId: string,
      itemId: string,
      status: string,
      reason: string,
      updatedFields: Record<string, unknown> = {}
    ) {
      this.ingestionLoading = true;
      this.ingestionError = null;
      try {
        const draft = (await ingestionClient.transitionItem(
          candidateId,
          itemId,
          status,
          reason,
          updatedFields
        )) as CandidateDraft;
        this.candidateDraft = draft;
        this.ingestionLoading = false;
        return draft;
      } catch (err: unknown) {
        const error = err as Error;
        this.ingestionError = error.message;
        this.ingestionLoading = false;
        throw err;
      }
    },

    async promoteCandidateDraft(
      this: ClinicalState,
      candidateId: string,
      changeReason: string
    ) {
      this.ingestionLoading = true;
      this.ingestionError = null;
      try {
        const res = await ingestionClient.promoteCandidate(
          candidateId,
          changeReason
        );
        if (this.candidateDraft && this.candidateDraft.id === candidateId) {
          this.candidateDraft.status = "PROMOTED";
        }
        this.ingestionLoading = false;
        return res;
      } catch (err: unknown) {
        const error = err as Error;
        this.ingestionError = error.message;
        this.ingestionLoading = false;
        throw err;
      }
    },
  };
}
