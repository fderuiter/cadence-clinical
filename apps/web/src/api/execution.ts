import { openApiClient, unwrapResponse } from "./client";
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
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/execution/subjects", {
        body,
        ...options,
      })
    );
  },

  /**
   * Evaluates subject screening eligibility against protocol criteria.
   */
  screenSubject(
    subjectId: string,
    body: SubjectScreeningRequest = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/execution/subjects/{subject_id}/screening", {
        params: { path: { subject_id: subjectId } },
        body: body ?? {},
        ...options,
      })
    );
  },

  /**
   * Lists clinical queries matching filters.
   */
  getQueries(options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/execution/queries", {
        ...options,
      })
    );
  },

  /**
   * Lists real-time lab alerts matching filters.
   */
  listLabAlerts(params: LabAlertParams = {}, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/execution/labs/toxicity/review-queue", {
        params: {
          query: {
            study_id: params.study_id,
            subject_id: params.subject_id,
          },
        },
        ...options,
      })
    );
  },

  /**
   * Retrieves detail for a single clinical query.
   */
  getQuery(queryId: string | number, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/execution/queries/{query_id}", {
        params: { path: { query_id: String(queryId) } },
        ...options,
      })
    );
  },

  /**
   * Submits/creates a form submission.
   */
  submitForm(body: Record<string, unknown>, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      (openApiClient as unknown as { POST: Function }).POST("/api/v1/execution/form-submissions", {
        body,
        ...options,
      })
    );
  },

  /**
   * Syncs clinical query ledger blocks.
   */
  syncQueries(blocks: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/execution/queries/sync", {
        body: { blocks } as never,
        ...options,
      })
    );
  },

  /**
   * Lists staged CANDIDATE clinical queries flagged by the anomaly detector.
   */
  getAnomalyCandidates(params: Record<string, string | undefined> = {}, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/execution/anomalies/candidates", {
        params: {
          query: {
            study_id: params.study_id,
            subject_id: params.subject_id,
            domain: params.domain,
          },
        },
        ...options,
      })
    );
  },

  /**
   * Triggers on-demand cross-domain anomaly evaluation for a subject.
   */
  evaluateAnomalies(body: Record<string, unknown>, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/execution/anomalies/evaluate", {
        body: body as never,
        ...options,
      })
    );
  },

  /**
   * Adjudicates a staged CANDIDATE query (APPROVE -> OPEN or REJECT -> CANCELLED).
   */
  adjudicateAnomalyCandidate(
    queryId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/execution/anomalies/candidates/{query_id}/adjudicate",
        {
          params: { path: { query_id: queryId } },
          body: body as never,
          ...options,
        }
      )
    );
  },
};
