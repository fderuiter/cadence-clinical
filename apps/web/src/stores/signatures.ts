import { defineStore } from "pinia";
import { apiClient } from "../api/apiClient";
import { isDemoBuild } from "../utils/demoMode.js";
import {
  BATCH_SIGN_OFF_ACTION,
  BATCH_SIGN_OFF_SEMANTIC_ACTION,
  executionBatchBindingId,
} from "../utils/signatureBinding";
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
    // Only the demo branch below persists a result, so only a demo build restores one (#4523).
    if (isDemoBuild() && typeof window !== "undefined" && window.localStorage) {
      try {
        const stored = window.localStorage.getItem("lastSignatureResult");
        if (stored) {
          savedLastSignatureResult = JSON.parse(stored);
        }
      } catch (e) {
        console.error(
          "Failed to parse lastSignatureResult from localStorage",
          e
        );
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
        if (
          isDemoBuild() &&
          authStore.isDemoMode &&
          !authStore.token &&
          !isMocked
        ) {
          await new Promise((resolve) => setTimeout(resolve, 300));
          const mockResponse: BatchSignResponse = {
            signature_id:
              "mock-sig-" +
              Date.now() +
              "-" +
              Math.random().toString(36).substring(2, 7),
            study_id: payload.studyId,
            subject_id: payload.subjectId,
            signed_forms_count: payload.formIds.length,
            content_digest:
              "sha256-" +
              Array.from({ length: 64 }, () =>
                Math.floor(Math.random() * 16).toString(16)
              ).join(""),
            timestamp_utc: new Date().toISOString(),
            audit_tx: "tx-" + Math.random().toString(36).substring(2, 10),
          };
          this.lastSignatureResult = mockResponse;
          if (typeof window !== "undefined" && window.localStorage) {
            try {
              window.localStorage.setItem(
                "lastSignatureResult",
                JSON.stringify(mockResponse)
              );
            } catch (e) {
              console.error("Failed to persist signature in demo mode", e);
            }
          }
          return mockResponse;
        }

        // The password only obtains a step-up token bound to these targets; it never reaches the service (ADR-2515).
        const targetType = payload.targetType || "FORM";
        const batchId = await executionBatchBindingId(
          payload.studyId,
          targetType,
          payload.formIds,
          payload.meaning
        );
        const verification = (await apiClient.post(
          "/api/v1/auth/signature-verification",
          {
            username: authStore.identity?.username || "",
            password: payload.password,
            action: BATCH_SIGN_OFF_ACTION,
            semantic_action: BATCH_SIGN_OFF_SEMANTIC_ACTION,
            batch_id: batchId,
          }
        )) as { sig_token?: string } | null;
        if (!verification?.sig_token) {
          throw Object.assign(new Error("REAUTHENTICATION_REQUIRED"), {
            status: 401,
          });
        }

        const response = (await apiClient.post(
          BATCH_SIGN_OFF_ACTION,
          {
            study_id: payload.studyId,
            target_type: targetType,
            target_ids: payload.formIds,
            signing_reason: payload.meaning,
          },
          {
            changeReason: payload.meaning,
            headers: { "X-Sig-Token": verification.sig_token },
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
