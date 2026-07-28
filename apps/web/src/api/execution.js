import { apiClient } from "./apiClient";

/**
 * Service module for the Clinical Execution microservice (EDC).
 * Interfaces with subjects, consents, clinical observations, form submissions, and queries.
 */
export const executionService = {
  /**
   * Enrolls/creates a new subject in the study.
   */
  createSubject(body, options = {}) {
    return apiClient.post(`/api/v1/execution/subjects`, body, options);
  },

  /**
   * Submits consent record for a subject.
   */
  consentSubject(subjectId, body, options = {}) {
    return apiClient.post(
      `/api/v1/execution/subjects/${subjectId}/consent`,
      body,
      options
    );
  },

  /**
   * Lists clinical queries matching filters.
   */
  getQueries(options = {}) {
    return apiClient.get(`/api/v1/execution/queries`, options);
  },

  /**
   * Retrieves detail for a single clinical query.
   */
  getQuery(queryId, options = {}) {
    return apiClient.get(`/api/v1/execution/queries/${queryId}`, options);
  },

  /**
   * Submits/creates a form submission.
   */
  submitForm(body, options = {}) {
    return apiClient.post(`/api/v1/execution/form-submissions`, body, options);
  },

  /**
   * Syncs clinical query ledger blocks.
   */
  syncQueries(blocks, options = {}) {
    return apiClient.post(`/api/v1/execution/queries/sync`, { blocks }, options);
  },
};
