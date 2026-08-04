import { defineStore } from "pinia";
import { apiClient } from "../api/apiClient";
import { useAuthStore } from "./auth.js";

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
  state: () => {
    let savedLastSignatureResult = null;
    if (typeof window !== "undefined" && window.localStorage) {
      try {
        const stored = window.localStorage.getItem("lastSignatureResult");
        if (stored) {
          savedLastSignatureResult = JSON.parse(stored);
        }
      } catch (e) {
        console.error("Failed to parse lastSignatureResult from localStorage", e);
      }
    }
    return {
      selectedFormIds: [] as string[],
      isSigning: false,
      signatureError: null as string | null,
      lastSignatureResult: savedLastSignatureResult as BatchSignResponse | null,
    };
  },
  actions: {
    async submitBatchSignature(
      payload: BatchSignPayload
    ): Promise<BatchSignResponse> {
      this.isSigning = true;
      this.signatureError = null;
      try {
        const authStore = useAuthStore();
        const isMocked =
          apiClient.post &&
          ((apiClient.post as any)._isMockFunction ||
            typeof (apiClient.post as any).mock === "object");
        if (authStore.isDemoMode && !isMocked) {
          await new Promise((resolve) => setTimeout(resolve, 300));
          const mockResponse: BatchSignResponse = {
            signature_id: "mock-sig-" + Date.now() + "-" + Math.random().toString(36).substring(2, 7),
            study_id: payload.studyId,
            subject_id: payload.subjectId,
            signed_forms_count: payload.formIds.length,
            content_digest: "sha256-" + Array.from({length: 64}, () => Math.floor(Math.random()*16).toString(16)).join(""),
            timestamp_utc: new Date().toISOString(),
            audit_tx: "tx-" + Math.random().toString(36).substring(2, 10),
          };
          this.lastSignatureResult = mockResponse;
          if (typeof window !== "undefined" && window.localStorage) {
            try {
              window.localStorage.setItem("lastSignatureResult", JSON.stringify(mockResponse));
            } catch (e) {
              console.error("Failed to persist signature in demo mode", e);
            }
          }
          return mockResponse;
        }

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
    resetDemoStorage() {
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.removeItem("lastSignatureResult");
      }
      this.lastSignatureResult = null;
    },
  },
});
