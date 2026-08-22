import { apiClient } from "./apiClient";
import type { components } from "./types";

export type SubjectCreate = components["schemas"]["Execution_SubjectCreate"];
export type SubjectResponse =
  components["schemas"]["Execution_SubjectResponse"];
export type SubjectScreeningRequest =
  components["schemas"]["Execution_SubjectScreeningRequest"];
export type SubjectScreeningResponse =
  components["schemas"]["Execution_SubjectScreeningResponse"];

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
   * Evaluates subject screening eligibility against protocol criteria.
   */
  screenSubject(
    subjectId: string,
    body: SubjectScreeningRequest | null = null,
    options: any = {}
  ): Promise<SubjectScreeningResponse> {
    return apiClient.post(
      `/api/v1/execution/subjects/${subjectId}/screening`,
      body,
      options
    );
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
   * Lists staged CANDIDATE clinical queries flagged by the anomaly detector.
   */
  getAnomalyCandidates(params: any = {}, options: any = {}): Promise<any> {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.subject_id) query.append("subject_id", params.subject_id);
    if (params.domain) query.append("domain", params.domain);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/execution/anomalies/candidates?${queryString}`
      : "/api/v1/execution/anomalies/candidates";
    return apiClient.get(path, options);
  },

  /**
   * Triggers on-demand cross-domain anomaly evaluation for a subject.
   */
  evaluateAnomalies(body: any, options: any = {}): Promise<any> {
    return apiClient.post(`/api/v1/execution/anomalies/evaluate`, body, options);
  },

  /**
   * Adjudicates a staged CANDIDATE query (APPROVE -> OPEN or REJECT -> CANCELLED).
   */
  adjudicateAnomalyCandidate(
    queryId: string,
    body: { action: "APPROVE" | "REJECT"; reason: string; updated_message?: string },
    options: any = {}
  ): Promise<any> {
    return apiClient.post(
      `/api/v1/execution/anomalies/candidates/${queryId}/adjudicate`,
      body,
      options
    );
  },
};
