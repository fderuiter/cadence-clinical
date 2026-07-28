import { apiClient } from "./apiClient";

/**
 * Service module for the FHIR / ePRO Interoperability Gateway microservice.
 * Interfaces with ePRO submissions, offline sync reconciliation, and instrument metadata.
 */
export const interopService = {
  /**
   * Submits a completed ePRO/eCOA questionnaire.
   */
  submitEpro(body, options = {}) {
    return apiClient.post(`/api/v1/interop/epro/submit`, body, options);
  },

  /**
   * Synchronizes queued offline ePRO transactions with the server.
   */
  syncEpro(body, options = {}) {
    return apiClient.post(`/api/v1/interop/epro/sync`, body, options);
  },

  /**
   * Fetches active assigned instruments for a given subject.
   */
  getInstruments(subjectId, options = {}) {
    return apiClient.get(
      `/api/v1/interop/subjects/${subjectId}/instruments`,
      options
    );
  },
};
