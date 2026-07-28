import { apiClient } from "./apiClient";

/**
 * Service module for the Designer microservice (MDR/SDR).
 * Interfaces with study design, CDISC USDM modeling, and metadata rule authoring.
 */
export const designerService = {
  /**
   * Fetches the details of a study design.
   */
  getStudy(studyId, options = {}) {
    return apiClient.get(`/api/v1/studies/${studyId}`, options);
  },

  /**
   * Creates a new study version.
   */
  createStudyVersion(studyId, body, options = {}) {
    return apiClient.post(`/api/v1/studies/${studyId}/versions`, body, options);
  },

  /**
   * Fetches active metadata rules for a study.
   */
  getRules(studyId, options = {}) {
    return apiClient.get(`/api/v1/studies/${studyId}/rules`, options);
  },

  /**
   * Creates/adds an authoring metadata rule for a study.
   */
  createRule(studyId, rule, options = {}) {
    return apiClient.post(`/api/v1/studies/${studyId}/rules`, rule, options);
  },

  /**
   * Fetches list of concepts from the MDR global library.
   */
  getConcepts(options = {}) {
    return apiClient.get(`/api/v1/mdr/concepts`, options);
  },
};
