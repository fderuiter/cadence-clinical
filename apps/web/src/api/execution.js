import { apiClient } from "./apiClient";

export const executionService = {
  createSubject(body, options = {}) {
    return apiClient.post(`/api/v1/execution/subjects`, body, options);
  },
  getQueries(options = {}) {
    return apiClient.get(`/api/v1/execution/queries`, options);
  },
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
  getQuery(queryId, options = {}) {
    return apiClient.get(`/api/v1/execution/queries/${queryId}`, options);
  },
  submitForm(body, options = {}) {
    return apiClient.post(`/api/v1/execution/form-submissions`, body, options);
  },
  syncQueries(blocks, options = {}) {
    return apiClient.post(`/api/v1/execution/queries/sync`, { blocks }, options);
  },
};
