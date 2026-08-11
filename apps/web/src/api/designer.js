import { apiClient } from "./apiClient";

export const designerService = {
  getStudy(studyId, options = {}) {
    return apiClient.get(`/api/v1/studies/${studyId}`, options);
  },
  createStudyVersion(studyId, body, options = {}) {
    return apiClient.post(`/api/v1/studies/${studyId}/versions`, body, options);
  },
  getRules(studyId, options = {}) {
    return apiClient.get(`/api/v1/studies/${studyId}/rules`, options);
  },
  createRule(studyId, rule, options = {}) {
    return apiClient.post(`/api/v1/studies/${studyId}/rules`, rule, options);
  },
  getConcepts(options = {}) {
    return apiClient.get(`/api/v1/mdr/concepts`, options);
  },
};
