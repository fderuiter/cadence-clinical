import { defineStore } from "pinia";
import { apiClient } from "../api/apiClient";

export interface BatchSignPayload {
  studyId: string;
  subjectId: string;
  formIds: string[];
  password: string;
  meaning: string;
  printedName?: string;
  targetType?: string;
}

export interface BatchSignResponse {
  signature_id: string;
  study_id: string;
  subject_id: string;
  signed_forms_count: number;
  content_digest: string;
  timestamp_utc: string;
  audit_tx: string;
}

export const useSignatureStore = defineStore("signatures", {
  state: () => ({
    selectedFormIds: [] as string[],
    isSigning: false,
    signatureError: null as string | null,
    lastSignatureResult: null as BatchSignResponse | null,
  }),
  actions: {
    async submitBatchSignature(
      payload: BatchSignPayload
    ): Promise<BatchSignResponse> {
      this.isSigning = true;
      this.signatureError = null;
      try {
        const printedName = payload.printedName || "Principal Investigator";
        const requestPayload = {
          study_id: payload.studyId,
          subject_id: payload.subjectId,
          target_type: payload.targetType || "FORM",
          target_ids: payload.formIds,
          target_form_ids: payload.formIds,
          signing_reason: payload.meaning,
          password: payload.password,
          printed_name: printedName,
        };

        const response = (await apiClient.post(
          "/api/v1/execution/signatures/batch-sign-off",
          requestPayload,
          {
            changeReason: payload.meaning,
          }
        )) as BatchSignResponse;

        this.lastSignatureResult = response;
        return response;
      } catch (err: any) {
        this.signatureError = err.message || "Failed to submit batch signature";
        throw err;
      } finally {
        this.isSigning = false;
      }
    },
  },
});
