import { apiClient } from "./apiClient";

export const interopService = {
  submitEpro(body, options = {}) {
    return apiClient.post(`/api/v1/interop/epro/submit`, body, options);
  },
  syncEpro(body, options = {}) {
    return apiClient.post(`/api/v1/interop/epro/sync`, body, options);
  },
  getInstruments(subjectId, options = {}) {
    return apiClient.get(`/api/v1/interop/subjects/${subjectId}/instruments`, options);
  },
};
