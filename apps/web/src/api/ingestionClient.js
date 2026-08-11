import { apiClient } from "./apiClient";

export const ingestionClient = {
  uploadProtocol(file, options = {}) {
    const { changeReason = "Upload protocol document", ...rest } = options;
    const formData = new FormData();
    formData.append("file", file);
    return apiClient.post(`/api/v1/designer/ingestion/upload`, formData, {
      changeReason,
      ...rest,
    });
  },
  getJobStatus(jobId, options = {}) {
    const { changeReason = "Get job status" } = options;
    return apiClient.get(`/api/v1/designer/ingestion/jobs/${jobId}`, {
      changeReason,
    });
  },
  getCandidate(candidateId, options = {}) {
    const { changeReason = "Get candidate draft" } = options;
    return apiClient.get(
      `/api/v1/designer/ingestion/candidates/${candidateId}`,
      { changeReason }
    );
  },
  transitionItem(
    candidateId,
    itemId,
    status,
    reason,
    updatedFields = {},
    options = {}
  ) {
    const { changeReason = "Review candidate item" } = options;
    return apiClient.post(
      `/api/v1/designer/ingestion/candidates/${candidateId}/items/${itemId}/transition`,
      { status, reason, ...updatedFields },
      { changeReason }
    );
  },
  promoteCandidate(candidateId, changeReason) {
    return apiClient.post(
      `/api/v1/designer/ingestion/candidates/${candidateId}/promote`,
      { change_reason: changeReason },
      { changeReason }
    );
  },
};
