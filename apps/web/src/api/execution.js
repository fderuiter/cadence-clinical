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
   * Lists clinical queries matching filters.
   */
  getQueries(options = {}) {
    return apiClient.get(`/api/v1/execution/queries`, options);
  },

  /**
   * Lists real-time lab alerts matching filters.
   */
  listLabAlerts(params = {}, options = {}) {
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
    return apiClient.post(
      `/api/v1/execution/queries/sync`,
      { blocks },
      options
    );
  },
};
