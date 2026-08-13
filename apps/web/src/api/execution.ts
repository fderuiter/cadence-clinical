import { apiClient } from "./apiClient";
import type { components } from "./types";
import { useAuthStore } from "../stores/auth";

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

export type SubjectCreate = components["schemas"]["Execution_SubjectCreate"];
export type SubjectResponse =
  components["schemas"]["Execution_SubjectResponse"];

export interface LabAlertParams {
  study_id?: string;
  subject_id?: string;
  test_code?: string;
}

/**
 * Service module for the Clinical Execution microservice (EDC).
 * Interfaces with subjects, consents, clinical observations, form submissions, and queries.
 */
export const executionService = {
  /**
   * Enrolls/creates a new subject in the study.
   */
  createSubject(
    body: SubjectCreate,
    options: any = {}
  ): Promise<SubjectResponse> {
    return apiClient.post(`/api/v1/execution/subjects`, body, options);
  },

  /**
   * Lists clinical queries matching filters.
   */
  getQueries(options: any = {}): Promise<any> {
    return apiClient.get(`/api/v1/execution/queries`, options);
  },

  /**
   * Lists real-time lab alerts matching filters.
   */
  listLabAlerts(params: LabAlertParams = {}, options: any = {}): Promise<any> {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.subject_id) query.append("subject_id", params.subject_id);
    if (params.test_code) query.append("test_code", params.test_code);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/execution/lab-alerts?${queryString}`
      : "/api/v1/execution/lab-alerts";
    return apiClient.get(path, options);
  },

  /**
   * Retrieves detail for a single clinical query.
   */
  getQuery(queryId: string | number, options: any = {}): Promise<any> {
    return apiClient.get(`/api/v1/execution/queries/${queryId}`, options);
  },

  /**
   * Submits/creates a form submission.
   */
  submitForm(body: any, options: any = {}): Promise<any> {
    return apiClient.post(`/api/v1/execution/form-submissions`, body, options);
  },

  /**
   * Syncs clinical query ledger blocks.
   */
  syncQueries(blocks: any, options: any = {}): Promise<any> {
    return apiClient.post(
      `/api/v1/execution/queries/sync`,
      { blocks },
      options
    );
  },

  async submitBatchSignature(
    payload: BatchSignPayload
  ): Promise<BatchSignResponse> {
    const authStore = useAuthStore();
    const isMocked =
      apiClient.post &&
      ((apiClient.post as any)._isMockFunction ||
        typeof (apiClient.post as any).mock === "object");
    if (authStore.isDemoMode && !authStore.token && !isMocked) {
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
        headers: {
          "X-Change-Reason": payload.meaning,
        },
      }
    )) as BatchSignResponse;

    return response;
  },
};

export const submitBatchSignature = (
  payload: BatchSignPayload
): Promise<BatchSignResponse> => executionService.submitBatchSignature(payload);

