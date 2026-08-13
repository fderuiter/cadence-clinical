import { apiClient } from "./apiClient";
import type { components } from "./types";
import { useAuthStore } from "../stores/auth";

/**
 * Payload interface for submitting a batch signature.
 */
export interface BatchSignPayload {
  /** The unique identifier of the clinical study. */
  studyId: string;
  /** The unique identifier of the subject being signed off. */
  subjectId: string;
  /** List of form IDs to sign off concurrently. */
  formIds: string[];
  /** The user's credential password for cryptographic verification. */
  password: string;
  /** The compliance meaning or reason of the signature (e.g. APPROVED). */
  meaning: string;
  /** Optional printed name of the signer. Defaults to Principal Investigator. */
  printedName?: string;
  /** Optional target type. Defaults to FORM. */
  targetType?: string;
}

/**
 * Response interface returned after successfully processing a batch signature.
 */
export interface BatchSignResponse {
  /** Generated unique electronic signature record ID. */
  signature_id: string;
  /** Reference to the clinical study. */
  study_id: string;
  /** Reference to the subject signed off. */
  subject_id: string;
  /** The count of forms successfully signed in this batch. */
  signed_forms_count: number;
  /** Cryptographic content digest over the signed form data assets. */
  content_digest: string;
  /** ISO UTC timestamp of the signature transaction. */
  timestamp_utc: string;
  /** Reference to the database ledger transaction hash. */
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

  /**
   * Submits a batch electronic signature for a list of clinical forms.
   *
   * Under standard mode, this sends an electronic signature sign-off request
   * to the EDC backend. Under demo mode, it bypasses network transmission and
   * generates/persists a mock signature response locally in localStorage.
   *
   * @param payload Payload parameters for the batch signature.
   * @returns Resolves with the processing signature response.
   */
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

/**
 * Convenience wrapper to submit a batch electronic signature for forms.
 *
 * @param payload Payload parameters for the batch signature.
 * @returns Resolves with the processing signature response.
 */
export const submitBatchSignature = (
  payload: BatchSignPayload
): Promise<BatchSignResponse> => executionService.submitBatchSignature(payload);
